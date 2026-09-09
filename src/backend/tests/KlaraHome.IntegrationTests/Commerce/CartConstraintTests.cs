using System.Globalization;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Carts.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// What the <c>carts</c> schema itself guarantees: the migration, the <c>CHECK</c>s behind every
/// column, the two partial unique indexes the module's whole shape depends on, and the sweeper that
/// writes off what nobody came back to.
/// </summary>
/// <remarks>
/// These are the rows the build sprint could not close without an engine. A partial unique index, a
/// <c>CHECK</c> and a <c>FOR UPDATE SKIP LOCKED</c> claim all behave perfectly in memory and only
/// differ against PostgreSQL.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class CartConstraintTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>The migration applied, it lives in this module's own schema, and it made five tables.</summary>
    /// <remarks>
    /// The re-run half is <c>SchemaMigrationTests</c>'s, which runs the migrator over all eighteen
    /// modules and asserts nothing is applied — a per-module version of that could not see the
    /// collision it is really guarding against.
    /// </remarks>
    [Fact]
    public async Task The_carts_migration_applied_into_its_own_schema()
    {
        SkipWithoutDocker();

        // The module keeps its own history, inside its own schema, and it has been applied.
        Assert.True(
            await Database.CountAsync(
                """SELECT COUNT(*) FROM carts."__ef_migrations_history" """,
                Cancellation) > 0,
            "the carts schema has a migration history table but nothing has been applied into it");

        var tables = (await Database.RowsAsync(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'carts'",
                Cancellation))
            .Select(row => row["table_name"]!.ToString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("carts", tables);
        Assert.Contains("cart_lines", tables);
        Assert.Contains("checkout_sessions", tables);
        Assert.Contains("checkout_shipments", tables);
        Assert.Contains("checkout_placements", tables);
    }

    /// <summary>
    /// Every <c>CHECK</c> in the schema refuses what it was written to refuse.
    /// </summary>
    /// <remarks>
    /// Asserted by writing the bad value straight to the table rather than through the API, because
    /// that is the only way to reach the constraint: the handlers refuse most of these before the
    /// database ever sees them, and the constraint is what stands behind the day one of them stops.
    /// The rows are real ones the product created, so nothing here depends on a hand-made shape.
    /// </remarks>
    [Fact]
    public async Task Every_carts_check_constraint_refuses_what_it_is_meant_to()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 199m);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        var cart = await scenario.AddAsync(shopper, offer, quantity: 2);
        var cartId = cart.GetProperty("id").GetGuid();
        var lineId = cart.GetProperty("lines").EnumerateArray().First().GetProperty("id").GetGuid();

        var sessionId = await scenario.ReadyCheckoutAsync(shopper, address);

        // A quantity of zero is a line nobody meant to keep; removing it is the operation for that.
        await RefusesAsync("UPDATE carts.cart_lines SET quantity = 0 WHERE id = $1", lineId);
        await RefusesAsync("UPDATE carts.cart_lines SET quantity = -1 WHERE id = $1", lineId);

        // A negative price is money flowing the wrong way.
        await RefusesAsync("UPDATE carts.cart_lines SET unit_price_at_add = -0.01 WHERE id = $1", lineId);

        // A negative badge count and a negative reminder count are both derived caches, and a
        // negative one is a bug that would otherwise surface as a header saying "-1 items".
        await RefusesAsync("UPDATE carts.carts SET line_count = -1 WHERE id = $1", cartId);
        await RefusesAsync("UPDATE carts.carts SET reminder_count = -1 WHERE id = $1", cartId);

        // A basket belonging to neither a shopper nor a browser is a row nobody can ever reach, and
        // the only symptom is a cart that quietly disappears.
        await RefusesAsync(
            "UPDATE carts.carts SET customer_id = NULL, anonymous_token_hash = NULL WHERE id = $1",
            cartId);

        // A status outside the enumeration.
        await RefusesAsync("UPDATE carts.carts SET status = 'Sideways' WHERE id = $1", cartId);
        await RefusesAsync("UPDATE carts.checkout_sessions SET status = 'Halfway' WHERE id = $1", sessionId);
        await RefusesAsync("UPDATE carts.checkout_sessions SET payment_method = 'Barter' WHERE id = $1", sessionId);

        // Negative amounts on a checkout, and a promise that ends before it begins.
        await RefusesAsync("UPDATE carts.checkout_sessions SET shipping_total = -1 WHERE id = $1", sessionId);
        await RefusesAsync("UPDATE carts.checkout_sessions SET grand_total = -1 WHERE id = $1", sessionId);

        await RefusesAsync(
            "UPDATE carts.checkout_shipments SET amount = -1 WHERE checkout_session_id = $1",
            sessionId);

        await RefusesAsync(
            "UPDATE carts.checkout_shipments SET tax_amount = -1 WHERE checkout_session_id = $1",
            sessionId);

        await RefusesAsync(
            """
            UPDATE carts.checkout_shipments
               SET promised_min_days = 5, promised_max_days = 4
             WHERE checkout_session_id = $1
            """,
            sessionId);

        await RefusesAsync(
            "UPDATE carts.checkout_shipments SET promised_min_days = -1 WHERE checkout_session_id = $1",
            sessionId);

        // And a placement status outside its own enumeration.
        await ReadAsync(await scenario.PlaceOrderAsync(shopper, sessionId, CartScenario.NewIdempotencyKey("checks")));

        await RefusesAsync(
            "UPDATE carts.checkout_placements SET status = 'Maybe' WHERE checkout_session_id = $1",
            sessionId);
    }

    /// <summary>
    /// The two partial unique indexes behave: one <c>Active</c> cart per customer with converted ones
    /// still in the table, and one open checkout per cart with closed ones not blocking a new attempt.
    /// </summary>
    /// <remarks>
    /// The filter is the whole point of both. Without it the first is "one cart per customer for
    /// ever", which would mean a shopper could never place a second order; and the second is "one
    /// checkout per basket for ever", which would mean a shopper who backed out of paying could never
    /// start again.
    /// </remarks>
    [Fact]
    public async Task The_partial_unique_indexes_allow_a_second_attempt_and_refuse_a_second_live_one()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 199m, stock: 40);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        var firstCart = await scenario.AddAsync(shopper, offer);
        var firstCartId = firstCart.GetProperty("id").GetGuid();

        var firstSession = await scenario.ReadyCheckoutAsync(shopper, address);

        // Opening checkout twice returns the attempt that is already open rather than a second one.
        var reopened = await ReadAsync(await scenario.StartCheckoutAsync(shopper));
        Assert.Equal(firstSession, reopened.GetProperty("id").GetGuid());

        Assert.Equal(1, await SessionsForAsync(firstCartId));

        // A second open session for the same basket, written directly, is refused by the index.
        Assert.Equal(
            "23505",
            await Database.RefusalAsync(
                """
                INSERT INTO carts.checkout_sessions
                    (id, tenant_id, cart_id, customer_id, status, currency_code, payment_method,
                     shipping_total, grand_total, expires_at, created_at)
                SELECT $1, tenant_id, cart_id, customer_id, 'Draft', currency_code, 'Prepaid',
                       0, 0, expires_at, now()
                  FROM carts.checkout_sessions WHERE id = $2
                """,
                Cancellation,
                Guid.CreateVersion7(),
                firstSession));

        // Backing out closes it, and a closed one does not block the next attempt.
        await ReadAsync(await shopper.PostAsync(
            new Uri($"/api/v1/store/checkout/{firstSession}/abandon", UriKind.Relative),
            content: null,
            Cancellation));

        var secondSession = await ReadAsync(await scenario.StartCheckoutAsync(shopper));

        Assert.NotEqual(firstSession, secondSession.GetProperty("id").GetGuid());
        Assert.Equal(2, await SessionsForAsync(firstCartId));

        // Now the cart index. A second Active cart for this shopper, written directly, is refused.
        var customerId = await Database.ScalarAsync<Guid>(
            "SELECT customer_id FROM carts.carts WHERE id = $1",
            Cancellation,
            firstCartId);

        Assert.Equal(
            "23505",
            await Database.RefusalAsync(
                """
                INSERT INTO carts.carts
                    (id, tenant_id, customer_id, status, currency_code, line_count, reminder_count,
                     last_activity_at, expires_at, created_at)
                SELECT $1, tenant_id, customer_id, 'Active', currency_code, 0, 0,
                       last_activity_at, expires_at, now()
                  FROM carts.carts WHERE id = $2
                """,
                Cancellation,
                Guid.CreateVersion7(),
                firstCartId));

        // Convert the basket by buying it, then start another. The converted row stays, because
        // conversion analysis reads it.
        await ReadAsync(await scenario.SetAddressAsync(
            shopper,
            secondSession.GetProperty("id").GetGuid(),
            address.Id));

        await ReadAsync(await scenario.SetCheapestShippingAsync(shopper, secondSession.GetProperty("id").GetGuid()));
        await ReadAsync(await scenario.SetPaymentMethodAsync(shopper, secondSession.GetProperty("id").GetGuid(), "cod"));

        await ReadAsync(await scenario.PlaceOrderAsync(
            shopper,
            secondSession.GetProperty("id").GetGuid(),
            CartScenario.NewIdempotencyKey("indexes")));

        var nextCart = await scenario.AddAsync(shopper, offer, quantity: 2);
        var nextCartId = nextCart.GetProperty("id").GetGuid();

        Assert.NotEqual(firstCartId, nextCartId);

        Assert.Equal(
            "Converted",
            await Database.ScalarAsync<string>(
                "SELECT status FROM carts.carts WHERE id = $1",
                Cancellation,
                firstCartId));

        Assert.Equal(
            2,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM carts.carts WHERE customer_id = $1",
                Cancellation,
                customerId));

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM carts.carts WHERE customer_id = $1 AND status = 'Active'",
                Cancellation,
                customerId));
    }

    /// <summary>
    /// The sweeper marks a stale basket once and not once per sweep, retires an empty one rather than
    /// chasing it, retires an abandoned one past retention, and closes a checkout that lapsed — and
    /// steps over a row another worker is holding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The "once" is what keeps a reminder campaign to one message per basket rather than one per
    /// poll, and it is the difference between a marketing email and a complaint.
    /// </para>
    /// <para>
    /// The <c>FOR UPDATE SKIP LOCKED</c> half is only observable while two transactions are alive at
    /// once, which is why this test opens its own connection and holds a row.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_sweeper_writes_off_each_basket_once_and_steps_over_a_locked_row()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 199m, stock: 60);

        var stale = await CartOfAsync(scenario, offer);
        var locked = await CartOfAsync(scenario, offer);
        var alreadyAbandoned = await CartOfAsync(scenario, offer);
        var empty = await EmptyCartOfAsync(scenario, offer);

        var (withSession, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(withSession);
        await scenario.AddAsync(withSession, offer);
        var sessionId = await scenario.ReadyCheckoutAsync(withSession, address);

        // Age the rows. Nothing else can do it: the clock is the platform's and a test that waited
        // four hours would not be a test.
        await AgeAsync("UPDATE carts.carts SET last_activity_at = now() - interval '5 hours' WHERE id = $1", stale);
        await AgeAsync("UPDATE carts.carts SET last_activity_at = now() - interval '5 hours' WHERE id = $1", locked);
        await AgeAsync("UPDATE carts.carts SET last_activity_at = now() - interval '5 hours' WHERE id = $1", empty);

        await AgeAsync(
            """
            UPDATE carts.carts
               SET status = 'Abandoned',
                   abandoned_at = now() - interval '61 days',
                   last_activity_at = now() - interval '61 days'
             WHERE id = $1
            """,
            alreadyAbandoned);

        await AgeAsync(
            "UPDATE carts.checkout_sessions SET expires_at = now() - interval '1 minute' WHERE id = $1",
            sessionId);

        var sweeper = Sweeper();

        // One worker holds a row while another sweeps. The claim skips it rather than waiting.
        await using (var holder = new NpgsqlConnection(Database.ConnectionString))
        {
            await holder.OpenAsync(Cancellation);

            await using var transaction = await holder.BeginTransactionAsync(Cancellation);

            await using (var hold = new NpgsqlCommand(
                             "SELECT id FROM carts.carts WHERE id = $1 FOR UPDATE",
                             holder,
                             transaction))
            {
                hold.Parameters.Add(new NpgsqlParameter { Value = locked });
                await hold.ExecuteScalarAsync(Cancellation);
            }

            Assert.True(await sweeper.SweepOnceAsync(Cancellation) > 0);

            await transaction.RollbackAsync(Cancellation);
        }

        // A basket left long enough to chase is abandoned and announced.
        Assert.Equal("Abandoned", await StatusOfAsync(stale));

        // The one another worker was holding was stepped over, not blocked on and not lost.
        Assert.Equal("Active", await StatusOfAsync(locked));

        // An empty basket is retired rather than chased: telling somebody they left nothing behind
        // is how a sender gets marked as spam.
        Assert.Equal("Expired", await StatusOfAsync(empty));
        Assert.Equal(0, await AbandonedEventsForAsync(empty));

        // Past retention, so it stops being of interest.
        Assert.Equal("Expired", await StatusOfAsync(alreadyAbandoned));

        // The lapsed checkout is closed, so the shopper's next visit starts cleanly rather than
        // resuming an address they chose last month.
        Assert.Equal(
            "Expired",
            await Database.ScalarAsync<string>(
                "SELECT status FROM carts.checkout_sessions WHERE id = $1",
                Cancellation,
                sessionId));

        // Exactly one announcement for the basket that was abandoned.
        Assert.Equal(1, await AbandonedEventsForAsync(stale));

        var abandonedAt = await Database.ScalarAsync<string>(
            "SELECT abandoned_at::text FROM carts.carts WHERE id = $1",
            Cancellation,
            stale);

        // A second sweep finds it already written off and says nothing more about it.
        await sweeper.SweepOnceAsync(Cancellation);

        Assert.Equal("Abandoned", await StatusOfAsync(stale));
        Assert.Equal(1, await AbandonedEventsForAsync(stale));

        Assert.Equal(
            abandonedAt,
            await Database.ScalarAsync<string>(
                "SELECT abandoned_at::text FROM carts.carts WHERE id = $1",
                Cancellation,
                stale));

        // And now that the lock is gone, the row that was skipped is swept on the next pass.
        Assert.Equal("Abandoned", await StatusOfAsync(locked));
        Assert.Equal(1, await AbandonedEventsForAsync(locked));
    }

    /// <summary>
    /// <c>CartAbandoned</c> and <c>CartConverted</c> are written to the outbox by the transaction
    /// that changed the basket, through this module's own keyed outbox, and reach a handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keying is the part that fails silently. The unkeyed <c>IOutbox</c> registration belongs to
    /// whichever module registered first; enqueuing through it here would add the row to a different
    /// context's change tracker, this module's save would not write it, and the event would be lost
    /// with no error anywhere. A row that exists at all is therefore the proof that the keyed
    /// registration is the one in use.
    /// </para>
    /// <para>
    /// It is drained with the real dispatcher afterwards, because a message nothing can deliver is a
    /// message that would sit in the queue until its attempts ran out.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Cart_abandoned_and_cart_converted_are_written_to_the_keyed_outbox()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 349m, stock: 20);

        // The conversion half: a basket that became an order announces itself.
        var (buyer, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(buyer);

        var bought = await scenario.AddAsync(buyer, offer, quantity: 2);
        var boughtCartId = bought.GetProperty("id").GetGuid();

        var sessionId = await scenario.ReadyCheckoutAsync(buyer, address);

        Assert.Equal(0, await EventsForAsync("CartConverted", boughtCartId));

        var placed = await ReadAsync(await scenario.PlaceOrderAsync(
            buyer,
            sessionId,
            CartScenario.NewIdempotencyKey("converted")));

        Assert.Equal(1, await EventsForAsync("CartConverted", boughtCartId));

        var announcement = Assert.Single(await Database.RowsAsync(
            """
            SELECT payload::text AS payload FROM platform.outbox_messages
            WHERE type LIKE '%CartConverted' AND payload::text LIKE $1
            """,
            Cancellation,
            $"%{boughtCartId}%"));

        // It carries what a consumer needs rather than only an id.
        Assert.Contains(
            placed.GetProperty("orderNumber").GetString()!,
            announcement["payload"]!.ToString()!,
            StringComparison.Ordinal);

        // The abandonment half, from the sweeper's own transaction.
        var abandoned = await CartOfAsync(scenario, offer);

        await AgeAsync(
            "UPDATE carts.carts SET last_activity_at = now() - interval '6 hours' WHERE id = $1",
            abandoned);

        Assert.Equal(0, await EventsForAsync("CartAbandoned", abandoned));

        await Sweeper().SweepOnceAsync(Cancellation);

        Assert.Equal(1, await EventsForAsync("CartAbandoned", abandoned));

        // A sweep that changes nothing announces nothing: the status guard and the enqueue are the
        // same decision, made once.
        await Sweeper().SweepOnceAsync(Cancellation);

        Assert.Equal(1, await EventsForAsync("CartAbandoned", abandoned));

        // Everything queued is deliverable. A handler that threw would leave the message behind.
        await OutboxDrain.RunAsync(Factory, Database, Cancellation);

        Assert.Equal(
            0,
            await Database.CountAsync(
                """
                SELECT COUNT(*) FROM platform.outbox_messages
                WHERE processed_at IS NULL AND (type LIKE '%CartAbandoned' OR type LIKE '%CartConverted')
                  AND payload::text LIKE $1
                """,
                Cancellation,
                $"%{abandoned}%"));
    }

    /// <summary>The sweeper this host registered, resolved rather than reconstructed.</summary>
    /// <remarks>
    /// It is a hosted service and its loop is disabled here, so it is sitting idle: asking it for one
    /// pass runs the same code the worker's timer would, on a schedule the test controls.
    /// </remarks>
    private AbandonedCartSweeper Sweeper()
        => Factory.Services.GetServices<IHostedService>().OfType<AbandonedCartSweeper>().Single();

    /// <summary>A signed-in shopper's basket with one line in it, and its id.</summary>
    private async Task<Guid> CartOfAsync(CartScenario scenario, SellableOffer offer)
    {
        var (shopper, _) = await SignedInShopperAsync();
        var cart = await scenario.AddAsync(shopper, offer);

        return cart.GetProperty("id").GetGuid();
    }

    /// <summary>A basket that was filled and then emptied, which is what "left nothing behind" is.</summary>
    private async Task<Guid> EmptyCartOfAsync(CartScenario scenario, SellableOffer offer)
    {
        var (shopper, _) = await SignedInShopperAsync();
        var cart = await scenario.AddAsync(shopper, offer);
        var cartId = cart.GetProperty("id").GetGuid();

        await ReadAsync(await shopper.DeleteAsync(new Uri("/api/v1/store/cart", UriKind.Relative), Cancellation));

        return cartId;
    }

    /// <summary>Where a basket has got to.</summary>
    private Task<string?> StatusOfAsync(Guid cartId)
        => Database.ScalarAsync<string>(
            "SELECT status FROM carts.carts WHERE id = $1",
            Cancellation,
            cartId);

    /// <summary>How many checkout sessions exist against one basket.</summary>
    private Task<long> SessionsForAsync(Guid cartId)
        => Database.CountAsync(
            "SELECT COUNT(*) FROM carts.checkout_sessions WHERE cart_id = $1",
            Cancellation,
            cartId);

    /// <summary>How many abandonment announcements name one basket.</summary>
    private Task<long> AbandonedEventsForAsync(Guid cartId) => EventsForAsync("CartAbandoned", cartId);

    /// <summary>How many outbox messages of a type name one basket.</summary>
    private Task<long> EventsForAsync(string type, Guid cartId)
        => Database.CountAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"SELECT COUNT(*) FROM platform.outbox_messages WHERE type LIKE '%{type}' AND payload::text LIKE $1"),
            Cancellation,
            $"%{cartId}%");

    /// <summary>Backdates a row, because the clock belongs to the platform.</summary>
    private async Task AgeAsync(string statement, Guid id)
        => Assert.Equal(1, await Database.ExecuteAsync(statement, Cancellation, id));

    /// <summary>Asserts a statement is refused by a <c>CHECK</c>.</summary>
    private async Task RefusesAsync(string statement, Guid id)
        => Assert.Equal("23514", await Database.RefusalAsync(statement, Cancellation, id));
}
