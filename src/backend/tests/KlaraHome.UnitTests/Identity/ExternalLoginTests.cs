using System.Text;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure;
using KlaraHome.Modules.Identity.Infrastructure.External;
using KlaraHome.Modules.Identity.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace KlaraHome.UnitTests.Identity;

/// <summary>PKCE, RFC 7636 — what stops a stolen authorization code from being redeemable.</summary>
public sealed class PkceTests
{
    [Fact]
    public void A_verifier_is_within_the_length_the_RFC_allows()
    {
        var verifier = Pkce.NewVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.All(verifier, character =>
            Assert.True(char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~'));
    }

    [Fact]
    public void Two_verifiers_are_never_the_same()
        => Assert.Distinct(Enumerable.Range(0, 100).Select(_ => Pkce.NewVerifier()).ToList());

    [Fact]
    public void The_challenge_matches_the_S256_definition()
    {
        // RFC 7636 appendix B: this verifier hashes to this challenge.
        const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        Assert.Equal(Challenge, Pkce.ChallengeFor(Verifier));
    }

    [Fact]
    public void The_challenge_is_a_hash_and_not_the_verifier()
    {
        var verifier = Pkce.NewVerifier();

        // Only the hash travels to the provider. If these were equal, an attacker who saw the
        // authorization request would hold everything needed to redeem the code.
        Assert.NotEqual(verifier, Pkce.ChallengeFor(verifier));
    }

    [Fact]
    public void A_state_is_unguessable_and_unique()
    {
        var states = Enumerable.Range(0, 100).Select(_ => Pkce.NewState()).ToList();

        Assert.Distinct(states);
        Assert.All(states, state => Assert.True(state.Length >= 43));
    }
}

/// <summary>The open-redirect guard on <c>returnUrl</c>.</summary>
public sealed class ReturnUrlTests
{
    private static readonly ExternalAuthOptions Options = new()
    {
        DefaultReturnUrl = "https://shop.example.in/",
        AllowedReturnUrls = ["https://shop.example.in", "https://www.shop.example.in"],
    };

    [Fact]
    public void No_return_url_falls_back_to_the_configured_default()
    {
        Assert.True(ExternalLoginService.TryResolveReturnUrl(null, Options, out var resolved));
        Assert.Equal("https://shop.example.in/", resolved);
    }

    [Theory]
    [InlineData("/account")]
    [InlineData("/checkout?step=2")]
    [InlineData("https://shop.example.in/account")]
    [InlineData("https://www.shop.example.in/")]
    public void A_relative_path_or_an_allowed_origin_is_accepted(string candidate)
    {
        Assert.True(ExternalLoginService.TryResolveReturnUrl(candidate, Options, out var resolved));
        Assert.Equal(candidate, resolved);
    }

    [Theory]
    [InlineData("https://evil.example.com/")]
    [InlineData("https://shop.example.in.evil.com/")]
    [InlineData("//evil.example.com/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http:evil")]
    public void Anything_else_is_refused(string candidate)
    {
        // An open redirect on the endpoint that has just issued a session is a phishing primitive,
        // so a rejected returnUrl fails the sign-in rather than quietly falling back to the default.
        Assert.False(ExternalLoginService.TryResolveReturnUrl(candidate, Options, out _));
    }

    [Fact]
    public void A_protocol_relative_url_is_not_mistaken_for_a_relative_path()
    {
        // "//evil.com" starts with a slash and goes to another origin. It is the single most
        // common way an open-redirect check written as StartsWith("/") is defeated.
        Assert.False(ExternalLoginService.TryResolveReturnUrl("//evil.example.com/x", Options, out _));
    }

    [Fact]
    public void The_callback_uri_is_built_from_configuration_and_not_from_the_request()
    {
        var options = new ExternalAuthOptions { CallbackBaseUrl = "https://api.example.in/" };

        // A forwarded Host header is attacker-controlled, and a redirect URI has to match the one
        // registered with the provider exactly.
        Assert.Equal(
            "https://api.example.in/api/v1/store/auth/external/google/callback",
            ExternalLoginService.CallbackUriFor(ExternalProvider.Google, options));
    }
}

/// <summary>The encrypted cookie that carries a sign-in between the two redirects.</summary>
public sealed class ExternalLoginStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static ExternalLoginStateCookie Cookie(DateTimeOffset? now = null)
    {
        var options = Options.Create(new AuthOptions
        {
            Encryption = new EncryptionOptions
            {
                CurrentKeyId = "k1",
                Keys = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["k1"] = "a2xhcmFob21lLWludGVncmF0aW9uLXRlc3RzLWtleSE=",
                },
            },
            External = new ExternalAuthOptions { StateLifetimeMinutes = 10 },
        });

        return new ExternalLoginStateCookie(new SecretProtector(options), options, new FixedClock(now ?? Now));
    }

