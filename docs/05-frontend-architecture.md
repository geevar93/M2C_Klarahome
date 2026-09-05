# 05 — Frontend Architecture (Angular, mobile-first)

> Two Angular applications in one Nx workspace: **storefront** (SSR, SEO-critical, mobile-first)
> and **admin** (SPA, data-dense, serves platform staff *and* vendors).
> Visual design is deliberately absent until Step 30 — see `10-design-system-placeholder.md`.

---

## 1. Workspace

```
src/frontend/
  apps/
    storefront/          # Angular + @angular/ssr, served by Node
    storefront-e2e/      # Playwright
    admin/               # Angular SPA, served by nginx
    admin-e2e/
  libs/
    data-access/
      api/               # GENERATED from OpenAPI — never edited by hand
      auth/              # token storage, refresh, guards, interceptors
      cart/  catalog/  orders/  content/   # feature-facing state + services
    domain/              # framework-free models, enums, money/GST helpers, validators
    ui/
      primitives/        # button, input, select, sheet, dialog, badge, skeleton...
      patterns/          # product-card, price-block, rating, address-form, data-table
      layout/            # grid, stack, container, responsive helpers
    util/                # formatting (INR, dates), storage, seo, analytics, a11y
    i18n/                # message catalogs, locale config
    testing/             # harnesses, MSW handlers, fixtures
```

Nx module boundary tags prevent, at lint time: `apps` importing another app, `ui` importing
`data-access`, and anything importing `api` except `data-access`.

---

## 2. Angular Baseline

| Decision | Choice | Rationale |
|---|---|---|
| Version | Latest stable Angular (20/21 line) | Signals, standalone, modern SSR/hydration |
| Components | **Standalone only**, no NgModules | Simpler graph, better lazy loading |
| Reactivity | **Signals** first; RxJS only where streams genuinely help (typeahead, websockets) | Less boilerplate, better change detection |
| Change detection | **Zoneless** (`provideZonelessChangeDetection`) | Measurably faster on low-end Android; forces explicit reactivity |
| State | Signal-based stores per feature (NgRx **SignalStore** where a store earns its keep) | No global store dogma; cart/auth/filters are stores, page data is not |
| Forms | Reactive forms, typed | Validation parity with backend rules |
| Routing | Lazy `loadComponent`/`loadChildren` on every feature route | Small initial bundle |
| HTTP | `HttpClient` + `fetch` backend + functional interceptors | SSR-friendly |
| i18n | `@angular/localize`, default `en-IN` | Hindi/regional deferred to Phase 2 but structurally ready |
| Styling | SCSS + **CSS custom properties as design tokens**; utility layer for spacing only | Runtime theming is a hard requirement for white-labelling |
| Component library | None in storefront (own primitives); Angular Material **admin only** | Storefront must be brandable; admin values speed |
| Icons | Inline SVG sprite, tree-shaken | No icon-font FOUT |
| Testing | Vitest/Jest + Testing Library, Playwright, MSW | — |

---

## 3. Storefront

### 3.1 Rendering strategy

| Route type | Strategy |
|---|---|
| Home, category, PLP, PDP, CMS pages, collections | **SSR with hydration** — indexable, fast FCP |
| Search results | SSR for the first page, client-side thereafter |
| Cart, checkout, account, orders | **CSR** behind auth; SSR skipped (personal, non-indexable) |
| Legal/static | Prerendered at build where content is stable, else SSR |

- **Incremental hydration** on below-the-fold blocks (`@defer (on viewport)`) so the PDP's
  reviews, recommendations and Q&A cost nothing until scrolled to.
- **TransferState** so SSR-fetched data is not re-fetched on the client.
- Server-side response caching in Redis for anonymous, non-personalised pages, keyed by
  URL + tenant + locale, invalidated on the relevant integration events.

### 3.2 Route map

```
/                                   Home (CMS blocks)
/c/:categorySlug                    Category / PLP
/collections/:slug                  Curated collection
/search?q=                          Search results
/p/:productSlug                     PDP
/cart                               Cart
/checkout                           Checkout (stepper)
/checkout/confirmation/:orderNumber Order confirmation
/account                            Dashboard
  /account/orders  /orders/:number  Orders + detail & tracking
  /account/returns /returns/:number Returns
  /account/addresses /profile /wishlist /wallet /notifications
/auth/login  /auth/register  /auth/otp  /auth/forgot-password
/pages/:slug                        CMS static/legal
/vendor/:slug                       Seller storefront page
/404  /500  /offline
```

