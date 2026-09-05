using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Identity.Domain;

namespace KlaraHome.IntegrationTests.Identity;

/// <summary>
/// The host, the captured codes, and the account-creation steps every Identity test needs.
/// </summary>
/// <remarks>
/// Accounts are created through the API rather than written into the database, so a test that
/// depends on a vendor owner existing also depends on the endpoint that creates one being correct.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public abstract class IdentityTestBase(KlaraHomeSchemaFixture fixture) : IDisposable
{
    private IdentityApiFactory? _factory;
    private bool _disposed;

    /// <summary>The codes and links the host has sent.</summary>
    internal CapturingOtpDispatcher Otp => Factory.Otp;

    /// <summary>The ambient cancellation token, so a hung test fails rather than hanging.</summary>
    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private IdentityApiFactory Factory => _factory ??= new IdentityApiFactory(fixture.ConnectionString);

    /// <summary>Skips the test when Docker is unavailable, rather than failing on the environment.</summary>
    protected void SkipWithoutDocker()
        => Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

    /// <summary>A client against the host under test.</summary>
    protected HttpClient CreateClient() => Factory.CreateClient();

    /// <summary>A mobile number no other test is using.</summary>
    /// <remarks>
    /// The collection shares one database, and a number is unique per tenant, so a fixed number
    /// would make the second test that used it fail for a reason unrelated to what it asserts.
    /// </remarks>
    protected static string NewMobile()
        => "+919" + Random.Shared.Next(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>An email address no other test is using.</summary>
    /// <param name="prefix">A readable hint about which test made it.</param>
    protected static string NewEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@klarahome.test";

    /// <summary>Signs a client in as the deployment's first administrator, enrolling 2FA if needed.</summary>
    /// <param name="client">The client to sign in.</param>
    protected static Task<TestSignIn.Session> SignInAsAdministratorAsync(HttpClient client)
        => TestSignIn.SignInAsync(
            client,
            "admin",
            KlaraHomeSchemaFixture.BootstrapEmail,
            KlaraHomeSchemaFixture.BootstrapPassword,
            Cancellation);

    /// <summary>Creates a staff account through the admin API.</summary>
    /// <param name="admin">A signed-in administrator.</param>
    /// <param name="email">The new account's email address.</param>
    /// <param name="roleCode">The role to grant.</param>
    protected async Task<Guid> CreateStaffAsync(HttpClient admin, string email, string roleCode)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new { email, mobile = (string?)null, userType = "Staff", roleCodes = new[] { roleCode } },
            Cancellation);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        return Guid.Parse(body.GetProperty("id").GetString()!);
    }

    /// <summary>
    /// Sets an account's password using the reset link it was sent, which is the only way a new
    /// account gets one.
    /// </summary>
    /// <param name="email">The account.</param>
    /// <param name="password">The password to set.</param>
    protected async Task SetPasswordAsync(string email, string password)
    {
        using var client = CreateClient();

        // Always a fresh link. A reset token is single use, so reusing the one the account was
        // created with would work exactly once and then fail for a reason unrelated to the test.
        var forgot = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/password/forgot",
            new { email },
            Cancellation);

        forgot.EnsureSuccessStatusCode();

        var reset = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/password/reset",
            new { email, token = Otp.Latest(email, OtpPurpose.PasswordReset), newPassword = password },
            Cancellation);

        reset.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Creates a vendor owner for a seller, sets their password, and signs them in.
    /// </summary>
    /// <param name="admin">A signed-in platform administrator.</param>
    /// <param name="vendorId">The seller they own.</param>
    protected async Task<(HttpClient Client, TestSignIn.Session Session)> CreateVendorOwnerAsync(
        HttpClient admin,
        Guid vendorId)
    {
        var email = NewEmail("owner");
        const string Password = "the-seller-signs-in-here";

        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new
            {
                email,
                mobile = (string?)null,
                userType = "Vendor",
                roleCodes = new[] { "vendor-owner" },
                vendorId,
            },
            Cancellation);

        created.EnsureSuccessStatusCode();
        await SetPasswordAsync(email, Password);

        var client = CreateClient();
        var session = await TestSignIn.SignInAsync(client, "admin", email, Password, Cancellation);

        return (client, session);
    }

    /// <summary>Creates a vendor staff account inside a seller, without signing them in.</summary>
    /// <param name="admin">A signed-in administrator, or the seller's own owner.</param>
    /// <param name="vendorId">The seller. Ignored when the caller is themselves a vendor user.</param>
    protected async Task<Guid> CreateVendorStaffAsync(HttpClient admin, Guid? vendorId)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new
            {
                email = NewEmail("staff"),
                mobile = (string?)null,
                userType = "Vendor",
                roleCodes = new[] { "vendor-staff" },
                vendorId,
            },
            Cancellation);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        return Guid.Parse(body.GetProperty("id").GetString()!);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes the host this test class started.</summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _factory?.Dispose();
        }

        _disposed = true;
    }
}
