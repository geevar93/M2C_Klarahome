using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Identity.Domain;

/// <summary>
/// A link between an account here and an identity at an external provider
/// (docs/03-database-design.md §4.2, ADR-014).
/// </summary>
/// <remarks>
/// <para>
/// The link is keyed on <see cref="Subject"/> — the provider's own stable identifier — and on
/// nothing else. An email address is stored because it is useful when a support engineer is
/// looking at a row, and because it is what a first link is matched on; it is deliberately not
/// what the link is <em>found</em> by afterwards, since a person can change the email on their
/// Google account and would otherwise lose their orders.
/// </para>
/// <para>
/// One row per (provider, subject), and a user may hold several: signing in with Google and later
/// linking Facebook adds a row rather than replacing one.
/// </para>
/// </remarks>
internal sealed class ExternalLogin : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private ExternalLogin(
        Guid id,
        Guid userId,
        ExternalProvider provider,
        string subject,
        string? email,
        bool emailVerified,
        string? displayName,
        DateTimeOffset linkedAt)
        : base(id)
    {
        UserId = userId;
        Provider = provider;
        Subject = Guard.NotNullOrWhiteSpace(subject);
        Email = email;
        EmailVerified = emailVerified;
        DisplayName = displayName;
        LinkedAt = linkedAt;
        LastLoginAt = linkedAt;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ExternalLogin() => Subject = string.Empty;

    /// <summary>The account this identity signs in to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Which provider asserted it.</summary>
    public ExternalProvider Provider { get; private set; }

    /// <summary>
    /// The provider's stable identifier for this person — Google's <c>sub</c>. Unique per provider,
    /// never reassigned, and unchanged when they rename themselves or change their email.
    /// </summary>
    public string Subject { get; private set; }

    /// <summary>The address the provider asserted, as it was at the last sign-in.</summary>
    public string? Email { get; private set; }

    /// <summary>
    /// Whether the provider stated that it had verified that address. Only a verified address may
    /// link to a pre-existing account (docs/07-security-compliance.md §1).
    /// </summary>
    public bool EmailVerified { get; private set; }

    /// <summary>The name the provider supplied, for the "signed in with" row in the account page.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>When the link was made.</summary>
    public DateTimeOffset LinkedAt { get; private set; }

    /// <summary>When this identity was last used to sign in.</summary>
    public DateTimeOffset LastLoginAt { get; private set; }

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

    /// <summary>Links a provider identity to an account.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="provider">The provider.</param>
    /// <param name="subject">The provider's stable identifier.</param>
    /// <param name="email">The asserted address.</param>
    /// <param name="emailVerified">Whether the provider verified it.</param>
    /// <param name="displayName">The asserted name.</param>
    /// <param name="linkedAt">Now.</param>
    public static ExternalLogin Link(
        Guid userId,
        ExternalProvider provider,
        string subject,
        string? email,
        bool emailVerified,
        string? displayName,
        DateTimeOffset linkedAt)
        => new(UuidV7.New(), userId, provider, subject, email, emailVerified, displayName, linkedAt);

    /// <summary>
    /// Records a sign-in through this identity, refreshing what the provider now asserts.
    /// </summary>
    /// <remarks>
    /// The stored email follows the provider, because a support engineer looking at this row wants
    /// the address the person actually uses. It does not touch the account's own email: changing
    /// that is the account owner's decision, not their identity provider's.
    /// </remarks>
    /// <param name="email">The address the provider asserts now.</param>
    /// <param name="emailVerified">Whether it says it is verified.</param>
    /// <param name="displayName">The name it asserts now.</param>
    /// <param name="at">Now.</param>
    public void RecordSignIn(string? email, bool emailVerified, string? displayName, DateTimeOffset at)
    {
        Email = email;
        EmailVerified = emailVerified;
        DisplayName = displayName;
        LastLoginAt = at;
    }
}

/// <summary>An identity provider this platform accepts.</summary>
internal enum ExternalProvider
{
    /// <summary>Google, through OpenID Connect discovery. Wired and enabled.</summary>
    Google = 0,

    /// <summary>
    /// Facebook. Configured and disabled: it cannot leave development mode until the deployment's
    /// owner completes Business Verification and publishes a privacy policy (ADR-014).
    /// </summary>
    Facebook = 1,
}
