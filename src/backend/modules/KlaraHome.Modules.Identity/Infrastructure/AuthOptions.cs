using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;

namespace KlaraHome.Modules.Identity.Infrastructure;

/// <summary>
/// Everything about authentication that a deployment may need to change without a build
/// (docs/07-security-compliance.md §1).
/// </summary>
/// <remarks>
/// Named <c>AuthOptions</c> rather than <c>IdentityOptions</c> on purpose: ASP.NET Core owns that
/// type name, and a reader who sees it will assume ASP.NET Core Identity is in play. It is not —
/// this module implements its own, because the framework's is built around a cookie-first,
/// UserManager-shaped world that does not fit mobile-OTP-primary, vendor-scoped, permission-based
/// authorisation.
/// </remarks>
internal sealed class AuthOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Auth";

    /// <summary>Access and refresh token issuance.</summary>
    public TokenOptions Tokens { get; set; } = new();

    /// <summary>Argon2id cost.</summary>
    public PasswordOptions Password { get; set; } = new();

    /// <summary>One-time code lifetime, length and throttling.</summary>
    public OtpOptions Otp { get; set; } = new();

    /// <summary>Failed-attempt lockout.</summary>
    public LockoutOptions Lockout { get; set; } = new();

    /// <summary>Keys protecting the TOTP secrets at rest.</summary>
    public EncryptionOptions Encryption { get; set; } = new();

    /// <summary>The first administrator, created once on an empty deployment.</summary>
    public BootstrapOptions Bootstrap { get; set; } = new();

    /// <summary>
    /// Roles for which a second factor is not optional (docs/07-security-compliance.md §1). A user
    /// holding any of these cannot complete a sign-in until they have enrolled one.
    /// </summary>
    public IList<string> MandatoryTwoFactorRoles { get; set; } =
        ["platform-admin", "vendor-owner"];
}

/// <summary>
/// The credential that creates the first platform administrator on an empty deployment.
/// </summary>
/// <remarks>
/// Both values must be supplied for the seeder to do anything, and it does nothing at all once any
/// administrator exists. There is deliberately no default: a product that ships with a known
/// administrator password is a product that is compromised the day it is deployed.
/// </remarks>
internal sealed class BootstrapOptions
{
    /// <summary>The first administrator's email address.</summary>
    public string? Email { get; set; }

    /// <summary>Their initial password. Supplied as a secret, used once, then changed by them.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// The example passwords that appear in the setup documentation and the compose file. A
    /// deployment that never changed one has not chosen a password, and outside Development the
    /// seeder refuses rather than creating an administrator anybody who read the README can log in as.
    /// </summary>
    private static readonly string[] WellKnown =
    [
        "change-me-please",
        "klarahome-dev-admin",
    ];

    /// <summary>Whether a password is one of the documented examples.</summary>
    /// <param name="password">The configured password.</param>
    public static bool IsWellKnown(string? password)
        => password is not null && WellKnown.Contains(password, StringComparer.Ordinal);
}

/// <summary>Access and refresh token issuance.</summary>
internal sealed class TokenOptions
{
    /// <summary>The <c>iss</c> claim, and what the validator requires.</summary>
    [Required]
    public string Issuer { get; set; } = "klarahome";

    /// <summary>The <c>aud</c> claim, and what the validator requires.</summary>
    [Required]
    public string Audience { get; set; } = "klarahome-api";

    /// <summary>Access token lifetime. Short by design: permissions are read from the token.</summary>
    [Range(1, 120)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh token lifetime, and therefore how long an idle session survives.</summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>How long a half-finished sign-in may wait at the 2FA prompt.</summary>
    [Range(1, 30)]
    public int TwoFactorChallengeMinutes { get; set; } = 5;

    /// <summary>The cookie the refresh token travels in.</summary>
    [Required]
    public string RefreshCookieName { get; set; } = "kh_rt";

    /// <summary>
    /// Whether the refresh cookie is marked <c>Secure</c>. True everywhere it matters; settable
    /// only so a developer on plain HTTP can sign in at all.
    /// </summary>
    public bool RefreshCookieSecure { get; set; } = true;

    /// <summary>
    /// RS256 signing keys as PEM private keys, newest first. More than one only during a
    /// rotation: the first signs, and every one of them validates, so tokens minted under the
    /// outgoing key keep working until they expire.
    /// </summary>
    public IList<SigningKeyOptions> SigningKeys { get; set; } = [];
}

/// <summary>One RSA key in the signing ring.</summary>
internal sealed class SigningKeyOptions
{
    /// <summary>The <c>kid</c> header, so a validator knows which key to try first.</summary>
    [Required]
    public string KeyId { get; set; } = string.Empty;

