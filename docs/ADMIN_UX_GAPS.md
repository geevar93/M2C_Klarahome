# Admin back office — UX gap list (2026-09-20)

Found by a page-by-page audit of `apps/admin` against the house conventions (page header → load
error alert → skeleton → data table with filter bar; `kh-confirm-dialog` for anything destructive;
`ToastService` on every action; `describeError` for every failure; `*khHasPermission` on every
mutating control; `core/format.ts` for every date and amount; `*-vocabulary.ts` for every enum).

Status: ☐ open · ☑ done · ⏳ parked (needs API work or is a feature, not a fix).

## A. Cross-cutting (fix once, lands everywhere)

| # | Gap | Where | Status |
|---|-----|-------|--------|
| A1 | `describeError` lags the storefront: prints `AUTH_*` server wording (an account-enumeration oracle on the login page) and has no per-status category sentences or offline sentence | `core/describe-error.ts` | ☑ |
| A2 | Session expiry is silent: when refresh fails the store clears but nothing navigates or explains; the login page never says why you were sent there | `core/shell.layout.ts`, `pages/auth/login.page.ts` | ☑ |
| A3 | `[dirty]="true"` hard-coded on `kh-form-shell` in 14 drawers/forms, so "Unsaved changes" is always lit and means nothing; `kh-entity-drawer` has no `dismissible`, so Escape/backdrop discards a part-filled form | brands, categories, attributes, suppliers, warehouses, variant-editor, banners, redirects, commission-plans, vendor/profile, roles, shipping-zones ×2, price-lists, tax-rates | ☑ |
| A4 | Permission gating applied to "New" but not to the edit/delete path beside it, or absent entirely | products (New/Import/bulk), brands, categories, attributes, fulfilment (all), ndr (Decide), price-lists (all), tax-rates (edit/delete), commission-plans (row edit), shipments (print/sync), return-detail (Close), vendor-detail sub-panels (`canManage=true`) | ☑ |
| A5 | Raw enums reach the screen where a vocabulary exists | users + user-detail (`Vendor` → Seller), vendor-detail + vendor/profile (constitution), pages.page (kind), commission-plans (planType), return-detail (refundMode), order-detail timeline (actorType) | ☑ |
| A6 | Raw ids shown where a name or link is available | users/user-detail `vendorId`, price-list-detail `listingId`, promotion redemptions `orderId`, fulfilment parcel `id`, adjustments `actorId`, ledger `vendorId`, vendors list `commissionPlanId`, vendor-detail staff `userId`, reports runs `reportKey`, shipping-zones override seller | ☐ |
| A7 | Data table: filtered-empty state has no "Clear filters"; "about N in total" and KPI tile values are unformatted numbers | `libs/ui/admin` data-table, kpi-card, dashboard | ☑ table offers Clear the filters (wired on products, orders, shipments, returns, users); counts and KPI values formatted |
| A8 | Filters never written to the URL, so a filtered queue cannot be shared or restored by Back | every list page | ⏳ design decision (filter-bar docs say URL; no page does it) |
| A9 | Modal Escape only fires with focus inside the panel; the top-bar account menu has no outside-click/Escape dismissal | `modal.ts`, `admin-top-bar.ts` | ☑ |

## B. Destructive or irreversible actions without a confirm / reason

| # | Gap | Where | Status |
|---|-----|-------|--------|
| B1 | Bulk "Archive" on the product list fires straight through | products.page | ☑ |
| B2 | Price row "Remove" deletes immediately, no toast | price-list-detail | ☑ |
| B3 | KYC "Approve" (unlocks trading, no un-approve) has no confirm; Reject beside it does | kyc.panel | ☑ |
| B4 | Sub-order cancel passes `requireReason=false` and no phrase, so the audit trail gets nothing; status transitions fire on click with the reason always null | order-detail | ☑ cancel takes a typed sub-order number and a reason; per-status transitions left one-click (reversible edges outnumber the rest) |
| B5 | "Close the current period for every seller" goes through a plain modal: no phrase, no reason when forced | settlement-cycles | ☑ |
| B6 | Ledger adjustment (an irreversible append) posts with no confirm and accepts a zero amount | ledger | ☑ |
| B7 | Negative stock adjustment / transfer, variant Deactivate, offer Deactivate, banner Switch off, single-session End, return Close, menu item with children removed, commission plan default/active change, tax-rate Active untick, NDR return-to-origin | adjustments, variant-editor, product-detail, banners, profile, return-detail, menu-editor, commission-plans, tax-rates, ndr | ☑ banner switch-off, menu item with children, negative stock adjustment, single session end; variant/offer deactivate, return close, commission plan default, tax-rate active, NDR return-to-origin still one-click |
| B8 | Closing the stock-take count drawer or the PO draft drawer discards every typed line | stock-takes, purchase-orders | ☑ |

