using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>An offer a pricing test can put in a basket, and the facts its price turns on.</summary>
/// <param name="ListingId">The offer, which is what a quote line names.</param>
/// <param name="VendorId">The seller, whose GSTIN decides the place of supply on this line.</param>
/// <param name="VariantId">The sellable thing behind it.</param>
/// <param name="ProductId">Its product.</param>
/// <param name="HsnCode">The HSN the GST rate is resolved from.</param>
/// <param name="SellingPrice">What the offer itself asks, which is the fallback when no list prices it.</param>
internal sealed record PricedOffer(
    Guid ListingId,
    Guid VendorId,
    Guid VariantId,
    Guid ProductId,
    string HsnCode,
    decimal SellingPrice);

/// <summary>
/// Builds the priceable world a Step 12 test stands on, through the API that would have built it.
/// </summary>
/// <remarks>
/// <para>
/// The same reasoning as <see cref="VendorScenario"/> and <see cref="CatalogScenario"/>. A price is
/// the product of a listing, a seller's GST registration, a price list, a tax rate and a promotion,
/// and four of those five live in other modules. Rows written by hand would be a basket the product
/// could never have produced — and worse here than anywhere else, because the thing under test is
/// arithmetic that looks right whatever it was given.
/// </para>
/// <para>
/// It composes the two existing builders rather than replacing them: sellers come from
/// <see cref="VendorScenario"/>, taxonomy and offers from <see cref="CatalogScenario"/>. What it
/// adds is the two things neither of them can express — a seller registered for GST in a
/// <em>chosen</em> state, and a product carrying a <em>chosen</em> HSN code and rate — because the
/// intra-state/inter-state split and the rate table are exactly what a pricing test is about.
/// </para>
/// </remarks>
/// <param name="admin">A client signed in as platform staff.</param>
/// <param name="cancellationToken">Cancellation token.</param>
internal sealed class PricingScenario(HttpClient admin, CancellationToken cancellationToken)
{
    /// <summary>Telangana. The seeded default coverage, so a seller here needs no special handling.</summary>
    public const string TelanganaGstCode = "36";

    /// <summary>Maharashtra. Somewhere else, which is the whole point of having a second one.</summary>
    public const string MaharashtraGstCode = "27";

    private readonly Dictionary<string, Guid> _statesByCode = new(StringComparer.Ordinal);

    /// <summary>The client this scenario drives, for a test that wants to carry on from here.</summary>
    public HttpClient Admin => admin;

    /// <summary>The <c>platform.states</c> row carrying a given two-digit GST code.</summary>
    /// <remarks>
    /// By code rather than by name, because the code is the thing under test: the place of supply is
    /// decided by comparing it against the first two characters of the seller's GSTIN, and a test
    /// that looked a state up by name would be asserting the seed's spelling instead.
    /// </remarks>
    /// <param name="gstCode">The two-digit GST state code, for example <c>36</c>.</param>
    public async Task<Guid> StateAsync(string gstCode)
    {
        if (_statesByCode.TryGetValue(gstCode, out var known))
        {
            return known;
        }

        var states = await Rest.ReadAsync(
            await admin.GetAsync(new Uri("/api/v1/store/states", UriKind.Relative), cancellationToken),
            cancellationToken);

        var all = states.ValueKind == JsonValueKind.Array ? states : states.GetProperty("items");

        foreach (var state in all.EnumerateArray())
        {
            _statesByCode[state.GetProperty("code").GetString()!] = state.GetProperty("id").GetGuid();
        }

        Assert.True(_statesByCode.ContainsKey(gstCode), $"The seed has no state with GST code {gstCode}.");

        return _statesByCode[gstCode];
    }

