# Step 27 — Admin: catalog, inventory, orders, fulfilment, returns

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** G · **Depends on:** Step 26
- **Objective:** Daily operations screens.
- **Deliverables:**
  - Catalog: category/brand/attribute management, product & variant editor, media manager,
    bulk import/export with error report, moderation queue for vendor submissions.
  - Inventory: stock by location, adjustments with reason, stock ledger view, low-stock queue,
    purchase orders and GRN, stock takes.
  - Orders: list with rich filters, order detail, timeline, notes, manual state transitions,
    cancellations, invoice/credit-note actions.
  - Fulfilment: pick/pack workflow, shipment creation, label/manifest printing, NDR queue.
  - Returns: RMA queue, approval, pickup scheduling, QC disposition, refund initiation.
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
- **Full acceptance criteria (verified at Step 29, not now):** An operator can take an order from placed to delivered, and a return
  from request to refund, entirely through the admin UI.
- **Outcome / Notes:** _(to be filled on completion)_
