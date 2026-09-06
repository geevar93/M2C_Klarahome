# Step 26 — Admin app shell, auth & RBAC navigation

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** G · **Depends on:** Step 25
- **Objective:** The back-office foundation, serving both platform staff and vendors.
- **Deliverables:**
  - Admin SPA shell: login with 2FA, session handling, responsive layout (desktop-first but
    usable on tablet), navigation driven by the user's permissions.
  - Reusable admin building blocks: data table (server-side paging/sorting/filtering, column
    config, bulk actions, export), form framework, drawer/modal patterns, file uploader,
    audit-trail viewer, confirmation patterns for destructive actions.
  - Global search, notifications centre, impersonation (audited) for support.
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
- **Full acceptance criteria (verified at Step 29, not now):** Two users with different roles see correctly different navigation
  and are blocked from unauthorised routes and API calls.
- **Outcome / Notes:** _(to be filled on completion)_
