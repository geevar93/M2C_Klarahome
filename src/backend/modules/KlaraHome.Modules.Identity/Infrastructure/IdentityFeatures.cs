using KlaraHome.Contracts.Platform;

namespace KlaraHome.Modules.Identity.Infrastructure;

/// <summary>
/// The feature flags the Identity module reads (ADR-014, `07-security-compliance.md` §1).
/// </summary>
/// <remarks>
/// <para>
/// Three of these four exist because SMS and transactional email are paid dependencies and this
/// deployment does not have them yet. They are switches rather than deletions on purpose: the code
/// behind each one is the code that was written and tested at Step 7, and the day a provider is
/// paid for, the feature returns without a deploy.
/// </para>
/// <para>
/// Every one of them ships **on**, because that is what the product does when it is fully
/// provisioned. A deployment without a provider turns them off — which is a decision an operator
/// makes and the audit trail records, rather than a default nobody chose.
/// </para>
/// </remarks>
internal static class IdentityFeatures
{
    /// <summary>
    /// Gates the mobile-OTP endpoints. Off means a customer signs in with an identity provider or
    /// with an email and a password; there is no free local path for SMS, so this is the flag a
    /// developer without an SMS account turns off too.
    /// </summary>
    public const string MobileOtpLogin = "identity.mobile-otp-login";

    /// <summary>
    /// Gates sending and confirming an email verification. Off means an address stays unverified
    /// unless an identity provider asserted it — which is a stronger assertion than our own link
    /// would have been.
    /// </summary>
    public const string EmailVerification = "identity.email-verification";

    /// <summary>
    /// Gates self-service password reset, which is an email round trip and nothing else. Off means
    /// an administrator issues a temporary password instead.
    /// </summary>
    public const string PasswordResetEmail = "identity.password-reset-email";

    /// <summary>
    /// Gates the whole external sign-in surface. A provider also has to be configured and enabled
    /// individually; this is the switch that takes all of them off at once.
    /// </summary>
    public const string ExternalLogin = "identity.external-login";

    /// <summary>Every flag this module declares.</summary>
    public static IReadOnlyList<FeatureFlagDeclaration> All { get; } =
    [
        new(MobileOtpLogin, true, "Let customers sign in with a mobile number and a one-time code. Needs an SMS provider."),
        new(EmailVerification, true, "Send and accept email verification links. Needs an email provider."),
        new(PasswordResetEmail, true, "Let anyone reset their own password by email. Needs an email provider."),
        new(ExternalLogin, true, "Let customers sign in with an external identity provider such as Google."),
    ];
}

/// <summary>Publishes this module's flags to the Platform seeder that owns the table.</summary>
internal sealed class IdentityFeatureFlagSource : IFeatureFlagSource
{
    /// <inheritdoc />
    public string Module => "Identity";

    /// <inheritdoc />
    public IReadOnlyList<FeatureFlagDeclaration> Flags => IdentityFeatures.All;
}
