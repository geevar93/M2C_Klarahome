using KlaraHome.Modules.Shipping.Application;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Courier;

/// <summary>
/// Dispatch with no aggregator at all (docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of the internal cash-on-delivery provider in Payments: an adapter with nothing
/// behind it, and a deliberate one. A seller who walks a parcel to a courier counter and comes back
/// with a handwritten air waybill has done everything a booking does; what is missing is only the
/// API call, and refusing to record the parcel because nobody made one would make the platform
/// unusable for exactly the shops most likely to start on it.
/// </para>
/// <para>
/// It is also the documented fallback when an aggregator booking fails. The sub-order stays packed,
/// the consignment appears in the exception queue, and an operator books it by hand — which is a
/// worse day than an API call and a much better day than a lost parcel.
/// </para>
/// <para>
/// Everything it cannot do, it refuses honestly rather than pretending. It cannot invent an air
/// waybill, so a booking without one supplied is a named failure; it cannot ask a courier anything,
/// so serviceability answers what the operator has configured rather than what a network says; and
/// it has no webhooks at all, so tracking is whatever a human entered.
/// </para>
/// </remarks>
internal sealed class ManualShippingProvider : IShippingProvider
{
    /// <summary>
    /// The key an operator books under by supplying the air waybill themselves.
    /// </summary>
    /// <remarks>
    /// Carried in the booking reference rather than as a parameter on the interface, because a
    /// manual waybill is not something an aggregator adapter could ever accept and widening the
    /// interface for it would put a hand-typed value into every future adapter's signature.
    /// </remarks>
    public const string AwbPrefix = "manual:";

    /// <inheritdoc />
    public string Name => ShippingProviders.Manual;

    /// <summary>Always usable. That is the entire point of it.</summary>
    public bool IsConfigured => true;

    /// <summary>Never. There is no courier to sign anything.</summary>
    public bool CanVerifyWebhooks => false;

    /// <inheritdoc />
    /// <remarks>
    /// Answers yes everywhere, with the store's own promise. Without an aggregator there is nobody
    /// to ask, and the honest position for a shop that ships by hand is that it will try — the
    /// serviceability the platform actually enforces in that case is the seller's own, which the
    /// Vendors module already answers.
    /// </remarks>
    public Task<Result<CourierServiceability>> CheckServiceabilityAsync(
        string pincode,
        string? pickupPincode,
        int weightGrams,
        bool isCod,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success(new CourierServiceability(
            ShippingProviders.Manual,
            PrepaidOk: true,
            CodOk: true,
            PickupOk: true,
            EtaDays: null,
            MaxWeightGrams: null)));

    /// <inheritdoc />
    /// <remarks>
    /// Accepts a booking only when the caller has supplied the courier's own air waybill in the
    /// reference. Inventing one would put a number on a label that no courier can scan.
    /// </remarks>
    public Task<Result<CourierBooking>> CreateShipmentAsync(
        CourierBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Reference.StartsWith(AwbPrefix, StringComparison.Ordinal))
        {
            return Task.FromResult(Result.Failure<CourierBooking>(ShippingErrors.ManualAwbRequired));
        }

        var supplied = request.Reference[AwbPrefix.Length..];
        var separator = supplied.IndexOf('|', StringComparison.Ordinal);

        var awb = separator < 0 ? supplied : supplied[..separator];
        var courier = separator < 0 || separator == supplied.Length - 1
            ? "Manual"
            : supplied[(separator + 1)..];

