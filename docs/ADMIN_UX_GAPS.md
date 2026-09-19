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
| B7 | Negative stock adjustment / transfer, variant Deactivate, offer Deactivate, banner Switch off, single-session End, return Close, menu item with children removed, commission plan default/active change, tax-rate Active untick, NDR return-to-origin | adjustments, variant-editor, product-detail, banners, profile, return-detail, menu-editor, commission-plans, tax-rates, ndr | ☐ |
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
