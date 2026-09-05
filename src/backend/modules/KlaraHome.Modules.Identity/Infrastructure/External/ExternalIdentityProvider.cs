using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KlaraHome.Modules.Identity.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace KlaraHome.Modules.Identity.Infrastructure.External;

/// <summary>Who a provider says the person signing in is.</summary>
/// <param name="Subject">The provider's stable identifier for them.</param>
/// <param name="Email">The address it asserts, or null.</param>
/// <param name="EmailVerified">Whether it states that it verified that address.</param>
/// <param name="DisplayName">The name it asserts, or null.</param>
internal sealed record ExternalIdentity(string Subject, string? Email, bool EmailVerified, string? DisplayName);

/// <summary>Why an exchange did not produce an identity.</summary>
internal enum ExternalExchangeFailure
{
    /// <summary>The provider refused the code, or it was replayed.</summary>
    Rejected = 0,

    /// <summary>The provider could not be reached, or answered something unreadable.</summary>
    Unavailable = 1,

    /// <summary>The provider answered, but with no usable subject.</summary>
    NoIdentity = 2,
}

/// <summary>The result of exchanging an authorization code.</summary>
/// <param name="Identity">Who they are, when the exchange worked.</param>
/// <param name="Failure">Why it did not, otherwise.</param>
internal sealed record ExternalExchangeResult(ExternalIdentity? Identity, ExternalExchangeFailure? Failure)
{
    /// <summary>A successful exchange.</summary>
    /// <param name="identity">Who the provider says they are.</param>
    public static ExternalExchangeResult Success(ExternalIdentity identity) => new(identity, null);

    /// <summary>A failed exchange.</summary>
    /// <param name="failure">Why.</param>
    public static ExternalExchangeResult Failed(ExternalExchangeFailure failure) => new(null, failure);
}

/// <summary>
/// One identity provider, behind the same shape as every other third-party adapter in this system
/// (docs/08-integrations.md §3.5).
/// </summary>
/// <remarks>
/// Two methods, because the authorization-code flow is two half-conversations with a redirect in
/// between: build the URL the browser is sent to, then swap the code it comes back with for an
/// identity. Nothing else about a provider differs.
/// </remarks>
internal interface IExternalIdentityProvider
{
    /// <summary>Which provider this is.</summary>
    ExternalProvider Provider { get; }

    /// <summary>Whether it is configured well enough to offer.</summary>
    bool IsUsable { get; }

    /// <summary>Builds the URL the browser is redirected to.</summary>
    /// <param name="state">The opaque anti-forgery value, echoed back by the provider.</param>
    /// <param name="codeChallenge">The S256 PKCE challenge.</param>
    /// <param name="redirectUri">Where the provider sends the browser back to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Uri> BuildAuthorizationUriAsync(
        string state,
        string codeChallenge,
        string redirectUri,
        CancellationToken cancellationToken);

    /// <summary>Swaps an authorization code for the person's identity.</summary>
    /// <param name="code">The code the provider redirected back with.</param>
    /// <param name="codeVerifier">The PKCE verifier this sign-in was started with.</param>
    /// <param name="redirectUri">The same redirect URI, which the provider re-checks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ExternalExchangeResult> ExchangeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken);
}

/// <summary>
/// PKCE (RFC 7636), which is what stops a stolen authorization code from being redeemable.
/// </summary>
/// <remarks>
/// The verifier is minted per sign-in and never leaves this server; only its SHA-256 hash travels
/// to the provider. An attacker who intercepts the code in the redirect cannot exchange it without
/// the verifier, which they have no way to see.
/// </remarks>
internal static class Pkce
{
    /// <summary>Mints a verifier: 32 random bytes, base64url, well inside RFC 7636's 43-128 range.</summary>
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>The S256 challenge for a verifier.</summary>
    /// <param name="verifier">The verifier.</param>
    public static string ChallengeFor(string verifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(verifier);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.ASCII.GetBytes(verifier), digest);

        return Base64Url(digest);
    }

    /// <summary>Mints the opaque <c>state</c> value.</summary>
    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(ReadOnlySpan<byte> bytes) => System.Buffers.Text.Base64Url.EncodeToString(bytes);
}

