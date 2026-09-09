# Step 29 · Step 11 — Inventory & warehouse: test-debt closure

> Worked under [`step-29-test-hardening-and-performance-baseline.md`](../step-29-test-hardening-and-performance-baseline.md),
> to the standard 29.1–29.3 set. Card: [`step-11-inventory-and-warehouse-module.md`](../step-11-inventory-and-warehouse-module.md).

**All 18 Step 11 rows in `TEST_DEBT.md` are closed by 24 named, passing integration tests.**
`InventoryConcurrencyTests` (7), `InventoryConstraintTests` (6), `InventoryStockTests` (8),
`InventoryAuthorisationTests` (3), around an `InventoryScenario` that builds warehouses, suppliers,
purchase orders, receipts and stocked offers through the API the people who run a warehouse would
use. **Eight real defects were found and fixed**, six of them on 🔴 rows: two that stranded stock
where nothing could account for it, one that made increasing a cart line a 500, one that left the
ledger editable a partition at a time, one that let a refused goods receipt commit half of itself,
and one that hid a seller's own fulfilment-by-platform stock from them entirely.

---

## 1. Debt rows → tests

| # | Behaviour (abridged) | Risk | Test | State |
|---|---|---|---|---|
| 74 | No oversell under concurrency: N callers race for the last unit, one wins, `quantity_reserved <= quantity_on_hand` throughout | 🔴 | `InventoryConcurrencyTests.Twelve_callers_racing_for_five_units_produce_five_holds_and_seven_refusals` | ✅ CLOSED |
| 75 | `quantity_on_hand = SUM(ledger.change)` and `quantity_reserved = SUM(ledger.reserved_change)` after an arbitrary sequence | 🔴 | `InventoryStockTests.Both_caches_equal_their_ledger_sums_after_receipts_holds_settlements_transfers_and_corrections` | ✅ CLOSED |
| 76 | `ReservationSweeper` releases a lapsed hold with its note, marks it `Expired`, and two sweepers never release it twice | 🔴 | `InventoryConcurrencyTests.Two_sweepers_running_together_release_a_lapsed_hold_exactly_once` | ✅ CLOSED |
| 77 | The append-only trigger refuses `UPDATE`, `DELETE` and `TRUNCATE`, on the parent **and on a partition** | 🔴 | `InventoryConstraintTests.The_stock_ledger_refuses_update_delete_and_truncate_on_the_parent_and_on_a_partition` | ✅ CLOSED — **defect 4** |
| 78 | Every `CHECK` refuses what it is meant to (seven named cases) | 🟡 | `InventoryConstraintTests.A_stock_row_refuses_a_negative_count_an_unhonourable_hold_and_a_stray_preorder_date`, `.A_ledger_entry_that_moves_nothing_or_lands_below_zero_is_refused`, `.A_reservation_refuses_a_bad_quantity_an_unknown_reference_type_and_a_blurred_settlement`, `.A_purchase_order_line_and_a_receipt_line_refuse_what_a_buyer_could_not_explain` | ✅ CLOSED |
| 79 | `HoldAsync` idempotent on `(referenceType, referenceId, lineReferenceId)`; `ux_stock_reservations_live` enforces it under concurrency | 🔴 | `InventoryConcurrencyTests.A_retried_hold_on_the_same_cart_line_returns_the_same_hold_and_holds_once`, `.Eight_simultaneous_retries_of_one_cart_line_leave_one_hold_and_no_stranded_units` | ✅ CLOSED — **defects 1 and 2** |
| 80 | `SettleAsync` idempotent under at-least-once delivery; a redelivery moves nothing and reports zero | 🔴 | `InventoryConcurrencyTests.A_redelivered_settlement_moves_no_stock_and_reports_zero` | ✅ CLOSED |
| 81 | `StockLedgerService` resets `xmin` from `RETURNING`, so a save after a movement in the same unit of work does not throw | 🟡 | `InventoryConcurrencyTests.An_adjustment_that_crosses_the_reorder_level_saves_the_alert_without_a_concurrency_failure` | ✅ CLOSED |
| 82 | The raw movement command is enlisted in the ambient transaction: a handler that throws after a movement leaves nothing | 🔴 | `InventoryConcurrencyTests.A_movement_rolls_back_with_the_transaction_the_handler_threw_out_of` | ✅ CLOSED |
| 83 | The `inventory` migrations apply and re-run clean; the ledger is created partitioned, 25 monthly partitions plus the default, and a row lands where its `occurred_at` says | 🟡 | `InventoryConstraintTests.The_stock_ledger_is_partitioned_monthly_with_a_default_and_rows_land_where_they_belong`, plus the existing `SchemaMigrationTests` (4) for apply/re-run | ✅ CLOSED |
| 84 | The vendor filter confines a caller to their own stock, locations, suppliers and documents; `InventoryScope.CanWrite` refuses a write to a platform-owned row they can see | 🔴 | `InventoryAuthorisationTests.A_vendor_caller_sees_only_their_own_stock_locations_suppliers_and_documents`, `.A_vendor_caller_reads_a_platform_location_but_may_not_write_into_it` | ✅ CLOSED — **defects 6 and 7** |
| 85 | Every permission the endpoints declare is in `PermissionCatalog` — by reflection, not a hand-written list | 🟡 | `InventoryAuthorisationTests.Every_inventory_endpoint_permission_is_defined_by_the_module_and_known_to_the_catalogue` | ✅ CLOSED |
| 86 | `ReceivePurchaseOrderCommandHandler` writes the GRN, the ledger entries and the status advance in one transaction | 🔴 | `InventoryStockTests.A_goods_receipt_writes_the_grn_the_ledger_and_the_status_together_or_not_at_all` | ✅ CLOSED — **defect 5** |
| 87 | A stock take posts one `Correction` per non-zero variance, leaves uncounted rows alone, and reports rather than forces a correction the shelf cannot absorb | 🟡 | `InventoryStockTests.Submitting_a_stock_take_corrects_the_counted_rows_and_reports_the_one_it_cannot` | ✅ CLOSED |
| 88 | A transfer writes both legs under one reference id or neither | 🟡 | `InventoryStockTests.A_transfer_writes_both_legs_under_one_reference_or_neither` | ✅ CLOSED — see note (a) |
| 89 | `StockReconciliationJob.DriftQuery` reports a tampered cache, reports nothing when everything agrees, and catches a cached quantity with no ledger entries at all | 🟡 | `InventoryStockTests.The_reconciliation_job_reports_drift_including_a_cache_with_no_ledger_behind_it` | ✅ CLOSED |
| 90 | `ListingPublished` opens a stock row at zero in the seller's highest-priority active location, is idempotent, and logs rather than throws when there is nowhere to put it | 🟡 | `InventoryStockTests.A_published_offer_opens_a_stock_row_at_zero_in_the_sellers_best_location_and_only_once`, `.A_published_offer_for_a_seller_with_no_location_is_logged_rather_than_thrown_at` | ✅ CLOSED |
| 91 | `StockLevelChanged` and `StockRunningLow` are written to the outbox in the movement's transaction; `StockRunningLow` fires once per crossing | 🟢 | `InventoryStockTests.Every_movement_is_announced_and_the_low_stock_alert_fires_once_per_crossing` | ✅ CLOSED |