## C. Missing loading / empty / error states

| # | Gap | Where | Status |
|---|-----|-------|--------|
| C1 | Shipments "courier messages that failed" table never shows an error: a failed fetch reads "Nothing has failed" | shipments | ☑ |
| C2 | Secondary fetches swallowed into `[]` → false empty states | reports (schedules), vendor/performance (definitions), roles (permission catalogue), shell (notification count) | ☑ |
| C3 | Product offers table says "No seller has listed this" while loading; stock ledger drawer says "Nothing has moved" while loading; profile sessions list has no skeleton | product-detail, stock, profile | ☑ |
| C4 | No empty state at all | feature-flags, shipping-zones (zero zones), reports catalogue, ledger "work it out" panels | ☑ feature flags, shipping zones; reports catalogue and ledger panels left |
| C5 | Errors rendered in the page-level alert *behind* the open modal/drawer | stock (adjust, settings), purchase-orders (draft, receive), stock-takes | ☑ |

## D. Validation and feedback

| # | Gap | Where | Status |
|---|-----|-------|--------|
| D1 | Password minimum is 8 on the login set-password step but 12 on profile and forgot-password | login.page | ☑ |
| D2 | Ledger `ENTRY_TYPES` values are snake_case but the enum is PascalCase: the Kind filter matches nothing and labels fall through raw | ledger | ☑ |
| D3 | Choosing a seller for a statement does not refilter the entries table | ledger | ☑ |
| D4 | Notifications "Queued" column prints the raw ISO timestamp | notifications.page | ☑ |
| D5 | Tax-rates HSN search is dead code (`searchable=false`, reads a filter that does not exist); notifications search is off; audit-log promises actor/action filters it lacks | tax-rates, notifications, audit-log | ☑ tax-rates and audit-log search; notifications recipient search ⏳ (API has no recipient filter) |
| D6 | Large detail forms skip the validators their create dialog uses | vendor-detail legal record (gstin/pincode/required), collection-detail (name/slug), reports schedule (name/key), users create (vendorId required for Vendor) | ☐ |
| D7 | Fields with no inline validation: redirects `toPath`, page-composer schedule `publishAt`, banners alt text, feature-flags percentage clamp, shipping-zones PIN ranges silently dropped, serviceable-regions rows silently dropped, return approve amount / QC quantity, PO rejected quantity without reason | as named | ☐ |
| D8 | No success toast | variant-editor (save, activate), price-lists toggle, promotion-detail toggle, kyc reject, bank-accounts (primary/remove), pickup-locations remove, collection-detail remove, reports delete schedule | ☑ |
| D9 | No busy label while saving | stock (adjust/settings), adjustments, feature-flags, vendor-detail ×2, collection-detail rule, page-composer transition | ☑ |
| D10 | Export CSV reuses the Import modal, so an export is headed "Import products" | products.page | ☑ |
| D11 | Promotion detail and collection detail are large forms with no unsaved-changes guard; promotion `[dirty]` hard-coded | promotion-detail, collection-detail, navigation.ts | ☑ |
| D12 | vendor/profile "Undo changes" on one form resets the other form too | vendor/profile | ☑ |
| D13 | Users create dialog is not a `<form>` (Enter does not submit) | users.page | ☑ |
| D14 | Profile: "End the other 1 session(s)" pluralisation; session timestamps date-only; TOTP secret has no copy control | profile.page | ☑ plural, timestamps and a confirm on End; TOTP copy control left |

## E. Missing cross-entity links

