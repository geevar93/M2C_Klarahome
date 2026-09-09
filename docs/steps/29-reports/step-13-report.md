# Step 29 — Step 13 (Cart & checkout) report

> Part 1 of [Step 29](../step-29-test-hardening-and-performance-baseline.md): the twenty-four
> `TEST_DEBT.md` rows Step 13 deferred, and the defects closing them found.

**Twenty-four rows closed by thirty-four tests, in seven new files.** Four real defects were found
and fixed — one of them a cash-on-delivery rule that had never been written, one an idempotent
replay that could never be reached, one a failure path with no compensation at all, and one a race
loser that would have answered a double-tap with a 500. A fifth defect was in the shared test
harness: `CommerceTestBase.SignedInShopperAsync` had never run, so nothing in the suite had ever
signed a customer in.

---

## 1. The debt rows

| # | Behaviour | Test | State |
|---|---|---|---|
| 117 | A multi-vendor cart produces a correct grouped, priced checkout summary — two sellers, two groups, per-seller subtotals, tax and shipping reconciling to the order total **(Step 13 acceptance criterion)** | `CheckoutTests.A_two_seller_basket_produces_a_grouped_priced_checkout_summary_that_reconciles`; `CartTests.A_two_seller_basket_is_grouped_by_seller_and_the_groups_reconcile_to_the_quote` | ✅ CLOSED |
| 118 | Duplicate place-order requests create exactly one order, sent concurrently **(Step 13 acceptance criterion)** | `CheckoutPlacementTests.Duplicate_place_order_requests_sent_at_once_create_exactly_one_order` | ✅ CLOSED |
| 119 | The unique index on `(tenant_id, idempotency_key)` is the race winner; the loser replays rather than creating a second order | `CheckoutPlacementTests.The_unique_index_wins_the_race_and_a_replay_returns_the_stored_response` | ✅ CLOSED — **defect 2** |
| 120 | A key replayed against a *different* basket is answered `409 IDEMPOTENCY_KEY_REUSED` | `CheckoutPlacementTests.A_key_replayed_against_a_different_basket_is_refused` | ✅ CLOSED |
| 121 | A failed key may be used again and produces exactly one order; one still in progress is refused | `CheckoutPlacementTests.A_key_whose_attempt_failed_may_be_used_again_and_produces_one_order`; `CheckoutPlacementFaultTests.A_key_whose_attempt_is_still_running_is_refused_and_the_first_attempt_still_wins` | ✅ CLOSED |
| 122 | `place-order` holds every line or none, and every failure after the hold — out of stock, ordering refusal, an exception — releases every hold it took | `CheckoutPlacementTests.A_hold_that_cannot_cover_every_line_takes_none_and_releases_what_it_took`; `An_ordering_refusal_after_the_hold_releases_every_unit_it_took`; `CheckoutPlacementFaultTests.An_attempt_that_throws_releases_its_holds_and_leaves_the_key_usable` | ✅ CLOSED — **defect 3** |
| 123 | A cart never holds stock; only `place-order` does | `CartTests.A_basket_reserves_no_stock_and_only_place_order_does` | ✅ CLOSED |
| 124 | A shopper cannot read, edit or place another shopper's basket or checkout | `CartAuthorisationTests.No_storefront_cart_route_takes_a_cart_id`; `A_shopper_cannot_read_change_or_place_another_shoppers_checkout`; `Two_shoppers_reading_the_same_url_get_their_own_baskets` | ✅ CLOSED |
| 125 | The anonymous cookie round trip; a forged or expired token gets a fresh basket | `CartTests.An_anonymous_token_round_trips_hashed_and_a_forged_one_gets_a_fresh_basket` | ✅ CLOSED |
| 126 | Merge on login: quantities sum and clamp, the guest cart is retired, the cookie is cleared, a second call is a no-op | `CartTests.Merging_on_login_sums_and_clamps_retires_the_guest_basket_and_is_idempotent` | ✅ CLOSED |
| 127 | `GET /store/cart` merges exactly once and saves it, and writes on no other request | `CartTests.A_read_merges_once_and_writes_nothing_on_the_next_read` | ✅ CLOSED |
| 128 | Cart validation reports **every** reason at once and blocks only what should block | `CartTests.Cart_validation_reports_every_reason_at_once_and_the_price_notice_never_blocks` | ✅ CLOSED |
| 129 | Serviceability through `IVendorDirectory.IsServiceableAsync`: all-India, exclusion over inclusion, prefix matching, unknown seller | `CheckoutTests.Serviceability_answers_all_india_exclusions_prefixes_and_an_unknown_seller` | ✅ CLOSED |
| 130 | Cash-on-delivery eligibility across **all four** rules, with the reason naming the rule that failed | `CheckoutTests.Cash_on_delivery_is_refused_by_each_of_its_four_rules_with_the_reason_that_failed`; `CheckoutSessionTests.Cash_on_delivery_is_refused_where_no_courier_will_collect_cash` (unit) | ✅ CLOSED — **defect 1** |
| 131 | Changing the destination clears the delivery choices; correcting an address inside the same PIN code keeps them — against a live checkout, not only the aggregate | `CheckoutTests.Moving_the_destination_clears_the_delivery_choices_and_a_correction_keeps_them` | ✅ CLOSED |
| 132 | A delivery choice is re-quoted rather than trusted | `CheckoutTests.A_delivery_choice_is_requoted_and_an_option_that_was_not_offered_is_refused` | ✅ CLOSED |
| 133 | The `carts` migrations apply and re-run clean, and every `CHECK` refuses what it is meant to | `CartConstraintTests.The_carts_migration_applied_into_its_own_schema`; `Every_carts_check_constraint_refuses_what_it_is_meant_to`; `SchemaMigrationTests.Re_running_every_modules_migrations_applies_nothing` | ✅ CLOSED |
| 134 | The two partial unique indexes behave | `CartConstraintTests.The_partial_unique_indexes_allow_a_second_attempt_and_refuse_a_second_live_one` | ✅ CLOSED |
| 135 | `AddressSnapshot` and the `QuoteResult` snapshot round-trip through `jsonb` intact | `CheckoutTests.The_address_and_quote_snapshots_round_trip_through_jsonb` | ✅ CLOSED |
| 136 | The abandoned-cart sweeper marks once, skips an empty basket, retires past retention, closes a lapsed session, and is safe for two workers | `CartConstraintTests.The_sweeper_writes_off_each_basket_once_and_steps_over_a_locked_row` | ✅ CLOSED |
| 137 | `CartAbandoned` and `CartConverted` reach the **keyed** outbox in the transaction that changed the cart | `CartConstraintTests.Cart_abandoned_and_cart_converted_are_written_to_the_keyed_outbox` | ✅ CLOSED (see note) |
| 138 | `place-order` without an `Idempotency-Key` is refused `400`, and the cart writes and `place-order` are rate-limited under their own policies | `CheckoutTests.Place_order_without_an_idempotency_key_is_refused`; `CartRateLimitTests.Storefront_cart_writes_are_limited_under_the_cart_write_policy`; `Place_order_is_limited_under_its_own_policy_and_per_shopper` | ✅ CLOSED |
| 139 | Every permission the Cart endpoints declare appears in `PermissionCatalog` | `CartAuthorisationTests.Every_cart_permission_is_in_the_catalogue_and_the_storefront_asks_for_none` | ✅ CLOSED |
| 140 | A cart render stays within its line ceiling, and its latency is measured with a full basket across several sellers | `CartLimitTests.A_basket_fills_to_its_ceiling_and_still_takes_more_of_what_is_in_it`; `A_full_basket_renders_in_about_what_a_single_line_basket_costs` | ✅ CLOSED (see note) |

