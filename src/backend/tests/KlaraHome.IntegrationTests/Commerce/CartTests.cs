using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The basket itself: who it belongs to, what it holds no claim over, and everything it has to tell
/// a shopper before they are allowed to pay for it.
/// </summary>
/// <remarks>
/// <para>
/// The cookie is written without <c>Secure</c> in this host and only in this host. The test server
/// speaks plain HTTP, and <c>CookieContainer</c> will accept a <c>Secure</c> cookie over it and then
/// never send it back — so every anonymous basket would be lost on the request after the one that
/// created it, and the guest half of this module would be untestable for a reason that has nothing
/// to do with the product.
/// </para>
/// </remarks>
public sealed class CartTests : CommerceTestBase
{
    /// <param name="fixture">The migrated database.</param>
    public CartTests(KlaraHomeSchemaFixture fixture)
        : base(fixture)
        => Factory.Overrides["Carts:CartCookieSecure"] = "false";

    /// <summary>
    /// A basket holding two sellers' offers is grouped by seller, and the groups' figures are the
    /// quote's own rather than a second sum of the same lines.
    /// </summary>
    /// <remarks>
    /// A multi-vendor basket is several parcels with several invoices, and the group is the sub-order
    /// it will become. The reconciliation is the part worth asserting: a cart that derived its group
    /// totals separately from the quote would agree with the invoice until the first time the two
    /// rounded differently, and would then disagree at the payment screen.
    /// </remarks>
    [Fact]
    public async Task A_two_seller_basket_is_grouped_by_seller_and_the_groups_reconcile_to_the_quote()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var first = await scenario.SellerAsync();
        var second = await scenario.SellerAsync();

        var fromFirst = await scenario.OfferAsync(first, sellingPrice: 499m);
        var fromSecond = await scenario.OfferAsync(second, sellingPrice: 899m);

        var (shopper, _) = await SignedInShopperAsync();

        await scenario.AddAsync(shopper, fromFirst, quantity: 2);
        var cart = await scenario.AddAsync(shopper, fromSecond);

        var groups = cart.GetProperty("groups").EnumerateArray().ToList();
        Assert.Equal(2, groups.Count);

        var quote = cart.GetProperty("quote");
        var quotedGroups = quote.GetProperty("vendorGroups").EnumerateArray()
            .ToDictionary(group => group.GetProperty("vendorId").GetGuid());

        foreach (var group in groups)
        {
            var vendorId = group.GetProperty("vendorId").GetGuid();
            var quoted = quotedGroups[vendorId];

            // Not "the numbers are plausible": the numbers are the quote's, to the paisa.
            Assert.Equal(quoted.GetProperty("subtotal").GetDecimal(), group.GetProperty("subtotal").GetDecimal());
            Assert.Equal(quoted.GetProperty("discount").GetDecimal(), group.GetProperty("discount").GetDecimal());
            Assert.Equal(quoted.GetProperty("taxTotal").GetDecimal(), group.GetProperty("taxTotal").GetDecimal());
            Assert.Equal(quoted.GetProperty("total").GetDecimal(), group.GetProperty("total").GetDecimal());

            Assert.False(string.IsNullOrWhiteSpace(group.GetProperty("vendorName").GetString()));
            Assert.True(group.GetProperty("dispatchSlaHours").GetInt32() > 0);
        }

        // Each group holds exactly its own seller's lines, which is the split the sub-orders will use.
        var lines = cart.GetProperty("lines").EnumerateArray()
            .ToDictionary(line => line.GetProperty("id").GetGuid(), line => line.GetProperty("vendorId").GetGuid());

        foreach (var group in groups)
        {
            var vendorId = group.GetProperty("vendorId").GetGuid();

            Assert.All(
                group.GetProperty("lineIds").EnumerateArray(),
                lineId => Assert.Equal(vendorId, lines[lineId.GetGuid()]));
        }

