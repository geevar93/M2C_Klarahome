using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Shipping;
using KlaraHome.Contracts.Vendors;

namespace KlaraHome.Modules.Carts.Infrastructure.Checkout;

/// <summary>
/// The delivery choice a store has before it has a logistics integration.
/// </summary>
/// <remarks>
/// <para>
/// One service, priced at nothing, promised against the seller's own dispatch SLA. It exists so
/// that checkout is a complete, working flow at Step 13 rather than a screen that throws, and it is
/// registered with <c>TryAdd</c> so the Shipping module (Step 16) replaces it without this class
/// being touched.
/// </para>
/// <para>
/// Charging zero rather than guessing is the same position <c>QuoteRequest.ShippingAmount</c>
/// already takes. A platform with no rate card has no basis for a delivery charge, and inventing
/// one would put a figure on an invoice that nobody could justify to a customer.
/// </para>
/// <para>
/// The promise is derived rather than invented: dispatch comes from the seller, and the delivery
/// window is a flat three-to-seven days, which is what a surface-mail estimate looks like in India
/// and is deliberately conservative. Step 16 replaces it with the aggregator's own estimate.
/// </para>
/// </remarks>
/// <param name="vendors">Supplies each seller's dispatch SLA.</param>
/// <param name="settings">Supplies whether cash on delivery is offered at all, and the delivery area.</param>
/// <param name="reference">Resolves a PIN code to a city, for the delivery-area check.</param>
internal sealed class StandardShippingOptions(
    IVendorDirectory vendors,
    IStoreSettings settings,
    IReferenceData reference) : IShippingOptions
{
    /// <summary>The code the one available service is stored as.</summary>
    public const string StandardCode = "standard";

    private const int PromisedMinDays = 3;
    private const int PromisedMaxDays = 7;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ShippingOption>> QuoteAsync(
        ShipmentQuoteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var seller = await vendors.FindAsync(request.VendorId, cancellationToken).ConfigureAwait(false);

        if (seller is null || !seller.IsActive)
        {
            // An empty list means "cannot be served", which the checkout reports against this
            // seller rather than failing the whole basket.
            return [];
        }

        var commerce = await settings.GetAsync<CommerceSettings>(cancellationToken).ConfigureAwait(false);

        return
        [
            new ShippingOption(
                StandardCode,
                "Standard delivery",
                Carrier: null,
                Amount: 0m,
                TaxAmount: 0m,
                seller.DispatchSlaHours,
                PromisedMinDays,
                PromisedMaxDays,
                commerce.CodEnabled),
        ];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The delivery area is still honoured here, and deliberately so. Coverage is the operator's
    /// trading decision (ADR-018) and has nothing to do with whether a courier integration exists —
    /// a store that has decided to deliver only within one city must refuse the rest of India
    /// whether it books parcels through an API or over a counter. Serviceability is the half this
    /// implementation cannot answer, so it says yes: with no courier to ask, refusing an order for a
    /// destination nobody has checked would lose the sale outright.
    /// </remarks>
    public async ValueTask<DeliveryCheck> CheckDestinationAsync(
        string pincode,
        bool isCod = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pincode);

        var policy = await settings
            .GetAsync<DeliveryCoverageSettings>(cancellationToken)
            .ConfigureAwait(false);

        var known = await reference.PincodeAsync(pincode, cancellationToken).ConfigureAwait(false);
        var covered = Covers(policy, pincode, known?.City);

        return new DeliveryCheck(
            pincode,
            covered,
            covered,
            Serviceable: true,
            CodAvailable: true,
            known?.City,
            known?.StateName,
            EtaDays: null,
            covered ? DeliveryRefusal.None : DeliveryRefusal.NotCovered,
            covered ? null : policy.Message);
    }

    /// <summary>
    /// Whether a policy admits a PIN code: a block beats everything, then any one allow rule is enough.
    /// </summary>
    /// <remarks>
    /// The same rule the Shipping module applies, written twice because the two modules may not
    /// reference one another and a shared helper would have to live in the contracts assembly, where
    /// behaviour does not belong. It is ten lines of pure matching over a settings record, and this
    /// implementation is replaced the moment Shipping is registered.
    /// </remarks>
    private static bool Covers(DeliveryCoverageSettings policy, string pincode, string? city)
    {
        if (!policy.Enabled)
        {
            return true;
        }

        var code = pincode.Trim();

        if (policy.BlockedPincodes.Any(blocked => Same(blocked, code)))
        {
            return false;
        }

        return policy.AllowedPincodes.Any(allowed => Same(allowed, code))
               || policy.AllowedPincodePrefixes.Any(prefix =>
                   !string.IsNullOrWhiteSpace(prefix)
                   && code.StartsWith(prefix.Trim(), StringComparison.Ordinal))
               || (!string.IsNullOrWhiteSpace(city)
                   && policy.AllowedCities.Any(allowed => Same(allowed, city)));
    }

    private static bool Same(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