**No row is left open.**

> **(a) One half of row 88 is unreachable by construction, and is asserted by proxy.** The row asks
> that "the outbound leg rolls back when the inbound one fails". An inbound movement's only guard is
> that on hand stays non-negative, so a positive change cannot be refused — the handler's own comment
> says as much and throws in that branch rather than returning. The test proves the reachable half
> (a source that cannot supply the quantity writes neither leg, and a successful transfer writes
> exactly two entries sharing one reference id); the rollback mechanism the unreachable branch relies
> on is the one proved by row 82's test, which throws out of a real transaction after a real
> movement and shows the movement gone.

## 2. Step 11's Full acceptance criteria

> "A concurrency test proves no oversell; ledger sum always equals on-hand quantity; reservations
> expire and release stock."

| Criterion | Evidenced by |
|---|---|
| A concurrency test proves no oversell | `Twelve_callers_racing_for_five_units_produce_five_holds_and_seven_refusals` — twelve callers, each in its own scope on its own connection, against five units; five holds, seven refusals, `quantity_reserved` never above `quantity_on_hand`, and five `Reservation` ledger entries and no more |
| Ledger sum always equals on-hand quantity | `Both_caches_equal_their_ledger_sums_after_receipts_holds_settlements_transfers_and_corrections` — a goods receipt, three holds (one committed, one released, one swept), a write-off, a transfer and a stock-take correction across two locations; both caches equal both ledger sums on both rows afterwards, and the reconciliation job reports neither |
| Reservations expire and release stock | `Two_sweepers_running_together_release_a_lapsed_hold_exactly_once` — the hold is released, the row is `Expired` with a `settled_at`, and the `Release` ledger entry carries the note an operator reads |

