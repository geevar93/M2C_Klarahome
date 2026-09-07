using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure;

namespace KlaraHome.IntegrationTests.Identity;

/// <summary>
/// The Step 7 acceptance criteria, over HTTP, against the real database: all three actor types
/// authenticate, and the token that comes back says what it should.
/// </summary>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class AuthenticationTests(KlaraHomeSchemaFixture fixture) : IdentityTestBase(fixture)
{
    [Fact]
    public async Task A_customer_registers_and_signs_in_with_a_mobile_number_and_a_one_time_code()
    {
        SkipWithoutDocker();

        // Mobile-OTP sign-in ships off: it was withdrawn from the storefront, not merely left
        // unprovisioned (IdentityFeatures.MobileOtpLogin). The capability is still supported
        // behind the flag, so the test that covers it turns it on rather than assuming a default.
        Features[IdentityFeatures.MobileOtpLogin] = true;

        using var client = CreateClient();
        var mobile = NewMobile();

        var request = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/request",
            new { mobile },
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, request.StatusCode);

        var issued = await request.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal(6, issued.GetProperty("codeLength").GetInt32());
        Assert.Equal(300, issued.GetProperty("expiresInSeconds").GetInt32());

        // The response says nothing about whether the number is registered — that is the point of
        // it. The verification is what registers the customer.
        var session = await TestSignIn.SignInWithOtpAsync(
            client,
            mobile,
            Otp.Latest(mobile, OtpPurpose.Login),
            Cancellation);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/store/me", Cancellation);
        var user = me.GetProperty("user");

        Assert.Equal(session.UserId.ToString(), user.GetProperty("id").GetString());
        Assert.Equal("customer", user.GetProperty("userType").GetString());
        Assert.Equal(mobile, user.GetProperty("mobile").GetString());
        Assert.True(user.GetProperty("mobileVerified").GetBoolean());
        Assert.Equal(["customer"], user.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));

        // A shopper carries no administrative permission at all.
        Assert.Empty(user.GetProperty("permissions").EnumerateArray());

        // Registering created the profile the account page reads.
        Assert.Equal(JsonValueKind.Object, me.GetProperty("profile").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(me.GetProperty("profile").GetProperty("referralCode").GetString()));
    }

    [Fact]
    public async Task The_same_number_signs_the_same_customer_back_in_rather_than_making_a_second_account()
    {
        SkipWithoutDocker();

        // Mobile-OTP sign-in ships off: it was withdrawn from the storefront, not merely left
        // unprovisioned (IdentityFeatures.MobileOtpLogin). The capability is still supported
        // behind the flag, so the test that covers it turns it on rather than assuming a default.
        Features[IdentityFeatures.MobileOtpLogin] = true;

        using var first = CreateClient();
        var mobile = NewMobile();

        await RequestOtpAsync(first, mobile);
        var one = await TestSignIn.SignInWithOtpAsync(first, mobile, Otp.Latest(mobile, OtpPurpose.Login), Cancellation);

        using var second = CreateClient();
        await RequestOtpAsync(second, mobile);
        var two = await TestSignIn.SignInWithOtpAsync(second, mobile, Otp.Latest(mobile, OtpPurpose.Login), Cancellation);

        Assert.Equal(one.UserId, two.UserId);
    }

    [Fact]
    public async Task A_platform_administrator_signs_in_with_a_password_and_a_mandatory_second_factor()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        // A second administrator, so this test owns the account whose first sign-in it asserts:
        // the bootstrap one has an authenticator by the time other tests have run.
        var email = NewEmail("second-admin");
        await CreateStaffAsync(admin, email, "platform-admin");
        await SetPasswordAsync(email, "the-second-administrator-here");

        using var client = CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new { email, password = "the-second-administrator-here" },
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // platform-admin cannot operate without a second factor, so the correct password buys a
        // challenge rather than a session.
        var challenged = await login.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal(JsonValueKind.Null, challenged.GetProperty("accessToken").ValueKind);
        Assert.Equal(
            "two-factor-enrolment",
            challenged.GetProperty("challenge").GetProperty("type").GetString());

        var session = await TestSignIn.SignInAsync(
            client,
            "admin",
            email,
            "the-second-administrator-here",
            Cancellation);

        Assert.Contains("platform.settings.manage", session.Permissions);
        Assert.Contains("identity.user.manage", session.Permissions);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/me", Cancellation);
        var user = me.GetProperty("user");

        Assert.Equal("staff", user.GetProperty("userType").GetString());
        Assert.True(user.GetProperty("twoFactorEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, user.GetProperty("vendorId").ValueKind);
    }

    [Fact]
    public async Task A_second_sign_in_asks_for_a_code_rather_than_a_new_enrolment()
    {
        SkipWithoutDocker();

        using var client = CreateClient();
        await SignInAsAdministratorAsync(client);

        using var again = CreateClient();

        var login = await again.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new
            {
                email = KlaraHomeSchemaFixture.BootstrapEmail,
                password = KlaraHomeSchemaFixture.BootstrapPassword,
            },
            Cancellation);

        var body = await login.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("two-factor", body.GetProperty("challenge").GetProperty("type").GetString());

        // Re-enrolling from a login challenge would let anyone holding the password replace the
        // second factor, which is the whole thing it defends against.
        var reEnrol = await again.PostAsJsonAsync(
            "/api/v1/admin/auth/2fa/enrol",
            new { challengeToken = body.GetProperty("challenge").GetProperty("challengeToken").GetString() },
            Cancellation);

        Assert.Equal(HttpStatusCode.Conflict, reEnrol.StatusCode);

        var problem = await reEnrol.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("IDENTITY_TWO_FACTOR_ALREADY_ENABLED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_wrong_second_factor_code_is_refused()
    {
        SkipWithoutDocker();

        using var client = CreateClient();
        await SignInAsAdministratorAsync(client);

        using var attacker = CreateClient();

        var login = await attacker.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new
            {
                email = KlaraHomeSchemaFixture.BootstrapEmail,
                password = KlaraHomeSchemaFixture.BootstrapPassword,
            },
            Cancellation);

        var body = await login.Content.ReadFromJsonAsync<JsonElement>(Cancellation);

        // Holding the password is not enough. That is the entire point of the second factor.
        var wrong = await attacker.PostAsJsonAsync(
            "/api/v1/admin/auth/2fa/verify",
            new
            {
                challengeToken = body.GetProperty("challenge").GetProperty("challengeToken").GetString(),
                code = "000000",
            },
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var problem = await wrong.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("AUTH_INVALID_CODE", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_vendor_user_signs_in_and_their_token_names_the_seller_they_act_for()
    {
        SkipWithoutDocker();

        var vendorId = Guid.NewGuid();
        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var (client, session) = await CreateVendorOwnerAsync(admin, vendorId);

        using (client)
        {
            Assert.Contains("identity.user.read", session.Permissions);

            var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/me", Cancellation);
            var user = me.GetProperty("user");

            Assert.Equal("vendor", user.GetProperty("userType").GetString());
            Assert.Equal(vendorId.ToString(), user.GetProperty("vendorId").GetString());
            Assert.Contains("vendor-owner", user.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        }
    }

    [Fact]
    public async Task An_administrator_never_learns_the_password_of_an_account_they_create()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var email = NewEmail("ops");

        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new { email, mobile = (string?)null, userType = "Staff", roleCodes = new[] { "operations" } },
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        // No password is chosen on their behalf: they set their own from a reset link, so nobody
        // ever holds a credential that would let them sign in as somebody else.
        Assert.True(Otp.Has(email, OtpPurpose.PasswordReset));

        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("twoFactorEnabled").GetBoolean());
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_address_are_told_apart_by_nobody()
    {
        SkipWithoutDocker();

        using var client = CreateClient();

        var unknown = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new { email = NewEmail("nobody"), password = "some-long-password-here" },
            Cancellation);

        var wrong = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new { email = KlaraHomeSchemaFixture.BootstrapEmail, password = "some-long-password-here" },
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var unknownProblem = await unknown.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        var wrongProblem = await wrong.Content.ReadFromJsonAsync<JsonElement>(Cancellation);

        Assert.Equal("AUTH_INVALID_CREDENTIALS", unknownProblem.GetProperty("code").GetString());
        Assert.Equal("AUTH_INVALID_CREDENTIALS", wrongProblem.GetProperty("code").GetString());
        Assert.Equal(
            unknownProblem.GetProperty("detail").GetString(),
            wrongProblem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_for_a_while()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var email = NewEmail("locked");
        await CreateStaffAsync(admin, email, "operations");
        await SetPasswordAsync(email, "a-perfectly-fine-password");

        using var client = CreateClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await client.PostAsJsonAsync(
                "/api/v1/admin/auth/login",
                new { email, password = "not-the-right-password" },
                Cancellation);

            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        // The sixth attempt is refused before the password is even considered, and says so — a
        // locked account is a state the person can act on, unlike a wrong password.
        var locked = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new { email, password = "a-perfectly-fine-password" },
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);

        var problem = await locked.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("AUTH_ACCOUNT_LOCKED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_one_time_code_is_refused_after_the_attempt_budget_is_spent()
    {
        SkipWithoutDocker();

        // Mobile-OTP sign-in ships off: it was withdrawn from the storefront, not merely left
        // unprovisioned (IdentityFeatures.MobileOtpLogin). The capability is still supported
        // behind the flag, so the test that covers it turns it on rather than assuming a default.
        Features[IdentityFeatures.MobileOtpLogin] = true;

        using var client = CreateClient();
        var mobile = NewMobile();

        await RequestOtpAsync(client, mobile);
        var code = Otp.Latest(mobile, OtpPurpose.Login);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await client.PostAsJsonAsync(
                "/api/v1/store/auth/otp/verify",
                new { mobile, code = "000000" },
                Cancellation);

            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        // The budget is spent, so even the code that was actually sent no longer works.
        var correct = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/verify",
            new { mobile, code },
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
    }

    [Fact]
    public async Task One_number_cannot_be_used_to_send_itself_an_unlimited_number_of_codes()
    {
        SkipWithoutDocker();

        // Mobile-OTP sign-in ships off: it was withdrawn from the storefront, not merely left
        // unprovisioned (IdentityFeatures.MobileOtpLogin). The capability is still supported
        // behind the flag, so the test that covers it turns it on rather than assuming a default.
        Features[IdentityFeatures.MobileOtpLogin] = true;

        using var client = CreateClient();
        var mobile = NewMobile();

        for (var request = 0; request < 3; request++)
        {
            var allowed = await client.PostAsJsonAsync(
                "/api/v1/store/auth/otp/request",
                new { mobile },
                Cancellation);

            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        // The edge limiter counts requests from one address, which does nothing about a botnet
        // spraying one number. This counts requests to one number.
        var throttled = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/request",
            new { mobile },
            Cancellation);

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);

        var problem = await throttled.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("AUTH_OTP_THROTTLED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Asking_for_a_code_again_invalidates_the_previous_one()
    {
        SkipWithoutDocker();

        // Mobile-OTP sign-in ships off: it was withdrawn from the storefront, not merely left
        // unprovisioned (IdentityFeatures.MobileOtpLogin). The capability is still supported
        // behind the flag, so the test that covers it turns it on rather than assuming a default.
        Features[IdentityFeatures.MobileOtpLogin] = true;

        using var client = CreateClient();
        var mobile = NewMobile();

        await RequestOtpAsync(client, mobile);
        var first = Otp.Latest(mobile, OtpPurpose.Login);

        await RequestOtpAsync(client, mobile);
        var second = Otp.Latest(mobile, OtpPurpose.Login);

        Assert.NotEqual(first, second);

        // Two live codes would leave the customer with no way to know which one the server accepts.
        var stale = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/verify",
            new { mobile, code = first },
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);

        var fresh = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/verify",
            new { mobile, code = second },
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Fact]
    public async Task A_forgotten_password_says_the_same_thing_whether_or_not_the_address_is_known()
    {
        SkipWithoutDocker();

        using var client = CreateClient();

        var known = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/password/forgot",
            new { email = KlaraHomeSchemaFixture.BootstrapEmail },
            Cancellation);

        var unknown = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/password/forgot",
            new { email = NewEmail("never-registered") },
            Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, known.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
    }

    [Fact]
    public async Task A_password_reset_signs_every_other_device_out()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var email = NewEmail("resets");
        await CreateStaffAsync(admin, email, "operations");
        await SetPasswordAsync(email, "the-first-password-here");

        using var device = CreateClient();
        await TestSignIn.SignInAsync(device, "admin", email, "the-first-password-here", Cancellation);

        var before = await device.GetAsync(new Uri("/api/v1/admin/me/sessions", UriKind.Relative), Cancellation);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Somebody resetting a password they have lost control of gains nothing if the party who
        // took it keeps a live session.
        await SetPasswordAsync(email, "a-completely-different-one");

        using var refresh = CreateClient();
        var refreshed = await refresh.PostAsync(new Uri("/api/v1/admin/auth/refresh", UriKind.Relative), null, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
    }

    [Fact]
    public async Task A_password_from_the_breach_lists_is_refused_at_registration()
    {
        SkipWithoutDocker();

        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/store/auth/register",
            new { email = NewEmail("weak"), password = "Password123", mobile = (string?)null, marketingConsent = false },
            Cancellation);

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("VALIDATION_FAILED", problem.GetProperty("code").GetString());

        // Field paths reach the client camelCased, as the rest of the JSON contract does.
        Assert.True(problem.GetProperty("errors").TryGetProperty("password", out _));
    }

    [Fact]
    public async Task A_shopper_can_register_with_an_email_address_instead()
    {
        SkipWithoutDocker();

        using var client = CreateClient();
        var email = NewEmail("shopper");

        var response = await client.PostAsJsonAsync(
            "/api/v1/store/auth/register",
            new
            {
                email,
                password = "the-quiet-lamp-post-hums",
                mobile = NewMobile(),
                marketingConsent = true,
            },
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.False(body.GetProperty("user").GetProperty("emailVerified").GetBoolean());

        // A verification link is on its way, unbundled from the sign-up itself.
        Assert.True(Otp.Has(email, OtpPurpose.VerifyEmail));
    }

    [Fact]
    public async Task An_email_address_already_in_use_is_refused_with_a_reason_the_person_can_act_on()
    {
        SkipWithoutDocker();

        using var client = CreateClient();
        var email = NewEmail("twice");
        var body = new { email, password = "the-quiet-lamp-post-hums", mobile = (string?)null, marketingConsent = false };

        var first = await client.PostAsJsonAsync("/api/v1/store/auth/register", body, Cancellation);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/v1/store/auth/register", body, Cancellation);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("IDENTITY_ACCOUNT_EXISTS", problem.GetProperty("code").GetString());
    }

    private static async Task RequestOtpAsync(HttpClient client, string mobile)
    {
        var response = await client.PostAsJsonAsync("/api/v1/store/auth/otp/request", new { mobile }, Cancellation);
        response.EnsureSuccessStatusCode();
    }
}
