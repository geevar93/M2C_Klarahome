using KlaraHome.Modules.Identity.Application.Authentication;
using KlaraHome.Modules.Identity.Application.Validation;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Endpoints;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.Modules.Identity.Infrastructure.Security;
using KlaraHome.Modules.Identity.Infrastructure.Seeding;

namespace KlaraHome.UnitTests.Identity;

/// <summary>Progressive lockout, verification state, and the second-factor lifecycle.</summary>
public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static void Fail(User user, int times)
    {
        for (var attempt = 0; attempt < times; attempt++)
        {
            user.RecordLoginFailed(Now, threshold: 3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(60));
        }
    }

    [Fact]
    public void Failures_below_the_threshold_do_not_lock_the_account()
    {
        var user = User.RegisterCustomer("+919876543210");

        Fail(user, 2);

        Assert.Equal(2, user.FailedAttempts);
        Assert.False(user.IsLockedOut(Now));
    }

    [Fact]
    public void The_lockout_doubles_with_each_failure_past_the_threshold()
    {
        var user = User.RegisterCustomer("+919876543210");

        Fail(user, 3);
        Assert.Equal(Now.AddSeconds(30), user.LockedUntil);

        Fail(user, 1);
        Assert.Equal(Now.AddSeconds(60), user.LockedUntil);

        Fail(user, 1);
        Assert.Equal(Now.AddSeconds(120), user.LockedUntil);
    }

    [Fact]
    public void The_lockout_is_capped_rather_than_permanent()
    {
        var user = User.RegisterCustomer("+919876543210");

        // An attacker who can lock any account for ever by guessing at it has a denial of service,
        // which is worse than the brute-force risk a permanent lock would close.
        Fail(user, 40);

        Assert.Equal(Now.AddMinutes(60), user.LockedUntil);
    }

    [Fact]
    public void A_successful_sign_in_clears_the_counter_and_the_lockout()
    {
        var user = User.RegisterCustomer("+919876543210");
        Fail(user, 5);

        user.RecordLoginSucceeded(Now.AddHours(2));

        Assert.Equal(0, user.FailedAttempts);
        Assert.Null(user.LockedUntil);
        Assert.Equal(Now.AddHours(2), user.LastLoginAt);
    }

    [Fact]
    public void A_lockout_expires_on_its_own()
    {
        var user = User.RegisterCustomer("+919876543210");
        Fail(user, 3);

        Assert.True(user.IsLockedOut(Now.AddSeconds(29)));
        Assert.False(user.IsLockedOut(Now.AddSeconds(31)));
    }

    [Fact]
    public void Changing_an_identifier_discards_the_proof_that_it_was_verified()
    {
        var user = User.RegisterCustomer("+919876543210");
        user.MarkMobileVerified(Now);
        user.SetEmail("someone@example.in");
        user.MarkEmailVerified(Now);

        user.SetMobile("+919000000000");
        user.SetEmail("other@example.in");

        Assert.Null(user.MobileVerifiedAt);
        Assert.Null(user.EmailVerifiedAt);
    }

    [Fact]
    public void Setting_an_identifier_to_its_current_value_keeps_the_verification()
    {
        var user = User.RegisterCustomer("+919876543210");
        user.MarkMobileVerified(Now);

        user.SetMobile("+919876543210");

        Assert.Equal(Now, user.MobileVerifiedAt);
    }

    [Fact]
    public void A_second_factor_is_staged_before_it_takes_effect()
    {
        var user = User.RegisterStaff(UserType.Staff, "staff@example.in");
        user.StageTotpSecret("protected-secret");

        // A secret that took effect the moment it was generated would lock out anyone whose
        // authenticator app failed to save it.
        Assert.False(user.TotpEnabled);

        user.EnableTotp();
        Assert.True(user.TotpEnabled);
    }

    [Fact]
    public void A_second_factor_cannot_be_enabled_without_a_staged_secret()
    {
        var user = User.RegisterStaff(UserType.Staff, "staff@example.in");

        Assert.Throws<InvalidOperationException>(user.EnableTotp);
    }

    [Fact]
    public void Disabling_a_second_factor_removes_the_secret_as_well()
    {
        var user = User.RegisterStaff(UserType.Staff, "staff@example.in");
        user.StageTotpSecret("protected-secret");
        user.EnableTotp();

        user.DisableTotp();

        Assert.False(user.TotpEnabled);
        Assert.Null(user.TotpSecretEncrypted);
    }

    [Fact]
    public void A_shopper_needs_at_least_one_identifier()
        => Assert.Throws<ArgumentException>(() => User.RegisterCustomer(null, null));

    [Fact]
    public void A_staff_factory_refuses_to_make_a_shopper()
        => Assert.Throws<ArgumentException>(
            () => User.RegisterStaff(UserType.Customer, "shopper@example.in"));

    [Fact]
    public void Granting_the_same_role_twice_is_one_grant()
    {
        var user = User.RegisterStaff(UserType.Staff, "staff@example.in");
        var role = Guid.NewGuid();

        user.GrantRole(role);
        user.GrantRole(role);

        Assert.Single(user.Roles);
    }

    [Fact]
    public void The_same_role_in_two_vendors_is_two_grants()
    {
        var user = User.RegisterStaff(UserType.Vendor, "seller@example.in");
        var role = Guid.NewGuid();

        user.GrantRole(role, Guid.NewGuid());
        user.GrantRole(role, Guid.NewGuid());

        Assert.Equal(2, user.Roles.Count);
    }
}

