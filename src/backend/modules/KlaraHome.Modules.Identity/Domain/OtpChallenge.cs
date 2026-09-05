using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Identity.Domain;

/// <summary>
/// A one-time code or link token sent to a mobile number or an email address, and the record of
/// what was done with it (docs/03-database-design.md §4.2).
/// </summary>
/// <remarks>
/// <para>
/// One table for both, because the rules that matter are identical: hashed at rest, single use,
/// a small attempt budget, an expiry, and never returned in a response
/// (docs/07-security-compliance.md §1). Only the length of the secret differs — six digits a
/// person retypes, or 256 bits in a link nobody reads.
/// </para>
/// <para>
/// Rows are kept after they are consumed rather than deleted: the throttle counts recent requests
/// per destination, and a deleted row is a throttle that resets itself.
/// </para>
/// </remarks>
internal sealed class OtpChallenge : AggregateRoot<Guid>, ITenantScoped
{
    private OtpChallenge(
        Guid id,
        string destination,
        OtpChannel channel,
        OtpPurpose purpose,
        string codeHash,
        DateTimeOffset requestedAt,
        DateTimeOffset expiresAt,
        Guid? userId)
        : base(id)
    {
        Destination = Guard.NotNullOrWhiteSpace(destination);
        Channel = channel;
        Purpose = purpose;
        CodeHash = Guard.NotNullOrWhiteSpace(codeHash);
        RequestedAt = requestedAt;
        ExpiresAt = expiresAt;
        UserId = userId;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private OtpChallenge()
    {
        Destination = string.Empty;
        CodeHash = string.Empty;
    }

    /// <summary>Where the code was sent: an E.164 mobile number or an email address.</summary>
    public string Destination { get; private set; }

    /// <summary>How it was sent.</summary>
    public OtpChannel Channel { get; private set; }

    /// <summary>What presenting the code proves.</summary>
    public OtpPurpose Purpose { get; private set; }

    /// <summary>Base64 of the SHA-256 of the code, salted with the challenge id.</summary>
    public string CodeHash { get; private set; }

    /// <summary>The account this challenge belongs to, when it was known at request time.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>When the code was requested. The throttle windows are measured from here.</summary>
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>When the code stops being accepted.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Verification attempts made against this challenge.</summary>
    public int Attempts { get; private set; }

    /// <summary>When the code was successfully used, or null.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Issues a challenge.</summary>
    /// <param name="destination">The mobile number or email address.</param>
    /// <param name="channel">How it is delivered.</param>
    /// <param name="purpose">What it proves.</param>
    /// <param name="codeHash">The hash of the code or token.</param>
    /// <param name="requestedAt">Now.</param>
    /// <param name="lifetime">How long the code is valid for.</param>
    /// <param name="userId">The account it belongs to, when known.</param>
    public static OtpChallenge Issue(
        string destination,
        OtpChannel channel,
        OtpPurpose purpose,
        string codeHash,
        DateTimeOffset requestedAt,
        TimeSpan lifetime,
        Guid? userId = null)
        => new(UuidV7.New(), destination, channel, purpose, codeHash, requestedAt, requestedAt + lifetime, userId);

    /// <summary>Whether this challenge can still be verified against.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="maxAttempts">The attempt budget.</param>
    public bool IsOpen(DateTimeOffset now, int maxAttempts)
        => ConsumedAt is null && ExpiresAt > now && Attempts < maxAttempts;

    /// <summary>Records a failed verification, spending one of the attempt budget.</summary>
    public void RecordFailedAttempt() => Attempts++;

    /// <summary>Marks the challenge used. A code is accepted exactly once.</summary>
    /// <param name="at">When it was used.</param>
    public void Consume(DateTimeOffset at)
    {
        Attempts++;
        ConsumedAt = at;
    }

    /// <summary>Burns the challenge without accepting it, when a newer one supersedes it.</summary>
    /// <param name="at">When it was superseded.</param>
    public void Supersede(DateTimeOffset at) => ConsumedAt ??= at;
}

/// <summary>How a one-time code reaches its destination.</summary>
internal enum OtpChannel
{
    /// <summary>SMS to an Indian mobile number, over a DLT-registered template.</summary>
    Sms = 0,

    /// <summary>Email, for verification links and password resets.</summary>
    Email = 1,

    /// <summary>WhatsApp, where the customer has opted in.</summary>
    WhatsApp = 2,
}

/// <summary>What presenting a valid code proves.</summary>
internal enum OtpPurpose
{
    /// <summary>Signing in, and registering on first use of an unknown number.</summary>
    Login = 0,

    /// <summary>That a mobile number belongs to the person adding it.</summary>
    VerifyMobile = 1,

    /// <summary>That an email address belongs to the person adding it.</summary>
    VerifyEmail = 2,

    /// <summary>That the holder may set a new password without knowing the old one.</summary>
    PasswordReset = 3,
}
