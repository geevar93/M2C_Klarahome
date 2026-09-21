using KlaraHome.Contracts.Orders;
using KlaraHome.Modules.Shipping.Application.Shipments;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;

/// <summary>
/// Puts what a seller still owes back on the packing list after a parcel was withdrawn.
/// </summary>
/// <remarks>
/// <para>
/// A partial cancellation withdraws the parcel that held the cancelled units — booked or not —
/// because its contents, weight, declared value and, on a cash order, the amount the courier
/// collects are all now wrong, and a waybill cannot be edited once it is issued. What is left of the
/// order still has to go out, so a fresh draft is opened for it, packed from the order as it now
/// stands: the same packer the booking screen uses, so the cash figure and the lines come from one
/// place.
/// </para>
/// <para>
/// Idempotent, because the cancellation event it runs from is redelivered: an open draft already
/// covering the sub-order, or nothing left to send, and it does nothing.
/// </para>
/// </remarks>
/// <param name="context">The Shipping data context.</param>
/// <param name="orders">Reads the order as it now stands, over the contract.</param>
/// <param name="options">Supplies the automatic-draft switch.</param>
/// <param name="logger">Reports what was reopened.</param>
internal sealed partial class PackingQueue(
    ShippingDbContext context,
    IOrderFulfilment orders,
    IOptions<ShippingOptions> options,
    ILogger<PackingQueue> logger)
{
    /// <summary>
    /// Opens a draft for the units of a sub-order that no live parcel carries. Saves nothing.
    /// </summary>
    /// <remarks>
    /// Reads the other parcels from the database, so a withdrawal must be committed before this runs
    /// or its units would still count as packed.
    /// </remarks>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether a draft was opened.</returns>
    public async Task<bool> ReopenAsync(Guid subOrderId, CancellationToken cancellationToken)
    {
        // With drafting off, the booking screen opens a parcel itself from the order as it stands.
        if (!options.Value.AutoDraftOnConfirmation)
        {
            return false;
        }

        var open = await context.Shipments
            .IgnoreQueryFilters()
            .AnyAsync(
                shipment => shipment.SubOrderId == subOrderId
                            && !shipment.IsReturn
                            && shipment.Status == ShipmentStatus.Draft,
                cancellationToken)
            .ConfigureAwait(false);

        if (open)
        {
            return false;
        }

        var view = await orders.GetAsync(subOrderId, cancellationToken).ConfigureAwait(false);

        if (view.IsFailure || view.Value.Status is not ("Confirmed" or "Processing" or "Packed"))
        {
            return false;
        }

        var order = view.Value;

        var draft = Shipment.Draft(
            order.OrderId,
            order.OrderNumber,
            order.SubOrderId,
            order.SubOrderNumber,
            order.VendorId,
            order.CustomerId);

        draft.Address(
            order.Destination.Pincode,
            order.Destination.StateId,
            order.IsCod ? order.AmountDueAtDelivery : null,
            order.DeclaredValue,
            freightCharged: 0m,
            order.CurrencyCode);

        var packed = await ShipmentPacker
            .PackAsync(context, orders, draft, [], cancellationToken)
            .ConfigureAwait(false);

        // Nothing left to send is the ordinary answer for a line cancelled in full.
        if (packed.IsFailure)
        {
            return false;
        }

        context.Shipments.Add(draft);

        Reopened(logger, order.SubOrderNumber, draft.Lines.Count);

        return true;
    }

    [LoggerMessage(EventId = 1773, Level = LogLevel.Information,
        Message = "Parcel reopened for {SubOrderNumber} after a partial cancellation: {LineCount} line(s) still to send.")]
    private static partial void Reopened(ILogger logger, string subOrderNumber, int lineCount);
}
