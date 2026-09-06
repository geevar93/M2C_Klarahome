# Step 11 — Inventory & Warehouse module

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** C · **Depends on:** Step 10
- **Objective:** Accurate, auditable stock across vendors and locations.
- **Deliverables:**
  - Warehouses/locations per vendor; stock item per (listing, location).
  - Stock ledger: every movement is an immutable entry (inbound, sale, reservation, release,
    return, adjustment, damage, transfer) — quantity on hand is derived and reconcilable.
  - Reservation model with TTL for carts/checkout; oversell prevention under concurrency.
  - Low-stock thresholds and alerts; backorder / pre-order flags.
  - Purchase orders, suppliers, goods receipt (GRN), stock takes / cycle counts.
  - Batch/lot and serial tracking flags (design present, enforcement optional per category).
- **Build acceptance - what closes this step during the sprint:**
  1. Every item under **Deliverables** is written: entities, configuration, handlers, endpoints,
     module registration and DI wiring, and the EF migration for this module.
  2. The code compiles - `dotnet build src/backend/KlaraHome.sln` (backend) or
     `npx nx build <app>` (frontend) succeeds with no errors.
  3. The endpoints are registered and declare their permissions; the module is referenced by the
     host, and the migrator project compiles with the new migration.
  4. Every behaviour named in the full acceptance criteria below that has not been proved has a
     row in [`../TEST_DEBT.md`](../TEST_DEBT.md).
  5. Outcome / Notes filled in, Parking Lot updated, permission requested.
- **Full acceptance criteria (verified at Step 29, not now):** A concurrency test proves no oversell; ledger sum always equals
  on-hand quantity; reservations expire and release stock.