/// <summary>A role's permission set is replaced as a whole, which is how the admin UI edits it.</summary>
public sealed class RoleTests
{
    [Fact]
    public void Setting_permissions_adds_removes_and_de_duplicates()
    {
        var role = Role.Define("warehouse-lead", "Warehouse lead", RoleScope.Platform);

        role.SetPermissions(["a.read", "a.write", "a.read"]);
        Assert.Equal(["a.read", "a.write"], role.Permissions.Select(permission => permission.PermissionCode).Order());

        role.SetPermissions(["a.write", "b.read"]);
        Assert.Equal(["a.write", "b.read"], role.Permissions.Select(permission => permission.PermissionCode).Order());

        role.SetPermissions([]);
        Assert.Empty(role.Permissions);
    }

    [Fact]
    public void Every_system_role_grants_only_permissions_the_catalogue_declares()
    {
        // A role granting a permission nothing enforces reads, on an access review, as an access
        // somebody has.
        foreach (var role in SystemRoles.All)
        {
            Assert.All(
                role.Permissions,
                permission => Assert.True(
                    PermissionCatalog.Contains(permission),
                    $"Role '{role.Code}' grants '{permission}', which is not in the catalogue."));
        }
    }

    [Fact]
    public void The_administrator_role_grants_every_permission()
    {
        var administrator = SystemRoles.All.Single(role => role.Code == SystemRoles.PlatformAdmin);

        Assert.Equal(
            PermissionCatalog.All.Select(permission => permission.Code).Order(),
            administrator.Permissions.Order());
    }

    [Fact]
    public void The_customer_role_grants_nothing_administrative()
    {
        var customer = SystemRoles.All.Single(role => role.Code == SystemRoles.Customer);

        Assert.Empty(customer.Permissions);
        Assert.Equal(RoleScope.Customer, customer.Scope);
    }

    [Fact]
    public void Every_role_the_second_factor_is_mandatory_for_actually_exists()
    {
        // The rule is configuration, and a typo in it would silently make 2FA optional for the
        // role it was meant to protect.
        var mandatory = new KlaraHome.Modules.Identity.Infrastructure.AuthOptions().MandatoryTwoFactorRoles;

        Assert.NotEmpty(mandatory);
        Assert.All(mandatory, code => Assert.Contains(code, SystemRoles.All.Select(role => role.Code)));
    }

    [Fact]
    public void Every_permission_code_is_a_dotted_lowercase_name()
        => Assert.All(PermissionCatalog.All, permission =>
        {
            Assert.Equal(permission.Code.ToLowerInvariant(), permission.Code);
            Assert.Contains(".", permission.Code, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(permission.Group));
            Assert.False(string.IsNullOrWhiteSpace(permission.Description));
        });

    [Fact]
    public void No_permission_is_declared_twice()
        => Assert.Distinct(PermissionCatalog.All.Select(permission => permission.Code));
}

