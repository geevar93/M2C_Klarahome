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
/// <param name="settings">Supplies whether cash on delivery is offered at all.</param>
internal sealed class StandardShippingOptions(IVendorDirectory vendors, IStoreSettings settings) : IShippingOptions
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
}
