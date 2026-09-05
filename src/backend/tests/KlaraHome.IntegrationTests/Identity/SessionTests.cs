using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Identity.Domain;

namespace KlaraHome.IntegrationTests.Identity;

/// <summary>
/// The Step 7 acceptance criterion for refresh-token rotation and revocation
/// (docs/07-security-compliance.md §1).
/// </summary>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class SessionTests(KlaraHomeSchemaFixture fixture) : IdentityTestBase(fixture)
{
    [Fact]
    public async Task The_refresh_token_travels_in_a_cookie_and_never_in_the_body()
    {
        SkipWithoutDocker();

        using var client = CreateClient();
        var mobile = NewMobile();

        await client.PostAsJsonAsync("/api/v1/store/auth/otp/request", new { mobile }, Cancellation);

        var response = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/verify",
            new { mobile, code = Otp.Latest(mobile, OtpPurpose.Login) },
            Cancellation);

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith("kh_rt=", StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);

        var body = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_refresh_rotates_the_token_and_issues_a_new_access_token()
    {
        SkipWithoutDocker();

        using var client = CreateClient();
        var first = await SignInAsCustomerAsync(client);

        var refreshed = await client.PostAsync(
            new Uri("/api/v1/store/auth/refresh", UriKind.Relative),
            null,
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

        var body = await refreshed.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        var second = body.GetProperty("accessToken").GetString()!;

        Assert.NotEqual(first.AccessToken, second);
        Assert.Contains(
            refreshed.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith("kh_rt=", StringComparison.Ordinal));

        // The rotated token is a working credential.
        Assert.Equal(
            first.UserId.ToString(),
            body.GetProperty("user").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Presenting_a_rotated_token_again_revokes_the_whole_session()
    {
        SkipWithoutDocker();

        using var client = CreateClient();
        var stolen = await SignInAndReadRefreshTokenAsync(client);

        // The legitimate client refreshes, which rotates the token the attacker copied.
        var rotated = await client.PostAsync(new Uri("/api/v1/store/auth/refresh", UriKind.Relative), null, Cancellation);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        using var thief = CreateClient();
        var replay = await PresentRefreshTokenAsync(thief, stolen);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // Two parties held the same secret and there is no way to tell which is which, so the
        // session ends for both. The legitimate client's freshly rotated token stops working too.
        var afterwards = await client.PostAsync(new Uri("/api/v1/store/auth/refresh", UriKind.Relative), null, Cancellation);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    [Fact]
    public async Task A_rejected_refresh_clears_the_cookie_so_the_client_stops_presenting_it()
    {
        SkipWithoutDocker();

        using var client = CreateClient();

        var response = await PresentRefreshTokenAsync(client, "a-token-that-was-never-issued");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith("kh_rt=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Signing_out_ends_the_session_and_the_refresh_token_with_it()
    {
        SkipWithoutDocker();

        using var client = CreateClient();
        await SignInAsCustomerAsync(client);

        var goodbye = await client.PostAsync(new Uri("/api/v1/store/auth/logout", UriKind.Relative), null, Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, goodbye.StatusCode);

        var afterwards = await client.PostAsync(new Uri("/api/v1/store/auth/refresh", UriKind.Relative), null, Cancellation);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    [Fact]
    public async Task Signing_out_when_there_is_nothing_to_sign_out_of_still_succeeds()
    {
        SkipWithoutDocker();

        using var client = CreateClient();

        // A logout that reports failure because the cookie was already gone gives the client
        // nothing to do about it and leaves a user staring at an error on their way out.
        var response = await client.PostAsync(new Uri("/api/v1/store/auth/logout", UriKind.Relative), null, Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_customer_sees_their_devices_and_can_sign_one_out()
    {
        SkipWithoutDocker();

        using var phone = CreateClient();
        var mobile = NewMobile();
        await SignInAsCustomerAsync(phone, mobile);

        using var laptop = CreateClient();
        await SignInAsCustomerAsync(laptop, mobile);

        var sessions = await laptop.GetFromJsonAsync<JsonElement>("/api/v1/store/me/sessions", Cancellation);
        var listed = sessions.EnumerateArray().ToList();

        Assert.Equal(2, listed.Count);
        Assert.Single(listed, session => session.GetProperty("isCurrent").GetBoolean());

        var other = listed.Single(session => !session.GetProperty("isCurrent").GetBoolean());

        var revoked = await laptop.DeleteAsync(
            new Uri($"/api/v1/store/me/sessions/{other.GetProperty("id").GetString()}", UriKind.Relative),
            Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        // The revoked device cannot refresh, and the one that revoked it still can.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await phone.PostAsync(new Uri("/api/v1/store/auth/refresh", UriKind.Relative), null, Cancellation)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await laptop.PostAsync(new Uri("/api/v1/store/auth/refresh", UriKind.Relative), null, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Signing_out_everywhere_keeps_the_current_device_by_default()
    {
        SkipWithoutDocker();

        var mobile = NewMobile();
        using var phone = CreateClient();
        await SignInAsCustomerAsync(phone, mobile);

        using var tablet = CreateClient();
        await SignInAsCustomerAsync(tablet, mobile);

        using var laptop = CreateClient();
        await SignInAsCustomerAsync(laptop, mobile);

        var response = await laptop.DeleteAsync(new Uri("/api/v1/store/me/sessions", UriKind.Relative), Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal(2, body.GetProperty("revoked").GetInt32());

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await phone.PostAsync(new Uri("/api/v1/store/auth/refresh", UriKind.Relative), null, Cancellation)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await laptop.PostAsync(new Uri("/api/v1/store/auth/refresh", UriKind.Relative), null, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task One_customer_cannot_end_another_customers_session()
    {
        SkipWithoutDocker();

        using var mine = CreateClient();
        await SignInAsCustomerAsync(mine);

        using var theirs = CreateClient();
        await SignInAsCustomerAsync(theirs);

        var sessions = await theirs.GetFromJsonAsync<JsonElement>("/api/v1/store/me/sessions", Cancellation);
        var target = sessions.EnumerateArray().Single().GetProperty("id").GetString();

        var response = await mine.DeleteAsync(
            new Uri($"/api/v1/store/me/sessions/{target}", UriKind.Relative),
            Cancellation);

        // 404 rather than 403: someone else's session must be indistinguishable from an absent one.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused_the_account_surface()
    {
        SkipWithoutDocker();

        using var client = CreateClient();

        foreach (var route in new[] { "/api/v1/store/me", "/api/v1/store/me/sessions", "/api/v1/admin/users" })
        {
            var response = await client.GetAsync(new Uri(route, UriKind.Relative), Cancellation);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_two_factor_challenge_token_is_not_an_access_token()
    {
        SkipWithoutDocker();

        using var client = CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new
            {
                email = KlaraHomeSchemaFixture.BootstrapEmail,
                password = KlaraHomeSchemaFixture.BootstrapPassword,
            },
            Cancellation);

        var body = await login.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        var challenge = body.GetProperty("challenge");

        Assert.Equal(JsonValueKind.Object, challenge.ValueKind);

        using var impostor = CreateClient();
        impostor.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            challenge.GetProperty("challengeToken").GetString());

        // A half-finished sign-in authenticates nothing. The challenge is minted for a different
        // audience precisely so presenting it as a bearer token fails validation.
        var response = await impostor.GetAsync(new Uri("/api/v1/admin/me", UriKind.Relative), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<TestSignIn.Session> SignInAsCustomerAsync(HttpClient client, string? mobile = null)
    {
        var number = mobile ?? NewMobile();

        var request = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/request",
            new { mobile = number },
            Cancellation);

        request.EnsureSuccessStatusCode();

        return await TestSignIn.SignInWithOtpAsync(
            client,
            number,
            Otp.Latest(number, OtpPurpose.Login),
            Cancellation);
    }

    /// <summary>
    /// Signs a new customer in and returns the refresh secret from the <c>Set-Cookie</c> header —
    /// what an attacker who read the cookie would have.
    /// </summary>
    private async Task<string> SignInAndReadRefreshTokenAsync(HttpClient client)
    {
        var mobile = NewMobile();

        var request = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/request",
            new { mobile },
            Cancellation);

        request.EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/verify",
            new { mobile, code = Otp.Latest(mobile, OtpPurpose.Login) },
            Cancellation);

        verify.EnsureSuccessStatusCode();

        var cookie = verify.Headers
            .GetValues("Set-Cookie")
            .Single(header => header.StartsWith("kh_rt=", StringComparison.Ordinal));

        return cookie["kh_rt=".Length..].Split(';')[0];
    }

    private static Task<HttpResponseMessage> PresentRefreshTokenAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/api/v1/store/auth/refresh", UriKind.Relative));

        request.Headers.Add("Cookie", $"kh_rt={token}");
        return client.SendAsync(request, Cancellation);
    }
}