- **Outcome / Notes:**

  **Status: DONE (2026-09-06).** The Inventory module is built: `inventory` schema, thirteen tables,
  three document-number sequences, one published contract, two integration events, three event
  handlers, two background loops and **36 endpoints**. 545 unit tests green (37 new); 14
  architecture tests green; `dotnet build src/backend/KlaraHome.sln` succeeds with no warnings.

  ### What was built

  | Area | Files |
  |---|---|
  | Domain | `Domain/Warehouse.cs`, `StockItem.cs`, `StockLedgerEntry.cs`, `StockReservation.cs`, `Supplier.cs`, `PurchaseOrder.cs`, `GoodsReceipt.cs`, `StockTake.cs`, `StockTracking.cs` |
  | Application | `Application/InventoryErrors.cs`, `InventoryQueries.cs`, `Warehouses/WarehouseFeature.cs`, `Stock/StockContracts.cs`, `Stock/StockFeature.cs`, `Stock/StockTrackingFeature.cs`, `Purchasing/SupplierFeature.cs`, `Purchasing/PurchaseOrderFeature.cs`, `Purchasing/GoodsReceiptFeature.cs`, `StockTakes/StockTakeFeature.cs` |
  | Endpoints | `Endpoints/InventoryPermissions.cs`, `AdminWarehouseEndpoints.cs`, `AdminStockEndpoints.cs`, `AdminPurchasingEndpoints.cs`, `AdminStockTakeEndpoints.cs` |
  | Infrastructure | `Infrastructure/InventoryOptions.cs`, `InventoryScope.cs`, `Events/InventoryEventPublisher.cs`, `Events/ListingLifecycleHandlers.cs`, `Stock/StockLedgerService.cs`, `Stock/StockAvailabilityService.cs`, `Stock/ReservationSweeper.cs`, `Stock/StockReconciliationJob.cs`, `Persistence/*` |
  | Contract | `shared/KlaraHome.Contracts/Inventory/IStockAvailability.cs`, `InventoryEvents.cs` |
  | Migrations | `20260906014845_InitialInventorySchema`, `20260906014938_StockLedgerPartitions` (hand-written) |
  | Tests | `tests/KlaraHome.UnitTests/Inventory/StockItemTests.cs`, `InventoryDocumentTests.cs`, `StockReservationTests.cs` |

  ### The deliverables, one by one

  1. **Warehouses/locations per vendor.** `inventory.warehouses`, vendor-scoped and nullable —
     platform-owned when `vendor_id` is null. `priority` orders the locations an allocator walks;
     ties break on the code, so the order is at least reproducible. One stock item per
     `(tenant, listing, warehouse)`, enforced by a unique index — two rows for the same pair would
     split the count and make every availability answer wrong by whichever half the query found.
  2. **Stock ledger.** `inventory.stock_ledger_entries`, append-only and
     `PARTITION BY RANGE (occurred_at)` with 25 monthly partitions plus a default. Append-only is
     **enforced by a trigger**, not merely agreed: the whole reconciliation story rests on it. Every
     movement carries a signed `change`, `balance_after`, `reserved_change` and `reserved_after`.
  3. **Reservations with TTL, and oversell prevention.** `IStockAvailability.HoldAsync` — see below.
  4. **Low-stock thresholds and alerts; backorder / pre-order flags.** `reorder_level`,
     `reorder_quantity`, `allow_backorder`, `allow_preorder`, `preorder_available_at`.
     `StockRunningLow` is raised **on the crossing**, guarded by `low_stock_notified_at`, and rearms
     once the item is replenished above the level or the level is moved.
  5. **Purchase orders, suppliers, goods receipt, stock takes.** All five documents, with the split
     that matters: a purchase order is an *intention* and moves nothing; the goods receipt is what
     writes the `Purchase` ledger entries; a stock take freezes the book figures when counting opens
     and posts one `Correction` per non-zero variance on submission.
  6. **Batch/lot and serial tracking flags.** `stock_batches` and `stock_serials` exist for every
     deployment; `stock_items.tracking_mode` decides whether an item is required to populate them —
     design present, enforcement per category, exactly as the card asks.

  ### The oversell defence, stated plainly

  `StockLedgerService` is the only way stock moves, and every movement is a **single conditional
  `UPDATE`** that tests the invariant and applies the change in one statement, with `RETURNING`
  supplying the balances the ledger entry is made of. A read followed by a write leaves a window in
  which two callers each see the last unit and each take it, and no amount of application-level
  checking closes it; PostgreSQL holds the row lock for the duration of the statement, so the second
  caller re-evaluates its `WHERE` against the already-updated row and matches nothing. **Refusal is
  zero rows returned, not an exception.** The `ck_stock_items_reserved_within_hand` constraint
  restates the same predicate and is the backstop, not the mechanism.

  Three consequences worth writing down:

  - `RETURNING` also reads back `xmin`, and the service resets the tracked entity's original row
    version. Without that, an entity whose low-stock flag changed in the same unit of work would
    fail its next optimistic-concurrency check against a conflict this service caused itself.
  - The raw command is enlisted into the ambient transaction explicitly. EF does that only for its
    own commands, and a movement outside the caller transaction would commit even when the handler
    rolled back.
  - A **release** floors reserved at zero rather than guarding above it. A release that would go
    negative means a hold was settled twice, and putting the units back on sale is the safe half of
    that mistake; refusing would strand them held for ever.

  ### Deviations from the card and the design docs

  1. **Two spec amendments, written before the code** (protocol rule 9) and logged in
     `CHANGE_LOG.md`. `03-database-design.md` §4.5 gains the batch/lot and serial tables a named
     deliverable needs, the `reserved_change`/`reserved_after` ledger columns, and columns for the
     seven tables §4.5 named but did not describe. `04-api-specification.md` §4 has its six-line
     Inventory block expanded to the routes that now exist.
  2. **`reserved_change` and `reserved_after` on the ledger** are the substantive addition. §4.5 says
     *both* quantity columns are caches reconciled against the ledger sum; without a signed reserved
     delta that statement is unverifiable for half of them, and a lost hold would be undetectable.
  3. **A transfer is two ledger entries sharing a reference id**, not a `stock_transfers` header
     table. §4.5 reason list already has `transfer_in`/`transfer_out`; a header table would add a
     document nobody asked for. Recorded in the Parking Lot in case Step 16 wants one.
  4. **Low-stock alerts are an integration event, not a direct `INotifier` call.** `StockRunningLow`
     goes through the outbox like everything else; whoever renders it is the Notifications module
     business. **No Notifications template exists for it yet** — see the Parking Lot.
  5. **`ListingUpdated` triggers a contract read.** The event carries price and terms, not the SKU,
     so refreshing the denormalised `stock_items.sku` needs one `IProductCatalog.FindListingAsync`
     call. A cross-schema join was never an option.
  6. **The reconciliation job asserts, it does not repair.** A cache that has drifted is a defect in
     the code that moved the stock, and silently correcting it would destroy the only evidence of
     how it happened.
  7. **A goods receipt opens a stock row if the destination has never stocked the offer.** Refusing
     because nobody pressed "open stock item" first would leave a pallet on a loading bay with
     nowhere to be recorded.
  8. **`AdjustStockValidator` restricts an operator to four reasons** — `Adjustment`, `Damage`,
     `Return`, `Correction`. `Sale`, `Reservation` and `Release` are written by the reservation
     machinery and nothing else; a hand-posted one would put the reserved cache out of step with the
     holds that justify it, and the nightly reconciliation would report a drift nobody could explain.

  ### Known gaps and technical debt

  - **Everything not listed under Tests above is deferred to Step 29**, with 18 rows in
    `TEST_DEBT.md`. The concurrency proof the full acceptance criteria demand — two callers racing
    for the last unit — needs a real database and is the single most important of them.
  - **`StockRunningLow` has no consumer.** The event is published; nothing renders it. Notifications
    needs a template and a handler.
  - **Partition maintenance is Step 31, as it is for `platform.audit_logs` and
    `notifications.notification_messages`.** Twenty-five months of partitions exist from first
    migration; after that, inserts land in the default partition. Unlike message bodies, ledger
    entries have **no retention limit** — dropping an old partition would make the oldest stock rows
    unreconcilable.
  - **Serial tracking is capture-only.** Units are booked in and can be moved between states, but
    nothing moves them automatically: a sale does not mark a serial `Sold`. That belongs with Orders
    at Step 14, and is in the Parking Lot.
  - **Batch quantities are not enforced against on hand.** They are a breakdown of it, reconciled by
    the same nightly job; making the ledger depend on them would put a second source of truth next
    to the first.
  - **No allocation strategy beyond warehouse priority.** `HoldAsync` takes the first location that
    can supply the whole quantity and deliberately does not split a line across warehouses — that is
    two parcels and two shipping charges, and it is a Shipping decision at Step 16.

