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
- **Outcome / Notes:**

  **The shell stands, it renders on the server, and every URL in the route map resolves.**
  `curl` against the built SSR server with no API reachable returns 24.5 kB of HTML for `/`
  carrying the header, the landmarks, the skip link and `ng-server-context="ssr"`; an unknown URL
  answers **404**, `/403` answers 403, and `/cart` is served as a client-rendered shell. Both apps
  build, 17 lint projects and 15 test projects are green, and the performance budget is now a gate
  rather than a note.

  ### What was built

  **`ui/layout` — the responsive primitives.** `khContainer` (a directive, not a wrapper element:
  `<main khContainer>` is both fewer nodes and a truer description), `kh-stack` and `kh-grid` with
  the gap taken from the 4px scale as a *type* rather than a length, and `Breakpoints` — a
  signal-based media-query service that answers **false for every breakpoint during SSR**, so the
  server always renders the 360px layout and a wider client adds to it on hydration rather than
  reflowing.

  **`ui/primitives` — the shell's own components.** `khButton` as a directive over `<button>` and
  `<a>` (the element carries meaning; a component would force the wrong one somewhere),
  `kh-icon` over an inline path registry, `kh-skeleton`, `kh-badge`, `kh-alert`, `kh-empty-state`,
  `kh-progress-bar`, `kh-toast-host` — the first renderer of the queue Step 22 built — and
  `kh-drawer`, which is a real dialog: focus moves in and is trapped, Escape closes and returns
  focus to whatever opened it, the page behind does not scroll, and it is **not in the DOM when
  closed**, so nothing inside it is focusable or server-rendered.

  **`ui/patterns` — the shell.** `SiteHeader` (burger, wordmark, inline nav from `lg`, account,
  cart with its count in the button's accessible name), `MobileNavDrawer` (two levels, `<details>`
  so it works before hydration), `SearchEntry` (a real `<form role="search">`, so Enter works with
  no JavaScript and a phone keyboard shows a Search key), `MiniCart`, `StickyActionBar` with the
  service and directive a page registers its primary action through, `SiteFooter`, `Breadcrumbs`
  and `OfflineNotice`. **Every one of them takes its data as an input** — the `ui` layer may not
  import `data-access`, and that boundary is what lets a header be rendered in a test with three
  lines of setup.

  **`util` — `SeoService`, `BreadcrumbTrail`, `NetworkStatus`, `featureFlagGuard`.** The SEO
  service writes title, description, canonical, Open Graph and JSON-LD into the document Angular is
  rendering — so the tags are in the HTML a crawler receives — and **removes what it wrote**, which
  is the half most implementations miss: a stale canonical tells a crawler two URLs are the same
  page. `allowIndexing: false` overrides any page that asks to be indexed, and the SEO config
  falling back on error yields `noindex`, so a deployment we know nothing about does not rank.

  **`data-access` — `StoreContentService`, `StoreConfigService`, `CartSummaryStore`,
  `provideKlaraHomeErrorHandling`.** The shell's reads are cached for the life of the page and
  never fail loudly (a header whose menu 404s is degraded; one that throws is a blank shop); a CMS
  page read does the opposite, because the route has to know. The global `ErrorHandler` tells the
  customer once and **does not re-report an `ApiError`**, which the interceptor has already shown.

  **The app.** The full route map from `05-frontend-architecture.md` §3.2 — every route
  `loadComponent`, `data.seo` merged down the tree and applied on `ResolveEnd` (before the page is
  constructed, so a page with real data wins), `data.breadcrumb` deriving the whole trail. The
  render-mode split is real: **Server** for the indexable pages, **Client** for cart, checkout,
  account and auth. Nothing is prerendered — a prerender runs at build time, where there is no API.

  ### Three decisions worth reading

  1. **`**` answers 404, not 200.** Because every real route is enumerated in
     `app.routes.server.ts`, the catch-all genuinely means "unknown", so it declares `status: 404`.
     A soft 404 is how a store keeps every dead URL in the index. The cost is that a route added to
     `app.routes.ts` and forgotten in the server table silently 404s — parked, with a `TEST_DEBT`
     row asking Step 29 for a test that diffs the two tables.
  2. **The transfer cache is an allow-list.** Angular skips any request carrying credentials by
     default, and every call here carries them, so a transfer cache was impossible without turning
     that off. `core/transfer-cache.ts` names the public paths that may travel inside the rendered
     document and states the test to apply before adding one: *if an edge cache served this
     document to somebody else, would anything be wrong?* `/store/content/banners` is excluded by
     name — it is the one content read that varies by sign-in.
  3. **Twenty routes share one `PlaceholderPage`.** Each route declares its heading and the step
     that fills it. The map is complete and navigable now, and Steps 24–25 land as a
     `loadComponent` line rather than a deletion.

  ### Two things found by running it

  - **Express 5** landed (the Step 5 parking-lot row assigned it here): `app.use('/**')` became a
    pathless middleware, and the `qs` override is gone with `npm audit` still at 0 vulnerabilities.
  - **`AngularNodeAppEngine` refuses every request whose `Host` it does not recognise, and the
    allow-list is empty by default** — the built server answered `400` to everything. Now
    configured by `KH_ALLOWED_HOSTS` and `KH_TRUST_PROXY_HEADERS`, both documented in
    `.env.example`. **Step 32 must set them**, or the container answers 400 at its real hostname.

  ### The budget is now a gate

  `ci.ps1 -Stage frontend` gzips the initial bundle — the scripts and modulepreloads the entry
  document names — and every route chunk, and fails over budget. Storefront **123.5 kB gzipped
  against 180 kB**; largest route chunk 1 kB against 80 kB; admin 81.9 kB. Proved to fail by
  lowering the number to 100 and watching it go red. This is the check the Step 22 parking-lot row
  prescribed (Angular measures raw bytes, the document states gzipped), and it closes the size half
  of that `TEST_DEBT` row; the Lighthouse half stays open for Step 29.

  ### Known gaps

  Banners and the announcement bar have no slot yet (Step 24); CMS redirects are not consulted;
  the mini-cart is read-only until Step 25 owns cart mutation; no service worker (Step 31);
  incremental hydration is enabled before anything defers. **Nothing is proved by a test that a
  machine runs**: 20 `TEST_DEBT.md` rows, including both headline criteria and every accessibility
  behaviour the drawer and the focus management claim.
