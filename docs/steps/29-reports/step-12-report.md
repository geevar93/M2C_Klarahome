# Step 29 — Step 12 (Pricing, tax & promotions): test-debt report

> Worklist: the 24 rows in [`../../TEST_DEBT.md`](../../TEST_DEBT.md) whose **Step** column is `12`.
> Card: [`../step-12-pricing-tax-and-promotions-module.md`](../step-12-pricing-tax-and-promotions-module.md).

**All 24 rows are closed by a named, passing test. Three real defects were found and fixed, one of
them a 🔴 that made the promotion ledger throw on every call it has ever been given.**

Forty-eight tests were written: 25 integration across five new files in
`tests/KlaraHome.IntegrationTests/Commerce/` around a new `PricingScenario`, and 23 unit tests in
`tests/KlaraHome.UnitTests/Pricing/PricingDomainTests.cs` for the window predicates and the wallet
arithmetic, which decide money and need nothing but a constructor.

| File | What it holds |
|---|---|
| `PricingScenario.cs` | The priceable world, built through the API that would have built it: a seller **registered for GST in a chosen state**, a product carrying a **chosen HSN code and rate**, price lists, tax rates, promotions, a registered shopper, and the quote call itself. It composes `VendorScenario` and `CatalogScenario` rather than replacing them — the two things it adds are exactly the two neither can express, and they are what a pricing test is about. |
| `TaxTests.cs` (3) | The rate in force on a past date, the two-state basket, and the thirty-scenario golden suite. |
| `PricingQuoteTests.cs` (6) | The price-list walk, the fallback, the engine's ordering end to end, the ceilings and latency, the rate limit, and the security row. |
| `PromotionTests.cs` (7) | The candidate query, per-customer limits, ledger idempotency, the last-use race, transactional atomicity, reversal, and the coupon switch. |
| `StoreCreditTests.cs` (3) | Reference idempotency and the unique index behind it, the all-or-nothing debit, and the feature flag at both doors. |
| `PricingConstraintTests.cs` (6) | Every `CHECK`, both partial unique indexes, the `jsonb` round-trip, vendor read/write scope, the permission catalogue, and the `PriceChanged` outbox. |

---

## 1. Debt rows

