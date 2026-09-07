using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Identity.Domain;

/// <summary>
/// One signed-in device (docs/07-security-compliance.md §1). Every access token names the session
/// it belongs to, and every refresh token hangs off one, so revoking a session ends both.
/// </summary>
/// <remarks>
/// The session exists as a row of its own rather than being inferred from the refresh-token chain
/// because a user needs to be shown "Chrome on Windows, Mumbai, active 2 minutes ago" and be able
/// to end it. A chain of rotated token hashes is not something anyone can read.
/// </remarks>
internal sealed class UserSession : AggregateRoot<Guid>, ITenantScoped
{
    private UserSession(
        Guid id,
        Guid userId,
        string? device,
        string? ipAddress,
        DateTimeOffset startedAt,
        Guid? impersonatedByUserId,
        string? impersonationReason,
        DateTimeOffset? impersonationExpiresAt)
        : base(id)
    {
        UserId = userId;
        Device = device;
        IpAddress = ipAddress;
        StartedAt = startedAt;
        LastSeenAt = startedAt;
        ImpersonatedByUserId = impersonatedByUserId;
        ImpersonationReason = impersonationReason;
        ImpersonationExpiresAt = impersonationExpiresAt;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private UserSession()
    {
    }

    /// <summary>The signed-in user.</summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// A short human-readable description of the device, derived from the user agent. Not the raw
    /// header: it is shown to the user, and the header is neither readable nor trustworthy.
    /// </summary>
    public string? Device { get; private set; }

    /// <summary>The address the session started from. Personal data; masked in logs and lists.</summary>
    public string? IpAddress { get; private set; }

    /// <summary>When the session began.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>When a token in this session was last exchanged.</summary>
    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>When the session was ended, or null while it is live.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Why the session ended, for the security timeline.</summary>
    public SessionEndReason? RevokedReason { get; private set; }

    /// <summary>
    /// The support user acting as this session's owner, or null for an ordinary sign-in
    /// (docs/07-security-compliance.md §2).
    /// </summary>
    /// <remarks>
    /// The marker lives on the session rather than only in the token because "visibly flagged in
    /// the session" has to survive the token: the audit trail, the device list and the exit control
    /// all ask the same question, and a claim that expires in thirty minutes cannot answer it.
    /// </remarks>
    public Guid? ImpersonatedByUserId { get; private set; }

    /// <summary>Why support needed to act as this user. Required, and recorded verbatim.</summary>
    public string? ImpersonationReason { get; private set; }

    /// <summary>
    /// When an impersonated session stops being honoured whatever else happens. Null for an
    /// ordinary session, which ends when its refresh token does.
    /// </summary>
    public DateTimeOffset? ImpersonationExpiresAt { get; private set; }

    /// <summary>Whether somebody is acting as this session's owner rather than being them.</summary>
    public bool IsImpersonated => ImpersonatedByUserId is not null;

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Whether tokens issued in this session are still honoured.</summary>
    public bool IsActive => RevokedAt is null;

    /// <summary>Starts a session for a device.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="device">A readable device description.</param>
    /// <param name="ipAddress">The originating address.</param>
    /// <param name="startedAt">When the sign-in happened.</param>
    public static UserSession Start(Guid userId, string? device, string? ipAddress, DateTimeOffset startedAt)
        => new(UuidV7.New(), userId, device, ipAddress, startedAt, null, null, null);

    /// <summary>Starts a support session that acts as a user rather than being them.</summary>
    /// <remarks>
    /// A session of its own, not a flag on the operator's: the impersonated session has to be
    /// endable, listable and expirable without touching the sign-in the operator will return to.
    /// </remarks>
    /// <param name="userId">The user being acted as.</param>
    /// <param name="impersonatedByUserId">The support user doing the acting.</param>
    /// <param name="reason">Why, recorded verbatim for the audit trail.</param>
    /// <param name="device">A readable device description.</param>
    /// <param name="ipAddress">The originating address.</param>
    /// <param name="startedAt">When it began.</param>
    /// <param name="expiresAt">When it stops being honoured, whatever else happens.</param>
    public static UserSession StartImpersonation(
        Guid userId,
        Guid impersonatedByUserId,
        string reason,
        string? device,
        string? ipAddress,
        DateTimeOffset startedAt,
        DateTimeOffset expiresAt)
        => new(
            UuidV7.New(),
            userId,
            device,
            ipAddress,
            startedAt,
            impersonatedByUserId,
            Guard.NotNullOrWhiteSpace(reason),
            expiresAt);

    /// <summary>Records that the session was used, so the user can see which device is active.</summary>
    /// <param name="at">The current instant.</param>
    public void Touch(DateTimeOffset at) => LastSeenAt = at;

    /// <summary>Ends the session. Ending it twice keeps the first reason and instant.</summary>
    /// <param name="at">When it ended.</param>
    /// <param name="reason">Why.</param>
    public void Revoke(DateTimeOffset at, SessionEndReason reason)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = at;
        RevokedReason = reason;
    }
}