### 3.3 Mobile-first rules (non-negotiable)

- Design and build at **360 × 640 first**; tablet and desktop are progressive enhancements.
- Breakpoints: `xs 0`, `sm 480`, `md 768`, `lg 1024`, `xl 1280`, `2xl 1536`. Media queries are
  always `min-width`.
- Touch targets ≥ 44 × 44 px; primary actions within thumb reach; sticky bottom bar for the
  PDP "Add to cart" and cart "Checkout".
- Filters open as a **bottom sheet** on mobile, a sidebar from `lg`.
- Images: `srcset`/`sizes` + AVIF/WebP via imgproxy, explicit `width`/`height` to eliminate CLS,
  `loading="lazy"` below the fold, `fetchpriority="high"` on the LCP image only.
- No hover-only affordances; every hover interaction has a tap equivalent.
- Fonts: `font-display: swap`, preloaded, subset — decided at Step 30, placeholder system stack
  until then.
- Data-saving: no autoplaying video, no carousel auto-rotation on mobile by default.

### 3.4 Performance budgets (enforced in CI, verified at Step 29)

| Metric | Budget |
|---|---|
| Initial JS (storefront, gzipped) | ≤ 180 KB |
| Route chunk | ≤ 80 KB |
| LCP (mobile, 4G, mid-tier Android) | ≤ 2.5 s |
| INP | ≤ 200 ms |
| CLS | ≤ 0.1 |
| Lighthouse Performance / SEO / Best Practices / A11y (mobile) | ≥ 90 / 100 / 95 / 95 |

A budget breach fails the build; it is not a warning.

### 3.5 SEO

- Per-route title/meta/canonical via a small `SeoService`.
- JSON-LD: `Product` + `Offer` + `AggregateRating` on PDP, `BreadcrumbList`, `Organization`,
  `WebSite` + `SearchAction`, `ItemList` on PLP.
- `sitemap.xml` (paginated, generated from the API) and `robots.txt` served by the storefront.
- Clean, stable slugs; 301 redirects served from the CMS redirect table; `hreflang` reserved
  for Phase 2 multi-language.
- Facet URLs are canonicalised — filter combinations are `noindex,follow` beyond a whitelist,
  to prevent index bloat.

### 3.6 Key UX behaviours

- **Optimistic cart** updates with rollback on server rejection, and an explicit reason banner
  (`PRICE_CHANGED`, `CART_ITEM_OUT_OF_STOCK`).
- **Guest → customer merge**: the anonymous cart is merged on login, with conflict resolution
  shown to the user.
- **PIN-code first**: delivery estimate and COD eligibility are requested on the PDP, remembered
  in local storage, and pre-applied at checkout.
- **Checkout resilience**: the payment step tolerates browser back, network drop and gateway
  cancel; the order is never lost, only its payment retried.
- **Skeletons, not spinners**, for content that has a known shape.
- **Offline/slow-network** notice; retry affordances on every failed fetch.

---

## 4. Admin Application

### 4.1 Shape

- Single SPA, no SSR; served as static files by nginx.
- **One app serves platform staff and vendors.** The navigation, routes and API scope are all
  derived from the authenticated user's permissions and `vendorId` claim. There is no separate
  vendor build to keep in sync.
- Desktop-first layout but fully usable on a tablet (vendors do dispatch on tablets), with a
  collapsible sidebar and responsive data tables.

### 4.2 Route map

```
/login (with TOTP step)
/dashboard                       role-aware KPIs
/catalog/products | /categories | /brands | /attributes | /moderation
/inventory/stock | /adjustments | /purchase-orders | /stock-takes | /warehouses
/orders | /orders/:id | /fulfilment | /shipments | /ndr
/returns | /returns/:id
/promotions | /promotions/:id | /price-lists | /tax-rates
/content/pages | /banners | /menus | /collections | /redirects
/vendors | /vendors/:id | /commission-plans          (platform only)
/settlements/cycles | /payouts | /ledger             (finance / vendor-scoped)
/reports/:key
/settings/store | /users | /roles | /feature-flags | /audit-log   (platform only)
/vendor/onboarding | /vendor/profile                 (vendor scope)
```

