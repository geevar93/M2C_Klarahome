using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Identity.Domain;

/// <summary>
/// A person who can sign in: a shopper, a seller's staff member, or platform staff
/// (docs/03-database-design.md §4.2).
/// </summary>
/// <remarks>
/// <para>
/// One table for all three actor classes rather than three, because the things that differ
/// between them — which credential is primary, whether TOTP is mandatory, what they may do — are
/// policy and roles, not shape. Three tables would mean three login paths, three lockout
/// implementations and three places to fix the next authentication bug.
/// </para>
/// <para>
/// Soft-deleted rather than removed (docs/03-database-design.md §1): an order, an audit entry and
/// a review all point at a user id, and a deleted account must not turn those into dangling
/// references. DPDP erasure anonymises the row; it does not drop it.
/// </para>
/// </remarks>
internal sealed class User : AggregateRoot<Guid>, ITenantScoped, IAuditable, ISoftDeletable
{
    private readonly List<UserRole> _roles = [];

    private User(Guid id, UserType userType, string? mobile, string? email)
        : base(id)
    {
        UserType = userType;
        Mobile = mobile;
        Email = email;
        Status = UserStatus.Active;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private User()
    {
    }

    /// <summary>Which surface this user belongs to, and therefore which login rules apply.</summary>
    public UserType UserType { get; private set; }

    /// <summary>Mobile number in E.164, unique within the tenant. The customer's primary identifier.</summary>
    public string? Mobile { get; private set; }

    /// <summary>Email address, lowercased, unique within the tenant. Primary for staff and vendors.</summary>
    public string? Email { get; private set; }

    /// <summary>
    /// The Argon2id PHC string, or null. Null is a real state, not a missing value: a customer who
    /// only ever signs in with an OTP has no password to store.
    /// </summary>
    public string? PasswordHash { get; private set; }

    /// <summary>When the mobile number was proved. Null means unverified.</summary>
    public DateTimeOffset? MobileVerifiedAt { get; private set; }

    /// <summary>When the email address was proved. Null means unverified.</summary>
    public DateTimeOffset? EmailVerifiedAt { get; private set; }

    /// <summary>Whether the account may sign in at all.</summary>
    public UserStatus Status { get; private set; }

    /// <summary>Consecutive failed sign-in attempts. Reset by any success.</summary>
    public int FailedAttempts { get; private set; }

    /// <summary>When the progressive lockout expires, or null when the account is not locked.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>The TOTP shared secret, encrypted at rest. Never leaves the server after enrolment.</summary>
    public string? TotpSecretEncrypted { get; private set; }

    /// <summary>Whether a second factor is enrolled and being demanded at sign-in.</summary>
    public bool TotpEnabled { get; private set; }

    /// <summary>When this user last signed in successfully.</summary>
    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>The roles this user holds, each optionally scoped to one vendor.</summary>
    public IReadOnlyList<UserRole> Roles => _roles;

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <inheritdoc />
    public Guid? DeletedBy { get; private set; }

    /// <summary>
    /// Registers a shopper. At least one identifier is required: the mobile number for the OTP
    /// path, the email address for the password path, or both.
    /// </summary>
    /// <param name="mobile">The mobile number in E.164, or null.</param>
    /// <param name="email">The email address, lowercased, or null.</param>
    /// <exception cref="ArgumentException">Neither identifier was supplied.</exception>
    public static User RegisterCustomer(string? mobile, string? email = null)
    {
        if (string.IsNullOrWhiteSpace(mobile) && string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException(
                "A shopper needs a mobile number or an email address; an account with neither could "
                + "never sign in and could never be found.",
                nameof(mobile));
        }

        return new User(UuidV7.New(), UserType.Customer, mobile, email);
    }

    /// <summary>Registers a vendor or platform staff user, who sign in with an email address.</summary>
    /// <param name="userType">Vendor staff or platform staff.</param>
    /// <param name="email">The email address; lowercased by the caller's validator.</param>
    /// <param name="mobile">An optional mobile number, for notifications and recovery.</param>
    public static User RegisterStaff(UserType userType, string email, string? mobile = null)
    {
        if (userType == UserType.Customer)
        {
            throw new ArgumentException("Use RegisterCustomer for a shopper.", nameof(userType));
        }

        return new User(UuidV7.New(), userType, mobile, Guard.NotNullOrWhiteSpace(email));
    }

    /// <summary>Sets or replaces the password hash. The hashing itself is an infrastructure concern.</summary>
    /// <param name="passwordHash">An Argon2id PHC string.</param>
    public void SetPasswordHash(string passwordHash)
        => PasswordHash = Guard.NotNullOrWhiteSpace(passwordHash);

    /// <summary>Attaches or replaces the email address. Verification starts again from zero.</summary>
    /// <param name="email">The new address, lowercased.</param>
    public void SetEmail(string? email)
    {
        if (string.Equals(Email, email, StringComparison.Ordinal))
        {
            return;
        }

        Email = email;
        EmailVerifiedAt = null;
    }

    /// <summary>Attaches or replaces the mobile number. Verification starts again from zero.</summary>
    /// <param name="mobile">The new number in E.164.</param>
    public void SetMobile(string? mobile)
    {
        if (string.Equals(Mobile, mobile, StringComparison.Ordinal))
        {
            return;
        }

        Mobile = mobile;
        MobileVerifiedAt = null;
    }

    /// <summary>Records that the mobile number has been proved by an OTP.</summary>
    /// <param name="at">When it was proved.</param>
    public void MarkMobileVerified(DateTimeOffset at) => MobileVerifiedAt = at;

    /// <summary>Records that the email address has been proved by a verification link.</summary>
    /// <param name="at">When it was proved.</param>
    public void MarkEmailVerified(DateTimeOffset at) => EmailVerifiedAt = at;

    /// <summary>Stores the encrypted TOTP secret without yet demanding it at sign-in.</summary>
    /// <remarks>
    /// Enrolment is two steps on purpose. A secret that took effect the moment it was generated
    /// would lock out anyone whose authenticator app failed to save it, and mandatory 2FA makes
    /// that unrecoverable without an administrator.
    /// </remarks>
    /// <param name="secretEncrypted">The encrypted shared secret.</param>
    public void StageTotpSecret(string secretEncrypted)
    {
        TotpSecretEncrypted = Guard.NotNullOrWhiteSpace(secretEncrypted);
        TotpEnabled = false;
    }

    /// <summary>Turns the second factor on, after a code from the staged secret has been verified.</summary>
    public void EnableTotp()
    {
        if (string.IsNullOrEmpty(TotpSecretEncrypted))
        {
            throw new InvalidOperationException("No TOTP secret has been staged for this user.");
        }

        TotpEnabled = true;
    }

    /// <summary>Removes the second factor and its secret.</summary>
    public void DisableTotp()
    {
        TotpEnabled = false;
        TotpSecretEncrypted = null;
    }

    /// <summary>Records a successful sign-in, clearing the failure counter and any lockout.</summary>
    /// <param name="at">When the sign-in happened.</param>
    public void RecordLoginSucceeded(DateTimeOffset at)
    {
        FailedAttempts = 0;
        LockedUntil = null;
        LastLoginAt = at;
    }

    /// <summary>
    /// Records a failed sign-in and applies progressive lockout
    /// (docs/07-security-compliance.md §1).
    /// </summary>
    /// <remarks>
    /// The delay doubles with each failure past the threshold and is capped, rather than locking
    /// the account outright: an attacker who can lock any account by guessing at it has a denial
    /// of service, which is the failure mode a permanent lockout trades a brute-force risk for.
    /// </remarks>
    /// <param name="at">When the attempt failed.</param>
    /// <param name="threshold">Failures tolerated before the first lockout.</param>
    /// <param name="baseLockout">The first lockout duration.</param>
    /// <param name="maxLockout">The longest lockout ever applied.</param>
    public void RecordLoginFailed(
        DateTimeOffset at,
        int threshold,
        TimeSpan baseLockout,
        TimeSpan maxLockout)
    {
        FailedAttempts++;

        if (FailedAttempts < threshold)
        {
            return;
        }

        var doublings = Math.Min(FailedAttempts - threshold, 16);
        var delay = TimeSpan.FromTicks(baseLockout.Ticks * (1L << doublings));

        LockedUntil = at + (delay > maxLockout ? maxLockout : delay);
    }

    /// <summary>Whether sign-in is currently refused because of failed attempts.</summary>
    /// <param name="now">The current instant.</param>
    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is not null && LockedUntil > now;

    /// <summary>Clears a lockout, as an administrator does on request.</summary>
    public void Unlock()
    {
        FailedAttempts = 0;
        LockedUntil = null;
    }

    /// <summary>Moves the account to a new status.</summary>
    /// <param name="status">The new status.</param>
    public void ChangeStatus(UserStatus status) => Status = status;

    /// <summary>Grants a role, optionally scoped to one vendor. Granting it twice is a no-op.</summary>
    /// <param name="roleId">The role to grant.</param>
    /// <param name="vendorId">The vendor the grant is scoped to, or null for a platform-wide grant.</param>
    public void GrantRole(Guid roleId, Guid? vendorId = null)
    {
        if (_roles.Exists(role => role.RoleId == roleId && role.VendorId == vendorId))
        {
            return;
        }

        _roles.Add(UserRole.Create(Id, roleId, vendorId));
    }

    /// <summary>Revokes a role grant.</summary>
    /// <param name="roleId">The role to revoke.</param>
    /// <param name="vendorId">The vendor scope of the grant being revoked.</param>
    public void RevokeRole(Guid roleId, Guid? vendorId = null)
        => _roles.RemoveAll(role => role.RoleId == roleId && role.VendorId == vendorId);

    /// <summary>Retires the account. History that points at it stays valid.</summary>
    /// <param name="at">When it was retired.</param>
    public void Delete(DateTimeOffset at)
    {
        DeletedAt = at;
        Status = UserStatus.Disabled;
    }
}

/// <summary>Which surface a user belongs to.</summary>
internal enum UserType
{
    /// <summary>A shopper on the storefront.</summary>
    Customer = 0,

    /// <summary>A seller's staff member, confined to that seller's data.</summary>
    Vendor = 1,

    /// <summary>Platform staff: operations, finance, catalog, support, administrators.</summary>
    Staff = 2,
}

/// <summary>Whether an account may sign in.</summary>
internal enum UserStatus
{
    /// <summary>Normal.</summary>
    Active = 0,

    /// <summary>Temporarily barred by an administrator. Recoverable.</summary>
    Suspended = 1,

    /// <summary>Permanently barred, or retired.</summary>
    Disabled = 2,
}
