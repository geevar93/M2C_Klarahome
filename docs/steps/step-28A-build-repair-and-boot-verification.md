# Step 28A — Build repair & boot verification

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** H · **Depends on:** Step 28
- **Objective:** Take everything written during the MVP build sprint (Steps 9–28) and make it
  **compile, migrate, boot and answer** as one system. This is the first time the whole thing is
  assembled, and it is the step that ends the sprint.
- **Why it exists:** Steps 9–28 deliberately deferred verification
  (`IMPLEMENTATION_PLAN.md` §3). Twenty steps of independently-compiled modules will not
  necessarily start together: DI registrations collide, migrations conflict, options bind to
  nothing, module boundaries drift. Fixing that is a job, and it gets its own step so it is
  budgeted rather than discovered.

- **Deliverables:**
  - **Compiles clean.** `pwsh tools/ci.ps1 -Stage build` succeeds for the whole backend solution;
    `pwsh tools/ci.ps1 -Stage frontend` builds both Angular apps. Zero errors, and analyzer
    warnings triaged rather than suppressed.
  - **Format and lint pass.** `pwsh tools/ci.ps1 -Stage format` and `-Stage lint` are green.
  - **Migrations apply.** Every module's EF migration applies in order against a **fresh**
    database via the migrator container, and a second run is a no-op. Ordering conflicts,
    duplicate schema/table names and cross-module FK gaps resolved here.
  - **The stack boots.** `docker compose` brings up Traefik, Postgres, Redis, MinIO, imgproxy,
    Mailpit, the API, the worker and the migrator. Every health check is green; the worker drains
    its queues; no container is in a restart loop.
  - **The surface answers.** The OpenAPI document generates for both surfaces (`/store`, `/admin`)
    without duplicate operation ids or unresolvable schemas; the storefront and admin apps serve
    and reach the API.
  - **A smoke walk, by hand.** One pass through the MVP journey against the running stack — sign
    in, list a catalog page, add to cart, place an order, see it in admin. Done with `curl` or the
    browser, **not** as an automated test. Where it breaks, fix or record.
  - **CI floors restored.** `tools/ci.ps1` per-suite minimums updated to the real post-sprint
    counts, and the coverage minimum documented with the number Step 29 must restore it to.
  - **A repair record.** Every defect found and fixed here, listed in Outcome / Notes, so Step 29
    knows which areas were fragile.

- **Explicitly out of scope:**
  - **No new features.** Anything missing from Steps 9–28 is a gap to record, not to build. If a
    deliverable was genuinely not written, reopen that step with the User — do not fill it in here.
  - **No new tests.** A failing behaviour found by hand is fixed and written into
    [`TEST_DEBT.md`](../TEST_DEBT.md); the test that would have caught it is Step 29's.
  - **No aesthetics** (rule 8 still holds until Step 30).

- **Acceptance criteria:**
  1. `pwsh tools/ci.ps1 -Stage build`, `-Stage format`, `-Stage lint` and `-Stage frontend` all
     pass from a clean checkout.
  2. The migrator applies every migration against an empty database, and re-runs as a no-op.
  3. The full dev stack comes up with all health checks green and stays up.
  4. The MVP smoke walk completes end to end by hand.
  5. Every defect fixed is recorded in Outcome / Notes; everything not fixed is in
     [`PARKING_LOT.md`](../PARKING_LOT.md) or [`TEST_DEBT.md`](../TEST_DEBT.md) with an owner step.

- **Outcome / Notes:** _(to be filled on completion)_