    /// <summary>
    /// A seller taken all the way to <c>Active</c>, registered for GST in a chosen state.
    /// </summary>
    /// <remarks>
    /// <see cref="VendorScenario.ActiveAsync"/> deliberately makes a seller below the GST threshold,
    /// which is the shortest legal form onboarding accepts — and a seller with no GSTIN has no state
    /// to supply from, so every line they sell would fall through to the "unknown, assume
    /// intra-state" branch. Half of this module's tax criteria need the other case. The GSTIN is
    /// built around the seller's own PAN because characters three to twelve of a GSTIN <em>are</em>
    /// the PAN and onboarding checks that.
    /// </remarks>
    /// <param name="sellers">The seller builder, for the onboarding steps that are unchanged.</param>
    /// <param name="gstStateCode">The two-digit state code the seller is registered in.</param>
    public async Task<Guid> SellerRegisteredInAsync(VendorScenario sellers, string gstStateCode)
    {
        ArgumentNullException.ThrowIfNull(sellers);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var pan = NewPan();

        var vendor = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/vendors",
                new
                {
                    legalName = $"GST Seller {suffix}",
                    displayName = $"Seller {suffix}",
                    businessType = "SoleProprietorship",
                    slug = $"gst-seller-{suffix}",
                    pan,
                    // State code, the PAN, an entity number, the literal Z and a checksum character.
                    gstin = $"{gstStateCode}{pan}1Z5",
                    supportEmail = $"support-{suffix}@klarahome.test",
                    supportPhone = "9876500000",
                },
                cancellationToken),
            cancellationToken);

        var vendorId = vendor.GetProperty("id").GetGuid();

        await Rest.ReadAsync(
            await admin.PutAsJsonAsync(
                $"/api/v1/admin/vendors/{vendorId}/commission-plan",
                new { planId = await sellers.CommissionPlanAsync() },
                cancellationToken),
            cancellationToken);

        await sellers.KycDocumentAsync(vendorId, "Pan");
        await sellers.KycDocumentAsync(vendorId, "IdentityProof");

        // Declaring a GSTIN adds a document to what onboarding requires, which is why
        // VendorScenario.ActiveAsync does not declare one: a seller who claims a registration has
        // to produce the certificate before the platform will let them invoice under it.
        await sellers.KycDocumentAsync(vendorId, "Gstin");

        await sellers.BankAccountAsync(vendorId);
        await sellers.PickupLocationAsync(vendorId);

        await Rest.ReadAsync(await sellers.TransitionAsync(vendorId, "submit"), cancellationToken);
        await Rest.ReadAsync(await sellers.TransitionAsync(vendorId, "approve"), cancellationToken);

        var activated = await Rest.ReadAsync(
            await sellers.TransitionAsync(vendorId, "activate"),
            cancellationToken);

        Assert.Equal("Active", activated.GetProperty("status").GetString());

        return vendorId;
    }

    /// <summary>
    /// Registers a shopper on the given client, signs them in, and answers their customer id.
    /// </summary>
    /// <remarks>
    /// Through <c>/store/auth/register</c> and then the ordinary password sign-in, which is the only
    /// route to a storefront customer that works in a shipped deployment: mobile OTP sign-in is
    /// behind <c>identity.mobile-otp-login</c>, and that flag ships <b>off</b> because the platform
    /// has no SMS provider. A pricing test needs a real customer id — per-customer usage limits and
    /// store credit are both keyed on it — and it must be one the product could actually produce.
    /// </remarks>
    /// <param name="client">The client to register on and attach the session to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Guid> ShopperAsync(HttpClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var email = $"shopper-{Guid.NewGuid():N}@klarahome.test";
        const string Password = "the-shopper-signs-in-here";

        await Rest.ReadAsync(
            await client.PostAsJsonAsync(
                "/api/v1/store/auth/register",
                new { email, password = Password, mobile = (string?)null, marketingConsent = false },
                cancellationToken),
            cancellationToken);

        // Sign in again rather than reading the registration's own token, so the session is attached
        // to the client by the same helper every other test uses.
        var session = await Database.TestSignIn.SignInAsync(client, "store", email, Password, cancellationToken);

        return session.UserId;
    }

    /// <summary>
    /// Publishes a platform-owned product carrying a chosen HSN code and rate, and opens one offer
    /// against it.
    /// </summary>
    /// <remarks>
    /// The HSN is the join between the catalogue and the rate table, and the product's own
    /// <c>gstRate</c> is what the resolver falls back to when the table has nothing in force. A
    /// scenario that could not vary both could not tell a resolved rate from a fallback one.
    /// </remarks>
    /// <param name="catalogue">The taxonomy builder, for the category and brand.</param>
    /// <param name="taxonomy">The vocabulary to file it under.</param>
    /// <param name="vendorId">The seller who offers it.</param>
    /// <param name="hsnCode">The HSN code. Four to eight digits.</param>
    /// <param name="gstRate">The rate on the product itself, used when the table has no row.</param>
    /// <param name="sellingPrice">What the offer asks, inclusive of GST.</param>
    /// <param name="mrp">The statutory maximum, which the selling price may not exceed.</param>
    public async Task<PricedOffer> OfferAsync(
        CatalogScenario catalogue,
        CatalogTaxonomy taxonomy,
        Guid vendorId,
        string hsnCode,
        decimal gstRate,
        decimal sellingPrice,
        decimal mrp = 99_999m)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(taxonomy);

        var suffix = Guid.NewGuid().ToString("N")[..8];

        var product = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/products",
                new
                {
                    name = $"Priced Item {suffix}",
                    slug = (string?)null,
                    categoryId = taxonomy.CategoryId,
                    brandId = taxonomy.BrandId,
                    vendorId = (Guid?)null,
                    shortDescription = "Something with a price on it.",
                    description = "Made to be quoted.",
                    hsnCode,
                    gstRate,
                    countryOfOrigin = "IN",
                    manufacturer = new
                    {
                        name = "Klara Textiles Pvt Ltd",
                        address = "Plot 9, Panipat, Haryana 132103",
                        contact = "care@klarahome.test",
                    },
                    packer = (object?)null,
                    importer = (object?)null,
                    isReturnable = true,
                    returnWindowDays = 7,
                    warranty = (string?)null,
                    specifications = new[] { new { label = "Weave", value = "Panama", group = (string?)null } },
                    seo = (object?)null,
                    attributes = Array.Empty<object>(),
                },
                cancellationToken),
            cancellationToken);

        var productId = product.GetProperty("id").GetGuid();

        var variant = await catalogue.VariantAsync(productId, taxonomy, optionIndex: 0, mrp: mrp);
        var variantId = variant.GetProperty("id").GetGuid();

        await Rest.ReadAsync(await catalogue.ActivateVariantAsync(variantId), cancellationToken);
        await catalogue.PublishAsync(productId);

        var listingId = await catalogue.OfferAsync(vendorId, variantId, sellingPrice, mrp);

        return new PricedOffer(listingId, vendorId, variantId, productId, hsnCode, sellingPrice);
    }

    /// <summary>Opens a price list and answers its id.</summary>
    /// <param name="priority">Resolution order. Lower wins.</param>
    /// <param name="vendorId">The seller it prices for, or null for a platform-wide list.</param>
    /// <param name="startsAt">When it starts applying.</param>
    /// <param name="endsAt">When it stops.</param>
    /// <param name="type">Why it exists: <c>Base</c>, <c>Sale</c> or <c>Scheduled</c>.</param>
    /// <param name="client">The client to create it through. Defaults to the scenario's own.</param>
    public async Task<Guid> PriceListAsync(
        int priority,
        Guid? vendorId = null,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        string type = "Sale",
        HttpClient? client = null)
    {
        var created = await Rest.ReadAsync(
            await (client ?? admin).PostAsJsonAsync(
                "/api/v1/admin/price-lists",
                new
                {
                    vendorId,
                    code = $"PL-{Guid.NewGuid():N}"[..16],
                    name = $"List at priority {priority.ToString(CultureInfo.InvariantCulture)}",
                    type,
                    priority,
                    startsAt,
                    endsAt,
                },
                cancellationToken),
            cancellationToken);

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>Prices offers into a list. One row per offer per quantity tier.</summary>
    /// <param name="priceListId">The list.</param>
    /// <param name="items">The prices, as <c>(offer, price, minQuantity)</c>.</param>
    /// <param name="client">The client to write through. Defaults to the scenario's own.</param>
    public Task<HttpResponseMessage> SetPricesAsync(
        Guid priceListId,
        (Guid ListingId, decimal Price, int MinQuantity)[] items,
        HttpClient? client = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        return (client ?? admin).PutAsJsonAsync(
            $"/api/v1/admin/price-lists/{priceListId}/items",
            new
            {
                items = items.Select(item => new
                {
                    listingId = item.ListingId,
                    price = item.Price,
                    minQuantity = item.MinQuantity,
                }),
            },
            cancellationToken);
    }

    /// <summary>Records a GST rate for an HSN code from a date, and answers its id.</summary>
    /// <param name="hsnCode">The HSN code.</param>
    /// <param name="rate">The GST percentage.</param>
    /// <param name="cessRate">The compensation cess percentage.</param>
    /// <param name="effectiveFrom">The first day it applies.</param>
    /// <param name="effectiveTo">The last day it applies, or null while it is current.</param>
    public async Task<Guid> TaxRateAsync(
        string hsnCode,
        decimal rate,
        decimal cessRate = 0m,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null)
    {
        var created = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/tax-rates",
                new
                {
                    hsnCode,
                    description = "Recorded by an integration test.",
                    rate,
                    cessRate,
                    effectiveFrom = effectiveFrom ?? new DateOnly(2020, 1, 1),
                    effectiveTo,
                },
                cancellationToken),
            cancellationToken);

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>An HSN code nothing else in the collection is using.</summary>
    /// <remarks>
    /// Eight digits, because the collection shares one database and one rate table: two tests each
    /// recording "18% on 940360" would be recording it twice on the same code, and the second would
    /// be refused for a duplicate start date.
    /// </remarks>
    public static string NewHsn()
        => Random.Shared.NextInt64(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);

    /// <summary>Drafts a promotion and, unless told not to, switches it on. Answers its id.</summary>
    /// <param name="type">Percentage, Fixed, FreeShipping, Bogo, Bundle or Tiered.</param>
    /// <param name="appliesTo">Line, Order or Shipping.</param>
    /// <param name="value">The percentage or amount.</param>
    /// <param name="code">The code a shopper types, or null for an automatic rule.</param>
    /// <param name="scope">What it applies to.</param>
    /// <param name="conditions">What a basket must satisfy.</param>
    /// <param name="stacking">Exclusive or Stackable.</param>
    /// <param name="priority">Evaluation order. Lower goes first.</param>
    /// <param name="startsAt">When it opens.</param>
    /// <param name="endsAt">When it closes.</param>
    /// <param name="usageLimitTotal">The most times it may ever be redeemed.</param>
    /// <param name="usageLimitPerCustomer">The most times one shopper may redeem it.</param>
    /// <param name="minOrderValue">The smallest basket it applies to.</param>
    /// <param name="maxDiscount">The most it will ever take off.</param>
    /// <param name="activate">Whether to switch it on.</param>
    public async Task<Guid> PromotionAsync(
        string type = "Percentage",
        string appliesTo = "Order",
        decimal value = 10m,
        string? code = null,
        object? scope = null,
        object? conditions = null,
        string stacking = "Exclusive",
        int priority = 100,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        int? usageLimitTotal = null,
        int? usageLimitPerCustomer = null,
        decimal minOrderValue = 0m,
        decimal? maxDiscount = null,
        bool activate = true)
    {
        var created = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/promotions",
                new
                {
                    code,
                    name = $"Campaign {Guid.NewGuid():N}"[..24],
                    description = (string?)null,
                    type,
                    appliesTo,
                    value,
                    scope,
                    conditions,
                    stacking,
                    priority,
                    startsAt = startsAt ?? DateTimeOffset.UtcNow.AddDays(-1),
                    endsAt,
                    usageLimitTotal,
                    usageLimitPerCustomer,
                    minOrderValue,
                    maxDiscount,
                },
                cancellationToken),
            cancellationToken);

        var promotionId = created.GetProperty("id").GetGuid();

        if (activate)
        {
            await Rest.ReadAsync(
                await admin.PostAsJsonAsync(
                    $"/api/v1/admin/promotions/{promotionId}/activate",
                    new { },
                    cancellationToken),
                cancellationToken);
        }

        return promotionId;
    }

    /// <summary>A coupon code nothing else in the collection is using.</summary>
    public static string NewCouponCode() => $"C{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    /// <summary>
    /// Prices a basket through <c>POST /store/quote</c>, the way a cart does.
    /// </summary>
    /// <remarks>
    /// Through the endpoint rather than through <c>IPriceQuoteEngine</c> directly, because the
    /// endpoint is where the caller's own customer id is put on the request and where the ceilings
    /// are applied. A test that called the engine would be skipping both.
    /// </remarks>
    /// <param name="client">The caller. Anonymous unless the test signed one in.</param>
    /// <param name="lines">The basket, as <c>(offer, quantity)</c>.</param>
    /// <param name="stateId">The shipping address's state, or null to quote at the store's own.</param>
    /// <param name="couponCode">A code to try.</param>
    /// <param name="paymentMethod">Prepaid or CashOnDelivery.</param>
    /// <param name="shippingAmount">What shipping would cost.</param>
    /// <param name="walletRedeemRequested">How much store credit to try to apply.</param>
    public async Task<JsonElement> QuoteAsync(
        HttpClient client,
        (Guid ListingId, int Quantity)[] lines,
        Guid? stateId = null,
        string? couponCode = null,
        string paymentMethod = "Prepaid",
        decimal shippingAmount = 0m,
        decimal walletRedeemRequested = 0m)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(lines);

        return await Rest.ReadAsync(
            await QuoteResponseAsync(
                client,
                lines,
                stateId,
                couponCode,
                paymentMethod,
                shippingAmount,
                walletRedeemRequested),
            cancellationToken);
    }

    /// <summary>The same request, unread, for the tests that expect it to be refused.</summary>
    /// <param name="client">The caller.</param>
    /// <param name="lines">The basket.</param>
    /// <param name="stateId">The shipping address's state.</param>
    /// <param name="couponCode">A code to try.</param>
    /// <param name="paymentMethod">Prepaid or CashOnDelivery.</param>
    /// <param name="shippingAmount">What shipping would cost.</param>
    /// <param name="walletRedeemRequested">How much store credit to try to apply.</param>
    public Task<HttpResponseMessage> QuoteResponseAsync(
        HttpClient client,
        (Guid ListingId, int Quantity)[] lines,
        Guid? stateId = null,
        string? couponCode = null,
        string paymentMethod = "Prepaid",
        decimal shippingAmount = 0m,
        decimal walletRedeemRequested = 0m)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(lines);

        return client.PostAsJsonAsync(
            "/api/v1/store/quote",
            new
            {
                lines = lines.Select(line => new
                {
                    lineId = (Guid?)null,
                    listingId = line.ListingId,
                    quantity = line.Quantity,
                }),
                stateId,
                couponCode,
                paymentMethod,
                shippingAmount,
                walletRedeemRequested,
            },
            cancellationToken);
    }

    /// <summary>The quoted line for one offer, which is where every per-line figure is.</summary>
    /// <param name="quote">A quote.</param>
    /// <param name="listingId">The offer.</param>
    public static JsonElement LineFor(JsonElement quote, Guid listingId)
        => Assert.Single(
            quote.GetProperty("lines").EnumerateArray(),
            line => line.GetProperty("listingId").GetGuid() == listingId);

    /// <summary>One seller's group, which is the sub-order Orders will create from it.</summary>
    /// <param name="quote">A quote.</param>
    /// <param name="vendorId">The seller.</param>
    public static JsonElement GroupFor(JsonElement quote, Guid vendorId)
        => Assert.Single(
            quote.GetProperty("vendorGroups").EnumerateArray(),
            group => group.GetProperty("vendorId").GetGuid() == vendorId);

    /// <summary>One promotion's report, applied or not, with its reason.</summary>
    /// <param name="quote">A quote.</param>
    /// <param name="promotionId">The promotion.</param>
    public static JsonElement PromotionIn(JsonElement quote, Guid promotionId)
        => Assert.Single(
            quote.GetProperty("promotions").EnumerateArray(),
            promotion => promotion.GetProperty("promotionId").GetGuid() == promotionId);

    /// <summary>Whether a promotion was considered at all, which is the candidate query's answer.</summary>
    /// <param name="quote">A quote.</param>
    /// <param name="promotionId">The promotion.</param>
    public static bool WasConsidered(JsonElement quote, Guid promotionId)
        => quote.GetProperty("promotions").EnumerateArray()
            .Any(promotion => promotion.GetProperty("promotionId").GetGuid() == promotionId);

    /// <summary>A decimal off a quote, a line or a group.</summary>
    /// <param name="element">The object to read.</param>
    /// <param name="name">The property.</param>
    public static decimal Amount(JsonElement element, string name) => element.GetProperty(name).GetDecimal();

    /// <summary>A structurally valid PAN nothing else is using.</summary>
    /// <remarks>Five letters, four digits, a letter — and characters three to twelve of the GSTIN.</remarks>
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
}
