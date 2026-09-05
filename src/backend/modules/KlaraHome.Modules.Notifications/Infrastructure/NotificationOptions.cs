using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Notifications.Infrastructure;

/// <summary>
/// How this deployment sends, retries and gives up.
/// </summary>
internal sealed class NotificationOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Notifications";

    /// <summary>
    /// Whether the dispatcher runs in this host. On in the worker, off in the API — the same
    /// division the outbox dispatcher follows, and for the same reason: every API replica running
    /// the loop would multiply the polling and contend for the same rows.
    /// </summary>
    public bool DispatcherEnabled { get; set; }

    /// <summary>Seconds between polls when the queue is empty.</summary>
    [Range(1, 300)]
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Messages claimed per poll.</summary>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 25;

    /// <summary>Attempts before a message is given up on.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 6;

    /// <summary>Delay before the second attempt, in seconds. Doubles from there.</summary>
    [Range(1, 3600)]
    public int RetryBaseSeconds { get; set; } = 30;

    /// <summary>Ceiling on the backoff, in seconds. One hour by default.</summary>
    [Range(60, 86_400)]
    public int RetryMaxSeconds { get; set; } = 3_600;

    /// <summary>Language used when a request does not name one and the store has no better answer.</summary>
    [Required]
    public string DefaultLocale { get; set; } = "en-IN";
}

/// <summary>
/// The email channel (docs/08-integrations.md §3.3).
/// </summary>
/// <remarks>
/// SMTP is the only transport built, and it is the reason email is the one channel that works
/// without a paid account: any host will do, and Mailpit answers on the development stack. A
/// managed provider — SES, Postmark — is still wanted for deliverability, DKIM and bounce
/// handling, and would be a second <c>IChannelSender</c>, not a change here.
/// </remarks>
internal sealed class EmailOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Email";

    /// <summary>Which transport to use. <c>smtp</c>, or empty for none.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Envelope sender address.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Display name on the envelope sender. Falls back to the store name.</summary>
    public string FromName { get; set; } = string.Empty;

    /// <summary>API key, for a provider that uses one rather than SMTP.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>SMTP host settings.</summary>
    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>Whether email can actually be sent.</summary>
    public bool IsConfigured
        => string.Equals(Provider, "smtp", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(Smtp.Host)
           && !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>An SMTP host.</summary>
internal sealed class SmtpOptions
{
    /// <summary>Host name. Empty disables the channel.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Port. 1025 is Mailpit; 587 is submission with STARTTLS.</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    /// <summary>Username, or empty for a host that wants no authentication.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Password. Supplied as a Docker secret in a deployed environment.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Whether TLS is required.
    /// </summary>
    /// <remarks>
    /// Off in development because Mailpit speaks plain SMTP on the compose network, and on
    /// everywhere else — an SMTP submission in the clear carries the password and the message.
    /// </remarks>
    public bool UseTls { get; set; } = true;

    /// <summary>Seconds to wait on the connection before giving up and retrying later.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// The SMS and WhatsApp channels (docs/08-integrations.md §3.1, §3.2).
/// </summary>
/// <remarks>
/// <see cref="Provider"/> is empty in every environment today, because there is no SMS account
/// (§7). Every SMS is therefore recorded as <c>Suppressed / NoProvider</c> in production, and
/// routed to Mailpit outside it so a developer can still complete an OTP sign-in (ADR-017).
/// </remarks>
internal sealed class SmsOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Sms";

    /// <summary>The provider adapter to use. Empty means the channel is suppressed.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>API key for the provider.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The registered sender header, six characters, issued with the DLT registration.</summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>The DLT principal entity id registered with TRAI.</summary>
    public string DltEntityId { get; set; } = string.Empty;

    /// <summary>Whether a real SMS provider is configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Provider);
}
