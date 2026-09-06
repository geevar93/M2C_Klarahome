# Step 24 — Storefront: browse, search, PDP

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** F · **Depends on:** Step 23
- **Objective:** The discovery journey.
- **Deliverables:**
  - Home page rendering CMS blocks; category landing pages.
  - Product listing page: mobile filter sheet, facets, sort, infinite scroll or pagination,
    URL-synced state, result count, no-results recovery.
  - Search with autocomplete and recent/trending searches.
  - Product detail page: responsive gallery/zoom, variant selection, price with MRP/discount,
    tax note, delivery estimate by PIN code, offers, vendor/seller info, stock messaging,
    specifications, reviews and Q&A, related/recently viewed.
  - Add-to-cart interactions and wishlist.
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
- **Full acceptance criteria (verified at Step 29, not now):** The full browse → search → PDP → add-to-cart journey works on a
  360px viewport; the PDP is server-rendered with correct structured data.
- **Outcome / Notes:** _(to be filled on completion)_