/// <summary>
/// The generic OpenID Connect adapter, which is all Google needs.
/// </summary>
/// <remarks>
/// <para>
/// Endpoints come from the authority's discovery document rather than from configuration, so a
/// provider that moves its token endpoint does not silently break — and, more to the point, so
/// that nothing a caller supplied can ever decide where the token exchange posts to
/// (docs/07-security-compliance.md §3). The authority itself is pinned in configuration.
/// </para>
/// <para>
/// The identity comes from the <c>id_token</c>, not from a second call to the userinfo endpoint.
/// It is signed by the provider, it already carries <c>sub</c>, <c>email</c> and
/// <c>email_verified</c>, and reading it costs no round trip. The signature is not re-validated
/// here: the token arrived over TLS from the provider's own token endpoint in a direct
/// server-to-server call, which is what OIDC §3.1.3.7 permits.
/// </para>
/// </remarks>
/// <param name="provider">Which provider this instance serves.</param>
/// <param name="options">Its registration.</param>
/// <param name="discovery">Fetches and caches the discovery document.</param>
/// <param name="httpClientFactory">Supplies the timeout-bounded client.</param>
/// <param name="logger">Reports an unreachable or unreadable provider.</param>
internal sealed partial class OidcIdentityProvider(
    ExternalProvider provider,
    ExternalProviderOptions options,
    OidcDiscoveryCache discovery,
    IHttpClientFactory httpClientFactory,
    ILogger logger) : IExternalIdentityProvider
{
    /// <summary>
    /// The provider's name, computed once. Enum.ToString allocates, and an argument to a
    /// LoggerMessage is evaluated whether or not the level is enabled.
    /// </summary>
    private readonly string _providerName = provider.ToString();

    /// <inheritdoc />
    public ExternalProvider Provider => provider;

    /// <inheritdoc />
    public bool IsUsable => options.IsUsable;

    /// <inheritdoc />
    public async Task<Uri> BuildAuthorizationUriAsync(
        string state,
        string codeChallenge,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        var endpoints = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["response_type"] = "code",
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = options.Scopes,
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };

        return new Uri(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            endpoints.Authorization,
            query));
    }

    /// <inheritdoc />
    public async Task<ExternalExchangeResult> ExchangeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        OidcEndpoints endpoints;

        try
        {
            endpoints = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            ProviderUnreachable(logger, _providerName, exception);
            return ExternalExchangeResult.Failed(ExternalExchangeFailure.Unavailable);
        }

        using var client = httpClientFactory.CreateClient(ExternalHttp.ClientName);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["code_verifier"] = codeVerifier,
        });

        try
        {
            using var response = await client
                .PostAsync(new Uri(endpoints.Token), form, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // A refused code is the ordinary outcome of a replayed or expired callback, and is
                // not logged with the body: the body carries the client secret's counterpart.
                ProviderRefusedCode(logger, _providerName, (int)response.StatusCode);
                return ExternalExchangeResult.Failed(ExternalExchangeFailure.Rejected);
            }

            var payload = await response.Content
                .ReadFromJsonAsync<TokenResponse>(cancellationToken)
                .ConfigureAwait(false);

            if (payload?.IdToken is null)
            {
                return ExternalExchangeResult.Failed(ExternalExchangeFailure.NoIdentity);
            }

            return ReadIdentity(payload.IdToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            ProviderUnreachable(logger, _providerName, exception);
            return ExternalExchangeResult.Failed(ExternalExchangeFailure.Unavailable);
        }
    }

    /// <summary>Reads the claims out of an <c>id_token</c>.</summary>
    internal static ExternalExchangeResult ReadIdentity(string idToken)
    {
        JsonWebToken token;

        try
        {
            token = new JsonWebToken(idToken);
        }
        catch (ArgumentException)
        {
            return ExternalExchangeResult.Failed(ExternalExchangeFailure.NoIdentity);
        }

        var subject = Claim(token, "sub");

        if (string.IsNullOrWhiteSpace(subject))
        {
            return ExternalExchangeResult.Failed(ExternalExchangeFailure.NoIdentity);
        }

        var email = Claim(token, "email");
        var verified = string.Equals(Claim(token, "email_verified"), "true", StringComparison.OrdinalIgnoreCase);

        return ExternalExchangeResult.Success(
            new ExternalIdentity(subject, email?.Trim().ToLowerInvariant(), verified, Claim(token, "name")));
    }

    private static string? Claim(JsonWebToken token, string type)
        => token.TryGetClaim(type, out var claim) ? claim.Value : null;

    private async Task<OidcEndpoints> ResolveAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.Authority))
        {
            return await discovery.GetAsync(options.Authority, cancellationToken).ConfigureAwait(false);
        }

        return new OidcEndpoints(
            options.AuthorizationEndpoint
                ?? throw new InvalidOperationException($"{provider} has neither an authority nor an authorization endpoint."),
            options.TokenEndpoint
                ?? throw new InvalidOperationException($"{provider} has neither an authority nor a token endpoint."),
            options.UserInfoEndpoint);
    }

    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("id_token")] string? IdToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string? AccessToken);

    [LoggerMessage(EventId = 1410, Level = LogLevel.Warning,
        Message = "Identity provider {ExternalProvider} could not be reached. Sign-in was refused.")]
    private static partial void ProviderUnreachable(ILogger logger, string externalProvider, Exception exception);

    [LoggerMessage(EventId = 1411, Level = LogLevel.Information,
        Message = "Identity provider {ExternalProvider} refused an authorization code with {StatusCode}. "
                  + "Usually a replayed or expired callback.")]
    private static partial void ProviderRefusedCode(ILogger logger, string externalProvider, int statusCode);
}