**No row is left open.**

### Two rows closed with a qualification

**Row 137 — "so a rolled-back sweep announces nothing".** The half that is proved is the half that
fails silently: the outbox is resolved *keyed by this module's context*, and a mis-keyed resolution
would enqueue onto a different context's change tracker, so this module's `SaveChanges` would write
nothing and the event would vanish with no error anywhere. A row existing at all is therefore proof
that the keyed registration is the one in use. What is asserted beside it is that the announcement
and the status change happen together, that a second sweep over the same basket announces nothing
more, and that a basket the sweep *expired* rather than abandoned is never announced. The literal
rollback case is not separately provoked: the enqueue and the status change are one `SaveChanges`
inside one transaction, and forcing that transaction to fail after the enqueue would need a fault
injected into the sweeper itself.

**Row 140 — latency.** The assertion that carries the weight is a *ratio*: a six-line basket across
three sellers must render in under four times what a one-line basket costs on the same host, in the
same process, against the same database. That is a machine-independent N+1 detector — a render that
walked the lines would show as a multiple however fast the box is. An absolute two-second budget sits
beside it as a smoke check. The real target — `09-nfr-testing-observability.md` §1's 300 ms p95 for a
read — belongs to k6 against a provisioned environment and is Step 29 Part 3's, not this row's.

