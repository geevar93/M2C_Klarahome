# Step 27 — Admin: catalog, inventory, orders, fulfilment, returns

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** G · **Depends on:** Step 26
- **Objective:** Daily operations screens.
- **Deliverables:**
  - Catalog: category/brand/attribute management, product & variant editor, media manager,
    bulk import/export with error report, moderation queue for vendor submissions.
  - Inventory: stock by location, adjustments with reason, stock ledger view, low-stock queue,
    purchase orders and GRN, stock takes.
  - Orders: list with rich filters, order detail, timeline, notes, manual state transitions,
    cancellations, invoice/credit-note actions.
  - Fulfilment: pick/pack workflow, shipment creation, label/manifest printing, NDR queue.
  - Returns: RMA queue, approval, pickup scheduling, QC disposition, refund initiation.
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
- **Full acceptance criteria (verified at Step 29, not now):** An operator can take an order from placed to delivered, and a return
  from request to refund, entirely through the admin UI.
- **Outcome / Notes:**

### What was built

Nineteen screens across five domains, wired into the Step 26 declaration by replacing eighteen
`step: 'Step 27'` markers with `load:` — which is the whole point of that file: a screen landing is
a one-line diff, and its route, its guard and its menu item were already real and already tested.

**`libs/data-access/admin/src/lib/` — six new services.**

| File | Covers |
|---|---|
| `catalog-admin.service.ts` | Products, variants, listings, categories, brands, attributes, attribute sets, the moderation queue, and the import/export jobs |
| `media-library.service.ts` | The media library: list, upload, signed link, delete |
| `inventory-admin.service.ts` | Stock, the ledger, adjustments and transfers, warehouses, suppliers, purchase orders, goods receipts, stock takes |
| `orders-admin.service.ts` | Orders, sub-orders, transitions, cancellation, notes, invoices |
| `fulfilment.service.ts` | Pick list, shipments through pack/weigh/book/dispatch, manifests, NDR, the courier dead-letter queue |
| `returns-admin.service.ts` | The RMA machine end to end, reason codes, credit notes |
| `document-print.service.ts` | The two endpoints that answer bytes rather than JSON |

**`apps/admin/src/app/pages/` — the screens.**

