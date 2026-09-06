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