## 3. Defects found and fixed

### 1. A simultaneous retry of one cart line left stock held by nothing at all 🔴

`Infrastructure/Stock/StockAvailabilityService.cs` — `HoldAsync`.

`HoldAsync` took the units and recorded the hold in **two** steps that nothing joined. The movement
is a raw conditional `UPDATE`, and `StockLedgerService` enlists it in the ambient transaction — but
`HoldAsync` opened none, so the `UPDATE` committed the instant it ran. The reservation row and the
ledger entry that justify those units were written by a separate `SaveChangesAsync` afterwards.

Anything that refused that save therefore left `quantity_reserved` permanently raised, with no
reservation anybody could release and no ledger entry to explain it — and something refuses it
routinely: `ux_stock_reservations_live` permits one live hold per `(referenceType, referenceId,
lineReferenceId)`, which is exactly what several simultaneous retries of the same checkout collide
on. Eight concurrent retries of a three-unit line left **nine units reserved against one hold of
three**; the reconciliation job would have reported the row every night thereafter and nobody could
have said why.

Fixed by putting the whole of `HoldAsync` inside `context.ExecuteInTransactionAsync`, so the movement
is enlisted and a refused insert takes the reserved bump back with it, and by catching the live-hold
collision specifically — matched on the index name, not on `23505` alone — and answering with the
hold that *did* commit. That is what idempotency on the line means, and it turns a lost race from a
500 into the same answer the winner got.

### 2. Increasing a cart line's quantity was a 500 🔴

Same file, same method.

When a shopper asks to hold more than they already hold, `HoldAsync` releases the existing hold and
takes the whole quantity afresh. It marked the old reservation `Released` in the change tracker and
then added the new `Held` one, leaving both to a single `SaveChangesAsync` — and EF writes an insert
ahead of an update in the same batch. So the new row arrived while the old one was still `Held`, and
`ux_stock_reservations_live` refused it. Every increase of a cart line's quantity was a
`DbUpdateException`.

Fixed by saving the release before adding the replacement, inside the transaction added above.

### 3. A multi-line settlement could move stock with no ledger behind it 🔴

Same file — `SettleAsync`.

The same shape as defect 1 and fixed with it: each hold's commit or release ran its raw movement
outside any transaction, and one `SaveChangesAsync` at the end wrote every ledger entry and every
status change. A failure part-way through a multi-line order would have committed some movements
with no entries behind them and left their holds live. No test provoked it — the window is much
narrower than `HoldAsync`'s — but it is the same defect and the fix is the same transaction.

### 4. The stock ledger was editable one partition at a time 🔴

`Infrastructure/Persistence/Migrations/20260908133011_StockLedgerAppendOnlyOnPartitions.cs` (new).

