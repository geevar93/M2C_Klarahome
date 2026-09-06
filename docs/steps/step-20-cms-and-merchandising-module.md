# Step 20 — CMS & Merchandising module

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** E · **Depends on:** Step 19
- **Objective:** Let the business change the storefront without a deployment.
- **Deliverables:**
  - Block-based page composer (hero, banner grid, product carousel, category tiles, rich text,
    FAQ, testimonial, custom HTML) with typed block schemas.
  - Home page layout, landing pages, static/legal pages, navigation menus, footer builder.
  - Curated collections (manual + rule-based), product ranking/pinning per collection.
  - Banners with scheduling and targeting; announcement bar.
  - Draft → Preview → Scheduled → Published workflow with version history and rollback.
  - SEO: per-page meta, Open Graph, `sitemap.xml`, `robots.txt`, structured data (Product,
    Offer, BreadcrumbList, Organization), 301 redirect manager.
  - Blog/lookbook (optional, feature-flagged).
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
- **Full acceptance criteria (verified at Step 29, not now):** A home page is composed, previewed, scheduled and published; the
  storefront renders it server-side; sitemap and structured data validate.
- **Outcome / Notes:** _(to be filled on completion)_
