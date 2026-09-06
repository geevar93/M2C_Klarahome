using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Shipping;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Shipping.Application;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;

/// <summary>
/// Answers <see cref="IReversePickup"/> from this module's own tables.
/// </summary>
/// <remarks>
/// <para>
/// A reverse pickup is a <see cref="Shipment"/> like any other, marked with <c>IsReturn</c>. That is
/// the whole design: it is booked through the same adapter registry, it appears in the same parcel
/// list, its scans arrive through the same webhook receiver, and its waybill is tracked by the same
/// polling fallback. A second, thinner idea of a parcel living in the returns schema would have to
/// re-implement every one of those.
/// </para>
/// <para>
/// It inverts the forward journey — the shopper's frozen delivery address becomes the pickup and the
/// seller's pickup point becomes the destination — and neither address is passed in, because this
/// module already holds both from the parcel that went out. The Returns module names a sub-order and
/// a set of lines and knows nothing about either address, which is exactly the boundary the seam
/// exists to keep.
/// </para>
/// <para>
/// It never collects cash and never declares a COD amount. Money coming back is the Payments
/// module's, and a courier told to collect cash on a return would collect it from the wrong person.
/// </para>
/// <para>
/// A deployment with no logistics account still works: the manual adapter takes a waybill an operator
/// typed in, exactly as it does for a forward dispatch.
/// </para>
/// </remarks>
/// <param name="context">The Shipping data context.</param>
/// <param name="providers">Finds the adapter to book with.</param>
/// <param name="orders">Reads what the parcel went out with.</param>
/// <param name="pickups">Resolves the seller's address, which is where the goods are going.</param>
/// <param name="reference">Resolves the shopper's state code for the courier's paperwork.</param>
/// <param name="options">Supplies the volumetric divisor.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was booked and what was not.</param>
internal sealed partial class ReversePickupService(
    ShippingDbContext context,
    ShippingProviderRegistry providers,
    IOrderFulfilment orders,
    IVendorPickupPoints pickups,
    IReferenceData reference,
    IOptions<ShippingOptions> options,
    IClock clock,
    ILogger<ReversePickupService> logger) : IReversePickup
{
    /// <inheritdoc />
    public async Task<Result<ReversePickupBooking>> BookAsync(
        ReversePickupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await context.Shipments
            .Include(shipment => shipment.Lines)
            .Where(shipment => shipment.SubOrderId == request.SubOrderId
                               && shipment.IsReturn
                               && shipment.Status != ShipmentStatus.Cancelled)
            .OrderByDescending(shipment => shipment.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // Idempotent. An operator clicking twice wants the collection that exists, not a second van.
        if (existing is { IsBooked: true })
        {
            return Result.Success(Describe(existing));
        }

        var view = await orders.GetAsync(request.SubOrderId, cancellationToken).ConfigureAwait(false);

        if (view.IsFailure)
        {
            return Result.Failure<ReversePickupBooking>(ShippingErrors.OrderUnavailable(view.Error.Message));
        }

        var order = view.Value;

        var pickup = await pickups
            .FindAsync(order.VendorId, pickupLocationId: null, cancellationToken)
            .ConfigureAwait(false);

        if (pickup is null)
        {
            return Result.Failure<ReversePickupBooking>(ShippingErrors.NoPickupLocation);
        }

        var shipment = existing ?? Open(order, request);

        if (existing is null)
        {
            context.Shipments.Add(shipment);
        }

        var manual = !string.IsNullOrWhiteSpace(request.ManualAwb);
        var provider = manual ? providers.Manual : providers.Default;

        var booking = await provider
            .CreateShipmentAsync(
                await BuildRequestAsync(shipment, order, pickup, request, cancellationToken)
                    .ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);

        if (booking.IsFailure)
        {
            NotBooked(logger, request.ReturnNumber, provider.Name, booking.Error.Message);
            return Result.Failure<ReversePickupBooking>(booking.Error);
        }

        var now = clock.UtcNow;

        if (!shipment.Book(
                provider.Name,
                booking.Value.Courier,
                booking.Value.ServiceName,
                booking.Value.Awb,
                booking.Value.ProviderShipmentId,
                booking.Value.TrackingUrl,
                booking.Value.ExpectedDeliveryAt,
                booking.Value.FreightCost,
                now))
        {
            return Result.Failure<ReversePickupBooking>(ShippingErrors.AlreadyBooked);
        }

        // Asked for a collection where the courier offers one. A manual booking is a waybill an
        // operator already arranged, so there is nobody to ask.
        if (!manual)
        {
            var when = request.ScheduledFor ?? now.AddDays(1);

            var scheduled = await provider
                .SchedulePickupAsync(shipment.ProviderShipmentId, shipment.Awb!, when, cancellationToken)
                .ConfigureAwait(false);

            if (scheduled.IsSuccess)
            {
                shipment.SchedulePickup(scheduled.Value, now);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Booked(logger, request.ReturnNumber, shipment.Awb ?? string.Empty, provider.Name);

        return Result.Success(Describe(shipment));
    }

    /// <inheritdoc />
    public async Task<Result> CancelAsync(
        Guid shipmentId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var shipment = await context.Shipments
            .FirstOrDefaultAsync(candidate => candidate.Id == shipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            return Result.Failure(ShippingErrors.NotFound("parcel"));
        }

        // The courier is told first. Cancelling our record of a collection a courier still expects is
        // how a driver arrives at a shopper's door for a parcel nobody is sending.
        if (shipment.IsBooked)
        {
            var told = await providers
                .For(shipment.Provider)
                .CancelShipmentAsync(shipment.ProviderShipmentId, shipment.Awb!, cancellationToken)
                .ConfigureAwait(false);

            if (told.IsFailure)
            {
                return told;
            }
        }

        if (!shipment.Advance(ShipmentStatus.Cancelled, clock.UtcNow, reason))
        {
            return Result.Failure(
                ShippingErrors.InvalidTransition(shipment.Status, ShipmentStatus.Cancelled));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Opens the reverse consignment, addressed to the seller.
    /// </summary>
    /// <remarks>
    /// The destination PIN code is the <em>seller's</em>, because that is where this parcel is going.
    /// The declared value is what the goods are worth for the courier's insurance; there is no COD
    /// amount, because nothing is collected at a return's door.
    /// </remarks>
    private static Shipment Open(SubOrderFulfilmentView order, ReversePickupRequest request)
    {
        var wanted = request.Lines.ToDictionary(line => line.OrderLineId, line => line.Quantity);

        var shipment = Shipment.Draft(
            order.OrderId,
            order.OrderNumber,
            order.SubOrderId,
            order.SubOrderNumber,
            order.VendorId,
            order.CustomerId);

        shipment.MarkReturn();

        var value = 0m;

        foreach (var line in order.Lines)
        {
            if (!wanted.TryGetValue(line.OrderLineId, out var quantity) || quantity <= 0)
            {
                continue;
            }

            var units = Math.Min(quantity, line.Quantity);

            var share = line.Quantity <= 0
                ? 0m
                : Math.Round(line.LineTotal * units / line.Quantity, 4, MidpointRounding.AwayFromZero);

            value += share;

            shipment.Pack(ShipmentLine.Pack(
                shipment.Id,
                line.OrderLineId,
                line.Sku,
                line.Name,
                units,
                line.UnitWeightGrams,
                share));
        }

        shipment.Address(
            order.Destination.Pincode,
            order.Destination.StateId,
            codAmount: null,
            value,
            freightCharged: 0m,
            order.CurrencyCode);

        return shipment;
    }

    /// <summary>
    /// Builds the courier request with the two addresses the other way round.
    /// </summary>
    /// <remarks>
    /// This is the only place in the module where the pickup is the customer and the destination is
    /// the seller, and it is the whole of what makes a return a return as far as a courier is
    /// concerned. <c>IsReturn</c> goes on the request too, because most aggregators price and route a
    /// reverse leg differently.
    /// </remarks>
    private async Task<CourierBookingRequest> BuildRequestAsync(
        Shipment shipment,
        SubOrderFulfilmentView order,
        VendorPickupPoint destination,
        ReversePickupRequest request,
        CancellationToken cancellationToken)
    {
        var stateCode = order.Destination.StateId == Guid.Empty
            ? null
            : await reference.StateCodeAsync(order.Destination.StateId, cancellationToken)
                .ConfigureAwait(false);

        var courierReference = string.IsNullOrWhiteSpace(request.ManualAwb)
            ? request.ReturnNumber
            : ManualShippingProvider.ReferenceFor(request.ManualAwb, request.ManualCourier);

        return new CourierBookingRequest(
            shipment.Id,
            courierReference,

            // Collected from the shopper.
            new CourierAddress(
                order.Destination.Name,
                order.Destination.Mobile,
                order.Destination.Line1,
                order.Destination.Line2,
                order.Destination.Landmark,
                order.Destination.City,
                stateCode,
                order.Destination.Pincode),

            // Delivered back to the seller.
            new CourierAddress(
                destination.ContactName,
                destination.ContactPhone,
                destination.Line1,
                destination.Line2,
                destination.Landmark,
                destination.City,
                StateName: null,
                destination.Pincode,
                destination.CourierLocationCode ?? destination.Label),
            shipment.ChargeableWeightGrams(options.Value.VolumetricDivisor),
            shipment.Dimensions,
            shipment.DeclaredValue,

            // Never any cash. Money coming back is the Payments module's, and a courier told to
            // collect on a return would collect it from the wrong person.
            CodAmount: null,
            shipment.CurrencyCode,
            ShippingMethod.Standard,
            IsReturn: true,
            [
                .. shipment.Lines.Select(line => new CourierParcelItem(
                    line.Sku,
                    line.Name,
                    line.Quantity,
                    line.DeclaredValue)),
            ]);
    }

    /// <summary>Describes a booked reverse consignment in the shape the seam returns.</summary>
    private static ReversePickupBooking Describe(Shipment shipment)
        => new(
            shipment.Id,
            shipment.Awb ?? string.Empty,
            shipment.Courier,
            shipment.TrackingUrl,
            shipment.PickupScheduledAt);

    [LoggerMessage(EventId = 1690, Level = LogLevel.Information,
        Message = "A reverse pickup for return {ReturnNumber} was booked on waybill {Awb} with {Provider}.")]
    private static partial void Booked(ILogger logger, string returnNumber, string awb, string provider);

    [LoggerMessage(EventId = 1691, Level = LogLevel.Error,
        Message = "No reverse pickup was booked for return {ReturnNumber} with {Provider}: {Detail}")]
    private static partial void NotBooked(
        ILogger logger,
        string returnNumber,
        string provider,
        string detail);
}
