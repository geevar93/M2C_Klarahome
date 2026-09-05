using System.Security.Cryptography;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace KlaraHome.Modules.Identity.Infrastructure.Security;

/// <summary>
/// The RSA keys access tokens are signed and validated with (docs/07-security-compliance.md §1).
/// </summary>
/// <remarks>
/// <para>
/// The first configured key signs; every configured key validates. That is what makes rotation
/// possible without a flag day — put the new key at the front, deploy, and tokens minted under the
/// outgoing key keep working until the last of them expires, at which point it can be dropped.
/// </para>
/// <para>
/// With no key configured a Development host mints an ephemeral one and says so, loudly, on every
/// start. Any other environment refuses to start: an ephemeral key differs per replica and is
/// discarded on restart, which in production is an outage that presents as a bug in whatever the
/// user happened to be doing.
/// </para>
/// </remarks>
internal sealed partial class SigningKeyRing
{
    private readonly List<RsaSecurityKey> _keys = [];

    /// <param name="options">The configured key ring.</param>
    /// <param name="environment">Decides whether a missing key is a warning or a refusal.</param>
    /// <param name="logger">Reports an ephemeral key on every Development start.</param>
    public SigningKeyRing(
        IOptions<AuthOptions> options,
        IHostEnvironment environment,
        ILogger<SigningKeyRing> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        foreach (var configured in options.Value.Tokens.SigningKeys)
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(configured.PrivateKeyPem);
            _keys.Add(new RsaSecurityKey(rsa) { KeyId = configured.KeyId });
        }

        if (_keys.Count > 0)
        {
            return;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Auth:Tokens:SigningKeys is empty. Outside Development the RSA signing key must be supplied "
                + "as a secret: an ephemeral key differs in every replica and is discarded on every restart, "
                + "signing every user out without explanation.");
        }

        _keys.Add(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "dev-ephemeral" });
        EphemeralSigningKey(logger);
    }

    /// <summary>The key new tokens are signed with.</summary>
    public SigningCredentials SigningCredentials => new(_keys[0], SecurityAlgorithms.RsaSha256);

    /// <summary>Every key a presented token may have been signed with.</summary>
    public IReadOnlyList<SecurityKey> ValidationKeys => _keys;

    [LoggerMessage(EventId = 1400, Level = LogLevel.Warning,
        Message = "No Auth:Tokens:SigningKeys configured. An ephemeral RSA key was generated for this process: "
                  + "every access token becomes invalid when it restarts. Development only.")]
    private static partial void EphemeralSigningKey(ILogger logger);
}

/// <summary>What a completed sign-in hands back.</summary>
/// <param name="AccessToken">The signed JWT. Held in memory by the client, never in storage.</param>
/// <param name="ExpiresAt">When the access token stops being accepted.</param>
/// <param name="RefreshToken">The opaque refresh secret, for the cookie. Never in a response body.</param>
/// <param name="SessionId">The session both tokens belong to.</param>
internal sealed record IssuedTokens(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    Guid SessionId);

