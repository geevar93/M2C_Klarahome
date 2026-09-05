using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.External;

namespace KlaraHome.IntegrationTests.Identity;

/// <summary>
/// Stands in for Google at the network boundary, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// It replaces exactly one thing: the two HTTP calls to a provider we do not control. Everything
/// that decides what happens to the identity it returns — the state cookie, the PKCE verifier, the
/// linking rules, the account creation, the session, the audit entry — is the implementation that
/// runs in production.
/// </para>
/// <para>
/// The alternative was a stub HTTP server speaking OIDC, which would test <c>HttpClient</c> and
/// System.Text.Json rather than any decision this system makes. The parts that <em>are</em> ours
/// at that boundary — reading an <c>id_token</c>, the host allow-list, PKCE — are covered by unit
/// tests against the published vectors instead.
/// </para>
/// </remarks>
internal sealed class FakeIdentityProvider : IExternalIdentityProvider
{
    /// <summary>The authorization code this provider answers to. Anything else is refused.</summary>
    public const string ValidCode = "a-code-the-provider-issued";

    /// <inheritdoc />
    public ExternalProvider Provider => ExternalProvider.Google;

    /// <summary>Whether the provider is offered. Settable, to test an unconfigured deployment.</summary>
    public bool IsUsable { get; set; } = true;

    /// <summary>Who the provider says the next caller is.</summary>
    public ExternalIdentity Identity { get; set; } =
        new("google-subject-1", "shopper@example.in", EmailVerified: true, "A Shopper");

    /// <summary>Set to make the next exchange fail as though the provider were unreachable.</summary>
    public ExternalExchangeFailure? NextFailure { get; set; }

    /// <summary>The PKCE verifier the last exchange was given, so a test can assert it travelled.</summary>
    public string? LastCodeVerifier { get; private set; }

    /// <summary>The redirect URI the last authorization request was built with.</summary>
    public string? LastRedirectUri { get; private set; }

    /// <inheritdoc />
    public Task<Uri> BuildAuthorizationUriAsync(
        string state,
        string codeChallenge,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        LastRedirectUri = redirectUri;

        return Task.FromResult(new Uri(
            $"https://accounts.example.test/authorize?state={state}&code_challenge={codeChallenge}"));
    }

    /// <inheritdoc />
    public Task<ExternalExchangeResult> ExchangeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        LastCodeVerifier = codeVerifier;

        if (NextFailure is { } failure)
        {
            NextFailure = null;
            return Task.FromResult(ExternalExchangeResult.Failed(failure));
        }

        return Task.FromResult(string.Equals(code, ValidCode, StringComparison.Ordinal)
            ? ExternalExchangeResult.Success(Identity)
            : ExternalExchangeResult.Failed(ExternalExchangeFailure.Rejected));
    }
}