    /// <summary>The RSA private key, PEM-encoded (PKCS#8). Supplied as a Docker secret.</summary>
    [Required]
    public string PrivateKeyPem { get; set; } = string.Empty;
}

/// <summary>
/// Argon2id cost. The defaults are OWASP's first recommended configuration — 19 MiB, two passes,
/// one lane — which is the lowest of their settings and the one intended for a server that hashes
/// on the request path.
/// </summary>
internal sealed class PasswordOptions
{
    /// <summary>Memory cost in KiB.</summary>
    [Range(8192, 1_048_576)]
    public int MemoryKib { get; set; } = 19_456;

    /// <summary>Time cost, in passes over that memory.</summary>
    [Range(1, 16)]
    public int Iterations { get; set; } = 2;

    /// <summary>Lanes. One, matching the OWASP profile the memory cost is taken from.</summary>
    [Range(1, 16)]
    public int Parallelism { get; set; } = 1;

    /// <summary>Minimum length. Ten, and no composition rules (§1).</summary>
    [Range(8, 128)]
    public int MinimumLength { get; set; } = 10;
}

/// <summary>One-time code lifetime, length and throttling (docs/07-security-compliance.md §1).</summary>
internal sealed class OtpOptions
{
    /// <summary>Code length. Six digits, as the DLT templates are registered for.</summary>
    [Range(4, 10)]
    public int CodeLength { get; set; } = 6;

    /// <summary>How long a code stays valid.</summary>
    [Range(1, 30)]
    public int LifetimeMinutes { get; set; } = 5;

    /// <summary>Verification attempts allowed against one code before it is burnt.</summary>
    [Range(1, 10)]
    public int MaxVerificationAttempts { get; set; } = 5;

    /// <summary>Codes a single destination may request inside <see cref="ThrottleWindowMinutes"/>.</summary>
    [Range(1, 20)]
    public int MaxRequestsPerDestination { get; set; } = 3;

    /// <summary>The window that limit is measured over.</summary>
    [Range(1, 120)]
    public int ThrottleWindowMinutes { get; set; } = 10;

    /// <summary>How long a password-reset or email-verification link stays valid.</summary>
    [Range(5, 1440)]
    public int LinkLifetimeMinutes { get; set; } = 60;
}

/// <summary>Progressive lockout after consecutive failed sign-ins.</summary>
internal sealed class LockoutOptions
{
    /// <summary>Failures tolerated before the first lockout.</summary>
    [Range(1, 50)]
    public int Threshold { get; set; } = 5;

    /// <summary>The first lockout, doubling with each further failure.</summary>
    [Range(1, 3600)]
    public int BaseSeconds { get; set; } = 30;

    /// <summary>The cap. A permanent lock would hand an attacker a denial of service.</summary>
    [Range(60, 86_400)]
    public int MaximumSeconds { get; set; } = 3600;
}

/// <summary>The AES-256-GCM keys protecting secrets that must be readable again.</summary>
internal sealed class EncryptionOptions
{
    /// <summary>Which key new values are written under.</summary>
    public string CurrentKeyId { get; set; } = string.Empty;

    /// <summary>Every key this deployment can still read, by id. Base64 of 32 bytes.</summary>
    public IDictionary<string, string> Keys { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The current key's bytes.</summary>
    /// <exception cref="InvalidOperationException">No usable current key is configured.</exception>
    public byte[] CurrentKeyBytes()
        => TryGetKeyBytes(CurrentKeyId, out var key)
            ? key
            : throw new InvalidOperationException(
                $"Auth:Encryption:CurrentKeyId is '{CurrentKeyId}', which is not in Auth:Encryption:Keys. "
                + "Without it a TOTP secret cannot be stored, so two-factor enrolment would fail at the "
                + "moment a user tried it rather than at startup.");

    /// <summary>Looks a key up by id, returning false rather than throwing for an unknown one.</summary>
    /// <param name="keyId">The key id from a stored envelope.</param>
    /// <param name="key">The 32 key bytes.</param>
    public bool TryGetKeyBytes(string keyId, out byte[] key)
    {
        key = [];

        if (string.IsNullOrWhiteSpace(keyId) || !Keys.TryGetValue(keyId, out var encoded))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length != 32)
            {
                return false;
            }

            key = bytes;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Generates a key of the right size, for the setup instructions and for tests.</summary>
    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
