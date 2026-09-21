using System.Globalization;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Rating;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Infrastructure.Quoting;

/// <summary>The keys <c>Shipping:ChargeSource</c> takes.</summary>
internal static class DeliveryChargeSources
{
    /// <summary>The courier's live price, passed on at cost, with the rate card as the fallback.</summary>
    public const string Aggregator = "aggregator";

    /// <summary>The platform's own rate card alone.</summary>
    public const string RateCard = "ratecard";
}

/// <summary>One seller's parcel, as it is priced.</summary>
/// <param name="VendorId">The seller dispatching it, whose pickup address is the origin.</param>
/// <param name="DestinationStateId">The destination's state, for the rate card's zones.</param>
/// <param name="DestinationPincode">The destination.</param>
/// <param name="WeightGrams">The dead weight of the lines, or zero where the catalogue has none.</param>
/// <param name="ItemsTotal">What the seller's lines come to.</param>
/// <param name="IsCod">Whether cash will be collected at the door.</param>
internal sealed record DeliveryChargeRequest(
    Guid VendorId,
    Guid? DestinationStateId,
    string DestinationPincode,
    int WeightGrams,
    decimal ItemsTotal,
    bool IsCod);

/// <summary>A service a parcel can be sent by, and what the shopper pays for it.</summary>
/// <param name="Method">Standard or express.</param>
/// <param name="Carrier">The courier, where one is known.</param>
/// <param name="Amount">What the shopper pays, inclusive of tax.</param>
/// <param name="TaxAmount">The tax inside that figure.</param>
/// <param name="EtaMinDays">Earliest delivery, in days from dispatch.</param>
/// <param name="EtaMaxDays">Latest delivery, in days from dispatch.</param>
/// <param name="IsCodAllowed">Whether cash may be collected on this service.</param>
internal sealed record PricedService(
    ShippingMethod Method,
    string? Carrier,
    decimal Amount,
    decimal TaxAmount,
    int EtaMinDays,
    int EtaMaxDays,
    bool IsCodAllowed);

/// <summary>
/// Where the delivery charge a shopper pays comes from.
/// </summary>
/// <remarks>
/// <para>
/// The switch between the platform's rate card and the courier's own price, chosen by
/// <c>Shipping:ChargeSource</c> when the container is built. Everything around it — the store's
/// coverage, the serviceability cache, the seller's dispatch SLA — is the same whichever answers,
/// which is why <see cref="RatedShippingOptions"/> asks this rather than either source directly.
/// </para>
/// <para>
/// An empty list means "cannot be carried", and checkout reports it against the seller.
/// </para>
/// </remarks>
internal interface IDeliveryChargeSource
{
    /// <summary>Prices a parcel on every service that can carry it, cheapest first.</summary>
    /// <param name="request">The parcel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PricedService>> PriceAsync(
        DeliveryChargeRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The platform's own rate card: zones, weight bands and a free-above threshold.
/// </summary>
/// <param name="rates">Chooses the zone and the rule, and prices the parcel.</param>
internal sealed class RateCardChargeSource(RateResolver rates) : IDeliveryChargeSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PricedService>> PriceAsync(
        DeliveryChargeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var priced = await rates
            .RateAsync(
                request.VendorId,
                request.DestinationStateId,
                request.DestinationPincode,
                Math.Max(request.WeightGrams, 0),
                request.ItemsTotal,
                request.IsCod,
                cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. priced.Select(parcel => new PricedService(
                parcel.Rate.Method,
                Carrier: null,
                parcel.Amount,
                parcel.TaxAmount,
                parcel.Rate.EtaMinDays,
                parcel.Rate.EtaMaxDays,
                parcel.Rate.IsCodAllowed)),
        ];
    }
}

