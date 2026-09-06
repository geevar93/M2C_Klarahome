using KlaraHome.Contracts.Inventory;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Shipping;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Infrastructure.Persistence;
using KlaraHome.Modules.Returns.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Returns.Infrastructure.Events;

/// <summary>
/// Keeps a return in step with the parcel carrying it, and handles the parcel nobody asked to send
/// back.
/// </summary>
/// <remarks>
/// <para>
/// Two facts about parcels concern this module and they are opposite halves of the same problem.
/// A <b>scan on a reverse consignment</b> moves the return that booked it, so a shopper following
/// their RMA sees what the courier sees without anybody re-keying it. And a <b>parcel returned to
/// origin</b> — attempted, refused, attempts exhausted — is goods coming back that nobody raised a
/// return for, and the units still have to go back on supply.
/// </para>
/// <para>
/// Both arrive on the same event. <c>ShipmentTrackingUpdated</c> is published on every applied
/// courier scan, including the one that says a parcel reached the seller again, and subscribing to
/// it once rather than to several narrower events is what makes the two paths visibly the same
/// mechanism.
/// </para>
/// <para>
/// Return-to-origin restocking is handled here rather than in Inventory because it is the same
/// decision a return's quality control makes and deserves the same answer. Putting it in Inventory
/// would put a returns policy in the module that owns the ledger.
/// </para>
/// <para>
/// Delivery is at-least-once, so both paths are idempotent: a redelivered scan finds the return
/// already where it is being moved to, and a redelivered return-to-origin finds the units already
/// moved under the same reference.
/// </para>
/// </remarks>
/// <param name="context">The Returns data context.</param>
/// <param name="orders">Reads what was in the parcel that came back.</param>
/// <param name="stock">Puts the units back on supply.</param>
/// <param name="workflow">Moves the return on the courier's word.</param>
/// <param name="logger">Reports what moved and what was put back.</param>
internal sealed partial class ShippingLifecycleHandlers(
    ReturnsDbContext context,
    IOrderReturns orders,
    IStockRestock stock,
    ReturnWorkflow workflow,
    ILogger<ShippingLifecycleHandlers> logger)
    : IIntegrationEventHandler<ShipmentTrackingUpdated>
{
    /// <summary>The shipping module's word for a parcel that reached the seller again.</summary>
    private const string RtoDelivered = "RtoDelivered";

    /// <inheritdoc />
    public async Task HandleAsync(ShipmentTrackingUpdated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (string.Equals(integrationEvent.Status, RtoDelivered, StringComparison.Ordinal))
        {
            await RestockUndeliveredAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
            return;
        }

        await AdvanceReturnAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a return when the courier scans the parcel carrying it.
    /// </summary>
    /// <remarks>
    /// Only the scans that mean something to a shopper are acted on. A hub arrival is a timeline
    /// entry on the parcel and is deliberately not a return transition — a return that changed state
    /// six times between two cities would be noise dressed up as progress.
    /// </remarks>
    private async Task AdvanceReturnAsync(
        ShipmentTrackingUpdated integrationEvent,
        CancellationToken cancellationToken)
    {
        if (Next(integrationEvent.Status) is not { } next)
        {
            return;
        }

        var request = await context.Returns
            .FirstOrDefaultAsync(
                candidate => candidate.PickupShipmentId == integrationEvent.ShipmentId,
                cancellationToken)
            .ConfigureAwait(false);

        if (request is null)
        {
            // A scan on a forward parcel, which is the overwhelming majority of them. Nothing here
            // is wrong; this module simply has no interest in it.
            return;
        }

        var moved = await workflow
            .TransitionAsync(
                request,
                next,
                ReturnActor.System,
                actorId: null,
                integrationEvent.Remark ?? ReturnLifecycle.Narrate(next),
                cancellationToken)
            .ConfigureAwait(false);

        if (moved.IsFailure)
        {
            // A courier reporting a collection on a return somebody cancelled yesterday is a
            // discrepancy, not an instruction. It is logged rather than forced.
            ScanNotApplied(logger, request.ReturnNumber, integrationEvent.Status, moved.Error.Message);
            return;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Puts every live unit of an undelivered parcel back on supply.
    /// </summary>
    /// <remarks>
    /// Restocked rather than inspected, deliberately. A return-to-origin parcel was never opened by a
    /// customer: it went out, nobody took it, and it came home sealed. A store that wants those
    /// inspected quarantines them in its own receiving process, which is a different conversation
    /// from this one.
    /// </remarks>
    private async Task RestockUndeliveredAsync(
        ShipmentTrackingUpdated integrationEvent,
        CancellationToken cancellationToken)
    {
        var view = await orders
            .GetAsync(integrationEvent.SubOrderId, customerId: null, cancellationToken)
            .ConfigureAwait(false);

        if (view.IsFailure)
        {
            return;
        }

        var order = view.Value;

        var units = order.Lines
            .Where(line => line.QuantityReturnable > 0)
            .Select(line => new RestockUnits(
                line.ListingId,
                line.QuantityReturnable,
                RestockDisposition.Restock))
            .ToArray();

        if (units.Length == 0)
        {
            return;
        }

        var moved = await stock
            .RestockAsync(
                units,
                RestockReferenceTypes.ReturnToOrigin,
                integrationEvent.SubOrderId,
                $"Undelivered parcel returned to origin on order {order.OrderNumber}",
                cancellationToken)
            .ConfigureAwait(false);

        if (moved > 0)
        {
            RtoRestocked(logger, order.SubOrderNumber, moved);
        }
    }

    /// <summary>
    /// What a courier's word about a parcel means for the return riding on it, or null when it means
    /// nothing.
    /// </summary>
    /// <remarks>
    /// The shipping module's vocabulary read as strings, because no module may reference another. A
    /// status this does not recognise is not an error — it is a scan a return has no opinion about.
    /// </remarks>
    private static ReturnStatus? Next(string status)
        => status switch
        {
            "PickedUp" => ReturnStatus.Picked,
            "InTransit" => ReturnStatus.InTransit,
            "OutForDelivery" => ReturnStatus.InTransit,
            "Delivered" => ReturnStatus.Received,
            _ => null,
        };

    [LoggerMessage(EventId = 1760, Level = LogLevel.Warning,
        Message = "A courier scan of {Status} was not applied to return {ReturnNumber}: {Detail}")]
    private static partial void ScanNotApplied(
        ILogger logger,
        string returnNumber,
        string status,
        string detail);

    [LoggerMessage(EventId = 1761, Level = LogLevel.Information,
        Message = "An undelivered parcel on sub-order {SubOrderNumber} put {Units} unit(s) back on supply.")]
    private static partial void RtoRestocked(ILogger logger, string subOrderNumber, int units);
}
