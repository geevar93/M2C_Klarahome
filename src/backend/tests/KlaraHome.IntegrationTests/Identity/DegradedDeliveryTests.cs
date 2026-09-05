using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure;

namespace KlaraHome.IntegrationTests.Identity;

/// <summary>
/// What the product does with no SMS gateway and no transactional email provider (ADR-014).
/// </summary>
/// <remarks>
/// The other half of the Step 7A acceptance criterion. Every feature that needs a paid provider
/// answers <c>404 FEATURE_DISABLED</c> while its flag is off and works again when it is on, and
/// nothing that does not need one is affected.
/// </remarks>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class DegradedDeliveryTests(KlaraHomeSchemaFixture fixture) : IdentityTestBase(fixture)
{
    [Fact]
    public async Task The_mobile_OTP_endpoints_disappear_when_SMS_is_off()
    {
        SkipWithoutDocker();

        Features[IdentityFeatures.MobileOtpLogin] = false;

        using var client = CreateClient();

        foreach (var route in new[] { "/api/v1/store/auth/otp/request", "/api/v1/store/auth/otp/verify" })
        {
            var response = await client.PostAsJsonAsync(
                route,
                new { mobile = NewMobile(), code = "123456" },
                Cancellation);

            // 404, not 403: a feature that is switched off has no resource to talk about, and 403
            // would tell an anonymous caller the endpoint exists.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
            Assert.Equal("FEATURE_DISABLED", problem.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task The_password_reset_endpoints_disappear_when_email_is_off()
    {
        SkipWithoutDocker();

        Features[IdentityFeatures.PasswordResetEmail] = false;

        using var client = CreateClient();

        var forgot = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/password/forgot",
            new { email = KlaraHomeSchemaFixture.BootstrapEmail },
            Cancellation);

        var reset = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/password/reset",
            new { email = KlaraHomeSchemaFixture.BootstrapEmail, token = "x", newPassword = "the-lamp-post-hums-here" },
            Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, forgot.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
    }

    [Fact]
    public async Task Email_verification_disappears_but_the_mobile_channel_is_judged_separately()
    {
        SkipWithoutDocker();

        // One route serves two channels, each needing a different paid provider, so they have to
        // be able to go dark independently.
        Features[IdentityFeatures.EmailVerification] = false;
        Features[IdentityFeatures.MobileOtpLogin] = true;

        var mobile = NewMobile();
        using var client = CreateClient();
        await SignInAsCustomerAsync(client, mobile);

        var email = await client.PostAsJsonAsync(
            "/api/v1/store/me/verify/request",
            new { channel = nameof(OtpChannel.Email) },
            Cancellation);

        var sms = await client.PostAsJsonAsync(
            "/api/v1/store/me/verify/request",
            new { channel = nameof(OtpChannel.Sms) },
            Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, email.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, sms.StatusCode);
    }

    [Fact]
    public async Task Registration_still_works_with_email_off_and_simply_leaves_the_address_unverified()
    {
        SkipWithoutDocker();

        Features[IdentityFeatures.EmailVerification] = false;

        using var client = CreateClient();
        var email = NewEmail("no-verification");

        var response = await client.PostAsJsonAsync(
            "/api/v1/store/auth/register",
            new { email, password = "the-quiet-lamp-post-hums", mobile = (string?)null, marketingConsent = false },
            Cancellation);

        // Refusing somebody an account because we cannot send them an email would be the wrong
        // trade: unverified is a state the model already has.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.False(body.GetProperty("user").GetProperty("emailVerified").GetBoolean());
        Assert.False(Otp.Has(email, OtpPurpose.VerifyEmail));
    }

    [Fact]
    public async Task Creating_a_staff_account_with_email_off_says_the_password_is_still_to_be_set()
    {
        SkipWithoutDocker();

        Features[IdentityFeatures.PasswordResetEmail] = false;

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var email = NewEmail("no-link");

        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new { email, mobile = (string?)null, userType = "Staff", roleCodes = new[] { "operations" } },
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation);

        // No link was sent, and the response says the account cannot sign in yet rather than
        // leaving the administrator to discover it when the person calls.
        Assert.False(Otp.Has(email, OtpPurpose.PasswordReset));
        Assert.True(body.GetProperty("passwordSetupPending").GetBoolean());
    }

    [Fact]
    public async Task An_administrator_recovers_a_locked_out_colleague_with_a_temporary_password()
    {
        SkipWithoutDocker();

        Features[IdentityFeatures.PasswordResetEmail] = false;

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var email = NewEmail("stranded");
        var userId = await CreateStaffAsync(admin, email, "operations");

        const string Temporary = "a-temporary-one-for-now";
        const string Chosen = "the-one-they-choose-themselves";

        var issued = await admin.PutAsJsonAsync(
            $"/api/v1/admin/users/{userId}/password",
            new { temporaryPassword = Temporary },
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);

        var issuedBody = await issued.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.True(issuedBody.GetProperty("mustChangePassword").GetBoolean());

        using var colleague = CreateClient();

        var login = await colleague.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new { email, password = Temporary },
            Cancellation);

        // The temporary password buys a challenge, not a session. That is what stops it from being
        // a credential two people hold indefinitely.
        var challenged = await login.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal(JsonValueKind.Null, challenged.GetProperty("accessToken").ValueKind);
        Assert.Equal(
            "password-change-required",
            challenged.GetProperty("challenge").GetProperty("type").GetString());

        var changed = await colleague.PostAsJsonAsync(
            "/api/v1/admin/auth/password/change",
            new
            {
                challengeToken = challenged.GetProperty("challenge").GetProperty("challengeToken").GetString(),
                currentPassword = Temporary,
                newPassword = Chosen,
            },
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var session = await changed.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("accessToken").GetString()));

        // And the temporary password is now worth nothing.
        using var stale = CreateClient();
        var replay = await stale.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new { email, password = Temporary },
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Issuing_a_temporary_password_ends_the_account_s_sessions()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var email = NewEmail("evicted");
        var userId = await CreateStaffAsync(admin, email, "operations");
        await SetPasswordAsync(email, "the-password-they-had");

        using var colleague = CreateClient();
        await TestSignIn.SignInAsync(colleague, "admin", email, "the-password-they-had", Cancellation);

        var before = await colleague.PostAsync(
            new Uri("/api/v1/admin/auth/refresh", UriKind.Relative),
            null,
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await admin.PutAsJsonAsync(
            $"/api/v1/admin/users/{userId}/password",
            new { temporaryPassword = "a-temporary-one-for-now" },
            Cancellation);

        // Whoever the old password reached is signed out too, which is the point of issuing one.
        var after = await colleague.PostAsync(
            new Uri("/api/v1/admin/auth/refresh", UriKind.Relative),
            null,
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task A_temporary_password_still_has_to_meet_the_password_policy()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var userId = await CreateStaffAsync(admin, NewEmail("weak-temp"), "operations");

        // "Temporary" is not a reason to accept a password from every breach list: it is a real
        // credential for as long as it lives.
        var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/users/{userId}/password",
            new { temporaryPassword = "Password123" },
            Cancellation);

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
    }

    [Fact]
    public async Task An_administrator_cannot_issue_themselves_one()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        var session = await SignInAsAdministratorAsync(admin);

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/users/{session.UserId}/password",
            new { temporaryPassword = "a-temporary-one-for-now" },
            Cancellation);

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("IDENTITY_CANNOT_SELF_ISSUE", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_signed_in_user_can_change_their_own_password_without_a_challenge()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var email = NewEmail("self-service");
        await CreateStaffAsync(admin, email, "operations");
        await SetPasswordAsync(email, "the-password-they-had");

        using var person = CreateClient();
        await TestSignIn.SignInAsync(person, "admin", email, "the-password-they-had", Cancellation);

        var changed = await person.PostAsJsonAsync(
            "/api/v1/admin/auth/password/change",
            new
            {
                challengeToken = (string?)null,
                currentPassword = "the-password-they-had",
                newPassword = "the-one-they-chose-instead",
            },
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        using var again = CreateClient();
        await TestSignIn.SignInAsync(again, "admin", email, "the-one-they-chose-instead", Cancellation);
    }

    [Fact]
    public async Task Changing_a_password_requires_the_current_one()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var email = NewEmail("wrong-current");
        await CreateStaffAsync(admin, email, "operations");
        await SetPasswordAsync(email, "the-password-they-had");

        using var person = CreateClient();
        await TestSignIn.SignInAsync(person, "admin", email, "the-password-they-had", Cancellation);

        // A borrowed session must not be enough to replace the password on it.
        var response = await person.PostAsJsonAsync(
            "/api/v1/admin/auth/password/change",
            new
            {
                challengeToken = (string?)null,
                currentPassword = "not-the-password-they-had",
                newPassword = "the-one-they-chose-instead",
            },
            Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Turning_a_flag_back_on_returns_the_feature_with_no_deploy()
    {
        SkipWithoutDocker();

        Features[IdentityFeatures.MobileOtpLogin] = false;

        using var client = CreateClient();
        var mobile = NewMobile();

        var off = await client.PostAsJsonAsync("/api/v1/store/auth/otp/request", new { mobile }, Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, off.StatusCode);

        // The whole point of a flag rather than a build-time switch: the code behind it is the code
        // that was written and tested at Step 7, and it comes back on the next request.
        Features[IdentityFeatures.MobileOtpLogin] = true;

        var on = await client.PostAsJsonAsync("/api/v1/store/auth/otp/request", new { mobile }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
    }

    [Fact]
    public async Task Nothing_from_Step_7_behaves_differently_while_the_flags_are_on()
    {
        SkipWithoutDocker();

        // The flags ship on, so the default path is the one Step 7 shipped. This is the regression
        // guard for "I do not want any of the existing functionality disrupted".
        using var client = CreateClient();
        var mobile = NewMobile();

        var request = await client.PostAsJsonAsync("/api/v1/store/auth/otp/request", new { mobile }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);

        var session = await TestSignIn.SignInWithOtpAsync(
            client,
            mobile,
            Otp.Latest(mobile, OtpPurpose.Login),
            Cancellation);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/store/me", Cancellation);

        Assert.Equal(session.UserId.ToString(), me.GetProperty("user").GetProperty("id").GetString());
        Assert.True(me.GetProperty("user").GetProperty("mobileVerified").GetBoolean());
    }

    private async Task SignInAsCustomerAsync(HttpClient client, string mobile)
    {
        var request = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/request",
            new { mobile },
            Cancellation);

        request.EnsureSuccessStatusCode();

        await TestSignIn.SignInWithOtpAsync(client, mobile, Otp.Latest(mobile, OtpPurpose.Login), Cancellation);
    }
}
