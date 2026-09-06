using KlaraHome.Contracts.Carts;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Returns;
using KlaraHome.Contracts.Settlements;
using KlaraHome.Contracts.Shipping;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Reporting.Domain;
using KlaraHome.Modules.Reporting.Infrastructure.Features;
using KlaraHome.Modules.Reporting.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Reporting.Infrastructure.Ingest;

/// <summary>
/// Turns the platform's integration events into this module's facts
/// (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// The whole of the module's write path. Thirteen subscriptions across six modules, and every one of
/// them writes a row that is denormalised at the moment it lands: the category, the seller, the
/// payment method and the product's name are all put on the fact row here, so that a report is later
/// a filtered aggregation over one table with no join in it. That is the price of the module
/// boundary and it is paid once, on the write, rather than on every read.
/// </para>
/// <para>
/// Delivery is at-least-once, so every handler is idempotent, and the two mechanisms are chosen per
/// case rather than applied uniformly. Where a fact has a natural key — an order line, a return line,
/// a settlement period — the row is looked up and updated, so a redelivery produces the same row.
/// Where it does not, because the fact is an <em>occurrence</em> rather than a thing, the row is
/// keyed on the event id and a redelivery fails the unique index: a payment capture counted twice
/// would overstate a day's takings and nothing would ever notice.
/// </para>
/// <para>
/// Everything here is guarded by <c>reporting.fact-ingest</c>, and that flag is the one in this
/// module that is not safe to leave off. There is no back-fill: the outbox moves on, and a period
/// with the ingest disabled is a permanent hole in every report over it.
/// </para>
/// </remarks>
/// <param name="context">The Reporting data context.</param>
/// <param name="catalogue">Resolves the category and brand a fact row is denormalised with.</param>
/// <param name="flags">Whether facts are being recorded at all.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what could not be resolved.</param>
internal sealed partial class CommerceFactHandlers(
    ReportingDbContext context,
    IProductProjectionSource catalogue,
    IFeatureFlags flags,
    IClock clock,
    ILogger<CommerceFactHandlers> logger)
    : IIntegrationEventHandler<OrderPlaced>,
        IIntegrationEventHandler<SubOrderConfirmed>,
        IIntegrationEventHandler<SubOrderCancelled>,
        IIntegrationEventHandler<SubOrderStatusChanged>,
        IIntegrationEventHandler<OrderCompleted>,
        IIntegrationEventHandler<PaymentCaptured>,
        IIntegrationEventHandler<RefundProcessed>,
        IIntegrationEventHandler<CodCashRecorded>,
        IIntegrationEventHandler<CartAbandoned>,
        IIntegrationEventHandler<CartConverted>,
        IIntegrationEventHandler<ShipmentDelivered>,
        IIntegrationEventHandler<ReturnRequested>,
        IIntegrationEventHandler<ReturnClosed>,
        IIntegrationEventHandler<SettlementCycleClosed>
{
    /// <inheritdoc />
    /// <remarks>
    /// The order-level row, written at placement rather than at confirmation. It is the only fact in
    /// the module that records something which may never become a sale, and that is exactly what the
    /// funnel needs it for: without it, "how many orders were placed and never paid for" has no
    /// numerator.
    /// </remarks>
    public async Task HandleAsync(OrderPlaced integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!await EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var existing = await context.Orders
            .FirstOrDefaultAsync(fact => fact.OrderId == integrationEvent.OrderId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return;
        }

        context.Orders.Add(OrderFact.Record(
            integrationEvent.OrderId,
            integrationEvent.OrderNumber,
            integrationEvent.CustomerId,
            integrationEvent.PaymentMethod,
            integrationEvent.GrandTotal,
            integrationEvent.AmountPayable,
            integrationEvent.CurrencyCode,
            integrationEvent.VendorIds.Count,
            integrationEvent.Status,
            integrationEvent.PlacedAt,
            DayOf(integrationEvent.PlacedAt)));

        AddFunnelStep(
            integrationEvent.EventId,
            FunnelStep.OrderPlaced,
            cartId: integrationEvent.CartId,
            orderId: integrationEvent.OrderId,
            customerId: integrationEvent.CustomerId,
            value: integrationEvent.GrandTotal,
            itemCount: 0,
            at: integrationEvent.PlacedAt);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Where the sale is recorded, and the one handler in this class that reaches outside the module.
    /// The event carries the listing, the SKU, the quantity and the money; it does not carry the
    /// category, the brand or the product's name, because those are the catalogue's and no event has
    /// any business copying the whole catalogue onto itself. They are resolved once, here, and frozen
    /// onto the fact row.
    /// </para>
    /// <para>
    /// Freezing them is deliberate rather than lazy. A product moved to a different category next
    /// year did not retrospectively sell in that category last March, and a report that re-resolved
    /// the category at read time would rewrite history every time a merchandiser tidied the tree.
    /// </para>
    /// </remarks>
    public async Task HandleAsync(SubOrderConfirmed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!await EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var lineIds = integrationEvent.Lines.Select(line => line.OrderLineId).ToArray();

        var already = await context.OrderLines
            .Where(fact => lineIds.Contains(fact.OrderLineId))
            .Select(fact => fact.OrderLineId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pending = integrationEvent.Lines.Where(line => !already.Contains(line.OrderLineId)).ToList();

        if (pending.Count == 0)
        {
            return;
        }

        var order = await context.Orders
            .FirstOrDefaultAsync(fact => fact.OrderId == integrationEvent.OrderId, cancellationToken)
            .ConfigureAwait(false);

        var paymentMethod = order?.PaymentMethod ?? "Unknown";
        var confirmedAt = integrationEvent.OccurredAtUtc == default ? clock.UtcNow : integrationEvent.OccurredAtUtc;

        var descriptors = await ResolveAsync(
                pending.Select(line => line.ListingId).Distinct().ToArray(),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var line in pending)
        {
            descriptors.TryGetValue(line.ListingId, out var descriptor);

            context.OrderLines.Add(SaleLineFact.Record(new SaleLineFactValues(
                line.OrderLineId,
                integrationEvent.OrderId,
                integrationEvent.OrderNumber,
                integrationEvent.SubOrderId,
                integrationEvent.VendorId,
                integrationEvent.CustomerId,
                line.ListingId,
                descriptor?.VariantId,
                descriptor?.ProductId,
                descriptor?.CategoryId,
                descriptor?.CategoryName,
                descriptor?.BrandId,
                descriptor?.BrandName,
                line.Sku,

                // The catalogue's current name where it still has one, and the SKU where it does not.
                // A blank product column in a top-sellers table is worse than a SKU nobody can read.
                descriptor?.ProductName ?? line.Sku,
                line.Quantity,
                line.LineTotal,
                paymentMethod,
                confirmedAt,
                DayOf(confirmedAt))));
        }

        AddFunnelStep(
            integrationEvent.EventId,
            FunnelStep.OrderConfirmed,
            cartId: null,
            orderId: integrationEvent.OrderId,
            customerId: integrationEvent.CustomerId,
            value: integrationEvent.Total,
            itemCount: integrationEvent.Lines.Count,
            at: confirmedAt);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Subtracted from the line rather than deleting it, because "what fraction of what we sold got
    /// cancelled" is a number a store is run on and a deleted row cannot answer it. A cancellation
    /// before confirmation finds no line at all, which is correct: nothing was ever counted as sold.
    /// </remarks>
    public async Task HandleAsync(SubOrderCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!await EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var lineIds = integrationEvent.Lines.Select(line => line.OrderLineId).ToArray();

        var facts = await context.OrderLines
            .Where(fact => lineIds.Contains(fact.OrderLineId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (facts.Count == 0)
        {
            return;
        }

        // Keyed on the event so a redelivery does not subtract twice. This is the one update path in
        // the module where the natural key is not enough on its own: the same line can legitimately
        // be cancelled twice in two parts, so "has this line already been reduced" is not the
        // question — "have I already applied this message" is.
        if (await SeenAsync(integrationEvent.EventId, "reporting.cancellation", cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        foreach (var line in integrationEvent.Lines)
        {
            var fact = facts.Find(row => row.OrderLineId == line.OrderLineId);

            fact?.RecordCancellation(line.Quantity, line.LineTotal);
        }

        MarkSeen(integrationEvent.EventId, "reporting.cancellation");

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only the order-level row follows the status. A sub-order's own progress is not a fact any of
    /// the thirteen reports is about, and recording every transition of every seller's parcel would
    /// be the highest-volume write in the module for no question anybody asks.
    /// </remarks>
    public async Task HandleAsync(SubOrderStatusChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!await EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var order = await context.Orders
            .FirstOrDefaultAsync(fact => fact.OrderId == integrationEvent.OrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return;
        }

        order.Advance(integrationEvent.OrderStatus);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task HandleAsync(OrderCompleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!await EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var order = await context.Orders
            .FirstOrDefaultAsync(fact => fact.OrderId == integrationEvent.OrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return;
        }

        order.Advance("Completed", integrationEvent.CompletedAt);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A capture is both a movement of money and a step of the funnel, and it is recorded as both.
    /// The funnel row carries the step in its unique key precisely so one event can be two facts
    /// without either of them being the other's duplicate.
    /// </remarks>
    public async Task HandleAsync(PaymentCaptured integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!await EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var at = integrationEvent.CapturedAt;

        await RecordPaymentAsync(
                integrationEvent.EventId,
                PaymentFactKind.Captured,
                integrationEvent.OrderId,
                integrationEvent.PaymentId,
                integrationEvent.Method,
                integrationEvent.Amount,
                integrationEvent.CurrencyCode,
                at,
                cancellationToken)
            .ConfigureAwait(false);

        AddFunnelStep(
            integrationEvent.EventId,
            FunnelStep.OrderPaid,
            cartId: null,
            orderId: integrationEvent.OrderId,
            customerId: null,
            value: integrationEvent.Amount,
            itemCount: 0,
            at: at);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task HandleAsync(RefundProcessed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return RecordMoneyAsync(
            integrationEvent.EventId,
            PaymentFactKind.Refunded,
            integrationEvent.OrderId,
            integrationEvent.RefundId,
            "Refund",
            integrationEvent.Amount,
            integrationEvent.CurrencyCode,
            integrationEvent.CompletedAt,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Cash at the door is kept apart from a gateway capture rather than folded into it. They are the
    /// same money to a finance team and completely different to an operations one — a capture is
    /// settled by a gateway on a known cycle, and cash is a courier owing the store a remittance.
    /// </remarks>
    public Task HandleAsync(CodCashRecorded integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return RecordMoneyAsync(
            integrationEvent.EventId,
            PaymentFactKind.CodCollected,
            integrationEvent.OrderId,
            integrationEvent.CollectionId,
            "Cod",
            integrationEvent.Amount,
            integrationEvent.CurrencyCode,
            integrationEvent.OccurredAt,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleAsync(CartAbandoned integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return RecordFunnelAsync(
            integrationEvent.EventId,
            FunnelStep.CartAbandoned,
            integrationEvent.CartId,
            orderId: null,
            integrationEvent.CustomerId,
            integrationEvent.EstimatedValue,
            integrationEvent.LineCount,
            integrationEvent.LastActivityAt,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleAsync(CartConverted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return RecordFunnelAsync(
            integrationEvent.EventId,
            FunnelStep.CartConverted,
            integrationEvent.CartId,
            integrationEvent.OrderId,
            integrationEvent.CustomerId,
            integrationEvent.GrandTotal,
            itemCount: 0,
            OccurredAt(integrationEvent.OccurredAtUtc),
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Recorded on the lines rather than on the order, because delivery is per parcel and a
    /// multi-seller order has several. It is what makes "how long from confirmation to the doorstep"
    /// answerable per seller, which is the measure a marketplace manages its sellers on.
    /// </remarks>
    public async Task HandleAsync(ShipmentDelivered integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!await EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var lines = await context.OrderLines
            .Where(fact => fact.SubOrderId == integrationEvent.SubOrderId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (lines.Count == 0)
        {
            return;
        }

        var at = integrationEvent.DeliveredAt;

        foreach (var line in lines)
        {
            line.RecordDelivery(at);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Recorded when the return is <em>asked for</em> rather than when it is accepted, because the
    /// number a buying team acts on is how many people wanted to send something back — a refusal is
    /// a decision the store made, not evidence that the product was fine.
    /// </remarks>
    public async Task HandleAsync(ReturnRequested integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!await EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var lineIds = integrationEvent.Lines.Select(line => line.ReturnLineId).ToArray();

        var already = await context.ReturnLines
            .Where(fact => lineIds.Contains(fact.ReturnLineId))
            .Select(fact => fact.ReturnLineId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pending = integrationEvent.Lines.Where(line => !already.Contains(line.ReturnLineId)).ToList();

        if (pending.Count == 0)
        {
            return;
        }

        var at = integrationEvent.RequestedAt;

        // The order lines this return is against were already denormalised when the sale was
        // recorded, so the category comes off them rather than out of the catalogue a second time.
        var orderLineIds = pending.Select(line => line.OrderLineId).ToArray();

        var sold = await context.OrderLines
            .Where(fact => orderLineIds.Contains(fact.OrderLineId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var line in pending)
        {
            var source = sold.Find(fact => fact.OrderLineId == line.OrderLineId);

            context.ReturnLines.Add(ReturnedLineFact.Record(new ReturnedLineFactValues(
                line.ReturnLineId,
                integrationEvent.ReturnId,
                line.OrderLineId,
                integrationEvent.OrderId,
                integrationEvent.SubOrderId,
                integrationEvent.VendorId,
                source?.VariantId,
                source?.ProductId,
                source?.CategoryId,
                source?.CategoryName,
                line.Sku,

                // The reason is on the return rather than on its lines: a shopper chooses one reason
                // for the parcel, which is the grain the report is written at.
                integrationEvent.ReasonCode,
                line.Quantity,
                line.RefundValue,
                "Requested",
                at,
                DayOf(at))));

            source?.RecordReturn(line.Quantity, line.RefundValue);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task HandleAsync(ReturnClosed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!await EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var lines = await context.ReturnLines
            .Where(fact => fact.ReturnId == integrationEvent.ReturnId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (lines.Count == 0)
        {
            return;
        }

        var at = OccurredAt(integrationEvent.OccurredAtUtc);

        foreach (var line in lines)
        {
            line.Advance(integrationEvent.Outcome, closedAt: at);
        }


        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The one event that carries money the platform earned rather than money it moved. The
    /// commission it names is written back onto the order lines as well as into the settlement fact,
    /// which is what makes the take rate in the GMV report a real number rather than a nought — and
    /// it is why <c>fact_order_lines.commission_amount</c> is empty until a period closes.
    /// </remarks>
    public async Task HandleAsync(SettlementCycleClosed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!await EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var existing = await context.Settlements
            .AnyAsync(fact => fact.PeriodId == integrationEvent.CycleId, cancellationToken)
            .ConfigureAwait(false);

        if (existing)
        {
            return;
        }

        var at = integrationEvent.ClosedAt;

        // The order count is the distinct orders this seller had lines confirmed in over the period,
        // counted from this module's own facts. The event does not carry it, and asking Settlements
        // for it would be a second seam for a number already sitting in the next table along.
        var orderCount = await context.OrderLines
            .Where(fact => fact.VendorId == integrationEvent.VendorId)
            .Where(fact => fact.ConfirmedAt >= integrationEvent.PeriodStart)
            .Where(fact => fact.ConfirmedAt < integrationEvent.PeriodEnd)
            .Select(fact => fact.OrderId)
            .Distinct()
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        context.Settlements.Add(SettlementFact.Record(new SettlementFactValues(
            integrationEvent.CycleId,
            integrationEvent.VendorId,
            integrationEvent.PeriodStart,
            integrationEvent.PeriodEnd,
            DayOf(at),
            integrationEvent.GrossSales,
            integrationEvent.TotalCommission,
            integrationEvent.TotalFees,
            integrationEvent.Tcs,
            integrationEvent.Tds,
            integrationEvent.TotalRefunds,
            integrationEvent.NetPayable,
            integrationEvent.CurrencyCode,
            orderCount,
            at)));

        // The commission is written back onto the lines the period settled, which is what makes the
        // take rate in the GMV report a real number. Apportioned by each line's share of the period's
        // gross, because the event states one figure for the whole cycle and the frozen per-line rate
        // lives in Ordering where this module may not read it.
        await ApportionCommissionAsync(integrationEvent, cancellationToken).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Spreads a cycle's commission across the lines it settled, in proportion to their value.
    /// </summary>
    /// <remarks>
    /// An apportionment rather than the real per-line figure, and it is worth being honest about
    /// why. Ordering freezes a commission rate on every line at placement and Settlements reads it
    /// there; this module may read neither. What it has is one total per cycle and the lines that
    /// cycle covered, so it divides the first across the second by value — which is exactly right
    /// where a seller is on a flat rate, and approximate where their plan has per-category rates.
    /// The cycle-level figure in the settlement summary is always exact; only the daily take-rate
    /// cut is an estimate, and it is the cut where an estimate is acceptable.
    /// </remarks>
    private async Task ApportionCommissionAsync(
        SettlementCycleClosed integrationEvent,
        CancellationToken cancellationToken)
    {
        if (integrationEvent.TotalCommission <= 0m)
        {
            return;
        }

        var lines = await context.OrderLines
            .Where(fact => fact.VendorId == integrationEvent.VendorId)
            .Where(fact => fact.ConfirmedAt >= integrationEvent.PeriodStart)
            .Where(fact => fact.ConfirmedAt < integrationEvent.PeriodEnd)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var total = lines.Sum(line => line.NetAmount);

        if (lines.Count == 0 || total <= 0m)
        {
            return;
        }

        foreach (var line in lines)
        {
            line.RecordCommission(
                Math.Round(
                    integrationEvent.TotalCommission * line.NetAmount / total,
                    4,
                    MidpointRounding.AwayFromZero));
        }
    }

    /// <summary>Whether facts are being recorded at all.</summary>
    private async Task<bool> EnabledAsync(CancellationToken cancellationToken)
        => await flags.IsEnabledAsync(ReportingFeatures.FactIngest, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Records one movement of money and saves.</summary>
    private async Task RecordMoneyAsync(
        Guid eventId,
        PaymentFactKind kind,
        Guid? orderId,
        Guid? paymentId,
        string method,
        decimal amount,
        string currencyCode,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (!await EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await RecordPaymentAsync(eventId, kind, orderId, paymentId, method, amount, currencyCode, at, cancellationToken)
            .ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds a payment fact if this event has not already produced one.</summary>
    /// <remarks>
    /// Checked as well as being enforced by the unique index. The index is what actually holds under a
    /// concurrent redelivery; this is what stops the common case from throwing and rolling back a
    /// transaction that also carries a funnel row.
    /// </remarks>
    private async Task RecordPaymentAsync(
        Guid eventId,
        PaymentFactKind kind,
        Guid? orderId,
        Guid? paymentId,
        string method,
        decimal amount,
        string currencyCode,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var already = await context.Payments
            .AnyAsync(fact => fact.SourceEventId == eventId, cancellationToken)
            .ConfigureAwait(false);

        if (already)
        {
            return;
        }

        context.Payments.Add(PaymentFact.Record(
            eventId,
            kind,
            orderId,
            paymentId,
            method,
            amount,
            currencyCode,
            at,
            DayOf(at)));
    }

    /// <summary>Records one funnel step and saves.</summary>
    private async Task RecordFunnelAsync(
        Guid eventId,
        FunnelStep step,
        Guid? cartId,
        Guid? orderId,
        Guid? customerId,
        decimal value,
        int itemCount,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (!await EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var already = await context.FunnelEvents
            .AnyAsync(fact => fact.SourceEventId == eventId && fact.Step == step, cancellationToken)
            .ConfigureAwait(false);

        if (already)
        {
            return;
        }

        AddFunnelStep(eventId, step, cartId, orderId, customerId, value, itemCount, at);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Queues a funnel row into the caller's own unit of work.</summary>
    private void AddFunnelStep(
        Guid eventId,
        FunnelStep step,
        Guid? cartId,
        Guid? orderId,
        Guid? customerId,
        decimal value,
        int itemCount,
        DateTimeOffset at)
        => context.FunnelEvents.Add(FunnelFact.Record(
            eventId,
            step,
            cartId,
            orderId,
            customerId,
            value,
            itemCount,
            at,
            DayOf(at)));

    /// <summary>Whether a named handler has already processed this message.</summary>
    private async Task<bool> SeenAsync(Guid eventId, string handler, CancellationToken cancellationToken)
        => await context.InboxMessages
            .AnyAsync(message => message.MessageId == eventId && message.Handler == handler, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Records that it has, in the caller's own unit of work.</summary>
    private void MarkSeen(Guid eventId, string handler)
        => context.InboxMessages.Add(new InboxMessage
        {
            MessageId = eventId,
            Handler = handler,
            ProcessedAt = clock.UtcNow,
        });

    /// <summary>
    /// The catalogue facts a sold line is frozen with, keyed on the listing.
    /// </summary>
    /// <remarks>
    /// One call for the whole sub-order. A listing the catalogue no longer has resolves to nothing
    /// and the fact row is written without a category rather than not written at all — losing the
    /// sale from the totals would be a far worse answer than losing its category from one cut.
    /// </remarks>
    private async Task<Dictionary<Guid, ListingDescriptor>> ResolveAsync(
        Guid[] listingIds,
        CancellationToken cancellationToken)
    {
        var descriptors = new Dictionary<Guid, ListingDescriptor>();

        if (listingIds.Length == 0)
        {
            return descriptors;
        }

        var variants = await catalogue.FindVariantsOfAsync(listingIds, cancellationToken).ConfigureAwait(false);

        if (variants.Count == 0)
        {
            UnresolvedListings(logger, listingIds.Length);
            return descriptors;
        }

        var projections = await catalogue
            .FindByVariantsAsync([.. variants.Values.Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var byListing = projections.ToDictionary(projection => projection.ListingId);

        foreach (var listingId in listingIds)
        {
            if (byListing.TryGetValue(listingId, out var projection))
            {
                descriptors[listingId] = new ListingDescriptor(
                    projection.VariantId,
                    projection.ProductId,
                    projection.ProductName,
                    projection.CategoryId,
                    projection.CategoryName,
                    projection.BrandId,
                    projection.BrandName);
            }
        }

        return descriptors;
    }

    /// <summary>When the event says it happened, or now for one that did not say.</summary>
    private DateTimeOffset OccurredAt(DateTimeOffset occurredAtUtc)
        => occurredAtUtc == default ? clock.UtcNow : occurredAtUtc;

    /// <summary>The reporting day an instant falls on.</summary>
    /// <remarks>
    /// UTC, and the same conversion everywhere in the module so a report's filter and its grouping
    /// cannot disagree about which day a midnight sale belongs to. Presenting it in the store's
    /// timezone is the admin app's job.
    /// </remarks>
    private static DateOnly DayOf(DateTimeOffset at) => DateOnly.FromDateTime(at.UtcDateTime);

    /// <summary>What a sold line is frozen with.</summary>
    /// <param name="VariantId">The sellable thing.</param>
    /// <param name="ProductId">Its product.</param>
    /// <param name="ProductName">The product's name at the time of sale.</param>
    /// <param name="CategoryId">Its category.</param>
    /// <param name="CategoryName">The category's name at the time of sale.</param>
    /// <param name="BrandId">Its brand.</param>
    /// <param name="BrandName">The brand's name.</param>
    private sealed record ListingDescriptor(
        Guid VariantId,
        Guid ProductId,
        string ProductName,
        Guid CategoryId,
        string CategoryName,
        Guid? BrandId,
        string? BrandName);

    [LoggerMessage(
        EventId = 8210,
        Level = LogLevel.Warning,
        Message = "Reporting could not resolve {ListingCount} listing(s); their fact rows carry no category")]
    private static partial void UnresolvedListings(ILogger logger, int listingCount);
}