/// <summary>
/// The configured courier's live price, passed on to the shopper at cost.
/// </summary>
/// <remarks>
/// <para>
/// Asked at checkout only, and bounded by <c>Shipping:LiveRateTimeoutMilliseconds</c>. Every way
/// of not getting an answer — no courier configured, a seller with no pickup address, a failed or
/// slow call — falls through to the rate card, so the courier's uptime is never the checkout's.
/// What does <em>not</em> fall through is the courier answering that nobody will carry the parcel:
/// that is a fact about the route, and pricing it from a tariff anyway would sell a delivery that
/// cannot happen.
/// </para>
/// <para>
/// One service is offered: standard, at the price of the cheapest courier on the route (the User's
/// choice; Shiprocket's own recommendation is often several times dearer). Booking does not pin a
/// courier — Shiprocket assigns one under the account's courier-priority rule — so that rule must be
/// set to <i>Cheapest</i> for the freight billed to match the price the shopper paid. An express
/// service needs the booking to name the courier that was quoted, and is not offered until it does.
/// </para>
/// <para>
/// A price is reused for <c>Shipping:LiveRateCacheSeconds</c> for the same route, weight and cash
/// flag, so the figure shown when options load is the figure recorded when one is chosen.
/// </para>
/// </remarks>
/// <param name="providers">Resolves the configured courier.</param>
/// <param name="pickups">Supplies the seller's pickup PIN code, which is the route's origin.</param>
/// <param name="fallback">The rate card, for every case the courier cannot answer.</param>
/// <param name="cache">Holds recent live prices.</param>
/// <param name="options">Timeout, tax convention and the default parcel weight.</param>
/// <param name="logger">Records every fallback, so a silent one is visible.</param>
internal sealed partial class AggregatorChargeSource(
    ShippingProviderRegistry providers,
    IVendorPickupPoints pickups,
    [FromKeyedServices(DeliveryChargeSources.RateCard)] IDeliveryChargeSource fallback,
    IMemoryCache cache,
    IOptions<ShippingOptions> options,
    ILogger<AggregatorChargeSource> logger) : IDeliveryChargeSource
{
    /// <summary>The window promised when a courier gives no estimate, matching the rate card's seed.</summary>
    private const int DefaultEtaMinDays = 4;

    private const int DefaultEtaMaxDays = 8;

    /// <summary>Days added to a courier's estimate for the latest date promised.</summary>
    /// <remarks>
    /// A courier's estimate is its median, not its ceiling. Promising exactly that turns every late
    /// parcel into a broken promise.
    /// </remarks>
    private const int EtaBufferDays = 2;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PricedService>> PriceAsync(
        DeliveryChargeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!providers.HasCourierApi)
        {
            return await fallback.PriceAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var provider = providers.Default;

        var origin = await pickups.FindAsync(request.VendorId, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (origin is null || string.IsNullOrWhiteSpace(origin.Pincode))
        {
            FellBack(logger, request.VendorId, request.DestinationPincode, "the seller has no pickup address");
            return await fallback.PriceAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var settings = options.Value;
        var weight = request.WeightGrams > 0 ? request.WeightGrams : settings.DefaultParcelWeightGrams;

        var quote = new CourierRateRequest(
            origin.Pincode,
            request.DestinationPincode,
            weight,
            request.ItemsTotal,
            request.IsCod);

        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"shipping:live-rate:{provider.Name}:{quote.OriginPincode}:{quote.DestinationPincode}:{quote.WeightGrams}:{quote.IsCod}:{quote.DeclaredValue:0.##}");

        if (!cache.TryGetValue(key, out IReadOnlyList<CourierRate>? couriers) || couriers is null)
        {
            couriers = await AskAsync(provider, quote, request.VendorId, settings, cancellationToken).ConfigureAwait(false);

            if (couriers is null)
            {
                return await fallback.PriceAsync(request, cancellationToken).ConfigureAwait(false);
            }

            if (settings.LiveRateCacheSeconds > 0)
            {
                cache.Set(key, couriers, TimeSpan.FromSeconds(settings.LiveRateCacheSeconds));
            }
        }

        var eligible = couriers.Where(courier => !request.IsCod || courier.CodOk).ToList();

        if (eligible.Count == 0)
        {
            // Asked, and answered: nobody carries this parcel on this route (or nobody collects cash
            // on it). An answer, not a failure, so the rate card is not asked to contradict it.
            return [];
        }

        // The cheapest, with the aggregator's own recommendation breaking a tie. The account's
        // courier priority must be set to "Cheapest" for the booking to match this quote.
        var chosen = eligible
            .OrderBy(courier => courier.Freight)
            .ThenByDescending(courier => courier.IsRecommended)
            .First();

        var amount = Math.Round(
            settings.AggregatorRatesIncludeTax
                ? chosen.Freight
                : chosen.Freight * (100m + settings.FreightGstRate) / 100m,
            2,
            MidpointRounding.AwayFromZero);

        var (etaMin, etaMax) = chosen.EtaDays is > 0 and var days
            ? (days, days + EtaBufferDays)
            : (DefaultEtaMinDays, DefaultEtaMaxDays);

        return
        [
            new PricedService(
                ShippingMethod.Standard,
                chosen.Courier,
                amount,
                RateResolver.TaxInside(amount, settings.FreightGstRate),
                etaMin,
                etaMax,
                chosen.CodOk),
        ];
    }

    /// <summary>The courier's answer, or null when it could not be had in time.</summary>
    private async Task<IReadOnlyList<CourierRate>?> AskAsync(
        IShippingProvider provider,
        CourierRateRequest quote,
        Guid vendorId,
        ShippingOptions settings,
        CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(settings.LiveRateTimeoutMilliseconds);

        try
        {
            var answered = await provider.QuoteRatesAsync(quote, bounded.Token).ConfigureAwait(false);

            if (answered.IsFailure)
            {
                FellBack(logger, vendorId, quote.DestinationPincode, answered.Error.Message);
                return null;
            }

            return answered.Value;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            FellBack(
                logger,
                vendorId,
                quote.DestinationPincode,
                $"the courier did not answer within {settings.LiveRateTimeoutMilliseconds} ms");
            return null;
        }
    }

    [LoggerMessage(EventId = 1740, Level = LogLevel.Warning,
        Message = "Live delivery rate unavailable (seller {VendorId}, PIN {Pincode}): {Reason}. "
                  + "The rate card priced it instead.")]
    private static partial void FellBack(ILogger logger, Guid vendorId, string pincode, string reason);
}
