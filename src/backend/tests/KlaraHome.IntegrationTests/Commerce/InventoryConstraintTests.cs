using System.Net;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The rules the <c>inventory</c> schema keeps whatever the application does: the ledger cannot be
/// edited, both quantity caches stay inside their bounds, and every document refuses the state it
/// could never explain.
/// </summary>
/// <remarks>
/// <para>
/// Asserted with raw SQL, deliberately. A <c>CHECK</c> constraint is the backstop for the code path
/// that forgot the rule, so proving it through the code path that remembers would prove nothing —
/// what is under test is what happens when the application is bypassed, which is the only situation
/// the constraint exists for.
/// </para>
/// <para>
/// The rows the statements act on are still made through the API. A hand-written stock item would be
/// a stock item without a tenant, without a listing anybody stocked and without the ledger the
/// constraints are about.
/// </para>
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class InventoryConstraintTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>PostgreSQL's SQLSTATE for a violated <c>CHECK</c> constraint.</summary>
    private const string CheckViolation = "23514";

    /// <summary>
    /// The stock ledger is append-only, and the trigger says so for every way of changing a row —
    /// on the parent and on the partition the row actually lives in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both quantity columns on <c>stock_items</c> are caches of sums over this table, so a ledger
    /// somebody can edit makes the nightly reconciliation meaningless: it would be comparing a cache
    /// against something equally mutable. "Append-only" is therefore enforced rather than agreed.
    /// </para>
    /// <para>
    /// The partition half is not a formality. A partitioned table is a parent and a set of children,
    /// and every child is a table in its own right that <c>UPDATE</c> can name directly — which is
    /// exactly what a maintenance script or a well-meaning operator would reach for.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_stock_ledger_refuses_update_delete_and_truncate_on_the_parent_and_on_a_partition()
    {
        SkipWithoutDocker();

        var offer = await StockedAsync(onHand: 4);

        var partition = await Database.ScalarAsync<string>(
            "SELECT tableoid::regclass::text FROM inventory.stock_ledger_entries "
            + "WHERE stock_item_id = $1 LIMIT 1",
            Cancellation,
            offer.StockItemId);

        Assert.False(string.IsNullOrWhiteSpace(partition));
        Assert.NotEqual("inventory.stock_ledger_entries", partition);

        foreach (var table in new[] { "inventory.stock_ledger_entries", partition! })
        {
            await AssertAppendOnlyAsync(
                table,
                $"UPDATE {table} SET change = change + 1 WHERE stock_item_id = $1",
                offer.StockItemId);

            await AssertAppendOnlyAsync(
                table,
                $"DELETE FROM {table} WHERE stock_item_id = $1",
                offer.StockItemId);

            await AssertAppendOnlyAsync(table, $"TRUNCATE {table}");
        }

        // Nothing got through. The entries the receipt and the adjustment wrote are still there and
        // still say what they said.
        var sums = await LedgerSumsAsync(offer.StockItemId);
        Assert.Equal(4, sums.OnHand);
    }

    /// <summary>
    /// The stock row refuses a negative count, holds it cannot honour, and a pre-order date on an
    /// item that is not on pre-order.
    /// </summary>
    /// <remarks>
    /// The middle one is the oversell backstop. The conditional update in
    /// <c>StockLedgerService</c> is the mechanism and this constraint restates the same predicate,
    /// so a code path that ever managed to hold more than it had would fail here rather than
    /// producing a row nobody could see was wrong. The backorder and pre-order flags are the
    /// exception, because without it the constraint would make those two flags a lie.
    /// </remarks>
    [Fact]
    public async Task A_stock_row_refuses_a_negative_count_an_unhonourable_hold_and_a_stray_preorder_date()
    {
        SkipWithoutDocker();

        var offer = await StockedAsync(onHand: 4);

        Assert.Equal(
            CheckViolation,
            await Database.RefusalAsync(
                "UPDATE inventory.stock_items SET quantity_on_hand = -1 WHERE id = $1",
                Cancellation,
                offer.StockItemId));

        Assert.Equal(
            CheckViolation,
            await Database.RefusalAsync(
                "UPDATE inventory.stock_items SET quantity_reserved = -1 WHERE id = $1",
                Cancellation,
                offer.StockItemId));

        Assert.Equal(
            CheckViolation,
            await Database.RefusalAsync(
                "UPDATE inventory.stock_items SET quantity_reserved = quantity_on_hand + 1 WHERE id = $1",
                Cancellation,
                offer.StockItemId));

        // The same statement is accepted the moment the seller says they will backorder, which is
        // what that flag means and what the constraint's exception is for.
        Assert.Null(await Database.RefusalAsync(
            "UPDATE inventory.stock_items "
            + "SET allow_backorder = true, quantity_reserved = quantity_on_hand + 1 WHERE id = $1",
            Cancellation,
            offer.StockItemId));

        Assert.Null(await Database.RefusalAsync(
            "UPDATE inventory.stock_items "
            + "SET allow_backorder = false, quantity_reserved = 0 WHERE id = $1",
            Cancellation,
            offer.StockItemId));

        Assert.Equal(
            CheckViolation,
            await Database.RefusalAsync(
                "UPDATE inventory.stock_items "
                + "SET allow_preorder = false, preorder_available_at = now() WHERE id = $1",
                Cancellation,
                offer.StockItemId));

        // And the application never gets there: a date sent with the flag off is dropped rather
        // than stored, so the constraint is a backstop here rather than a 500 waiting to happen.
        var inventory = new InventoryScenario(offer.Admin, Cancellation);

        var configured = await ReadAsync(await inventory.ConfigureAsync(
            offer.StockItemId,
            allowPreorder: false,
            preorderAvailableAt: DateTimeOffset.UtcNow.AddDays(30)));

        Assert.Equal(
            System.Text.Json.JsonValueKind.Null,
            configured.GetProperty("preorderAvailableAt").ValueKind);
    }

    /// <summary>
    /// A ledger entry has to move something, and its balances can never be negative.
    /// </summary>
    /// <remarks>
    /// An entry of <c>(0, 0)</c> is always a bug in the caller rather than a real movement, and it is
    /// noise in the one table that has to stay readable months later. The balances are what the
    /// conditional update returned, so a negative one would mean the write path is wrong; this is
    /// where it stops rather than where it is discovered.
    /// </remarks>
    [Fact]
    public async Task A_ledger_entry_that_moves_nothing_or_lands_below_zero_is_refused()
    {
        SkipWithoutDocker();

        var offer = await StockedAsync(onHand: 4);

        var tenantId = await Database.ScalarAsync<Guid>(
            "SELECT tenant_id FROM inventory.stock_items WHERE id = $1",
            Cancellation,
            offer.StockItemId);

        Assert.Equal(
            CheckViolation,
            await InsertLedgerAsync(tenantId, offer.StockItemId, change: 0, balance: 4, reserved: 0, after: 0));

        Assert.Equal(
            CheckViolation,
            await InsertLedgerAsync(tenantId, offer.StockItemId, change: -9, balance: -5, reserved: 0, after: 0));

        Assert.Equal(
            CheckViolation,
            await InsertLedgerAsync(tenantId, offer.StockItemId, change: 0, balance: 4, reserved: -1, after: -1));

        // The API refuses the same thing earlier and more kindly: an adjustment of nothing is a
        // validation failure rather than a constraint name in a 500.
        var inventory = new InventoryScenario(offer.Admin, Cancellation);

        await RefusedAsync(
            await inventory.AdjustAsync(offer.StockItemId, 0),
            HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// A hold has a quantity, one of the two reference types, and a settlement time exactly when it
    /// is settled.
    /// </summary>
    /// <remarks>
    /// The last is the one that matters: without it "held" and "settled" blur, and "how long do holds
    /// actually last" stops being answerable. A third reference type is refused because the two
    /// modules that write one and the one that reads them must not be able to spell it differently.
    /// </remarks>
    [Fact]
    public async Task A_reservation_refuses_a_bad_quantity_an_unknown_reference_type_and_a_blurred_settlement()
    {
        SkipWithoutDocker();

        var offer = await StockedAsync(onHand: 6);
        var reservationId = await HoldAsync(offer.ListingId, 2);

        Assert.NotNull(reservationId);

        Assert.Equal(
            CheckViolation,
            await Database.RefusalAsync(
                "UPDATE inventory.stock_reservations SET quantity = 0 WHERE id = $1",
                Cancellation,
                reservationId.Value));

        Assert.Equal(
            CheckViolation,
            await Database.RefusalAsync(
                "UPDATE inventory.stock_reservations SET reference_type = 'quote' WHERE id = $1",
                Cancellation,
                reservationId.Value));

        // Settled without saying when.
        Assert.Equal(
            CheckViolation,
            await Database.RefusalAsync(
                "UPDATE inventory.stock_reservations SET status = 'Committed' WHERE id = $1",
                Cancellation,
                reservationId.Value));

        // And still held, but with a settlement time — the other half of the same rule.
        Assert.Equal(
            CheckViolation,
            await Database.RefusalAsync(
                "UPDATE inventory.stock_reservations SET settled_at = now() WHERE id = $1",
                Cancellation,
                reservationId.Value));
    }

    /// <summary>
    /// A purchase-order line cannot receive more than was ordered, and a refused receipt line has to
    /// say why.
    /// </summary>
    /// <remarks>
    /// Both are numbers somebody takes back to a supplier. A receipt for more than was ordered is an
    /// invoice nobody can reconcile, and a rejected quantity with no reason is a claim that cannot be
    /// made.
    /// </remarks>
    [Fact]
    public async Task A_purchase_order_line_and_a_receipt_line_refuse_what_a_buyer_could_not_explain()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var catalogue = new CatalogScenario(admin, Cancellation);
        var inventory = new InventoryScenario(admin, Cancellation);
        var sellers = Sellers(admin);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();
        var offer = await inventory.StockedAsync(catalogue, taxonomy, seller.Id);

        var supplierId = await inventory.SupplierAsync(seller.Id);

        var (orderId, lineIds) = await inventory.PurchaseOrderAsync(
            supplierId,
            offer.WarehouseId,
            [(offer.ListingId, 10)]);

        Assert.Equal(
            CheckViolation,
            await Database.RefusalAsync(
                "UPDATE inventory.purchase_order_lines SET quantity_received = quantity_ordered + 1 "
                + "WHERE id = $1",
                Cancellation,
                lineIds[0]));

        Assert.Equal(
            CheckViolation,
            await Database.RefusalAsync(
                "UPDATE inventory.purchase_order_lines SET quantity_received = -1 WHERE id = $1",
                Cancellation,
                lineIds[0]));

        // The API refuses the same overage as an ordinary business answer rather than a 500: the
        // domain clamps what it accepts to what is still outstanding.
        var receipt = await ReadAsync(await inventory.ReceiveAsync(
            orderId,
            InventoryScenario.ReceiptLine(lineIds[0], accepted: 10, rejected: 2, rejectionReason: "Torn wrapping.")));

        var receiptLineId = receipt.GetProperty("lines").EnumerateArray().First().GetProperty("id").GetGuid();

        Assert.Equal(
            CheckViolation,
            await Database.RefusalAsync(
                "UPDATE inventory.goods_receipt_lines SET rejection_reason = NULL WHERE id = $1",
                Cancellation,
                receiptLineId));

        Assert.Equal(
            CheckViolation,
            await Database.RefusalAsync(
                "UPDATE inventory.goods_receipt_lines SET quantity_accepted = -1 WHERE id = $1",
                Cancellation,
                receiptLineId));

        // And the validator refuses it a layer earlier, with something a receiver can act on.
        var (secondOrderId, secondLineIds) = await inventory.PurchaseOrderAsync(
            supplierId,
            offer.WarehouseId,
            [(offer.ListingId, 4)]);

        await RefusedAsync(
            await inventory.ReceiveAsync(
                secondOrderId,
                InventoryScenario.ReceiptLine(secondLineIds[0], accepted: 0, rejected: 3)),
            HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// The ledger is created partitioned, with two years of monthly partitions and a default, and a
    /// movement lands in the partition its instant names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A partitioned table is created partitioned or not at all — there is no migration that converts
    /// one afterwards — so this is a property of the first deploy rather than of a later one. The
    /// default partition is the safety net: without it a movement dated outside every range fails to
    /// insert, and the sale that caused it fails with it.
    /// </para>
    /// <para>
    /// That the migrations apply to a live database and re-run clean is asserted for every module at
    /// once by <see cref="SchemaMigrationTests"/>; what is left, and what only this schema has, is
    /// the shape.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_stock_ledger_is_partitioned_monthly_with_a_default_and_rows_land_where_they_belong()
    {
        SkipWithoutDocker();

        // 'p' is a partitioned table. 'r' would mean somebody replaced it with an ordinary one.
        Assert.Equal(
            "p",
            await Database.ScalarAsync<string>(
                "SELECT c.relkind::text FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace "
                + "WHERE n.nspname = 'inventory' AND c.relname = 'stock_ledger_entries'",
                Cancellation));

        Assert.Equal(
            "RANGE",
            await Database.ScalarAsync<string>(
                "SELECT CASE p.partstrat WHEN 'r' THEN 'RANGE' WHEN 'l' THEN 'LIST' ELSE 'HASH' END "
                + "FROM pg_partitioned_table p JOIN pg_class c ON c.oid = p.partrelid "
                + "JOIN pg_namespace n ON n.oid = c.relnamespace "
                + "WHERE n.nspname = 'inventory' AND c.relname = 'stock_ledger_entries'",
                Cancellation));

        // Twenty-five months from the one before the migration ran, plus the default.
        Assert.Equal(26, await PartitionCountAsync());

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace "
                + "WHERE n.nspname = 'inventory' AND c.relname = 'stock_ledger_entries_default'",
                Cancellation));

        // A movement made now lands in this month's partition, named for the month it belongs to.
        var offer = await StockedAsync(onHand: 3);

        var landed = await Database.RowsAsync(
            "SELECT tableoid::regclass::text AS partition, "
            + "'inventory.stock_ledger_entries_' || to_char(occurred_at, 'YYYY_MM') AS expected "
            + "FROM inventory.stock_ledger_entries WHERE stock_item_id = $1",
            Cancellation,
            offer.StockItemId);

        Assert.NotEmpty(landed);

        Assert.All(landed, row => Assert.Equal((string?)row["expected"], (string?)row["partition"]));
    }

    /// <summary>
    /// Asserts one statement is refused by the append-only trigger, naming the table it was aimed
    /// at.
    /// </summary>
    /// <remarks>
    /// The table is in the message on purpose: the parent and the partition run the same three
    /// statements, and a bare "expected 0A000, got null" would not say which of the two let it
    /// through — which is the whole distinction this test exists to draw.
    /// </remarks>
    /// <param name="table">The table the statement names.</param>
    /// <param name="statement">The statement.</param>
    /// <param name="parameters">Its parameters.</param>
    private async Task AssertAppendOnlyAsync(string table, string statement, params object?[] parameters)
    {
        var refusal = await Database.RefusalAsync(statement, Cancellation, parameters);

        Assert.True(
            refusal == AppendOnly,
            $"'{statement}' should have been refused by the append-only trigger on {table} "
            + $"with SQLSTATE {AppendOnly}, but the database answered "
            + $"{(refusal is null ? "no error at all — it went through" : refusal)}.");
    }

    /// <summary>Every partition of the ledger, the default included.</summary>
    private Task<long> PartitionCountAsync()
        => Database.CountAsync(
            "SELECT COUNT(*) FROM pg_inherits i "
            + "JOIN pg_class parent ON parent.oid = i.inhparent "
            + "JOIN pg_namespace n ON n.oid = parent.relnamespace "
            + "WHERE n.nspname = 'inventory' AND parent.relname = 'stock_ledger_entries'",
            Cancellation);

    /// <summary>The SQLSTATE the append-only trigger raises.</summary>
    /// <remarks>
    /// <c>0A000</c> — "feature not supported" — chosen by the trigger itself rather than a generic
    /// error, so a caller can tell "this table cannot be changed" from "this change was refused".
    /// </remarks>
    private static string AppendOnly => "0A000";

    /// <summary>Writes a ledger entry directly, bypassing the service that would never write one.</summary>
    private Task<string?> InsertLedgerAsync(
        Guid tenantId,
        Guid stockItemId,
        int change,
        int balance,
        int reserved,
        int after)
        => Database.RefusalAsync(
            "INSERT INTO inventory.stock_ledger_entries "
            + "(occurred_at, id, tenant_id, stock_item_id, change, balance_after, "
            + " reserved_change, reserved_after, reason) "
            + "VALUES (now(), gen_random_uuid(), $1, $2, $3, $4, $5, $6, 'Adjustment')",
            Cancellation,
            tenantId,
            stockItemId,
            change,
            balance,
            reserved,
            after);

    /// <summary>Takes a hold through the published contract.</summary>
    private Task<Guid?> HoldAsync(Guid listingId, int quantity)
        => InScopeAsync<Contracts.Inventory.IStockAvailability, Guid?>(async (stock, token) =>
            await stock.HoldAsync(
                listingId,
                quantity,
                Contracts.Inventory.ReservationReferenceTypes.Cart,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(15),
                token));

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

    /// <summary>An offer with a shelf and units on it, built through the API.</summary>
    /// <param name="onHand">How many units to put on it.</param>
    private async Task<ConstraintFixture> StockedAsync(int onHand)
    {
        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var inventory = new InventoryScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();

        var offer = await inventory.StockedAsync(catalogue, taxonomy, seller.Id, onHand);

        return new ConstraintFixture(admin, offer.ListingId, offer.StockItemId);
    }

    /// <summary>The offer a constraint test acts on, and the client that built it.</summary>
    /// <param name="Admin">A client signed in as platform staff.</param>
    /// <param name="ListingId">The offer.</param>
    /// <param name="StockItemId">Its one stock row.</param>
    private sealed record ConstraintFixture(HttpClient Admin, Guid ListingId, Guid StockItemId);
}
