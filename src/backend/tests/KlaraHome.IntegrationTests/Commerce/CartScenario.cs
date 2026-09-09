using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>An offer a shopper can actually buy, and the ids a basket test needs to act on it.</summary>
/// <param name="ListingId">The offer, which is what a cart line names.</param>
/// <param name="VendorId">The seller who will dispatch it.</param>
/// <param name="ProductId">The product it belongs to.</param>
/// <param name="VariantId">The variant it is an offer against.</param>
/// <param name="Sku">The stock-keeping unit, as the cart renders it.</param>
/// <param name="SellingPrice">What the seller is asking, before tax and promotions.</param>
/// <param name="WarehouseId">Where its stock is.</param>
/// <param name="StockItemId">The stock row, so a test can move the units under a basket's feet.</param>
internal sealed record SellableOffer(
    Guid ListingId,
    Guid VendorId,
    Guid ProductId,
    Guid VariantId,
    string Sku,
    decimal SellingPrice,
    Guid WarehouseId,
    Guid StockItemId);

/// <summary>An address on a shopper's account, and the two facts a checkout keys off it.</summary>
/// <param name="Id">The address.</param>
/// <param name="Pincode">Where it is.</param>
/// <param name="StateId">Its <c>platform.states</c> row, which decides the tax split.</param>
internal sealed record ShopperAddress(Guid Id, string Pincode, Guid StateId);

/// <summary>
/// Builds the basket a Step 13 test stands on, through the API a shopper and a merchandiser
/// actually use.
/// </summary>
/// <remarks>
/// <para>
/// The same reasoning as <see cref="VendorScenario"/> and <see cref="CatalogScenario"/>, and it
/// matters more here than anywhere: a cart line written as a row is a line whose offer was never
/// checked, whose seller may not be trading and whose stock nobody opened — and every one of those
/// is a thing this module's validation exists to notice. A test standing on hand-written rows would
/// be standing on a basket the product could never have produced, and would pass while the
/// behaviour it names was broken.
/// </para>
/// <para>
/// The offers it builds are <b>platform-owned products with a seller's offer against them</b>,
/// which is the shape a multi-vendor catalogue actually has (docs/03-database-design.md §4.4) and
/// the only one in which two sellers can appear in one basket without two of everything.
/// </para>
/// <para>
/// Every offer it builds is <b>stocked</b>. An untracked offer is not "zero available", it is an
/// offer Inventory has never heard of, and <c>StockAvailability.CanFulfil</c> refuses it — so an
/// unstocked basket blocks checkout for a reason that has nothing to do with what a test is
/// proving.
/// </para>
/// </remarks>
/// <param name="admin">A client signed in as platform staff.</param>
/// <param name="cancellationToken">Cancellation token.</param>
internal sealed class CartScenario(HttpClient admin, CancellationToken cancellationToken)
{
    private readonly VendorScenario _sellers = new(admin, cancellationToken);
    private readonly CatalogScenario _catalogue = new(admin, cancellationToken);
    private readonly Dictionary<Guid, Guid> _warehouses = [];
    private CatalogTaxonomy? _taxonomy;

    /// <summary>The seller builder, for a test that needs to suspend one or change its coverage.</summary>
    public VendorScenario Sellers => _sellers;

    /// <summary>The catalogue builder, for a test that needs a second variant or a withdrawn offer.</summary>
    public CatalogScenario Catalogue => _catalogue;

    /// <summary>The client this scenario drives, for a test that wants to carry on from here.</summary>
    public HttpClient Admin => admin;

    /// <summary>
    /// The category, brand and attribute vocabulary every offer in this scenario is described with.
    /// </summary>
    /// <remarks>
    /// Built once and remembered. Two sellers in one basket are two offers, not two catalogues, and
    /// rebuilding the taxonomy per offer would pay for four extra round trips a test never asserts on.
    /// </remarks>
    public async Task<CatalogTaxonomy> TaxonomyAsync()
        => _taxonomy ??= await _catalogue.TaxonomyAsync().ConfigureAwait(false);

    /// <summary>A seller taken all the way to <c>Active</c>.</summary>
    public Task<OnboardedVendor> SellerAsync() => _sellers.ActiveAsync();

