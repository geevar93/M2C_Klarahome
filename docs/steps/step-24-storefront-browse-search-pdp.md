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
- **Outcome / Notes:**

  **Status: ✅ DONE (2026-09-06).** The discovery journey is built end to end in code: home, category,
  collection, search, seller and product pages, the faceted listing behind them, add-to-cart and the
  wishlist. Both apps build; 19 lint and 17 test projects are green; the gzipped bundle gate passes
  at **149.7 kB against 180 kB**, with the largest route chunk at 5.9 kB. The built SSR server was
  run and driven with `curl`: `/` and `/search?q=lamp` answer 200 with real server-rendered HTML,
  `/404` and an unknown URL answer a genuine 404, and the log is clean.

  ### What was built

  **The listing, once, for four pages.** `pages/listing/product-listing.ts` is the body of the
  category page, the search page and the seller page — the same endpoint, the same facets, the same
  keyset paging, differing only in where the narrowing comes from. Two implementations would have
  drifted by the second sprint.

  **The URL is the listing's state** (`core/listing-query.ts`). Filters, sort and query live in the
  query string and nowhere else, so a filtered listing can be shared, opened in a new tab and undone
  with the applied-filter chips. Three things have to agree about that mapping — the URL the page
  writes, the request the service builds, the panel that reads back what is selected — which is why
  it is one pure module, and why `toSearchQuery` carries the step's only new unit test (8 cases).
  The **cursor is deliberately not in the URL**: paging is keyset, and `?cursor=` in a shared link
  opens somebody else's page four with no page one above it.

  `applyFacet` is where the facet vocabulary meets the filter vocabulary, because they are not the
  same: `availability` is written `inStock`, a `price` band becomes `minPrice`/`maxPrice` and
  *replaces* rather than accumulates, `rating` and `discount` are thresholds, and `attr.*` is open —
  its names are a merchandiser's, not a compile-time list.

  **The PDP decides what arrives when.** Resolved, and therefore in the SSR HTML: the product, its
  variants, the buy box, the price, and the `Product` + `Offer` + `AggregateRating` structured data
  built from them. Deferred to the viewport: reviews and questions. Deferred to a tap: the other
  sellers' offers. Deferred to the browser: the delivery estimate and the recently-viewed rail,
  because both read `localStorage` and **a server-rendered document carrying one visitor's PIN code
  could not be edge-cached at Step 32**. The variant lives in `?variant=` rather than in a field, so
  a shared link carries the colour and size that were chosen — while the canonical stays the
  product's own URL, because letting each variant claim one is how a product becomes nine competing
  pages in an index.

  **The home page is a CMS document**, not a hard-coded page. `CmsBlockRenderer` draws seven of the
  eight block types; `CustomHtml` is deliberately not among them — it is raw markup from an editor,
  and the home page every visitor lands on is the one place this storefront will not inject
  unreviewed HTML. An unknown block type renders nothing rather than an error, so a storefront that
  has not been redeployed since a new block shipped still renders the rest of the page. The same
  renderer now draws `/pages/:slug`, replacing the honest note Step 23 left there.

  ### Files

  - **`ui-primitives`** (six new): `Price` (MRP as a real `<s>`, percentage computed here so the
    card and the PDP cannot disagree), `Rating` (stars decorative, the sentence beside them the
    accessible name), `Chip`, `QuantityStepper` (a real `<input type="number">`, not two buttons and
    a label), `Disclosure` (`<details>`, so the content is findable and crawlable while collapsed),
    `ProductImage` (the box reserved before the bytes arrive; `priority` opt-in for the one LCP
    image).
  - **`ui-patterns`** (eleven new + `catalog.model.ts`): `ProductCard`, `ProductGrid`,
    `ProductCarousel` (scrolls, never rotates), `FilterPanel`, `ListingToolbar`, `ProductGallery`,
    `VariantSelector` (with `deriveAxes`/`resolveSelection` as exported pure functions),
    `DeliveryEstimator`, `OfferList`, `SellerCard`, `ReviewList`, `QuestionList`,
    `CmsBlockRenderer`, `SearchSuggestions`; `SearchEntry` gained the combobox keyboard model.
  - **`data-access`**: `StoreCatalogService` and `ProductSearchService` (catalog);
    **`data-access-engagement`** and **`data-access-delivery`**, both new; `CartActions` (cart);
    `StoreContentService.collection`.
  - **`util`**: `ImageUrls`; `BreadcrumbTrail.setAncestors`.
  - **`apps/storefront`**: `core/listing-query.ts`, `core/catalog.mapper.ts`,
    `core/cms-block.mapper.ts`, `core/recently-viewed.store.ts`, `core/recent-searches.store.ts`,
    four new resolvers; `pages/home`, `category`, `collection`, `search`, `product`, `vendor`, and
    `pages/listing/product-listing.ts`.

  ### Deviations and decisions

  - **The listing's query string is passed through a new `ApiRequestOptions.params` escape hatch.**
    `GET /store/products` declares no query parameters in the OpenAPI document — it reads its
    filters off the request by prefix, because attribute filter names are merchandising data — so
    the generated `storeSearchProducts` takes no query at all and there was nothing to call. The
    open half of that query string can never be declared; **the closed half should be**, and that is
    a backend change plus a regeneration, parked rather than done inline.
  - **The PDP does not show the brand.** `StorefrontProduct` carries `brandId` and no brand name,
    while the search projection carries `brandName` — so a card shows the brand and the product page
    does not. Inconsistent, parked, not papered over.
  - **"Related products" is not built.** There is no recommendations endpoint (Phase 2), so the PDP's
    rail is *recently viewed* only. Naming it "related" and filling it from the same category would
    have been a guess dressed as a feature.
  - **Asking a question and writing a review are not built** — both need the account surface Step 25
    builds. The read side is complete; the control says so rather than doing nothing.
  - **Wishlist toggling is optimistic with rollback**, and a signed-out visitor is told to sign in.
    There is no anonymous wishlist on the API, and there should not be: a saved item has to survive
    a device change.
  - **`data-access-catalog`'s generator stub was deleted** rather than left unexported (the
    `data-access-content` precedent), because a real spec now takes its place.
  - **An `NG0600` was found by running the built SSR server**, not by the unit tests: two stores
    hydrated `localStorage` lazily, and the header's suggestion list reads them from a `computed` —
    a write inside a computed, which Angular refuses. Both now read in their constructors.

  ### Known gaps

  Everything the full acceptance criteria demand and nothing proves is in
  [`../TEST_DEBT.md`](../TEST_DEBT.md) — **28 rows for this step**, including both headline criteria.
  Nothing here has been driven against a live API, in a real browser, or at 360 × 640. Out-of-step
  discoveries — the undeclared query string, the missing brand name, the bundle headroom, the raw
  Angular budget that now warns on every build, `imageBaseUrl` being blank in `config.json` — are in
  [`../PARKING_LOT.md`](../PARKING_LOT.md).
