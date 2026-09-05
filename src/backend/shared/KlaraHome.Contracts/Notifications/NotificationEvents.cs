namespace KlaraHome.Contracts.Notifications;

/// <summary>
/// Every event this platform can notify about, declared in code.
/// </summary>
/// <remarks>
/// <para>
/// The same arrangement as the permission catalogue and the feature-flag declarations: the key
/// exists because a caller uses it, the seeder creates a template for it on every deploy, and a
/// test asserts the two sets match. A template for an event nobody raises is a message an operator
/// can edit and never see; an event with no template is a notification that silently is not sent.
/// </para>
/// <para>
/// Keys for modules that do not exist yet are deliberately absent. They arrive with the step that
/// first raises them, so this list and the delivered surface cannot drift apart.
/// </para>
/// </remarks>
public static class NotificationEvents
{
    /// <summary>A one-time code for signing in with a mobile number.</summary>
    public const string OtpLogin = "identity.otp.login";

    /// <summary>A one-time code proving control of a mobile number being added or changed.</summary>
    public const string OtpMobileVerification = "identity.otp.mobile-verification";

    /// <summary>A link proving control of an email address.</summary>
    public const string EmailVerification = "identity.email.verification";

    /// <summary>A link that lets somebody set a new password.</summary>
    public const string PasswordReset = "identity.password.reset";

    /// <summary>Confirmation that a password was changed, sent to the address on the account.</summary>
    public const string PasswordChanged = "identity.password.changed";

    /// <summary>An administrator issued a temporary password (ADR-014).</summary>
    public const string TemporaryPasswordIssued = "identity.password.temporary-issued";

    /// <summary>Welcome, sent once when an account is created.</summary>
    public const string AccountCreated = "identity.account.created";

    /// <summary>A test message, sent from the admin surface to prove a channel works end to end.</summary>
    public const string ChannelTest = "platform.channel.test";

    /// <summary>Every declared event key.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        OtpLogin,
        OtpMobileVerification,
        EmailVerification,
        PasswordReset,
        PasswordChanged,
        TemporaryPasswordIssued,
        AccountCreated,
        ChannelTest,
    ];

    /// <summary>Whether a key is one this platform declares.</summary>
    /// <param name="eventKey">The key to check.</param>
    public static bool Contains(string eventKey)
        => All.Contains(eventKey, StringComparer.Ordinal);
}