        return Task.FromResult(string.IsNullOrWhiteSpace(awb)
            ? Result.Failure<CourierBooking>(ShippingErrors.ManualAwbRequired)
            : Result.Success(new CourierBooking(
                courier.Trim(),
                ServiceName: null,
                awb.Trim(),
                ProviderShipmentId: null,
                TrackingUrl: null,
                ExpectedDeliveryAt: null,
                FreightCost: null,
                LabelUrl: null)));
    }

    /// <inheritdoc />
    /// <remarks>
    /// There is no courier label to fetch. The caller renders this platform's own 4×6 label instead,
    /// which is what the failure tells it to do.
    /// </remarks>
    public Task<Result<byte[]>> GenerateLabelAsync(
        string? providerShipmentId,
        string awb,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<byte[]>(ShippingErrors.ProviderUnavailable));

    /// <inheritdoc />
    /// <remarks>The sheet is still produced and still signed; only the API call is missing.</remarks>
    public Task<Result<CourierManifest>> GenerateManifestAsync(
        IReadOnlyList<string> awbs,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success(new CourierManifest(ProviderManifestId: null, DocumentUrl: null)));

    /// <inheritdoc />
    /// <remarks>
    /// Accepted as recorded rather than requested. A seller who rings their courier has scheduled a
    /// collection; the platform's record of it is worth keeping even though nothing was called.
    /// </remarks>
    public Task<Result<DateTimeOffset>> SchedulePickupAsync(
        string? providerShipmentId,
        string awb,
        DateTimeOffset pickupAt,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success(pickupAt));

    /// <inheritdoc />
    /// <remarks>Nothing to cancel with a courier; the platform's own record is what changes.</remarks>
    public Task<Result> CancelShipmentAsync(
        string? providerShipmentId,
        string awb,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success());

    /// <inheritdoc />
    /// <remarks>
    /// There is nobody to ask. The failure is what stops the polling fallback from marking a
    /// hand-booked parcel as unreachable every half hour.
    /// </remarks>
    public Task<Result<CourierTracking>> TrackAsync(
        string awb,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<CourierTracking>(ShippingErrors.ProviderUnavailable));

    /// <inheritdoc />
    public bool VerifyWebhookSignature(string rawBody, string? signature) => false;

    /// <inheritdoc />
    public Result<CourierWebhookEnvelope> ReadWebhook(string rawBody)
        => Result.Failure<CourierWebhookEnvelope>(ShippingErrors.ProviderUnavailable);

    /// <summary>Builds the reference an operator's hand-typed booking is carried in.</summary>
    /// <param name="awb">The air waybill the courier gave them.</param>
    /// <param name="courier">Who is carrying it.</param>
    public static string ReferenceFor(string awb, string? courier)
        => string.IsNullOrWhiteSpace(courier)
            ? $"{AwbPrefix}{awb}"
            : $"{AwbPrefix}{awb}|{courier}";
}

/// <summary>
/// The adapters this build has, and which one answers for a given consignment.
/// </summary>
/// <remarks>
/// <para>
/// A shipment records the provider that booked it, and every later call about it goes back to that
/// adapter. That is what keeps a deployment that switches aggregator from losing its parcels: the
/// ones already in flight keep talking to the aggregator that has them, and only new bookings go to
/// the new one.
/// </para>
/// <para>
/// <see cref="Default"/> is the configured aggregator when it is usable and the manual adapter when
/// it is not. There is deliberately no third possibility: this module always has somewhere to book.
/// </para>
/// </remarks>
/// <param name="providers">Every registered adapter.</param>
/// <param name="options">Names the configured aggregator.</param>
internal sealed class ShippingProviderRegistry(
    IEnumerable<IShippingProvider> providers,
    IOptionsMonitor<ShippingOptions> options)
{
    private readonly IReadOnlyList<IShippingProvider> _providers = [.. providers];

    /// <summary>The adapter new bookings go to: the aggregator where it works, the manual one where it does not.</summary>
    public IShippingProvider Default
    {
        get
        {
            var configured = Find(ShippingProviders.Aggregator);

            return configured is { IsConfigured: true } && options.CurrentValue.HasAggregator
                ? configured
                : Manual;
        }
    }

    /// <summary>Hand-booking, which is always available.</summary>
    public IShippingProvider Manual => Find(ShippingProviders.Manual)!;

    /// <summary>Whether new bookings will actually reach an aggregator.</summary>
    public bool HasAggregator => Default.Name == ShippingProviders.Aggregator;

    /// <summary>The adapter for a stored provider name, or null when this build has none.</summary>
    /// <param name="name">The provider name as it is stored on a shipment.</param>
    public IShippingProvider? Find(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? null
            : _providers.FirstOrDefault(provider =>
                string.Equals(provider.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The adapter for a consignment, falling back to the manual one rather than failing.</summary>
    /// <remarks>
    /// A parcel booked by an adapter this build no longer has is still a parcel, and an operator has
    /// to be able to record what happened to it. The manual adapter accepts exactly that.
    /// </remarks>
    /// <param name="providerName">The provider recorded on the shipment.</param>
    public IShippingProvider For(string? providerName) => Find(providerName) ?? Manual;
}
