using KlaraHome.Contracts.Notifications;

namespace KlaraHome.Modules.Notifications.Infrastructure.Channels;

/// <summary>A message about to leave this system.</summary>
/// <param name="Recipient">The address: an email address, or a number in E.164 form.</param>
/// <param name="RecipientName">A display name, when one is known.</param>
/// <param name="Subject">The rendered subject, for a channel that has one.</param>
/// <param name="Body">The rendered body.</param>
/// <param name="ProviderTemplateId">The provider's registered template id — the DLT id, for SMS.</param>
/// <param name="EventKey">The event, so a provider that routes by priority can.</param>
internal sealed record OutboundMessage(
    string Recipient,
    string? RecipientName,
    string? Subject,
    string Body,
    string? ProviderTemplateId,
    string EventKey);

/// <summary>What the provider said.</summary>
/// <param name="IsSuccess">Whether it accepted the message.</param>
/// <param name="ProviderMessageId">The provider's id for it, for reconciling a receipt.</param>
/// <param name="Error">What went wrong, when something did.</param>
/// <param name="IsPermanent">
/// Whether retrying is pointless. A malformed address or a rejected recipient is permanent; a
/// timeout or a 5xx is not. Retrying a permanent failure five times wastes an hour and tells
/// nobody anything new.
/// </param>
internal sealed record SendOutcome(bool IsSuccess, string? ProviderMessageId, string? Error, bool IsPermanent)
{
    /// <summary>The provider accepted it.</summary>
    /// <param name="providerMessageId">The provider's id for the message.</param>
    public static SendOutcome Accepted(string? providerMessageId = null)
        => new(true, providerMessageId, null, false);

    /// <summary>It failed, and trying again might work.</summary>
    /// <param name="error">What went wrong.</param>
    public static SendOutcome Transient(string error) => new(false, null, error, false);

    /// <summary>It failed, and trying again will not work.</summary>
    /// <param name="error">What went wrong.</param>
    public static SendOutcome Permanent(string error) => new(false, null, error, true);
}

/// <summary>
/// Carries a message on one channel.
/// </summary>
/// <remarks>
/// One implementation per channel per provider, selected by configuration, exactly as the payment
/// and courier adapters are (docs/08-integrations.md preamble). A channel with no registered
/// sender is not an error condition to handle at every call site — the router answers that a
/// message is suppressed, and the delivery log records why (ADR-017).
/// </remarks>
internal interface IChannelSender
{
    /// <summary>The channel this sender carries.</summary>
    NotificationChannel Channel { get; }

    /// <summary>
    /// Whether it can actually send. A sender that is registered but unconfigured reports false
    /// and is treated as absent, which is the ordinary state of a fresh deployment rather than a
    /// fault.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Whether a message must carry a registered provider template id before this sender will take
    /// it.
    /// </summary>
    /// <remarks>
    /// True for an Indian SMS provider, where an unregistered message is silently dropped by the
    /// operator. False for the development mail sink, which is not an operator and has nothing to
    /// register with — without the distinction a developer could never receive a sign-in code
    /// locally, which is the whole point of that route (ADR-017).
    /// </remarks>
    bool RequiresProviderTemplate => false;

    /// <summary>Hands the message to the provider.</summary>
    /// <param name="message">What to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SendOutcome> SendAsync(OutboundMessage message, CancellationToken cancellationToken);
}
