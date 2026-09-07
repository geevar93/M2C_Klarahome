using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The host, the boundary fakes and the sign-ins every Step 29 commerce test needs.
/// </summary>
/// <remarks>
/// One factory per test class rather than per test: booting the host is the expensive half and the
/// database underneath it is shared by the collection anyway, so a per-test host would pay the cost
/// without buying any isolation. Tests keep themselves apart by creating their own data — a fresh
/// seller, a fresh product, a fresh shopper — which is also what makes them safe to read in
/// isolation later.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
[Collection(KlaraHomeSchema.CollectionName)]
public abstract class CommerceTestBase(KlaraHomeSchemaFixture fixture) : IDisposable
{
    private CommerceApiFactory? _factory;
    private Sql? _database;
    private bool _disposed;

    /// <summary>The ambient cancellation token, so a hung test fails rather than hanging.</summary>
    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The host under test, with its boundary fakes.</summary>
    internal CommerceApiFactory Factory => _factory ??= new CommerceApiFactory(fixture.ConnectionString);

    /// <summary>The database behind that host, for the assertions the API cannot make.</summary>
    internal Sql Database => _database ??= new Sql(fixture.ConnectionString);

    /// <summary>A scenario builder driving the given administrator.</summary>
    /// <param name="admin">A client signed in as platform staff.</param>
    internal static VendorScenario Sellers(HttpClient admin) => new(admin, Cancellation);

    /// <summary>
    /// A second host over the same database, for the tests that need two of them.
    /// </summary>
    /// <remarks>
    /// Key rotation is the case that needs it: proving that a row written under the previous key
    /// still decrypts means standing up a host whose <em>current</em> key is the next one, and a
    /// setting read at start-up cannot be changed on a host that is already running.
    /// </remarks>
    internal CommerceApiFactory NewFactory() => new(fixture.ConnectionString);

    /// <summary>Skips the test when Docker is unavailable, rather than failing on the environment.</summary>
    protected void SkipWithoutDocker()
        => Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

    /// <summary>An anonymous client against the host under test.</summary>
    protected HttpClient CreateClient() => Factory.CreateClient();

    /// <summary>A client signed in as the deployment's first administrator.</summary>
    /// <remarks>
    /// Through the real sign-in, second factor included, so the flow is proved by every test that
    /// depends on it rather than by one test that could be deleted.
    /// </remarks>
    protected async Task<HttpClient> SignedInAdministratorAsync()
    {
        var client = CreateClient();

        await TestSignIn.SignInAsync(
            client,
            "admin",
            KlaraHomeSchemaFixture.BootstrapEmail,
            KlaraHomeSchemaFixture.BootstrapPassword,
            Cancellation);

        return client;
    }

    /// <summary>
    /// A client signed in as a shopper who has never been here before.
    /// </summary>
    /// <remarks>
    /// Registered by the mobile OTP flow, which is how a storefront customer account actually comes
    /// into existence — there is no other route that produces one with a verified mobile number,
    /// and several of the behaviours under test key off exactly that.
    /// </remarks>
    /// <param name="mobile">The number, or null for one nothing else is using.</param>
    protected async Task<(HttpClient Client, string Mobile)> SignedInShopperAsync(string? mobile = null)
    {
        var client = CreateClient();
        var number = mobile ?? NewMobile();

        var start = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/start",
            new { mobile = number },
            Cancellation);

        start.EnsureSuccessStatusCode();

        var code = Factory.Otp.Latest(number, Modules.Identity.Domain.OtpPurpose.Login);

        await TestSignIn.SignInWithOtpAsync(client, number, code, Cancellation);

