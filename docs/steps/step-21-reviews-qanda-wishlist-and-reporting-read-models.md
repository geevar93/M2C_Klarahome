# Step 21 — Reviews, Q&A, Wishlist & Reporting read-models

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** E · **Depends on:** Step 20
- **Objective:** Social proof, saved intent, and the numbers the business runs on.
- **Deliverables:**
  - Verified-purchase reviews with ratings, images, moderation queue, vendor replies,
    helpfulness votes; aggregate rating projections.
  - Product Q&A; report-abuse workflow.
  - Wishlist / save-for-later; back-in-stock and price-drop subscriptions.
  - Reporting read-models & APIs: sales by day/category/vendor, GMV vs net revenue, AOV,
    conversion funnel, cart abandonment, top/slow SKUs, stock ageing, return rate by reason,
    settlement summary, COD vs prepaid mix.
  - Scheduled report exports (CSV) and email delivery.
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
- **Full acceptance criteria (verified at Step 29, not now):** Reports reconcile against transactional data for a seeded dataset;
  a review can only be posted against a delivered purchase.
- **Outcome / Notes:** _(to be filled on completion)_
