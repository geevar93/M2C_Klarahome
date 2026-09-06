# Step 21 — Reviews, Q&A, Wishlist & Reporting read-models

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** E · **Depends on:** Step 20
- **Objective:** Social proof, saved intent, and the numbers the business runs on.
- **Deliverables:**
  - Verified-purchase reviews with ratings, images, moderation queue, vendor replies,
    helpfulness votes; aggregate rating projections.
  - Product Q&A; report-abuse workflow.
  - Wishlist / save-for-later; back-in-stock and price-drop subscriptions.
  - Reporting read-models & APIs: sales by day/category/vendor, GMV vs net revenue, AOV,
    conversion funnel, cart abandonment, top/slow SKUs, stock ageing, return rate by reason,
    settlement summary, COD vs prepaid mix.
  - Scheduled report exports (CSV) and email delivery.
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
- **Full acceptance criteria (verified at Step 29, not now):** Reports reconcile against transactional data for a seeded dataset;
  a review can only be posted against a delivered purchase.

---

## Outcome / Notes

**Status: ✅ DONE — 2026-09-06.** Two modules built, `reviews` and `reporting`, and they are the
last two schemas in the platform. The build is clean at **0 warnings** in every project this step
touched; **941 unit tests** green (31 new); the solution's only warnings are the eight pre-existing
ones in `KlaraHome.Modules.Payments` that Step 28A owns.

> **One architecture test is red, and it was red before this step.**
> `ModuleBoundaryTests.A_module_exposes_nothing_publicly_except_its_module_class` fails on
> `KlaraHome.Modules.Content`, which exposes 41 public `Application` records where the rule says a
> module's DTOs stay internal. Confirmed pre-existing against `git show HEAD`. Reviews and Reporting
> were first written with the same mistake and **were corrected inside this step**, so the suite now
> names only Content. Not fixed here under protocol rule 6; raised at the boundary in
> [`../PARKING_LOT.md`](../PARKING_LOT.md) for a decision, because a gate that sits red stops being
> read. The other 13 architecture tests pass.

### What was built — Reviews (`reviews` schema, ten tables)

Four surfaces that look unrelated and are not. A rating, a question, a saved item and a price
somebody would buy at are all the same kind of fact: a shopper telling the store something it could
not have worked out on its own. All four are anchored to the catalogue and none of them may read it,
so every one goes through `IProductProjectionSource` at write time to confirm what it names and at
read time to price it.

**The rule that shapes the module is that a review requires a *delivered* purchase.** Not an order
and not a payment: delivery, resolved through a new seam `IOrderPurchases` that Orders implements —
because the module that owns the state machine deciding what *delivered* means is the module that
should answer for it. Every identifier on a review (the product, the variant, the seller) comes off
what that contract returned rather than off the request, and the one-review-per-line rule is a
unique index rather than a handler check, because two submissions racing each other is the ordinary
case on a slow connection.

| Piece | Notes |
|---|---|
| `reviews` + `review_votes` | Verified-purchase reviews with images, a shortened author name, moderation, a seller reply and helpfulness. A **vote is a row keyed on the voter**, not a counter, which is the whole anti-stuffing mechanism |
| `product_ratings` + `vendor_ratings` | Stored aggregates with the 1..5 histogram, **recomputed in full** on every change rather than adjusted, so a rejection, an edit and a redelivery all give the same answer |
| `questions` + `answers` | No purchase required — the point of a question is that somebody has not bought it yet. `author_type` is decided from the caller's own claims and never from the request, and a seller's own answer skips the queue where the store allows it |
| `abuse_reports` | A complaint is a **row**, not a flag: ten people reporting one review is one review and ten complaints, and the count is the strongest triage signal a moderator has. Upholding is what refuses the content — which stops a competitor removing a five-star review by reporting it |
| `wishlists` + `wishlist_items` | One default list per customer enforced by a filtered unique index; a share token is 32 random bytes and not the list's id; **no price is stored**, because a wishlist is a live shopping surface |
| `stock_subscriptions` | Back-in-stock and price-drop, with `target_price` and `price_at_subscription` — the second is what makes "cheaper" answerable once the price has moved twice |

**24 endpoints, three permissions, five feature flags, two event subscriptions, one worker.**

### What was built — Reporting (`reporting` schema, nine tables)

The module that owns no business rule and may change nothing. **It keeps its own facts** — seven
fact tables written by fourteen integration-event subscriptions across six modules, one row per
transactional row, denormalised at the moment the event lands with the category, the seller and the
payment method already on them. A report is then a filtered aggregation over a single table with
**no join anywhere in the module**.