| Behaviour the row names | Test that closes it | State |
|---|---|---|
| 🔴 A golden-file suite of ~30 pricing and tax scenarios passes exactly | `TaxTests.Thirty_pricing_and_tax_scenarios_reproduce_exactly` | ✅ CLOSED |
| 🔴 The price-list walk picks the right row: lowest priority, seller scope, window, highest tier at or below the quantity | `PricingQuoteTests.The_price_list_walk_picks_the_lowest_priority_the_right_tier_and_the_open_window` | ✅ CLOSED |
| 🔴 An offer no list prices falls back to `catalog.listings.selling_price`; a seller's list never prices another seller's offer | `PricingQuoteTests.An_offer_no_list_prices_keeps_its_own_price_and_a_sellers_list_never_prices_another_sellers_offer` | ✅ CLOSED |
| 🔴 `TaxRateResolver` returns the row in force on a past date, not today's | `TaxTests.A_rate_change_leaves_a_past_supply_at_the_rate_that_was_in_force` | ✅ CLOSED |
| 🔴 Two sellers in two GST states: CGST+SGST on one line, IGST on the other; group totals match the sub-order's invoice | `TaxTests.A_basket_across_two_gst_states_splits_one_line_cgst_sgst_and_the_other_igst` | ✅ CLOSED |
| 🔴 `QuoteEngine` end to end in the documented order, every figure reconciling | `PricingQuoteTests.A_quote_composes_price_promotion_tax_shipping_cod_rounding_and_credit_in_that_order`, plus `TaxTests.AssertReconciles` asserted on **every** quote in the suite | ✅ CLOSED |
| 🔴 The candidate query returns only live, in-window promotions and only the coupon the shopper typed | `PromotionTests.The_candidate_query_offers_only_live_promotions_and_only_the_code_the_shopper_typed` | ✅ CLOSED |
| 🔴 A per-customer limit is counted from `promotion_redemptions`, and a reversed redemption still counts | `PromotionTests.A_per_customer_limit_counts_redemptions_including_the_ones_that_were_reversed` | ✅ CLOSED — **found defect 2** |
| 🔴 `RedeemAsync` is idempotent on `(promotion, order)`: delivered twice, redeems once | `PromotionTests.An_order_redeeming_twice_redeems_once_and_increments_the_counter_once` | ✅ CLOSED — **found defect 3** |
| 🔴 The conditional `usage_count` update is a genuine race winner | `PromotionTests.Two_orders_racing_for_the_last_use_produce_one_redemption_and_one_refusal` | ✅ CLOSED — **found defect 3** |
| 🔴 The claim, the redemption row and the outbox event commit together; the explicit transaction really wraps `ExecuteUpdateAsync` | `PromotionTests.A_redemption_commits_the_claim_the_row_and_the_event_together` | ✅ CLOSED — **found defect 3** |
| 🔴 `ReverseAsync` gives back exactly what it took, marks rather than deletes, floors at zero, reports zero twice | `PromotionTests.Reversing_gives_back_exactly_the_uses_it_took_and_reports_nothing_the_second_time` | ✅ CLOSED — **found defect 3** |
| 🔴 `StoreCreditService` is idempotent on its reference, and the unique index enforces it under concurrency | `StoreCreditTests.A_refund_delivered_twice_credits_once_and_the_unique_index_is_what_enforces_it` | ✅ CLOSED |
| 🔴 A wallet debit is all or nothing; the balance cache equals the sum of its transactions | `StoreCreditTests.A_wallet_debit_is_all_or_nothing_and_the_balance_always_equals_its_transactions`, with the pure half in `PricingDomainTests.A_wallet_debit_is_all_or_nothing_and_records_what_was_left` and `..._Expiring_credit_takes_it_away_and_reversing_a_debit_gives_it_back` | ✅ CLOSED |
| 🔴 The vendor query filter shows a vendor their own lists **and the platform's**; `CanWrite` refuses a write to a platform list they can see | `PricingConstraintTests.A_vendor_reads_their_own_price_lists_and_the_platforms_and_writes_only_to_their_own` | ✅ CLOSED — **found defect 1** |
| 🔴 A signed-in caller's quote uses their own id and never one named in the body | `PricingQuoteTests.A_signed_in_callers_quote_uses_their_own_id_and_never_one_named_in_the_body` | ✅ CLOSED |
| 🟡 Every wallet route and `IStoreCredit` mover refuses while `pricing.store-credit` is off; the balance reads zero and inactive | `StoreCreditTests.Every_wallet_route_and_mover_refuses_while_store_credit_is_off` | ✅ CLOSED |
| 🟡 `pricing.coupons` off stops every code; a code typed while off is "not being accepted", not invalid | `PromotionTests.Switching_coupons_off_stops_every_code_and_says_they_are_not_being_accepted` | ✅ CLOSED |
| 🟡 The `pricing` migrations apply and re-run clean; every `CHECK` refuses what it is meant to | `PricingConstraintTests.Every_check_constraint_refuses_what_it_is_meant_to` (18 refusals through the API and behind it, plus the seven tables), with the re-run half already held by the cross-cutting `SchemaMigrationTests` | ✅ CLOSED |
| 🟡 The two partial unique indexes constrain only the rows they name | `PricingConstraintTests.The_partial_unique_indexes_constrain_only_the_rows_they_are_meant_to` | ✅ CLOSED |
| 🟡 `PromotionScope` and `PromotionConditions` round-trip through `jsonb`, tier ladder included | `PricingConstraintTests.A_promotions_scope_and_conditions_round_trip_through_jsonb_intact` | ✅ CLOSED |
| 🟡 Every permission the Pricing endpoints declare appears in `PermissionCatalog` | `PricingConstraintTests.Every_permission_the_pricing_endpoints_declare_exists_in_the_catalogue` | ✅ CLOSED |
| 🟢 `PriceChanged` is written in the transaction that wrote the price, only for the base tier, only when it moved | `PricingConstraintTests.A_price_change_reaches_the_outbox_only_for_the_base_tier_and_only_when_it_moved` | ✅ CLOSED |
| 🟡 `POST /store/quote` stays within its ceilings, is rate-limited, and its latency is measured | `PricingQuoteTests.The_quote_stays_within_its_line_and_promotion_ceilings_and_serves_a_realistic_basket` and `PricingQuoteTests.The_quote_is_rate_limited_as_an_anonymous_storefront_read` | ✅ CLOSED |

