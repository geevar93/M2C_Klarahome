using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.Modules.Identity.Infrastructure.Security;

namespace KlaraHome.IntegrationTests.Database;

/// <summary>
/// Signs a client in over HTTP, through the endpoints a person would use.
/// </summary>
/// <remarks>
/// Deliberately not a shortcut that mints a token from the container. Every test that needs an
/// authenticated caller therefore exercises the real sign-in — including the mandatory second
/// factor — so the flow is proved by every test that depends on it rather than by one test that
/// could be deleted.
/// </remarks>
public static class TestSignIn
{
    /// <summary>
    /// The authenticator secrets these tests have enrolled, by email address.
    /// </summary>
    /// <remarks>
    /// A second factor can only be enrolled once, and the server never hands the secret back after
    /// that — which is the point of it. A test that signs the same account in twice therefore has to
    /// remember what it enrolled the first time, exactly as the person would have to keep their
    /// phone.
    /// </remarks>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Authenticators =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a completed sign-in gives a test.</summary>
    /// <param name="AccessToken">The bearer token.</param>
    /// <param name="UserId">The signed-in user.</param>
    /// <param name="Permissions">The permissions the token carries.</param>
    public sealed record Session(string AccessToken, Guid UserId, IReadOnlyList<string> Permissions);

    /// <summary>
    /// Signs in with an email and password, answering a two-factor challenge if one comes back.
    /// </summary>
    /// <param name="client">The client to sign in and to attach the token to.</param>
    /// <param name="surface">The surface prefix: <c>store</c> or <c>admin</c>.</param>
    /// <param name="email">The email address.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Session> SignInAsync(
        HttpClient client,
        string surface,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var login = await client.PostAsJsonAsync(
            $"/api/v1/{surface}/auth/login",
            new { email, password },
            cancellationToken);

        login.EnsureSuccessStatusCode();

        var body = await login.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        if (body.TryGetProperty("challenge", out var challenge) && challenge.ValueKind != JsonValueKind.Null)
        {
            body = await AnswerTwoFactorAsync(client, surface, email, challenge, cancellationToken);
        }

        return Attach(client, body);
    }

    /// <summary>Signs in with a mobile number and the OTP that was just written to the log.</summary>
    /// <param name="client">The client to sign in.</param>
    /// <param name="mobile">The mobile number.</param>
    /// <param name="code">The code, read from the dispatcher.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Session> SignInWithOtpAsync(
        HttpClient client,
        string mobile,
        string code,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var verify = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/verify",
            new { mobile, code },
            cancellationToken);

        verify.EnsureSuccessStatusCode();

        var body = await verify.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return Attach(client, body);
    }

    /// <summary>
    /// Answers a two-factor challenge, enrolling an authenticator first if the account has none.
    /// </summary>
    private static async Task<JsonElement> AnswerTwoFactorAsync(
        HttpClient client,
        string surface,
        string email,
        JsonElement challenge,
        CancellationToken cancellationToken)
    {
        var token = challenge.GetProperty("challengeToken").GetString()!;
        string secret;

        if (challenge.GetProperty("type").GetString() == "two-factor-enrolment")
        {
            var enrol = await client.PostAsJsonAsync(
                $"/api/v1/{surface}/auth/2fa/enrol",
                new { challengeToken = token },
                cancellationToken);

            enrol.EnsureSuccessStatusCode();

            var setup = await enrol.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            secret = setup.GetProperty("secret").GetString()!;
            Authenticators[email] = secret;
        }
        else if (!Authenticators.TryGetValue(email, out secret!))
        {
            throw new InvalidOperationException(
                $"'{email}' already has an authenticator enrolled and this process did not enrol it, so its "
                + "secret cannot be recovered. Sign in with a freshly created account.");
        }

        var verify = await client.PostAsJsonAsync(
            $"/api/v1/{surface}/auth/2fa/verify",
            new { challengeToken = token, code = Totp.Compute(secret, DateTimeOffset.UtcNow) },
            cancellationToken);

        verify.EnsureSuccessStatusCode();
        return await verify.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static Session Attach(HttpClient client, JsonElement body)
    {
        var accessToken = body.GetProperty("accessToken").GetString()
                          ?? throw new InvalidOperationException(
                              "The sign-in did not complete: the response carries a challenge, not a token.");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var user = body.GetProperty("user");

        return new Session(
            accessToken,
            Guid.Parse(user.GetProperty("id").GetString()!, CultureInfo.InvariantCulture),
            [.. user.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString()!)]);
    }
}
