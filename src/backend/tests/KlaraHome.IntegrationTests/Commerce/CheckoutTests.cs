using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KlaraHome.Contracts.Vendors;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Carts.Domain;
using KlaraHome.Modules.Carts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 13's own acceptance criterion — a multi-vendor cart produces a correct grouped, priced
/// checkout summary — and the decisions the shopper makes on the way to it: where it goes, who will
/// carry it, and how it is paid for.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class CheckoutTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>The store's documented cash-on-delivery ceiling, in rupees.</summary>
    private const decimal CodCeiling = 5000m;

    /// <summary>
    /// The step's full acceptance criterion: two sellers, two groups, per-seller subtotals, tax and
    /// shipping, and every one of them reconciling to the total the shopper is about to agree to.
    /// </summary>
    /// <remarks>
    /// Driven all the way through the endpoints a storefront calls — open, address, delivery, payment
    /// method, review — because the summary is the product of those decisions and a session assembled
    /// any other way would be one the product could never have produced. The basket is kept below the
    /// free-shipping threshold on purpose: a summary in which delivery is free proves nothing about
    /// how delivery is split between two sellers.
    /// </remarks>
    [Fact]
    public async Task A_two_seller_basket_produces_a_grouped_priced_checkout_summary_that_reconciles()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var first = await scenario.SellerAsync();
        var second = await scenario.SellerAsync();

        var fromFirst = await scenario.OfferAsync(first, sellingPrice: 199m);
        var fromSecond = await scenario.OfferAsync(second, sellingPrice: 249m);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        await scenario.AddAsync(shopper, fromFirst, quantity: 2);
        await scenario.AddAsync(shopper, fromSecond);

        var opened = await ReadAsync(await scenario.StartCheckoutAsync(shopper));
        var sessionId = opened.GetProperty("id").GetGuid();

        await ReadAsync(await scenario.SetAddressAsync(shopper, sessionId, address.Id));

        // Both sellers are offered a service, and each is quoted for their own parcel.
        var offered = await scenario.ShippingOptionsAsync(shopper, sessionId);
        Assert.Equal(2, offered.EnumerateArray().Count());

        Assert.All(
            offered.EnumerateArray(),
            seller => Assert.NotEmpty(seller.GetProperty("options").EnumerateArray()));

        await ReadAsync(await scenario.SetCheapestShippingAsync(shopper, sessionId));
        await ReadAsync(await scenario.SetPaymentMethodAsync(shopper, sessionId, "prepaid"));

        var review = await ReadAsync(await scenario.ReviewAsync(shopper, sessionId));

        Assert.Equal("PaymentSet", review.GetProperty("status").GetString());

        var cart = review.GetProperty("cart");
        var quote = cart.GetProperty("quote");
        var groups = cart.GetProperty("groups").EnumerateArray().ToList();

        Assert.Equal(2, groups.Count);
        Assert.True(cart.GetProperty("isReadyForCheckout").GetBoolean());

        // One delivery choice per seller, and each carries its own promise rather than the basket's.
        var shipments = review.GetProperty("shipments").EnumerateArray().ToList();
        Assert.Equal(2, shipments.Count);

        Assert.All(shipments, shipment =>
        {
            Assert.False(string.IsNullOrWhiteSpace(shipment.GetProperty("selectedCode").GetString()));

            var chosen = Assert.Single(shipment.GetProperty("options").EnumerateArray());
            Assert.True(chosen.GetProperty("promisedMaxDays").GetInt32() >= chosen.GetProperty("promisedMinDays").GetInt32());
            Assert.True(chosen.GetProperty("dispatchSlaHours").GetInt32() > 0);
        });

        // Delivery is actually charged here, and it is split across the sellers rather than landing
        // on one of them.
        var shipping = quote.GetProperty("shipping").GetDecimal();
        Assert.True(shipping > 0m, $"the basket should be below the free-shipping threshold; shipping was {shipping}");

        Assert.Equal(shipping, groups.Sum(group => group.GetProperty("shipping").GetDecimal()));
        Assert.All(groups, group => Assert.True(group.GetProperty("shipping").GetDecimal() > 0m));

        // Tax reconciles: the lines' tax in the groups, the delivery tax beside it, and nothing else.
        // Read off the quote's own groups, which carry the two halves separately; the cart's groups
        // state one tax-inclusive delivery figure, because that is what a shopper is shown.
        var quotedGroups = quote.GetProperty("vendorGroups").EnumerateArray().ToList();

        Assert.Equal(2, quotedGroups.Count);

        Assert.Equal(
            quote.GetProperty("taxTotal").GetDecimal(),
            quotedGroups.Sum(group => group.GetProperty("taxTotal").GetDecimal())
            + quotedGroups.Sum(group => group.GetProperty("shippingTax").GetDecimal()));

        // Each seller's group is their own lines and their own share of delivery, and the cart states
        // the same figures the quote does.
        Assert.All(quotedGroups, group => Assert.Equal(
            group.GetProperty("total").GetDecimal(),
            groups.Single(shown => shown.GetProperty("vendorId").GetGuid() == group.GetProperty("vendorId").GetGuid())
                .GetProperty("total")
                .GetDecimal()));

        // And the total the shopper is about to agree to is the groups plus the two order-level
        // figures, to the paisa.
        Assert.Equal(
            quote.GetProperty("grandTotal").GetDecimal(),
            groups.Sum(group => group.GetProperty("total").GetDecimal())
            + quote.GetProperty("codFee").GetDecimal()
            + quote.GetProperty("roundingAdjustment").GetDecimal());

        // The snapshot the order will be built from is the number on the screen.
        Assert.Equal(
            quote.GetProperty("grandTotal").GetDecimal(),
            await Database.ScalarAsync<decimal>(
                "SELECT grand_total FROM carts.checkout_sessions WHERE id = $1",
                Cancellation,
                sessionId));
    }

    /// <summary>
    /// Serviceability through <c>IVendorDirectory.IsServiceableAsync</c>: serves-all-India says yes
    /// to everything, an exclusion beats an inclusion wherever both match, a PIN prefix matches by
    /// prefix, and a seller nobody has heard of is not serviceable.
    /// </summary>
    /// <remarks>
    /// The last one is the case worth writing down. "No such seller" is not "delivers everywhere",
    /// and a directory that answered yes to an unknown id would let a cart line survive validation
    /// for a seller that does not exist.
    /// </remarks>
    [Fact]
    public async Task Serviceability_answers_all_india_exclusions_prefixes_and_an_unknown_seller()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var everywhere = await scenario.SellerAsync();
        var byPrefix = await scenario.SellerAsync();
        var withExclusion = await scenario.SellerAsync();

        var telangana = await scenario.Sellers.StateIdAsync();

        await scenario.ServiceableRegionsAsync(byPrefix.Id, servesAllIndia: false, CartScenario.PincodeRule("500"));

        // The whole state, minus one sorting region inside it. Two rows, and the second wins.
        await scenario.ServiceableRegionsAsync(
            withExclusion.Id,
            servesAllIndia: false,
            CartScenario.StateRule(telangana),
            CartScenario.PincodeRule("5000", isExcluded: true));

        await RunOnceAsync<IVendorDirectory>(async (directory, cancellation) =>
        {
            // Serves all India: yes to a PIN code nobody named.
            Assert.True(await directory.IsServiceableAsync(everywhere.Id, telangana, "781001", cancellation));

            // A prefix matches by prefix, not by equality.
            Assert.True(await directory.IsServiceableAsync(byPrefix.Id, telangana, "500034", cancellation));
            Assert.True(await directory.IsServiceableAsync(byPrefix.Id, telangana, "500081", cancellation));
            Assert.False(await directory.IsServiceableAsync(byPrefix.Id, telangana, "400001", cancellation));

            // The exclusion beats the state rule where both match, and only where both match.
            Assert.False(await directory.IsServiceableAsync(withExclusion.Id, telangana, "500034", cancellation));
            Assert.True(await directory.IsServiceableAsync(withExclusion.Id, telangana, "500134", cancellation));

            // A seller nobody has heard of is not serviceable anywhere.
            Assert.False(
                await directory.IsServiceableAsync(Guid.CreateVersion7(), telangana, "500034", cancellation));
        });
    }

    /// <summary>
    /// Cash on delivery is checked against all four rules, and the reason given names the one that
    /// failed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fourth rule — no courier will collect cash at that PIN code — was missing until Step 29,
    /// and it is the one that costs money: delivery is chosen before the payment method, so the
    /// services on offer were quoted as prepaid parcels and had nothing to say about cash. A shopper
    /// could choose cash for a destination no courier collects at, and nobody would find out until a
    /// driver was at the door with a parcel and no way to be paid for it.
    /// </para>
    /// <para>
    /// The store-wide switch is restored in a <c>finally</c>: it is a row in the shared settings
    /// table, and leaving it off would silently disable cash on delivery for whatever ran next.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Cash_on_delivery_is_refused_by_each_of_its_four_rules_with_the_reason_that_failed()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();

        var cashRefused = await scenario.OfferAsync(seller, sellingPrice: 299m, isCodAllowed: false);
        var expensive = await scenario.OfferAsync(seller, sellingPrice: 1_250m, stock: 10);
        var ordinary = await scenario.OfferAsync(seller, sellingPrice: 299m);

        // Rule two: one item its seller will not take cash for, and the reason names it.
        var (withRefusedItem, _) = await SignedInShopperAsync();
        var refusedAddress = await scenario.AddressAsync(withRefusedItem);

        await scenario.AddAsync(withRefusedItem, cashRefused);

        var refusedSession = await OpenToShippingAsync(scenario, withRefusedItem, refusedAddress);

        var itemReason = await CodReasonAsync(scenario, withRefusedItem, refusedSession);
        Assert.Contains("cannot be paid for at the door", itemReason, StringComparison.Ordinal);

        await RefusedAsync(
            await scenario.SetPaymentMethodAsync(withRefusedItem, refusedSession, "cod"),
            HttpStatusCode.UnprocessableEntity,
            "CHECKOUT_COD_UNAVAILABLE");

        // Rule three: over the store's value ceiling, and the reason names the ceiling.

        // The ceiling has to be the store's own for this to be about the ceiling. `OrderScenario`
        // raises it to a million so its own cash orders are not refused, and does not put it back —
        // the settings row is shared by every class in the collection — so whichever ran first
        // decided what this figure was. Pinned to the documented default here, and left there:
        // that is the value anything running after should find.
        await SetCodCeilingAsync(admin, await SettingsSectionAsync(admin, "commerce"), CodCeiling);
        var (bigSpender, _) = await SignedInShopperAsync();
        var bigAddress = await scenario.AddressAsync(bigSpender);

        await scenario.AddAsync(bigSpender, expensive, quantity: 5);

        var bigSession = await OpenToShippingAsync(scenario, bigSpender, bigAddress);

        var ceilingReason = await CodReasonAsync(scenario, bigSpender, bigSession);
        Assert.Contains(CodCeiling.ToString(System.Globalization.CultureInfo.InvariantCulture), ceilingReason, StringComparison.Ordinal);

        await RefusedAsync(
            await scenario.SetPaymentMethodAsync(bigSpender, bigSession, "cod"),
            HttpStatusCode.UnprocessableEntity,
            "CHECKOUT_COD_UNAVAILABLE");

        // Rule four: a PIN code the courier will reach and will not collect cash at.
        const string NoCashPincode = "500091";

        Factory.Courier.NoCod.Add(NoCashPincode);

        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/shipping/serviceability/{NoCashPincode}/refresh",
            new { },
            Cancellation));

        var (atNoCash, _) = await SignedInShopperAsync();
        var noCashAddress = await scenario.AddressAsync(atNoCash, pincode: NoCashPincode);

        await scenario.AddAsync(atNoCash, ordinary);

        var noCashSession = await OpenToShippingAsync(scenario, atNoCash, noCashAddress);

        var courierReason = await CodReasonAsync(scenario, atNoCash, noCashSession);
        Assert.Contains("collect cash", courierReason, StringComparison.Ordinal);

        await RefusedAsync(
            await scenario.SetPaymentMethodAsync(atNoCash, noCashSession, "cod"),
            HttpStatusCode.UnprocessableEntity,
            "CHECKOUT_COD_UNAVAILABLE");

        // Prepaid is still offered there, which is the whole reason this is its own refusal.
        await ReadAsync(await scenario.SetPaymentMethodAsync(atNoCash, noCashSession, "prepaid"));

        // Rule one: the store does not offer cash on delivery at all.
        var (ordinaryShopper, _) = await SignedInShopperAsync();
        var ordinaryAddress = await scenario.AddressAsync(ordinaryShopper);

        await scenario.AddAsync(ordinaryShopper, ordinary);

        var ordinarySession = await OpenToShippingAsync(scenario, ordinaryShopper, ordinaryAddress);

        // It is available before the switch is thrown, which is what makes the refusal below the
        // switch's doing rather than something else's.
        Assert.Null(await CodReasonAsync(scenario, ordinaryShopper, ordinarySession));

        var commerce = await SettingsSectionAsync(admin, "commerce");

        try
        {
            await SetCodEnabledAsync(admin, commerce, enabled: false);

            var storeReason = await CodReasonAsync(scenario, ordinaryShopper, ordinarySession);
            Assert.Contains("does not offer cash on delivery", storeReason, StringComparison.Ordinal);

            await RefusedAsync(
                await scenario.SetPaymentMethodAsync(ordinaryShopper, ordinarySession, "cod"),
                HttpStatusCode.UnprocessableEntity,
                "CHECKOUT_COD_UNAVAILABLE");
        }
        finally
        {
            await SetCodEnabledAsync(admin, commerce, enabled: true);
            Factory.Courier.NoCod.Remove(NoCashPincode);
        }
    }

    /// <summary>
    /// Moving the destination clears every delivery choice and re-prices; correcting the address
    /// within the same PIN code keeps them.
    /// </summary>
    /// <remarks>
    /// A rate quoted to one PIN code is not a rate to another, and silently keeping it is how a
    /// shopper is charged Hyderabad's shipping for a parcel to Shillong. Correcting a flat number is
    /// the opposite case and has to be cheap, or every typo costs the shopper their delivery choice.
    /// </remarks>
    [Fact]
    public async Task Moving_the_destination_clears_the_delivery_choices_and_a_correction_keeps_them()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 199m);

        var (shopper, _) = await SignedInShopperAsync();

        var banjaraHills = await scenario.AddressAsync(shopper);
        var gachibowli = await scenario.AddressAsync(shopper, pincode: "500032", line1: "Tower B, Lanco Hills");

        // The same PIN code as Gachibowli and a different flat: a typo corrected, not a move.
        var corrected = await scenario.AddressAsync(shopper, pincode: "500032", line1: "Tower C, Lanco Hills");

        await scenario.AddAsync(shopper, offer, quantity: 2);

        var opened = await ReadAsync(await scenario.StartCheckoutAsync(shopper));
        var sessionId = opened.GetProperty("id").GetGuid();

        await ReadAsync(await scenario.SetAddressAsync(shopper, sessionId, banjaraHills.Id));
        var chosen = await ReadAsync(await scenario.SetCheapestShippingAsync(shopper, sessionId));

        Assert.Equal("ShippingSet", chosen.GetProperty("status").GetString());
        Assert.Equal(1, await ShipmentsAsync(sessionId));

        var shippingTotal = await Database.ScalarAsync<decimal>(
            "SELECT shipping_total FROM carts.checkout_sessions WHERE id = $1",
            Cancellation,
            sessionId);

        Assert.True(shippingTotal > 0m);

        // A different PIN code. The choice, its charge and the step it unlocked all go.
        var moved = await ReadAsync(await scenario.SetAddressAsync(shopper, sessionId, gachibowli.Id));

        Assert.Equal("AddressSet", moved.GetProperty("status").GetString());
        Assert.Empty(moved.GetProperty("shipments").EnumerateArray()
            .SelectMany(shipment => shipment.GetProperty("options").EnumerateArray()));

        Assert.Equal(0, await ShipmentsAsync(sessionId));

        Assert.Equal(
            0m,
            await Database.ScalarAsync<decimal>(
                "SELECT shipping_total FROM carts.checkout_sessions WHERE id = $1",
                Cancellation,
                sessionId));

        // Choose again, then correct the flat number inside the same PIN code.
        await ReadAsync(await scenario.SetCheapestShippingAsync(shopper, sessionId));
        Assert.Equal(1, await ShipmentsAsync(sessionId));

        var stillChosen = await ReadAsync(await scenario.SetAddressAsync(shopper, sessionId, corrected.Id));

        // The address is the new one and the delivery choice survived it.
        Assert.Equal(
            corrected.Id,
            stillChosen.GetProperty("shippingAddress").GetProperty("sourceAddressId").GetGuid());

        Assert.Equal("ShippingSet", stillChosen.GetProperty("status").GetString());
        Assert.Equal(1, await ShipmentsAsync(sessionId));
    }

    /// <summary>
    /// A delivery choice is re-quoted rather than trusted: a code that was not offered is refused,
    /// and an amount or a carrier named by the client is not what gets stored.
    /// </summary>
    /// <remarks>
    /// The price and the promise are the server's to state. A client that could name an amount could
    /// name zero, and the difference would be discovered by a seller reading their settlement.
    /// </remarks>
    [Fact]
    public async Task A_delivery_choice_is_requoted_and_an_option_that_was_not_offered_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var first = await scenario.SellerAsync();
        var second = await scenario.SellerAsync();

        var fromFirst = await scenario.OfferAsync(first, sellingPrice: 199m);
        var fromSecond = await scenario.OfferAsync(second, sellingPrice: 249m);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        await scenario.AddAsync(shopper, fromFirst);
        await scenario.AddAsync(shopper, fromSecond);

        var opened = await ReadAsync(await scenario.StartCheckoutAsync(shopper));
        var sessionId = opened.GetProperty("id").GetGuid();

        await ReadAsync(await scenario.SetAddressAsync(shopper, sessionId, address.Id));

        var offered = await scenario.ShippingOptionsAsync(shopper, sessionId);

        var quoted = offered.EnumerateArray()
            .First(seller => seller.GetProperty("vendorId").GetGuid() == first.Id)
            .GetProperty("options")
            .EnumerateArray()
            .First();

        var code = quoted.GetProperty("code").GetString()!;
        var amount = quoted.GetProperty("amount").GetDecimal();

        // A code nobody offered.
        await RefusedAsync(
            await scenario.SetShippingAsync(shopper, sessionId, (first.Id, "free-teleportation"), (second.Id, code)),
            HttpStatusCode.UnprocessableEntity,
            "CHECKOUT_UNKNOWN_SHIPPING_OPTION");

        // A seller left undecided. A basket with one parcel decided and one not is a total nobody
        // can quote.
        await RefusedAsync(
            await scenario.SetShippingAsync(shopper, sessionId, (first.Id, code)),
            HttpStatusCode.UnprocessableEntity,
            "CHECKOUT_INCOMPLETE");

        Assert.Equal(0, await ShipmentsAsync(sessionId));

        // And a client naming its own amount and carrier is simply not believed: the body has no
        // such fields, and what is stored is what the server quoted.
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            new Uri($"/api/v1/store/checkout/{sessionId}/shipping", UriKind.Relative))
        {
            Content = new StringContent(
                $$"""
                  {
                    "perVendor": [
                      { "vendorId": "{{first.Id}}", "optionCode": "{{code}}", "amount": 0, "carrier": "Free Couriers" },
                      { "vendorId": "{{second.Id}}", "optionCode": "{{code}}", "amount": 0, "carrier": "Free Couriers" }
                    ]
                  }
                  """,
                Encoding.UTF8,
                "application/json"),
        };

        using var accepted = await shopper.SendAsync(request, Cancellation);
        await ReadAsync(accepted);

        var stored = await Database.RowsAsync(
            "SELECT amount, carrier FROM carts.checkout_shipments WHERE checkout_session_id = $1 AND vendor_id = $2",
            Cancellation,
            sessionId,
            first.Id);

        var row = Assert.Single(stored);

        Assert.Equal(amount, (decimal)row["amount"]!);
        Assert.True(amount > 0m, "the server's own quote should not be free for this basket");
        Assert.NotEqual("Free Couriers", row["carrier"] as string);
    }

    /// <summary>
    /// The address snapshots and the agreed quote survive a save and a reload through <c>jsonb</c>
    /// intact, nested vendor groups and promotion list included.
    /// </summary>
    /// <remarks>
    /// They are stored as documents rather than as owned entity graphs, which means a converter
    /// writes them and a converter reads them back — and a converter is exactly the kind of thing
    /// that works until the shape gains a nested list. Reloaded in a fresh context, because a value
    /// still sitting in the change tracker would prove only that the object was assigned.
    /// </remarks>
    [Fact]
    public async Task The_address_and_quote_snapshots_round_trip_through_jsonb()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var first = await scenario.SellerAsync();
        var second = await scenario.SellerAsync();

        var fromFirst = await scenario.OfferAsync(first, sellingPrice: 199m);
        var fromSecond = await scenario.OfferAsync(second, sellingPrice: 249m);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper, gstin: "36AABCU9603R1ZM");

        await scenario.AddAsync(shopper, fromFirst, quantity: 2);
        await scenario.AddAsync(shopper, fromSecond);

        var opened = await ReadAsync(await scenario.StartCheckoutAsync(shopper));
        var sessionId = opened.GetProperty("id").GetGuid();

        await ReadAsync(await scenario.SetAddressAsync(shopper, sessionId, address.Id, gstin: "36AABCU9603R1ZM"));
        await ReadAsync(await scenario.SetCheapestShippingAsync(shopper, sessionId));

        // Both columns really are jsonb, not text that happens to hold JSON.
        var types = Assert.Single(await Database.RowsAsync(
            """
            SELECT pg_typeof(shipping_address)::text AS shipping,
                   pg_typeof(billing_address)::text  AS billing,
                   pg_typeof(quote_snapshot)::text   AS quote
            FROM carts.checkout_sessions WHERE id = $1
            """,
            Cancellation,
            sessionId));

        Assert.All(types.Values, type => Assert.Equal("jsonb", type?.ToString()));

        await RunOnceAsync<CartsDbContext>(async (context, cancellation) =>
        {
            var reloaded = await context.CheckoutSessions
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstAsync(session => session.Id == sessionId, cancellation);

            var shipping = Assert.IsType<AddressSnapshot>(reloaded.ShippingAddress);

            Assert.Equal(address.Id, shipping.SourceAddressId);
            Assert.Equal("Test Shopper", shipping.RecipientName);
            Assert.Equal("500034", shipping.Pincode);
            Assert.Equal("Hyderabad", shipping.City);
            Assert.Equal("Road No 12", shipping.Line2);
            Assert.Equal(address.StateId, shipping.StateId);
            Assert.Equal("36AABCU9603R1ZM", shipping.Gstin);

            var billing = Assert.IsType<AddressSnapshot>(reloaded.BillingAddress);
            Assert.Equal(address.Id, billing.SourceAddressId);

            var quote = reloaded.QuoteSnapshot;
            Assert.NotNull(quote);

            // The nested collections are the part a converter loses: two sellers' groups, each with
            // its own line ids, and the promotion list beside them.
            Assert.Equal(2, quote!.VendorGroups.Count);
            Assert.All(quote.VendorGroups, group => Assert.NotEmpty(group.LineIds));
            Assert.NotNull(quote.Promotions);

            Assert.Equal(3, quote.Lines.Sum(line => line.Quantity));
            Assert.Equal(reloaded.GrandTotal, quote.GrandTotal);
            Assert.Equal("INR", quote.CurrencyCode);
        });
    }

    /// <summary>A place-order request carrying no idempotency key is refused before anything happens.</summary>
    /// <remarks>
    /// Required rather than optional, because this is the one request on the platform that reserves
    /// stock and creates an order: without a key, a retried request is a second order.
    /// </remarks>
    [Fact]
    public async Task Place_order_without_an_idempotency_key_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 199m);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        await scenario.AddAsync(shopper, offer);

        var sessionId = await scenario.ReadyCheckoutAsync(shopper, address);

        await RefusedAsync(
            await scenario.PlaceOrderAsync(shopper, sessionId, idempotencyKey: null),
            HttpStatusCode.BadRequest,
            "IDEMPOTENCY_KEY_REQUIRED");

        // An empty header is the same omission written differently.
        await RefusedAsync(
            await scenario.PlaceOrderAsync(shopper, sessionId, idempotencyKey: string.Empty),
            HttpStatusCode.BadRequest,
            "IDEMPOTENCY_KEY_REQUIRED");

        // Nothing was attempted: no order, and no claim on any key.
        Assert.Equal(
            0,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM carts.checkout_placements WHERE checkout_session_id = $1",
                Cancellation,
                sessionId));
    }

    /// <summary>Opens a checkout and takes it as far as a chosen delivery service.</summary>
    private static async Task<Guid> OpenToShippingAsync(
        CartScenario scenario,
        HttpClient shopper,
        ShopperAddress address)
    {
        var opened = await ReadAsync(await scenario.StartCheckoutAsync(shopper));
        var sessionId = opened.GetProperty("id").GetGuid();

        await ReadAsync(await scenario.SetAddressAsync(shopper, sessionId, address.Id));
        await ReadAsync(await scenario.SetCheapestShippingAsync(shopper, sessionId));

        return sessionId;
    }

    /// <summary>Why cash on delivery may not be chosen for a session, or null when it may.</summary>
    private static async Task<string?> CodReasonAsync(CartScenario scenario, HttpClient shopper, Guid sessionId)
    {
        var methods = await scenario.PaymentMethodsAsync(shopper, sessionId);

        var cod = Assert.Single(
            methods.EnumerateArray(),
            method => method.GetProperty("method").GetString() == "cod");

        var reason = cod.GetProperty("reason");

        // Availability and the reason are two statements of one fact and must never disagree.
        Assert.Equal(reason.ValueKind == JsonValueKind.Null, cod.GetProperty("isAvailable").GetBoolean());

        return reason.ValueKind == JsonValueKind.Null ? null : reason.GetString();
    }

    /// <summary>How many delivery choices a session is holding.</summary>
    private Task<long> ShipmentsAsync(Guid sessionId)
        => Database.CountAsync(
            "SELECT COUNT(*) FROM carts.checkout_shipments WHERE checkout_session_id = $1",
            Cancellation,
            sessionId);

    /// <summary>One store settings section, exactly as it stands.</summary>
    private static async Task<JsonElement> SettingsSectionAsync(HttpClient admin, string key)
    {
        var settings = await ReadAsync(await admin.GetAsync(
            new Uri("/api/v1/admin/settings", UriKind.Relative),
            Cancellation));

        return Assert.Single(
                settings.GetProperty("sections").EnumerateArray(),
                section => section.GetProperty("key").GetString() == key)
            .GetProperty("value")
            .Clone();
    }

    /// <summary>Pins the store's cash-on-delivery ceiling, leaving every other figure alone.</summary>
    /// <remarks>
    /// The sibling of <see cref="SetCodEnabledAsync"/>, and it exists for the same reason: the
    /// commerce section is one row shared by the whole collection, so a test that asserts on a
    /// figure in it must set that figure rather than inherit it.
    /// </remarks>
    private static async Task SetCodCeilingAsync(HttpClient admin, JsonElement commerce, decimal ceiling)
    {
        var section = commerce.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Name.Equals("codOrderValueLimit", StringComparison.OrdinalIgnoreCase)
                    ? (object)ceiling
                    : property.Name.Equals("codEnabled", StringComparison.OrdinalIgnoreCase)
                        ? true
                        : JsonSerializer.Deserialize<object>(property.Value.GetRawText())!,
                StringComparer.Ordinal);

        using var response = await admin.PutAsJsonAsync(
            new Uri("/api/v1/admin/settings/commerce", UriKind.Relative),
            section,
            Cancellation);

        await ReadAsync(response);
    }

    /// <summary>Throws the store-wide cash-on-delivery switch, leaving every other figure alone.</summary>
    private static async Task SetCodEnabledAsync(HttpClient admin, JsonElement commerce, bool enabled)
    {
        var section = commerce.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Name.Equals("codEnabled", StringComparison.OrdinalIgnoreCase)
                    ? (object)enabled
                    : JsonSerializer.Deserialize<object>(property.Value.GetRawText())!,
                StringComparer.Ordinal);

        using var response = await admin.PutAsJsonAsync(
            new Uri("/api/v1/admin/settings/commerce", UriKind.Relative),
            section,
            Cancellation);

        await ReadAsync(response);
    }
}
