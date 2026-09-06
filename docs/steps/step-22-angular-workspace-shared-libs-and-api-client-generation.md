# Step 22 — Angular workspace, shared libs & API client generation

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** F · **Depends on:** Step 21
- **Objective:** Frontend foundations shared by both apps.
- **Deliverables:**
  - Nx workspace with apps `storefront` and `admin`; libs `ui`, `data-access`, `domain`,
    `util`, `i18n`, `testing`.
  - Typed API client **generated from the backend OpenAPI document** as a CI step (no
    hand-written DTOs).
  - HTTP interceptors: auth/refresh, correlation ID, error normalisation, loading state, retry.
  - Core services: auth/session, runtime config bootstrap (`config.json`, so one image serves
    any environment), feature flags, analytics abstraction, toast/notification.
  - Placeholder design tokens wired as CSS custom properties (per
    `10-design-system-placeholder.md`) — **strictly neutral, no branding**.
  - Testing setup: Jest/Vitest + Testing Library, Playwright for e2e, MSW for API mocking.
  - Accessibility and i18n scaffolding (`en-IN` default), currency/number/date pipes for INR.
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
- **Full acceptance criteria (verified at Step 29, not now):** Both apps build and serve; the generated client compiles against the
  live OpenAPI document; a sample authenticated request succeeds through the interceptor chain.
- **Outcome / Notes:** _(to be filled on completion)_
