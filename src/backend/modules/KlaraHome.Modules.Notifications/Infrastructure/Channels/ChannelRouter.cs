using KlaraHome.Contracts.Notifications;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Notifications.Infrastructure.Templating;

namespace KlaraHome.Modules.Notifications.Infrastructure.Channels;

/// <summary>
/// Decides whether a channel can carry a message at all, and hands it to the sender when it can.
/// </summary>
/// <remarks>
/// The one place that answers "is this channel available", so the notifier, the dispatcher and the
/// admin surface cannot disagree about it. Availability is three questions and they are asked in
/// this order: is the channel switched on, is a provider configured, and does the message satisfy
/// the channel's own rules — the DLT registration, for SMS.
/// </remarks>
/// <param name="senders">Every registered sender. At most one per channel is expected.</param>
/// <param name="flags">The channel feature flags.</param>
internal sealed class ChannelRouter(IEnumerable<IChannelSender> senders, IFeatureFlags flags)
{
    private readonly Dictionary<NotificationChannel, IChannelSender> _senders = senders
        .GroupBy(sender => sender.Channel)
        .ToDictionary(group => group.Key, group => group.First());

    /// <summary>Whether a sender exists and is configured for a channel.</summary>
    /// <param name="channel">The channel.</param>
    public bool HasProvider(NotificationChannel channel)
        => _senders.TryGetValue(channel, out var sender) && sender.IsConfigured;

    /// <summary>
    /// Whether a channel is available right now, and if not, why not.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<NotificationSuppression> AvailabilityAsync(
        NotificationChannel channel,
        CancellationToken cancellationToken)
    {
        var flag = NotificationFeatures.FlagFor(channel);

        if (flag is not null
            && !await flags.IsEnabledAsync(flag, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            return NotificationSuppression.ChannelDisabled;
        }

        return HasProvider(channel) ? NotificationSuppression.None : NotificationSuppression.NoProvider;
    }

    /// <summary>
    /// Sends, having already established that the channel is available.
    /// </summary>
    /// <remarks>
    /// The DLT check happens here rather than at template-editing time because both halves have to
    /// be true at once: the template must be registered <em>and</em> the values about to be
    /// substituted must fit what a registered template may carry. An operator editing wording
    /// cannot know the second.
    /// </remarks>
    /// <param name="channel">The channel to send on.</param>
    /// <param name="message">The rendered message.</param>
    /// <param name="variables">The values that were substituted, for the DLT length rule.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SendOutcome> SendAsync(
        NotificationChannel channel,
        OutboundMessage message,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_senders.TryGetValue(channel, out var sender) || !sender.IsConfigured)
        {
            return SendOutcome.Permanent($"No provider is configured for the {channel} channel.");
        }

        if (sender.RequiresProviderTemplate)
        {
            var violation = DltRules.Validate(message.Body, message.ProviderTemplateId);

            if (violation != DltViolation.None)
            {
                // Permanent, and deliberately loud. A non-conforming SMS is not rejected by an
                // Indian operator — it is silently dropped — so a message that would vanish is
                // refused here instead, where somebody can see the reason.
                return SendOutcome.Permanent($"DLT rule violated: {violation}.");
            }

            var valueViolation = DltRules.ValidateValues(variables, out var offending);

            if (valueViolation != DltViolation.None)
            {
                return SendOutcome.Permanent($"DLT rule violated: {valueViolation} ({offending}).");
            }
        }

        return await sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
