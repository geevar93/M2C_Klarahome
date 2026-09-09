using KlaraHome.Contracts.Inventory;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Modules.Orders.Application;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure.Events;
using KlaraHome.Modules.Orders.Infrastructure.Invoicing;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Orders.Infrastructure.Lifecycle;

/// <summary>How many units of one line a cancellation covers.</summary>
/// <param name="OrderLineId">The line.</param>
/// <param name="Quantity">How many units, clamped to what is left.</param>
internal sealed record CancellationLine(Guid OrderLineId, int Quantity);

/// <summary>
/// Every move a sub-order can make, and everything that has to happen alongside it.
/// </summary>
/// <remarks>
/// <para>
/// One place, deliberately. A transition is never just a column: confirming commits stock, packing
/// raises a tax invoice, cancelling gives back money and units, delivering starts the return window,
/// and every one of them writes the timeline, re-derives the order's status and announces itself. If
/// that lived in the handlers, the storefront's "cancel" and the admin's "cancel" would slowly stop
/// doing the same things.
/// </para>
/// <para>
/// It saves nothing. Every method mutates the tracked graph and enqueues the outbox rows, and the
/// caller commits — which is what keeps a transition and its announcement in one transaction
/// (ADR-003), and what lets the placement path use the same code inside its own.
/// </para>
/// </remarks>
/// <param name="invoices">Raises the tax invoice when a parcel is closed.</param>
/// <param name="stock">Settles the units held against the cart.</param>
/// <param name="promotions">Gives back a coupon when the whole order falls through.</param>
/// <param name="wallet">Gives back store credit when the whole order falls through.</param>
/// <param name="settings">Supplies the store's return window.</param>
/// <param name="events">Announces what happened.</param>
/// <param name="options">Supplies the fallback return window and the auto-invoice switch.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class SubOrderWorkflow(
    InvoiceService invoices,
    IStockAvailability stock,
    IPromotionLedger promotions,
    IStoreCredit wallet,
    IStoreSettings settings,
    OrdersEventPublisher events,
    IOptions<OrdersOptions> options,
    IClock clock)
{
    /// <summary>
    /// Moves a sub-order, with everything that goes with it.
    /// </summary>
    /// <remarks>
    /// The refusal is split in two on purpose. A move the machine has no edge for is a conflict —
    /// the sub-order is not where the caller thought it was. A move the machine has but this caller
    /// may not take is a forbidden — they are asking for something real that is not theirs to ask.
    /// Collapsing them would make "already packed" and "not your order" indistinguishable in a log.
    /// </remarks>
    /// <param name="order">The order, with its sub-orders and lines loaded.</param>
    /// <param name="subOrder">The seller's part to move.</param>
    /// <param name="next">Where to move it.</param>
    /// <param name="actor">Who is asking.</param>
    /// <param name="actorId">Which user, when there is one.</param>
    /// <param name="reason">Why, when a reason was given.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> TransitionAsync(
        Order order,
        SubOrder subOrder,
        SubOrderStatus next,
        OrderActor actor,
        Guid? actorId,
        string? reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(subOrder);

        var from = subOrder.Status;

        if (!SubOrderLifecycle.IsTransitionAllowed(from, next))
        {
            return Result.Failure(OrdersErrors.InvalidTransition(from, next));
        }

        if (!SubOrderLifecycle.IsAllowedFor(from, next, actor))
        {
            return Result.Failure(OrdersErrors.TransitionNotPermitted(next));
        }

        // Cancellation is not a plain transition — it gives back money and units, and it may be
        // partial — so it has its own path and this one refuses to be a shortcut into it.
        if (next == SubOrderStatus.Cancelled)
        {
            return await CancelAsync(
                    order,
                    subOrder,
                    lines: null,
                    InitiatorFor(actor),
                    actorId,
                    reason,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        var returnWindow = await ReturnWindowAsync(subOrder, cancellationToken).ConfigureAwait(false);

        if (!subOrder.TransitionTo(next, actor, now, returnWindow))
        {
            return Result.Failure(OrdersErrors.InvalidTransition(from, next));
        }

        order.Record(OrderEvent.StatusChange(
            order.Id,
            subOrder.Id,
            from,
            next,
            actor,
            actorId,
            reason ?? Narrate(next),
            IsCustomerVisible(next),
            now));

        await AfterTransitionAsync(order, subOrder, from, next, cancellationToken).ConfigureAwait(false);

        var derived = order.Rederive(now);
        events.StatusChanged(order, subOrder, from, actor, reason);

        if (next == SubOrderStatus.Confirmed)
        {
            events.Confirmed(order, subOrder);
        }

        AnnounceCompletion(order, derived);

        return Result.Success();
    }

    /// <summary>
    /// Cancels a sub-order, in whole or in part.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three initiators differ in what they are allowed to do, not in what happens afterwards. A
    /// shopper may cancel up to <c>Packed</c>; a seller may cancel what they cannot fulfil; and
    /// Operations may cancel after dispatch, with a reason, because somebody has to be able to recall
    /// a parcel.
    /// </para>
    /// <para>
    /// A partial cancellation deliberately does not move the sub-order. Three of five units going
    /// back leaves a sub-order that is still being packed and still being shipped, and forcing it
    /// into a "partially cancelled" state would double every branch of the machine for a case the
    /// line already describes.
    /// </para>
    /// <para>
    /// What happens to the units depends on whether they had been committed. Before confirmation
    /// they are still a reservation held against the cart, and the reservation is released when the
    /// <em>whole</em> order falls through — the hold is cart-scoped and cannot be released a seller
    /// at a time. After confirmation they have left stock, and only Inventory can put them back;
    /// <c>SubOrderCancelled</c> carries the quantities for it to do so.
    /// </para>
    /// </remarks>
    /// <param name="order">The order, with its sub-orders and lines loaded.</param>
    /// <param name="subOrder">The seller's part to cancel.</param>
    /// <param name="lines">Which units, or null for all of them.</param>
    /// <param name="initiator">Who asked.</param>
    /// <param name="actorId">Which user, when there is one.</param>
    /// <param name="reason">Why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> CancelAsync(
        Order order,
        SubOrder subOrder,
        IReadOnlyList<CancellationLine>? lines,
        CancellationInitiator initiator,
        Guid? actorId,
        string? reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(subOrder);

        var actor = ActorFor(initiator);
        var from = subOrder.Status;

        if (!SubOrderLifecycle.IsAllowedFor(from, SubOrderStatus.Cancelled, actor))
        {
            return Result.Failure(
                SubOrderLifecycle.IsTransitionAllowed(from, SubOrderStatus.Cancelled)
                    ? OrdersErrors.TransitionNotPermitted(SubOrderStatus.Cancelled)
                    : OrdersErrors.NotCancellable);
        }

        // After dispatch the parcel is with a third party, and a recall nobody has explained is a
        // dispute waiting to happen.
        if (from >= SubOrderStatus.Shipped && string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(OrdersErrors.CancellationReasonRequired);
        }

        var requested = lines?.ToDictionary(line => line.OrderLineId, line => line.Quantity);

        if (requested is not null && requested.Keys.Any(id => subOrder.Lines.All(line => line.Id != id)))
        {
            return Result.Failure(OrdersErrors.UnknownLine);
        }

        var wasConfirmed = SubOrderLifecycle.HasCommittedStock(from);
        var now = clock.UtcNow;

        List<OrderLineFact> cancelled = [];
        var cancelledTotal = 0m;

        foreach (var line in subOrder.Lines)
        {
            var wanted = requested is null
                ? line.QuantityLive
                : requested.GetValueOrDefault(line.Id);

            var taken = line.Cancel(wanted);

            if (taken == 0)
            {
                continue;
            }

            var value = Math.Round(line.LineTotal * taken / line.Quantity, 4, MidpointRounding.AwayFromZero);
            cancelledTotal += value;
            cancelled.Add(new OrderLineFact(line.Id, line.ListingId, line.Sku, taken, value));
        }

        if (cancelled.Count == 0)
        {
            return Result.Failure(OrdersErrors.NothingToCancel);
        }

        var isPartial = !subOrder.IsFullyCancelled;

        if (!isPartial)
        {
            subOrder.RecordCancellation(initiator, reason);

            if (!subOrder.TransitionTo(SubOrderStatus.Cancelled, actor, now, returnWindowDays: 0))
            {
                return Result.Failure(OrdersErrors.InvalidTransition(from, SubOrderStatus.Cancelled));
            }
        }

        order.Record(OrderEvent.Record(
            order.Id,
            subOrder.Id,
            OrderEventTypes.Cancelled,
            actor,
            actorId,
            isPartial
                ? $"{cancelled.Sum(line => line.Quantity)} item(s) cancelled by {initiator.ToString().ToLowerInvariant()}."
                : $"Cancelled by {initiator.ToString().ToLowerInvariant()}."
                  + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" {reason.Trim()}"),
            payload: null,
            visible: true,
            now));

        var derived = order.Rederive(now);

        events.Cancelled(order, subOrder, initiator, reason, isPartial, wasConfirmed, cancelledTotal, cancelled);

        if (!isPartial)
        {
            events.StatusChanged(order, subOrder, from, actor, reason);
        }

        // A cancellation can be what finishes an order: the last part still open is given up while
        // another has already completed, and §5.2 makes the whole order Completed. Settlement,
        // loyalty and the shopper's own "order complete" message all hang off OrderCompleted, so an
        // order that reaches the state down this path has to announce it exactly as one that
        // reached it down the other.
        AnnounceCompletion(order, derived);

        await SettleMoneyAndStockAsync(order, wasConfirmed, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Raises the tax invoice for a sub-order, on request rather than on dispatch.
    /// </summary>
    /// <remarks>
    /// The manual counterpart of the automatic issue at <c>Packed</c>. An operator needs it for the
    /// sub-order that was dispatched before the switch was turned on, and for the seller who asks for
    /// their invoice early.
    /// </remarks>
    /// <param name="order">The order.</param>
    /// <param name="subOrder">The seller's part.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Result<Invoice>> IssueInvoiceAsync(
        Order order,
        SubOrder subOrder,
        CancellationToken cancellationToken)
        => invoices.IssueAsync(order, subOrder, cancellationToken);

    /// <summary>
    /// Announces <c>OrderCompleted</c>, and only on the write that actually completed the order.
    /// </summary>
    /// <remarks>
    /// Gated on whether the derivation changed rather than on the state it landed in, so the event
    /// is published once by whichever write finished the order and never again by a later one that
    /// merely found it already finished.
    /// </remarks>
    /// <param name="order">The order, already re-derived.</param>
    /// <param name="derivedChanged">Whether that derivation moved the order's status.</param>
    private void AnnounceCompletion(Order order, bool derivedChanged)
    {
        if (derivedChanged && order.Status == OrderStatus.Completed)
        {
            events.Completed(order);
        }
    }

    /// <summary>Everything a particular transition drags along with it.</summary>
    private async Task AfterTransitionAsync(
        Order order,
        SubOrder subOrder,
        SubOrderStatus from,
        SubOrderStatus next,
        CancellationToken cancellationToken)
    {
        switch (next)
        {
            case SubOrderStatus.Confirmed when !SubOrderLifecycle.HasCommittedStock(from):
                // The units were held against the cart at checkout; this is where they leave stock
                // for good. Idempotent by contract, so a second confirmation settles nothing twice.
                await stock.SettleAsync(
                        ReservationReferenceTypes.Cart,
                        order.CartId,
                        ReservationOutcome.Committed,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            case SubOrderStatus.Packed when options.Value.AutoInvoiceOnPacked:
                // A tax invoice travels with the goods, so the moment the parcel is closed is the
                // moment it is needed. A failure here is swallowed rather than blocking a dispatch:
                // the invoice can be raised by hand, and a parcel that cannot leave is worse.
                await invoices.IssueAsync(order, subOrder, cancellationToken).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Gives back what a cancellation freed, once it is clear the whole order has fallen through.
    /// </summary>
    /// <remarks>
    /// Deliberately order-level rather than sub-order-level, because both things being given back
    /// are: a coupon was redeemed against the order, store credit was spent on the order, and the
    /// stock reservation is held against the cart the order came from. Reversing any of them while
    /// one seller is still shipping would take a discount off a sale that is still happening.
    /// </remarks>
    private async Task SettleMoneyAndStockAsync(Order order, bool wasConfirmed, CancellationToken cancellationToken)
    {
        if (order.Status != OrderStatus.Cancelled)
        {
            return;
        }

        await promotions.ReverseAsync(order.Id, cancellationToken).ConfigureAwait(false);

        if (order.WalletApplied > 0m)
        {
            // Idempotent on the reference, so a second full cancellation of an already-cancelled
            // order credits nothing. Store credit given away twice is money.
            await wallet
                .CreditAsync(
                    order.CustomerId,
                    order.WalletApplied,
                    StoreCreditReasons.OrderCancelled,
                    "order",
                    order.Id,
                    expiresAt: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!wasConfirmed)
        {
            // Nothing ever left stock: the units are still a reservation against the cart, and this
            // puts them straight back on sale rather than waiting for the hold to lapse.
            await stock.SettleAsync(
                    ReservationReferenceTypes.Cart,
                    order.CartId,
                    ReservationOutcome.Released,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// How long the shopper has to send this seller's parcel back.
    /// </summary>
    /// <remarks>
    /// The longest window any product in the parcel carries, falling back to the store's own. The
    /// longest rather than the shortest, because the parcel goes back in one collection and telling a
    /// shopper that two of their four items are out of time is a support call nobody wins.
    /// </remarks>
    private async Task<int> ReturnWindowAsync(SubOrder subOrder, CancellationToken cancellationToken)
    {
        int storeWindow;

        try
        {
            var commerce = await settings.GetAsync<CommerceSettings>(cancellationToken).ConfigureAwait(false);
            storeWindow = commerce.ReturnWindowDays;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A settings read that failed must not leave an order with no window at all, which would
            // be a shopper with no right to return anything.
            storeWindow = options.Value.FallbackReturnWindowDays;
        }

        var productWindows = subOrder.Lines
            .Where(line => line.Snapshot.IsReturnable)
            .Select(line => line.Snapshot.ReturnWindowDays ?? storeWindow)
            .ToArray();

        return productWindows.Length == 0 ? 0 : Math.Max(storeWindow, productWindows.Max());
    }

    /// <summary>The initiator a transition actor stands for.</summary>
    private static CancellationInitiator InitiatorFor(OrderActor actor)
        => actor switch
        {
            OrderActor.Customer => CancellationInitiator.Customer,
            OrderActor.Vendor => CancellationInitiator.Vendor,
            OrderActor.Platform => CancellationInitiator.Platform,
            _ => CancellationInitiator.System,
        };

    /// <summary>The transition actor an initiator stands for.</summary>
    private static OrderActor ActorFor(CancellationInitiator initiator)
        => initiator switch
        {
            CancellationInitiator.Customer => OrderActor.Customer,
            CancellationInitiator.Vendor => OrderActor.Vendor,
            CancellationInitiator.Platform => OrderActor.Platform,
            _ => OrderActor.System,
        };

    /// <summary>Whether the shopper sees this transition on "track my order".</summary>
    /// <remarks>
    /// Everything except the states that only mean something inside the business. A shopper is told
    /// their parcel is out for delivery; they are not told it moved from Confirmed to Processing,
    /// which is a seller's queue and not an event in the life of their order.
    /// </remarks>
    private static bool IsCustomerVisible(SubOrderStatus status)
        => status is not (SubOrderStatus.Processing or SubOrderStatus.ReturnInProgress);

    /// <summary>What the timeline says about a transition when nobody gave a reason.</summary>
    private static string Narrate(SubOrderStatus status)
        => status switch
        {
            SubOrderStatus.Confirmed => "Order confirmed.",
            SubOrderStatus.PendingPayment => "Awaiting payment.",
            SubOrderStatus.PaymentFailed => "Payment was not completed.",
            SubOrderStatus.Processing => "The seller is preparing your order.",
            SubOrderStatus.Packed => "Packed and ready for dispatch.",
            SubOrderStatus.Shipped => "Handed to the courier.",
            SubOrderStatus.OutForDelivery => "Out for delivery.",
            SubOrderStatus.DeliveryFailed => "Delivery could not be completed. The courier will try again.",
            SubOrderStatus.RtoInitiated => "On its way back to the seller.",
            SubOrderStatus.RtoDelivered => "Returned to the seller.",
            SubOrderStatus.Delivered => "Delivered.",
            SubOrderStatus.ReturnRequested => "Return requested.",
            SubOrderStatus.ReturnInProgress => "Return in progress.",
            SubOrderStatus.Returned => "Return complete.",
            SubOrderStatus.Completed => "Order complete.",
            _ => "Order updated.",
        };
}
