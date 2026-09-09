using KlaraHome.Contracts.Inventory;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using KlaraHome.Modules.Inventory.Infrastructure.Stock;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The reason this module exists: two callers racing for the last unit produce one sale and one
/// refusal, the ledger and the caches never disagree, and every one of the paths that moves stock
/// can be delivered twice without moving it twice.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these needs the real engine. The oversell defence is a single conditional
/// <c>UPDATE</c> whose correctness is PostgreSQL's row lock and its re-evaluation of the predicate
/// against the already-updated row; <c>FOR UPDATE SKIP LOCKED</c> is only observable while two
/// transactions are alive at once; and a partial unique index is not something a mock has.
/// </para>
/// <para>
/// The holds are taken through <see cref="IStockAvailability"/> rather than through a route, because
/// that is the whole of the surface Cart and Orders have and no endpoint reaches it. What is proved
/// here is what a checkout will do.
/// </para>
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class InventoryConcurrencyTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// Step 11's own acceptance criterion: N callers race for the last unit and exactly one wins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Twelve callers against five units, all started before any of them has finished, so the window
    /// a read-then-write would leave open is genuinely open. Five holds come back and seven refusals
    /// do, and — the assertion that actually matters — the shelf never held more than it had.
    /// </para>
    /// <para>
    /// Each caller gets its own scope, so each has its own context on its own connection. Sharing one
    /// would serialise them through a single connection and prove nothing at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Twelve_callers_racing_for_five_units_produce_five_holds_and_seven_refusals()
    {
        SkipWithoutDocker();

        var offer = await StockedAsync(onHand: 5);

        var cartId = Guid.NewGuid();

        var attempts = Enumerable.Range(0, 12)
            .Select(_ => Guid.NewGuid())
            .Select(lineId => Task.Run(
                async () => await HoldAsync(offer.ListingId, 1, cartId, lineId),
                Cancellation))
            .ToArray();

        var outcomes = await Task.WhenAll(attempts);

        var held = outcomes.Where(id => id is not null).ToList();

        Assert.Equal(5, held.Count);
        Assert.Distinct(held);

        // The invariant, read from the row rather than inferred from the count of successes: the
        // reserved cache is exactly the five units that were taken, and it never exceeded what was
        // on the shelf. A sixth hold would have broken ck_stock_items_reserved_within_hand, so a
        // sixth success could not even have been stored — which is the backstop, not the mechanism.
        var row = await ReadQuantitiesAsync(offer.StockItemId);

        Assert.Equal(5, row.OnHand);
        Assert.Equal(5, row.Reserved);
        Assert.True(row.Reserved <= row.OnHand);

        // And the ledger agrees with the cache. Five holds means five Reservation entries and no
        // more: a refusal writes nothing, which is what "refusal is zero rows returned" means.
        var ledger = await LedgerSumsAsync(offer.StockItemId);

        Assert.Equal(row.OnHand, ledger.OnHand);
        Assert.Equal(row.Reserved, ledger.Reserved);

        Assert.Equal(
            5,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM inventory.stock_ledger_entries "
                + "WHERE stock_item_id = $1 AND reason = 'Reservation'",
                Cancellation,
                offer.StockItemId));
    }

    /// <summary>
    /// A retried checkout gets its own hold back, and the stock is held once.
    /// </summary>
    /// <remarks>
    /// Sequentially first, which is the ordinary retry — a request that timed out at the client and
    /// was sent again. The second call finds the live hold on the same
    /// <c>(referenceType, referenceId, lineReferenceId)</c> triple, returns it, and pushes its expiry
    /// out because the shopper is evidently still there.
    /// </remarks>
    [Fact]
    public async Task A_retried_hold_on_the_same_cart_line_returns_the_same_hold_and_holds_once()
    {
        SkipWithoutDocker();

        var offer = await StockedAsync(onHand: 10);

        var cartId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        var first = await HoldAsync(offer.ListingId, 2, cartId, lineId, minutes: 5);
        var second = await HoldAsync(offer.ListingId, 2, cartId, lineId, minutes: 30);

        Assert.NotNull(first);
        Assert.Equal(first, second);

        var row = await ReadQuantitiesAsync(offer.StockItemId);
        Assert.Equal(2, row.Reserved);

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM inventory.stock_reservations "
                + "WHERE reference_id = $1 AND line_reference_id = $2 AND status = 'Held'",
                Cancellation,
                cartId,
                lineId));

        // The retry pushed the expiry out rather than leaving the shopper on the first call's clock.
        // Compared in the database, so no timestamp crosses a driver's time-zone mapping on the way
        // out and back.
        Assert.True(
            await Database.ScalarAsync<bool>(
                "SELECT expires_at > now() + interval '20 minutes' "
                + "FROM inventory.stock_reservations WHERE id = $1",
                Cancellation,
                first!.Value),
            "The retry should have extended the hold rather than leaving the first call's expiry.");

        // Asking for more than is held replaces the hold rather than adding a second one, so the
        // outcome is one hold of the requested size rather than two of unclear provenance.
        var upsized = await HoldAsync(offer.ListingId, 4, cartId, lineId);

        Assert.NotNull(upsized);
        Assert.NotEqual(first, upsized);

        var after = await ReadQuantitiesAsync(offer.StockItemId);
        Assert.Equal(4, after.Reserved);

        var sums = await LedgerSumsAsync(offer.StockItemId);
        Assert.Equal(after.Reserved, sums.Reserved);
    }

    /// <summary>
    /// The partial unique index holds the line to one live reservation even when the retries arrive
    /// at the same instant, and a collision costs no stock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sequential retry above is answered in application code — the hold is looked up and
    /// returned. Eight simultaneous retries get past that lookup together, because none of them has
    /// committed yet when the others read, and what stops them is
    /// <c>ux_stock_reservations_live</c>.
    /// </para>
    /// <para>
    /// The assertion is deliberately not "seven of them threw". A collision is allowed to be
    /// expensive; it is not allowed to be lossy. One live hold exists, the reserved cache is the
    /// size of that hold, and the ledger agrees with the cache — a caller whose insert was refused
    /// must not have left its units held by nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Eight_simultaneous_retries_of_one_cart_line_leave_one_hold_and_no_stranded_units()
    {
        SkipWithoutDocker();

        var offer = await StockedAsync(onHand: 40);

        var cartId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        var attempts = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(
                async () =>
                {
                    try
                    {
                        return await HoldAsync(offer.ListingId, 3, cartId, lineId);
                    }
                    catch (DbUpdateException)
                    {
                        // The index refused this retry's insert. That is the mechanism working; what
                        // the assertions below check is that it cost nothing.
                        return null;
                    }
                },
                Cancellation))
            .ToArray();

        var outcomes = await Task.WhenAll(attempts);

        Assert.Contains(outcomes, id => id is not null);

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM inventory.stock_reservations "
                + "WHERE reference_id = $1 AND line_reference_id = $2 AND status = 'Held'",
                Cancellation,
                cartId,
                lineId));

        var row = await ReadQuantitiesAsync(offer.StockItemId);
        var sums = await LedgerSumsAsync(offer.StockItemId);

        Assert.Equal(sums.OnHand, row.OnHand);
        Assert.Equal(sums.Reserved, row.Reserved);

        // Three units are held, because one hold of three succeeded. Any more than that is a retry
        // whose reserved bump outlived the insert the index refused.
        Assert.Equal(3, row.Reserved);
    }

    /// <summary>
    /// A redelivered commit or release moves no stock the second time, and says so.
    /// </summary>
    /// <remarks>
    /// The events that drive settlement are delivered at least once, so this is not a nicety. A
    /// second commit that took the units again would sell stock nobody has; a second release that
    /// gave them back again would put sold stock back on the shelf. Both halves are asserted, on two
    /// separate carts, because the two outcomes fail differently.
    /// </remarks>
    [Fact]
    public async Task A_redelivered_settlement_moves_no_stock_and_reports_zero()
    {
        SkipWithoutDocker();

        var offer = await StockedAsync(onHand: 10);

        var committed = Guid.NewGuid();
        var released = Guid.NewGuid();

        Assert.NotNull(await HoldAsync(offer.ListingId, 3, committed, Guid.NewGuid()));
        Assert.NotNull(await HoldAsync(offer.ListingId, 2, released, Guid.NewGuid()));

        var held = await ReadQuantitiesAsync(offer.StockItemId);
        Assert.Equal(5, held.Reserved);

        // The sale happens: three units leave stock and stop being held, in one statement.
        Assert.Equal(1, await SettleAsync(committed, ReservationOutcome.Committed));

        var afterSale = await ReadQuantitiesAsync(offer.StockItemId);
        Assert.Equal(7, afterSale.OnHand);
        Assert.Equal(2, afterSale.Reserved);

        // The redelivery finds no live hold, moves nothing, and reports zero.
        Assert.Equal(0, await SettleAsync(committed, ReservationOutcome.Committed));
        Assert.Equal(0, await SettleAsync(committed, ReservationOutcome.Released));

        var afterReplay = await ReadQuantitiesAsync(offer.StockItemId);
        Assert.Equal(afterSale.OnHand, afterReplay.OnHand);
        Assert.Equal(afterSale.Reserved, afterReplay.Reserved);

        // And the other cart is abandoned: the two units go back on sale, once.
        Assert.Equal(1, await SettleAsync(released, ReservationOutcome.Released));
        Assert.Equal(0, await SettleAsync(released, ReservationOutcome.Released));

        var final = await ReadQuantitiesAsync(offer.StockItemId);
        Assert.Equal(7, final.OnHand);
        Assert.Equal(0, final.Reserved);

        var sums = await LedgerSumsAsync(offer.StockItemId);
        Assert.Equal(final.OnHand, sums.OnHand);
        Assert.Equal(final.Reserved, sums.Reserved);

        // One Sale entry and one Release entry, not two of either. The ledger is where a redelivery
        // that moved nothing but wrote something anyway would show up.
        Assert.Equal(1, await EntriesAsync(offer.StockItemId, "Sale"));
        Assert.Equal(1, await EntriesAsync(offer.StockItemId, "Release"));
    }

    /// <summary>
    /// The sweeper releases a lapsed hold, records it with the note an operator can read, marks the
    /// row expired, and two sweepers running together never release the same hold twice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first half is the behaviour "reservations expire and release stock" names. The second is
    /// what makes running more than one worker safe by construction: the claim is
    /// <c>FOR UPDATE SKIP LOCKED</c>, so a second sweeper polling at the same instant steps over the
    /// batch the first is holding rather than blocking on it or, worse, releasing it again.
    /// </para>
    /// <para>
    /// Two passes started together, each in its own scope on its own connection. A release that
    /// happened twice would show as four units back on sale from a hold of two, or as two Release
    /// entries against one reservation — both are asserted, because the second survives even if the
    /// floor at zero hides the first.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Two_sweepers_running_together_release_a_lapsed_hold_exactly_once()
    {
        SkipWithoutDocker();

        var offer = await StockedAsync(onHand: 6);

        var cartId = Guid.NewGuid();
        var reservationId = await HoldAsync(offer.ListingId, 2, cartId, Guid.NewGuid());

        Assert.NotNull(reservationId);
        Assert.Equal(2, (await ReadQuantitiesAsync(offer.StockItemId)).Reserved);

        // Lapsed. The clock is the one thing here that cannot be driven through the product: a hold
        // is capped at two hours, and a test that waited for one would not be a test.
        await Database.ExecuteAsync(
            "UPDATE inventory.stock_reservations SET expires_at = now() - interval '1 minute' WHERE id = $1",
            Cancellation,
            reservationId.Value);

        var sweeps = Enumerable.Range(0, 2)
            .Select(_ => Task.Run(async () => await SweepAsync(), Cancellation))
            .ToArray();

        var released = await Task.WhenAll(sweeps);

        // Exactly one sweeper accounted for it. The other found the row locked and skipped it, or
        // found it already settled — either way it did not release it a second time.
        Assert.Equal(1, released.Sum());

        var row = await ReadQuantitiesAsync(offer.StockItemId);

        Assert.Equal(6, row.OnHand);
        Assert.Equal(0, row.Reserved);

        var settled = await Database.RowsAsync(
            "SELECT status, settled_at FROM inventory.stock_reservations WHERE id = $1",
            Cancellation,
            reservationId.Value);

        Assert.Equal("Expired", (string?)Assert.Single(settled)["status"]);
        Assert.NotNull(settled[0]["settled_at"]);

        // One Release entry, carrying the note. "Where did four units go at 2 a.m." has an answer.
        var releases = await Database.RowsAsync(
            "SELECT reserved_change, note FROM inventory.stock_ledger_entries "
            + "WHERE stock_item_id = $1 AND reason = 'Release'",
            Cancellation,
            offer.StockItemId);

        var entry = Assert.Single(releases);

        Assert.Equal(-2, (int)entry["reserved_change"]!);
        Assert.Equal("The hold expired before it was settled.", (string?)entry["note"]);

        var sums = await LedgerSumsAsync(offer.StockItemId);

        Assert.Equal(row.OnHand, sums.OnHand);
        Assert.Equal(row.Reserved, sums.Reserved);
    }

    /// <summary>
    /// A movement is enlisted in the caller's transaction: a handler that throws after one leaves no
    /// ledger entry and no quantity change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The raw command is not EF's, and EF enlists only its own. A movement that ran outside the
    /// caller's transaction would commit even when the handler rolled back, and the row would then
    /// disagree with a ledger that never recorded the change — undetectable until the nightly
    /// reconciliation, and unexplainable afterwards.
    /// </para>
    /// <para>
    /// Driven through the ledger service inside a real transaction rather than through an endpoint,
    /// because no endpoint fails on purpose. This is the shape every multi-movement handler in the
    /// module has — the transfer, the goods receipt, the stock take — and it is asserted once here
    /// rather than three times by proxy.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_movement_rolls_back_with_the_transaction_the_handler_threw_out_of()
    {
        SkipWithoutDocker();

        var offer = await StockedAsync(onHand: 9);

        var before = await ReadQuantitiesAsync(offer.StockItemId);
        var entriesBefore = await Database.CountAsync(
            "SELECT COUNT(*) FROM inventory.stock_ledger_entries WHERE stock_item_id = $1",
            Cancellation,
            offer.StockItemId);

        using var scope = Factory.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<StockLedgerService>();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.ExecuteInTransactionAsync(
                async (_, token) =>
                {
                    var item = await context.StockItems
                        .IgnoreQueryFilters()
                        .FirstAsync(candidate => candidate.Id == offer.StockItemId, token);

                    var movement = await ledger.MoveAsync(
                        item,
                        -4,
                        StockMovementReason.Damage,
                        referenceType: "test",
                        referenceId: Guid.NewGuid(),
                        note: "Written off, then the handler failed.",
                        actorId: null,
                        cancellationToken: token);

                    Assert.True(movement.Applied);
                    Assert.Equal(5, movement.QuantityOnHand);

                    throw new InvalidOperationException("The handler failed after the movement.");
                },
                Cancellation));

        Assert.Equal("The handler failed after the movement.", thrown.Message);

        var after = await ReadQuantitiesAsync(offer.StockItemId);

        Assert.Equal(before.OnHand, after.OnHand);
        Assert.Equal(before.Reserved, after.Reserved);

        Assert.Equal(
            entriesBefore,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM inventory.stock_ledger_entries WHERE stock_item_id = $1",
                Cancellation,
                offer.StockItemId));
    }

    /// <summary>
    /// A save that follows a movement in the same unit of work does not fail its concurrency check
    /// against a conflict the movement itself caused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The raw <c>UPDATE</c> bumps <c>xmin</c>, which is what every entity here maps its concurrency
    /// token to. Without reading it back from <c>RETURNING</c> and resetting the tracked entity's
    /// original value, the next <c>SaveChangesAsync</c> on that entity issues
    /// <c>WHERE xmin = &lt;stale&gt;</c>, matches nothing, and throws.
    /// </para>
    /// <para>
    /// Reachable through the product, and this is the path: an adjustment that takes an item below
    /// its reorder level sets <c>low_stock_notified_at</c> on the very entity the movement just
    /// updated, so the handler's save has something to write. Before the resynchronisation existed,
    /// crossing a reorder level by hand was a 500.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_adjustment_that_crosses_the_reorder_level_saves_the_alert_without_a_concurrency_failure()
    {
        SkipWithoutDocker();

        var offer = await StockedAsync(onHand: 10);
        var inventory = new InventoryScenario(offer.Admin, Cancellation);

        await ReadAsync(await inventory.ConfigureAsync(
            offer.StockItemId,
            reorderLevel: 5,
            reorderQuantity: 20));

        // Six units written off takes available to four, which is below the level the seller set.
        // The handler moves the stock and then saves the entity the movement flagged.
        var adjusted = await ReadAsync(
            await inventory.AdjustAsync(offer.StockItemId, -6, "Damage", "Water damage on the pallet."));

        Assert.Equal(4, adjusted.GetProperty("quantityOnHand").GetInt32());
        Assert.True(adjusted.GetProperty("isLow").GetBoolean());

        // The flag was actually persisted, which is the half a swallowed exception would lose.
        Assert.True(
            await Database.ScalarAsync<bool>(
                "SELECT low_stock_notified_at IS NOT NULL FROM inventory.stock_items WHERE id = $1",
                Cancellation,
                offer.StockItemId),
            "Crossing the reorder level should have recorded that the alert was raised.");

        var row = await ReadQuantitiesAsync(offer.StockItemId);
        var sums = await LedgerSumsAsync(offer.StockItemId);

        Assert.Equal(sums.OnHand, row.OnHand);
        Assert.Equal(sums.Reserved, row.Reserved);
    }

    /// <summary>An offer with a shelf and units on it, built through the API.</summary>
    /// <param name="onHand">How many units to put on it.</param>
    private async Task<StockedFixture> StockedAsync(int onHand)
    {
        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var inventory = new InventoryScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();

        var offer = await inventory.StockedAsync(catalogue, taxonomy, seller.Id, onHand);

        return new StockedFixture(admin, offer.ListingId, offer.StockItemId);
    }

    /// <summary>Takes a hold through the published contract, exactly as a checkout does.</summary>
    /// <param name="listingId">The offer.</param>
    /// <param name="quantity">How many units.</param>
    /// <param name="cartId">The cart holding them.</param>
    /// <param name="lineId">The line within it.</param>
    /// <param name="minutes">How long to ask for.</param>
    private Task<Guid?> HoldAsync(Guid listingId, int quantity, Guid cartId, Guid lineId, int minutes = 15)
        => InScopeAsync<IStockAvailability, Guid?>(async (stock, token) =>
            await stock.HoldAsync(
                listingId,
                quantity,
                ReservationReferenceTypes.Cart,
                cartId,
                lineId,
                DateTimeOffset.UtcNow.AddMinutes(minutes),
                token));

    /// <summary>Settles every live hold of one cart, as an order event does.</summary>
    /// <param name="cartId">The cart.</param>
    /// <param name="outcome">Whether the sale happened.</param>
    private Task<int> SettleAsync(Guid cartId, ReservationOutcome outcome)
        => InScopeAsync<IStockAvailability, int>(async (stock, token) =>
            await stock.SettleAsync(ReservationReferenceTypes.Cart, cartId, outcome, token));

    /// <summary>
    /// Runs one pass of the real reservation sweeper over the host under test.
    /// </summary>
    /// <remarks>
    /// Constructed rather than resolved, exactly as <see cref="OutboxDrain"/> constructs the
    /// dispatcher: <c>AddHostedService</c> registers the loop as an <c>IHostedService</c> and not
    /// under its own type. It is given the host's own provider, so the sweep resolves the same
    /// scoped context and ledger service the timer would.
    /// </remarks>
    private async Task<int> SweepAsync()
    {
        var sweeper = new ReservationSweeper(
            Factory.Services,
            Factory.Services.GetRequiredService<IOptionsMonitor<InventoryOptions>>(),
            Factory.Services.GetRequiredService<IClock>(),
            NullLogger<ReservationSweeper>.Instance);

        return await sweeper.SweepOnceAsync(Cancellation);
    }

    /// <summary>The two cached quantities, read from the row rather than through the API.</summary>
    /// <param name="stockItemId">The stock row.</param>
    private async Task<(int OnHand, int Reserved)> ReadQuantitiesAsync(Guid stockItemId)
    {
        var rows = await Database.RowsAsync(
            "SELECT quantity_on_hand, quantity_reserved FROM inventory.stock_items WHERE id = $1",
            Cancellation,
            stockItemId);

        var row = Assert.Single(rows);

        return ((int)row["quantity_on_hand"]!, (int)row["quantity_reserved"]!);
    }

    /// <summary>What the ledger sums to for one stock row — the record of truth behind both caches.</summary>
    /// <param name="stockItemId">The stock row.</param>
    private async Task<(int OnHand, int Reserved)> LedgerSumsAsync(Guid stockItemId)
    {
        var rows = await Database.RowsAsync(
            "SELECT COALESCE(SUM(change), 0)::int AS on_hand, "
            + "COALESCE(SUM(reserved_change), 0)::int AS reserved "
            + "FROM inventory.stock_ledger_entries WHERE stock_item_id = $1",
            Cancellation,
            stockItemId);

        var row = Assert.Single(rows);

        return ((int)row["on_hand"]!, (int)row["reserved"]!);
    }

    /// <summary>How many entries of one reason a stock row has.</summary>
    /// <param name="stockItemId">The stock row.</param>
    /// <param name="reason">The movement reason.</param>
    private Task<long> EntriesAsync(Guid stockItemId, string reason)
        => Database.CountAsync(
            "SELECT COUNT(*) FROM inventory.stock_ledger_entries WHERE stock_item_id = $1 AND reason = $2",
            Cancellation,
            stockItemId,
            reason);

    /// <summary>The offer these tests race against, and the client that built it.</summary>
    /// <param name="Admin">A client signed in as platform staff.</param>
    /// <param name="ListingId">The offer.</param>
    /// <param name="StockItemId">Its one stock row.</param>
    private sealed record StockedFixture(HttpClient Admin, Guid ListingId, Guid StockItemId);
}