/// <summary>
/// Mints the access token (docs/07-security-compliance.md §1) and the opaque refresh secret that
/// accompanies it.
/// </summary>
/// <remarks>
/// <para>
/// Permissions are written as a JSON array under one claim name, which the bearer handler expands
/// back into one claim per permission. The authorisation handler then compares whole values, so a
/// permission called <c>orders.read</c> can never be satisfied by one called <c>orders.read-all</c>.
/// </para>
/// <para>
/// <see cref="JsonWebTokenHandler"/> rather than the older <c>JwtSecurityTokenHandler</c>: the
/// latter rewrites <c>sub</c> into a WS-Federation claim type on the way in, and a claim the
/// issuer and the reader spell differently is a permission check that silently never matches.
/// </para>
/// </remarks>
/// <param name="keys">The signing key ring.</param>
/// <param name="options">Token lifetimes, issuer and audience.</param>
/// <param name="tenant">The tenant stamped into every token.</param>
/// <param name="clock">The clock, so lifetimes are testable.</param>
internal sealed class TokenIssuer(
    SigningKeyRing keys,
    IOptions<AuthOptions> options,
    ITenantContext tenant,
    IClock clock)
{
    /// <summary>
    /// The audience a 2FA challenge is minted for. Deliberately not the API's own: presented as a
    /// bearer token, a challenge must fail audience validation rather than authenticate someone who
    /// has not finished proving who they are.
    /// </summary>
    public const string TwoFactorAudience = "klarahome-2fa";

    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>Mints an access token and a refresh secret for a session.</summary>
    /// <param name="user">The signed-in user.</param>
    /// <param name="sessionId">The session the tokens belong to.</param>
    /// <param name="permissions">The permissions the user holds right now.</param>
    /// <param name="vendorId">The vendor the user acts for, or null.</param>
    public IssuedTokens Issue(
        User user,
        Guid sessionId,
        IReadOnlyCollection<string> permissions,
        Guid? vendorId)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(permissions);

        var tokens = options.Value.Tokens;
        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(tokens.AccessTokenMinutes);

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [KlaraHomeClaims.UserId] = user.Id.ToString(),
            [KlaraHomeClaims.TenantId] = tenant.TenantId.ToString(),
            [KlaraHomeClaims.UserType] = UserTypeNames.Of(user.UserType),
            [KlaraHomeClaims.SessionId] = sessionId.ToString(),
            [JwtRegisteredClaimNames.Jti] = UuidV7.New().ToString(),
            [KlaraHomeClaims.Permission] = permissions.ToArray(),
        };

        if (vendorId is not null)
        {
            claims[KlaraHomeClaims.VendorId] = vendorId.Value.ToString();
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = tokens.Issuer,
            Audience = tokens.Audience,
            Claims = claims,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            SigningCredentials = keys.SigningCredentials,
        };

        return new IssuedTokens(
            _handler.CreateToken(descriptor),
            expiresAt,
            SecretHasher.NewOpaqueToken(),
            sessionId);
    }

    /// <summary>Mints the short-lived token that carries a half-finished sign-in to the 2FA prompt.</summary>
    /// <remarks>
    /// A token rather than a row, so a sign-in abandoned at the prompt leaves nothing to clean up.
    /// It carries no permission claim at all: it authenticates nothing.
    /// </remarks>
    /// <param name="userId">The user part-way through signing in.</param>
    public string IssueTwoFactorChallenge(Guid userId)
    {
        var tokens = options.Value.Tokens;
        var now = clock.UtcNow;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = tokens.Issuer,
            Audience = TwoFactorAudience,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [KlaraHomeClaims.UserId] = userId.ToString(),
            },
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(tokens.TwoFactorChallengeMinutes).UtcDateTime,
            IssuedAt = now.UtcDateTime,
            SigningCredentials = keys.SigningCredentials,
        };

        return _handler.CreateToken(descriptor);
    }

    /// <summary>Reads a 2FA challenge token, or returns null if it is not one we minted.</summary>
    /// <param name="challengeToken">The token presented at the prompt.</param>
    public async Task<Guid?> ReadTwoFactorChallengeAsync(string? challengeToken)
    {
        if (string.IsNullOrWhiteSpace(challengeToken))
        {
            return null;
        }

        var tokens = options.Value.Tokens;

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = tokens.Issuer,
            ValidAudience = TwoFactorAudience,
            IssuerSigningKeys = keys.ValidationKeys,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        var result = await _handler.ValidateTokenAsync(challengeToken, parameters).ConfigureAwait(false);

        if (!result.IsValid)
        {
            return null;
        }

        var subject = result.ClaimsIdentity.FindFirst(KlaraHomeClaims.UserId)?.Value;
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}

/// <summary>The wire spelling of <see cref="UserType"/>, used by the token and the API alike.</summary>
internal static class UserTypeNames
{
    /// <summary>A shopper.</summary>
    public const string Customer = "customer";

    /// <summary>A seller's staff member.</summary>
    public const string Vendor = "vendor";

    /// <summary>Platform staff.</summary>
    public const string Staff = "staff";

    /// <summary>The wire spelling of a user type.</summary>
    /// <param name="userType">The type.</param>
    public static string Of(UserType userType) => userType switch
    {
        UserType.Customer => Customer,
        UserType.Vendor => Vendor,
        UserType.Staff => Staff,
        _ => throw new ArgumentOutOfRangeException(nameof(userType)),
    };
}