### 4.3 Shared admin building blocks

- **DataTable**: server-side paging/sorting/filtering, saved views, column chooser, bulk
  selection with bulk actions, CSV export, sticky header, virtual scroll for long lists.
- **FormShell**: unsaved-changes guard, server validation mapping to fields, autosave for drafts.
- **EntityDrawer / ConfirmDialog**: destructive actions require typed confirmation and a reason.
- **AuditTrail** panel embeddable on any entity detail page.
- **JobMonitor** for async imports/exports.
- **PermissionDirective** (`*hasPermission="'orders.suborder.cancel'"`) — hides UI, while the
  API still enforces the real check.

---

## 5. Cross-App Concerns

| Concern | Implementation |
|---|---|
| Runtime config | `assets/config.json` fetched before bootstrap (`APP_INITIALIZER`) → **one Docker image runs in every environment and for every tenant** |
| Auth | Access token in memory only (never `localStorage`); refresh via HttpOnly cookie; silent refresh on 401 with a single-flight queue; logout clears all |
| Interceptors | auth → correlation id → error normalisation → retry (idempotent GETs only) → loading indicator |
| Error handling | Global `ErrorHandler` → user-facing toast keyed by ProblemDetails `code`, technical detail to the log sink with the correlation id |
| Analytics | Thin `AnalyticsService` facade with a documented event taxonomy (view_item, add_to_cart, begin_checkout, purchase…) so GA4/Meta/other can be swapped or disabled per tenant |
| Accessibility | WCAG 2.2 AA target: semantic landmarks, focus management on route change and dialogs, visible focus, `aria-live` for cart/toast, keyboard-complete flows, `prefers-reduced-motion` respected |
| Security | No `innerHTML` without `DomSanitizer`; CMS rich text sanitised server-side **and** client-side; CSP with nonces; no secrets in the bundle |
| Theming | All colour/spacing/typography via CSS custom properties on `:root`, overridable per tenant at runtime — proves out at Step 30 |
| Offline/PWA | Service worker for static assets and an offline page in v1; full PWA/installability is a Phase-2 decision |

---

## 6. Component Inventory (build order aligns with Steps 23–28)

**Storefront primitives:** Button, IconButton, Link, Input, Textarea, Select, Checkbox, Radio,
Switch, QuantityStepper, Badge, Chip, Tag, Rating, Price, Skeleton, Spinner, Alert, Toast,
Modal, BottomSheet, Drawer, Accordion, Tabs, Breadcrumb, Pagination, EmptyState, Stepper,
Tooltip, ProgressBar, Divider, Avatar.

**Storefront patterns:** Header, MobileNavDrawer, SearchBar + Suggestions, MiniCart,
StickyActionBar, Footer, ProductCard, ProductGrid, ProductCarousel, FilterSheet, FilterSidebar,
SortControl, AppliedFilters, Gallery + Zoom, VariantSelector, DeliveryEstimator, OfferList,
SellerCard, ReviewList + ReviewForm, QnAList, CartLine, OrderSummary, AddressCard, AddressForm,
PaymentMethodSelector, OrderTimeline, ReturnRequestForm, CmsBlockRenderer.

**Admin patterns:** AppShell, Sidebar, TopBar, DataTable, FilterBar, SavedViews, FormShell,
MediaManager, RichTextEditor, ImageUploader, StatusBadge, TimelineViewer, KpiCard, ChartPanel,
BulkActionBar, ImportWizard, PageComposer (block list + config panel + preview), CouponBuilder,
LedgerTable, PayoutWizard.

---

## 7. Testing Strategy (frontend)

| Level | Tool | Coverage expectation |
|---|---|---|
| Unit | Vitest/Jest | Pure logic: pricing display, GST formatting, guards, validators, stores |
| Component | Testing Library | Every `ui` primitive and pattern, incl. keyboard and a11y assertions |
| Integration | Testing Library + MSW | Feature flows against mocked API contracts derived from OpenAPI |
| E2E | Playwright | Browse→buy (prepaid + COD), login/OTP, returns, vendor dispatch, admin order ops. Run on mobile viewport as the default project |
| Visual | Playwright screenshots | Introduced **at Step 30**, once the design is stable — pointless before |
| A11y | axe-core in Playwright | Zero critical violations on storefront critical paths |