| # | Gap | Where | Status |
|---|-----|-------|--------|
| E1 | Order → its shipments; shipment → its order; return → its order; NDR row → order; returns list → order; fulfilment queue → order | order-detail, shipments, return-detail, ndr, returns, fulfilment | ☑ shipment → order, return → order; NDR/returns list/fulfilment rows carry no order id (API) and order → shipments needs URL filters (A8) |
| E2 | Seller detail → its products, ledger, payouts, cycles; payout item → seller; settlement cycle → seller and payout run; ledger row → seller | vendor-detail, payout-detail, payouts, settlement-cycles, ledger | ☑ payout item → seller, cycle → seller and payout run; seller → its ledger/payouts needs URL filters (A8) |
| E3 | Stock row → product; warehouse stock count → stock list; supplier → its POs; collection item → product | stock, warehouses, suppliers, collection-detail | ☑ collection item → product; stock row carries no product id, warehouse/supplier links need URL filters (A8) |
| E4 | Order list customer name is inert (no customer route exists) | orders | ⏳ |

## F. Thin screens (features, not fixes)

| # | Gap | Where | Status |
|---|-----|-------|--------|
| F1 | A Draft purchase order cannot be edited after save; the only path is cancel-and-retype | purchase-orders | ⏳ |
| F2 | Attribute sets are read-only everywhere though categories require choosing one | attributes | ⏳ |
| F3 | Seller/listing pickers exist but four screens still ask for a pasted GUID | commission-plans, report-runner, price-lists, promotion simulator | ☐ |
| F4 | Page composer preview is raw JSON (already parked in PARKING_LOT) | page-composer | ⏳ |
| F5 | Fulfilment pick list is unbounded with no print/export despite its docstring | fulfilment | ⏳ |
| F6 | Return detail has no timeline; order detail and product detail do | return-detail | ⏳ |
| F7 | Product listings capped at 50 with no pager; adjustments search capped at 10 with no count; failed courier events fixed at 10 | product-detail, adjustments, shipments | ☐ |
| F8 | No Seller column on the moderation queue or product offers; no supplier/warehouse columns on the PO list; no warehouse column on stock takes | moderation, product-detail, purchase-orders, stock-takes | ☐ |

---

# Second audit — 2026-10-05, `feat/admin-revamp` (after the prototype-matching revamp)

Walked every screen signed in as `platform-admin` at 1366 px and at 390 px (Playwright, real UI
sign-in). Forty-six screens incl. detail pages. Severity: **P1** misleads or blocks · **P2** real
friction · **P3** polish. Owner: [FE] frontend · [BE] API/read model · [BOTH].

## G. Cross-cutting (fix once, lands everywhere)

| # | Sev | Gap | Where | Status |
|---|-----|-----|-------|--------|
| G1 | P2 [FE] | Filtered-empty copy with no filter applied: every list says "No <thing> matches these filters" (and sometimes offers "Clear filters") when the user has filtered nothing — banners, collections, price lists, purchase orders, stock takes, suppliers, moderation, tax rates. Honest copy is "No banners yet" + the create CTA. Where a *default* filter is on (returns, sellers) say what it is: "No returns are waiting for a decision · Show all". | `kh-data-table` empty state + callers | ☑ Done: empty copy derived honest when unfiltered; default-filter pages (returns, sellers) say so + "Show all". |
| G2 | P1 [FE] | Phone card rows render *every* column as a label/value line, so one order is ~440 px, one product ~200 px and ten rows fill 2000 px. Card view needs a compact mapping — title column, status, one or two key values — with the rest behind the row link. | `kh-data-table` card mode; orders, products, stock, shipments, fulfilment | ☑ Done: `DataTableColumn.card` / `.kh-cards-extra` compact phone cards on orders, products, stock, shipments, returns, to-pack. |
| G3 | P2 [FE] | Danger-weight controls everywhere a secondary one would do: spec-row delete ×4 (solid red), profile "End" ×9, page composer "Delete" at primary weight beside Save, feature-flag "Switch on" ×12 as filled primaries. One primary per view. | product-detail, profile, page-composer, feature-flags | ☐ |
| G4 | P3 [FE] | Native `<input type=date>` shows mm/dd/yyyy from the browser locale; India expects dd/mm. Hint text, or the shared date control. | returns, ledger, reports, tax-rates | ☐ |
| G5 | P2 [FE] | Phone top bar has seven controls (menu, home, search, create, theme, bell, avatar). Theme toggle and the home pill move into the avatar menu on < 768 px. | `admin-top-bar` | ☐ |
| G6 | P3 [FE] | Status quick-chip row clips on the right at 390 px with no scroll affordance (fade or arrow). | `kh-filter-bar` | ☑ Done: right-edge fade on the chip row while tabs are scrolled out of view. |

