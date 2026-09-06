# Deferred Test Debt

> Ledger for the Klara Home implementation plan.
> Tracker: [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) · Parking Lot: [`PARKING_LOT.md`](PARKING_LOT.md)

**Render this file when you defer a test (Steps 9–28) and when you work Step 29. Not otherwise.**

---

## Why this file exists

Steps 9–28 run under the **MVP Build Sprint** (`IMPLEMENTATION_PLAN.md` §3): production code is
written, integration tests are not. That is a schedule decision, and it only stays a decision — as
opposed to an accident — because every skipped test is written down here the moment it is skipped.

**Step 29 works this file top to bottom.** A step's *full* acceptance criteria are not met until
its rows are closed. Nothing is dropped from a step's acceptance criteria; the criteria move here
and Step 29 pays for them.

---

## How to add a row

At the end of each build-sprint step, before you ask permission to continue, add one row per piece
of behaviour that the step's **Full acceptance criteria** would have demanded proof of.

- **Step** — the step that incurred it.
- **Behaviour to prove** — what must be true, in the language of the acceptance criterion. Not
  "test the vendor service" but "a vendor onboarded through the API reaches Active".
- **Test kind** — `unit` · `integration` · `contract` · `e2e` · `load` · `security` · `a11y`.
- **Where** — the suite and, if known, the fixture or file it belongs in.
- **Risk** — 🔴 high (money, stock, auth, data loss) · 🟡 medium · 🟢 low. Step 29 works 🔴 first.
- **Status** — `⬜ OPEN` · `✅ CLOSED` (with the test's name).

Keep the row short. If it needs a paragraph, the paragraph belongs in that step's card under
Outcome / Notes, and this row links to it.

---

## Open debt

| Step | Behaviour to prove | Test kind | Where | Risk | Status |
|---|---|---|---|---|---|
| 9 | A vendor can be onboarded end to end through the API and reaches `Active` — apply, submit, KYC upload and verify, bank account and its check, pickup location, approve, activate | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 9 | Activation is **refused** while any onboarding requirement is unmet, and the response names every blocker rather than the first | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 9 | A commission plan resolves correctly for a given vendor + category **through the resolver and the database** — the algorithm is unit-tested, the loading and the `Include` of rules are not | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 9 | `VendorActivated` / `VendorSuspended` / `VendorOffboarded` are written to `platform.outbox_messages` **in the same transaction** as the status change, from the keyed per-context outbox | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 9 | A bank account number round-trips through `IFieldProtector` and is never returned by any endpoint; a key rotation with an overlapping window still decrypts | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 9 | A vendor caller cannot read or write another seller's vendor, KYC, bank accounts, pickup locations, regions or staff — every route answers 404, not 403 | security | `IntegrationTests` (authorisation matrix) | 🔴 | ⬜ OPEN |
| 9 | A vendor caller cannot create a vendor, approve or activate one, verify their own KYC or bank account, or assign a commission plan | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 9 | The partial unique indexes hold: at most one primary bank account and one default pickup location per vendor, and one default commission plan per tenant | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 9 | Every `CHECK` constraint refuses what it is meant to — IFSC shape, PIN code shape, rejection-without-a-reason, an inverted price band, a rate above 100 | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 9 | The `vendors` migration applies against a live database and re-runs clean, and `vendor_code_seq` yields distinct codes under concurrent applications | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 9 | Every permission the Vendors endpoints declare appears in `PermissionCatalog` (the two lists are kept in step by hand today) | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 9 | A KYC document in the public bucket is refused, and a signed link is minted only after the permission and scope checks pass | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 9 | `IVendorDirectory.FindManyAsync` returns one row per known id and silently omits the rest, in one query | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 9 | Keyset pagination over the seller list neither repeats nor skips a row while sellers are being created | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 9 | The ILIKE search escapes a caller's own `%` and `_` | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 10 | A variant with attributes, media and **two competing vendor offers** can be created, moderated, published and retrieved through the API — the step's own full acceptance criterion | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 10 | **Bulk import of 1,000 SKUs validates and loads**, produces the right report for the rows it rejects, and re-running the same file converges rather than duplicating | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 10 | The buy box resolves **against the database and the store settings** — the comparison is unit-tested, the candidate loading, the `IVendorDirectory` batch call and the settings read are not | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 10 | A **suspended seller's live offers are actually withdrawn** when `VendorSuspended` is dispatched, and a redelivery of the same event is a no-op | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 10 | `ListingPublished` / `ListingUpdated` / `ListingDeactivated` are written to the outbox **in the same transaction** as the state change, from the keyed per-context outbox | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 10 | A vendor caller cannot write to a **platform-owned** product they can see, nor to another seller's product, variant, offer or import job — every route answers 404 or the scope error, never somebody else's data | security | `IntegrationTests` (authorisation matrix) | 🔴 | ⬜ OPEN |
| 10 | A vendor caller cannot **approve, reject, publish or archive** a product, nor change the taxonomy | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 10 | Publication is **refused** while any mandatory disclosure is missing, and the response names every gap rather than the first — product-level and variant-level together | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 10 | Moving a category rewrites **every descendant's** path and level, a cycle is refused, and the depth cap holds for the whole subtree rather than only the moved node | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 10 | The unique indexes hold under concurrency: one offer per `(vendor, variant)`, one variant per `(product, attribute_hash)`, one pending moderation row per product | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 10 | Every `CHECK` constraint refuses what it is meant to — `selling_price > mrp`, an HSN of five digits, a variant-defining `text` attribute, a media asset with no owner, a listing with no vendor | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 10 | The `catalog` migration applies against a live database and re-runs clean; the generated `search_vector` populates and its GIN index is used | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 10 | Every permission the Catalog endpoints declare appears in `PermissionCatalog` (the two lists are kept in step by hand today) | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 10 | `IProductCatalog.FindListingsAsync` returns one row per known id in **one query**, resolves the variant image before the product's, and reports `IsPurchasable` false when any of the three states is not Active | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 10 | A `catalog_jobs` row is claimed by exactly one worker under `FOR UPDATE SKIP LOCKED`, and a failed attempt requeues with its counters reset rather than double-counting on the retry | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 11 | **No oversell under concurrency.** N callers race for the last unit against one stock item; exactly one `HoldAsync` succeeds and the rest are refused, with `quantity_reserved <= quantity_on_hand` holding throughout. The whole module exists to make this true and nothing proves it today | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 11 | `quantity_on_hand = SUM(ledger.change)` and `quantity_reserved = SUM(ledger.reserved_change)` after an arbitrary sequence of receipts, holds, commits, releases, expiries, transfers and corrections | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 11 | `ReservationSweeper` releases a lapsed hold, writes the `Release` ledger entry with its note, marks the row `Expired`, and two sweepers running together never release the same hold twice (`FOR UPDATE SKIP LOCKED`) | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 11 | The append-only trigger on `inventory.stock_ledger_entries` refuses `UPDATE`, `DELETE` and `TRUNCATE`, on the parent and on a partition | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 11 | Every `CHECK` constraint refuses what it is meant to: `quantity_on_hand < 0`, `quantity_reserved > quantity_on_hand` without a backorder or pre-order flag, a pre-order date with the flag off, `quantity_received > quantity_ordered`, a rejected receipt line with no reason, a settled reservation with no `settled_at`, a ledger entry that moves nothing | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 11 | `HoldAsync` is idempotent on `(referenceType, referenceId, lineReferenceId)`: a retried checkout gets its own hold back and the stock is held once, and the partial unique index `ux_stock_reservations_live` enforces it under concurrency | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 11 | `SettleAsync` is idempotent under at-least-once delivery: a redelivered commit or release moves no stock the second time and reports zero | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 11 | `StockLedgerService` resets the tracked entity's `xmin` from `RETURNING`, so a save that follows a movement in the same unit of work does not throw `DbUpdateConcurrencyException` | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 11 | The raw movement command is enlisted in the ambient transaction: a handler that throws after a movement leaves no ledger entry and no quantity change | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 11 | The `inventory` migrations apply against a live database and re-run clean; `stock_ledger_entries` is created partitioned, 25 monthly partitions plus the default exist, and a row lands in the partition its `occurred_at` names | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 11 | The vendor query filter confines a vendor caller to their own stock, locations, suppliers and documents, and `InventoryScope.CanWrite` refuses a write to a platform-owned row they can see | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 11 | Every permission the Inventory endpoints declare appears in `PermissionCatalog` — asserted for the five codes by `StockReservationTests`, but by a hand-written list rather than by reflecting over the endpoints | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 11 | `ReceivePurchaseOrderCommandHandler` writes the GRN, the ledger entries and the order status advance in **one transaction**: a failure part-way leaves none of them | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 11 | Submitting a stock take posts exactly one `Correction` per non-zero variance, leaves uncounted rows alone, and reports rather than forces a downward correction the shelf can no longer absorb | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 11 | A transfer writes both legs under one reference id or neither: the outbound leg rolls back when the inbound one fails | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 11 | `StockReconciliationJob.DriftQuery` reports a stock row whose cache has been tampered with, reports nothing when everything agrees, and includes a row that has a cached quantity but no ledger entries at all | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 11 | `ListingPublished` opens a stock row at zero in the seller's highest-priority active location, is idempotent on redelivery, and logs rather than throws when the seller has no warehouse | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 11 | `StockLevelChanged` and `StockRunningLow` are written to the outbox in the movement's transaction, and `StockRunningLow` fires once per crossing rather than once per movement below the level | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 12 | A golden-file suite of ~30 pricing and tax scenarios passes exactly - intra-state, inter-state, cess-bearing, coupon-and-tax interaction, rounding, multi-vendor basket - which is this step's stated full acceptance criterion | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | The price-list walk picks the right row against a live database: lowest priority wins, a seller's own list beats nothing it should not, a list outside its window is ignored, and the highest tier at or below the quantity is chosen | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | An offer with no item in any applicable list falls back to `catalog.listings.selling_price`, and a seller's list never prices another seller's offer | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | `TaxRateResolver` returns the row in force on a past date rather than today's, so an invoice reprinted after a rate change reproduces the original figures | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | A quote across two sellers in two GST states produces CGST+SGST on one line and IGST on the other, and each seller's group totals match the invoice their sub-order will carry | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | `QuoteEngine` end to end against a live database: catalogue, price lists, tax rates, promotions, shipping, COD fee, rounding and wallet in the documented order, with every figure reconciling | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | The promotion candidate query returns only live, in-window promotions and only the coupon the shopper typed - an expired code and somebody else's code are both invisible | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | A per-customer usage limit is counted from `promotion_redemptions` correctly, and a reversed redemption still counts against the shopper | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | `PromotionLedger.RedeemAsync` is idempotent on `(promotion, order)`: an `OrderPlaced` delivered twice redeems once and increments `usage_count` once | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | The conditional `usage_count` update is genuinely a race winner: two orders placed concurrently against a promotion with one use left produce one redemption and one refusal | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | `PromotionLedger` commits the claim, the redemption row and the outbox event together: a failure part-way leaves none of them, and the explicit transaction really wraps the `ExecuteUpdateAsync` | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | `ReverseAsync` gives back exactly the uses it took, marks the rows rather than deleting them, floors `usage_count` at zero, and reports zero on a second call | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | `StoreCreditService.RedeemAsync` and `CreditAsync` are idempotent on their reference: the same refund event delivered twice credits once, and the unique index is what enforces it under concurrency | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | A wallet debit is all or nothing: a redeem larger than the balance takes nothing and returns zero, and the balance cache always equals the sum of its transactions | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | Every wallet route and `IStoreCredit` mover refuses while `pricing.store-credit` is off, and the balance reads as zero and inactive rather than throwing | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 12 | `pricing.coupons` switched off stops every code at once, and a code typed while it is off is reported as "not being accepted" rather than invalid | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 12 | The `pricing` migrations apply against a live database and re-run clean, and every `CHECK` constraint refuses what it is meant to: a negative price, `min_quantity < 1`, a rate above 100, a window that ends before it starts, a negative wallet balance, a zero-amount wallet transaction | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 12 | The `promotions` partial unique index allows any number of automatic rules with no code while refusing a duplicate code, and the `wallet_transactions` partial unique index only constrains rows that carry a reference | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 12 | `PromotionScope` and `PromotionConditions` round-trip through `jsonb` intact, including the nested tier ladder, after a save and a reload | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 12 | The vendor query filter confines a vendor caller to their own price lists plus the platform's, and `PricingScope.CanWrite` refuses a write to a platform list they can see | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 12 | Every permission the Pricing endpoints declare appears in `PermissionCatalog` - the codes were diffed by hand at the step boundary, not asserted by a test | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 12 | `PriceChanged` is written to the outbox in the transaction that wrote the price, fires only for the base quantity tier, and is not raised when the price did not actually move | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 12 | `POST /store/quote` stays within its line and promotion ceilings, is rate-limited, and its latency is measured under a realistic basket - it is the most expensive anonymous read on the platform | load | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 12 | A signed-in caller's quote uses their own id and never one named in the body, so no caller can evaluate or spend another shopper's store credit | security | `IntegrationTests` | 🔴 | ⬜ OPEN |

| 13 | A multi-vendor cart produces a correct grouped, priced checkout summary — two sellers, two groups, per-seller subtotals, tax and shipping that reconcile to the order total. This step's stated full acceptance criterion | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | Duplicate place-order requests create exactly one order, sent concurrently against a live database. This step's other stated full acceptance criterion | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | The unique index on `(tenant_id, idempotency_key)` is genuinely the race winner: the loser re-reads the winner's row and replays its response rather than creating a second order | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | A key replayed against a *different* basket is answered `409 IDEMPOTENCY_KEY_REUSED`, never with somebody else's order | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | A key whose attempt failed may be used again and produces exactly one order on the retry; a key still in progress is refused | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | `place-order` holds every line or none, and every failure path after the hold — out of stock, ordering refusal, an exception — releases every hold it took. The holds and the cart transaction are **not** atomic, so the compensating release is the only thing standing between a failed checkout and unsellable stock | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | A cart never holds stock: adding, updating and rendering a basket create no reservation, and only `place-order` does | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | A shopper cannot read, edit or place another shopper's basket or checkout session — no storefront route takes a cart id, and every checkout read is keyed on `(session, customer)` | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | The anonymous cookie round trip against a live database: a token is issued, hashed and resolved; a forged or expired token matches no row and gets a fresh basket rather than somebody else's | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | Merge on login against a live database, including the partial unique index that allows exactly one `Active` cart per customer: quantities sum and clamp, the guest cart is retired, the cookie is cleared, and a second call is a no-op | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | `GET /store/cart` performs the merge exactly once and saves it, and the write it makes on a read path does not fire on any other request | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 13 | Cart validation reports **every** reason at once — withdrawn listing, suspended seller, insufficient stock, price change, unserviceable address — and blocks only what should block, with the price-change notice never blocking | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | Serviceability through `IVendorDirectory.IsServiceableAsync`: serves-all-India says yes to everything, an exclusion wins over an inclusion wherever both match, a PIN prefix matches by prefix, and an unknown seller is not serviceable | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | Cash-on-delivery eligibility across all four rules against a live basket, and the reason returned names the rule that failed | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 13 | Changing the destination clears the per-seller delivery choices and re-prices; correcting an address within the same PIN code keeps them. Proved as a unit test on the aggregate, not against a live checkout | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 13 | A delivery choice is re-quoted rather than trusted: a client naming an amount, a carrier or an option code that was not offered is refused | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | The `carts` migrations apply against a live database and re-run clean, and every `CHECK` refuses what it is meant to: a zero quantity, a negative price or amount, a negative line or reminder count, and a cart with neither a customer nor a token | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 13 | The two partial unique indexes behave: one `Active` cart per customer while converted ones stay in the table, and one open checkout session per cart while closed ones do not block a new attempt | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 13 | `AddressSnapshot` and the `QuoteResult` snapshot round-trip through `jsonb` intact after a save and a reload, including the nested vendor groups and promotion list | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 13 | The abandoned-cart sweeper marks a stale basket once and not once per sweep, skips an empty one, retires an abandoned one past retention, and closes a lapsed checkout session — claimed with `FOR UPDATE SKIP LOCKED` so two workers are safe | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 13 | `CartAbandoned` and `CartConverted` are written to the outbox in the transaction that changed the cart, through the **keyed** outbox, so a rolled-back sweep announces nothing | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 13 | `place-order` without an `Idempotency-Key` header is refused `400`, and the storefront cart writes and `place-order` are rate-limited under their own policies | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 13 | Every permission the Cart endpoints declare appears in `PermissionCatalog` — the codes were diffed by hand at the step boundary, not asserted by a test | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 13 | A cart render stays within its line ceiling and its latency is measured with a full basket across several sellers — it is the hottest authenticated read on the storefront and it calls the quote engine on every request | load | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 14 | A two-vendor order splits into two sub-orders that transition **independently** — one delivered while the other is cancelled — and the parent order's derived status follows the documented rule | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 14 | **Invoice numbers are gapless per vendor per financial year** — concurrent issues serialise on the counter row, and a rolled-back transaction gives its number back | integration | `IntegrationTests` (concurrency) | 🔴 | ⬜ OPEN |
| 14 | Invalid transitions are rejected: every `(from, to)` pair absent from the table answers `409`, and every pair the caller may not take answers `403`, **through the API** rather than through the table alone | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 14 | `place-order` end to end for **cash on delivery**: the order is created, both sub-orders reach `Confirmed`, the cart's stock reservations are **committed**, and the coupon and store credit are spent exactly once | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 14 | A placement that fails after the order graph is written **rolls the order back and reverses the promotion redemption and the wallet debit**, leaving the shopper able to try again with nothing lost | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 14 | A pre-confirmation cancellation of the **whole** order releases the cart-scoped reservations; a post-confirmation cancellation does **not**, and publishes `SubOrderCancelled` with the quantities instead | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 14 | A **partial** cancellation leaves the sub-order in its own state, writes `quantity_cancelled` on the named lines only, and the order's derived net total falls by the pro-rata share of the discounted line totals | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 14 | `Orders.OrderPlaced` / `SubOrderConfirmed` / `SubOrderCancelled` / `SubOrderStatusChanged` / `InvoiceIssued` / `OrderCompleted` are written to `platform.outbox_messages` **in the same transaction** as the change, from the keyed per-context outbox | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 14 | A **customer** cannot read, cancel or transition another shopper's order — every route answers 404, not 403 | security | `IntegrationTests` (authorisation matrix) | 🔴 | ⬜ OPEN |
| 14 | A **vendor** caller cannot read another seller's sub-order, cannot see the order shell of an order they have no part in, and cannot take an edge reserved for Operations | security | `IntegrationTests` (authorisation matrix) | 🔴 | ⬜ OPEN |
| 14 | Order and invoice numbers are **unique per tenant** and the money totals of an order equal the sum of its sub-orders' — the §4.4 invariant, asserted against the database | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 14 | The order lines' frozen snapshot is **unaffected by a later catalogue edit**: renaming a product or changing its price does not alter a historical order or a reprinted invoice | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 14 | The invoice PDF renders with the statutory content (supplier GSTIN, serial number, HSN, per-head tax, place of supply) and the **CGST/SGST versus IGST columns follow the supply type** | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 14 | An invoice raised while the document store is unreachable still gets its number, with `file_id` null, and can be re-rendered | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 14 | The lifecycle sweeper completes a delivered sub-order once its return window closes, cancels an unpaid order past the timeout, and is safe to run in more than one worker (`FOR UPDATE SKIP LOCKED`) | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 14 | A prepaid placement answers `503 PAYMENTS_UNAVAILABLE` **and writes no order** while `IPaymentInitiation` is the refusal — and stops doing so once Step 15 lands | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 14 | Every permission the Orders endpoints declare appears in the Identity permission catalogue, and the system-role bundles grant what the step card says they grant | integration | `IntegrationTests` (`AdminSurfaceTests`) | 🟡 | ⬜ OPEN |
| 14 | The order and sub-order list endpoints keyset-paginate correctly across a page boundary, and the vendor filter applies to both | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 14 | `GET /store/orders/{id}/timeline` never returns an entry with `is_customer_visible = false`, including operator notes | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 14 | An invoice download link is minted only after the ownership check, and a link for somebody else's invoice answers 404 | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | **A sandbox end-to-end payment moves an order to `Confirmed`** — placement opens a Razorpay order, the widget pays it, the webhook lands and the order confirms. The step's first stated acceptance criterion | integration | `IntegrationTests` + Razorpay sandbox | 🔴 | ⬜ OPEN |
| 15 | **A replayed webhook is a no-op** — the same signed body twice produces one `gateway_events` row, one attempt and one state transition. The second stated criterion | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | **A refund is recorded and reconciles** — raised, sent, `refund.processed` applied, and `Σ captured − Σ refunded` agrees with the payment's cached totals. The third stated criterion | integration | `IntegrationTests` + sandbox | 🔴 | ⬜ OPEN |
| 15 | **A dropped webhook is recovered by the reconciliation job** — a capture with no webhook delivered is found by the sweep and confirms the order, leaving an attempt whose source is `Reconciliation`. The fourth stated criterion | integration | `IntegrationTests` + sandbox | 🔴 | ⬜ OPEN |
| 15 | A webhook whose HMAC does not verify is stored with `signature_valid = false`, answered `401`, and is **never** processable — including after a replay | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | A webhook outside the skew window is stored, marked `Ignored` and answered `200` — a captured signed body cannot be replayed hours later | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | The raw body is read before any model binding, so the HMAC is computed over exactly the bytes sent — proved by a body whose re-serialisation would differ (key order, whitespace) | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | A capture short of the order total does **not** confirm the order, raises `PaymentMismatchDetected`, and leaves the attempt recorded | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | A replayed `place-order` under one `Idempotency-Key` opens **one** gateway order and returns the same instruction; two concurrent retries against one order cannot open two open collections (the partial unique index) | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | A refund above the threshold cannot be sent without a second, **different** approver — refused by the handler and, independently, by the `ck_refunds_approver` check constraint | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | A duplicate refund request under one `Idempotency-Key` returns the original refund and sends the gateway one refund, not two | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | A gateway event that keeps failing is dead-lettered after `MaxEventAttempts` and is never dropped; a replay resets its budget without re-verifying its signature | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 15 | One poisonous event does not stop the queue: the batch around it still drains, because each event commits in its own transaction | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 15 | `MarkPaidAsync` is idempotent — applying the same capture twice leaves one confirmed order, one stock commit and one invoice | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | A `payment.failed` arriving after a capture does not move a confirmed order back to `PaymentFailed` | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | Cancelling a paid order raises a proportional refund for that seller's share only, clamped to what is still refundable, and a redelivered `SubOrderCancelled` raises no second one | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | A confirmed cash-on-delivery sub-order opens exactly one `cod_collections` row, and a redelivered `SubOrderConfirmed` opens no second | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 15 | A batch remittance apportions a courier's net total across its records in proportion to what each was for, and skips records already remitted or waived | unit | `UnitTests` | 🟡 | ⬜ OPEN |
| 15 | Settlement ingestion is idempotent over a lookback window, and a line naming a payment we do not hold becomes a `Mismatched` entry plus an alert rather than being repaired | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 15 | No instrument identifier ever reaches the database: a card payment stores network and last four only, and a UPI payment stores the handle's domain half only | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | No endpoint, log line or response body ever contains `Razorpay__KeySecret` or `Razorpay__WebhookSecret` | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | The webhook endpoint refuses a body over `MaxWebhookBytes` before hashing it, so an unauthenticated caller cannot make the signature check the denial of service | security | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 15 | Every payments endpoint enforces its declared permission, and a shopper cannot read another shopper's payment by any route | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | `POST /store/payments/orders/{id}/verify` never confirms an order, however valid the handshake — the order stays `PendingPayment` until a webhook or a re-fetch says otherwise | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 15 | The migration applies against a live PostgreSQL and re-runs clean; the partial unique index and every check constraint are created as written | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16 | **A confirmed order produces a shipment with an air waybill in the provider sandbox** — the step's own full acceptance criterion, end to end through `POST /admin/sub-orders/{id}/shipments` | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16 | **Tracking updates flow into the order timeline and trigger notifications** — the second half of the same criterion. The timeline half is buildable now; the notification half needs a consumer that does not yet exist | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16 | A courier webhook is verified, stored, answered `200`, drained by the worker and applied exactly once — and a **redelivery of the same scan changes nothing**, colliding on `(shipment, provider_event_id, occurred_at)` | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16 | A webhook whose signature does not verify is **stored, marked ignored, answered `401`, and never processed** — including after an operator replays it | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16 | A delivery scan on a cash-on-delivery parcel marks the `payments.cod_collections` row collected, and a return to origin waives it — **through `ICodCollections`, in the Payments schema** | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16 | A courier remittance file matched by air waybill apportions a short total across the parcels it covers and leaves an already-remitted one untouched | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16 | `IOrderFulfilment.AdvanceAsync` moves a sub-order through the **ordering** state machine as `System`, writes its timeline and raises its events — and is **refused** for an edge the machine does not have | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16 | Two partial shipments against one sub-order cannot between them pack more units than were ordered, and the second is refused with `SHIPMENT_TOO_MANY_UNITS` | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16 | A vendor caller cannot read, pack, book, label or cancel another seller's parcel, cannot work another seller's failed deliveries, and cannot write a platform-wide rate rule — every route answers 404, not 403 | security | `IntegrationTests` (authorisation matrix) | 🔴 | ⬜ OPEN |
| 16 | The `shipping` migration applies against a live database and re-runs clean; `tracking_events` is created **partitioned**, its append-only trigger refuses `UPDATE` and `DELETE` from `psql`, and its `DEFAULT` partition accepts a scan outside every range | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16 | The aggregator adapter against a **sandbox account**: serviceability, booking, label, manifest, pickup, cancel and tracking, plus one token refresh on a `401` | contract | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16 | The outbound client **refuses any host but the configured base URL**, so a label link from a third party cannot become a server-side request forgery | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16 | `IShippingOptions` returns priced services at checkout, an unserviceable PIN code returns none, and a COD basket is offered nothing on a service that refuses cash | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 16 | The serviceability cache is read on the hot path and **never** calls a courier; the nightly job refreshes the oldest answers and upserts rather than appending | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 16 | The tracking poll picks up only booked, unfinished parcels silent for the configured window, and running two workers produces no duplicate scans | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 16 | Every `CHECK` constraint refuses what it is meant to — a booked parcel with no waybill, an inverted weight band, a negative freight, a resolved report with no action | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 16 | Every permission the Shipping endpoints declare appears in `PermissionCatalog` (the two lists are kept in step by hand today) | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 16 | The rendered 4×6 label and the manifest PDF are produced, stored privately, and reachable only through a signed link that expires | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 16A | **A Shiprocket sandbox account end to end**: login and token refresh on a `401`, serviceability, PIN-code details, the two-call booking, label, manifest, pickup, cancel and tracking — the step's own first full acceptance criterion | contract | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16A | A Shiprocket webhook carrying the configured `x-api-key` is verified, stored, answered `200`, drained and applied **exactly once**; one carrying a wrong key is stored, marked ignored and answered `401` — and the comparison is constant-time | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16A | **An order outside the coverage area cannot be created through any of the five gates** — the storefront PIN check, checkout address selection, cart validation, shipping-option quoting and `place-order` — each refusing with `DELIVERY_AREA_NOT_COVERED` and the operator's own message | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16A | Adding `560` to `allowedPincodePrefixes` makes Bengaluru orderable **with no deploy and no restart**, and disabling coverage restores national trading — through `PUT /admin/settings/delivery-coverage`, audited | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16A | Changing `Shipping:Provider` from `shiprocket` to `manual` leaves parcels already booked with Shiprocket **tracking, labelling and cancelling through Shiprocket** — the registry resolves a stored provider name from the row, never from configuration | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16A | A coverage rule tightened while a checkout session is open refuses at `place-order` **before stock is held or money is asked for** | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 16A | The settings validator refuses an enabled policy with no city, prefix or PIN code — the state that would silently stop the store selling to anybody | integration | `IntegrationTests` (settings surface) | 🟡 | ⬜ OPEN |
| 16A | A PIN code Shiprocket refuses is refused with `PINCODE_NOT_SERVICEABLE` rather than the coverage code, and its city and state are written to the cache row | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 16A | The `shipping` migration `ShiprocketServiceabilityDetails` applies against a live database over an existing `serviceability_cache` and re-runs clean | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 16A | `IReferenceData.PincodeAsync` resolves a seeded PIN code to its city and state, and answers null for one the platform has no row for — the fallback path the coverage check then takes | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 16A | The outbound client still refuses any host but `Shipping:BaseUrl` after the base address is reduced to its origin, so a path pasted into the variable cannot move an endpoint | security | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 16A | `GET /store/config` carries the public coverage summary, so a storefront can state where the store delivers before a shopper types a PIN code | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 17 | **A delivered line can be returned, picked up, QC'd and refunded end to end** with a correct credit note and stock adjustment — the step's whole full acceptance criterion, through the API | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 17 | The three-policy eligibility resolution: a product window beats a seller's, a seller's beats the store's, and a non-returnable product vetoes all three — with the refusal naming which policy decided | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 17 | **The same unit cannot be returned twice.** A second RMA against units already recorded through `IOrderReturns.RecordReturnedAsync` finds nothing left, and a redelivered QC event records them once | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 17 | Proportional tax on a partial return: three of five units credit three fifths of the line's CGST/SGST (or IGST), and the credit note's total equals the sum of its lines plus freight less the collection fee | unit + integration | `UnitTests` (calculator) then `IntegrationTests` | 🔴 | ⬜ OPEN |
| 17 | The credit-note series is **gapless per seller per financial year** under concurrency, and a rolled-back transaction gives its number back — the same `FOR UPDATE` proof Step 14 owes for invoices | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 17 | A refund to the original instrument goes through the Payments maker–checker threshold: above it the return still closes as refunded and the refund waits in the approvals queue rather than being reported as a failure | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 17 | A cash-on-delivery order refused at the door has nothing refundable, and the return answers `RETURN_NOTHING_REFUNDABLE` while still raising the credit note and restocking | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 17 | `IStockRestock` is idempotent on `(referenceType, referenceId)`: a redelivered QC event moves the units once, a batch that failed halfway finishes the rest on retry, and a quarantined line moves no stock at all | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 17 | The RMA transition table refuses every edge it does not have and every actor who may not take one — in particular **a vendor cannot grade their own return**, and a shopper cannot cancel after collection | unit + integration | `UnitTests` (table) then `IntegrationTests` (routes) | 🔴 | ⬜ OPEN |
| 17 | A reverse pickup is booked through `IReversePickup` with the two addresses inverted, carries **no COD amount**, and its courier scans move the return through `Picked → InTransit → Received` | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 17 | A `RtoDelivered` scan on a forward parcel puts every live unit back on supply exactly once, with no RMA involved | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 17 | The `returns` migration applies against a live database and re-runs clean; every `CHECK` refuses what it is meant to — a refunded return with no mode, an accepted quantity above the sent quantity, a credit note carrying both IGST and CGST | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 17 | Evidence: a reason that requires a photograph refuses without one, the ceiling is enforced, and **a file id the media library does not know is refused rather than stored** | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 17 | A seller sees only their own returns through the vendor query filter, and another seller's RMA id answers `404` rather than `403` | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 17 | Auto-approval on both grounds — a reason marked automatic, and a value at or below `AutoApproveBelow` — approves in the same request and publishes `ReturnApproved` | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 17 | Freight treatment: the original delivery charge goes back only on a **full** return with `RefundShippingOnFullReturn` on, and the collection fee is deducted only when the payer is the customer | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 17 | The seven `Returns.*` events are written to the outbox **in the same transaction** as the fact they describe, from the keyed per-context outbox | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 17 | Every permission the Returns endpoints declare appears in `PermissionCatalog` (the two lists are kept in step by hand today) | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 17 | The `ReturnsSettings` validator refuses an unknown payer, mode or disposition, and refuses store credit as the default while the wallet is off | integration | `IntegrationTests` (settings surface) | 🟡 | ⬜ OPEN |
| 17 | The stale-return sweep finds the three quiet states — uncollected, uninspected, unpaid — and reports each once per pass without repairing anything | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 17 | The rendered credit-note PDF carries every particular rule 53 requires, is stored privately, and is reachable only through a signed link that expires | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 18 | **The step's whole acceptance criterion**: for a sample month, a vendor's ledger balance ties out to their orders, returns and payouts **to the paisa**, and `Σ credits − Σ debits = closing balance` for every seller | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 18 | The TCS/TDS extract matches expected values for a sample month, on **both** bases: TCS on net taxable supplies and TDS on gross sales including GST. The arithmetic is unit-tested; that it reads the right figures out of closed cycles is not | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 18 | Posting is idempotent against a live database: a redelivered `SubOrderStatusChanged`, `CodCashRecorded` or `CreditNoteIssued` collides on the unique `(tenant_id, source_key)` index and credits nothing a second time | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 18 | A prepaid sale earns on `Delivered` and a cash sale earns **only** on a remitted `CodCashRecorded` — a courier who has collected and not remitted leaves the seller's balance unmoved | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 18 | A cycle draws in every unassigned entry older than its end, including one posted late against a period that has already closed, and each entry lands in **exactly one** cycle | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 18 | The opening balance of a cycle equals the signed sum over entries already assigned to a cycle, and survives a **failed** payout — the cycle it released is still owed in the next period | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 18 | Maker–checker holds through the HTTP surface: a batch approved by the person who raised it is refused with `PAYOUT_SELF_APPROVAL`, and the database `CHECK` refuses it too if a handler is ever bypassed | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 18 | The `settlements` migration applies against a live database and re-runs clean; every `CHECK` refuses what it is meant to — a negative amount, an unknown entry type, a closed cycle with no `closed_at`, a completed payout item with no `provider_payout_id` | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 18 | **Nothing is proved against a live gateway.** `Payouts:Provider` is blank by the User's instruction, so neither the Route nor the RazorpayX adapter has ever made a call: the send, the outcome mapping, the idempotency header and the reconciliation re-read are all unexercised | integration | `IntegrationTests`, once credentials exist | 🔴 | ⬜ OPEN |
| 18 | A payout batch survives a partial send: a process pass that stops halfway leaves every item in a known state, and calling `/process` again continues from where it stopped without re-sending anything | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 18 | A completed transfer posts exactly one `payout` debit, marks its cycle `Paid`, and publishes `PayoutCompleted`; a failed one posts nothing, releases the cycle and publishes `PayoutFailed` | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 18 | The reversal ratio is taken on money and clamped: two returns and a cancellation against one parcel never give back more commission than was charged, and never reverse more than was credited | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 18 | `RefundProcessed` is deliberately **not** consumed. A return that is credited *and* refunded reverses the seller's supply exactly once — the assertion that guards the decision recorded in `PARKING_LOT.md` | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 18 | A seller sees only their own cycles, ledger and payout items through the vendor query filter, and another seller's batch id answers `404` rather than `403` | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 18 | A seller and platform finance cannot reach the write surface as each other: adjustments, closes, batch creation, approval and processing all refuse a vendor-scoped caller with `SETTLEMENT_VENDOR_FORBIDDEN` | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 18 | The scheduler closes the previous period once and only once for every settleable seller, is safe run twice concurrently, and closes nothing before the hold expires | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 18 | The gapless payout reference: two concurrent batch builds take two consecutive references, and a rolled-back build gives its number back | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 18 | The three `Settlements.*` events are written to the outbox **in the same transaction** as the fact they describe, from the keyed per-context outbox | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 18 | Every permission the Settlements endpoints declare appears in `PermissionCatalog` (the two lists are kept in step by hand today) | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 18 | The `SettlementSettings` validator refuses an unknown frequency, a nil rate on an enabled deduction, and a gateway fee configured but not charged | integration | `IntegrationTests` (settings surface) | 🟡 | ⬜ OPEN |
| 18 | `IOrderSettlement` reads the **frozen** commission off the order line, and a commission plan changed after placement does not alter what a past sale is charged | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 18 | `IVendorPayouts` reports a seller unpayable for each reason in turn — suspended, no verified account, no gateway account — and no plaintext account number ever crosses the seam | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 18 | The statement and TCS/TDS exports open in a spreadsheet with the byte-order mark intact, quote a seller name containing a comma, and refuse a range above `MaxExportRows` | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 18 | The platform-revenue report reconciles against the sellers' ledgers: what it reports as revenue is exactly the sum of the charge entries, and TCS/TDS are excluded from it | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 18 | The reconciliation sweep reports a stuck transfer without touching it, and repairs one the gateway has since resolved | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 18 | The TDS annual threshold is evaluated on the financial year to date across cycles, not on the period, and a seller who crosses it mid-year is deducted from that period on | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 19 | **p95 search latency within the NFR target (400 ms) on a seeded catalogue of 50,000 SKUs** — the headline full acceptance criterion, and the one that decides whether ADR-019's dedicated engine is ever needed | performance | k6, with the large-catalogue generator `09-nfr-testing-observability.md` §2 requires | 🔴 | ⬜ OPEN |
| 19 | **Facet counts are provably correct against SQL ground truth** — the second headline criterion. Every group's count equals a `COUNT(*)` over the same predicate set with that group's own filter lifted, for a browse, a search, one filter, and two attribute filters at once | integration | `IntegrationTests`, generated from the same predicate builder the engine uses | 🔴 | ⬜ OPEN |
| 19 | The generated `search_vector` is actually built with the four weights, and a match on the product name outranks the same word in the category name and in an attribute | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 19 | The whole SQL the engine generates parses and runs on PostgreSQL 18 — the union of ten facet branches, the lateral `jsonb` expansion, the trigram predicate and `set_limit`. Unit tests assert its shape; nothing has executed it | integration | `IntegrationTests` (Testcontainers) | 🔴 | ⬜ OPEN |
| 19 | Keyset pagination over a computed score neither repeats nor skips a row across pages, including when two rows score identically and the id breaks the tie, and on the ascending price sort | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 19 | A filter on a parent category returns everything in its subtree, via `category_ids @> ARRAY[id]`, and the GIN index is used rather than a sequential scan | integration | `IntegrationTests` + `EXPLAIN` | 🔴 | ⬜ OPEN |
| 19 | An attribute filter uses the GIN index on `attributes` (containment), and two attribute filters combine as AND across keys and OR within one | integration | `IntegrationTests` + `EXPLAIN` | 🔴 | ⬜ OPEN |
| 19 | The fuzzy fallback runs only when the exact pass matched nothing, corrects a real misspelling ("cushin" → cushion), and honours the store's configured threshold through `set_limit` | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 19 | `ListingPublished` / `ListingUpdated` / `ListingDeactivated` / `PriceChanged` each rebuild the variant's row and **re-resolve the buy box**, so a price change that changes the winner changes the row's seller and price together | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 19 | `StockLevelChanged` updates only the two availability columns of the row that names the listing, and touches nothing when the listing is not the current buy-box winner | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 19 | `SubOrderConfirmed` counts units once and **only once** — a redelivered event finds its inbox row and adds nothing, and a concurrent redelivery fails the primary key rather than double-counting | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 19 | A variant whose every offer is withdrawn is **deactivated rather than deleted**, keeps its `units_sold`, and returns to results with its popularity intact when an offer comes back | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 19 | A full rebuild is resumable: walking with a cursor covers every variant exactly once, retires the rows whose offers are gone, and reaches the end of a catalogue larger than one batch | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 19 | A rebuild running concurrently with the event pipeline produces one row per variant and never two, enforced by the unique index rather than by timing | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 19 | The staleness sweep finds only rows older than the configured window, oldest first, and is a no-op on a healthy index | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 19 | `search.search_queries` is created **partitioned** by month with its default partition, a row lands in the right partition, and the click update finds it by both halves of the key | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 19 | The query log is written only when both `SearchSettings.LogQueries` and the `search.query-logging` flag are on, and a failed log write does not fail the search | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 19 | The zero-result report groups on the normalised query, counts click-through and average click position correctly, and separates `search` from `suggest` | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 19 | Synonyms and stop words take effect on the next query after the vocabulary cache expires, and an edit in one process is seen by another within the TTL | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 19 | A bidirectional synonym expands in both directions **and** between siblings — sofa ↔ couch ↔ settee — as the reader builds it | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 19 | `SearchEngineRegistry` falls back to PostgreSQL for each of the four reasons ADR-019 names, and logs which one | unit/integration | `UnitTests` (substituted flags and options) | 🟡 | ⬜ OPEN |
| 19 | The three storefront routes are refused with 404 when their feature flags are off, and answer anonymously when they are on | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 19 | The three admin permissions are enforced: a caller without `search.vocabulary.manage` cannot edit a synonym, without `search.index.manage` cannot rebuild, and without `search.query.read` cannot read the log | security | `IntegrationTests` (authorisation matrix) | 🔴 | ⬜ OPEN |
| 19 | The `search` settings section round-trips through `PUT /admin/settings/search`, and its validator refuses out-of-order price bands, an unknown default sort and every weight at zero | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 19 | `GET /store/products` is served from the output cache for identical query strings and varies correctly by every filter parameter | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 19 | The buy box the search index holds is the **same** offer `GET /store/products/{slug}` shows, for a variant with several sellers, under each configured buy-box rule | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 20 | **A home page is composed, previewed, scheduled and published, and the storefront renders it server-side** — the headline full acceptance criterion, end to end through the API | integration | `IntegrationTests`, then Playwright once Step 23 exists | 🔴 | ⬜ OPEN |
| 20 | **`sitemap.xml` and the structured-data graph validate** — the second headline criterion. The sitemap against the sitemaps.org XSD, the `@graph` against Google's Rich Results test or the `schema.org` shapes | contract | `IntegrationTests` + a schema validator | 🔴 | ⬜ OPEN |
| 20 | The HTML sanitiser refuses an **adversarial** payload set, not only the cases reasoned about while writing it: mixed-case and entity-encoded schemes, `<svg onload>`, nested and unbalanced tags, `<noscript>`, CSS `expression()`, malformed attribute quoting, and mutation-XSS shapes a browser reparses | security | `UnitTests`, with a payload corpus | 🔴 | ⬜ OPEN |
| 20 | A custom-HTML block is refused for a caller without `content.custom-html.write`, and is **withheld at render time** when `content.custom-html` is off — including for blocks already stored | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 20 | Exactly one home page can be `Published`: a second publish is refused by the handler **and** by the partial unique index, under a concurrent double publish | integration | `IntegrationTests`, two simultaneous requests | 🔴 | ⬜ OPEN |
| 20 | An editor holding `content.content.manage` but not `content.page.publish` can reach `InReview` and no further, on every edge of the table | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 20 | The scheduler publishes a page when its time passes, publishes nothing before, is idempotent across two passes, and takes the incumbent home page down when the due page is a home page | integration | `IntegrationTests` with a controllable clock | 🔴 | ⬜ OPEN |
| 20 | A rollback restores the content of an old version as a **new** version, leaves the status alone, and appears in the history | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 20 | A rollback is **refused** when the snapshot no longer satisfies the current block schema, with the offending field named | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 20 | Block reconciliation: a save that keeps some blocks, edits others, adds and removes, updates rows in place rather than deleting and re-inserting the same primary keys, and leaves positions contiguous from zero | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 20 | A block's window is honoured at render: a block starts appearing at `starts_at` and is gone at `ends_at` exactly, with no republish | integration | `IntegrationTests` with a controllable clock | 🟡 | ⬜ OPEN |
| 20 | `GET /store/content/home` resolves every reference in one batch — asserted on the query count, not only on the response | integration | `IntegrationTests`, query interception | 🟡 | ⬜ OPEN |
| 20 | A rule-based collection materialises the products the rule matches, in the rule's sort order, bounded by its limit | integration | `IntegrationTests` over a seeded catalogue | 🔴 | ⬜ OPEN |
| 20 | A refresh **deletes only the rows the rule wrote**: pinned rows keep their position and survive even after the rule stops matching them | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 20 | `ListingPublished` / `ListingUpdated` / `ListingDeactivated` add and remove a product's membership within seconds, and are idempotent under at-least-once redelivery | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 20 | The sweep rebuilds a collection whose rule was edited and one whose "published within N days" condition has aged out — the two cases no event fires for | integration | `IntegrationTests` with a controllable clock | 🟡 | ⬜ OPEN |
| 20 | A collection tile and the product page it links to name the **same seller at the same price**, for a variant with several sellers, under each configured buy-box rule | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 20 | A product card is the buy box of the product's **cheapest purchasable** variant, and falls back correctly when nothing is purchasable | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 20 | A redirect fires for the same page asked for under every normalised form — casing, trailing slash, doubled slashes, a query string, an absolute URL — and its hit counter moves | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 20 | A 410 rule answers 410 with no location, and a rule pointing at itself is refused | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 20 | `robots.txt` is `Disallow: /` and nothing else while `AllowIndexing` is off, and carries the real directives and an absolute `Sitemap:` line when it is on | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 20 | The sitemap index paginates at `SitemapPageSize`, omits empty sections, and every `loc` is absolute and resolves to a route the storefront serves | integration | `IntegrationTests`, cross-checked against the storefront route table | 🔴 | ⬜ OPEN |
| 20 | The `Offer` in a product's structured data carries the **same price and availability** the product page renders | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 20 | `AggregateRating` is emitted only when there are reviews, and the graph still validates when it is absent | contract | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 20 | A `BreadcrumbList` names every ancestor of a product's category in order, and renders without one when the category has been deleted | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 20 | A menu item whose target has been deleted or unpublished is dropped, **along with everything beneath it** | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 20 | A menu tree is refused when a parent is listed after its child, when it nests deeper than three, and when an item's link type and target disagree | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 20 | Banner targeting: an anonymous visitor and a signed-in one are served different sets, and the window is applied in the database rather than in memory | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 20 | The announcement bar is refused without a message, and every other placement without an image and alt text — by the handler **and** by the `CHECK` | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 20 | Every `content` migration applies to an empty database, re-runs clean, and its partial unique indexes and `CHECK`s reject the rows they are meant to | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 20 | `GET /store/content/*` is served from the output cache for identical requests, and the banner read does not leak one visitor's audience to another | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 20 | The preview is refused to a caller without `content.content.manage`, and there is no anonymous route by which an unpublished page can be read | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 20 | Lighthouse SEO ≥ 100 on the home page, a CMS page and a collection page, once Step 23 renders them | a11y/seo | Lighthouse CI, after Step 23 | 🟡 | ⬜ OPEN |
| 21 | **A review can only be posted against a delivered purchase** — the headline full acceptance criterion. Refused for a line that does not exist, one belonging to another customer, one that was cancelled, one still in transit, and one delivered outside the review window; accepted for a delivered one, exactly once | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | **Reports reconcile against transactional data for a seeded dataset** — the second headline criterion. Seed orders, payments, returns and a settlement cycle, run all thirteen reports, and assert every figure against the same sums computed from `orders`, `payments`, `returns` and `settlements` directly | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | The unique index on `(tenant_id, order_line_id)` refuses a second review under **concurrent** submission, not only a sequential one — the race the index exists for | integration | `IntegrationTests`, two parallel writers | 🔴 | ⬜ OPEN |
| 21 | An approved review moves the product's average and its histogram; refusing it afterwards moves both back; a reviewer editing their score moves them again — and each publishes exactly one `ProductRatingChanged` | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | `ProductRatingChanged` reaches **Catalog** and **Search**, and `VendorRatingChanged` reaches **Vendors** — so `ProductProjection.RatingAverage`, the product page and the search index all show the same number | integration | `IntegrationTests`, through the outbox | 🔴 | ⬜ OPEN |
| 21 | Redelivering `ProductRatingChanged` and `VendorRatingChanged` changes nothing — the idempotency the events' carrying an aggregate rather than a delta is supposed to give for free | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 21 | A second helpfulness vote from the same customer **changes** their vote rather than adding one, and withdrawing it puts both counts back | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | A shopper cannot vote on their own review, and the refusal is `REVIEW_CANNOT_VOTE_OWN` | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 21 | Editing an approved review returns it to `Pending` and removes it from the average — the rule that stops moderation being walked around by getting acceptable text approved and then rewriting it | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | A seller may reply to a review of **their own** sale and is refused one of another seller's; platform staff may reply to any | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | A seller listing the moderation queue sees only their own sales, whatever `vendorId` they put in the query string | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | A seller cannot moderate anything — the permission split that stops a seller curating their own rating | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | `AutoApproveReviews` on publishes immediately and off holds in the queue; the same for questions, answers and a seller's own answers | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 21 | An answer's `author_type` comes from the caller's claims: a customer cannot write an answer labelled as the seller's, and the `ck_answers_vendor` constraint holds for a row written any other way | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | Upholding an abuse report refuses the content and moves the rating; dismissing one leaves the content alone — and neither hides anything before a moderator decides | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | The filtered unique index refuses a second **open** report from the same signed-in reporter against the same thing, and permits one after the first is resolved | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 21 | The anonymous report endpoint's rate limiter actually holds under a scripted flood — the control standing in for the uniqueness rule an anonymous reporter cannot have | security | `IntegrationTests` or a load harness | 🔴 | ⬜ OPEN |
| 21 | A customer has exactly one default wishlist even when two tabs save at the same moment — the filtered unique index, under concurrency | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 21 | A wishlist card is priced from **today's** buy box, and an item whose offers have all been withdrawn renders as not purchasable rather than disappearing | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 21 | Turning sharing off revokes the previously issued link, and turning it on again mints a different one | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | A shared wishlist reveals no share token and no notes to the holder of the link | security | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 21 | `StockLevelChanged` crossing from unavailable to available fires the waiting alerts once and closes them; a movement that does not cross the boundary fires nothing | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | Redelivering `StockLevelChanged` or `PriceChanged` sends **no second message** — the inbox guard, which unlike a projection cannot be made right afterwards | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | A price-drop alert fires only for the subscriptions the new price actually satisfies, and a recipient who has opted out of Marketing gets a suppressed row | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | The expiry sweep closes due subscriptions and leaves the rest, and an expired row can no longer be used to send anything | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 21 | Every fact handler is idempotent under redelivery: a redelivered `SubOrderConfirmed` writes no second line, a redelivered `PaymentCaptured` does not double a day's takings, and a redelivered `SubOrderCancelled` does not subtract twice | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | A sale is recorded on **confirmation** and not on placement — so a placed order that is never paid for appears in the funnel and in no revenue figure | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | The category and brand frozen on a fact row do **not** change when a merchandiser later moves the product, so last March's report still says what it said | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 21 | A seller running a report sees only their own figures whatever `vendorId` they pass, and a report declared not vendor-scoped is refused to them with `REPORT_NOT_VENDOR_SCOPED` | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | The commission apportioned across a cycle's lines sums to the cycle's own `TotalCommission`, and the settlement summary's cycle-level figures are exact | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 21 | The nightly inventory snapshot writes one row per stock line per day, and re-running it on the same day replaces rather than doubles | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 21 | A due schedule produces exactly one run, the period it covers is the whole of the previous day/week/month, and `next_run_at` always moves strictly forward — including for a run that failed | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | A produced export is stored in the **private** bucket and is not reachable without a signed link, and the link stops working when it expires | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 21 | A scheduled report's message reaches its recipients through Notifications, and a schedule with no recipients still produces and files the report | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 21 | A report at the row ceiling reports `truncated`, and a period longer than `MaxPeriodDays` is refused rather than silently clamped | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |
| 21 | Every declared report runs against an empty schema without failing — the state of a store on its first day | integration | `IntegrationTests` | 🟡 | ⬜ OPEN |
| 21 | Both migrations apply to an empty database, re-run clean, and every `CHECK` refuses the row it names | integration | `IntegrationTests` | 🔴 | ⬜ OPEN |

---

## Standing debt carried in from before the sprint

These predate the sprint and are already owned by Step 29. Listed here so Step 29 has one worklist.

| Step | Behaviour to prove | Test kind | Where | Risk | Status |
|---|---|---|---|---|---|
| 7 | Authorisation matrix: every endpoint × every role. `07-security-compliance.md` §8 requires it on every CI run | security | `IntegrationTests` | 🔴 | ⬜ OPEN |
| 7A | The Google redirect flow driven from a real browser, including `SameSite=Lax` cookie behaviour — proved by `curl` and attributes today | e2e | Playwright, after Step 23 | 🟡 | ⬜ OPEN |
| 5 | Per-assembly coverage gate (currently gated on the total line rate only) | — | `tools/ci.ps1` | 🟢 | ⬜ OPEN |
| 3 | `X-RateLimit-*` headers on every response, not only `429` | integration | `IntegrationTests` | 🟢 | ⬜ OPEN |

---

## Closing rules

1. A row closes only when a **named, passing test** exists — not when the code "looks right".
2. Record the test's name in the Status cell: `✅ CLOSED — VendorOnboardingTests.ReachesActive`.
3. If a row turns out to be unprovable (missing credentials, third-party sandbox), move it to
   [`PARKING_LOT.md`](PARKING_LOT.md) with the blocker named. Do not delete it.
4. Step 29 cannot be closed while a 🔴 row is open.
