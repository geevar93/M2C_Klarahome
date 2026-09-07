# Step 28 — Admin: promotions, CMS, reports + Vendor portal

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** G · **Depends on:** Step 27
- **Objective:** Merchandising, analytics, and the vendor-facing experience.
- **Deliverables:**
  - Promotions: coupon and cart-rule builder with live preview/simulator, scheduling,
    usage analytics.
  - CMS: page composer UI with block library, drag-order, preview, scheduling, version history,
    menu/banner/collection management, redirect and SEO tools.
  - Reports dashboards (placeholder-styled charts only; visual polish deferred to Step 30).
  - Vendor portal (same app, vendor-scoped roles): onboarding/KYC, listings, inventory,
    orders and dispatch, returns, ledger and settlement statements, performance metrics.
  - Platform administration: users/roles, vendors, commission plans, settings, feature flags,
    tax rates, shipping zones, audit log.
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
- **Full acceptance criteria (verified at Step 29, not now):** A vendor completes a full day's operations in the portal; a
  merchandiser publishes a campaign (coupon + banner + collection) without engineering help.
- **Outcome / Notes:**

  **The last build step, and the one that empties the declaration.** Step 26 declared 42
  destinations, each carrying its permissions and its scope, and said that landing a screen should
  be a one-line diff. Step 27 proved it for eighteen. This step turned the remaining **twenty-four
  `step:` markers into `load:`** and added seven more destinations beside them, so
  `core/navigation.ts` now holds **52 destinations and 52 `load:` functions**: for the first time
  since Step 26 there is no placeholder left in the back office. The `step` field stays in
  `AdminDestination` because the arrangement is the point, not because anything still needs it.

  ### What was built

  **31 screens**, plus 5 supporting components and 3 vocabulary files (39 files, ~18,000 lines),
  over **8 new services** in `data-access-admin` and **2 new building blocks** in `ui-admin`
  (~2,000 lines).

  | Section | Screens |
  |---|---|
  | Pricing | Promotions list, the promotion builder, price lists, price-list rows, tax rates |
  | Content | Pages, the page composer, banners, menus, the menu editor, collections, collection detail, redirects |
  | Sellers | The seller directory, the seller record, commission plans |
  | Finance | Settlement cycles, payout runs, one payout run, the ledger |
  | Insight | The report catalogue and schedules, one report |
  | Your seller | Getting set up, the seller profile, how you are doing |
  | Settings | Store settings, shipping zones, users, one user, roles, feature flags |

  New services: `PricingAdminService`, `ContentAdminService`, `VendorsAdminService`,
  `SettlementsAdminService`, `ReportingAdminService`, `IdentityAdminService`,
  `PlatformSettingsService`, `ShippingZonesService`. `DocumentPrintService` gained the two CSV
  exports that stream bytes behind the bearer token. New `ui-admin` blocks: `ReportChart` and
  `ReorderList`.

  ### The ideas the screens are built on

  **The simulator is what makes the rule builder honest.** A promotion is a small program — a
  mechanic, a scope, a set of conditions, a stacking rule and a priority — and the only way to know
  what it does is to run it. `POST /admin/promotions/simulate` runs it through **the same quote
  engine that runs at checkout** (Step 12's `IPriceQuoteEngine`), so what the builder shows is not
  an approximation of the discount: it is the discount. The whole quote comes back, including
  **every promotion the engine considered with its reason for not applying**, which is the question
  the screen exists to answer. Nothing is saved by simulating and the promotion need not exist yet.

  **The composer's form is a schema the server sent.** `GET /admin/content/block-types` answers
  each block type with its fields, their kinds, whether each is required and what it accepts, and
  the page composer renders a control per field from that. Step 20 made those schemas data for
  exactly this reason; the consequence is that **there is no `switch (block.type)` anywhere in the
  composer**, and a block type added on the server appears in the library with no change to this
  app. The same idea, one level up, drives the report screens: the report catalogue is data, so a
  report added on the server gets a tile, a runner and a schedule option for free.

  **Money screens read the machine rather than copying it.** A payout batch's buttons are its own
  `nextStatuses`; a page's transitions are its own `allowedTransitions`. Maker–checker is not
  implemented here at all — creating a run and approving it are two calls because Step 18 refuses
  the same person in the handler, the aggregate *and* a database `CHECK`, and the screen simply
  offers the edges and shows the refusal. Nothing on the ledger screen sets a balance, because
  `Σ credits − Σ debits` has no column to set: the one write it offers is a signed adjustment
  carrying its reason.

  **TCS and TDS are never added together.** They sit on two different bases — net taxable supplies
  under CGST s.52, gross sales under s.194-O — and every screen that shows them shows them apart
  and says why.

  **The vendor portal is the same endpoints seen from the other side.** Four panels (KYC, bank
  accounts, pickup addresses, serviceable regions) are shared between the platform's seller record
  and a seller's own onboarding, with `canVerify` as the only difference: a seller submits a
  document, the platform verifies it. `readiness` is the spine of both — the server says what is
  outstanding and every panel re-asks for it after a change, which is what stops a seller
  submitting into a refusal they cannot diagnose. A seller's operational screens are the shared
  ones; they were already non-`platformOnly` from Step 26.

  **Charts are drawn from the data, not from a preference.** `ReportChart` takes a line for a
  report grouped by day and bars for one grouped by anything else, starts every value axis at zero,
  thins its own axis labels, and carries the same numbers as a table for anybody who cannot read a
  picture. It is placeholder-styled, as Step 30 requires.

  ### Deviations, and one thing done outside the deliverables

  - **`ReportingAdminService.table()` casts through `unknown`.** `GET /admin/reports/{key}`
    declares two 200 bodies — `ReportResult` for `format=json` and `ReportRunResponse` for
    `format=csv` — and OpenAPI carries one, so the generator typed the operation as the CSV answer
    and never emitted `ReportResult`. The request is correct and only the declared response is
    wrong, so the lie is told once, in a named place, behind a hand-written `ReportTable`. Parked.
  - **`vendor-vocabulary.ts` holds a client-side copy of the onboarding transitions.** A vendor
    record carries no `nextStatuses`; the six transitions are six endpoints. The API refuses an
    edge it does not allow and the screen shows the refusal, so the copy is not unsafe — but it is
    the only such copy left in the back office. Parked.
  - **The shell now names a vendor user's seller.** The Step 26 parking-lot row asked for this and
    named this step; `ShellLayout` makes one request to `GET /admin/vendors/me` and falls back to
    the shortened id. Done here rather than parked again because "the seller portal names the
    seller" is part of the vendor-portal deliverable. It is the only change to a Step 26 file.
  - **The shipping rate card is read-only.** Zones are editable; rate bands are eleven numeric
    fields each and belong in an editor of their own. `createRate`/`updateRate` exist in the
    service and no screen calls them. Parked — it means shipping prices have to be seeded.
  - **A CMS block's repeated items are edited as JSON.** `itemFields` declares their schema and a
    schema-driven repeater was not built. Honest rather than pretending; parked.
  - **A collection's pinned items can only be edited a page at a time**, because the endpoint
    replaces the whole pinned set. Parked.
  - **Settings are edited by the shape of the value.** There is no settings-schema endpoint, so the
    form is derived from the JSON the API returned — it knows a field is a number and not that the
    number must be positive. Every rule is the API's, and refusals are shown against the section.
    Parked; the durable fix is the arrangement the block types already have.
  - **Seven places still ask for a raw identifier** because `ui-admin` has no entity picker. The
    endpoints to build one all exist. Parked.

  ### Numbers

  - `npx nx build admin` clean, no warnings. **131.9 kB gzipped initial** against a 300 kB budget
    (114.5 kB at Step 27); largest route chunk **11.5 kB** (the promotion builder) against 120 kB.
    `pwsh tools/ci.ps1 -Stage frontend` passes both budgets for both apps.
  - `nx run-many -t lint,test --all`: **23 projects, 44 tasks, all green.** The storefront is
    untouched and still builds at 162.3 kB gzipped.
  - No backend file changed.

  ### What is not proved

  Nothing on these screens has been run against an API, a database, a gateway, a courier or a
  browser. **33 rows in [`../TEST_DEBT.md`](../TEST_DEBT.md)**, including both halves of the
  headline criterion — a merchandiser publishing a campaign, and a seller completing a day's
  operations in the portal.