/// <summary>The three endpoints an authorization-code flow needs.</summary>
/// <param name="Authorization">Where the browser is sent.</param>
/// <param name="Token">Where the code is exchanged, server to server.</param>
/// <param name="UserInfo">Where profile claims can be fetched, when the id token is not enough.</param>
internal sealed record OidcEndpoints(string Authorization, string Token, string? UserInfo);

/// <summary>
/// Fetches an authority's <c>/.well-known/openid-configuration</c> once and remembers it.
/// </summary>
/// <remarks>
/// Cached because it changes about never and a sign-in should not pay for it, and bounded because
/// a provider that starts answering slowly must not hold request threads. A failed fetch is not
/// cached: the next sign-in tries again rather than inheriting a stale outage.
/// </remarks>
/// <param name="httpClientFactory">Supplies the timeout-bounded client.</param>
internal sealed class OidcDiscoveryCache(IHttpClientFactory httpClientFactory)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, OidcEndpoints> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The endpoints an authority publishes.</summary>
    /// <param name="authority">The issuer, pinned in configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OidcEndpoints> GetAsync(string authority, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(authority, out var cached))
        {
            return cached;
        }

        var document = string.Create(
            CultureInfo.InvariantCulture,
            $"{authority.TrimEnd('/')}/.well-known/openid-configuration");

        using var client = httpClientFactory.CreateClient(ExternalHttp.ClientName);

        var discovered = await client
            .GetFromJsonAsync<DiscoveryDocument>(new Uri(document), cancellationToken)
            .ConfigureAwait(false);

        if (discovered?.AuthorizationEndpoint is null || discovered.TokenEndpoint is null)
        {
            throw new InvalidOperationException(
                $"The discovery document at '{document}' has no authorization or token endpoint.");
        }

        var endpoints = new OidcEndpoints(
            discovered.AuthorizationEndpoint,
            discovered.TokenEndpoint,
            discovered.UserInfoEndpoint);

        _cache[authority] = endpoints;
        return endpoints;
    }

    private sealed record DiscoveryDocument(
        [property: System.Text.Json.Serialization.JsonPropertyName("authorization_endpoint")] string? AuthorizationEndpoint,
        [property: System.Text.Json.Serialization.JsonPropertyName("token_endpoint")] string? TokenEndpoint,
        [property: System.Text.Json.Serialization.JsonPropertyName("userinfo_endpoint")] string? UserInfoEndpoint);
}

/// <summary>The named outbound client, and the only hosts this system is allowed to call.</summary>
/// <remarks>
/// The first outbound HTTP this product makes, so it arrives with the allow-list
/// <c>07-security-compliance.md</c> §3 requires rather than after one. Nothing here is
/// caller-supplied: the hosts come from a pinned authority and from compiled-in provider
/// endpoints, and a request to anything else is refused before a connection is opened.
/// </remarks>
internal static class ExternalHttp
{
    /// <summary>The named <see cref="HttpClient"/> every provider adapter resolves.</summary>
    public const string ClientName = "identity-provider";

    /// <summary>How long a provider has to answer before the sign-in is refused.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>The hosts this client may reach.</summary>
    public static readonly IReadOnlySet<string> AllowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "accounts.google.com",
        "oauth2.googleapis.com",
        "openidconnect.googleapis.com",
        "www.googleapis.com",
        "www.facebook.com",
        "graph.facebook.com",
    };
}

/// <summary>Refuses any outbound request to a host outside the allow-list.</summary>
/// <remarks>
/// A belt-and-braces control. The URLs are already not caller-supplied; this makes a future
/// mistake that lets one through fail at the socket rather than at the provider.
/// </remarks>
internal sealed partial class AllowedHostHandler(ILogger<AllowedHostHandler> logger) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var host = request.RequestUri?.Host;

        if (host is null || !ExternalHttp.AllowedHosts.Contains(host))
        {
            BlockedHost(logger, host ?? "<none>");

            throw new HttpRequestException(
                $"Outbound request to '{host}' was refused: it is not in the identity-provider allow-list.");
        }

        return base.SendAsync(request, cancellationToken);
    }

    [LoggerMessage(EventId = 1412, Level = LogLevel.Error,
        Message = "Refused an outbound request to {BlockedHost}: not in the identity-provider allow-list.")]
    private static partial void BlockedHost(ILogger logger, string blockedHost);
}