/// <summary>Why a session stopped being honoured.</summary>
internal enum SessionEndReason
{
    /// <summary>The user signed out on this device.</summary>
    SignedOut = 0,

    /// <summary>The user signed out everywhere, or an administrator ended their sessions.</summary>
    SignedOutEverywhere = 1,

    /// <summary>A rotated refresh token was presented again — the chain is treated as stolen.</summary>
    TokenReuseDetected = 2,

    /// <summary>The password changed, so every other device must sign in again.</summary>
    CredentialChanged = 3,

    /// <summary>The account was suspended or retired.</summary>
    AccountClosed = 4,

    /// <summary>A support impersonation was ended, by its operator or by its own clock.</summary>
    ImpersonationEnded = 5,
}

/// <summary>
/// One opaque refresh token, stored only as a hash (docs/07-security-compliance.md §1).
/// </summary>
/// <remarks>
/// <para>
/// Rotated on every use: presenting a token issues a new one and marks this row as replaced.
/// Presenting a token that has already been replaced means two parties hold the same secret, so
/// the entire session is revoked rather than the single token — that is the whole reason the
/// superseded rows are kept instead of deleted.
/// </para>
/// <para>
/// Hashed with SHA-256 rather than Argon2id, deliberately. The token is 256 bits of cryptographic
/// randomness, so there is no dictionary to attack and nothing for a memory-hard KDF to slow down;
/// what it needs is a fast, constant-time lookup on every refresh.
/// </para>
/// </remarks>
internal sealed class RefreshToken : Entity<Guid>, ITenantScoped
{
    private RefreshToken(Guid id, Guid sessionId, Guid userId, string tokenHash, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
        : base(id)
    {
        SessionId = sessionId;
        UserId = userId;
        TokenHash = Guard.NotNullOrWhiteSpace(tokenHash);
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private RefreshToken() => TokenHash = string.Empty;

    /// <summary>The session this token belongs to.</summary>
    public Guid SessionId { get; private set; }

    /// <summary>The user, denormalised so a revocation sweep needs no join.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Base64 of the SHA-256 of the token. The token itself is never stored.</summary>
    public string TokenHash { get; private set; }

    /// <summary>When this token was issued.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>When it stops being accepted.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When it was used or revoked, or null while it is live.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>The token issued in its place, so the rotation chain can be walked.</summary>
    public Guid? ReplacedById { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Whether this token may still be exchanged.</summary>
    /// <param name="now">The current instant.</param>
    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>Issues a token.</summary>
    /// <param name="sessionId">The owning session.</param>
    /// <param name="userId">The owning user.</param>
    /// <param name="tokenHash">The hash of the secret handed to the client.</param>
    /// <param name="issuedAt">Now.</param>
    /// <param name="expiresAt">When it expires.</param>
    public static RefreshToken Issue(
        Guid sessionId,
        Guid userId,
        string tokenHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
        => new(UuidV7.New(), sessionId, userId, tokenHash, issuedAt, expiresAt);

    /// <summary>Marks this token spent and records what replaced it.</summary>
    /// <param name="at">When the exchange happened.</param>
    /// <param name="replacementId">The id of the token issued in its place.</param>
    public void Rotate(DateTimeOffset at, Guid replacementId)
    {
        RevokedAt = at;
        ReplacedById = replacementId;
    }

    /// <summary>Revokes the token without a replacement.</summary>
    /// <param name="at">When it was revoked.</param>
    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;
}