---

## 2. Defects found and fixed

### 1. Cash on delivery had only three of its four rules, and the missing one was the courier's

`CheckoutWorkflow.CodRefusalReason` documented four rules and implemented three: the store has to
offer cash at all, every item has to be one its seller accepts cash for, and the order has to be
under the value ceiling. The fourth — *a courier has to be willing to collect cash at the
destination* — was written down in the method's own summary and appeared nowhere in its body.

It is not a rule the rest of the flow accidentally covers, because of the order the screens come in.
A delivery service is chosen **before** the payment method, so every option on offer was quoted as a
*prepaid* parcel (`ShipmentQuoteRequest.IsCod = false`) and had nothing to say about cash. The
storefront then showed "Cash on delivery" as available, `PUT …/payment-method` accepted it, and
`place-order` let it through as well: its own last gate reads `DeliveryCheck.Deliverable`, which
`RatedShippingOptions` computes as `covered && serviceable` and which is **true** for a destination
that refuses cash — the refusal is carried separately, in `DeliveryCheck.Refusal ==
DeliveryRefusal.CodUnavailable`. Plenty of Indian PIN codes take a prepaid parcel and refuse a COD
one, so the end of this path is a driver at the door with a parcel and no way to be paid for it.

Fixed in three places, all in the Cart module:

- `Infrastructure/Checkout/CheckoutWorkflow.cs:205` — `CodRefusalReason` gained a
  `DeliveryCheck? destination` parameter and the fourth rule, plus `CodDestinationAsync`, which asks
  the logistics seam with `isCod: true`. The parameter is nullable because the payment-method screen
  can be reached before an address is chosen, and greying the option out then would be wrong.
- `Application/Checkout/CheckoutFeature.cs:716, 767` — `GetPaymentMethodsQueryHandler` and
  `SetPaymentMethodCommandHandler` take `IShippingOptions` and pass the answer in, so the screen and
  the choice agree.
- `Application/Checkout/PlaceOrderFeature.cs:143` — `place-order` now refuses
  `DeliveryRefusal.CodUnavailable` explicitly rather than reading only `Deliverable`.

### 2. A successful place-order could never be replayed — the point of the idempotency key

