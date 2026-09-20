using System.Globalization;
using KlaraHome.Contracts.Notifications;
using KlaraHome.Contracts.Orders;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Notifications.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Notifications.Infrastructure.Events;

/// <summary>
/// Tells a shopper what happened to their order, off the events Orders publishes
/// (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Three facts are worth a message and they arrive on three different events: the order was placed,
/// a seller's part of it was cancelled, and a seller's part of it moved along the timeline. The
/// third is the general one, and it is deliberately <em>not</em> forwarded wholesale: the machine
/// allows fourteen transitions and a shopper does not want to hear about all of them. Only the four
/// that change what they should expect to happen next produce a message, and a transition outside
/// that set is discarded before any query is issued.
/// </para>
/// <para>
/// Delivery is at-least-once, so every handler is guarded by an inbox row keyed on the event and
/// the handler name. The guard is not ceremony: sending is not idempotent, and a redelivered
/// <c>SubOrderStatusChanged</c> without it would tell the same shopper twice that the same parcel
/// had been delivered.
/// </para>
/// <para>
/// The recipient is named by user id alone. This module resolves an address per channel from the
/// account, and the in-app channel needs no address beyond the id — the delivery log row
/// <em>is</em> the message.
/// </para>
/// </remarks>
/// <param name="context">The Notifications data context, which the inbox rows are written through.</param>
/// <param name="notifier">Queues the messages.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was queued and what was skipped.</param>
internal sealed partial class OrderNotificationHandlers(
    NotificationsDbContext context,
    INotifier notifier,
    IClock clock,
    ILogger<OrderNotificationHandlers> logger)
    : IIntegrationEventHandler<OrderPlaced>,
        IIntegrationEventHandler<SubOrderCancelled>,
        IIntegrationEventHandler<SubOrderStatusChanged>
{
    /// <summary>
    /// The names the three handlers record themselves under in the inbox.
    /// </summary>
    /// <remarks>
    /// Constants rather than the type name, so renaming the class does not silently make every
    /// already-sent message sendable again.
    /// </remarks>
    private const string PlacedHandler = "notifications.order.placed";

    private const string CancelledHandler = "notifications.order.cancelled";

    private const string StatusHandler = "notifications.order.status";

    /// <summary>
    /// Which timeline states are worth telling a shopper about, and what each is called.
    /// </summary>
    /// <remarks>
    /// The four that change what the shopper should expect next. <c>Confirmed</c> and
    /// <c>Processing</c> are absent because the order-placed message already covered them, and the
    /// RTO states are absent because a parcel going back to the seller ends in a refund, which
    /// Payments announces with the money in hand rather than in prospect.
    /// </remarks>
    internal static readonly Dictionary<string, string> NotifiableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Shipped"] = NotificationEvents.OrderShipped,
        ["OutForDelivery"] = NotificationEvents.OrderOutForDelivery,
        ["Delivered"] = NotificationEvents.OrderDelivered,
        ["DeliveryFailed"] = NotificationEvents.OrderDeliveryFailed,
    };

    /// <inheritdoc />
    public async Task HandleAsync(OrderPlaced integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await NotifyAsync(
                integrationEvent.EventId,
                PlacedHandler,
                NotificationEvents.OrderPlaced,
                integrationEvent.CustomerId,
                VariablesFor(integrationEvent),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task HandleAsync(SubOrderCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await NotifyAsync(
                integrationEvent.EventId,
                CancelledHandler,
                NotificationEvents.OrderCancelled,
                integrationEvent.CustomerId,
                VariablesFor(integrationEvent),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The cheap test first. This event is published on every transition the machine allows, and
    /// most of them are seller-side bookkeeping the shopper has no use for.
    /// </remarks>
    public async Task HandleAsync(SubOrderStatusChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!NotifiableStatuses.TryGetValue(integrationEvent.ToStatus, out var eventKey))
        {
            return;
        }

        await NotifyAsync(
                integrationEvent.EventId,
                StatusHandler,
                eventKey,
                integrationEvent.CustomerId,
                VariablesFor(integrationEvent),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>What the order-placed template is given.</summary>
    /// <remarks>
    /// Internal and static so a test can render the shipped template with exactly what the handler
    /// supplies. A template edited to use a placeholder nothing passes does not fail loudly - it
    /// fails one message at a time, in the delivery log, after it has shipped.
    /// </remarks>
    /// <param name="integrationEvent">The event.</param>
    internal static Dictionary<string, string> VariablesFor(OrderPlaced integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["orderNumber"] = integrationEvent.OrderNumber,
            ["total"] = Money.Format(integrationEvent.GrandTotal, integrationEvent.CurrencyCode),
        };
    }

    /// <summary>What the cancellation template is given.</summary>
    /// <param name="integrationEvent">The event.</param>
    internal static Dictionary<string, string> VariablesFor(SubOrderCancelled integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["orderNumber"] = integrationEvent.OrderNumber,
            ["cancelledTotal"] = Money.Format(integrationEvent.CancelledTotal, integrationEvent.CurrencyCode),
            ["reason"] = Reason(integrationEvent.Reason),
        };
    }

    /// <summary>What the four timeline templates are given.</summary>
    /// <param name="integrationEvent">The event.</param>
    internal static Dictionary<string, string> VariablesFor(SubOrderStatusChanged integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["orderNumber"] = integrationEvent.OrderNumber,
            ["subOrderNumber"] = integrationEvent.SubOrderNumber,
            ["reason"] = Reason(integrationEvent.Reason),
        };
    }

    /// <summary>
    /// The shape all three share: claim the event, queue the message, record that it was handled.
    /// </summary>
    /// <remarks>
    /// The inbox row is written after the notifier has been called rather than before, which is the
    /// same order <c>StockAlertHandlers</c> uses and the same trade it accepts. The notifier writes
    /// in its own scope, so the two cannot share a transaction; a crash between them re-delivers
    /// the event and sends a duplicate, which is the failure worth having over one where the row is
    /// claimed and the message never sent.
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

    /// <summary>
    /// What the <c>{{reason}}</c> placeholder gets when the event carries none.
    /// </summary>
    /// <remarks>
    /// A template fails outright on a missing variable, which is the right behaviour for a value
    /// the caller forgot and the wrong one for a value the domain is allowed not to have. An empty
    /// string satisfies the renderer and leaves the sentence around it reading properly.
    /// </remarks>
    private static string Reason(string? reason)
        => string.IsNullOrWhiteSpace(reason) ? string.Empty : reason.Trim();

    [LoggerMessage(
        EventId = 1550,
        Level = LogLevel.Debug,
        Message = "Event {EventId} was already handled for {EventKey}; no message was queued")]
    private static partial void AlreadyHandled(ILogger logger, string eventKey, Guid eventId);

    [LoggerMessage(
        EventId = 1551,
        Level = LogLevel.Information,
        Message = "Queued a {EventKey} notification for customer {CustomerId}")]
    private static partial void Queued(ILogger logger, string eventKey, Guid customerId);
}

/// <summary>
/// How an amount is written into a message.
/// </summary>
/// <remarks>
/// <para>
/// The symbol for the currency this platform actually trades in, and the ISO code for anything
/// else. A shopper reading "INR 1,299.00" is reading a system's idea of money rather than their
/// own, and a symbol guessed wrong for a currency nobody configured is worse than a code.
/// </para>
/// <para>
/// <b>The Indian grouping is built rather than looked up.</b> This host runs with
/// <c>InvariantGlobalization</c> (Directory.Build.props), so asking for <c>en-IN</c> throws — the
/// ICU data is not in the image. Cloning the invariant format and setting the group sizes to
/// <c>3, 2</c> produces the same 1,29,999.00 without needing any of it.
/// </para>
/// </remarks>
internal static class Money
{
    /// <summary>Thousands, then hundreds: the Indian grouping, without a culture to look it up in.</summary>
    private static readonly NumberFormatInfo IndianGrouping = CreateIndianGrouping();

    /// <summary>Formats an amount for a template placeholder.</summary>
    /// <param name="amount">The amount.</param>
    /// <param name="currencyCode">Its ISO 4217 code.</param>
    public static string Format(decimal amount, string? currencyCode)
    {
        var isRupees = string.Equals(currencyCode, "INR", StringComparison.OrdinalIgnoreCase);
        var value = amount.ToString("N2", isRupees ? IndianGrouping : CultureInfo.InvariantCulture);

        return isRupees ? $"₹{value}" : $"{currencyCode} {value}".Trim();
    }

    private static NumberFormatInfo CreateIndianGrouping()
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberGroupSizes = [3, 2];

        // Frozen: this is a static shared by every message the platform formats, and a mutable one
        // is a field any caller could reach in and change.
        return NumberFormatInfo.ReadOnly(format);
    }
}