**Nothing is left open.**

### Step 12's Full acceptance criterion

> "A golden-file test suite of ~30 pricing/tax scenarios (intra-state, inter-state, coupon + tax
> interaction, rounding) passes exactly."

`TaxTests.Thirty_pricing_and_tax_scenarios_reproduce_exactly` is that suite: exactly thirty
scenarios over seven offers and two sellers registered in two states, asserted to the paisa on nine
figures per line and six per quote. It spans the rate sweep (0 / 5 / 12 / 18 / 28 %) intra-state and
inter-state, a cess-bearing line at 28 + 12 % in both directions and at three quantities, an HSN the
rate table says nothing about (falling back to the product's own rate), an order-level percentage, a
flat amount, a line-level percentage, a discount on a cess-bearing line, rupee rounding in both
directions including an exact half-rupee, an allocation that does not divide evenly, and a
multi-vendor basket with and without a coupon.

Every expected figure was computed from the documented rule — taxable value is
`gross / (1 + (rate + cess) / 100)`, cess is rounded and GST takes the residue, CGST takes the
rounded half and SGST the remainder, the grand total rounds half away from zero to a whole rupee —
and none of it was copied from a run. The test collects every disagreement rather than stopping at
the first, because a rounding change breaks many of them at once and a run that reports only the
first costs an afternoon.

---

## 2. Defects found and fixed

### 1. A seller could not see the platform's price lists, so the rule refusing them the *write* was dead code

`src/backend/modules/KlaraHome.Modules.Pricing/Domain/PriceList.cs:42` — `PriceList` declared
`IVendorScoped` but not `IPlatformShared`.

The generic vendor query filter says "a row with no vendor belongs to the platform, and a vendor user
has no business seeing it either", and `IPlatformShared` is the per-table opt-in that widens the read.
Catalog's `Product` declares it — nothing else did. So a vendor caller could see only their own price
lists, and every platform-wide list was invisible to them.

Four things in the module say the opposite, in writing. `PricingDbContext`'s own summary: "the global
vendor filter then shows a vendor caller their own lists and **the platform's shared ones** and nobody
else's". `PricingScope`'s: "the global query filter shows a vendor caller their own lists and the
platform's shared ones", and `CanWrite` "exists solely to refuse the write to a row a seller can
read". `GET /admin/price-lists` summarises itself as "a vendor caller sees their own and the
platform's". And the debt row states it as the behaviour to prove.

It matters beyond a listing screen: a platform-wide list **prices that seller's own offers**, so a
seller whose offer was selling at a figure they did not set had no way to find out why —
`GET /admin/prices/resolve` ran the same filtered walk and reported the seller's own asking price,
which is a different number from the one the storefront was charging. And `CanWrite`'s platform branch
could never be reached, because a row you cannot read is a 404 long before it is a refusal. This is
the same shape as Step 10's first defect, in the module next door.

Fixed by declaring `IPlatformShared` on `PriceList` and nothing else. The test asserts both halves —
the platform's list is readable and is what `resolve` answers with, and writing to it is refused with
`PRICING_SCOPE` — plus that a *competitor's* list is still a 404, which is the thing the marker must
not have widened.

### 2. A cancelled order gave a shopper their single-use coupon back

`src/backend/modules/KlaraHome.Modules.Pricing/Infrastructure/Calculation/QuoteEngine.cs:276` — the
per-customer redemption count filtered on `redemption.Status == RedemptionStatus.Redeemed`.

`PromotionRedemption`'s own summary explains why that is wrong: "A reversal marks the row rather than
deleting it: a per-customer limit that forgot a cancelled order would let one shopper cycle a
single-use coupon for ever." `IPromotionLedger.ReverseAsync` says the same thing. The row was marked
and then not counted, so the reason the row was kept was defeated by the query that was supposed to
read it.

The exploit is one line long: place an order with the coupon, cancel it, place another. The global
counter is *meant* to give the use back — a cancelled order consumed nothing, and denying the next
shopper would be wrong — but the per-customer limit is a statement about a person, and it is not.
That asymmetry is the whole design, and only half of it was implemented.

Fixed by dropping the status filter, so the count is of redemption rows rather than of standing ones.
The test walks the whole cycle and asserts the asymmetry directly: after the reversal `usage_count` is
back to zero and a *different* shopper can use it, while the original shopper is still told "You have
already used this offer."

### 3. 🔴 The promotion ledger threw on every call, so no order carrying a promotion could be placed or cancelled

`src/backend/modules/KlaraHome.Modules.Pricing/Infrastructure/Promotions/PromotionLedger.cs:80` and
`:152` — both methods called `context.Database.BeginTransactionAsync` directly.

Retry-on-failure is enabled for every context in this platform, and EF refuses a hand-rolled
`BeginTransactionAsync` under a retrying strategy — it cannot re-run a unit of work whose boundaries
it does not own. Every call threw:

```
InvalidOperationException: The configured execution strategy 'NpgsqlRetryingExecutionStrategy'
does not support user-initiated transactions.
```

`KlaraHomeDbContext.ExecuteInTransactionAsync` exists for exactly this, describes exactly this failure
in its own remarks, and calls itself "the only sanctioned way to span more than one
`SaveChangesAsync`". Carts, Catalog, Inventory and Orders each wrap their transaction in
`CreateExecutionStrategy()`. `PromotionLedger` was the only place in the backend that did neither.

The blast radius is the whole promotion feature. `OrderPlacementService` calls `RedeemAsync` whenever a
quote applied a promotion, so **no order with a coupon or a cart rule on it could be placed at all** —
it threw a 500 after the order had been built. `SubOrderWorkflow` calls `ReverseAsync` on every
cancellation, so an order that had somehow been placed could not be cancelled. It has never worked;
there was no test that called it.

Fixed by routing both methods through `ExecuteInTransactionAsync`. Because the helper's operation may
run more than once, both now read their own inputs **inside** the operation and reset what they answer
at the top of it — `refused.Clear()` and `reversed = 0` — which is the shape `ReservationSweeper`
already uses.

Once it ran, three tests could assert what the debt rows actually asked for, including the one that
matters most: `A_redemption_commits_the_claim_the_row_and_the_event_together` arranges the conflict
rather than racing for it. A second connection inserts the redemption row for the same
`(promotion, order)` and holds its transaction open, so the row is invisible to the ledger's "already
redeemed" read; the ledger claims a use and blocks on the unique index; committing the other
connection turns the block into a refusal. One row, `usage_count` back at zero, and no outbox event —
which is what proves the `ExecuteUpdateAsync` claim is inside the transaction rather than beside it.
Had it been outside, the counter would stand at one with no row to explain it, permanently.

---

## 3. Cross-module and shared-file edits

**None.** Every production change is inside `src/backend/modules/KlaraHome.Modules.Pricing/`. No
shared harness file was modified — `PricingScenario.cs` is a new file, and the shopper, seller and
catalogue builders it needs are composed from the existing public members of `VendorScenario` and
`CatalogScenario`.

### Two shared-harness defects found and deliberately *not* fixed

Both are in files three other agents are working against this session. Editing them from here would
put a conflict in a shared harness file for the sake of changes that belong to whoever owns it, so
they are reported with the fix rather than applied.

1. **`CommerceTestBase.SignedInShopperAsync` cannot work** (`CommerceTestBase.cs:90`). It posts to
   `/api/v1/store/auth/otp/start`; the route is `/otp/request`. Nothing calls the method, so the
   404 has never surfaced. Worse, fixing the URL alone would not be enough: both OTP routes are behind
   `identity.mobile-otp-login`, which **ships off** ("Withdrawn from the storefront; needs an SMS
   provider"), so the method's own remark — "there is no other route that produces one with a verified
   mobile number" — describes a flow no shipped deployment offers. Carts and Orders will both need a
   signed-in shopper. The route that works is `POST /store/auth/register` followed by the ordinary
   password sign-in, which is what `PricingScenario.ShopperAsync` does; it is four lines and could be
   lifted into the base class wholesale.
2. **`Rest.PostRawAsync` disposes the request while the send is still in flight**
   (`Rest.cs:105-115`). It builds the request under `using var` and then **returns the task without
   awaiting it**, so `HttpRequestMessage` — and its `StringContent` — are disposed as the method
   returns. It fails intermittently with
   `ObjectDisposedException: 'System.Net.Http.StreamContent'`; it did so here on the second call of
   two, having succeeded on the first. The webhook signature tests depend on this method, so it is
   worth fixing centrally: make it `async` and `await client.SendAsync(...)` inside the `using`. This
   suite no longer calls it.

---

## 4. Specification problems found

**None.** Nothing in the Step 12 card, and nothing in the parts of `03-database-design.md` §4.6 and
`04-api-specification.md` §4 the card names, turned out to contradict what the module should do. The
card's endpoint count (33: 11 price-list, 6 tax-rate, 9 promotion, 4 wallet, 3 storefront) is accurate
against the routes that are mapped, and its statement of the resolution rule, the tax arithmetic and
the engine's ordering is what the tests assert.

The three defects above were all cases of the **code** disagreeing with documentation that was itself
correct — which is why each of them could be written up from the module's own summaries before a line
of the fix was written.

One thing worth recording for Step 14 rather than as a specification problem: the module's remark that
`RedeemAsync` "can refuse a promotion the quote applied" and that "Step 14 must act on that list" is
satisfied — `OrderPlacementService` records the refusals as an internal order note and honours the
quoted total. That path is now reachable for the first time.

---

## 5. Suite counts

| Suite | Before | After |
|---|---|---|
| Unit | 947 | 970 (+23), all green |
| Architecture | 14 | 14, all green |
| Integration | 254 | 279 (+25); all 25 new tests green |

`dotnet build -c Release src/backend/KlaraHome.sln` produces **0 warnings, 0 errors**.

### A note on the full-suite run, for whoever reconciles the numbers

The five new classes were run repeatedly on their own and are **25 / 25 green**.

The final whole-suite run reported 21 failures, **none of them in a Pricing class** and none of them a
test failure: every one is `Npgsql: Failed to connect to 127.0.0.1:55550 — the target machine actively
refused it`, in `VendorConstraintTests`, `VendorAuthorisationTests` and `Platform.AuditTrailTests`.
The Testcontainers PostgreSQL container went away part-way through and everything scheduled after it
failed identically (85 occurrences of that one message). That run took **1292s against a 683s
baseline**, because three agents were running Testcontainers suites on this machine at the same time —
the container was reaped under the load rather than failing a test. Re-running those three classes on
their own, against this same build, gives **28 / 28 green in 114s**.

Worth flagging for Step 29's CI work regardless of this session: a reaped container currently surfaces
as twenty-one unexplained assertion failures spread across unrelated modules, which is an expensive
thing to debug. The fixture could fail fast and say so.

`dotnet format --verify-no-changes` is clean for every file this work touched. It reports two failures
elsewhere, both **pre-existing on `main`** and in modules this step does not own — `IMPORTS: Fix
imports ordering` in `KlaraHome.Modules.Inventory/InventoryModule.cs` and
`KlaraHome.Modules.Search/SearchModule.cs`. Neither file is modified by this work
(`git diff` against them is empty).
