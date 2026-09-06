using KlaraHome.Contracts.Orders;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Events;

/// <summary>
/// Keeps the parcels in step with the life of an order (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// A confirmed seller's part is a parcel waiting to be packed, and this is what puts it on the
/// packing list. The consignment is opened in <see cref="ShipmentStatus.Draft"/> with its lines, its
/// destination and its cash figure already on it, so a packer opens a queue rather than a form —
/// and so "what is waiting to go out" is a query rather than a join across two modules.
/// </para>
/// <para>
/// It is deliberately <b>not</b> a booking. Nothing is asked of a courier here: the parcel has not
/// been weighed, and a booking made on a guessed weight is a weight dispute with a courier who has
/// the parcel and the invoice. Booking happens when a human has put it on a scale.
/// </para>
/// <para>
/// A cancellation withdraws a parcel that has not left. One that has is not withdrawn, because it is
/// on a van: the ordering module only permits a post-dispatch cancellation by Operations, and what
/// follows it is a return, which is Step 17's.
/// </para>
/// <para>
/// Delivery is at-least-once, so both handlers are idempotent: a redelivered confirmation finds the
/// draft already open, and a redelivered cancellation finds it already withdrawn.
/// </para>
/// </remarks>
/// <param name="context">The Shipping data context.</param>
/// <param name="orders">Reads the destination and the lines, over the contract.</param>
/// <param name="options">Supplies the automatic-draft switch.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was opened and what was withdrawn.</param>
internal sealed partial class OrderLifecycleHandlers(
    ShippingDbContext context,
    IOrderFulfilment orders,
    IOptions<ShippingOptions> options,
    IClock clock,
    ILogger<OrderLifecycleHandlers> logger)
    : IIntegrationEventHandler<SubOrderConfirmed>,
        IIntegrationEventHandler<SubOrderCancelled>
{
    /// <summary>Opens the parcel a confirmed seller's part will be packed into.</summary>
    public async Task HandleAsync(SubOrderConfirmed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!options.Value.AutoDraftOnConfirmation)
        {
            return;
        }

        var exists = await context.Shipments
            .IgnoreQueryFilters()
            .AnyAsync(
                shipment => shipment.SubOrderId == integrationEvent.SubOrderId && !shipment.IsReturn,
                cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        var view = await orders.GetAsync(integrationEvent.SubOrderId, cancellationToken).ConfigureAwait(false);

        if (view.IsFailure)
        {
            // The order could not be read. Nothing is written, and the outbox will redeliver — which
            // is right: a parcel with no destination is worse than a parcel that appears a minute
            // late.
            OrderUnreadable(logger, integrationEvent.SubOrderNumber, view.Error.Message);
            return;
        }

        var order = view.Value;

        var shipment = Shipment.Draft(
            order.OrderId,
            order.OrderNumber,
            order.SubOrderId,
            order.SubOrderNumber,
            order.VendorId,
            order.CustomerId);

        shipment.Address(
            order.Destination.Pincode,
            order.Destination.StateId,
            order.IsCod ? order.AmountDueAtDelivery : null,
            order.DeclaredValue,
            freightCharged: 0m,
            order.CurrencyCode);

        foreach (var line in order.Lines)
        {
            shipment.Pack(ShipmentLine.Pack(
                shipment.Id,
                line.OrderLineId,
                line.Sku,
                line.Name,
                line.Quantity,
                line.UnitWeightGrams,
                line.LineTotal));
        }

        context.Shipments.Add(shipment);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        DraftOpened(logger, order.SubOrderNumber, order.Lines.Count, shipment.DeadWeightGrams);
    }

    /// <summary>Withdraws a parcel that was never handed over.</summary>
    public async Task HandleAsync(SubOrderCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var shipments = await context.Shipments
            .IgnoreQueryFilters()
            .Include(shipment => shipment.Lines)
            .Where(shipment => shipment.SubOrderId == integrationEvent.SubOrderId && !shipment.IsReturn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (shipments.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        var withdrawn = 0;

        foreach (var shipment in shipments)
        {
            // A partial cancellation leaves a parcel to send. The lines are left alone rather than
            // recomputed here: what is still going out is decided by the packer, who is the only one
            // who knows what is already in the box.
            if (integrationEvent.IsPartial && shipment.Status != ShipmentStatus.Draft)
            {
                continue;
            }

            if (shipment.Advance(ShipmentStatus.Cancelled, now, integrationEvent.Reason))
            {
                withdrawn++;
            }
        }

        if (withdrawn == 0)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Withdrawn(logger, integrationEvent.SubOrderNumber, withdrawn);
    }

    [LoggerMessage(EventId = 1770, Level = LogLevel.Information,
        Message = "Parcel opened for {SubOrderNumber}: {LineCount} line(s), {WeightGrams} g to pack.")]
    private static partial void DraftOpened(
        ILogger logger,
        string subOrderNumber,
        int lineCount,
        int weightGrams);

    [LoggerMessage(EventId = 1771, Level = LogLevel.Warning,
        Message = "No parcel was opened for {SubOrderNumber}: the order could not be read ({Detail}). "
                  + "The event will be redelivered.")]
    private static partial void OrderUnreadable(ILogger logger, string subOrderNumber, string detail);

    [LoggerMessage(EventId = 1772, Level = LogLevel.Information,
        Message = "{Count} parcel(s) for {SubOrderNumber} were withdrawn after a cancellation.")]
    private static partial void Withdrawn(ILogger logger, string subOrderNumber, int count);
}