`StockLedgerPartitions` put the append-only trigger on the parent as
`BEFORE UPDATE OR DELETE OR TRUNCATE … FOR EACH STATEMENT`. A **statement**-level trigger on a
partitioned table is not inherited by its partitions — only a row-level one is — so
`UPDATE inventory.stock_ledger_entries …` was refused and
`UPDATE inventory.stock_ledger_entries_2026_09 …` went straight through. Every partition is an
ordinary table whose name is exactly what a maintenance script or a repair query reaches for.

That is the whole reconciliation story: both quantity caches are sums over this table, so a ledger
somebody can edit lets the cache and the ledger be made to agree about a number neither earned, and
the only evidence of how the stock really moved is destroyed. The Platform module got the same
problem right by accident — `platform.audit_logs` uses `FOR EACH ROW`, which *is* cloned to
partitions.

Fixed with a new migration that attaches the trigger to every existing partition and redefines
`inventory.ensure_stock_ledger_partition` to attach it to every partition it creates from now on,
including the ones Step 31's maintenance job will add. A new migration rather than an edit to the
old one, because the old one has been applied.

### 5. A goods receipt refused on its second line committed its first line's stock 🔴

`Application/Purchasing/GoodsReceiptFeature.cs` — `ReceivePurchaseOrderCommandHandler`.

The handler walked the counted lines inside `ExecuteInTransactionAsync`, moving each line's stock as
it went, and refused an unknown order line by setting `outcome` and **returning out of the lambda**.
A `return` completes the operation normally, so `ExecuteInTransactionAsync` then *commits*: the
earlier lines' movements — already enlisted and already applied — were made permanent, while
`SaveChangesAsync` was never reached, so no ledger entry, no GRN and no advance on the order were
written at all.

A two-line receipt whose second line named a line from a different order therefore booked the first
line's pallet onto the shelf and left nothing anywhere that said it had happened. The handler's own
documentation says the three land together or not at all.

This one was found by reading rather than by a red test, so it was confirmed by putting it back: with
the fix reverted, `A_goods_receipt_writes_the_grn_the_ledger_and_the_status_together_or_not_at_all`
reads **12 units on hand** after a receipt the API answered 404 to, against the 0 it asserts.

Fixed by resolving and checking every line **before** the transaction opens, so a refusal is returned
before anything has moved. `INVENTORY_NOTHING_OUTSTANDING` was reachable the same way and is fixed by
the same change.

### 6. A seller could not see the platform locations their own stock sits on 🔴

`Domain/Warehouse.cs`.

The generic vendor query filter's rule is "a row with no vendor belongs to the platform, and a vendor
user has no business seeing it either". Five places in this module say the opposite for a warehouse —
the endpoint summary (*"A vendor caller sees their own and the platform's"*), the `InventoryDbContext`
and `InventoryScope` XML, `ListWarehousesQueryHandler`'s own comment, and the debt row itself — and
the filter won, because the conventions are applied after a module has configured its model. So
`InventoryScope.CanWrite`'s platform branch was dead code, and row 84's second half could not be
demonstrated at all.

It was not only a dead branch. `OpenStockItemCommandHandler` deliberately supports fulfilment by
platform — a stock row's owner is the *listing's* seller, not the shelf's — and
`ListingLifecycleHandlers` falls back to a platform warehouse for a seller who has none. Both produce
a stock row the seller owns on a shelf they could not see, and `ListStockQueryHandler` joins stock to
warehouse: **the seller's own fulfilment-by-platform stock silently vanished from their stock list**,
and `GET /admin/stock/{id}` answered 404 for it.

Fixed with the opt-in Step 10 introduced for exactly this: `Warehouse` now declares
`IPlatformShared`, which widens the *read* for that one table and says nothing about the write.
Nothing else in the schema declares it — the platform's supplier list, stock, purchase orders,
receipts and stock takes stay private, as `Supplier`'s own documentation requires.

### 7. Refusing a seller a platform-owned write answered 422 🔴

`Application/InventoryErrors.cs`, and the eighteen call sites that used it.

