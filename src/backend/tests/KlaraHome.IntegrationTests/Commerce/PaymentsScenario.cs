using System.Net.Http.Json;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>A seller, a sellable offer and the stock behind it, ready to be bought.</summary>
/// <param name="VendorId">The seller.</param>
/// <param name="ListingId">The offer.</param>
/// <param name="VariantId">The variant it is against.</param>
/// <param name="Price">What it sells for.</param>
internal sealed record PaymentsSellableOffer(Guid VendorId, Guid ListingId, Guid VariantId, decimal Price);

/// <summary>An order this scenario placed, and what a payment test needs to act on it.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number — the gateway receipt.</param>
/// <param name="SubOrderId">The one seller's part in it.</param>
/// <param name="VendorId">That seller.</param>
/// <param name="Amount">What is payable.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="ProviderOrderId">The gateway order id opened for it, or null for cash on delivery.</param>
internal sealed record PaymentsPlacedOrder(
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    Guid VendorId,
    decimal Amount,
    string CurrencyCode,
    string? ProviderOrderId);

/// <summary>
/// Builds a seller, a live offer, a shipping rate and a placed order through the API a shopper would
/// use, so a Payments test starts from something the platform could actually have produced.
/// </summary>
/// <remarks>
/// <para>
/// The same reasoning as <see cref="CatalogScenario"/> and <see cref="VendorScenario"/>: a payment
/// against a hand-written order row would be a payment against an order the checkout could never
/// have created, standing on none of the totals, the sub-order split or the idempotency key a real
/// placement carries.
/// </para>
/// <para>
/// Shipping is given the widest possible rate card — one zone covering every PIN code, at zero cost
/// — because these tests are about money, not about logistics; a Payments test that could not reach
/// <c>place-order</c> because no delivery service was on offer would be failing for a reason that has
/// nothing to do with what it is proving.
/// </para>
/// </remarks>
/// <param name="admin">A client signed in as platform staff.</param>
/// <param name="cancellationToken">Cancellation token.</param>
internal sealed class PaymentsScenario(HttpClient admin, CancellationToken cancellationToken)
{
    private static bool _shippingReady;
    private static readonly SemaphoreSlim ShippingGate = new(1, 1);

    private readonly CatalogScenario _catalog = new(admin, cancellationToken);
    private readonly VendorScenario _vendors = new(admin, cancellationToken);

    /// <summary>Opens a seller, a warehouse, a taxonomy, a variant and a live, stocked offer.</summary>
    /// <param name="price">What it sells for.</param>
    /// <param name="quantity">How many units are on hand.</param>
    public async Task<PaymentsSellableOffer> OfferAsync(decimal price = 999m, int quantity = 50)
    {
        var seller = await _vendors.ActiveAsync();
        await EnsureShippingRateCardAsync();

        var taxonomy = await _catalog.TaxonomyAsync();
        var product = await _catalog.DraftAsync(taxonomy);
        await _catalog.ActivateVariantAsync(product.VariantId);
        await _catalog.PublishAsync(product.Id);

        // The listing's own declared MRP, not the variant's — a scenario asking for a high-value
        // offer (a refund above the maker-checker threshold, say) needs a matching ceiling, and the
        // listing carries one independently of what the variant's own label says.
        var listingId = await _catalog.OfferAsync(seller.Id, product.VariantId, price, mrp: price);

        var warehouseId = await WarehouseAsync(seller.Id);
        var stockItemId = await OpenStockAsync(listingId, warehouseId);
        await AdjustStockAsync(stockItemId, quantity);

        return new PaymentsSellableOffer(seller.Id, listingId, product.VariantId, price);
    }

