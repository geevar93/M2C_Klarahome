# Step 23 — Storefront shell, SSR, routing & mobile-first layout

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** F · **Depends on:** Step 22
- **Objective:** The storefront skeleton: fast, indexable, mobile-first.
- **Deliverables:**
  - Angular SSR (`@angular/ssr`) with hydration; transfer-state caching; route-level
    prerendering where appropriate.
  - App shell: header, mobile navigation drawer, search entry, mini-cart, sticky bottom action
    bar (mobile), footer, breadcrumbs.
  - Responsive layout primitives and breakpoint strategy (mobile-first, 360px baseline).
  - Routing map with lazy-loaded feature routes and route-level data resolvers.
  - Global states: loading skeletons, empty states, error boundary, offline notice, 404/500.
  - SEO service (title/meta/canonical/JSON-LD); performance budgets enforced in CI.
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
- **Full acceptance criteria (verified at Step 29, not now):** Lighthouse mobile performance/SEO/a11y meet the NFR thresholds on
  the shell; SSR output contains meaningful HTML (verified with JS disabled).
- **Outcome / Notes:** _(to be filled on completion)_