        return (client, number);
    }

    /// <summary>
    /// A client signed in as the owner of a given seller, and the session they got.
    /// </summary>
    /// <remarks>
    /// The scope that matters comes off the token's <c>vendorId</c> claim, not off the path, which
    /// is why the account is created with the seller on it rather than being told which seller to
    /// act as afterwards. Sign-in is the real one, second factor included.
    /// </remarks>
    /// <param name="admin">A client signed in as platform staff.</param>
    /// <param name="vendorId">The seller they own.</param>
    protected async Task<(HttpClient Client, TestSignIn.Session Session)> SignedInVendorOwnerAsync(
        HttpClient admin,
        Guid vendorId)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var email = NewEmail("owner");
        const string Password = "the-seller-signs-in-here";

        await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new
            {
                email,
                mobile = (string?)null,
                userType = "Vendor",
                roleCodes = new[] { "vendor-owner" },
                vendorId,
            },
            Cancellation));

        await SetPasswordAsync(email, Password);

        var client = CreateClient();
        var session = await TestSignIn.SignInAsync(client, "admin", email, Password, Cancellation);

        return (client, session);
    }

    /// <summary>Sets an account's password through the reset flow, as a new joiner would.</summary>
    /// <remarks>
    /// Always a fresh link: a reset token is single use, so reusing the one an account was created
    /// with works exactly once and then fails for a reason unrelated to the test.
    /// </remarks>
    /// <param name="email">The account.</param>
    /// <param name="password">What to set it to.</param>
    protected async Task SetPasswordAsync(string email, string password)
    {
        using var client = CreateClient();

        await ReadAsync(await client.PostAsJsonAsync(
            "/api/v1/admin/auth/password/forgot",
            new { email },
            Cancellation));

        await ReadAsync(await client.PostAsJsonAsync(
            "/api/v1/admin/auth/password/reset",
            new
            {
                email,
                token = Factory.Otp.Latest(email, Modules.Identity.Domain.OtpPurpose.PasswordReset),
                newPassword = password,
            },
            Cancellation));
    }

    /// <summary>Runs one pass of a hosted background service, rather than waiting for its timer.</summary>
    /// <remarks>
    /// The processors are registered but disabled in this host. A test that needs one asks for a
    /// single pass and asserts on what it did — which proves the same behaviour as the timer would,
    /// and proves it the same way every run.
    /// </remarks>
    /// <typeparam name="TService">The service to resolve.</typeparam>
    /// <param name="pass">What one pass of it means.</param>
    protected async Task RunOnceAsync<TService>(Func<TService, CancellationToken, Task> pass)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(pass);

        using var scope = Factory.Services.CreateScope();
        await pass(scope.ServiceProvider.GetRequiredService<TService>(), Cancellation);
    }

    /// <summary>An email address no other test is using.</summary>
    /// <param name="prefix">A readable hint about which test made it.</param>
    protected static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@klarahome.test";

    /// <summary>An Indian mobile number no other test is using.</summary>
    protected static string NewMobile()
        => $"9{Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>Reads a successful response as JSON, failing with the body when it was not successful.</summary>
    /// <param name="response">The response.</param>
    protected static Task<JsonElement> ReadAsync(HttpResponseMessage response)
        => Rest.ReadAsync(response, Cancellation);

    /// <summary>Asserts a response failed with a given status and error code, and returns the problem.</summary>
    /// <param name="response">The response.</param>
    /// <param name="status">The status it must carry.</param>
    /// <param name="code">The stable error code it must name, or null to accept any.</param>
    protected static Task<JsonElement> RefusedAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string? code = null)
        => Rest.RefusedAsync(response, status, code, Cancellation);

    /// <summary>Sends a body exactly as written, which <c>PostAsJsonAsync</c> cannot.</summary>
    /// <param name="client">The client to send on.</param>
    /// <param name="path">The path.</param>
    /// <param name="rawBody">The body, exactly as it should arrive.</param>
    /// <param name="headers">Headers to set on the request.</param>
    protected static Task<HttpResponseMessage> PostRawAsync(
        HttpClient client,
        string path,
        string rawBody,
        params (string Name, string Value)[] headers)
        => Rest.PostRawAsync(client, path, rawBody, Cancellation, headers);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _factory?.Dispose();
        GC.SuppressFinalize(this);
    }
}
