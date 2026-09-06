using KlaraHome.Contracts.Media;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.Modules.Shipping.Application;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Documents;
using KlaraHome.Modules.Shipping.Infrastructure.Quoting;
using KlaraHome.Modules.Shipping.Infrastructure.Rating;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;

/// <summary>
/// Turns a packed parcel into a booked one.
/// </summary>
/// <remarks>
/// <para>
/// Everything between "the box is ready" and "a courier is expecting it", in one place because the
/// steps only make sense together: the seller's pickup address has to be registered with the courier
/// before a consignment can be booked against it, the consignment has to exist before a label can be
/// produced, and the label is what a packer actually needs. A handler doing three of those and
/// forgetting the fourth is a parcel nobody can print.
/// </para>
/// <para>
/// A label failure is never a booking failure. Once an air waybill exists the parcel <em>is</em>
/// booked, and refusing the whole operation because a PDF could not be fetched would leave a
/// consignment live at the courier and absent from this platform. The label is retried on its own
/// endpoint, and this platform's own 4×6 sheet is rendered when the courier has none.
/// </para>
/// <para>
/// The freight the shopper paid is read from the rate card here rather than taken from the courier's
/// quote. What the customer pays and what the platform pays are different numbers by design, and
/// this is the point where both land on the same row.
/// </para>
/// </remarks>
/// <param name="providers">The adapters. The manual one is what an unconfigured deployment books with.</param>
/// <param name="workflow">Records the booking and tells the order.</param>
/// <param name="orders">Reads the destination and the lines, over the contract.</param>
/// <param name="pickups">Reads and registers the seller's collection address.</param>
/// <param name="vendors">Names the seller on the label.</param>
/// <param name="reference">Supplies the destination state's name for the courier's paperwork.</param>
/// <param name="rates">Prices what the shopper paid for delivery.</param>
/// <param name="documents">Renders and stores this platform's own label.</param>
/// <param name="storage">Stores a courier's own label PDF when it publishes one.</param>
/// <param name="options">Supplies the volumetric divisor and the automatic-pickup switch.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what the courier refused.</param>
internal sealed partial class ShipmentBooker(
    ShippingProviderRegistry providers,
    ShipmentWorkflow workflow,
    IOrderFulfilment orders,
    IVendorPickupPoints pickups,
    IVendorDirectory vendors,
    IReferenceData reference,
    RateResolver rates,
    IDocumentStore documents,
    IFileStorage storage,
    IOptions<ShippingOptions> options,
    IClock clock,
    ILogger<ShipmentBooker> logger)
{
    /// <summary>What to book, beyond what the parcel already knows.</summary>
    /// <param name="Method">The service, or null to use whatever the shopper chose at checkout.</param>
    /// <param name="PickupLocationId">The address to collect from, or null for the seller's default.</param>
    /// <param name="ManualAwb">An air waybill an operator obtained themselves, for a hand-booking.</param>
    /// <param name="ManualCourier">Who is carrying it, for a hand-booking.</param>
    internal sealed record BookingIntent(
        ShippingMethod? Method,
        Guid? PickupLocationId,
        string? ManualAwb,
        string? ManualCourier);

    /// <summary>
    /// Books a drafted parcel with a courier.
    /// </summary>
    /// <param name="shipment">The consignment, loaded for update and already packed and weighed.</param>
    /// <param name="intent">What to book.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> BookAsync(
        Shipment shipment,
        BookingIntent intent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(intent);

        if (shipment.IsBooked)
        {
            return Result.Failure(ShippingErrors.AlreadyBooked);
        }

        if (shipment.Lines.Count == 0)
        {
            return Result.Failure(ShippingErrors.NothingToShip);
        }

        var view = await orders.GetAsync(shipment.SubOrderId, cancellationToken).ConfigureAwait(false);

        if (view.IsFailure)
        {
            return Result.Failure(ShippingErrors.OrderUnavailable(view.Error.Message));
        }

        var order = view.Value;
        var vendorId = shipment.VendorId ?? order.VendorId;

        var pickup = await pickups
            .FindAsync(vendorId, intent.PickupLocationId, cancellationToken)
            .ConfigureAwait(false);

        if (pickup is null)
        {
            return Result.Failure(ShippingErrors.NoPickupLocation);
        }

        shipment.CollectFrom(pickup.Id, pickup.Pincode);

        var method = intent.Method ?? RatedShippingOptions.MethodFor(order.ShippingOptionCode);

        await PriceAsync(shipment, order, vendorId, method, cancellationToken).ConfigureAwait(false);

        // A hand-typed waybill goes to the manual adapter whatever else is configured. An operator
        // who has already been to a courier counter is telling us what happened, not asking us to
        // book something.
        var manual = !string.IsNullOrWhiteSpace(intent.ManualAwb);
        var provider = manual ? providers.Manual : providers.Default;

        var request = await BuildRequestAsync(shipment, order, pickup, method, intent, cancellationToken)
            .ConfigureAwait(false);

        var booked = await provider.CreateShipmentAsync(request, cancellationToken).ConfigureAwait(false);

        if (booked.IsFailure)
        {
            // The parcel stays a draft and appears in the exception queue with a manual-AWB fallback,
            // which is exactly what docs/08-integrations.md §2 asks for.
            BookingFailed(logger, shipment.SubOrderNumber, provider.Name, booked.Error.Message);

            return Result.Failure(booked.Error);
        }

        var recorded = await workflow
            .RecordBookingAsync(shipment, booked.Value, provider.Name, cancellationToken)
            .ConfigureAwait(false);

        if (recorded.IsFailure)
        {
            return recorded;
        }

        // From here on the parcel is booked and nothing may fail the operation. A label that cannot
        // be produced is an endpoint away from being retried; a booking rolled back is a live
        // consignment at a courier that this platform has no record of.
        await AttachLabelAsync(shipment, order, pickup, booked.Value.LabelUrl, cancellationToken)
            .ConfigureAwait(false);

        if (options.Value.AutoSchedulePickup && !manual)
        {
            await SchedulePickupAsync(shipment, clock.UtcNow.AddDays(1), cancellationToken)
                .ConfigureAwait(false);
        }

        // The seller's address is registered with the courier now that one has accepted a parcel
        // from it, so the next booking does not repeat the registration.
        if (!manual
            && string.IsNullOrWhiteSpace(pickup.CourierLocationCode)
            && booked.Value.ProviderShipmentId is { Length: > 0 })
        {
            await pickups
                .RecordCourierLocationCodeAsync(pickup.Id, pickup.Label, cancellationToken)
                .ConfigureAwait(false);
        }

        return Result.Success();
    }

    /// <summary>Asks the courier to collect, and records when.</summary>
    /// <param name="shipment">The consignment, loaded for update.</param>
    /// <param name="pickupAt">When the parcel will be ready.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> SchedulePickupAsync(
        Shipment shipment,
        DateTimeOffset pickupAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        if (!shipment.IsBooked)
        {
            return Result.Failure(ShippingErrors.NotBooked);
        }

        var provider = providers.For(shipment.Provider);

        var scheduled = await provider
            .SchedulePickupAsync(shipment.ProviderShipmentId, shipment.Awb!, pickupAt, cancellationToken)
            .ConfigureAwait(false);

        if (scheduled.IsFailure)
        {
            return scheduled;
        }

        return shipment.SchedulePickup(scheduled.Value, clock.UtcNow)
            ? Result.Success()
            : Result.Failure(ShippingErrors.InvalidTransition(shipment.Status, ShipmentStatus.PickupScheduled));
    }

    /// <summary>
    /// Produces the label a packer prints, from the courier where there is one and from this
    /// platform where there is not.
    /// </summary>
    /// <param name="shipment">The consignment, loaded for update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<Guid>> EnsureLabelAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        if (shipment.HasLabel)
        {
            return Result.Success(shipment.Id);
        }

        if (!shipment.IsBooked)
        {
            return Result.Failure<Guid>(ShippingErrors.NotBooked);
        }

        var view = await orders.GetAsync(shipment.SubOrderId, cancellationToken).ConfigureAwait(false);

        if (view.IsFailure)
        {
            return Result.Failure<Guid>(ShippingErrors.OrderUnavailable(view.Error.Message));
        }

        var pickup = shipment.VendorId is { } vendorId
            ? await pickups.FindAsync(vendorId, shipment.PickupLocationId, cancellationToken)
                .ConfigureAwait(false)
            : null;

        await AttachLabelAsync(shipment, view.Value, pickup, labelUrl: null, cancellationToken)
            .ConfigureAwait(false);

        return shipment.HasLabel
            ? Result.Success(shipment.Id)
            : Result.Failure<Guid>(ShippingErrors.LabelMissing);
    }

    /// <summary>
    /// Records what the shopper paid for delivery, from this platform's own rate card.
    /// </summary>
    /// <remarks>
    /// Priced at booking rather than copied from the sub-order, because the sub-order's figure was
    /// quoted against a basket and this is a parcel: a partial shipment, or a cancellation between
    /// confirmation and packing, changes both the weight and the value the tariff bands on. Where the
    /// card has no rule, the charge stays at zero — a hole in the tariff must not invent a figure.
    /// </remarks>
    private async Task PriceAsync(
        Shipment shipment,
        SubOrderFulfilmentView order,
        Guid vendorId,
        ShippingMethod method,
        CancellationToken cancellationToken)
    {
        var chargeable = shipment.ChargeableWeightGrams(options.Value.VolumetricDivisor);

        var priced = await rates
            .RateAsync(
                vendorId,
                order.Destination.StateId,
                order.Destination.Pincode,
                chargeable,
                order.DeclaredValue,
                order.IsCod,
                cancellationToken)
            .ConfigureAwait(false);

        var chosen = priced.FirstOrDefault(parcel => parcel.Rate.Method == method)
                     ?? (priced.Count > 0 ? priced[0] : null);

        shipment.Address(
            order.Destination.Pincode,
            order.Destination.StateId,
            order.IsCod ? order.AmountDueAtDelivery : null,
            order.DeclaredValue,
            chosen?.Amount ?? 0m,
            order.CurrencyCode);
    }

    private async Task<CourierBookingRequest> BuildRequestAsync(
        Shipment shipment,
        SubOrderFulfilmentView order,
        VendorPickupPoint pickup,
        ShippingMethod method,
        BookingIntent intent,
        CancellationToken cancellationToken)
    {
        var stateCode = order.Destination.StateId == Guid.Empty
            ? null
            : await reference.StateCodeAsync(order.Destination.StateId, cancellationToken).ConfigureAwait(false);

        var reference_ = string.IsNullOrWhiteSpace(intent.ManualAwb)
            ? order.SubOrderNumber
            : ManualShippingProvider.ReferenceFor(intent.ManualAwb, intent.ManualCourier);

        return new CourierBookingRequest(
            shipment.Id,
            reference_,
            new CourierAddress(
                pickup.ContactName,
                pickup.ContactPhone,
                pickup.Line1,
                pickup.Line2,
                pickup.Landmark,
                pickup.City,
                StateName: null,
                pickup.Pincode,

                // The courier's own name for the address, falling back to the seller's label — which
                // is what an aggregator registers a pickup point under when it is created by hand.
                pickup.CourierLocationCode ?? pickup.Label),
            new CourierAddress(
                order.Destination.Name,
                order.Destination.Mobile,
                order.Destination.Line1,
                order.Destination.Line2,
                order.Destination.Landmark,
                order.Destination.City,
                stateCode,
                order.Destination.Pincode),
            shipment.ChargeableWeightGrams(options.Value.VolumetricDivisor),
            shipment.Dimensions,
            shipment.DeclaredValue,
            shipment.CodAmount,
            shipment.CurrencyCode,
            method,
            shipment.IsReturn,
            [
                .. shipment.Lines.Select(line => new CourierParcelItem(
                    line.Sku,
                    line.Name,
                    line.Quantity,
                    line.DeclaredValue)),
            ]);
    }

    /// <summary>
    /// Attaches a printable label, preferring the courier's own.
    /// </summary>
    /// <remarks>
    /// The courier's label carries their barcode and their sortation marks, which this platform's
    /// renderer cannot draw. Where they publish one it is stored as it came; where they do not, the
    /// 4×6 sheet is rendered instead, which is enough for a hand-booked parcel and is deliberately
    /// not enough for a courier's own automation.
    /// </remarks>
    private async Task AttachLabelAsync(
        Shipment shipment,
        SubOrderFulfilmentView order,
        VendorPickupPoint? pickup,
        string? labelUrl,
        CancellationToken cancellationToken)
    {
        var provider = providers.For(shipment.Provider);

        if (labelUrl is null or { Length: 0 })
        {
            var fetched = await provider
                .GenerateLabelAsync(shipment.ProviderShipmentId, shipment.Awb ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);

            if (fetched.IsSuccess)
            {
                var stored = await StoreAsync(shipment, fetched.Value, cancellationToken).ConfigureAwait(false);

                if (stored is { Length: > 0 } key)
                {
                    shipment.AttachCourierLabel(key, clock.UtcNow);
                    return;
                }
            }
        }

        try
        {
            var seller = shipment.VendorId is { } vendorId
                ? await vendors.FindAsync(vendorId, cancellationToken).ConfigureAwait(false)
                : null;

            var rendered = await documents
                .RenderAsync(
                    ShippingDocumentBuilder.Label(shipment, order, pickup, seller?.DisplayName),
                    $"label-{shipment.Awb ?? shipment.SubOrderNumber}.pdf",
                    "Shipment",
                    shipment.Id,
                    cancellationToken)
                .ConfigureAwait(false);

            shipment.AttachLabel(rendered.Id, clock.UtcNow);
        }
        catch (InvalidOperationException exception)
        {
            // Rendering is unavailable on this deployment. The parcel is booked and the air waybill
            // exists; a packer can write it on the box, and the label endpoint will retry.
            LabelUnavailable(logger, shipment.Awb ?? shipment.SubOrderNumber, exception.Message);
        }
    }

    /// <summary>
    /// Stores a courier's own label PDF, or null when it could not be kept.
    /// </summary>
    /// <remarks>
    /// Into the private bucket, because a label carries a customer's name, address and telephone
    /// number, and a publicly addressable one would be a disclosure with a URL
    /// (docs/07-security-compliance.md §5).
    /// </remarks>
    private async Task<string?> StoreAsync(Shipment shipment, byte[] pdf, CancellationToken cancellationToken)
    {
        if (!storage.IsAvailable)
        {
            return null;
        }

        try
        {
            using var content = new MemoryStream(pdf, writable: false);

            // Deterministic, so re-fetching a label overwrites rather than accumulating one object
            // per retry. The parcel id and nothing else: an air waybill can be reassigned, and a key
            // built from one would strand the object it named.
            var key = $"shipping/labels/{shipment.Id:N}.pdf";

            var stored = await storage
                .PutAsync(key, content, "application/pdf", StorageVisibility.Private, cancellationToken)
                .ConfigureAwait(false);

            return stored.Key;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            LabelUnavailable(logger, shipment.Awb ?? shipment.SubOrderNumber, exception.Message);
            return null;
        }
    }

    [LoggerMessage(EventId = 1800, Level = LogLevel.Warning,
        Message = "Parcel for {SubOrderNumber} could not be booked with {Provider}: {Detail}. "
                  + "It stays packed and is in the exception queue.")]
    private static partial void BookingFailed(
        ILogger logger,
        string subOrderNumber,
        string provider,
        string detail);

    [LoggerMessage(EventId = 1801, Level = LogLevel.Warning,
        Message = "No label could be produced for parcel {Awb}: {Detail}. The booking stands.")]
    private static partial void LabelUnavailable(ILogger logger, string awb, string detail);
}