- **Catalogue** — `products.page.ts` (list, bulk lifecycle actions, CSV import with its error report,
  server-side export), `product-detail.page.ts` (the editor: details, compliance, attributes,
  specifications, SEO, media, variants, sellers' offers and the audit trail),
  `variant-editor.ts`, `media-picker.ts`, `media-manager.ts`, `categories.page.ts` (the tree),
  `brands.page.ts`, `attributes.page.ts` (with the options editor), `moderation.page.ts`.
- **Inventory** — `stock.page.ts` (with the ledger drawer, the adjust dialog, the settings dialog and
  the low-stock filter), `adjustments.page.ts` (adjust and transfer, over a search),
  `purchase-orders.page.ts` (draft, submit, cancel, and the goods receipt),
  `stock-takes.page.ts` (the count sheet), `warehouses.page.ts`.
- **Orders** — `orders.page.ts`, `order-detail.page.ts` (per-sub-order transitions, whole and partial
  cancellation, notes, invoices, the timeline).
- **Fulfilment** — `fulfilment.page.ts` (the pick list and the four-step pack workflow),
  `shipments.page.ts` (tracking, label, manifest, manual scan, the courier DLQ), `ndr.page.ts`.
- **Returns** — `returns.page.ts`, `return-detail.page.ts` (the whole RMA machine, per-line QC, refund,
  credit note).
- **`core/format.ts`** — `tableDate`, `tableDateTime`, `tableMoney`, for the one place a pipe cannot
  reach.

### The decisions worth recording

**The buttons come off the machine, in three places.** A sub-order's transitions are
`SubOrderResponse.nextStatuses`, a return's actions are `ReturnResponse.nextStatuses`, and a
product's lifecycle is six named endpoints behind six `*khHasPermission` directives. None of the
three is a client-side status table. An edge added or removed on the server changes what these
screens offer with no change to this app — which is the property that made Steps 14 and 17 build
their transition tables as data in the first place.

**Nothing on the inventory screens sets a quantity.** Every write is a movement: a signed change
with a reason from a closed list, a receipt against an ordered line, or a counted variance. That
is not a UI preference — `inventory.stock_ledger` is append-only and the balance is derived from
it, and there is deliberately no endpoint that would let a screen overwrite it.

**Pack → weigh → book → dispatch, in that order and one step at a time.** The pack dialog offers
exactly the next step and nothing else. Weight before booking because the courier prices on the
greater of actual and volumetric weight, and a parcel booked unweighed is re-rated at the
courier's own rate three weeks later; the volumetric figure is shown as it is typed for the same
reason. Booking that fails — no aggregator account, an unserviceable PIN code — falls to the
manual waybill, which is Step 16A's whole point.

**Receiving counts accepted and rejected separately, and QC is per line.** Thirty arriving against
forty is a supplier conversation or an insurance claim depending on which, and a single "received"
number cannot say. Only the accepted quantity moves the ledger; only a `Restock` disposition
returns a unit to sale.

**Bulk actions report their failures.** There is no bulk endpoint, so the products and moderation
screens fan out one request per row, catch each failure into a value so every request completes,
and then say how many were refused and what the first one said. A bulk action whose failures are
invisible is worse than none.

**Two files needed care about authorisation and files.** `DocumentPrintService` fetches the
shipping label and the import template as blobs through `HttpClient` — still the whole interceptor
chain, only bypassing `ApiTransport`'s JSON response type, which it fixes correctly for the other
489 operations. A plain link would carry no bearer token, because the access token is in memory
only. The invoice, by contrast, answers a signed URL and is opened as an ordinary navigation.

**One route now carries `canDeactivate`.** `AdminDestination.guardUnsavedChanges` was added and set
on the product editor alone — the only screen long enough that a stray click on the sidebar loses
real work. Opt-in rather than default: a confirmation on a three-field form trains people to
dismiss it, which is what makes it useless on the screen with ninety.

### What was not built, and why

- **Suppliers have no screen.** The card's Deliverables name purchase orders and GRN, not
  suppliers, so one was not built — but a purchase order cannot be raised until a supplier exists,
  which makes it a real gap in the first-run path. Parked for a decision.
- **A replacement return records the decision with a null `replacementOrderId`**, because the back
  office has no way to place an order. Parked.
- **A warehouse's state is entered as a raw identifier**, because no reference-data endpoint is
  exposed on the admin surface. Parked; it is a backend change either way.
- **Nothing is sortable**, because no list endpoint in this step declares a sort parameter and
  `DataTableColumn.sortKey` exists exactly so a header that would 400 is not offered.
- **No warehouse scoping on the fulfilment queue or the pick list** — the endpoints do not carry
  one. Harmless for a single-warehouse deployment, wrong for two.

All five, and seven more, are in [`../PARKING_LOT.md`](../PARKING_LOT.md).

### Verification

- `npx nx build admin` — **succeeds, no warnings**. Initial bundle **114.5 kB gzipped against the
  300 kB budget** (up from 94.4 kB at Step 26, because the screens now pull the two admin barrels
  into the shared chunk). Largest route chunk: the product editor at 10.2 kB against 120 kB.
- `npx nx run-many -t lint` — **23 projects clean**. The only warnings in the workspace are the
  five pre-existing non-null assertions in `navigation.spec.ts`.
- `npx nx run-many -t test` — **21 projects green**, including the Step 26 RBAC rule table, which
  still passes unchanged against the eighteen newly-filled destinations.
- `npx nx build storefront` — still succeeds; the shared library changes did not disturb it.

**Nothing here is proved against a live API, a database, a courier or a browser.** Per the sprint
rules no integration test was written; **35 rows** were added to
[`../TEST_DEBT.md`](../TEST_DEBT.md), including both halves of the headline criterion — an order
taken from placed to delivered, and a return from request to refund, entirely through this UI.