## H. Dashboard and orders

| # | Sev | Gap | Where | Status |
|---|-----|-----|-------|--------|
| H1 | P1 [FE] | Six attention tiles stack one per row on a phone (1250 px of scrolling before Recent orders); 4+2 orphan row on desktop; zero-count tiles carry the same weight as the one with work. 2-column compact grid on phone, 3×2 on desktop, muted zero tiles. | dashboard | ☐ |
| H2 | P2 [BOTH] | No commercial KPIs on the dashboard (orders today, revenue today, pending COD, low stock). Reporting facts already answer "Sales by day"; a `GET /admin/dashboard/summary` built on them, or a client-side run of the sales-by-day report for today, would do. | dashboard, reporting | ☐ |
| H3 | P1 [BOTH] | To-pack queue semantics contradict the dashboard: the tile says "Awaiting packing — Confirmed, not yet packed", the queue shows parts in "New — to accept" with a "Pack" button. Verify what Pack does to a New part; either accept implicitly (and say so on the button) or exclude unaccepted parts and surface "To accept" as its own count. | fulfilment, dashboard, ordering | ☐ |
| H4 | P2 [FE] | Orders list: raw enum `CashOnDelivery` in the Payment column; "Net total" and "Ordered" show the same figure on every row; nine columns overflow at 1366 so "Placed" is clipped. | orders | ☑ Done: "Cash on delivery"; items/sellers folded into the order cell; "Ordered" renamed and (now genuinely) hidden by default — hiddenByDefault was ignored on tables with a storageKey. |
| H5 | P2 [BOTH] | Customer identity is a phone number on the dashboard, orders list and order header; the order already holds the delivery name. Expose `customerName` on the order summary, show name first. | orders, dashboard, order-detail | ☐ |
| H6 | P2 [FE] | Order detail: address prints the state as its code ("Hyderabad, 36 500081"); Money card lists "Tax ₹214.18" above "Ordered ₹1,999" (= Items) with nothing saying tax is inclusive; phone number repeated in the Customer card. | order-detail | ☐ |
| H7 | P3 [FE] | Shipments: "Not booked yet" chip and "Not booked" tracking cell say the same thing; "0 g" weight and "—" expected for unbooked parcels; failed-courier-messages table narrower than its card with a header row over an empty body. | shipments | ☑ Done: unbooked "0 g" → "—", duplicate "Not booked" dropped, failed-messages table full width / no header over empty body. |
| H8 | P1 [FE] | Returns: filter panel open by default with the Status select showing "Any" while the applied chip says "Awaiting a decision" — the panel does not mirror the applied value. | returns | ☑ Done: status is the quick tab row, panel stays closed; filter selects now mirror applied value. |
| H9 | P3 [FE] | To pack: "Refresh the pick list" styled as the page's primary action; pick-list Order cell is plain text; pager with no count. | fulfilment | ☐ |

## I. Catalogue and inventory