    /// <summary>
    /// A published product with one seller's offer against it, stocked and ready to be bought.
    /// </summary>
    /// <param name="seller">The seller making the offer.</param>
    /// <param name="sellingPrice">What they are asking.</param>
    /// <param name="stock">How many units to put on the shelf.</param>
    /// <param name="isCodAllowed">Whether the seller will take cash at the door for it.</param>
    /// <param name="maxOrderQuantity">The seller's own per-customer cap, or null for none.</param>
    public async Task<SellableOffer> OfferAsync(
        OnboardedVendor seller,
        decimal sellingPrice = 499m,
        int stock = 25,
        bool isCodAllowed = true,
        int? maxOrderQuantity = null)
    {
        ArgumentNullException.ThrowIfNull(seller);

        var taxonomy = await TaxonomyAsync().ConfigureAwait(false);
        var product = await _catalogue.DraftAsync(taxonomy).ConfigureAwait(false);

        await Read(await _catalogue.ActivateVariantAsync(product.VariantId)).ConfigureAwait(false);
        await _catalogue.PublishAsync(product.Id).ConfigureAwait(false);

        var listingId = await OfferAgainstAsync(
                seller,
                product.VariantId,
                sellingPrice,
                isCodAllowed,
                maxOrderQuantity)
            .ConfigureAwait(false);

        var stockItemId = await StockAsync(seller, listingId, stock).ConfigureAwait(false);

        return new SellableOffer(
            listingId,
            seller.Id,
            product.Id,
            product.VariantId,
            product.Sku,
            sellingPrice,
            _warehouses[seller.Id],
            stockItemId);
    }

    /// <summary>Opens one more seller's offer against a variant that is already published.</summary>
    /// <remarks>
    /// The other half of a shared catalogue: two sellers offering the same variant is what the buy
    /// box exists for, and what makes "the same item from two sellers" expressible in a basket.
    /// </remarks>
    /// <param name="seller">The seller making the offer.</param>
    /// <param name="variantId">The variant being offered.</param>
    /// <param name="sellingPrice">What they are asking.</param>
    /// <param name="isCodAllowed">Whether they will take cash at the door for it.</param>
    /// <param name="maxOrderQuantity">Their own per-customer cap, or null for none.</param>
    public async Task<Guid> OfferAgainstAsync(
        OnboardedVendor seller,
        Guid variantId,
        decimal sellingPrice,
        bool isCodAllowed = true,
        int? maxOrderQuantity = null)
    {
        ArgumentNullException.ThrowIfNull(seller);

        var created = await Read(await admin.PostAsJsonAsync(
                "/api/v1/admin/listings",
                new
                {
                    variantId,
                    vendorId = seller.Id,
                    mrp = (decimal?)null,
                    sellingPrice,
                    vendorSku = (string?)null,
                    handlingTimeHours = 24,
                    isCodAllowed,
                    maxOrderQuantity,
                },
                cancellationToken))
            .ConfigureAwait(false);

        var listingId = created.GetProperty("id").GetGuid();

        await Read(await admin.PostAsJsonAsync(
                $"/api/v1/admin/listings/{listingId}/activate",
                new { reason = (string?)null },
                cancellationToken))
            .ConfigureAwait(false);

        return listingId;
    }

