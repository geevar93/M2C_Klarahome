using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Identity.Infrastructure;
using KlaraHome.Modules.Identity.Infrastructure.External;

namespace KlaraHome.IntegrationTests.Identity;

/// <summary>
/// The Step 7A acceptance criterion: a customer registers and signs in with an identity provider
/// while every paid-delivery feature is switched off (ADR-014).
/// </summary>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class ExternalLoginTests(KlaraHomeSchemaFixture fixture) : IdentityTestBase(fixture)
{
    [Fact]
    public async Task A_customer_registers_and_signs_in_with_a_provider_while_delivery_is_off()
    {
        SkipWithoutDocker();

        // The state a deployment with no SMS and no email account is actually in.
        TurnOffPaidDelivery();

        var email = NewEmail("google-shopper");
        Provider.Identity = new ExternalIdentity("google-" + Guid.NewGuid(), email, EmailVerified: true, "A Shopper");

        using var client = CreateClient(followRedirects: false);

        var offered = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/store/auth/external/providers",
            Cancellation);

        Assert.Equal("google", offered.EnumerateArray().Single().GetProperty("provider").GetString());

        var session = await SignInWithProviderAsync(client, "/account");

        // A real session, with the customer role and the profile the account page reads.
        var me = await session.GetFromJsonAsync<JsonElement>("/api/v1/store/me", Cancellation);
        var user = me.GetProperty("user");

        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal("customer", user.GetProperty("userType").GetString());
        Assert.Equal(["customer"], user.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));

        // Verified by the provider, which is a stronger assertion than our own link would be —
        // and the reason this works with no email transport at all.
        Assert.True(user.GetProperty("emailVerified").GetBoolean());
        Assert.Equal("A", me.GetProperty("profile").GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task Signing_in_again_returns_the_same_account_rather_than_a_second_one()
    {
        SkipWithoutDocker();

        var subject = "google-" + Guid.NewGuid();
        Provider.Identity = new ExternalIdentity(subject, NewEmail("returning"), EmailVerified: true, "A Shopper");

        using var first = CreateClient(followRedirects: false);
        var one = await SignInWithProviderAsync(first);

        // The provider's subject is the key, so a changed email address does not lose their orders.
        Provider.Identity = Provider.Identity with { Email = NewEmail("renamed") };

        using var second = CreateClient(followRedirects: false);
        var two = await SignInWithProviderAsync(second);

        Assert.Equal(await UserIdOf(one), await UserIdOf(two));
    }

    [Fact]
    public async Task A_verified_email_links_to_an_account_that_already_exists()
    {
        SkipWithoutDocker();

        var email = NewEmail("already-here");

        using var registered = CreateClient();
        var response = await registered.PostAsJsonAsync(
            "/api/v1/store/auth/register",
            new { email, password = "the-quiet-lamp-post-hums", mobile = (string?)null, marketingConsent = false },
            Cancellation);

        response.EnsureSuccessStatusCode();
        var existing = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation))
            .GetProperty("user").GetProperty("id").GetString();

        Provider.Identity = new ExternalIdentity("google-" + Guid.NewGuid(), email, EmailVerified: true, "A Shopper");

        using var client = CreateClient(followRedirects: false);
        var session = await SignInWithProviderAsync(client);

        Assert.Equal(existing, await UserIdOf(session));
    }

    [Fact]
    public async Task An_unverified_email_does_not_link_to_an_account_that_already_exists()
    {
        SkipWithoutDocker();

        var email = NewEmail("not-yours");

        using var registered = CreateClient();
        var response = await registered.PostAsJsonAsync(
            "/api/v1/store/auth/register",
            new { email, password = "the-quiet-lamp-post-hums", mobile = (string?)null, marketingConsent = false },
            Cancellation);

        response.EnsureSuccessStatusCode();
        var victim = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation))
            .GetProperty("user").GetProperty("id").GetString();

        // The account-takeover vector: a provider asserting an address it has not verified must not
        // be able to claim somebody else's account (docs/07-security-compliance.md §1).
        Provider.Identity = new ExternalIdentity("google-" + Guid.NewGuid(), email, EmailVerified: false, "An Attacker");

        using var client = CreateClient(followRedirects: false);

        // The email is already taken by the account it must not reach, so a new one cannot be
        // created either. Refused outright rather than silently linked.
        var callback = await CallbackAsync(client, await StartAsync(client, null));

        Assert.Equal(HttpStatusCode.Conflict, callback.StatusCode);

        var problem = await callback.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("IDENTITY_ACCOUNT_EXISTS", problem.GetProperty("code").GetString());

        // And the account it was aimed at is untouched.
        Assert.NotNull(victim);
    }

    [Fact]
    public async Task A_staff_account_cannot_be_taken_over_by_an_identity_provider()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var email = NewEmail("ops-person");
        await CreateStaffAsync(admin, email, "operations");

        // ADR-014 decision 3: staff sign in with a password and a second factor. A provider
        // asserting their address must not become a way around that.
        Provider.Identity = new ExternalIdentity("google-" + Guid.NewGuid(), email, EmailVerified: true, "Ops");

        using var client = CreateClient(followRedirects: false);
        var callback = await CallbackAsync(client, await StartAsync(client, null));

        Assert.Equal(HttpStatusCode.Forbidden, callback.StatusCode);

        var problem = await callback.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("IDENTITY_EXTERNAL_NOT_PERMITTED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_callback_without_the_state_cookie_is_refused()
    {
        SkipWithoutDocker();

        using var starter = CreateClient(followRedirects: false);
        var started = await StartAsync(starter, null);

        // A fresh client holds no cookie, so this is a callback nobody started — the shape a
        // forged or replayed callback takes.
        using var stranger = CreateClient(followRedirects: false);

        var response = await stranger.GetAsync(
            new Uri($"/api/v1/store/auth/external/google/callback?code={FakeIdentityProvider.ValidCode}&state={started.State}", UriKind.Relative),
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("IDENTITY_EXTERNAL_CALLBACK_INVALID", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_callback_with_a_forged_state_is_refused()
    {
        SkipWithoutDocker();

        using var client = CreateClient(followRedirects: false);
        await StartAsync(client, null);

        var response = await client.GetAsync(
            new Uri($"/api/v1/store/auth/external/google/callback?code={FakeIdentityProvider.ValidCode}&state=forged", UriKind.Relative),
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_PKCE_verifier_reaches_the_exchange_and_never_the_browser()
    {
        SkipWithoutDocker();

        Provider.Identity = new ExternalIdentity("google-" + Guid.NewGuid(), NewEmail("pkce"), true, "A Shopper");

        using var client = CreateClient(followRedirects: false);
        var started = await StartAsync(client, null);

        // The challenge goes to the provider; the verifier stays here. An attacker who intercepts
        // the code has the first and not the second, which is what makes the code unredeemable.
        Assert.Contains("code_challenge=", started.Location, StringComparison.Ordinal);
        Assert.DoesNotContain("code_verifier", started.Location, StringComparison.Ordinal);

        await CallbackAsync(client, started);

        Assert.NotNull(Provider.LastCodeVerifier);
        Assert.DoesNotContain(Provider.LastCodeVerifier, started.Location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_redirect_uri_comes_from_configuration_rather_than_the_request_host()
    {
        SkipWithoutDocker();

        using var client = CreateClient(followRedirects: false);
        await StartAsync(client, null);

        // A forwarded Host header is attacker-controlled, and the provider re-checks this value
        // against the one registered with it.
        Assert.Equal(
            "http://localhost/api/v1/store/auth/external/google/callback",
            Provider.LastRedirectUri);
    }

    [Fact]
    public async Task A_return_url_outside_the_allow_list_is_refused_before_the_provider_is_reached()
    {
        SkipWithoutDocker();

        using var client = CreateClient(followRedirects: false);

        var response = await client.GetAsync(
            new Uri("/api/v1/store/auth/external/google/start?returnUrl=https://evil.example.com/", UriKind.Relative),
            Cancellation);

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("IDENTITY_RETURN_URL_NOT_ALLOWED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_allowed_return_url_is_where_the_browser_lands()
    {
        SkipWithoutDocker();

        Provider.Identity = new ExternalIdentity("google-" + Guid.NewGuid(), NewEmail("returns-to"), true, "A Shopper");

        using var client = CreateClient(followRedirects: false);
        var callback = await CallbackAsync(client, await StartAsync(client, "https://shop.example.test/account"));

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("https://shop.example.test/account", callback.Headers.Location?.ToString());
    }

    [Fact]
    public async Task An_unreachable_provider_is_a_503_rather_than_a_failed_sign_in()
    {
        SkipWithoutDocker();

        using var client = CreateClient(followRedirects: false);
        var started = await StartAsync(client, null);

        Provider.NextFailure = ExternalExchangeFailure.Unavailable;

        var callback = await CallbackAsync(client, started);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, callback.StatusCode);

        var problem = await callback.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("IDENTITY_PROVIDER_UNAVAILABLE", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unknown_provider_is_a_404()
    {
        SkipWithoutDocker();

        using var client = CreateClient(followRedirects: false);

        var response = await client.GetAsync(
            new Uri("/api/v1/store/auth/external/apple/start", UriKind.Relative),
            Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_whole_surface_disappears_when_the_flag_is_off()
    {
        SkipWithoutDocker();

        Features[IdentityFeatures.ExternalLogin] = false;

        using var client = CreateClient(followRedirects: false);

        foreach (var route in new[]
                 {
                     "/api/v1/store/auth/external/providers",
                     "/api/v1/store/auth/external/google/start",
                 })
        {
            var response = await client.GetAsync(new Uri(route, UriKind.Relative), Cancellation);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
            Assert.Equal("FEATURE_DISABLED", problem.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task A_customer_sees_their_linked_provider_and_can_unlink_it_once_they_have_a_password()
    {
        SkipWithoutDocker();

        var email = NewEmail("unlinker");
        Provider.Identity = new ExternalIdentity("google-" + Guid.NewGuid(), email, EmailVerified: true, "A Shopper");

        using var client = CreateClient(followRedirects: false);
        var session = await SignInWithProviderAsync(client);

        var links = await session.GetFromJsonAsync<JsonElement>("/api/v1/store/me/external-logins", Cancellation);
        var link = links.EnumerateArray().Single();

        Assert.Equal("google", link.GetProperty("provider").GetString());
        Assert.Equal(email, link.GetProperty("email").GetString());

        var id = link.GetProperty("id").GetString();

        // The account was created by the provider, so it has no password and no verified mobile
        // number: removing the link would leave it unreachable by anybody, including its owner.
        var refused = await session.DeleteAsync(
            new Uri($"/api/v1/store/me/external-logins/{id}", UriKind.Relative),
            Cancellation);

        Assert.Equal(HttpStatusCode.UnprocessableContent, refused.StatusCode);

        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("IDENTITY_LAST_CREDENTIAL", problem.GetProperty("code").GetString());
    }

    /// <summary>Turns off every feature that needs a provider nobody has paid for.</summary>
    private void TurnOffPaidDelivery()
    {
        Features[IdentityFeatures.MobileOtpLogin] = false;
        Features[IdentityFeatures.EmailVerification] = false;
        Features[IdentityFeatures.PasswordResetEmail] = false;
    }

    private sealed record StartedSignIn(string Location, string State);

    private static async Task<StartedSignIn> StartAsync(HttpClient client, string? returnUrl)
    {
        var route = returnUrl is null
            ? "/api/v1/store/auth/external/google/start"
            : $"/api/v1/store/auth/external/google/start?returnUrl={Uri.EscapeDataString(returnUrl)}";

        var response = await client.GetAsync(new Uri(route, UriKind.Relative), Cancellation);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location?.ToString() ?? string.Empty;
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(new Uri(location).Query)["state"]
            .ToString();

        return new StartedSignIn(location, state);
    }

    private static Task<HttpResponseMessage> CallbackAsync(HttpClient client, StartedSignIn started)
        => client.GetAsync(
            new Uri(
                $"/api/v1/store/auth/external/google/callback?code={FakeIdentityProvider.ValidCode}&state={started.State}",
                UriKind.Relative),
            Cancellation);

    /// <summary>
    /// Runs the whole redirect dance and returns a client holding an access token.
    /// </summary>
    /// <remarks>
    /// The callback ends in a redirect with a refresh cookie, not a token — it is a browser
    /// navigation. The storefront picks up an access token by calling <c>/auth/refresh</c> on
    /// arrival, which is what this does.
    /// </remarks>
    private static async Task<HttpClient> SignInWithProviderAsync(HttpClient client, string? returnUrl = null)
    {
        var callback = await CallbackAsync(client, await StartAsync(client, returnUrl));

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);

        var refreshed = await client.PostAsync(
            new Uri("/api/v1/store/auth/refresh", UriKind.Relative),
            null,
            Cancellation);

        refreshed.EnsureSuccessStatusCode();

        var body = await refreshed.Content.ReadFromJsonAsync<JsonElement>(Cancellation);

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            body.GetProperty("accessToken").GetString());

        return client;
    }

    private static async Task<string?> UserIdOf(HttpClient session)
    {
        var me = await session.GetFromJsonAsync<JsonElement>("/api/v1/store/me", Cancellation);
        return me.GetProperty("user").GetProperty("id").GetString();
    }
}