| # | Sev | Gap | Where | Status |
|---|-----|-----|-------|--------|
| I1 | P2 [FE] | Product detail: Images, Variants and Offers sit at the bottom of a ~3,500 px page under Specifications and SEO. Move them up after Product details, or add a section index. New-product form needs a one-liner that photos, variants and prices come after Create. | product-detail | ☐ |
| I2 | P2 [FE] | GST rate is free text (five legal slabs — a select); Country of origin free text and shows the raw "IN". | product-detail | ☐ |
| I3 | P2 [FE] | No "View on storefront" link on a published product. | product-detail | ☐ |
| I4 | P2 [BOTH] | Stock rows show SKU + warehouse *code* only ("DEMO-CC-0002 / DEMO-WH-JAI") — no product name, no warehouse name, no link. Stock summary needs `productId`, `productName`, `warehouseName`. | stock, inventory API | ☐ |
| I5 | P2 [FE] | Adjustments & transfers is a lone "Search by SKU" box: no explanation of the two flows, no recent movements, no path from the Stock list. | adjustments | ☐ |
| I6 | P2 [FE] | Attributes: two empty sections with no CTA in the empty state; sets are read-only (F2). | attributes | ☐ |
| I7 | P3 [FE] | Products list: "Rating —" on every row (hide until reviews exist); Created-at-seconds beside Published. Variant "Name —" on single-variant products. Stock "Tracking: None" unexplained. | products, product-detail, stock | ☐ |
| I8 | P3 [BE] | Product History says "Nothing recorded yet" for a product that was created and published — catalog audit not reaching the trail. Demo products are all "Keep out of search engines" — seeder. | catalog audit, demo seeder | ☐ |

## J. Grow (promotions, storefront content)

| # | Sev | Gap | Where | Status |
|---|-----|-----|-------|--------|
| J1 | P1 [FE] | Page composer on a phone: block labels wrap per character ("Categor / y tiles", "Prod / uct / carou / sel"). | page-composer | ☐ |
| J2 | P2 [FE] | Page composer lists blocks twice (sortable list, then a second collapsed editor list with the same names) and shows developer type codes ("RichText", "CategoryTiles") beside the labels. | page-composer | ☐ |
| J3 | P2 [BOTH] | Header says "Published · v0 · published 5 Oct" while Version history says "This page has never been published" (seeded page has no version row). | page-composer, content seeder | ☐ |
| J4 | P2 [FE] | New promotion: the basket simulator takes the right column before a rule exists; listings/sellers are "one id per line" textareas (GUIDs, F3) under a search box; categories/brands are native multi-select listboxes (ctrl-click). | promotion-detail | ☐ |
| J5 | P3 [FE] | Promotions list "Combine with other offers" header clipped at 1366. | promotions | ☐ |

## K. Marketplace, reports, settings, system

| # | Sev | Gap | Where | Status |
|---|-----|-----|-------|--------|
| K1 | P1 [FE] | Commission plans table is clipped to two columns (Plan, How) at 1366 by the side panel — Default rate, Fee, Exceptions, Sellers, State are invisible with no scroll affordance. | commission-plans | ☐ |
| K2 | P2 [FE] | Seller detail readiness panel prints raw document enums ("Pan, IncorporationCertificate, AddressProof"). | vendor-detail | ☐ |
| K3 | P2 [data] | Demo seller is Active and trading while its readiness panel says "Not ready to trade yet" (no PAN, plan, docs, bank, pickup). Seed the readiness or label the override. | demo seeder | ☐ |
| K4 | P3 [FE] | Commission simulator and report runner still take a pasted Seller id; the ledger already has a seller-by-name picker — reuse it (F3). | commission-plans, report-detail | ☐ |
| K5 | P2 [FE] | Report runner: From/To blank while a default 30-day period is applied; chart y-axis has fractional ticks for an integer count; "The store's own" badge unexplained. | report-detail, report-chart, reports | ☐ |
| K6 | P2 [FE] | Feature flags: 28 flags in one flat list keyed by code, twelve filled "Switch on" primaries; group by area, lead with the description, toggle control, confirm on the two "permanent gap" flags. | feature-flags | ☐ |
| K7 | P2 [BOTH] | Audit log entries are unreadable: actor "Staff user 01a10867", target "User · <guid> · <ip> · <correlation guid>", changes hidden behind "2 field(s)". Read model needs an actor display name and a target label; FE shows ≤3 changed fields inline. | audit-log, platform audit read model | ☐ |
| K8 | P3 [FE] | Users list shows "2FA off · Email unverified" on every customer row (customers never have 2FA); Store settings sub-nav does not track the active section on scroll; sidebar label "Adjustments & transf…" truncates. | users, store-settings, sidebar | ☐ |
| K9 | P3 [FE] | Login: no show/hide password; 2FA code field not focused after the password step. | login | ☐ |