    [Fact]
    public void A_state_round_trips_through_its_cookie()
    {
        var cookies = Cookie();
        var (state, value) = cookies.Issue(ExternalProvider.Google, "/account");

        var read = cookies.Read(value, ExternalProvider.Google, state.State);

        Assert.NotNull(read);
        Assert.Equal(state.CodeVerifier, read.CodeVerifier);
        Assert.Equal("/account", read.ReturnUrl);
    }

    [Fact]
    public void The_cookie_does_not_reveal_the_verifier()
    {
        var (state, value) = Cookie().Issue(ExternalProvider.Google, "/account");

        // An attacker who reads the cookie and intercepts the code must still not be able to
        // exchange it, which is the whole point of PKCE.
        Assert.DoesNotContain(state.CodeVerifier, value, StringComparison.Ordinal);
        Assert.DoesNotContain(state.State, value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_state_the_provider_did_not_echo_back_is_refused()
    {
        var cookies = Cookie();
        var (_, value) = cookies.Issue(ExternalProvider.Google, "/account");

        // The anti-forgery check: a callback carrying somebody else's state is not this sign-in.
        Assert.Null(cookies.Read(value, ExternalProvider.Google, "a-forged-state"));
        Assert.Null(cookies.Read(value, ExternalProvider.Google, null));
    }

    [Fact]
    public void A_callback_at_a_different_provider_is_refused()
    {
        var cookies = Cookie();
        var (state, value) = cookies.Issue(ExternalProvider.Google, "/account");

        Assert.Null(cookies.Read(value, ExternalProvider.Facebook, state.State));
    }

    [Fact]
    public void An_expired_sign_in_is_refused()
    {
        var issued = Cookie().Issue(ExternalProvider.Google, "/account");

        var later = Cookie(Now.AddMinutes(11));

        Assert.Null(later.Read(issued.Cookie, ExternalProvider.Google, issued.State.State));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-envelope")]
    public void A_missing_or_tampered_cookie_is_refused(string? value)
        => Assert.Null(Cookie().Read(value, ExternalProvider.Google, "anything"));

    private sealed class FixedClock(DateTimeOffset now) : KlaraHome.SharedKernel.Time.IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}

/// <summary>Reading an identity out of a provider's <c>id_token</c>.</summary>
public sealed class ExternalIdentityTests
{
    [Fact]
    public void A_verified_email_is_read_as_verified()
    {
        var token = IdToken("""{"sub":"1234567890","email":"Shopper@Example.IN","email_verified":true,"name":"A Shopper"}""");

        var result = OidcIdentityProvider.ReadIdentity(token);

        Assert.NotNull(result.Identity);
        Assert.Equal("1234567890", result.Identity.Subject);
        Assert.Equal("shopper@example.in", result.Identity.Email);
        Assert.True(result.Identity.EmailVerified);
        Assert.Equal("A Shopper", result.Identity.DisplayName);
    }

    [Fact]
    public void An_unverified_email_is_read_as_unverified()
    {
        // The distinction the linking rules turn on: an unverified address may not join an
        // existing account (docs/07-security-compliance.md §1).
        var token = IdToken("""{"sub":"1","email":"shopper@example.in","email_verified":false}""");

        Assert.False(OidcIdentityProvider.ReadIdentity(token).Identity!.EmailVerified);
    }

    [Fact]
    public void A_missing_email_verified_claim_is_not_verified()
    {
        var token = IdToken("""{"sub":"1","email":"shopper@example.in"}""");

        Assert.False(OidcIdentityProvider.ReadIdentity(token).Identity!.EmailVerified);
    }

    [Fact]
    public void A_token_with_no_subject_yields_no_identity()
    {
        var result = OidcIdentityProvider.ReadIdentity(IdToken("""{"email":"shopper@example.in"}"""));

        Assert.Null(result.Identity);
        Assert.Equal(ExternalExchangeFailure.NoIdentity, result.Failure);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("")]
    [InlineData("a.b")]
    public void An_unreadable_token_yields_no_identity_rather_than_throwing(string token)
        => Assert.Null(OidcIdentityProvider.ReadIdentity(token).Identity);

    /// <summary>An unsigned JWT with the given payload. The signature is not read by this method.</summary>
    private static string IdToken(string payload)
    {
        static string Segment(string json)
            => System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

        return $"{Segment("""{"alg":"RS256","typ":"JWT"}""")}.{Segment(payload)}.c2lnbmF0dXJl";
    }
}

/// <summary>The provider names that appear in routes.</summary>
public sealed class ProviderNameTests
{
    [Theory]
    [InlineData("google", nameof(ExternalProvider.Google))]
    [InlineData("GOOGLE", nameof(ExternalProvider.Google))]
    [InlineData("facebook", nameof(ExternalProvider.Facebook))]
    public void A_known_name_parses(string name, string expected)
    {
        // The expected value crosses the [InlineData] boundary as a string: the enum is internal,
        // and a public test method cannot take an internal parameter type.
        Assert.True(ProviderNames.TryParse(name, out var provider));
        Assert.Equal(expected, provider.ToString());
    }

    [Theory]
    [InlineData("apple")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("../../etc/passwd")]
    public void Anything_else_does_not(string? name)
        => Assert.False(ProviderNames.TryParse(name, out _));

    [Fact]
    public void Every_provider_has_a_wire_name_and_round_trips()
    {
        foreach (var provider in Enum.GetValues<ExternalProvider>())
        {
            Assert.True(ProviderNames.TryParse(ProviderNames.Of(provider), out var parsed));
            Assert.Equal(provider, parsed);
        }
    }
}

/// <summary>What the outbound client is allowed to reach.</summary>
public sealed class OutboundAllowListTests
{
    [Theory]
    [InlineData("accounts.google.com")]
    [InlineData("oauth2.googleapis.com")]
    [InlineData("graph.facebook.com")]
    public void A_provider_host_is_allowed(string host)
        => Assert.Contains(host, ExternalHttp.AllowedHosts);

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("localhost")]
    [InlineData("accounts.google.com.evil.example")]
    [InlineData("internal-service")]
    public void Anything_else_is_not(string host)
    {
        // The link-local metadata address is the classic SSRF target, and a suffix that merely
        // ends in a provider's name is the classic way past a naive check.
        Assert.DoesNotContain(host, ExternalHttp.AllowedHosts);
    }

    [Fact]
    public void The_client_has_a_bounded_timeout()
    {
        // A provider that starts answering slowly must not hold request threads open.
        Assert.InRange(ExternalHttp.Timeout, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
    }
}

/// <summary>Whether a provider is offered at all.</summary>
public sealed class ProviderConfigurationTests
{
    [Fact]
    public void A_provider_with_no_client_id_is_not_usable_however_enabled_it_says_it_is()
    {
        var options = new ExternalProviderOptions { Enabled = true, Authority = "https://accounts.google.com" };

        // A button that fails when somebody presses it is worse than no button, and this is the
        // ordinary state of a deployment that has not created an OAuth client yet.
        Assert.False(options.IsUsable);
    }

    [Fact]
    public void A_configured_provider_that_is_switched_off_is_not_usable()
    {
        var options = new ExternalProviderOptions
        {
            Enabled = false,
            ClientId = "id",
            ClientSecret = "secret",
            Authority = "https://accounts.google.com",
        };

        Assert.False(options.IsUsable);
    }

    [Fact]
    public void A_fully_configured_provider_is_usable()
    {
        var options = new ExternalProviderOptions
        {
            Enabled = true,
            ClientId = "id",
            ClientSecret = "secret",
            Authority = "https://accounts.google.com",
        };

        Assert.True(options.IsUsable);
    }

    [Fact]
    public void Facebook_ships_configured_but_off()
    {
        var defaults = new ExternalAuthOptions();

        // ADR-014: designed for, not enabled. It cannot leave development mode until the
        // deployment's owner completes Business Verification.
        Assert.False(defaults.Facebook.Enabled);
        Assert.False(defaults.Facebook.IsUsable);
        Assert.False(string.IsNullOrWhiteSpace(defaults.Facebook.AuthorizationEndpoint));
    }

    [Fact]
    public void Google_ships_pointed_at_the_right_authority_and_asks_for_nothing_extra()
    {
        var defaults = new ExternalAuthOptions();

        Assert.Equal("https://accounts.google.com", defaults.Google.Authority);
        Assert.Equal("openid email profile", defaults.Google.Scopes);
    }
}

/// <summary>The flags that turn the paid-provider features off.</summary>
public sealed class IdentityFeatureTests
{
    [Fact]
    public void Every_flag_is_declared_with_a_description_and_a_module_prefixed_key()
        => Assert.All(IdentityFeatures.All, flag =>
        {
            Assert.StartsWith("identity.", flag.Key, StringComparison.Ordinal);
            Assert.Equal(flag.Key.ToLowerInvariant(), flag.Key);
            Assert.False(string.IsNullOrWhiteSpace(flag.Description));
        });

    [Fact]
    public void Every_flag_ships_on()
    {
        // What the product does when it is fully provisioned. A deployment without a provider
        // turns them off, which is a decision an operator makes and the audit trail records.
        Assert.All(IdentityFeatures.All, flag => Assert.True(flag.Enabled));
    }

    [Fact]
    public void The_four_flags_ADR_014_names_all_exist()
    {
        var keys = IdentityFeatures.All.Select(flag => flag.Key).ToList();

        Assert.Contains(IdentityFeatures.MobileOtpLogin, keys);
        Assert.Contains(IdentityFeatures.EmailVerification, keys);
        Assert.Contains(IdentityFeatures.PasswordResetEmail, keys);
        Assert.Contains(IdentityFeatures.ExternalLogin, keys);
        Assert.Equal(4, keys.Count);
    }

    [Fact]
    public void The_source_publishes_them_under_this_module()
    {
        var source = new IdentityFeatureFlagSource();

        Assert.Equal("Identity", source.Module);
        Assert.Equal(IdentityFeatures.All, source.Flags);
    }
}

/// <summary>The temporary-password state on the user.</summary>
public sealed class TemporaryPasswordTests
{
    [Fact]
    public void An_administrator_issued_password_owes_a_change()
    {
        var user = User.RegisterStaff(UserType.Staff, "staff@example.in");

        user.SetPasswordHash("$argon2id$issued-by-an-administrator", mustChange: true);

        Assert.True(user.MustChangePassword);
    }

    [Fact]
    public void A_password_the_owner_chose_discharges_the_obligation()
    {
        var user = User.RegisterStaff(UserType.Staff, "staff@example.in");
        user.SetPasswordHash("$argon2id$temporary", mustChange: true);

        user.SetPasswordHash("$argon2id$their-own");

        Assert.False(user.MustChangePassword);
    }

    [Fact]
    public void An_account_with_only_an_external_login_has_no_local_credential()
    {
        var user = User.RegisterCustomer(mobile: null, "shopper@example.in");

        // Read before unlinking a provider: removing the last credential would leave an account
        // nobody can reach, including its owner.
        Assert.False(user.HasLocalCredential);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_password_or_a_verified_mobile_number_is_a_local_credential(bool password, bool mobile)
    {
        var user = User.RegisterCustomer(mobile ? "+919876543210" : null, "shopper@example.in");

        if (password)
        {
            user.SetPasswordHash("$argon2id$something");
        }

        if (mobile)
        {
            user.MarkMobileVerified(DateTimeOffset.UtcNow);
        }

        Assert.True(user.HasLocalCredential);
    }
}