    /// <summary>Changes what a seller is asking for an offer, so a price move can be disclosed.</summary>
    /// <param name="offer">The offer.</param>
    /// <param name="sellingPrice">The new price.</param>
    /// <param name="isCodAllowed">Whether cash may still be taken at the door for it.</param>
    public async Task RepriceAsync(SellableOffer offer, decimal sellingPrice, bool isCodAllowed = true)
    {
        ArgumentNullException.ThrowIfNull(offer);

        await Read(await admin.PutAsJsonAsync(
                $"/api/v1/admin/listings/{offer.ListingId}",
                new
                {
                    mrp = 1299m,
                    sellingPrice,
                    vendorSku = (string?)null,
                    handlingTimeHours = 24,
                    isCodAllowed,
                    maxOrderQuantity = (int?)null,
                },
                cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>Takes an offer off the storefront, so a basket holding it has to say so.</summary>
    /// <param name="offer">The offer.</param>
    public async Task WithdrawAsync(SellableOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);

        await Read(await admin.PostAsJsonAsync(
                $"/api/v1/admin/listings/{offer.ListingId}/deactivate",
                new { reason = "Withdrawn by an integration test." },
                cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>Moves the units under a basket's feet, which is how a sell-out is staged.</summary>
    /// <param name="offer">The offer whose stock is moving.</param>
    /// <param name="change">Signed units.</param>
    public async Task AdjustStockAsync(SellableOffer offer, int change)
    {
        ArgumentNullException.ThrowIfNull(offer);

        await Read(await admin.PostAsJsonAsync(
                "/api/v1/admin/stock/adjustments",
                new
                {
                    stockItemId = offer.StockItemId,
                    change,
                    reason = "Correction",
                    note = "Staged by an integration test.",
                },
                cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>Replaces where a seller is willing to deliver.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="servesAllIndia">Whether they deliver everywhere, in which case the rules are ignored.</param>
    /// <param name="regions">The rules, when they do not.</param>
    public async Task<JsonElement> ServiceableRegionsAsync(
        Guid vendorId,
        bool servesAllIndia,
        params object[] regions)
        => await Read(await admin.PutAsJsonAsync(
                $"/api/v1/admin/vendors/{vendorId}/serviceable-regions",
                new { servesAllIndia, regions },
                cancellationToken))
            .ConfigureAwait(false);

    /// <summary>One serviceability rule, as the API accepts it.</summary>
    /// <param name="stateId">The state it admits or excludes.</param>
    /// <param name="isExcluded">Whether it takes the state away rather than adding it.</param>
    public static object StateRule(Guid stateId, bool isExcluded = false)
        => new { scope = "State", stateId, pincodePrefix = (string?)null, isExcluded };

    /// <summary>One serviceability rule keyed on a PIN code prefix.</summary>
    /// <param name="prefix">The prefix. Two digits is a postal circle, six one delivery office.</param>
    /// <param name="isExcluded">Whether it takes the prefix away rather than adding it.</param>
    public static object PincodeRule(string prefix, bool isExcluded = false)
        => new { scope = "PincodePrefix", stateId = (Guid?)null, pincodePrefix = prefix, isExcluded };

    /// <summary>Saves an address on a shopper's own account, through their own client.</summary>
    /// <remarks>
    /// Through <c>/store/me/addresses</c> rather than written as a row, because the checkout resolves
    /// an address by <em>(customer, address)</em> and an id that was never on the account is exactly
    /// the case a security test needs to be able to produce.
    /// </remarks>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="pincode">Where it is. Hyderabad by default, which the seeded coverage admits.</param>
    /// <param name="stateId">Its state.</param>
    /// <param name="line1">The house or flat, so two addresses in one PIN code are distinguishable.</param>
    /// <param name="gstin">A GST registration, for a B2B purchase.</param>
    public async Task<ShopperAddress> AddressAsync(
        HttpClient shopper,
        string pincode = "500034",
        Guid? stateId = null,
        string line1 = "Flat 3, Sapphire Residency",
        string? gstin = null)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        var state = stateId ?? await _sellers.StateIdAsync().ConfigureAwait(false);

        var created = await Read(await shopper.PostAsJsonAsync(
                "/api/v1/store/me/addresses",
                new
                {
                    label = "Home",
                    recipientName = "Test Shopper",
                    mobile = "9876500002",
                    line1,
                    line2 = "Road No 12",
                    landmark = (string?)null,
                    city = "Hyderabad",
                    stateId = state,
                    pincode,
                    gstin,
                    type = "Home",
                    isDefaultShipping = true,
                    isDefaultBilling = true,
                },
                cancellationToken))
            .ConfigureAwait(false);

        return new ShopperAddress(created.GetProperty("id").GetGuid(), pincode, state);
    }

    /// <summary>Puts units of an offer into a caller's basket, and answers the basket.</summary>
    /// <param name="client">The shopper, or an anonymous browser.</param>
    /// <param name="offer">The offer.</param>
    /// <param name="quantity">How many units.</param>
    public async Task<JsonElement> AddAsync(HttpClient client, SellableOffer offer, int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(offer);

        return await Read(await client.PostAsJsonAsync(
                "/api/v1/store/cart/items",
                new { listingId = offer.ListingId, quantity },
                cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>The same, without asserting it succeeded — for the refusals.</summary>
    /// <param name="client">The shopper, or an anonymous browser.</param>
    /// <param name="listingId">The offer.</param>
    /// <param name="quantity">How many units.</param>
    public Task<HttpResponseMessage> TryAddAsync(HttpClient client, Guid listingId, int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client.PostAsJsonAsync(
            "/api/v1/store/cart/items",
            new { listingId, quantity },
            cancellationToken);
    }

    /// <summary>Reads a caller's own basket.</summary>
    /// <param name="client">The shopper, or an anonymous browser.</param>
    public async Task<JsonElement> CartAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await Read(await client.GetAsync(
                new Uri("/api/v1/store/cart", UriKind.Relative),
                cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>Opens a checkout against the caller's basket.</summary>
    /// <param name="shopper">A client signed in as the shopper.</param>
    public Task<HttpResponseMessage> StartCheckoutAsync(HttpClient shopper)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        return shopper.PostAsJsonAsync("/api/v1/store/checkout", new { }, cancellationToken);
    }

    /// <summary>Chooses where the order goes.</summary>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="addressId">One of their own addresses.</param>
    /// <param name="gstin">The GSTIN to raise the invoice against.</param>
    public Task<HttpResponseMessage> SetAddressAsync(
        HttpClient shopper,
        Guid sessionId,
        Guid addressId,
        string? gstin = null)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        return shopper.PutAsJsonAsync(
            $"/api/v1/store/checkout/{sessionId}/address",
            new { shippingAddressId = addressId, billingAddressId = (Guid?)null, gstin },
            cancellationToken);
    }

    /// <summary>Reads what each seller's parcel may be sent by.</summary>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="sessionId">The session.</param>
    public async Task<JsonElement> ShippingOptionsAsync(HttpClient shopper, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        return await Read(await shopper.GetAsync(
                new Uri($"/api/v1/store/checkout/{sessionId}/shipping-options", UriKind.Relative),
                cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>Chooses a delivery service for every seller named.</summary>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="choices">One (seller, service code) pair per seller in the basket.</param>
    public Task<HttpResponseMessage> SetShippingAsync(
        HttpClient shopper,
        Guid sessionId,
        params (Guid VendorId, string OptionCode)[] choices)
    {
        ArgumentNullException.ThrowIfNull(shopper);
        ArgumentNullException.ThrowIfNull(choices);

        return shopper.PutAsJsonAsync(
            $"/api/v1/store/checkout/{sessionId}/shipping",
            new
            {
                perVendor = choices
                    .Select(choice => new { vendorId = choice.VendorId, optionCode = choice.OptionCode })
                    .ToArray(),
            },
            cancellationToken);
    }

    /// <summary>Chooses every seller's first offered service, which is what a shopper mostly does.</summary>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="sessionId">The session.</param>
    public async Task<HttpResponseMessage> SetCheapestShippingAsync(HttpClient shopper, Guid sessionId)
    {
        var offered = await ShippingOptionsAsync(shopper, sessionId).ConfigureAwait(false);

        var choices = offered.EnumerateArray()
            .Select(seller => (
                VendorId: seller.GetProperty("vendorId").GetGuid(),
                OptionCode: seller.GetProperty("options").EnumerateArray().First().GetProperty("code").GetString()!))
            .ToArray();

        return await SetShippingAsync(shopper, sessionId, choices).ConfigureAwait(false);
    }

    /// <summary>Reads what this basket may be paid by.</summary>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="sessionId">The session.</param>
    public async Task<JsonElement> PaymentMethodsAsync(HttpClient shopper, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        return await Read(await shopper.GetAsync(
                new Uri($"/api/v1/store/checkout/{sessionId}/payment-methods", UriKind.Relative),
                cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>Chooses how the order is paid for.</summary>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="method">Either <c>prepaid</c> or <c>cod</c>.</param>
    public Task<HttpResponseMessage> SetPaymentMethodAsync(HttpClient shopper, Guid sessionId, string method)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        return shopper.PutAsJsonAsync(
            $"/api/v1/store/checkout/{sessionId}/payment-method",
            new { method },
            cancellationToken);
    }

    /// <summary>Re-validates and re-prices a session, as the storefront does before the pay button.</summary>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="sessionId">The session.</param>
    public Task<HttpResponseMessage> ReviewAsync(HttpClient shopper, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        return shopper.GetAsync(
            new Uri($"/api/v1/store/checkout/{sessionId}/review", UriKind.Relative),
            cancellationToken);
    }

    /// <summary>Sends a place-order request carrying an idempotency key.</summary>
    /// <remarks>
    /// The key travels in the header rather than the body because that is where the specification
    /// puts it, and because a route that took it in the body would let a retry change it by accident.
    /// </remarks>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="idempotencyKey">The key, or null to send none at all.</param>
    public Task<HttpResponseMessage> PlaceOrderAsync(
        HttpClient shopper,
        Guid sessionId,
        string? idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/api/v1/store/checkout/{sessionId}/place-order", UriKind.Relative));

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return shopper.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// A basket taken as far as the payment screen: address chosen, delivery chosen, method chosen.
    /// </summary>
    /// <remarks>
    /// Every step through its own endpoint, because the sequence itself is what the checkout
    /// deliverable names and because a session assembled any other way would be one the product
    /// could never have produced.
    /// </remarks>
    /// <param name="shopper">A client signed in as the shopper.</param>
    /// <param name="address">Where it goes.</param>
    /// <param name="method">Either <c>prepaid</c> or <c>cod</c>.</param>
    public async Task<Guid> ReadyCheckoutAsync(
        HttpClient shopper,
        ShopperAddress address,
        string method = "cod")
    {
        ArgumentNullException.ThrowIfNull(address);

        var session = await Read(await StartCheckoutAsync(shopper)).ConfigureAwait(false);
        var sessionId = session.GetProperty("id").GetGuid();

        await Read(await SetAddressAsync(shopper, sessionId, address.Id)).ConfigureAwait(false);
        await Read(await SetCheapestShippingAsync(shopper, sessionId)).ConfigureAwait(false);
        await Read(await SetPaymentMethodAsync(shopper, sessionId, method)).ConfigureAwait(false);

        return sessionId;
    }

    /// <summary>A key nothing else in the collection is using.</summary>
    /// <param name="hint">A readable hint about which test minted it.</param>
    public static string NewIdempotencyKey(string hint)
        => string.Create(CultureInfo.InvariantCulture, $"{hint}-{Guid.NewGuid():N}");

    /// <summary>Opens a stock row for an offer at the seller's own warehouse and fills it.</summary>
    private async Task<Guid> StockAsync(OnboardedVendor seller, Guid listingId, int quantity)
    {
        var warehouseId = await WarehouseAsync(seller).ConfigureAwait(false);

        var item = await Read(await admin.PostAsJsonAsync(
                "/api/v1/admin/stock",
                new { listingId, warehouseId },
                cancellationToken))
            .ConfigureAwait(false);

        var stockItemId = item.GetProperty("id").GetGuid();

        if (quantity > 0)
        {
            await Read(await admin.PostAsJsonAsync(
                    "/api/v1/admin/stock/adjustments",
                    new
                    {
                        stockItemId,
                        change = quantity,
                        reason = "Correction",
                        note = "Opening stock for an integration test.",
                    },
                    cancellationToken))
                .ConfigureAwait(false);
        }

        return stockItemId;
    }

    /// <summary>The seller's own stock location, created once and remembered.</summary>
    private async Task<Guid> WarehouseAsync(OnboardedVendor seller)
    {
        if (_warehouses.TryGetValue(seller.Id, out var known))
        {
            return known;
        }

        var created = await Read(await admin.PostAsJsonAsync(
                "/api/v1/admin/warehouses",
                new
                {
                    vendorId = seller.Id,
                    code = $"wh-{Guid.NewGuid():N}"[..16],
                    name = "Test warehouse",
                    pincode = "500034",
                    address = new
                    {
                        line1 = "Plot 42",
                        line2 = (string?)null,
                        landmark = (string?)null,
                        city = "Hyderabad",
                        stateId = await _sellers.StateIdAsync().ConfigureAwait(false),
                        contactName = "Warehouse Manager",
                        contactPhone = "9876500001",
                    },
                    priority = 0,
                },
                cancellationToken))
            .ConfigureAwait(false);

        var warehouseId = created.GetProperty("id").GetGuid();
        _warehouses[seller.Id] = warehouseId;

        return warehouseId;
    }

    private Task<JsonElement> Read(HttpResponseMessage response) => Rest.ReadAsync(response, cancellationToken);
}
