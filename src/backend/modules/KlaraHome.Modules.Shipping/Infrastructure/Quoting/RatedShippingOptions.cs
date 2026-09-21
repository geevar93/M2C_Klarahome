using KlaraHome.Contracts.Shipping;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Serviceability;

namespace KlaraHome.Modules.Shipping.Infrastructure.Quoting;

/// <summary>
/// What one seller's parcel may be sent by, and what it costs.
/// </summary>
/// <remarks>
/// <para>
/// The seam the Cart module declared at Step 13 and left filled by a degenerate free-delivery
/// quoter. Registering this replaces it, and nothing in checkout changes: the contract is the same,
/// the codes are the same shape, and a store with no rate card configured simply gets no options,
/// which the checkout already reports per seller.
/// </para>
/// <para>
/// Three things have to be true for a service to be offered, and each rules out a different failure.
/// The <b>destination must be servable</b>, or the parcel cannot be carried at all. The <b>price
/// source must cover it</b> — the courier's live price or the rate card — or the platform has no
/// basis for a charge and inventing one would put a figure on an invoice nobody could justify. And for a cash parcel the <b>courier must handle cash</b> —
/// plenty of Indian PIN codes take a prepaid parcel and refuse a COD one, and offering it anyway is
/// a delivery that fails at the door.
/// </para>
/// <para>
/// The promise is built from both halves of what is known: the seller's own dispatch SLA says how
/// long they have to hand the parcel over, and the rate rule says how long the courier then takes.
/// Where a courier has told us its own estimate for the destination, the faster of the two is used —
/// a rate card is a tariff, and a courier that says three days for this PIN code knows more than it
/// does.
/// </para>
/// <para>
/// The gates read the serviceability cache and never an aggregator. The price comes from
/// <see cref="IDeliveryChargeSource"/>, which <c>Shipping:ChargeSource</c> points at either the rate
/// card or the courier's live price; only checkout's delivery step asks for a price, so a live call
/// never reaches a product page or a basket render.
/// </para>
/// </remarks>
/// <param name="charges">Prices the parcel, from the rate card or the courier.</param>
/// <param name="serviceability">Answers whether the destination can be served, from the cache.</param>
/// <param name="coverage">Answers whether this store delivers there at all.</param>
/// <param name="vendors">Supplies each seller's dispatch SLA.</param>
internal sealed class RatedShippingOptions(
    IDeliveryChargeSource charges,
    ServiceabilityService serviceability,
    DeliveryCoverageService coverage,
    IVendorDirectory vendors) : IShippingOptions
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ShippingOption>> QuoteAsync(
        ShipmentQuoteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var seller = await vendors.FindAsync(request.VendorId, cancellationToken).ConfigureAwait(false);

        if (seller is null || !seller.IsActive)
        {
            // An empty list means "cannot be served", which the checkout reports against this seller
            // rather than failing the whole basket.
            return [];
        }

        // The store's own delivery area, checked before the courier's. One of the five gates
        // ADR-018 names: an uncovered destination is offered no service at all, so a checkout
        // cannot advance to a payment method for an order that could never be shipped.
        var (covered, _, _) = await coverage
            .EvaluateAsync(request.DestinationPincode, cancellationToken)
            .ConfigureAwait(false);

        if (!covered)
        {
            return [];
        }

        var answer = await serviceability
            .ReadAsync(request.DestinationPincode, cancellationToken)
            .ConfigureAwait(false);

        if (!answer.IsServiceable || (request.IsCod && !answer.CodOk))
        {
            return [];
        }

        var priced = await charges
            .PriceAsync(
                new DeliveryChargeRequest(
                    request.VendorId,
                    request.DestinationStateId,
                    request.DestinationPincode,
                    Math.Max(request.WeightGrams, 0),
                    request.ItemsTotal,
                    request.IsCod),
                cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. priced.Select(service => new ShippingOption(
                CodeFor(service.Method),
                NameFor(service.Method),
                service.Carrier ?? answer.Courier,
                service.Amount,
                service.TaxAmount,
                seller.DispatchSlaHours,
                PromisedMin(service, answer),
                PromisedMax(service, answer),
                service.IsCodAllowed && answer.CodOk)),
        ];
    }

    /// <inheritdoc />
    public async ValueTask<DeliveryCheck> CheckDestinationAsync(
        string pincode,
        bool isCod = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pincode);

        var (covered, place, message) = await coverage
            .EvaluateAsync(pincode, cancellationToken)
            .ConfigureAwait(false);

        var answer = await serviceability.ReadAsync(pincode, cancellationToken).ConfigureAwait(false);

        // The courier's own view of where the PIN code is, used only where the platform's reference
        // data and the settings-side lookup both came up empty.
        var city = place.City ?? answer.City;
        var state = place.State ?? answer.State;

        var refusal = DeliveryCoverageService.RefusalFor(covered, answer.IsServiceable, isCod, answer.CodOk);

        return new DeliveryCheck(
            pincode,
            covered && answer.IsServiceable,
            covered,
            answer.IsServiceable,
            answer.CodOk,
            city,
            state,
            answer.EtaDays,
            refusal,
            refusal == DeliveryRefusal.NotCovered ? message : null);
    }

    /// <summary>The stable code a shopper's choice is stored as.</summary>
    /// <remarks>
    /// The service, not the rule. A rate card is reissued and a rule replaced; a saved checkout that
    /// pointed at a row id would point at nothing, which is exactly what the contract warns against.
    /// </remarks>
    /// <param name="method">The service.</param>
    public static string CodeFor(ShippingMethod method) => method.ToString().ToLowerInvariant();

    /// <summary>The service a stored code names, or standard when the code is unknown.</summary>
    /// <remarks>
    /// Falls back rather than failing. A code from a rate card that has since been reshaped must
    /// still dispatch a parcel somebody has already paid for.
    /// </remarks>
    /// <param name="code">The code stored on the sub-order.</param>
    public static ShippingMethod MethodFor(string? code)
        => Enum.TryParse<ShippingMethod>(code, ignoreCase: true, out var method)
            ? method
            : ShippingMethod.Standard;

    /// <summary>What the shopper sees.</summary>
    /// <param name="method">The service.</param>
    public static string NameFor(ShippingMethod method)
        => method switch
        {
            ShippingMethod.Express => "Express delivery",
            _ => "Standard delivery",
        };

    private static int PromisedMin(PricedService service, ServiceabilityAnswer answer)
        => answer.EtaDays is { } days && days > 0
            ? Math.Min(service.EtaMinDays, days)
            : service.EtaMinDays;

    private static int PromisedMax(PricedService service, ServiceabilityAnswer answer)
    {
        var promised = answer.EtaDays is { } days && days > 0
            ? Math.Min(service.EtaMaxDays, days)
            : service.EtaMaxDays;

        // The window never inverts, whichever estimate won. A promise of "four to three days" would
        // render as nonsense on a checkout screen.
        return Math.Max(promised, PromisedMin(service, answer));
    }
}