    /// <summary>Saves a delivery address for a shopper, as they would on their first order.</summary>
    /// <param name="shopper">The signed-in shopper.</param>
    /// <param name="pincode">Where it goes.</param>
    public async Task<Guid> AddressAsync(HttpClient shopper, string pincode = "500034")
    {
        ArgumentNullException.ThrowIfNull(shopper);

        var stateId = await _vendors.StateIdAsync();

        var created = await Rest.ReadAsync(
            await shopper.PostAsJsonAsync(
                "/api/v1/store/me/addresses",
                new
                {
                    label = "Home",
                    recipientName = "Asha Rao",
                    mobile = $"9{Random.Shared.NextInt64(100_000_000, 999_999_999)}",
                    line1 = "12 MG Road",
                    line2 = (string?)null,
                    landmark = (string?)null,
                    city = "Hyderabad",
                    stateId,
                    pincode,
                    gstin = (string?)null,
                    type = "Home",
                    isDefaultShipping = true,
                    isDefaultBilling = true,
                },
                cancellationToken),
            cancellationToken);

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Carries a shopper from an empty basket to a placed order, through checkout exactly as the
    /// storefront drives it.
    /// </summary>
    /// <param name="shopper">The signed-in shopper.</param>
    /// <param name="offer">What to buy.</param>
    /// <param name="method">"prepaid" or "cod".</param>
    /// <param name="quantity">How many units.</param>
    /// <param name="idempotencyKey">The placement key, or a fresh one.</param>
    public async Task<(PaymentsPlacedOrder Order, JsonElement PlaceOrderResponse)> PlaceOrderAsync(
        HttpClient shopper,
        PaymentsSellableOffer offer,
        string method = "prepaid",
        int quantity = 1,
        string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        await AddressAsync(shopper);

        await Rest.ReadAsync(
            await shopper.PostAsJsonAsync(
                "/api/v1/store/cart/items",
                new { listingId = offer.ListingId, quantity },
                cancellationToken),
            cancellationToken);

        var checkout = await Rest.ReadAsync(
            await shopper.PostAsJsonAsync("/api/v1/store/checkout", new { }, cancellationToken),
            cancellationToken);

        var checkoutId = checkout.GetProperty("id").GetGuid();

        var addresses = await Rest.ReadAsync(
            await shopper.GetAsync(new Uri("/api/v1/store/me/addresses", UriKind.Relative), cancellationToken),
            cancellationToken);

        var addressId = addresses.EnumerateArray().First().GetProperty("id").GetGuid();

        await Rest.ReadAsync(
            await shopper.PutAsJsonAsync(
                $"/api/v1/store/checkout/{checkoutId}/address",
                new { shippingAddressId = addressId, billingAddressId = (Guid?)null, gstin = (string?)null },
                cancellationToken),
            cancellationToken);

        var options = await Rest.ReadAsync(
            await shopper.GetAsync(
                new Uri($"/api/v1/store/checkout/{checkoutId}/shipping-options", UriKind.Relative),
                cancellationToken),
            cancellationToken);

        var perVendor = options.EnumerateArray()
            .Select(vendor => new
            {
                vendorId = vendor.GetProperty("vendorId").GetGuid(),
                optionCode = vendor.GetProperty("options").EnumerateArray().First().GetProperty("code").GetString(),
            })
            .ToArray();

        await Rest.ReadAsync(
            await shopper.PutAsJsonAsync(
                $"/api/v1/store/checkout/{checkoutId}/shipping",
                new { perVendor },
                cancellationToken),
            cancellationToken);

        await Rest.ReadAsync(
            await shopper.PutAsJsonAsync(
                $"/api/v1/store/checkout/{checkoutId}/payment-method",
                new { method },
                cancellationToken),
            cancellationToken);

        await Rest.ReadAsync(
            await shopper.GetAsync(
                new Uri($"/api/v1/store/checkout/{checkoutId}/review", UriKind.Relative),
                cancellationToken),
            cancellationToken);

        var key = idempotencyKey ?? $"place:{Guid.NewGuid():N}";

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/store/checkout/{checkoutId}/place-order");

        request.Headers.Add("Idempotency-Key", key);

        var placed = await Rest.ReadAsync(await shopper.SendAsync(request, cancellationToken), cancellationToken);

        var orderId = placed.GetProperty("orderId").GetGuid();

        var order = await Rest.ReadAsync(
            await shopper.GetAsync(new Uri($"/api/v1/store/orders/{orderId}", UriKind.Relative), cancellationToken),
            cancellationToken);

        var subOrder = order.GetProperty("subOrders").EnumerateArray().First();

        var payment = placed.TryGetProperty("payment", out var paymentElement)
                      && paymentElement.ValueKind == JsonValueKind.Object
            ? paymentElement.GetProperty("providerOrderId").GetString()
            : null;

        return (
            new PaymentsPlacedOrder(
                orderId,
                placed.GetProperty("orderNumber").GetString()!,
                subOrder.GetProperty("id").GetGuid(),
                offer.VendorId,
                order.GetProperty("amountPayable").GetDecimal(),
                order.GetProperty("currencyCode").GetString()!,
                payment),
            placed);
    }

    /// <summary>Replays the same idempotency key. Answers whatever the API answers.</summary>
    /// <param name="shopper">The same shopper.</param>
    /// <param name="checkoutId">The same session.</param>
    /// <param name="idempotencyKey">The same key.</param>
    public async Task<HttpResponseMessage> ReplayPlaceOrderAsync(
        HttpClient shopper,
        Guid checkoutId,
        string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/store/checkout/{checkoutId}/place-order");

        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return await shopper.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Opens a rate card covering every PIN code in the country, once per test run.
    /// </summary>
    /// <remarks>
    /// Guarded by a static flag and a semaphore rather than being reopened per scenario: the zone's
    /// code is unique per tenant, and every commerce test in the collection shares one database.
    /// </remarks>
    private async Task EnsureShippingRateCardAsync()
    {
        if (_shippingReady)
        {
            return;
        }

        await ShippingGate.WaitAsync(cancellationToken);

        try
        {
            if (_shippingReady)
            {
                return;
            }

            var zone = await Rest.ReadAsync(
                await admin.PostAsJsonAsync(
                    "/api/v1/admin/shipping/zones",
                    new
                    {
                        code = $"all-india-{Guid.NewGuid():N}"[..24],
                        name = "All India",
                        priority = 0,
                        states = Array.Empty<Guid>(),
                        pincodeRanges = new[] { new { from = "000000", to = "999999" } },
                    },
                    cancellationToken),
                cancellationToken);

            var zoneId = zone.GetProperty("id").GetGuid();

            await Rest.ReadAsync(
                await admin.PostAsJsonAsync(
                    "/api/v1/admin/shipping/rates",
                    new
                    {
                        zoneId,
                        method = "standard",
                        vendorId = (Guid?)null,
                        terms = new
                        {
                            minWeightGrams = 0,
                            maxWeightGrams = 100_000,
                            minOrderValue = 0m,
                            maxOrderValue = (decimal?)null,
                            // Nonzero on purpose: this all-India, platform-wide rate is the cheapest
                            // option most checkouts in the collection resolve to, so a free (0m)
                            // base rate made every other test's basket look free too. A Payments test
                            // only needs shipping to be cheap and predictable, not literally zero.
                            baseRate = 39m,
                            perKgRate = 0m,
                            freeAbove = (decimal?)null,
                            codFee = 0m,
                            isCodAllowed = true,
                            etaMinDays = 3,
                            etaMaxDays = 7,
                        },
                        isActive = true,
                    },
                    cancellationToken),
                cancellationToken);

            _shippingReady = true;
        }
        finally
        {
            ShippingGate.Release();
        }
    }

    private async Task<Guid> WarehouseAsync(Guid vendorId)
    {
        var stateId = await _vendors.StateIdAsync();

        var created = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/warehouses",
                new
                {
                    vendorId,
                    code = $"WH-{Guid.NewGuid():N}"[..12],
                    name = "Seller Warehouse",
                    pincode = "500034",
                    address = new
                    {
                        line1 = "Plot 4, Industrial Area",
                        line2 = (string?)null,
                        landmark = (string?)null,
                        city = "Hyderabad",
                        stateId,
                        contactName = "Warehouse Manager",
                        contactPhone = "9876543210",
                    },
                    priority = 0,
                },
                cancellationToken),
            cancellationToken);

        return created.GetProperty("id").GetGuid();
    }

    private async Task<Guid> OpenStockAsync(Guid listingId, Guid warehouseId)
    {
        var opened = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/stock",
                new { listingId, warehouseId },
                cancellationToken),
            cancellationToken);

        return opened.GetProperty("id").GetGuid();
    }

    private async Task AdjustStockAsync(Guid stockItemId, int quantity)
        => await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/stock/adjustments",
                new { stockItemId, change = quantity, reason = "Adjustment", note = "Scenario stock." },
                cancellationToken),
            cancellationToken);
}
