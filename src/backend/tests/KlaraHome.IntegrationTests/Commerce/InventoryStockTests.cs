using System.Net;
using System.Text.Json;
using KlaraHome.Contracts.Inventory;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Inventory.Infrastructure;
using KlaraHome.Modules.Inventory.Infrastructure.Stock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// What the ledger is for: the two quantity caches are reconcilable against it after anything the
/// product can do, every document that moves stock moves it in one transaction, and the events that
/// tell the rest of the platform about a movement are written with it.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class InventoryStockTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// After an arbitrary sequence of everything that moves stock, both caches still equal their
    /// ledger sums.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the invariant the whole module rests on and the one the nightly job asserts:
    /// <c>quantity_on_hand = Σ(change)</c> and <c>quantity_reserved = Σ(reserved_change)</c>. The
    /// second column exists precisely so the statement is checkable for holds as well as for stock,
    /// and without it a lost hold would be invisible.
    /// </para>
    /// <para>
    /// The sequence is deliberately not tidy: a goods receipt, two holds, one committed and one
    /// released, one left to expire and swept, a hand-posted write-off, a transfer out to a second
    /// location and a correction from a stock take. Each of those is a different code path into
    /// <c>StockLedgerService</c>, and the point is that no one of them can be the odd one out.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Both_caches_equal_their_ledger_sums_after_receipts_holds_settlements_transfers_and_corrections()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var inventory = new InventoryScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();

        var main = await inventory.WarehouseAsync(seller.Id, priority: 0);
        var overflow = await inventory.WarehouseAsync(seller.Id, priority: 9);

        var offer = await inventory.StockedAsync(catalogue, taxonomy, seller.Id, warehouseId: main);
        var secondShelf = await inventory.StockItemAsync(offer.ListingId, overflow);

        // Inbound, through the paperwork: a supplier, an order, and the receipt that books it in.
        var supplierId = await inventory.SupplierAsync(seller.Id);

        var (orderId, lineIds) = await inventory.PurchaseOrderAsync(
            supplierId,
            main,
            [(offer.ListingId, 40)]);

        await ReadAsync(await inventory.ReceiveAsync(
            orderId,
            InventoryScenario.ReceiptLine(lineIds[0], accepted: 40)));

        // Two holds. One becomes a sale, one is given up.
        var sold = Guid.NewGuid();
        var abandoned = Guid.NewGuid();
        var lapsed = Guid.NewGuid();

        Assert.NotNull(await HoldAsync(offer.ListingId, 6, sold));
        Assert.NotNull(await HoldAsync(offer.ListingId, 4, abandoned));

        var lapsedId = await HoldAsync(offer.ListingId, 3, lapsed);
        Assert.NotNull(lapsedId);

        Assert.Equal(1, await SettleAsync(sold, ReservationOutcome.Committed));
        Assert.Equal(1, await SettleAsync(abandoned, ReservationOutcome.Released));

        // And one nobody settles, put back by the sweeper.
        await Database.ExecuteAsync(
            "UPDATE inventory.stock_reservations SET expires_at = now() - interval '1 minute' WHERE id = $1",
            Cancellation,
            lapsedId!.Value);

        // The sweeper runs over the whole database, and every suite in this collection shares one,
        // so the number of holds it puts back is not this test's to predict — a cart or checkout
        // test's lapsed hold is swept by the same pass. What belongs to this test is its own
        // reservation, so that is what is asserted.
        Assert.True(await SweepAsync() >= 1);

        var lapsedRow = await Database.RowsAsync(
            "SELECT status FROM inventory.stock_reservations WHERE id = $1",
            Cancellation,
            lapsedId.Value);

        Assert.Equal("Expired", (string?)Assert.Single(lapsedRow)["status"]);

        // A write-off by hand, and a transfer to the overflow shelf.
        await ReadAsync(await inventory.AdjustAsync(offer.StockItemId, -2, "Damage", "Crushed in transit."));

        await ReadAsync(await inventory.TransferAsync(offer.ListingId, main, overflow, 5, "Making room."));

        // A recount that finds one fewer than the book says.
        var take = await ReadAsync(await inventory.StockTakeAsync(overflow, [secondShelf]));
        var takeId = take.GetProperty("id").GetGuid();

        await ReadAsync(await inventory.CountAsync(takeId, (secondShelf, 4)));
        await ReadAsync(await inventory.SubmitStockTakeAsync(takeId));

        // Forty received, six sold, two written off, five moved out: twenty-seven on the main shelf,
        // and four on the overflow one after the recount corrected it down from five.
        var mainRow = await ReadQuantitiesAsync(offer.StockItemId);
        var overflowRow = await ReadQuantitiesAsync(secondShelf);

        Assert.Equal(27, mainRow.OnHand);
        Assert.Equal(0, mainRow.Reserved);
        Assert.Equal(4, overflowRow.OnHand);

        // The assertion the module exists for: neither cache has drifted from the ledger behind it.
        foreach (var stockItemId in new[] { offer.StockItemId, secondShelf })
        {
            var cached = await ReadQuantitiesAsync(stockItemId);
            var summed = await LedgerSumsAsync(stockItemId);

            Assert.Equal(summed.OnHand, cached.OnHand);
            Assert.Equal(summed.Reserved, cached.Reserved);
        }

        // And the job that would have found a drift finds none on these two rows.
        var drifted = await ReconcileAsync();

        Assert.DoesNotContain(drifted, drift => drift.StockItemId == offer.StockItemId);
        Assert.DoesNotContain(drifted, drift => drift.StockItemId == secondShelf);
    }

    /// <summary>
    /// A goods receipt writes the GRN, the ledger entries and the order's status advance together,
    /// and a receipt that is refused part-way leaves none of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three cannot be separated. A receipt whose stock movement failed shows units nobody has; a
    /// movement whose receipt failed shows units nobody ordered; and an order that never advanced can
    /// be received against twice. Neither is recoverable from the other end.
    /// </para>
    /// <para>
    /// The refusal is put on the <em>second</em> line on purpose. A refusal on the first proves
    /// nothing about atomicity, because nothing had happened yet.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_goods_receipt_writes_the_grn_the_ledger_and_the_status_together_or_not_at_all()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var inventory = new InventoryScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();

        var warehouseId = await inventory.WarehouseAsync(seller.Id);
        var first = await inventory.StockedAsync(catalogue, taxonomy, seller.Id, warehouseId: warehouseId);
        var second = await inventory.StockedAsync(catalogue, taxonomy, seller.Id, warehouseId: warehouseId);

        var supplierId = await inventory.SupplierAsync(seller.Id);

        var (orderId, lineIds) = await inventory.PurchaseOrderAsync(
            supplierId,
            warehouseId,
            [(first.ListingId, 12), (second.ListingId, 8)]);

        // A receipt whose second line names a line that is not on this order. Nothing may survive it
        // — not the first line's units, not a ledger entry, not the order's advance.
        await RefusedAsync(
            await inventory.ReceiveAsync(
                orderId,
                InventoryScenario.ReceiptLine(lineIds[0], accepted: 12),
                InventoryScenario.ReceiptLine(Guid.NewGuid(), accepted: 8)),
            HttpStatusCode.NotFound,
            "INVENTORY_NOT_FOUND");

        Assert.Equal(0, (await ReadQuantitiesAsync(first.StockItemId)).OnHand);

        Assert.Equal(
            0,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM inventory.stock_ledger_entries "
                + "WHERE stock_item_id = $1 AND reason = 'Purchase'",
                Cancellation,
                first.StockItemId));

        Assert.Equal(0, await ReceiptsAgainstAsync(orderId));

        var stalled = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/purchase-orders/{orderId}", UriKind.Relative),
            Cancellation));

        Assert.Equal("Submitted", stalled.GetProperty("status").GetString());
        Assert.All(
            stalled.GetProperty("lines").EnumerateArray(),
            line => Assert.Equal(0, line.GetProperty("quantityReceived").GetInt32()));

        // The same receipt, with the line it should have named. Now all three land together.
        var receipt = await ReadAsync(await inventory.ReceiveAsync(
            orderId,
            InventoryScenario.ReceiptLine(lineIds[0], accepted: 12),
            InventoryScenario.ReceiptLine(lineIds[1], accepted: 6, rejected: 2, rejectionReason: "Damp cartons.")));

        Assert.Equal("Posted", receipt.GetProperty("status").GetString());
        Assert.StartsWith("GRN-", receipt.GetProperty("number").GetString(), StringComparison.Ordinal);

        Assert.Equal(12, (await ReadQuantitiesAsync(first.StockItemId)).OnHand);

        // Rejected units are recorded and deliberately not booked in: they never reached the shelf.
        Assert.Equal(6, (await ReadQuantitiesAsync(second.StockItemId)).OnHand);

        var advanced = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/purchase-orders/{orderId}", UriKind.Relative),
            Cancellation));

        Assert.Equal("PartiallyReceived", advanced.GetProperty("status").GetString());

        // The ledger entries carry the receipt as their reference, so "what did that GRN do to
        // stock" is a query rather than a reconstruction.
        var entries = await Database.RowsAsync(
            "SELECT stock_item_id, change FROM inventory.stock_ledger_entries "
            + "WHERE reference_type = 'goods_receipt' AND reference_id = $1",
            Cancellation,
            receipt.GetProperty("id").GetGuid());

        Assert.Equal(2, entries.Count);
        Assert.Equal(18, entries.Sum(entry => (int)entry["change"]!));

        foreach (var stockItemId in new[] { first.StockItemId, second.StockItemId })
        {
            var cached = await ReadQuantitiesAsync(stockItemId);
            var summed = await LedgerSumsAsync(stockItemId);

            Assert.Equal(summed.OnHand, cached.OnHand);
        }
    }

    /// <summary>
    /// Submitting a stock take posts one correction per non-zero variance, leaves uncounted rows
    /// alone, and reports rather than forces a downward correction the shelf can no longer absorb.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Nobody counted this shelf" and "this shelf is empty" are different facts. Posting the first
    /// as the second would write off stock that is simply still on it, which is why an uncounted row
    /// produces nothing at all rather than a correction to zero.
    /// </para>
    /// <para>
    /// The refused row is the interesting one. The book figures are frozen when counting opens, so a
    /// sheet counted on Tuesday and submitted on Thursday can carry a variance the shelf has since
    /// sold; forcing it would drive on hand negative, and the recount is stale anyway. It is reported
    /// in the audit record instead, by SKU, so somebody can go and count it again.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Submitting_a_stock_take_corrects_the_counted_rows_and_reports_the_one_it_cannot()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var inventory = new InventoryScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();
        var warehouseId = await inventory.WarehouseAsync(seller.Id);

        var over = await inventory.StockedAsync(catalogue, taxonomy, seller.Id, 10, warehouseId);
        var under = await inventory.StockedAsync(catalogue, taxonomy, seller.Id, 10, warehouseId);
        var uncounted = await inventory.StockedAsync(catalogue, taxonomy, seller.Id, 10, warehouseId);
        var stale = await inventory.StockedAsync(catalogue, taxonomy, seller.Id, 10, warehouseId);

        var take = await ReadAsync(await inventory.StockTakeAsync(
            warehouseId,
            [over.StockItemId, under.StockItemId, uncounted.StockItemId, stale.StockItemId]));

        var takeId = take.GetProperty("id").GetGuid();

        Assert.Equal("Counting", take.GetProperty("status").GetString());
        Assert.Equal(4, take.GetProperty("lines").GetArrayLength());

        Assert.All(
            take.GetProperty("lines").EnumerateArray(),
            line =>
            {
                Assert.Equal(10, line.GetProperty("expectedQuantity").GetInt32());
                Assert.Equal(JsonValueKind.Null, line.GetProperty("countedQuantity").ValueKind);
            });

        // Between the freeze and the submission, the stale row sells almost everything it had. Its
        // variance is now bigger than the shelf.
        await ReadAsync(await inventory.AdjustAsync(stale.StockItemId, -9, "Damage", "Flood."));

        await ReadAsync(await inventory.CountAsync(
            takeId,
            (over.StockItemId, 13),
            (under.StockItemId, 7),
            (stale.StockItemId, 3)));

        var submitted = await ReadAsync(await inventory.SubmitStockTakeAsync(takeId));

        Assert.Equal("Submitted", submitted.GetProperty("status").GetString());

        // Three counted, three variances, two of them postable.
        Assert.Equal(13, (await ReadQuantitiesAsync(over.StockItemId)).OnHand);
        Assert.Equal(7, (await ReadQuantitiesAsync(under.StockItemId)).OnHand);

        Assert.Equal(1, await CorrectionsAsync(over.StockItemId));
        Assert.Equal(1, await CorrectionsAsync(under.StockItemId));

        // The uncounted row was left entirely alone: no correction, no movement, no change.
        Assert.Equal(0, await CorrectionsAsync(uncounted.StockItemId));
        Assert.Equal(10, (await ReadQuantitiesAsync(uncounted.StockItemId)).OnHand);

        // The stale row was reported, not forced. Its shelf still holds the one unit it had.
        Assert.Equal(0, await CorrectionsAsync(stale.StockItemId));
        Assert.Equal(1, (await ReadQuantitiesAsync(stale.StockItemId)).OnHand);

        // And somebody can find out which row it was, by SKU, from the audit record of the
        // submission rather than from a log line.
        var audited = await Database.ScalarAsync<string>(
            "SELECT \"after\"::text FROM platform.audit_logs "
            + "WHERE action = 'inventory.stock-take.submitted' AND entity_id = $1",
            Cancellation,
            takeId.ToString());

        Assert.NotNull(audited);
        Assert.Contains(stale.Sku, audited, StringComparison.Ordinal);
        Assert.DoesNotContain(over.Sku, audited, StringComparison.Ordinal);

        foreach (var stockItemId in new[]
                 {
                     over.StockItemId, under.StockItemId, uncounted.StockItemId, stale.StockItemId,
                 })
        {
            var cached = await ReadQuantitiesAsync(stockItemId);
            var summed = await LedgerSumsAsync(stockItemId);

            Assert.Equal(summed.OnHand, cached.OnHand);
        }
    }

    /// <summary>
    /// A transfer writes both legs under one reference id, or writes neither.
    /// </summary>
    /// <remarks>
    /// Two entries rather than one, because the units leave one shelf and arrive on another and a
    /// single entry could not say both; one reference id, because a ledger that lists them separately
    /// still has to make them recognisable as one movement. A source that cannot supply the quantity
    /// refuses the whole thing — stock in flight that exists nowhere is the worst outcome available
    /// here, and the outbound leg is inside the same transaction as the inbound one so that it cannot
    /// happen.
    /// </remarks>
    [Fact]
    public async Task A_transfer_writes_both_legs_under_one_reference_or_neither()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var inventory = new InventoryScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();

        var main = await inventory.WarehouseAsync(seller.Id, priority: 0);
        var overflow = await inventory.WarehouseAsync(seller.Id, priority: 5);

        var offer = await inventory.StockedAsync(catalogue, taxonomy, seller.Id, 8, main);
        var destination = await inventory.StockItemAsync(offer.ListingId, overflow);

        // More than the source shelf holds. Neither leg is written.
        await RefusedAsync(
            await inventory.TransferAsync(offer.ListingId, main, overflow, 20),
            HttpStatusCode.Conflict,
            "INVENTORY_INSUFFICIENT_STOCK");

        Assert.Equal(8, (await ReadQuantitiesAsync(offer.StockItemId)).OnHand);
        Assert.Equal(0, (await ReadQuantitiesAsync(destination)).OnHand);

        Assert.Equal(
            0,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM inventory.stock_ledger_entries WHERE reference_type = 'transfer' "
                + "AND stock_item_id IN ($1, $2)",
                Cancellation,
                offer.StockItemId,
                destination));

        // A transfer between one location and itself is refused before anything is attempted.
        await RefusedAsync(
            await inventory.TransferAsync(offer.ListingId, main, main, 1),
            HttpStatusCode.UnprocessableEntity,
            "INVENTORY_SAME_WAREHOUSE");

        // And the transfer that fits writes exactly two entries, sharing one reference.
        var moved = await ReadAsync(await inventory.TransferAsync(
            offer.ListingId,
            main,
            overflow,
            3,
            "Rebalancing before the sale."));

        Assert.Equal(2, moved.GetArrayLength());

        Assert.Equal(5, (await ReadQuantitiesAsync(offer.StockItemId)).OnHand);
        Assert.Equal(3, (await ReadQuantitiesAsync(destination)).OnHand);

        var legs = await Database.RowsAsync(
            "SELECT reference_id, reason, change FROM inventory.stock_ledger_entries "
            + "WHERE reference_type = 'transfer' AND stock_item_id IN ($1, $2)",
            Cancellation,
            offer.StockItemId,
            destination);

        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(leg => (Guid)leg["reference_id"]!).Distinct());

        Assert.Contains(legs, leg => (string?)leg["reason"] == "TransferOut" && (int)leg["change"]! == -3);
        Assert.Contains(legs, leg => (string?)leg["reason"] == "TransferIn" && (int)leg["change"]! == 3);

        // A transfer to a location that has never stocked this offer is refused rather than opening
        // a row nobody asked for: the destination has to be a place somebody chose.
        var elsewhere = await inventory.WarehouseAsync(seller.Id, priority: 7);

        await RefusedAsync(
            await inventory.TransferAsync(offer.ListingId, main, elsewhere, 1),
            HttpStatusCode.NotFound,
            "INVENTORY_NOT_FOUND");
    }

    /// <summary>
    /// The reconciliation job reports a cache that has been tampered with, reports nothing when
    /// everything agrees, and catches a cached quantity with no ledger behind it at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The last case is the reason the comparison is a left join. A stock row whose quantity no
    /// movement explains is precisely the drift worth catching — it is what a seeder that wrote the
    /// number, or a repair script that "fixed" a count, leaves behind — and an inner join would drop
    /// it silently.
    /// </para>
    /// <para>
    /// The job reports and does not repair, which is asserted too: a cache it had corrected would be
    /// a cache whose evidence it had destroyed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_reconciliation_job_reports_drift_including_a_cache_with_no_ledger_behind_it()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var inventory = new InventoryScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();

        var moved = await inventory.StockedAsync(catalogue, taxonomy, seller.Id, 7);
        var untouched = await inventory.StockedAsync(catalogue, taxonomy, seller.Id);

        // Everything agrees, so neither row is reported.
        var clean = await ReconcileAsync();

        Assert.DoesNotContain(clean, drift => drift.StockItemId == moved.StockItemId);
        Assert.DoesNotContain(clean, drift => drift.StockItemId == untouched.StockItemId);

        // A cache tampered with behind the ledger's back — the shape a repair script leaves.
        await Database.ExecuteAsync(
            "UPDATE inventory.stock_items SET quantity_on_hand = quantity_on_hand + 4 WHERE id = $1",
            Cancellation,
            moved.StockItemId);

        // And a row that has never moved at all, given a quantity from nowhere.
        await Database.ExecuteAsync(
            "UPDATE inventory.stock_items SET quantity_on_hand = 3 WHERE id = $1",
            Cancellation,
            untouched.StockItemId);

        var drifted = await ReconcileAsync();

        var tampered = Assert.Single(drifted, drift => drift.StockItemId == moved.StockItemId);

        Assert.Equal(11, tampered.CachedOnHand);
        Assert.Equal(7, tampered.LedgerOnHand);
        Assert.Equal(moved.Sku, tampered.Sku);

        var invented = Assert.Single(drifted, drift => drift.StockItemId == untouched.StockItemId);

        Assert.Equal(3, invented.CachedOnHand);
        Assert.Equal(0, invented.LedgerOnHand);

        // It reported and did not repair. The evidence of how the row got that way is still there.
        Assert.Equal(11, (await ReadQuantitiesAsync(moved.StockItemId)).OnHand);

        // Put the shared database back the way the product left it, so the next test that reconciles
        // is not reading this one's mess.
        await Database.ExecuteAsync(
            "UPDATE inventory.stock_items SET quantity_on_hand = $2 WHERE id = $1",
            Cancellation,
            moved.StockItemId,
            7);

        await Database.ExecuteAsync(
            "UPDATE inventory.stock_items SET quantity_on_hand = 0 WHERE id = $1",
            Cancellation,
            untouched.StockItemId);
    }

    /// <summary>
    /// An offer going live opens a stock row at zero in the seller's highest-priority active
    /// location, a redelivery opens no second one, and a seller with nowhere to put it is logged
    /// rather than thrown at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A listing that goes live with no stock row is an offer whose availability nobody can answer:
    /// the contract reports it untracked, the storefront refuses to sell it, and the seller has to
    /// notice. Zero rather than one, deliberately — opening a row does not invent stock, it makes the
    /// offer countable.
    /// </para>
    /// <para>
    /// Delivery is at-least-once, so the redelivery half is not optional; the row is put back on the
    /// queue exactly as a broker would replay it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_published_offer_opens_a_stock_row_at_zero_in_the_sellers_best_location_and_only_once()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var inventory = new InventoryScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();

        // Two locations, and the one the allocator would walk first is not the one created first.
        var distant = await inventory.WarehouseAsync(seller.Id, priority: 9);
        var nearest = await inventory.WarehouseAsync(seller.Id, priority: 1);

        var product = await catalogue.DraftAsync(taxonomy);
        await ReadAsync(await catalogue.ActivateVariantAsync(product.VariantId));
        await catalogue.PublishAsync(product.Id);

        var listingId = await catalogue.OfferAsync(seller.Id, product.VariantId, 799m);

        // Nothing yet: the API does not dispatch its own outbox.
        Assert.Equal(0, await StockRowsForAsync(listingId));

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);

        var opened = await Database.RowsAsync(
            "SELECT warehouse_id, quantity_on_hand, quantity_reserved, sku, vendor_id "
            + "FROM inventory.stock_items WHERE listing_id = $1",
            Cancellation,
            listingId);

        var row = Assert.Single(opened);

        Assert.Equal(nearest, (Guid)row["warehouse_id"]!);
        Assert.NotEqual(distant, (Guid)row["warehouse_id"]!);
        Assert.Equal(0, (int)row["quantity_on_hand"]!);
        Assert.Equal(0, (int)row["quantity_reserved"]!);
        Assert.Equal(seller.Id, (Guid)row["vendor_id"]!);
        Assert.False(string.IsNullOrWhiteSpace((string?)row["sku"]));

        // Opening a row does not invent a movement either: the ledger for it is empty.
        Assert.Equal(
            0,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM inventory.stock_ledger_entries WHERE stock_item_id = $1",
                Cancellation,
                await Database.ScalarAsync<Guid>(
                    "SELECT id FROM inventory.stock_items WHERE listing_id = $1",
                    Cancellation,
                    listingId)));

        // The redelivery, exactly as an at-least-once broker would replay it.
        Assert.Equal(
            1,
            await Database.ExecuteAsync(
                "UPDATE platform.outbox_messages SET processed_at = NULL, attempts = 0 "
                + "WHERE type LIKE '%ListingPublished%' AND payload::text LIKE $1",
                Cancellation,
                $"%{listingId}%"));

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);

        Assert.Equal(1, await StockRowsForAsync(listingId));
    }

    /// <summary>
    /// A seller with nowhere to put their goods gets a log line rather than a poisoned outbox
    /// message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Throwing would be worse than useless. The dispatcher retries a failed handler up to its
    /// budget and then leaves the message alone for ever, so one seller who has not told us where
    /// their warehouse is would stall nothing but themselves — until the message ahead of somebody
    /// else's in the same batch is theirs. The handler records that it could not track the offer and
    /// lets the message be marked done.
    /// </para>
    /// <para>
    /// Every active platform location is closed for the length of this test and reopened afterwards,
    /// by id. The handler falls back to a platform warehouse when a seller has none, so leaving one
    /// open would put the row somewhere and prove the wrong thing. The collection runs serially, so
    /// no other test is looking at them while this one has them shut.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_published_offer_for_a_seller_with_no_location_is_logged_rather_than_thrown_at()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();

        var product = await catalogue.DraftAsync(taxonomy);
        await ReadAsync(await catalogue.ActivateVariantAsync(product.VariantId));
        await catalogue.PublishAsync(product.Id);

        var listingId = await catalogue.OfferAsync(seller.Id, product.VariantId, 749m);

        // Closed by id rather than by predicate, and reopened by the same ids: a blanket
        // "reopen every platform location" would reopen one some other test had deliberately shut.
        var closed = (await Database.RowsAsync(
                "SELECT id FROM inventory.warehouses WHERE vendor_id IS NULL AND is_active",
                Cancellation))
            .Select(row => (Guid)row["id"]!)
            .ToArray();

        await Database.ExecuteAsync(
            "UPDATE inventory.warehouses SET is_active = false WHERE id = ANY($1)",
            Cancellation,
            closed);

        try
        {
            // No exception, and the queue drains: the message is not left to be retried for ever.
            await OutboxDrain.RunAsync(Factory, Database, Cancellation);

            Assert.Equal(0, await StockRowsForAsync(listingId));

            Assert.Equal(
                0,
                await Database.CountAsync(
                    "SELECT COUNT(*) FROM platform.outbox_messages "
                    + "WHERE processed_at IS NULL AND payload::text LIKE $1",
                    Cancellation,
                    $"%{listingId}%"));
        }
        finally
        {
            await Database.ExecuteAsync(
                "UPDATE inventory.warehouses SET is_active = true WHERE id = ANY($1)",
                Cancellation,
                closed);
        }
    }

    /// <summary>
    /// Every movement announces itself in the outbox in the transaction that made it, and the
    /// low-stock alert fires on the crossing rather than on every sale beneath the level.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first half is what stops the search index advertising stock that was rolled back: a
    /// refused movement writes no event, because the event and the ledger entry are enqueued in the
    /// same unit of work.
    /// </para>
    /// <para>
    /// The second is the difference between an alert somebody reads and one they filter out. One
    /// slow-selling item below its threshold would otherwise alert its seller on every single sale,
    /// and the crossing that mattered would be lost among them. It rearms once the item is
    /// replenished above the level, which is asserted by making it cross twice.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Every_movement_is_announced_and_the_low_stock_alert_fires_once_per_crossing()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var inventory = new InventoryScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();
        var offer = await inventory.StockedAsync(catalogue, taxonomy, seller.Id, 20);

        await ReadAsync(await inventory.ConfigureAsync(offer.StockItemId, reorderLevel: 5, reorderQuantity: 25));

        var announced = await EventsAboutAsync("StockLevelChanged", offer.ListingId);

        // Below the level for the first time: one crossing, one alert.
        await ReadAsync(await inventory.AdjustAsync(offer.StockItemId, -16, "Damage", "Pallet crushed."));

        Assert.Equal(announced + 1, await EventsAboutAsync("StockLevelChanged", offer.ListingId));
        Assert.Equal(1, await EventsAboutAsync("StockRunningLow", offer.StockItemId));

        // Still below it. The movement is announced; the alert is not raised again.
        await ReadAsync(await inventory.AdjustAsync(offer.StockItemId, -2, "Damage", "Two more."));

        Assert.Equal(announced + 2, await EventsAboutAsync("StockLevelChanged", offer.ListingId));
        Assert.Equal(1, await EventsAboutAsync("StockRunningLow", offer.StockItemId));

        // Replenished above the level, which rearms it.
        await ReadAsync(await inventory.AdjustAsync(offer.StockItemId, 25, "Return", "Restocked."));

        Assert.Equal(1, await EventsAboutAsync("StockRunningLow", offer.StockItemId));

        // And crossing a second time raises a second alert.
        await ReadAsync(await inventory.AdjustAsync(offer.StockItemId, -24, "Damage", "Sold through."));

        Assert.Equal(2, await EventsAboutAsync("StockRunningLow", offer.StockItemId));

        // A movement the row refuses announces nothing at all. The event is enqueued in the unit of
        // work the ledger entry belongs to, so there is no event without a movement.
        var before = await EventsAboutAsync("StockLevelChanged", offer.ListingId);

        await RefusedAsync(
            await inventory.AdjustAsync(offer.StockItemId, -500, "Damage", "More than there is."),
            HttpStatusCode.Conflict,
            "INVENTORY_INSUFFICIENT_STOCK");

        Assert.Equal(before, await EventsAboutAsync("StockLevelChanged", offer.ListingId));
    }

    /// <summary>How many stock rows exist for one offer.</summary>
    private Task<long> StockRowsForAsync(Guid listingId)
        => Database.CountAsync(
            "SELECT COUNT(*) FROM inventory.stock_items WHERE listing_id = $1",
            Cancellation,
            listingId);

    /// <summary>How many goods receipts were posted against one order.</summary>
    private Task<long> ReceiptsAgainstAsync(Guid purchaseOrderId)
        => Database.CountAsync(
            "SELECT COUNT(*) FROM inventory.goods_receipts WHERE purchase_order_id = $1",
            Cancellation,
            purchaseOrderId);

    /// <summary>How many corrections one stock row has taken.</summary>
    private Task<long> CorrectionsAsync(Guid stockItemId)
        => Database.CountAsync(
            "SELECT COUNT(*) FROM inventory.stock_ledger_entries "
            + "WHERE stock_item_id = $1 AND reason = 'Correction'",
            Cancellation,
            stockItemId);

    /// <summary>How many events of a kind the outbox holds about one id.</summary>
    private Task<long> EventsAboutAsync(string eventType, Guid subjectId)
        => Database.CountAsync(
            "SELECT COUNT(*) FROM platform.outbox_messages WHERE type LIKE $1 AND payload::text LIKE $2",
            Cancellation,
            $"%{eventType}%",
            $"%{subjectId}%");

    /// <summary>Takes a hold through the published contract, as a checkout does.</summary>
    private Task<Guid?> HoldAsync(Guid listingId, int quantity, Guid cartId)
        => InScopeAsync<IStockAvailability, Guid?>(async (stock, token) =>
            await stock.HoldAsync(
                listingId,
                quantity,
                ReservationReferenceTypes.Cart,
                cartId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(15),
                token));

    /// <summary>Settles every live hold of one cart.</summary>
    private Task<int> SettleAsync(Guid cartId, ReservationOutcome outcome)
        => InScopeAsync<IStockAvailability, int>(async (stock, token) =>
            await stock.SettleAsync(ReservationReferenceTypes.Cart, cartId, outcome, token));

    /// <summary>Runs one pass of the real reservation sweeper over the host under test.</summary>
    private async Task<int> SweepAsync()
    {
        var sweeper = new ReservationSweeper(
            Factory.Services,
            Factory.Services.GetRequiredService<IOptionsMonitor<InventoryOptions>>(),
            Factory.Services.GetRequiredService<SharedKernel.Time.IClock>(),
            NullLogger<ReservationSweeper>.Instance);

        return await sweeper.SweepOnceAsync(Cancellation);
    }

    /// <summary>Runs one reconciliation pass over the host under test and answers what it found.</summary>
    /// <remarks>
    /// Constructed rather than resolved, for the same reason as the sweeper: the loop is registered
    /// as an <c>IHostedService</c> and not under its own type. The pass itself is the one the nightly
    /// timer runs.
    /// </remarks>
    private async Task<IReadOnlyList<StockDrift>> ReconcileAsync()
    {
        var job = new StockReconciliationJob(
            Factory.Services,
            Factory.Services.GetRequiredService<IOptionsMonitor<InventoryOptions>>(),
            NullLogger<StockReconciliationJob>.Instance);

        return await job.ReconcileAsync(Cancellation);
    }

    /// <summary>The two cached quantities, read from the row rather than through the API.</summary>
    private async Task<(int OnHand, int Reserved)> ReadQuantitiesAsync(Guid stockItemId)
    {
        var rows = await Database.RowsAsync(
            "SELECT quantity_on_hand, quantity_reserved FROM inventory.stock_items WHERE id = $1",
            Cancellation,
            stockItemId);

        var row = Assert.Single(rows);

        return ((int)row["quantity_on_hand"]!, (int)row["quantity_reserved"]!);
    }

    /// <summary>What the ledger sums to for one stock row.</summary>
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
}