That shape is the decision, and it is recorded as **ADR-021**: `03-database-design.md` §4.18 named
seven materialised views, and a materialised view over another module's schema is a cross-schema
read with a different word in front of it — worse than the violation the rule forbids, because being
declared in DDL puts it out of reach of the architecture tests.

**Thirteen declared reports**, served as data the admin app reads back so the report picker, the
column headings and the CSV all come off one declaration: sales by day / category / seller, GMV vs
net revenue, AOV, the conversion funnel, cart abandonment, top and slow SKUs, stock ageing, return
rate by reason, the settlement summary, and COD vs prepaid.

**Scheduled exports**: a `report_schedules` row with a stored `next_run_at` (so the sweep is an
index seek), a `report_runs` log, CSV written by hand — for the byte-order mark and the
formula-injection defusing, not for the parsing — stored in the private bucket under
`reporting/exports/`, and emailed as a **short-lived signed link** rather than an attachment.

**11 endpoints, two permissions, three feature flags, fourteen event subscriptions, two workers.**

### The seams added, and why each is in the module that owns the data

| Seam | Implemented by | Why it had to exist |
|---|---|---|
| `IOrderPurchases` | Orders | "A review can only be posted against a delivered purchase" is this step's acceptance criterion. One definition of *delivered*, in the module that owns the state machine |
| `IInventoryAgeing` | Inventory | The one inventory number no event carries. `StockLevelChanged` says what the balance *is*; nothing says when the units on the shelf arrived. That is in the stock ledger |
| `Reviews.ProductRatingChanged` | consumed by Catalog + Search | Fills the `RatingAverage` that has been null since Step 19 |
| `Reviews.VendorRatingChanged` | consumed by Vendors | A seller's own score, which the buy-box rule may rank on |

Both rating events carry the **recomputed aggregate** rather than a delta, so applying either is
idempotent by construction and neither consumer needed an inbox row.

### Deviations from the specification

1. **`03-database-design.md` §4.18 rewritten**, from seven `mv_*` materialised views to seven fact
   tables. ADR-021 carries the reasoning and the four rejected alternatives. This is the one
   substantive spec change in the step.
2. **§4.15 rewritten** from five table names to the columns, the constraints and the indexes as
   built. `review_votes`, `product_ratings`, `vendor_ratings` and `abuse_reports` are four tables
   the original five-line sketch did not name, and each is load-bearing.
3. **§3.6 of the API spec** was five lines and is now the 24 storefront routes, with the reason the
   review write takes an `orderLineId` and ignores the product in its own route.
4. **`02-domain-model.md` §6** now names three Reviews events where it named one, and adds Reporting
   as a consumer on the rows it actually subscribes to.
5. **Moderation is a configuration value rather than a store setting** (`Reviews:AutoApprove*`,
   all off). Turning it off is a decision about legal exposure under the IT Rules
   (`07-security-compliance.md` §5), not about merchandising, and not one a merchandiser should be
   able to flip from an admin screen on a Friday afternoon.
6. **CSV exports are not registered in the media library.** That library identifies uploads by
   sniffing magic numbers and CSV has none; teaching it to accept a format it cannot recognise would
   weaken the control that stops the store serving executable content from its own domain.

### Known gaps and technical debt

- **Nothing is proved against a database.** Both headline acceptance criteria — reports reconciling
  against a seeded dataset, and a review requiring a delivered purchase — are Step 29's.
  **37 `TEST_DEBT.md` rows** recorded.
- **There is no back-fill for the facts.** Reporting begins the day the module is deployed with the
  ingest on, and `reporting.fact-ingest` is the one flag in the platform that is not safe to leave
  off. It exists so an operator can keep the store selling if the ingest ever causes a problem, and
  the environment file and the flag's own description say so.
- **Commission on the daily take-rate cut is apportioned, not exact.** `SettlementCycleClosed`
  carries one figure per cycle and the per-line rate is frozen in `orders`, which this module may not
  read. Exact for a flat commission plan, approximate for a per-category one; the cycle-level figure
  in the settlement summary is always exact.
- **The conversion funnel starts at the basket.** "Viewed a product" needs a client-side analytics
  pipeline this platform does not have.
- **Stock alerts are queued outside the transaction that closes the subscriptions.** `INotifier`
  writes in its own scope by design, and holding a transaction open across a fan-out to five hundred
  recipients would be worse than the failure it prevents.
- **No admin screen renders any of it.** The moderation queue is Step 27's and the reports are
  Step 28's.

Twenty-one Parking Lot items recorded.