`PlaceOrderCommandHandler` loaded its session with `CheckoutLoader.LoadAsync(requireOpen: true)`. A
placed session's status is `Placed`, which is not open, so **every** retry of a key that had already
succeeded was answered `409 CHECKOUT_CLOSED — "That checkout is no longer open. Start again from
your basket."** The replay branch inside `ClaimAsync`/`Interpret` — the code that reads the stored
response back — was unreachable for a sequential retry and could only ever fire for a request that
had already got past `LoadAsync` before the winner committed.

This is the ordinary case, not an exotic one. A shopper on a flaky connection sees the request time
out, presses *Pay* again, and their client sends the key it already has; the order exists, and the
platform told them to start over. The `carts.checkout_placements` table, its stored `response`
column and the whole idempotency design existed for exactly this request and never served it.

Fixed at `Application/Checkout/PlaceOrderFeature.cs:100`, by loading with `requireOpen: false` and
deciding the three closed states explicitly:

- `Placing` → `ORDER_PLACEMENT_IN_PROGRESS` (409). Also a correction of the code: a duplicate that
  arrived while the first attempt was running previously got `CHECKOUT_CLOSED`, which tells a
  storefront to send the shopper back to their basket while their order is being created.
- `Placed` → `ReplayAsync`, which returns the stored response when the key was used **against this
  session**, `IDEMPOTENCY_KEY_REUSED` when the key belongs to another session, and `CHECKOUT_CLOSED`
  when the key is unknown. The session check is what stops a client that reused a key being handed a
  confirmation for goods it is not buying.
- Anything else closed → `CHECKOUT_CLOSED`, as before.

### 3. An attempt that *threw* compensated nothing: the stock stayed off sale and the key stayed claimed

Every refusal below the hold released the holds by hand — out of stock, an ordering refusal — and an
exception released nothing. There was no `try` anywhere between `HoldStockAsync` and the final
`SaveChanges`, so anything that threw (Ordering, Payments, the database) left two things behind:

- **the holds**, off sale until Inventory's reservation sweeper expired them, and
- **the placement row**, stuck at `InProgress` for ever — so every later press of *Pay* carrying that
  key was refused with `ORDER_PLACEMENT_IN_PROGRESS`, permanently.

The second is the worse of the two: a transient failure in a collaborator locked the shopper out of
their own order with no way back.

Fixed at `Application/Checkout/PlaceOrderFeature.cs:176`. Everything below the hold moved into a
private `PlaceAsync`, so all three exits — refusal, exception, success — are inside one `try` and no
future failure path can be added without the compensation coming with it. The `catch` releases the
holds, marks the placement `Failed` with a new `ORDER_PLACEMENT_FAILED` code (which makes the key
usable again, because an attempt that created no order has spent nothing), hands the session back at
`PaymentSet`, and rethrows. The compensation itself is best-effort and logs rather than throwing: the
change tracker may be exactly what failed, and a secondary failure must not replace the original.
Two new `LoggerMessage` entries (7221 `Critical`, 7222 `Error`) name the two things an operator would
otherwise have to discover from a stock count.

### 4. The loser of the key race left its own placement on the session

`ClaimAsync` handles losing the race on `(tenant_id, idempotency_key)` by detaching the row it tried
to insert. Detaching is not enough: the row is still in `CheckoutSession._placements`, so the **next**
`SaveChanges` re-discovers it through the navigation, marks it `Added` and hits the same unique index
again — turning a lost race into an unhandled 500.

It is reachable whenever the loser's winner had already *failed*: `Interpret` then restarts the
winner's row and the handler carries on to `session.BeginPlacing()` and a save. Narrow, but it is
precisely the double-tap-on-a-bad-connection case the whole table exists for.

Fixed at `Domain/CheckoutSession.cs:806` (a new `DiscardPlacement`) and
`Application/Checkout/PlaceOrderFeature.cs:339`, which now drops the row from the session as well as
detaching it. Found by reading rather than by a failing test: forcing a loser whose winner has
already failed needs an interleaving no test can pin down. The concurrency tests exercise the
surrounding path and would catch a regression that widened it.

### 5. Harness: `CommerceTestBase.SignedInShopperAsync` had never been executed

Three separate faults in one helper, which is what happens to code nothing calls. Nothing in the
suite had ever signed a *customer* in, so every behaviour that keys off a storefront account was
unreachable.

- It posted to `/api/v1/store/auth/otp/start`. The route is `/otp/request`, so every call 404'd.
- The OTP flow ships behind `identity.mobile-otp-login`, which is declared **off** (it needs an SMS
  provider). It is now pinned on for the commerce host through the factory's existing feature
  overlay, rather than switched on through the admin API — the flags are rows in a schema the whole
  collection shares.
- It read the code back under the ten digits it had typed. A number is normalised to E.164 on the way
  in, so the dispatcher is keyed on `+91…`. A new `E164` helper does the conversion.

---

## 3. Cross-module and shared-file edits

### Defects found in modules this agent does not own — **not fixed here**

**Inventory — availability ignores whether a warehouse is active, and the hold does not.**

- `modules/KlaraHome.Modules.Inventory/Infrastructure/Stock/StockAvailabilityService.cs:70`
  (`FindManyAsync`) and `:46` (`FindAsync`) select every `stock_items` row for a listing and
  aggregate it. Neither joins `warehouses`, so an inactive location's units still count towards
  `QuantityAvailable`.
- The same file at `:141` (`HoldAsync`) joins `context.Warehouses` and filters `row.IsActive` before
  walking the locations, so those units cannot actually be held.

The consequence is a divergence an operator can produce in one click: deactivate a warehouse and the
basket goes on saying "in stock" while `place-order` refuses with `CART_ITEM_OUT_OF_STOCK`. Every
shopper holding that offer walks to the payment screen and is turned away there. The two reads should
agree; the natural fix is for the aggregate to exclude inactive locations, which is Inventory's call
to make.

*(This report's row-122 test uses that divergence deliberately, as the only deterministic way to make
a hold refuse after validation has passed. If Inventory closes it, that test needs a new way to stage
a partial hold failure — a note has been left in the test's own remarks.)*

**Shipping — `DeliveryCheck.Deliverable` does not include cash collection.**

`modules/KlaraHome.Modules.Shipping/Infrastructure/Quoting/RatedShippingOptions.cs:135` computes
`Deliverable` as `covered && answer.IsServiceable`, while `DeliveryCoverageService.RefusalFor` can
return `DeliveryRefusal.CodUnavailable` alongside it. A caller that reads only `Deliverable` — which
`place-order` did — accepts a destination no courier will collect cash at. Carts now reads `Refusal`
as well (defect 1), so nothing is broken today; it is recorded because the shape invites the same
mistake from the next caller, and because the contract's own summary says the check "answers the two
questions an address has to pass … and says which one refused".

### Shared harness files added to

All additive, all flagged as required:

| File | Change |
|---|---|
| `CommerceApiFactory.cs` | New `Overlays` property (a list of `Action<IServiceCollection>`) applied last in `ConfigureTestServices`. It is the only way to prove what a module does when a collaborator *throws* or *blocks*: nothing real can be asked to do either on cue. Empty for every host but `CheckoutPlacementFaultTests`. |
| `CommerceTestBase.cs` | `SignedInShopperAsync` repaired (defect 5) and a new `E164` helper. This is a **modification** of an existing member, not only an addition — the member had never run. **The Orders agent fixed the same three faults independently on its own branch; the two versions need reconciling by hand at merge.** They agree on the route (`/otp/request`) and on pinning `MobileOtpLogin`; they differ on the third — this branch keeps `NewMobile()` returning the ten digits a request carries and normalises at the *lookup* (`Factory.Otp.Latest(E164(number), …)`), where the Orders branch reportedly normalises at generation. Either is correct; taking Orders' version costs nothing here, because no test on this branch asserts on the shape of the number itself. |

`Rest.PostRawAsync` — the intermittent `ObjectDisposedException` the Orders agent fixed — is **not
used by anything here**, so this branch carries no version of that fix and nothing to reconcile. The
one raw-bodied request these tests send (the delivery-choice injection in row 132) is built inline
and awaited inside its own `using`.

### Production file outside the debt's direct scope

| File | Change |
|---|---|
| `modules/KlaraHome.Modules.Carts/Infrastructure/Jobs/AbandonedCartSweeper.cs` | New `internal SweepOnceAsync`, so a test runs one pass instead of racing a timer. Same code, same schedule of one. (Inside the module this agent owns.) |

### Unit tests updated

`tests/KlaraHome.UnitTests/Carts/CheckoutSessionTests.cs` — the four existing
`CodRefusalReason` tests now pass a `DeliveryCheck`, and two were added for the fourth rule and for
the no-address-yet case.

---

## 4. Store credit at checkout — a functional gap, not a test gap

Raised by the Orders agent, whose Step 14 rows are waiting on it: nothing ever sets
`CartRenderContext.WalletRedeemRequested`, so store credit can never be applied. It is Carts code, so
the finding is answered here. **It is a missing feature, and building it is not in Step 13's scope.**

The plumbing is real and complete as far as it goes. `CartRenderer` passes
`context.WalletRedeemRequested` into every `QuoteRequest`
(`Infrastructure/Carts/CartRenderer.cs:197`), and Pricing's engine honours it — clamping the request
against the balance, against `WalletMaxRedeemPercent` and against the rounded grand total, then
reporting `WalletApplied` and `AmountPayable` (`QuoteEngine.cs:292, 399`). What is missing is a
shopper's way to *elect* an amount. Every construction of the context in this module takes the
default of zero:

- `CartWorkflow.CommitAsync` / `RenderAsync` — `new CartRenderContext()`
  (`Application/Carts/CartFeature.cs:119, 126`);
- `CheckoutWorkflow.ContextFor` — four positional arguments, stopping before `IsFirstOrder` and
  `WalletRedeemRequested` (`Infrastructure/Checkout/CheckoutWorkflow.cs:65`);
- `AdminCartFeature`'s operator view — the same (`Application/Carts/AdminCartFeature.cs:225`).

So the quote snapshot Orders copies onto every order carries `WalletApplied = 0` by construction. The
one place a non-zero amount reaches the engine at all is Pricing's own preview,
`POST /store/quote` (`StorePricingEndpoints.cs:31, 77`), which prices an arbitrary basket and is
attached to no cart and no checkout: a shopper can be *shown* what their credit would do and can
never spend it.

**This is exactly the gap the Step 13 card declares at its own boundary** — "Known gaps at this
boundary", item 3: *"Store credit cannot be applied at checkout. `CartRenderContext.WalletRedeemRequested`
is plumbed through to the quote engine but no endpoint sets it."* It was recorded as built-and-known,
not deferred as a test, and it has no `TEST_DEBT.md` row, no Step 13 deliverable and no acceptance
criterion behind it — the deliverables name "payment method selection (prepaid vs COD), COD
eligibility rules and limits" and are silent on store credit.

Closing it is a feature with four moving parts, not an assertion:

1. a column on `carts.checkout_sessions` for the amount the shopper elected, and a migration for it —
   the election has to survive the round trip, or it is lost on every re-price;
2. a storefront route to set it (`PUT /store/checkout/{id}/wallet`), with its own refusals for a
   balance that has since been spent;
3. `CheckoutWorkflow.ContextFor` passing it through, so the review screen and the placement snapshot
   agree;
4. a decision about what the shopper is shown when the engine clamps their request below what they
   asked for — the engine returns the clamped figure and says nothing about why.

Writing that here would be inventing a deliverable mid-step, which protocol rules 3 and 6 rule out.
**Recommendation: it belongs in the Parking Lot as a scheduled feature with an owner, and the two
Step 14 rows waiting on it should stay open against that item rather than against this report.**

---

## 5. Specification problems found

**None.** Two things were checked and found consistent:

- The new stable error code `ORDER_PLACEMENT_FAILED` does not contradict
  `04-api-specification.md` §1.2, which does not enumerate the cart and checkout codes; it sits
  alongside `ORDER_PLACEMENT_IN_PROGRESS` and `IDEMPOTENCY_KEY_REUSED`, which are likewise declared
  only in `CartsErrors`.
- The Step 13 card's "**`POST /store/checkout/{id}/place-order` therefore cannot succeed on this
  build**" is a statement about the Step 13 boundary and was superseded by Step 14. Every place-order
  test here runs against the real `OrderPlacementService`.

The four-versus-three cash-on-delivery discrepancy was a defect in the code, not in a document: the
card, the contract and the method's own summary all said four.

---

## 6. Suites

| Suite | Before | After |
|---|---|---|
| Unit | 947 | **949** (+2: the fourth cash-on-delivery rule, and the no-address-yet case) |
| Architecture | 14 | **14** |
| Integration | 254 | **288** (+34) |

The unit baseline is 947 rather than the 946 the Step 29 card records at 29.3; the four existing
`CodRefusalReason` tests were amended in place rather than added to, so the delta is exactly the two
new ones.

`dotnet build -c Release src/backend/KlaraHome.sln` — **0 warnings, 0 errors**.

`dotnet format --verify-no-changes` — clean for every file this work touched. It reports two
pre-existing `IMPORTS` failures in files nothing here went near —
`modules/KlaraHome.Modules.Inventory/InventoryModule.cs` and
`modules/KlaraHome.Modules.Search/SearchModule.cs` — which are on `main` and are somebody else's to
fix.

### A note on running the integration suite while four agents share one machine

The final numbers above come from a clean, uncontended run: **288 integration tests, 0 failed, 0
skipped, 1166 s**.

Two earlier whole-suite runs failed widely and *differently from each other*, in classes nothing here
had touched (`ExternalLoginTests`, `SessionTests`, `CatalogAuthorisationTests`), with
`SocketException: the target machine actively refused it` and `500` out of a sign-in — the
Testcontainers PostgreSQL going away underneath a running test while four agents' suites shared one
Docker daemon. Every affected class was re-run in isolation and passed, and the third run, taken
once the other worktrees were quiet, was green end to end. Recorded here because the same trap is
waiting for whoever runs `pwsh tools/ci.ps1 all` next to somebody else's suite.
