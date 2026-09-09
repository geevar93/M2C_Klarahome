using System.Net.Http.Json;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>A seller who has something to sell, and enough stock behind it to sell it.</summary>
/// <param name="Vendor">The onboarded seller.</param>
/// <param name="WarehouseId">The location their units are held at.</param>
/// <param name="StockItemId">The stock row those units are on.</param>
/// <param name="ProductId">The product.</param>
/// <param name="Slug">Its URL segment.</param>
/// <param name="VariantId">The variant being offered.</param>
/// <param name="ListingId">The offer a shopper buys.</param>
/// <param name="SellingPrice">What they are asking, inclusive of GST.</param>
internal sealed record OrderSeller(
    OnboardedVendor Vendor,
    Guid WarehouseId,
    Guid StockItemId,
    Guid ProductId,
    string Slug,
    Guid VariantId,
    Guid ListingId,
    decimal SellingPrice);

/// <summary>A state or union territory, as the seeded reference data has it.</summary>
/// <param name="Id">The <c>platform.states</c> row.</param>
/// <param name="Code">Its two-digit GST state code, which decides CGST + SGST against IGST.</param>
/// <param name="Name">What it is called.</param>
internal sealed record IndianState(Guid Id, string Code, string Name);

/// <summary>A signed-in shopper with somewhere for a parcel to go.</summary>
/// <param name="Client">Their client, carrying their token.</param>
/// <param name="CustomerId">Their account.</param>
/// <param name="AddressId">The address in their book that a checkout ships to.</param>
internal sealed record OrderShopper(HttpClient Client, Guid CustomerId, Guid AddressId);

/// <summary>An order that exists, and the identifiers a test needs to act on it.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">The number a shopper quotes.</param>
/// <param name="CartId">The basket it came from — the stock-reservation reference.</param>
/// <param name="SessionId">The checkout session that produced it.</param>
/// <param name="Status">The derived order status the placement reported.</param>
/// <param name="Payment">The gateway instruction, for a prepaid order.</param>
internal sealed record PlacedOrderFacts(
    Guid OrderId,
    string OrderNumber,
    Guid CartId,
    Guid SessionId,
    string Status,
    JsonElement Payment);

/// <summary>
/// Builds the order a Step 14 test stands on, through the API a shopper and an operator would use.
/// </summary>
/// <remarks>
/// <para>
/// The same reasoning as <see cref="VendorScenario"/> and <see cref="CatalogScenario"/>, and it
/// matters more here than anywhere else on the platform. An order written as rows is an order with
/// no reservation behind it, no coupon redemption against it, no frozen commission on its lines and
/// no <c>placed</c> entry on its timeline — and every one of those is something a Step 14 row is
/// about. So the whole journey is driven: a seller is onboarded, given a warehouse and stock, and
/// publishes an offer; a shopper registers, saves an address, fills a basket and walks the five
/// steps of checkout; and <c>place-order</c> is what creates the order.
/// </para>
/// <para>
/// It composes the two scenarios that came before rather than repeating them, so a change to how a
/// seller is onboarded or a product is published reaches these tests too.
/// </para>
/// </remarks>
/// <param name="admin">A client signed in as platform staff.</param>
/// <param name="cancellationToken">Cancellation token.</param>
internal sealed class OrderScenario(HttpClient admin, CancellationToken cancellationToken)
{
    /// <summary>PIN codes inside the seeded delivery coverage — Hyderabad, per the shipped settings.</summary>
    public const string CoveredPincode = "500034";

    /// <summary>The cash-on-delivery ceiling these tests pin, which is the validator's own maximum.</summary>
    private const decimal CodCeiling = 1_000_000m;

    private readonly VendorScenario _sellers = new(admin, cancellationToken);
    private readonly CatalogScenario _catalogue = new(admin, cancellationToken);
    private bool _cashOnDelivery;

    /// <summary>The client this scenario drives, for a test that wants to carry on from here.</summary>
    public HttpClient Admin => admin;

