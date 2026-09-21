using System.Globalization;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.Modules.Payments.Infrastructure.Processing;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Payments.Infrastructure.Events;

/// <summary>
/// Keeps the money in step with the life of an order (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Two facts about an order concern this module and neither of them is the payment itself. A
/// <b>cash-on-delivery order</b> is confirmed without a gateway, and the money still has to be
/// recorded, expected at a door and reconciled against a courier's remittance — so a confirmation
/// opens a cash record per parcel. And a <b>cancellation</b> of something already paid for is money
/// owed back, which is raised here rather than waiting for somebody to notice.
/// </para>
/// <para>
/// The payment method is read from the order over the contract rather than carried on the event.
/// It could have been added to <c>SubOrderConfirmed</c>, but that event is consumed by four modules
/// that do not care about it, and — more to the point — a handler that depended on
/// <c>OrderPlaced</c> having been processed first would be a handler that depended on outbox
/// ordering, which is a guarantee nothing here makes.
/// </para>
/// <para>
/// Delivery is at-least-once, so every method is idempotent: a redelivered confirmation finds the
/// cash record already open, and a redelivered cancellation finds the refund already raised under
/// the same key.
/// </para>
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="orders">Reads the order's payment method and totals, over the contract.</param>
/// <param name="workflow">Raises refunds, applying the maker-checker threshold.</param>
/// <param name="refunds">Sends the ones that were approved on creation.</param>
/// <param name="settings">Supplies the automatic-refund switch.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was opened and what was given back.</param>
internal sealed partial class OrderLifecycleHandlers(
    PaymentsDbContext context,
    IOrderPaymentSync orders,
    PaymentWorkflow workflow,
    RefundDispatcher refunds,
    IStoreSettings settings,
    IClock clock,
    ILogger<OrderLifecycleHandlers> logger)
    : IIntegrationEventHandler<SubOrderConfirmed>,
        IIntegrationEventHandler<SubOrderCancelled>,
        IIntegrationEventHandler<PaymentCapturedOnCancelledOrder>
{
    /// <summary>
    /// Opens the cash record for a confirmed cash-on-delivery parcel.
    /// </summary>
    /// <remarks>
    /// One row per sub-order, because cash is collected per parcel: two sellers in one basket are two
    /// deliveries, two couriers and two amounts. A prepaid confirmation does nothing here — its money
    /// arrived before the order was confirmed, which is why it was confirmed.
    /// </remarks>
    public async Task HandleAsync(SubOrderConfirmed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var order = await orders
            .GetAsync(integrationEvent.OrderId, customerId: null, cancellationToken)
            .ConfigureAwait(false);

        if (order.IsFailure || !IsCashOnDelivery(order.Value.PaymentMethod))
        {
            return;
        }

        var exists = await context.CodCollections
            .AnyAsync(collection => collection.SubOrderId == integrationEvent.SubOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        await EnsureCodPaymentAsync(order.Value, cancellationToken).ConfigureAwait(false);

        var record = CodCollection.Expect(
            integrationEvent.OrderId,
            integrationEvent.SubOrderId,
            integrationEvent.VendorId,
            integrationEvent.Total,
            integrationEvent.CurrencyCode);

        context.CodCollections.Add(record);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        CashExpected(logger, integrationEvent.SubOrderNumber, integrationEvent.Total);
    }

    /// <summary>
    /// Gives back what was collected for a cancelled parcel, and stands down the cash for one that
    /// never got there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refund is <em>proportional to what was cancelled</em>, not the whole order: a two-seller
    /// basket where one seller cannot fulfil owes the shopper that seller's share and no more. It is
    /// also clamped to what is actually still refundable, so a full cancellation arriving after a
    /// partial refund does not ask the gateway for money that has already gone.
    /// </para>
    /// <para>
    /// The idempotency key is derived from the sub-order and the amount rather than generated, which
    /// is what makes a redelivered cancellation harmless: the second attempt collides on the unique
    /// index and raises nothing.
    /// </para>
    /// </remarks>
    public async Task HandleAsync(SubOrderCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await WaiveCashAsync(integrationEvent, cancellationToken).ConfigureAwait(false);

        var governance = await settings.GetAsync<PaymentSettings>(cancellationToken).ConfigureAwait(false);

        if (!governance.AutoRefundOnCancellation || integrationEvent.CancelledTotal <= 0m)
        {
            return;
        }

        var payment = await context.Payments
            .Include(candidate => candidate.Refunds)
            .Where(candidate => candidate.OrderId == integrationEvent.OrderId
                                && candidate.Provider != PaymentProviders.InternalCod)
            .OrderByDescending(candidate => candidate.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // Nothing was ever collected: the cancellation is of an order that never got past awaiting
        // payment, and there is no money to give back.
        if (payment is null || payment.AmountRefundable <= 0m)
        {
            return;
        }

        var amount = Math.Min(integrationEvent.CancelledTotal, payment.AmountRefundable);

        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"cancel:{integrationEvent.SubOrderId}:{amount:0.0000}");

        var already = await context.Refunds
            .AnyAsync(refund => refund.IdempotencyKey == key, cancellationToken)
            .ConfigureAwait(false);

        if (already)
        {
            return;
        }

        var raised = await workflow
            .RaiseRefundAsync(
                payment,
                amount,
                $"Order {integrationEvent.OrderNumber} was cancelled: "
                + (integrationEvent.Reason ?? "no reason was given."),
                key,
                // No initiator. The platform raised this, not a person — and leaving it null is what
                // lets any operator approve it without tripping the self-approval constraint.
                initiatedBy: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (raised.IsFailure)
        {
            RefundNotRaised(logger, integrationEvent.SubOrderNumber, raised.Error.Message);
            return;
        }

        raised.Value.AttachCause(integrationEvent.SubOrderId, returnId: null);

        if (integrationEvent.WasDispatched)
        {
            // The courier has the goods. The refund is owed, but it waits for them to come back:
            // ShippingLifecycleHandlers releases it when the parcel reaches the seller again.
            raised.Value.HoldForReturn();
            RefundHeld(logger, integrationEvent.SubOrderNumber, amount);
        }
        else
        {
            // Sends only if the amount was under the approval threshold; anything above it waits in
            // the approvals queue for a second pair of eyes, which is the whole point of the threshold.
            await refunds.SendAsync(payment, raised.Value, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        RefundRaised(logger, integrationEvent.SubOrderNumber, amount, raised.Value.Status);
    }

    /// <summary>
    /// Gives back money that was captured after its order had already been cancelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cancellation handler above ran while nothing had been captured, so it rightly refunded
    /// nothing. When the capture arrives later — a webhook held up, the unpaid-order sweeper first to
    /// act — this is the refund that cancellation would have raised, for everything still refundable.
    /// </para>
    /// <para>
    /// Governed by the same switch and the same approval threshold as every cancellation refund, and
    /// keyed on the payment, so a redelivery raises nothing and a resend asks the gateway under the
    /// same idempotency key.
    /// </para>
    /// </remarks>
    public async Task HandleAsync(
        PaymentCapturedOnCancelledOrder integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var governance = await settings.GetAsync<PaymentSettings>(cancellationToken).ConfigureAwait(false);

        if (!governance.AutoRefundOnCancellation)
        {
            LateCaptureNotRefunded(logger, integrationEvent.OrderNumber, integrationEvent.Amount);
            return;
        }

        var payment = await context.Payments
            .Include(candidate => candidate.Refunds)
            .FirstOrDefaultAsync(candidate => candidate.Id == integrationEvent.PaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (payment is null || payment.AmountRefundable <= 0m)
        {
            return;
        }

        var key = $"late-capture:{payment.Id}";

        var already = await context.Refunds
            .AnyAsync(refund => refund.IdempotencyKey == key, cancellationToken)
            .ConfigureAwait(false);

        if (already)
        {
            return;
        }

        var raised = await workflow
            .RaiseRefundAsync(
                payment,
                payment.AmountRefundable,
                $"Order {integrationEvent.OrderNumber} was cancelled before its payment was confirmed.",
                key,
                initiatedBy: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (raised.IsFailure)
        {
            RefundNotRaised(logger, integrationEvent.OrderNumber, raised.Error.Message);
            return;
        }

        await refunds.SendAsync(payment, raised.Value, cancellationToken).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        RefundRaised(logger, integrationEvent.OrderNumber, raised.Value.Amount, raised.Value.Status);
    }

    /// <summary>Stands down cash nobody is going to collect, because the parcel was cancelled.</summary>
    private async Task WaiveCashAsync(SubOrderCancelled integrationEvent, CancellationToken cancellationToken)
    {
        var cash = await context.CodCollections
            .FirstOrDefaultAsync(
                collection => collection.SubOrderId == integrationEvent.SubOrderId,
                cancellationToken)
            .ConfigureAwait(false);

        // A partial cancellation leaves the parcel going out with the rest of its lines, so the cash
        // is still owed — at a lower figure that the shipment carries, not this record.
        if (cash is null || integrationEvent.IsPartial)
        {
            return;
        }

        if (cash.Waive($"Order {integrationEvent.OrderNumber} was cancelled."))
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens the cash-on-delivery collection row, once per order.
    /// </summary>
    /// <remarks>
    /// It exists so that every order in this platform has a payment, whatever it was paid with — a
    /// reconciliation that could only see the gateway's orders would be a reconciliation with a hole
    /// in it the size of cash on delivery.
    /// </remarks>
    private async Task EnsureCodPaymentAsync(OrderPaymentView order, CancellationToken cancellationToken)
    {
        var key = $"cod:{order.OrderId}";

        var exists = await context.Payments
            .AnyAsync(payment => payment.IdempotencyKey == key, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        var payment = Payment.Open(
            order.OrderId,
            order.OrderNumber,
            order.CustomerId,
            PaymentProviders.InternalCod,
            order.AmountPayable,
            order.CurrencyCode,
            key,
            clock.UtcNow);

        context.Payments.Add(payment);
    }

    private static bool IsCashOnDelivery(string paymentMethod)
        => string.Equals(paymentMethod, "CashOnDelivery", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(EventId = 1610, Level = LogLevel.Information,
        Message = "Cash of {Amount} is expected at the door for sub-order {SubOrderNumber}.")]
    private static partial void CashExpected(ILogger logger, string subOrderNumber, decimal amount);

    [LoggerMessage(EventId = 1611, Level = LogLevel.Information,
        Message = "Raised a refund of {Amount} for cancelled sub-order {SubOrderNumber}. It is {Status}.")]
    private static partial void RefundRaised(
        ILogger logger,
        string subOrderNumber,
        decimal amount,
        RefundStatus status);

    [LoggerMessage(EventId = 1614, Level = LogLevel.Information,
        Message = "The refund of {Amount} for sub-order {SubOrderNumber} is held until its parcel is back "
                  + "with the seller: the order was cancelled after the courier collected it.")]
    private static partial void RefundHeld(ILogger logger, string subOrderNumber, decimal amount);

    [LoggerMessage(EventId = 1612, Level = LogLevel.Error,
        Message = "No refund was raised for cancelled sub-order {SubOrderNumber}: {Detail}")]
    private static partial void RefundNotRaised(ILogger logger, string subOrderNumber, string detail);

    [LoggerMessage(EventId = 1613, Level = LogLevel.Error,
        Message = "Order {OrderNumber} was cancelled and then paid {Amount}. Automatic refunds on "
                  + "cancellation are switched off, so it must be refunded by hand.")]
    private static partial void LateCaptureNotRefunded(ILogger logger, string orderNumber, decimal amount);
}