`InventoryErrors.OutOfScope` was `Error.Validation`, so every `CanWrite` refusal was a 422. Once
defect 6 was fixed those refusals became reachable, and each of them is the same thing: the caller
is authenticated, the resource they named is one they can already read, and opening stock in a
platform warehouse, renaming one, closing one, counting one or buying into one is not something a
seller does. That is a permission refusal, and §1 of `04-api-specification.md` gives it 403 — the
identical split Step 9 drew when it separated `VendorErrors.PlatformOnly` from `NotFound`.

Renamed to `InventoryErrors.PlatformOnly`, `Error.Forbidden("INVENTORY_PLATFORM_ONLY", …)`. Nothing
outside this module referenced the old code — the frontend, the docs and the generated client have
no mention of `INVENTORY_SCOPE`.

### 8. A hand-posted adjustment moved stock outside a transaction 🟡

`Application/Stock/StockFeature.cs` — `AdjustStockCommandHandler`.

The same shape as defect 1: the raw movement committed on its own, and the ledger entry it justifies
was written by the `SaveChangesAsync` that followed. The window is much narrower here — one movement,
one save — but it is the same class of drift and the same fix, and this is the path an operator uses
daily. Wrapped in `ExecuteInTransactionAsync`.

## 4. Cross-module and shared-file edits

**Nothing outside `src/backend/modules/KlaraHome.Modules.Inventory/` and
`src/backend/tests/KlaraHome.IntegrationTests/` was changed.** In particular Pricing, Carts and
Orders are untouched.

Two additions to shared files, both **new members only**, nothing restructured:

| File | Addition | Why |
|---|---|---|
| `tests/…/Commerce/CommerceTestBase.cs` | `InScopeAsync<TService, TResult>(…)` | The sibling of the existing `RunOnceAsync`, for criteria about a *published contract* rather than an endpoint. `IStockAvailability.HoldAsync` is the platform's oversell boundary and no route reaches it, so a test that proves it must call it the way Cart and Orders do — and a fresh scope per call is what makes two concurrent calls a real race |
| `modules/…/Infrastructure/Stock/ReservationSweeper.cs` | `internal Task<int> SweepOnceAsync(CancellationToken)` | One deterministic pass, exactly as `StockReconciliationJob.ReconcileAsync` already offers. The loop is a timer; a test that raced it would be flaky |

One unrelated tidy inside the module: `InventoryModule.cs` had its `using`s in the wrong order and
was failing `dotnet format`. Corrected. `SearchModule.cs` has the same pre-existing failure and was
left alone — see §6.

## 5. Specification problems found

Protocol rule 9: none of these were changed.

1. **`Supplier`'s documentation and its endpoint summary contradict each other.**
   `Domain/Supplier.cs` says "a seller keeps their own supplier list, the platform keeps its own for
   platform-owned stock, **and neither sees the other's**". `AdminPurchasingEndpoints`' summary says
   "Lists suppliers. A vendor caller sees their own and the platform's." Implemented per the domain
   type — a platform supplier is invisible to a seller and the refusal is a 404 — and the test
   asserts that. The endpoint summary is wrong and needs correcting, but it is published in the
   OpenAPI document and therefore in the generated client, so changing it means a codegen run and is
   left for the orchestrator.

2. **Step 29.3's aside conflicts with this module's design.** The Step 10 note says widening the
   vendor convention "would have exposed platform warehouses … to every seller on the marketplace",
   listing that as a thing to avoid. This module states the opposite in five places and depends on
   it in two code paths (defect 6). Resolved in favour of the module, narrowly: `IPlatformShared` is
   declared by `Warehouse` and by nothing else, which is the per-table opt-in Step 10 built for this
   exact case rather than the convention-wide widening it warned about. Flagged because it is a
   direct disagreement between two step records.