    /// <summary>The seller builder underneath, for a test that needs a second seller of its own.</summary>
    public VendorScenario Sellers => _sellers;

    /// <summary>The catalogue builder underneath.</summary>
    public CatalogScenario Catalogue => _catalogue;

    /// <summary>A category, brand and attribute vocabulary nothing else is using.</summary>
    public Task<CatalogTaxonomy> TaxonomyAsync() => _catalogue.TaxonomyAsync();

    /// <summary>
    /// A seller who is active, has a warehouse, has stock, and has a live offer against a published
    /// product of their own.
    /// </summary>
    /// <remarks>
    /// The warehouse is opened before the offer is activated on purpose: Inventory opens a stock row
    /// at zero when a listing goes live, and only in a warehouse that already exists. Opening the
    /// warehouse afterwards leaves the offer permanently untracked, which reads in a test as "out of
    /// stock" for a reason that has nothing to do with stock.
    /// </remarks>
    /// <param name="taxonomy">The vocabulary the product is described with.</param>
    /// <param name="sellingPrice">
    /// What they are asking, inclusive of GST. At or below the variant's MRP of ₹1,299, because an
    /// offer above the printed price is illegal in India and the module refuses it.
    /// </param>
    /// <param name="quantity">How many units to put on the shelf.</param>
    /// <param name="commissionRate">What the platform charges them.</param>
    public async Task<OrderSeller> SellerAsync(
        CatalogTaxonomy taxonomy,
        decimal sellingPrice = 999m,
        int quantity = 50,
        decimal commissionRate = 10m)
    {
        ArgumentNullException.ThrowIfNull(taxonomy);

        var vendor = await _sellers.ActiveAsync(commissionRate).ConfigureAwait(false);
        var warehouseId = await WarehouseAsync(vendor.Id).ConfigureAwait(false);

        var product = await _catalogue.DraftAsync(taxonomy).ConfigureAwait(false);

        await Rest.ReadAsync(
            await _catalogue.ActivateVariantAsync(product.VariantId).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        await _catalogue.PublishAsync(product.Id).ConfigureAwait(false);

        var listingId = await _catalogue
            .OfferAsync(vendor.Id, product.VariantId, sellingPrice)
            .ConfigureAwait(false);

        var stockItemId = await StockAsync(listingId, warehouseId, quantity).ConfigureAwait(false);

        return new OrderSeller(
            vendor,
            warehouseId,
            stockItemId,
            product.Id,
            product.Slug,
            product.VariantId,
            listingId,
            sellingPrice);
    }

    /// <summary>
    /// Gives a seller a GST registration in a named state, so their supplies can be inter-state.
    /// </summary>
    /// <remarks>
    /// A seller with no GSTIN supplies intra-state by definition — the split is decided against the
    /// supplier's registered state and an unknown one falls back to the store's — so IGST is
    /// unreachable without this. The PAN is reissued alongside it because characters three to twelve
    /// of a GSTIN <em>are</em> the holder's PAN, and the module refuses a pair that disagrees.
    /// </remarks>
    /// <param name="vendorId">The seller.</param>
    /// <param name="state">The state they are registered in.</param>
    public async Task RegisterInStateAsync(Guid vendorId, IndianState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var vendor = await Rest.ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/vendors/{vendorId}", UriKind.Relative), cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        var pan = NewPan();

        await Rest.ReadAsync(
            await admin.PutAsJsonAsync(
                $"/api/v1/admin/vendors/{vendorId}",
                new
                {
                    legalName = vendor.GetProperty("legalName").GetString(),
                    businessType = vendor.GetProperty("businessType").GetString(),
                    pan,

                    // Two state digits, the PAN, an entity digit, the literal Z, and a checksum
                    // character — the shape the module's own regular expression enforces.
                    gstin = $"{state.Code}{pan}1Z5",
                    registeredAddress = new
                    {
                        line1 = "Plot 42",
                        line2 = (string?)null,
                        city = state.Name,
                        stateId = state.Id,
                        pincode = "500034",
                    },
                },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The state this scenario's shoppers live in, with its GST code.</summary>
    public async Task<IndianState> HomeStateAsync()
    {
        var home = await _sellers.StateIdAsync().ConfigureAwait(false);

        return await StateAsync(state => state.GetProperty("id").GetGuid() == home).ConfigureAwait(false);
    }

    /// <summary>A state other than the one this scenario's shoppers live in, with its GST code.</summary>
    /// <remarks>
    /// Read from the seeded reference data rather than hard-coded, so the pair stays a real state
    /// and a real code — the module refuses a GSTIN whose first two digits name no state at all.
    /// </remarks>
    public async Task<IndianState> ElsewhereAsync()
    {
        var home = await _sellers.StateIdAsync().ConfigureAwait(false);

        return await StateAsync(state => state.GetProperty("id").GetGuid() != home).ConfigureAwait(false);
    }

    /// <summary>The first state in the seeded reference data matching a predicate.</summary>
    /// <param name="matches">What makes a state the one wanted.</param>
    private async Task<IndianState> StateAsync(Func<JsonElement, bool> matches)
    {
        var states = await Rest.ReadAsync(
            await admin.GetAsync(new Uri("/api/v1/store/states", UriKind.Relative), cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        var all = states.ValueKind == JsonValueKind.Array ? states : states.GetProperty("items");
        var found = all.EnumerateArray().First(matches);

        return new IndianState(
            found.GetProperty("id").GetGuid(),
            found.GetProperty("code").GetString()!,
            found.GetProperty("name").GetString()!);
    }

    /// <summary>Opens a stock location for one seller.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="pincode">Where it is. Inside the delivery coverage by default.</param>
    public async Task<Guid> WarehouseAsync(Guid vendorId, string pincode = CoveredPincode)
    {
        var warehouse = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/warehouses",
                new
                {
                    vendorId,
                    code = $"WH{Suffix()}",
                    name = "Test warehouse",
                    pincode,
                    address = (object?)null,
                    priority = 0,
                },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return warehouse.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Puts units on the shelf for one offer, opening the stock row first if nothing opened it.
    /// </summary>
    /// <remarks>
    /// The row is normally opened by Inventory's own handler when the offer goes live, so this looks
    /// for it before creating one — a second row for the same (offer, location) pair is refused by a
    /// unique index, and the refusal would be this helper's fault rather than the product's.
    /// </remarks>
    /// <param name="listingId">The offer.</param>
    /// <param name="warehouseId">Where the units are held.</param>
    /// <param name="quantity">How many to book in.</param>
    public async Task<Guid> StockAsync(Guid listingId, Guid warehouseId, int quantity)
    {
        var existing = await Rest.ReadAsync(
            await admin.GetAsync(
                new Uri($"/api/v1/admin/stock?listingId={listingId}", UriKind.Relative),
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        var row = existing.GetProperty("items").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("warehouseId").GetGuid() == warehouseId);

        var stockItemId = row.ValueKind == JsonValueKind.Object
            ? row.GetProperty("id").GetGuid()
            : (await Rest.ReadAsync(
                await admin.PostAsJsonAsync(
                    "/api/v1/admin/stock",
                    new { listingId, warehouseId },
                    cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false))
                .GetProperty("id").GetGuid();

        if (quantity > 0)
        {
            await Rest.ReadAsync(
                await admin.PostAsJsonAsync(
                    "/api/v1/admin/stock/adjustments",
                    new { stockItemId, change = quantity, reason = "Adjustment", note = "Seeded by a test." },
                    cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }

        return stockItemId;
    }

    /// <summary>What is on the shelf and what is held against it, for one stock row.</summary>
    /// <param name="stockItemId">The stock row.</param>
    public async Task<(int OnHand, int Reserved, int Available)> StockLevelsAsync(Guid stockItemId)
    {
        var item = await Rest.ReadAsync(
            await admin.GetAsync(
                new Uri($"/api/v1/admin/stock/{stockItemId}", UriKind.Relative),
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return (
            item.GetProperty("quantityOnHand").GetInt32(),
            item.GetProperty("quantityReserved").GetInt32(),
            item.GetProperty("quantityAvailable").GetInt32());
    }

    /// <summary>Gives a signed-in shopper an address, and answers who they are.</summary>
    /// <param name="client">A client signed in as a shopper.</param>
    /// <param name="pincode">Where their parcels go. Inside the delivery coverage by default.</param>
    public async Task<OrderShopper> ShopperAsync(HttpClient client, string pincode = CoveredPincode)
    {
        ArgumentNullException.ThrowIfNull(client);

        var me = await Rest.ReadAsync(
            await client.GetAsync(new Uri("/api/v1/store/me", UriKind.Relative), cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        var address = await Rest.ReadAsync(
            await client.PostAsJsonAsync(
                "/api/v1/store/me/addresses",
                new
                {
                    label = "Home",
                    recipientName = "Asha Rao",
                    mobile = "9876543210",
                    line1 = "12 MG Road",
                    line2 = (string?)null,
                    landmark = (string?)null,
                    city = "Hyderabad",
                    stateId = await _sellers.StateIdAsync().ConfigureAwait(false),
                    pincode,
                    gstin = (string?)null,
                    type = "Home",
                    isDefaultShipping = true,
                    isDefaultBilling = true,
                },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return new OrderShopper(
            client,
            me.GetProperty("user").GetProperty("id").GetGuid(),
            address.GetProperty("id").GetGuid());
    }

    /// <summary>Puts an offer in the shopper's basket.</summary>
    /// <param name="shopper">The shopper.</param>
    /// <param name="listingId">The offer.</param>
    /// <param name="quantity">How many units.</param>
    public async Task<JsonElement> AddToCartAsync(OrderShopper shopper, Guid listingId, int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        return await Rest.ReadAsync(
            await shopper.Client.PostAsJsonAsync(
                "/api/v1/store/cart/items",
                new { listingId, quantity },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a live percentage coupon and answers its code and id.</summary>
    /// <param name="percent">What it takes off the order.</param>
    /// <param name="maxDiscount">A ceiling on the money, or null for none.</param>
    public async Task<(Guid Id, string Code)> CouponAsync(decimal percent = 10m, decimal? maxDiscount = null)
    {
        var code = $"SAVE{Suffix().ToUpperInvariant()}";

        var promotion = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/promotions",
                new
                {
                    code,
                    name = $"{percent}% off",
                    description = (string?)null,
                    type = "Percentage",
                    appliesTo = "Order",
                    value = percent,
                    scope = (object?)null,
                    conditions = (object?)null,
                    stacking = "Exclusive",
                    priority = 0,
                    startsAt = DateTimeOffset.UtcNow.AddDays(-1),
                    endsAt = (DateTimeOffset?)null,
                    usageLimitTotal = (int?)null,
                    usageLimitPerCustomer = (int?)null,
                    minOrderValue = 0m,
                    maxDiscount,
                },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        var id = promotion.GetProperty("id").GetGuid();

        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/promotions/{id}/activate",
                new { },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return (id, code);
    }

    /// <summary>Applies a coupon code to the shopper's basket and answers the re-priced basket.</summary>
    /// <param name="shopper">The shopper.</param>
    /// <param name="code">The code they typed.</param>
    public async Task<JsonElement> ApplyCouponAsync(OrderShopper shopper, string code)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        return await Rest.ReadAsync(
            await shopper.Client.PostAsJsonAsync(
                "/api/v1/store/cart/coupon",
                new { code },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>How many times a promotion has been redeemed, as the campaign report states it.</summary>
    /// <param name="promotionId">The promotion.</param>
    public async Task<int> RedemptionCountAsync(Guid promotionId)
    {
        var promotion = await Rest.ReadAsync(
            await admin.GetAsync(
                new Uri($"/api/v1/admin/promotions/{promotionId}", UriKind.Relative),
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return promotion.GetProperty("usageCount").GetInt32();
    }

    /// <summary>
    /// Walks a full checkout and places the order: address, delivery, payment method, place-order.
    /// </summary>
    /// <remarks>
    /// Every step is the route the storefront calls, in the order it calls them, because the steps
    /// are not independent — the delivery options cannot be listed before an address is chosen, the
    /// payment method cannot be set before a delivery service is, and <c>place-order</c> refuses a
    /// session missing any of them.
    /// </remarks>
    /// <param name="shopper">The shopper, with a filled basket.</param>
    /// <param name="method">How they are paying: <c>cod</c> or <c>prepaid</c>.</param>
    /// <param name="idempotencyKey">The key, or null for one nothing else is using.</param>
    public async Task<PlacedOrderFacts> PlaceAsync(
        OrderShopper shopper,
        string method = "cod",
        string? idempotencyKey = null)
    {
        var session = await CheckoutAsync(shopper, method).ConfigureAwait(false);
        var sessionId = session.GetProperty("id").GetGuid();
        var cartId = session.GetProperty("cartId").GetGuid();

        var placed = await Rest.ReadAsync(
            await PlaceRawAsync(shopper, sessionId, idempotencyKey).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return new PlacedOrderFacts(
            placed.GetProperty("orderId").GetGuid(),
            placed.GetProperty("orderNumber").GetString()!,
            cartId,
            sessionId,
            placed.GetProperty("status").GetString()!,
            placed.GetProperty("payment"));
    }

    /// <summary>
    /// Walks the checkout up to the point of payment and stops, answering the session.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="PlaceAsync"/> for the tests that need the placement itself to be
    /// the thing under test — a refused gateway, a replayed idempotency key, a missing header.
    /// </remarks>
    /// <param name="shopper">The shopper, with a filled basket.</param>
    /// <param name="method">How they are paying: <c>cod</c> or <c>prepaid</c>.</param>
    public async Task<JsonElement> CheckoutAsync(OrderShopper shopper, string method = "cod")
    {
        ArgumentNullException.ThrowIfNull(shopper);

        if (string.Equals(method, "cod", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureCashOnDeliveryAsync().ConfigureAwait(false);
        }

        var session = await Rest.ReadAsync(
            await shopper.Client.PostAsJsonAsync("/api/v1/store/checkout", new { }, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        var sessionId = session.GetProperty("id").GetGuid();

        await Rest.ReadAsync(
            await shopper.Client.PutAsJsonAsync(
                $"/api/v1/store/checkout/{sessionId}/address",
                new { shippingAddressId = shopper.AddressId, billingAddressId = (Guid?)null, gstin = (string?)null },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        var options = await ShippingOptionsAsync(shopper, sessionId).ConfigureAwait(false);

        await Rest.ReadAsync(
            await shopper.Client.PutAsJsonAsync(
                $"/api/v1/store/checkout/{sessionId}/shipping",
                new
                {
                    perVendor = options.EnumerateArray().Select(seller => new
                    {
                        vendorId = seller.GetProperty("vendorId").GetGuid(),
                        optionCode = seller.GetProperty("options").EnumerateArray().First()
                            .GetProperty("code").GetString(),
                    }).ToArray(),
                },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        await Rest.ReadAsync(
            await shopper.Client.PutAsJsonAsync(
                $"/api/v1/store/checkout/{sessionId}/payment-method",
                new { method },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return await Rest.ReadAsync(
            await shopper.Client.GetAsync(
                new Uri($"/api/v1/store/checkout/{sessionId}/review", UriKind.Relative),
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Posts <c>place-order</c> and hands back whatever it answered, success or refusal.</summary>
    /// <param name="shopper">The shopper.</param>
    /// <param name="sessionId">The checkout session.</param>
    /// <param name="idempotencyKey">The key, or null for one nothing else is using.</param>
    public Task<HttpResponseMessage> PlaceRawAsync(
        OrderShopper shopper,
        Guid sessionId,
        string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        return Rest.PostRawAsync(
            shopper.Client,
            $"/api/v1/store/checkout/{sessionId}/place-order",
            "{}",
            cancellationToken,
            ("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N")));
    }

    /// <summary>
    /// The delivery services on offer for every seller in the basket, provisioning a rate card the
    /// first time the store turns out not to have one.
    /// </summary>
    /// <remarks>
    /// Shipping's rated quoter offers nothing where the rate card does not cover the destination,
    /// and a store with no card at all therefore cannot check out. Rather than assume a seeded card,
    /// this asks and provisions once — which also keeps these tests independent of whatever Step 16's
    /// own tests leave behind.
    /// </remarks>
    /// <param name="shopper">The shopper.</param>
    /// <param name="sessionId">The checkout session.</param>
    public async Task<JsonElement> ShippingOptionsAsync(OrderShopper shopper, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        var options = await ReadShippingOptionsAsync(shopper, sessionId).ConfigureAwait(false);

        if (HasEveryOption(options))
        {
            return options;
        }

        await ProvisionDeliveryAsync().ConfigureAwait(false);

        options = await ReadShippingOptionsAsync(shopper, sessionId).ConfigureAwait(false);

        Assert.True(
            HasEveryOption(options),
            "No delivery service was offered for at least one seller even after a rate card was provisioned.");

        return options;
    }

    /// <summary>Moves a sub-order as platform staff, and asserts it landed where it was sent.</summary>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="status">Where to move it.</param>
    /// <param name="reason">Why, for the transitions that carry one.</param>
    public async Task TransitionAsync(Guid subOrderId, string status, string? reason = null)
    {
        var moved = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/transition",
                new { status, reason },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        Assert.Equal(status, moved.GetProperty("status").GetString());
    }

    /// <summary>Cancels a sub-order as platform staff, in whole or in part.</summary>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="reason">Why.</param>
    /// <param name="lines">Which units, or null for all of them.</param>
    public async Task<JsonElement> CancelAsync(Guid subOrderId, string reason, object? lines = null)
        => await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/cancel",
                new { reason, lines },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    /// <summary>Reads one order in full, as platform staff.</summary>
    /// <param name="orderId">The order.</param>
    public async Task<JsonElement> ReadOrderAsync(Guid orderId)
        => await Rest.ReadAsync(
            await admin.GetAsync(
                new Uri($"/api/v1/admin/orders/{orderId}", UriKind.Relative),
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    /// <summary>The order's derived status.</summary>
    /// <param name="orderId">The order.</param>
    public async Task<string?> OrderStatusAsync(Guid orderId)
        => (await ReadOrderAsync(orderId).ConfigureAwait(false)).GetProperty("status").GetString();

    /// <summary>One seller's part of an order, as the admin surface projects it.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="vendorId">The seller.</param>
    public async Task<JsonElement> SubOrderOfAsync(Guid orderId, Guid vendorId)
    {
        var order = await ReadOrderAsync(orderId).ConfigureAwait(false);

        return Assert.Single(
            order.GetProperty("subOrders").EnumerateArray(),
            candidate => candidate.GetProperty("vendorId").GetGuid() == vendorId);
    }

    /// <summary>The id of one seller's part of an order.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="vendorId">The seller.</param>
    public async Task<Guid> SubOrderIdAsync(Guid orderId, Guid vendorId)
        => (await SubOrderOfAsync(orderId, vendorId).ConfigureAwait(false)).GetProperty("id").GetGuid();

    /// <summary>Where one seller's part stands.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="vendorId">The seller.</param>
    public async Task<string?> SubOrderStatusAsync(Guid orderId, Guid vendorId)
        => (await SubOrderOfAsync(orderId, vendorId).ConfigureAwait(false)).GetProperty("status").GetString();

    /// <summary>Walks a sub-order along the fulfilment chain, one legal edge at a time.</summary>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="statuses">The states to move through, in order.</param>
    public async Task DriveAsync(Guid subOrderId, params string[] statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        foreach (var status in statuses)
        {
            await TransitionAsync(subOrderId, status).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Makes sure the store accepts cash on delivery for a basket of any size these tests build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cash-on-delivery ceiling is a <em>store setting</em> — a row in a database the whole
    /// collection shares — and it is not left where the seed put it: another test in this suite
    /// lowers it to ₹2,500 and does not restore it. A basket that is fine on its own therefore
    /// fails once that test has run, in whatever order the runner happens to pick, and the failure
    /// arrives at the payment-method step with nothing about it that points at an order.
    /// </para>
    /// <para>
    /// So the value is pinned rather than assumed. It is raised and left raised: the test that
    /// lowers it writes its own value immediately before asserting it, so nothing is broken by
    /// finding the ceiling high.
    /// </para>
    /// </remarks>
    public async Task EnsureCashOnDeliveryAsync()
    {
        if (_cashOnDelivery)
        {
            return;
        }

        _cashOnDelivery = true;

        var settings = await Rest.ReadAsync(
            await admin.GetAsync(new Uri("/api/v1/admin/settings", UriKind.Relative), cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        var commerce = settings.GetProperty("sections").EnumerateArray()
            .First(section => section.GetProperty("key").GetString() == "commerce")
            .GetProperty("value");

        if (commerce.GetProperty("codEnabled").GetBoolean()
            && commerce.GetProperty("codOrderValueLimit").GetDecimal() >= CodCeiling)
        {
            return;
        }

        var value = System.Text.Json.Nodes.JsonNode.Parse(commerce.GetRawText())!.AsObject();

        value["codEnabled"] = true;
        value["codOrderValueLimit"] = CodCeiling;

        await Rest.ReadAsync(
            await admin.PutAsJsonAsync("/api/v1/admin/settings/commerce", value, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A short suffix nothing else in the collection is using.</summary>
    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>A structurally valid PAN nothing else is using: five letters, four digits, a letter.</summary>
    private static string NewPan()
    {
        const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        var pan = new char[10];

        for (var index = 0; index < 5; index++)
        {
            pan[index] = Letters[Random.Shared.Next(Letters.Length)];
        }

        for (var index = 5; index < 9; index++)
        {
            pan[index] = (char)('0' + Random.Shared.Next(10));
        }

        pan[9] = Letters[Random.Shared.Next(Letters.Length)];

        return new string(pan);
    }

    /// <summary>Whether every seller in the basket has at least one service to choose.</summary>
    private static bool HasEveryOption(JsonElement options)
        => options.ValueKind == JsonValueKind.Array
           && options.GetArrayLength() > 0
           && options.EnumerateArray().All(seller => seller.GetProperty("options").GetArrayLength() > 0);

    private async Task<JsonElement> ReadShippingOptionsAsync(OrderShopper shopper, Guid sessionId)
        => await Rest.ReadAsync(
            await shopper.Client.GetAsync(
                new Uri($"/api/v1/store/checkout/{sessionId}/shipping-options", UriKind.Relative),
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    /// <summary>Opens a zone covering the Hyderabad PIN codes and a standard rate to price it.</summary>
    private async Task ProvisionDeliveryAsync()
    {
        var zone = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/shipping/zones",
                new
                {
                    code = $"Z{Suffix().ToUpperInvariant()}",
                    name = "Hyderabad",
                    priority = 0,
                    states = new[] { await _sellers.StateIdAsync().ConfigureAwait(false) },
                    pincodeRanges = new[] { new { from = "500000", to = "500999" } },
                    isActive = true,
                },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/shipping/rates",
                new
                {
                    zoneId = zone.GetProperty("id").GetGuid(),
                    method = "standard",
                    vendorId = (Guid?)null,
                    terms = new
                    {
                        minWeightGrams = 0,
                        maxWeightGrams = 30_000,
                        minOrderValue = 0m,
                        maxOrderValue = (decimal?)null,
                        baseRate = 50m,
                        perKgRate = 0m,
                        freeAbove = (decimal?)null,
                        codFee = 0m,
                        isCodAllowed = true,
                        etaMinDays = 2,
                        etaMaxDays = 5,
                    },
                    isActive = true,
                },
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }
}