/// <summary>Indian mobile numbers, GSTINs and PIN codes.</summary>
public sealed class IdentityFormatTests
{
    [Theory]
    [InlineData("9876543210", "+919876543210")]
    [InlineData("09876543210", "+919876543210")]
    [InlineData("919876543210", "+919876543210")]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("+91-98765-43210", "+919876543210")]
    [InlineData("  9876543210  ", "+919876543210")]
    [InlineData("6000000000", "+916000000000")]
    public void A_number_typed_any_of_the_usual_ways_normalises_to_one_value(string typed, string expected)
    {
        // Storing them as typed would mean one person holding several accounts and a uniqueness
        // constraint that never fires.
        Assert.True(IndianMobile.TryNormalize(typed, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void A_ten_digit_number_beginning_91_keeps_all_ten_digits()
    {
        // 91 is stripped only from a twelve-digit number. Dropping it from a ten-digit one would
        // silently corrupt every number in the 91 series.
        Assert.True(IndianMobile.TryNormalize("9188776655", out var normalized));
        Assert.Equal("+919188776655", normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("123456789")]
    [InlineData("98765432101")]
    [InlineData("5876543210")]
    [InlineData("0000000000")]
    [InlineData("not a number")]
    public void Anything_that_is_not_an_Indian_mobile_number_is_refused(string? typed)
        => Assert.False(IndianMobile.IsValid(typed));

    [Fact]
    public void Normalising_an_invalid_number_throws_rather_than_guessing()
        => Assert.Throws<ArgumentException>(() => IndianMobile.Normalize("123"));

    [Theory]
    [InlineData("27AAPFU0939F1ZV", true)]
    [InlineData("29AAGCB7383J1Z4", true)]
    [InlineData("27AAPFU0939F1Z", false)]
    [InlineData("27AAPFU0939F1AV", false)]
    [InlineData("AA27PFU0939F1ZV", false)]
    [InlineData("", false)]
    public void A_GSTIN_is_checked_for_shape(string candidate, bool valid)
        => Assert.Equal(valid, IdentityFormats.Gstin().IsMatch(candidate));

    [Theory]
    [InlineData("400001", true)]
    [InlineData("110001", true)]
    [InlineData("040001", false)]
    [InlineData("40001", false)]
    [InlineData("4000012", false)]
    public void A_PIN_code_is_six_digits_and_never_starts_with_zero(string candidate, bool valid)
        => Assert.Equal(valid, IdentityFormats.Pincode().IsMatch(candidate));
}

/// <summary>What the audit trail and the account screen are allowed to show.</summary>
public sealed class MaskingTests
{
    [Theory]
    [InlineData("+919876543210", "***3210")]
    [InlineData("1234", "***")]
    [InlineData("", "")]
    [InlineData("founder@klarahome.in", "fo***@klarahome.in")]
    [InlineData("a@b.in", "a***@b.in")]
    public void An_identifier_in_the_audit_trail_is_recognisable_but_not_a_contact_list(
        string identifier,
        string expected)
        => Assert.Equal(expected, SignInCoordinator.Mask(identifier));

    [Theory]
    [InlineData("203.0.113.42", "203.0.113.x")]
    [InlineData("2001:db8:85a3:0:0:8a2e:370:7334", "2001:db8:85a3:x")]
    [InlineData("::1", "::x")]
    [InlineData(null, null)]
    public void An_address_in_the_session_list_is_narrowed_before_it_is_shown(string? address, string? expected)
        => Assert.Equal(expected, GetSessionsQueryHandler.MaskIp(address));

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/131.0 Safari/537.36", "Chrome on Windows")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0) Version/18.0 Mobile Safari/604.1", "Safari on iPhone")]
    [InlineData("Mozilla/5.0 (Linux; Android 15) Chrome/131.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) Chrome/131.0 Safari/537.36 Edg/131.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) Firefox/133.0", "Firefox on Linux")]
    [InlineData("curl/8.5.0", "Browser on unknown device")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_device_description_is_something_a_person_recognises(string? userAgent, string? expected)
        => Assert.Equal(expected, AuthCookies.Describe(userAgent));
}

/// <summary>The offline floor under the password policy.</summary>
public sealed class BreachedPasswordTests
{
    private readonly BreachedPasswords _breached = new();

    [Theory]
    [InlineData("password")]
    [InlineData("PASSWORD")]
    [InlineData("qwerty123")]
    [InlineData("letmein")]
    [InlineData("klarahome123")]
    [InlineData("india123")]
    public void A_password_from_the_top_of_every_dump_is_refused(string password)
        => Assert.True(_breached.IsBreached(password));

    [Theory]
    [InlineData("dragon2024")]
    [InlineData("welcome2026!")]
    [InlineData("shadow99")]
    public void Appending_a_year_does_not_rescue_it(string password)
        => Assert.True(_breached.IsBreached(password));

    [Theory]
    [InlineData("correct-horse-battery-staple")]
    [InlineData("the-lamp-post-eats-quietly")]
    [InlineData("")]
    [InlineData(null)]
    public void A_password_nobody_has_guessed_yet_is_allowed(string? password)
        => Assert.False(_breached.IsBreached(password));
}