3. **No seeded vendor role grants the whole Inventory surface, and `vendor-owner` grants none of
   it.** `modules/KlaraHome.Modules.Identity/…/PermissionCatalog.cs`: `vendor-staff` holds
   `inventory.stock.read`, `inventory.stock.adjust` and `inventory.stock-take.manage`;
   `vendor-owner` holds **no** inventory permission at all, so a seller's owner has strictly fewer
   rights over their own stock than their own staff — which is the only place in the catalogue where
   that is true. Neither vendor role holds `inventory.warehouse.manage` or
   `inventory.purchasing.manage`, though the admin navigation exposes Warehouses and Suppliers behind
   exactly those, and the platform `catalog-manager` role's comment ("Their own stock, confined to
   their own locations by the vendor scope. A seller runs their own warehouse and buys their own
   stock; the platform does not do it for them") reads as though it was written for a vendor role.
   **Not fixed** — who is granted what is a policy decision in a module I do not own. The tests mint
   a vendor-scoped role through the real role-management API instead, which exercises the same scope
   machinery without changing anybody's seeded grants.

## 6. Known gaps and residual risk

- **Two movement paths still move stock outside a transaction**, the same shape as defects 1, 3 and
  8: `Infrastructure/Stock/StockRestockService.cs:153` (Step 17's returns seam) and
  `Infrastructure/Seeding/DemoStockSeeder.cs:143` (non-production seeding). The restock one belongs
  to Step 17's debt and may be another agent's; the fix is one `ExecuteInTransactionAsync` around the
  movement and the save.
- **`ReceivePurchaseOrderCommandHandler` is not safe to retry.** `orderLine.Receive(payload.Accepted)`
  mutates a tracked entity inside the `ExecuteInTransactionAsync` body, and that body may run more
  than once — the execution strategy re-runs it after a transient failure — which would double-count
  the received quantity. Pre-existing, unrelated to defect 5, and it needs the accepted quantities
  computed per attempt rather than accumulated.
- **`platform.audit_logs` cannot be truncated-proofed by its own trigger.** It uses `FOR EACH ROW`,
  which is correctly inherited by partitions but never fires on `TRUNCATE`. Not this step's row and
  not this module; noted because it is the mirror image of defect 4.
- **`dotnet format` still fails on `modules/KlaraHome.Modules.Search/SearchModule.cs`** (imports
  ordering). Pre-existing on `main`, another module, not touched.
- The `Twelve_callers…` and `Eight_simultaneous_retries…` tests take real locks on a real engine and
  are the slowest in the class (~10 s each). They are deterministic — every assertion is on a
  committed effect rather than on elapsed time — but they are the ones to look at first if the
  suite's wall clock becomes a problem.

## 7. Suite counts

| Suite | Before | After |
|---|---|---|
| Unit | 947 | 947 |
| Architecture | 14 | 14 |
| Integration | 254 | **278** |

Unit and architecture are green. Integration is green in the `Commerce` collection (85 of 85, which
contains every test written here) and green everywhere else except the flake below. Verified on the
final build as: `Commerce` 85/85, `MigrationPipelineTests` 6/6, unit 947/947, architecture 14/14; and
on the build before it as two whole-suite runs of 278, whose only failures were the flake.

`dotnet build -c Release src/backend/KlaraHome.sln` produces **0 warnings, 0 errors**.
`dotnet format --verify-no-changes` is clean for everything this work touched.

**One flake to be aware of, and it is not this step's.**
`MigrationPipelineTests` failed on two full runs — `Seeders_run_after_the_migrations` on the first,
that and `The_extensions_the_design_requires_are_installed` on the second — and passes **6 of 6 on
its own**, twice, including immediately after each failing run. It composes `PlatformModule` alone
(no Inventory assembly, no Inventory schema, no Inventory data) and stands up its own container, and
both failing runs happened with **four** integration-test processes sharing this machine's Docker
daemon — three of them the other Step 29 worktrees running in parallel. The Commerce
collection, which is where all of this step's work lives, is **85 of 85 green** on the same build.
Worth an entry in Step 29's flake sweep; nothing here touches it.

A third whole-suite run was started to settle it and had to be abandoned: `docker ps` came back empty
while two test hosts were still alive, so the daemon had dropped their containers underneath them.
That is the same contention, one stage worse, and it is the strongest argument for the four Step 29
worktrees not running their integration suites at the same time.
