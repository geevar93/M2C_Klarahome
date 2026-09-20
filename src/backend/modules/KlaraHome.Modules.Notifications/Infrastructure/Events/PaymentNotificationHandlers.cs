using KlaraHome.Contracts.Notifications;
using KlaraHome.Contracts.Payments;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Notifications.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Notifications.Infrastructure.Events;

/// <summary>
/// Tells a shopper what happened to their money, off the events Payments publishes
/// (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Three of the payment events are addressed to the shopper: the capture, the failure and the
/// refund. The others are not, and their absence here is deliberate — <c>CodCashRecorded</c> is a
/// courier remitting cash days after the shopper handed it over and is settlement bookkeeping, and
/// <c>PaymentMismatchDetected</c> is an operator's problem that no shopper can act on.
/// </para>
/// <para>
/// <c>PaymentCapturedOnCancelledOrder</c> is likewise not handled, and that one is worth naming:
/// it is published <em>alongside</em> <c>PaymentCaptured</c>, never instead of it, so handling both
/// would tell a shopper twice that the same money had been taken. The refund that follows is what
/// they actually need to hear, and it arrives as <c>RefundProcessed</c> like any other.
/// </para>
/// <para>
/// All three are transactional and none is opt-out-able, because each one is a statement about
/// money that has moved. Somebody who has turned off tracking chatter has not turned off being
/// told that they were charged.
/// </para>
/// </remarks>
/// <param name="context">The Notifications data context, which the inbox rows are written through.</param>
/// <param name="notifier">Queues the messages.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was queued and what was skipped.</param>
internal sealed partial class PaymentNotificationHandlers(
    NotificationsDbContext context,
    INotifier notifier,
    IClock clock,
    ILogger<PaymentNotificationHandlers> logger)
    : IIntegrationEventHandler<PaymentCaptured>,
        IIntegrationEventHandler<PaymentFailed>,
        IIntegrationEventHandler<RefundProcessed>
{
    private const string CapturedHandler = "notifications.payment.captured";

    private const string FailedHandler = "notifications.payment.failed";

    private const string RefundHandler = "notifications.payment.refunded";

    /// <inheritdoc />
    public async Task HandleAsync(PaymentCaptured integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await NotifyAsync(
                integrationEvent.EventId,
                CapturedHandler,
                NotificationEvents.PaymentCaptured,
                integrationEvent.CustomerId,
                VariablesFor(integrationEvent),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The gateway's own failure code is not passed to the template. It is for the delivery log and
    /// for support; a shopper told "card declined — code GW_02" learns nothing they can act on that
    /// "card declined" did not already tell them.
    /// </remarks>
    public async Task HandleAsync(PaymentFailed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await NotifyAsync(
                integrationEvent.EventId,
                FailedHandler,
                NotificationEvents.PaymentFailed,
                integrationEvent.CustomerId,
                VariablesFor(integrationEvent),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task HandleAsync(RefundProcessed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await NotifyAsync(
                integrationEvent.EventId,
                RefundHandler,
                NotificationEvents.RefundProcessed,
                integrationEvent.CustomerId,
                VariablesFor(integrationEvent),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>What the capture template is given.</summary>
    /// <remarks>
    /// Internal and static for the same reason as the order handlers: a test renders the shipped
    /// template with exactly this, so a placeholder nothing supplies fails the build rather than
    /// one message at a time in production.
    /// </remarks>
    /// <param name="integrationEvent">The event.</param>
    internal static Dictionary<string, string> VariablesFor(PaymentCaptured integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["orderNumber"] = integrationEvent.OrderNumber,
            ["amount"] = Money.Format(integrationEvent.Amount, integrationEvent.CurrencyCode),
        };
    }

    /// <summary>What the failure template is given.</summary>
    /// <param name="integrationEvent">The event.</param>
    internal static Dictionary<string, string> VariablesFor(PaymentFailed integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["orderNumber"] = integrationEvent.OrderNumber,
            ["reason"] = string.IsNullOrWhiteSpace(integrationEvent.Reason)
                ? "The payment was not completed."
                : integrationEvent.Reason.Trim(),
        };
    }

    /// <summary>What the refund template is given.</summary>
    /// <param name="integrationEvent">The event.</param>
    internal static Dictionary<string, string> VariablesFor(RefundProcessed integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["orderNumber"] = integrationEvent.OrderNumber,
            ["amount"] = Money.Format(integrationEvent.Amount, integrationEvent.CurrencyCode),
        };
    }

    /// <summary>
    /// Claim the event, queue the message, record that it was handled.
    /// </summary>
    /// <remarks>
    /// The same order, and the same trade, as <see cref="OrderNotificationHandlers"/>: a crash
    /// between the send and the inbox row sends a duplicate rather than losing a message about
    /// money.
    /// </remarks>
    private async Task NotifyAsync(
        Guid eventId,
        string handler,
        string eventKey,
        Guid customerId,
        Dictionary<string, string> variables,
        CancellationToken cancellationToken)
    {
        var handled = await context.InboxMessages
            .AsNoTracking()
            .AnyAsync(
                message => message.MessageId == eventId && message.Handler == handler,
                cancellationToken)
            .ConfigureAwait(false);

        if (handled)
        {
            AlreadyHandled(logger, eventKey, eventId);
            return;
        }

        await notifier
            .EnqueueAsync(
                new NotificationRequest(
                    eventKey,
                    new NotificationRecipient(customerId),
                    variables),
                cancellationToken)
            .ConfigureAwait(false);

        context.InboxMessages.Add(new InboxMessage
        {
            MessageId = eventId,
            Handler = handler,
            ProcessedAt = clock.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Queued(logger, eventKey, customerId);
    }

    [LoggerMessage(
        EventId = 1552,
        Level = LogLevel.Debug,
        Message = "Event {EventId} was already handled for {EventKey}; no message was queued")]
    private static partial void AlreadyHandled(ILogger logger, string eventKey, Guid eventId);

    [LoggerMessage(
        EventId = 1553,
        Level = LogLevel.Information,
        Message = "Queued a {EventKey} notification for customer {CustomerId}")]
    private static partial void Queued(ILogger logger, string eventKey, Guid customerId);
}
