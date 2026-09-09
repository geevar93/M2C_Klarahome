using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 13's other acceptance criterion — duplicate place-order requests create exactly one order —
/// and the two things that stand behind it: the unique index that decides the race, and the
/// compensating release that stops a failed attempt taking stock off sale.
/// </summary>
/// <remarks>
/// Every one of these runs against a real database and the real Ordering module. The whole subject
/// is what happens when two writes meet, and a test that stubbed either half would be asserting its
/// own arrangement of the furniture.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class CheckoutPlacementTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// Four place-order requests carrying one key, sent at once, create exactly one order — and the
    /// losers replay the winner's response rather than making a second.
    /// </summary>
    /// <remarks>
    /// The unique index on <c>(tenant_id, idempotency_key)</c> is the enforcement and this is what
    /// proves it: a check-then-insert would pass a sequential test perfectly and produce two orders
    /// here. A loser is allowed either answer — the winner's stored response, or
    /// <c>ORDER_PLACEMENT_IN_PROGRESS</c> if it arrived while the winner was still running — and
    /// what it may never do is create an order of its own.
    /// </remarks>
    [Fact]
    public async Task Duplicate_place_order_requests_sent_at_once_create_exactly_one_order()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 199m, stock: 40);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        var cart = await scenario.AddAsync(shopper, offer, quantity: 2);
        var cartId = cart.GetProperty("id").GetGuid();

        var sessionId = await scenario.ReadyCheckoutAsync(shopper, address);
        var key = CartScenario.NewIdempotencyKey("race");

        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => scenario.PlaceOrderAsync(shopper, sessionId, key)));

        var accepted = new List<JsonElement>();

        try
        {
            foreach (var attempt in attempts)
            {
                if (attempt.IsSuccessStatusCode)
                {
                    accepted.Add(await ReadAsync(attempt));
                    continue;
                }

                // The only refusal a duplicate may get. Anything else — a second order, a 500, a
                // conflict about the key being reused — is the bug this test exists for.
                await RefusedAsync(attempt, HttpStatusCode.Conflict, "ORDER_PLACEMENT_IN_PROGRESS");
            }
        }
        finally
        {
            foreach (var attempt in attempts)
            {
                attempt.Dispose();
            }
        }

        Assert.NotEmpty(accepted);

        // Every request that was answered with an order was answered with the same one.
        var orderIds = accepted.ConvertAll(response => response.GetProperty("orderId").GetGuid());
        Assert.Single(orderIds.Distinct());

        var orderId = orderIds[0];

        // One row in the database, whatever the wire said.
        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM orders.orders WHERE cart_id = $1",
                Cancellation,
                cartId));

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM carts.checkout_placements WHERE idempotency_key = $1",
                Cancellation,
                key));

        Assert.Equal(
            "Converted",
            await Database.ScalarAsync<string>(
                "SELECT status FROM carts.carts WHERE id = $1",
                Cancellation,
                cartId));
    }

    /// <summary>
    /// The unique index is what decides the race, and the loser replays the winner's stored response
    /// verbatim rather than creating anything.
    /// </summary>
    /// <remarks>
    /// Two halves. The index is asserted to exist and to be unique on the pair, because everything
    /// else in the place-order path is arranged around the assumption that it is; and a sequential
    /// replay of a key that has already succeeded is asserted to be byte-for-byte the first
    /// response, because a "replay" that re-serialised the order would drift from it the first time
    /// a field was added.
    /// </remarks>
    [Fact]
    public async Task The_unique_index_wins_the_race_and_a_replay_returns_the_stored_response()
    {
        SkipWithoutDocker();

        var index = Assert.Single(await Database.RowsAsync(
            """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'carts' AND tablename = 'checkout_placements'
              AND indexdef LIKE '%idempotency_key%'
            """,
            Cancellation));

        var definition = index["indexdef"]!.ToString()!;

        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains("tenant_id", definition, StringComparison.Ordinal);

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 149m);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        await scenario.AddAsync(shopper, offer);

        var sessionId = await scenario.ReadyCheckoutAsync(shopper, address);
        var key = CartScenario.NewIdempotencyKey("replay");

        string first;
        string replayed;

        using (var placed = await scenario.PlaceOrderAsync(shopper, sessionId, key))
        {
            placed.EnsureSuccessStatusCode();
            first = await placed.Content.ReadAsStringAsync(Cancellation);
        }

        // The session is closed now, so a replay that did any work at all would refuse rather than
        // answer — which is precisely what makes this assertion worth making.
        using (var again = await scenario.PlaceOrderAsync(shopper, sessionId, key))
        {
            again.EnsureSuccessStatusCode();
            replayed = await again.Content.ReadAsStringAsync(Cancellation);
        }

        Assert.Equal(first, replayed);

        var stored = await Database.RowsAsync(
            "SELECT status, response::text AS response FROM carts.checkout_placements WHERE idempotency_key = $1",
            Cancellation,
            key);

        var row = Assert.Single(stored);

        Assert.Equal("Succeeded", row["status"]!.ToString());
        Assert.Equal(
            JsonDocument.Parse(first).RootElement.GetProperty("orderNumber").GetString(),
            JsonDocument.Parse(row["response"]!.ToString()!).RootElement.GetProperty("orderNumber").GetString());
    }

    /// <summary>
    /// A key replayed against a different basket is refused with a conflict, never with somebody
    /// else's order.
    /// </summary>
    /// <remarks>
    /// The request hash is what catches it. Answering a client that reused its key with the previous
    /// order would be the worst possible outcome: the shopper would be shown a confirmation for
    /// goods they are not buying, and the basket they are actually holding would never be ordered.
    /// </remarks>
    [Fact]
    public async Task A_key_replayed_against_a_different_basket_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var first = await scenario.OfferAsync(seller, sellingPrice: 149m);
        var second = await scenario.OfferAsync(seller, sellingPrice: 899m);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        await scenario.AddAsync(shopper, first);

        var firstSession = await scenario.ReadyCheckoutAsync(shopper, address);
        var key = CartScenario.NewIdempotencyKey("reuse");

        var placed = await ReadAsync(await scenario.PlaceOrderAsync(shopper, firstSession, key));
        var orderId = placed.GetProperty("orderId").GetGuid();

        // A wholly different basket: the previous one was converted, so this is a new cart, a new
        // session and a different total.
        await scenario.AddAsync(shopper, second, quantity: 2);

        var secondSession = await scenario.ReadyCheckoutAsync(shopper, address);

        var problem = await RefusedAsync(
            await scenario.PlaceOrderAsync(shopper, secondSession, key),
            HttpStatusCode.Conflict,
            "IDEMPOTENCY_KEY_REUSED");

        // Not somebody else's order, and not this one either.
        Assert.DoesNotContain(
            orderId.ToString(),
            problem.GetRawText(),
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM carts.checkout_placements WHERE idempotency_key = $1",
                Cancellation,
                key));

        // The second basket is untouched and can still be ordered — under its own key.
        var recovered = await ReadAsync(await scenario.PlaceOrderAsync(
            shopper,
            secondSession,
            CartScenario.NewIdempotencyKey("reuse-recovered")));

        Assert.NotEqual(orderId, recovered.GetProperty("orderId").GetGuid());
    }

    /// <summary>
    /// A key whose attempt failed may be used again, and the retry produces exactly one order.
    /// </summary>
    /// <remarks>
    /// An idempotency key promises <em>at most one</em> order, and a failure created none. Leaving
    /// the key claimed would mean that a shopper whose card was declined — whose client will send
    /// the key it already has when they press Pay again — is answered with a conflict for ever.
    /// </remarks>
    [Fact]
    public async Task A_key_whose_attempt_failed_may_be_used_again_and_produces_one_order()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 199m, stock: 20);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        var cart = await scenario.AddAsync(shopper, offer, quantity: 2);
        var cartId = cart.GetProperty("id").GetGuid();

        var sessionId = await scenario.ReadyCheckoutAsync(shopper, address, method: "prepaid");
        var key = CartScenario.NewIdempotencyKey("retry");

        try
        {
            // The gateway goes away between the review screen and the button, which is the ordinary
            // shape of this failure.
            Factory.Gateway.IsConfigured = false;

            await RefusedAsync(
                await scenario.PlaceOrderAsync(shopper, sessionId, key),
                HttpStatusCode.ServiceUnavailable,
                "PAYMENT_PROVIDER_UNAVAILABLE");
        }
        finally
        {
            Factory.Gateway.IsConfigured = true;
        }

        var failed = Assert.Single(await Database.RowsAsync(
            "SELECT status, failure_code FROM carts.checkout_placements WHERE idempotency_key = $1",
            Cancellation,
            key));

        Assert.Equal("Failed", failed["status"]!.ToString());
        Assert.Equal("PAYMENT_PROVIDER_UNAVAILABLE", failed["failure_code"]!.ToString());

        // Nothing was created and nothing is held: the shopper is exactly where they were.
        Assert.Equal(0, await OrdersForAsync(cartId));
        Assert.Equal(0, await HeldAsync(cartId));

        Assert.Equal(
            "PaymentSet",
            await Database.ScalarAsync<string>(
                "SELECT status FROM carts.checkout_sessions WHERE id = $1",
                Cancellation,
                sessionId));

        // The same key again, which is what a client that already has one will send.
        var placed = await ReadAsync(await scenario.PlaceOrderAsync(shopper, sessionId, key));

        Assert.False(string.IsNullOrWhiteSpace(placed.GetProperty("orderNumber").GetString()));
        Assert.Equal(1, await OrdersForAsync(cartId));

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM carts.checkout_placements WHERE idempotency_key = $1",
                Cancellation,
                key));
    }

    /// <summary>
    /// A hold that cannot be taken for every line takes none: the lines already held are released
    /// and the shopper is told what sold out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The holds and the cart transaction are not one atomic unit — they are two contexts across a
    /// module boundary — so the compensating release is the only thing standing between a failed
    /// checkout and stock nobody can buy.
    /// </para>
    /// <para>
    /// The failure is staged by closing the second seller's warehouse. That is a state an operator
    /// can produce in one click, and it is a divergence worth knowing about on its own: availability
    /// aggregates every stock row for an offer, while a hold walks only the <em>active</em>
    /// locations, so the basket says "in stock" and the hold refuses.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_hold_that_cannot_cover_every_line_takes_none_and_releases_what_it_took()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var reachable = await scenario.SellerAsync();
        var closing = await scenario.SellerAsync();

        var willHold = await scenario.OfferAsync(reachable, sellingPrice: 199m, stock: 20);
        var willRefuse = await scenario.OfferAsync(closing, sellingPrice: 249m, stock: 20);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        var cart = await scenario.AddAsync(shopper, willHold, quantity: 2);
        var cartId = cart.GetProperty("id").GetGuid();

        await scenario.AddAsync(shopper, willRefuse, quantity: 2);

        var sessionId = await scenario.ReadyCheckoutAsync(shopper, address);

        // The basket still says both lines are in stock, which is what makes the hold the thing
        // under test rather than the validation in front of it.
        await ReadAsync(await scenario.ReviewAsync(shopper, sessionId));

        await ReadAsync(await admin.PutAsJsonAsync(
            $"/api/v1/admin/warehouses/{willRefuse.WarehouseId}",
            new
            {
                name = "Test warehouse",
                pincode = "500034",
                address = (object?)null,
                priority = 0,
                isActive = false,
            },
            Cancellation));

        await RefusedAsync(
            await scenario.PlaceOrderAsync(shopper, sessionId, CartScenario.NewIdempotencyKey("partial")),
            HttpStatusCode.Conflict,
            "CART_ITEM_OUT_OF_STOCK");

        // Every hold this attempt took is back on sale, and no order exists.
        Assert.Equal(0, await HeldAsync(cartId));
        Assert.Equal(0, await OrdersForAsync(cartId));

        Assert.Equal(
            0,
            await Database.ScalarAsync<int>(
                "SELECT quantity_reserved FROM inventory.stock_items WHERE id = $1",
                Cancellation,
                willHold.StockItemId));


        // The basket is still the shopper's, and the session is back in their hands.
        Assert.Equal(
            "Active",
            await Database.ScalarAsync<string>(
                "SELECT status FROM carts.carts WHERE id = $1",
                Cancellation,
                cartId));

        Assert.Equal(
            "PaymentSet",
            await Database.ScalarAsync<string>(
                "SELECT status FROM carts.checkout_sessions WHERE id = $1",
                Cancellation,
                sessionId));
    }

    /// <summary>An ordering refusal after the hold releases every unit the attempt took.</summary>
    /// <remarks>
    /// The other half of the compensation, and the one that actually happens in production: the
    /// stock was there, the hold succeeded, and it was the gateway that would not open a collection.
    /// Inventory's sweeper would free the units eventually, but "eventually" is the wrong answer
    /// while the same shopper is about to press the button again.
    /// </remarks>
    [Fact]
    public async Task An_ordering_refusal_after_the_hold_releases_every_unit_it_took()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 199m, stock: 20);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        var cart = await scenario.AddAsync(shopper, offer, quantity: 3);
        var cartId = cart.GetProperty("id").GetGuid();

        var sessionId = await scenario.ReadyCheckoutAsync(shopper, address, method: "prepaid");

        try
        {
            Factory.Gateway.IsConfigured = false;

            await RefusedAsync(
                await scenario.PlaceOrderAsync(shopper, sessionId, CartScenario.NewIdempotencyKey("refused")),
                HttpStatusCode.ServiceUnavailable,
                "PAYMENT_PROVIDER_UNAVAILABLE");
        }
        finally
        {
            Factory.Gateway.IsConfigured = true;
        }

        Assert.Equal(0, await HeldAsync(cartId));
        Assert.Equal(0, await OrdersForAsync(cartId));

        Assert.Equal(
            0,
            await Database.ScalarAsync<int>(
                "SELECT quantity_reserved FROM inventory.stock_items WHERE id = $1",
                Cancellation,
                offer.StockItemId));

        // The hold was really taken and really given back, rather than never taken at all.
        Assert.Equal(
            "Released",
            await Database.ScalarAsync<string>(
                "SELECT status FROM inventory.stock_reservations WHERE reference_type = 'cart' AND reference_id = $1",
                Cancellation,
                cartId));

        // And the whole basket is buyable again on the retry.
        var placed = await ReadAsync(await scenario.PlaceOrderAsync(
            shopper,
            sessionId,
            CartScenario.NewIdempotencyKey("refused-retry")));

        Assert.False(string.IsNullOrWhiteSpace(placed.GetProperty("orderNumber").GetString()));
        Assert.Equal(1, await OrdersForAsync(cartId));
    }

    /// <summary>How many live holds this cart is keeping off sale.</summary>
    private Task<long> HeldAsync(Guid cartId)
        => Database.CountAsync(
            """
            SELECT COUNT(*) FROM inventory.stock_reservations
            WHERE reference_type = 'cart' AND reference_id = $1 AND status = 'Held'
            """,
            Cancellation,
            cartId);

    /// <summary>How many orders exist for one basket.</summary>
    private Task<long> OrdersForAsync(Guid cartId)
        => Database.CountAsync(
            "SELECT COUNT(*) FROM orders.orders WHERE cart_id = $1",
            Cancellation,
            cartId);
}
