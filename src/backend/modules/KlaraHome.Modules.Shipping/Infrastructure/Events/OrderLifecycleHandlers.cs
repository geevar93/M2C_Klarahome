using KlaraHome.Contracts.Orders;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
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
/// A cancellation withdraws a parcel that has not left. One that has is on a van: the ordering
/// module only permits that cancellation by Operations and only for the whole seller's part, and
/// <see cref="CourierReturns"/> marks the parcel to come back and tells the courier as soon as the
/// courier will take the instruction.
/// </para>
/// <para>
/// A booked parcel is withdrawn through <see cref="CourierCancellation"/>, which tells the courier
/// before changing our record. The order is still <c>Packed</c> while its parcel sits labelled and
/// waiting for a pickup, so a shopper can cancel it then — and a courier nobody told still sends a
/// driver and still charges the freight.
/// </para>
/// <para>
/// A partial cancellation withdraws only the parcels that hold a cancelled line, and then
/// <see cref="PackingQueue"/> opens a fresh draft for what is still owed. A booked parcel cannot be
/// edited — its waybill carries the old weight, value and, on a cash order, the old amount to
/// collect — so it is cancelled and rebooked rather than left to go out wrong.
/// </para>
/// <para>
/// Delivery is at-least-once, so both handlers are idempotent: a redelivered confirmation finds the
/// draft already open, and a redelivered cancellation finds it already withdrawn.
/// </para>
/// </remarks>
/// <param name="context">The Shipping data context.</param>
/// <param name="orders">Reads the destination and the lines, over the contract.</param>
/// <param name="options">Supplies the automatic-draft switch.</param>
/// <param name="couriers">Withdraws a parcel, telling its courier first.</param>
/// <param name="packing">Reopens a parcel for what a partial cancellation left.</param>
/// <param name="returns">Brings back a parcel the courier had already collected.</param>
/// <param name="logger">Reports what was opened and what was withdrawn.</param>
internal sealed partial class OrderLifecycleHandlers(
    ShippingDbContext context,
    IOrderFulfilment orders,
    IOptions<ShippingOptions> options,
    CourierCancellation couriers,
    PackingQueue packing,
    CourierReturns returns,
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
    /// <remarks>
    /// A courier that cannot be reached does not fail the event. The parcel is marked and left booked
    /// for the retry sweep, because throwing here would hold back every other handler of the
    /// cancellation — the shopper's refund among them — until the courier answered.
    /// </remarks>
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

        var withdrawn = 0;
        var pending = 0;
        var recalled = 0;

        // On a partial cancellation, only the parcels carrying a cancelled line are affected: another
        // seller's box is not in this sub-order, and a parcel of this one that holds none of the
        // cancelled units is still exactly right.
        var cancelledLines = integrationEvent.Lines
            .Select(line => line.OrderLineId)
            .ToHashSet();

        foreach (var shipment in shipments)
        {
            if (integrationEvent.IsPartial
                && !shipment.Lines.Any(line => cancelledLines.Contains(line.OrderLineId)))
            {
                continue;
            }

            var outcome = await couriers
                .WithdrawAsync(shipment, integrationEvent.Reason, cancellationToken)
                .ConfigureAwait(false);

            switch (outcome)
            {
                case CourierWithdrawal.Withdrawn:
                    withdrawn++;
                    break;

                case CourierWithdrawal.Pending:
                    pending++;
                    break;

                // Already collected. Only a whole seller's part may be cancelled after dispatch, so
                // everything in this parcel is unwanted and all of it comes back.
                case CourierWithdrawal.NotWithdrawable when !integrationEvent.IsPartial:
                    if (await returns.RequestAsync(shipment, integrationEvent.Reason, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        recalled++;
                    }

                    break;

                default:
                    break;
            }
        }

        if (withdrawn > 0 || pending > 0 || recalled > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (withdrawn > 0)
        {
            Withdrawn(logger, integrationEvent.SubOrderNumber, withdrawn);
        }

        // Committed first, because the packer counts what other parcels hold from the database. Run
        // on every partial cancellation rather than only after a withdrawal here, so a redelivery
        // after a crash between the two commits still reopens the parcel.
        if (integrationEvent.IsPartial
            && await packing.ReopenAsync(integrationEvent.SubOrderId, cancellationToken).ConfigureAwait(false))
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
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