        // The sellers' totals, the cash-handling fee and the rupee rounding are the whole of the
        // grand total. Nothing is charged that no group accounts for.
        Assert.Equal(
            quote.GetProperty("grandTotal").GetDecimal(),
            groups.Sum(group => group.GetProperty("total").GetDecimal())
            + quote.GetProperty("codFee").GetDecimal()
            + quote.GetProperty("roundingAdjustment").GetDecimal());

        Assert.Equal(
            quote.GetProperty("subtotal").GetDecimal() - quote.GetProperty("discountTotal").GetDecimal(),
            groups.Sum(group => group.GetProperty("total").GetDecimal())
            - groups.Sum(group => group.GetProperty("shipping").GetDecimal()));
    }

    /// <summary>
    /// Adding to, changing and rendering a basket reserve nothing. Only <c>place-order</c> does.
    /// </summary>
    /// <remarks>
    /// The rule the whole module is arranged around (docs/02-domain-model.md §4.2): holding stock at
    /// add-to-cart would make every browsing shopper a denial of service against every buying one.
    /// It is asserted against <c>inventory.stock_reservations</c> rather than against the available
    /// count, because a hold that took a unit and gave it straight back would leave the count right
    /// and the rule broken.
    /// </remarks>
    [Fact]
    public async Task A_basket_reserves_no_stock_and_only_place_order_does()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 299m, stock: 10);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        var cart = await scenario.AddAsync(shopper, offer, quantity: 2);
        var cartId = cart.GetProperty("id").GetGuid();
        var lineId = cart.GetProperty("lines").EnumerateArray().First().GetProperty("id").GetGuid();

        await ReadAsync(await shopper.PatchAsJsonAsync(
            $"/api/v1/store/cart/items/{lineId}",
            new { quantity = 3, savedForLater = (bool?)null },
            Cancellation));

        await scenario.CartAsync(shopper);

        Assert.Equal(0, await HoldsAsync(cartId));

        // The whole basket still on sale: three units in a basket are three units anybody can buy.
        var stock = Assert.Single(await Database.RowsAsync(
            "SELECT quantity_on_hand, quantity_reserved FROM inventory.stock_items WHERE id = $1",
            Cancellation,
            offer.StockItemId));

        Assert.Equal(10, (int)stock["quantity_on_hand"]!);
        Assert.Equal(0, (int)stock["quantity_reserved"]!);

        var sessionId = await scenario.ReadyCheckoutAsync(shopper, address);

        await ReadAsync(await scenario.PlaceOrderAsync(
            shopper,
            sessionId,
            CartScenario.NewIdempotencyKey("holds")));

        // And now — and only now — the units are spoken for against the cart.
        Assert.Equal(1, await HoldsAsync(cartId));
    }

    /// <summary>
    /// The anonymous cookie round trip: a token is issued, stored only as a digest, and resolves the
    /// basket it named. A forged one matches no row and gets a fresh basket rather than somebody
    /// else's.
    /// </summary>
    /// <remarks>
    /// The token is a bearer capability — whoever holds it can read and edit the basket — so the
    /// assertion that the row holds a digest rather than the value is the one that matters: a dump of
    /// <c>carts.carts</c> must hand an attacker nobody's basket.
    /// </remarks>
    [Fact]
    public async Task An_anonymous_token_round_trips_hashed_and_a_forged_one_gets_a_fresh_basket()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 249m);

        // Cookies handled by hand rather than by a jar, because the token itself is what is under
        // test: it has to be read off the header the server set and put back on the next request.
        using var browser = Factory.CreateClient(NoCookieJar);

        string token;
        Guid cartId;

        using (var opening = await scenario.TryAddAsync(browser, offer.ListingId, quantity: 2))
        {
            cartId = (await ReadAsync(opening)).GetProperty("id").GetGuid();
            token = TokenIn(opening);
        }

        browser.DefaultRequestHeaders.Add("Cookie", $"kh_cart={token}");

        // The same browser comes back and finds the same basket, resolved from the cookie alone.
        var returned = await scenario.CartAsync(browser);
        Assert.Equal(cartId, returned.GetProperty("id").GetGuid());
        Assert.Equal(2, returned.GetProperty("lines").EnumerateArray().First().GetProperty("quantity").GetInt32());
        Assert.True(returned.GetProperty("customerId").ValueKind == JsonValueKind.Null);

        // Stored hashed: a SHA-256 digest, base64, and never the value in the cookie.
        var stored = await Database.ScalarAsync<string>(
            "SELECT anonymous_token_hash FROM carts.carts WHERE id = $1",
            Cancellation,
            cartId);

        Assert.NotNull(stored);
        Assert.Equal(44, stored!.Length);
        Assert.Equal(32, Convert.FromBase64String(stored).Length);

        Assert.NotEqual(token, stored);
        Assert.Equal(stored, HashOf(token));

        // A forged token matches no row. The read answers an empty basket rather than anybody's.
        using var forger = Factory.CreateClient(NoCookieJar);

        forger.DefaultRequestHeaders.Add("Cookie", $"kh_cart={Convert.ToBase64String(new byte[32])}");

        var empty = await scenario.CartAsync(forger);
        Assert.Equal(Guid.Empty, empty.GetProperty("id").GetGuid());
        Assert.Empty(empty.GetProperty("lines").EnumerateArray());

        // And a write on a forged token opens a new basket rather than adopting the one it named.
        var forged = await scenario.AddAsync(forger, offer);
        Assert.NotEqual(cartId, forged.GetProperty("id").GetGuid());
        Assert.Single(forged.GetProperty("lines").EnumerateArray());
    }

    /// <summary>
    /// Merging on login sums the quantities, clamps them to the store's per-line ceiling, retires the
    /// guest basket, clears the cookie, and does nothing at all the second time it is called.
    /// </summary>
    /// <remarks>
    /// The partial unique index is the reason this cannot be "keep both": exactly one <c>Active</c>
    /// cart may exist per customer, so the guest row has to stop being active in the same transaction
    /// that folds it in. Retiring rather than deleting is deliberate — a shopper who says "my basket
    /// lost something" is then answerable from the record instead of from a guess.
    /// </remarks>
    [Fact]
    public async Task Merging_on_login_sums_and_clamps_retires_the_guest_basket_and_is_idempotent()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var shared = await scenario.OfferAsync(seller, sellingPrice: 199m, stock: 50);
        var guestOnly = await scenario.OfferAsync(seller, sellingPrice: 349m, stock: 50);

        // The shopper already has a basket of their own, signed in on another device.
        var (onPhone, mobile) = await SignedInShopperAsync();
        var own = await scenario.AddAsync(onPhone, shared, quantity: 6);
        var ownCartId = own.GetProperty("id").GetGuid();

        // And a basket built in this browser before they signed in.
        using var browser = CreateClient();

        await scenario.AddAsync(browser, shared, quantity: 6);
        var guest = await scenario.AddAsync(browser, guestOnly, quantity: 2);
        var guestCartId = guest.GetProperty("id").GetGuid();

        Assert.NotEqual(ownCartId, guestCartId);

        await SignInShopperOnAsync(browser, mobile);

        var merged = await ReadAsync(await browser.PostAsJsonAsync(
            "/api/v1/store/cart/merge",
            new { },
            Cancellation));

        // Their own basket survives; the guest one is folded into it.
        Assert.Equal(ownCartId, merged.GetProperty("id").GetGuid());
        Assert.Equal(2, merged.GetProperty("lineCount").GetInt32());

        var lines = merged.GetProperty("lines").EnumerateArray()
            .ToDictionary(
                line => line.GetProperty("listingId").GetGuid(),
                line => line.GetProperty("quantity").GetInt32());

        // Six and six is twelve, clamped to the store's ten-per-line ceiling rather than refused.
        Assert.Equal(10, lines[shared.ListingId]);
        Assert.Equal(2, lines[guestOnly.ListingId]);

        var guestStatus = await Database.ScalarAsync<string>(
            "SELECT status FROM carts.carts WHERE id = $1",
            Cancellation,
            guestCartId);

        Assert.Equal("Expired", guestStatus);

        // One Active cart for this shopper, which is what the partial unique index promises.
        var customerId = await Database.ScalarAsync<Guid>(
            "SELECT customer_id FROM carts.carts WHERE id = $1",
            Cancellation,
            ownCartId);

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM carts.carts WHERE customer_id = $1 AND status = 'Active'",
                Cancellation,
                customerId));

        // A second call finds no guest cookie and changes nothing.
        var again = await ReadAsync(await browser.PostAsJsonAsync(
            "/api/v1/store/cart/merge",
            new { },
            Cancellation));

        Assert.Equal(ownCartId, again.GetProperty("id").GetGuid());
        Assert.Equal(2, again.GetProperty("lineCount").GetInt32());

        Assert.Equal(
            10,
            again.GetProperty("lines").EnumerateArray()
                .First(line => line.GetProperty("listingId").GetGuid() == shared.ListingId)
                .GetProperty("quantity")
                .GetInt32());
    }

    /// <summary>
    /// A plain read of the basket performs the merge and saves it, and the write it makes on that
    /// read path does not fire again on the next one.
    /// </summary>
    /// <remarks>
    /// Merging on the first read rather than only on <c>POST /store/cart/merge</c> is deliberate: a
    /// shopper who signs in and finds an empty basket does not file a bug, they leave. The second
    /// half is what keeps that defensible — a <c>GET</c> that wrote on every request would be a
    /// write amplifier on the hottest authenticated read the storefront has.
    /// </remarks>
    [Fact]
    public async Task A_read_merges_once_and_writes_nothing_on_the_next_read()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 129m);

        var (onPhone, mobile) = await SignedInShopperAsync();
        var own = await scenario.AddAsync(onPhone, offer, quantity: 1);
        var ownCartId = own.GetProperty("id").GetGuid();

        using var browser = CreateClient();
        var guest = await scenario.AddAsync(browser, offer, quantity: 3);
        var guestCartId = guest.GetProperty("id").GetGuid();

        await SignInShopperOnAsync(browser, mobile);

        // The read alone does it. Nothing called the merge endpoint.
        var afterRead = await scenario.CartAsync(browser);

        Assert.Equal(ownCartId, afterRead.GetProperty("id").GetGuid());
        Assert.Equal(4, afterRead.GetProperty("lines").EnumerateArray().First().GetProperty("quantity").GetInt32());

        Assert.Equal(
            "Expired",
            await Database.ScalarAsync<string>(
                "SELECT status FROM carts.carts WHERE id = $1",
                Cancellation,
                guestCartId));

        var afterMerge = await TouchedAtAsync(ownCartId);

        // Two more reads. There is nothing left to merge, so there is nothing to save.
        await scenario.CartAsync(browser);
        await scenario.CartAsync(browser);

        Assert.Equal(afterMerge, await TouchedAtAsync(ownCartId));

        // And a read by a shopper who never had a guest cookie writes nothing either.
        var (other, _) = await SignedInShopperAsync();
        var otherCart = await scenario.AddAsync(other, offer);
        var otherCartId = otherCart.GetProperty("id").GetGuid();

        var beforeReads = await TouchedAtAsync(otherCartId);

        await scenario.CartAsync(other);
        await scenario.CartAsync(other);

        Assert.Equal(beforeReads, await TouchedAtAsync(otherCartId));
    }

    /// <summary>
    /// A basket with five different problems reports all five at once, and blocks only the four that
    /// should block.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "You cannot check out" is useless on its own, and a rule that reveals the next problem only
    /// after the last is corrected is a basket nobody finishes. The withdrawn offer, the suspended
    /// seller, the sold-out line and the seller who will not deliver there each stop the sale; the
    /// price change is a disclosure and does not.
    /// </para>
    /// <para>
    /// Read through the checkout rather than the cart page, because the serviceability answer needs a
    /// destination and a cart page has none. It is the same renderer either way, which is the whole
    /// point of there being one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Cart_validation_reports_every_reason_at_once_and_the_price_notice_never_blocks()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var stocked = await scenario.SellerAsync();
        var suspended = await scenario.SellerAsync();
        var elsewhere = await scenario.SellerAsync();

        var withdrawn = await scenario.OfferAsync(stocked, sellingPrice: 199m);
        var soldOut = await scenario.OfferAsync(stocked, sellingPrice: 299m, stock: 5);
        var repriced = await scenario.OfferAsync(stocked, sellingPrice: 399m);
        var fromSuspended = await scenario.OfferAsync(suspended, sellingPrice: 499m);
        var fromElsewhere = await scenario.OfferAsync(elsewhere, sellingPrice: 599m);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        await scenario.AddAsync(shopper, withdrawn);
        await scenario.AddAsync(shopper, soldOut, quantity: 3);
        await scenario.AddAsync(shopper, repriced);
        await scenario.AddAsync(shopper, fromSuspended);
        await scenario.AddAsync(shopper, fromElsewhere);

        var session = await ReadAsync(await scenario.StartCheckoutAsync(shopper));
        var sessionId = session.GetProperty("id").GetGuid();

        await ReadAsync(await scenario.SetAddressAsync(shopper, sessionId, address.Id));

        // Now break five different things behind the shopper's back.
        await scenario.WithdrawAsync(withdrawn);
        await scenario.AdjustStockAsync(soldOut, -4);
        await scenario.RepriceAsync(repriced, sellingPrice: 449m);
        await ReadAsync(await scenario.Sellers.TransitionAsync(suspended.Id, "suspend", "Under review."));

        await scenario.ServiceableRegionsAsync(
            elsewhere.Id,
            servesAllIndia: false,
            CartScenario.PincodeRule("600"));

        var checkout = await ReadAsync(await shopper.GetAsync(
            new Uri($"/api/v1/store/checkout/{sessionId}", UriKind.Relative),
            Cancellation));

        var cart = checkout.GetProperty("cart");
        var lines = cart.GetProperty("lines").EnumerateArray()
            .ToDictionary(line => line.GetProperty("listingId").GetGuid());

        AssertBlocking(lines[withdrawn.ListingId], "CART_LISTING_UNAVAILABLE");
        AssertBlocking(lines[soldOut.ListingId], "CART_ITEM_OUT_OF_STOCK");
        AssertBlocking(lines[fromSuspended.ListingId], "CART_VENDOR_INACTIVE");
        AssertBlocking(lines[fromElsewhere.ListingId], "CART_NOT_SERVICEABLE");

        // All four in one response rather than the first one the renderer met.
        Assert.Equal(4, lines.Values.Count(line => Blocking(line).Length > 0));

        // The price change is disclosed against its own line and blocks nothing.
        var priceIssue = Assert.Single(
            Issues(lines[repriced.ListingId]),
            issue => issue.GetProperty("code").GetString() == "CART_PRICE_CHANGED");

        Assert.False(priceIssue.GetProperty("isBlocking").GetBoolean());
        Assert.Empty(Blocking(lines[repriced.ListingId]));
        Assert.Equal(449m, lines[repriced.ListingId].GetProperty("unitPrice").GetDecimal());
        Assert.Equal(399m, lines[repriced.ListingId].GetProperty("unitPriceWhenAdded").GetDecimal());

        Assert.False(cart.GetProperty("isReadyForCheckout").GetBoolean());

        // Take the four blocking lines out and the price notice alone lets the sale through.
        foreach (var listingId in new[]
                 {
                     withdrawn.ListingId, soldOut.ListingId, fromSuspended.ListingId, fromElsewhere.ListingId,
                 })
        {
            var lineId = lines[listingId].GetProperty("id").GetGuid();

            await ReadAsync(await shopper.DeleteAsync(
                new Uri($"/api/v1/store/cart/items/{lineId}", UriKind.Relative),
                Cancellation));
        }

        var reviewed = await ReadAsync(await scenario.ReviewAsync(shopper, sessionId));
        var remaining = reviewed.GetProperty("cart");

        Assert.True(remaining.GetProperty("isReadyForCheckout").GetBoolean());

        Assert.Contains(
            Issues(remaining.GetProperty("lines").EnumerateArray().First()),
            issue => issue.GetProperty("code").GetString() == "CART_PRICE_CHANGED");
    }

    /// <summary>Every issue reported against one line.</summary>
    private static List<JsonElement> Issues(JsonElement line)
        => [.. line.GetProperty("issues").EnumerateArray()];

    /// <summary>Only the issues that stop the sale.</summary>
    private static JsonElement[] Blocking(JsonElement line)
        => [.. Issues(line).Where(issue => issue.GetProperty("isBlocking").GetBoolean())];

    /// <summary>Asserts one line carries a given blocking issue, with words a shopper can act on.</summary>
    private static void AssertBlocking(JsonElement line, string code)
    {
        var issue = Assert.Single(Issues(line), candidate => candidate.GetProperty("code").GetString() == code);

        Assert.True(issue.GetProperty("isBlocking").GetBoolean(), $"'{code}' should stop the sale.");
        Assert.False(string.IsNullOrWhiteSpace(issue.GetProperty("message").GetString()));
    }

    /// <summary>
    /// When a basket was last written, as text.
    /// </summary>
    /// <remarks>
    /// Read as text on purpose: the column is a timestamp and the assertion is "unchanged", so what
    /// matters is that the two readings are the same string rather than what they mean.
    /// </remarks>
    private Task<string?> TouchedAtAsync(Guid cartId)
        => Database.ScalarAsync<string>(
            "SELECT updated_at::text FROM carts.carts WHERE id = $1",
            Cancellation,
            cartId);

    /// <summary>How many live or settled holds this cart has taken.</summary>
    private Task<long> HoldsAsync(Guid cartId)
        => Database.CountAsync(
            "SELECT COUNT(*) FROM inventory.stock_reservations WHERE reference_type = 'cart' AND reference_id = $1",
            Cancellation,
            cartId);

    /// <summary>The digest a basket row is found by, computed the way the module computes it.</summary>
    private static string HashOf(string token)
        => Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    /// <summary>A client that keeps no cookie jar, so a test can hold the token itself.</summary>
    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions NoCookieJar
        => new() { HandleCookies = false };

    /// <summary>The token a response issued, read off its <c>Set-Cookie</c> header.</summary>
    /// <remarks>
    /// The attributes are asserted here rather than in a test of their own, because they are what
    /// makes the token a capability nobody else can read: <c>HttpOnly</c> keeps it away from script,
    /// and <c>SameSite=Lax</c> is what survives the return from a payment redirect.
    /// </remarks>
    private static string TokenIn(HttpResponseMessage response)
    {
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith("kh_cart=", StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "samesite=lax",
            cookie.Replace(" ", string.Empty, StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);

        return cookie.Split(';')[0]["kh_cart=".Length..];
    }

    /// <summary>Signs an existing shopper in on a client that is already carrying a guest basket.</summary>
    /// <remarks>
    /// The real OTP flow on the browser that holds the cookie. Signing in on a second client and
    /// copying a header would prove the merge against a request the storefront never makes.
    /// </remarks>
    private async Task SignInShopperOnAsync(HttpClient client, string mobile)
    {
        var start = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/request",
            new { mobile },
            Cancellation);

        start.EnsureSuccessStatusCode();

        await TestSignIn.SignInWithOtpAsync(
            client,
            mobile,
            Factory.Otp.Latest(E164(mobile), Modules.Identity.Domain.OtpPurpose.Login),
            Cancellation);
    }
}
