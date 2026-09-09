# Step 29 · Step 14 — Ordering module & order state machine

> Report for the orchestrator. Ledger updates (`TEST_DEBT.md`, `PARKING_LOT.md`, the Step 29 card)
> are deliberately **not** made here.

Twenty `TEST_DEBT.md` rows carry `Step = 14`. **Eighteen are closed** by named, passing tests; **two
are closed in part and left open for their remaining half**, both blocked on the same defect in the
Carts module, which another agent owns this session.

New tests: **28 integration** across five files, and **5 unit** across two.

| File | Tests |
|---|---|
| `tests/KlaraHome.IntegrationTests/Commerce/OrderLifecycleTests.cs` | 8 |
| `tests/KlaraHome.IntegrationTests/Commerce/OrderStateMachineTests.cs` | 6 |
| `tests/KlaraHome.IntegrationTests/Commerce/OrderAuthorisationTests.cs` | 4 |
| `tests/KlaraHome.IntegrationTests/Commerce/OrderInvoiceTests.cs` | 4 |
| `tests/KlaraHome.IntegrationTests/Commerce/OrderConstraintTests.cs` | 6 |
| `tests/KlaraHome.UnitTests/Orders/InvoiceDocumentTests.cs` | 4 |
| `tests/KlaraHome.UnitTests/Orders/PaymentInitiationFallbackTests.cs` | 1 |

They stand on a new `Commerce/OrderScenario.cs`, which builds a seller with a warehouse, stock and a
live offer, gives a shopper an address, fills a basket and walks the five steps of checkout to
`place-order` — all through the routes a person's browser calls, composing `VendorScenario` and
`CatalogScenario` rather than repeating them.

---

## 1. Debt rows

| # | Behaviour to prove | Test | Status |
|---|---|---|---|
| 1 | A two-vendor order splits into two sub-orders that transition **independently** — one delivered while the other is cancelled — and the parent order's derived status follows the documented rule | `OrderLifecycleTests.Two_sub_orders_transition_independently_and_the_order_status_is_derived_from_both` | ✅ CLOSED |
| 2 | **Invoice numbers are gapless per vendor per financial year** — concurrent issues serialise on the counter row, and a rolled-back transaction gives its number back | `OrderConstraintTests.Invoice_numbers_are_gapless_per_seller_per_year_and_a_rollback_gives_one_back`; `OrderConstraintTests.A_failed_placement_gives_its_order_number_back_to_the_series` | ✅ CLOSED — **found defect 3** |
| 3 | Invalid transitions are rejected: every `(from, to)` pair absent from the table answers `409`, and every pair the caller may not take answers `403`, **through the API** | `OrderStateMachineTests.Every_pair_the_machine_does_not_have_is_refused_as_a_conflict_at_every_state`; `.The_delivery_failure_branch_is_a_loop_and_ends_at_a_returned_parcel`; `.The_states_before_payment_and_the_terminal_ones_refuse_every_edge_the_table_lacks`; `.A_seller_is_refused_the_edges_reserved_for_operations_and_the_platform`; `.An_unknown_status_is_refused_with_a_named_error_rather_than_a_binder_failure`; `.A_shoppers_cancellation_right_ends_at_packed_and_the_two_refusals_differ` — between them every one of the sixteen states is asked for every one of the sixteen | ✅ CLOSED |
| 4 | `place-order` end to end for **cash on delivery**: the order is created, both sub-orders reach `Confirmed`, the cart's stock reservations are **committed**, and the coupon and store credit are spent exactly once | `OrderLifecycleTests.Cash_on_delivery_places_a_two_vendor_order_confirms_both_parts_and_spends_the_coupon_once` | 🟡 **CLOSED except store credit** — see §4, Carts |
| 5 | A placement that fails after the order graph is written **rolls the order back and reverses the promotion redemption and the wallet debit** | `OrderLifecycleTests.A_placement_that_fails_at_the_gateway_writes_no_order_and_gives_the_coupon_back` | 🟡 **CLOSED except the wallet debit** — see §4, Carts |
| 6 | A pre-confirmation cancellation of the **whole** order releases the cart-scoped reservations; a post-confirmation one does **not**, and publishes `SubOrderCancelled` with the quantities instead | `OrderLifecycleTests.Cancelling_before_confirmation_releases_the_hold_and_cancelling_after_it_does_not` | ✅ CLOSED |
| 7 | A **partial** cancellation leaves the sub-order in its own state, writes `quantity_cancelled` on the named lines only, and the order's derived net total falls by the pro-rata share of the discounted line totals | `OrderLifecycleTests.A_partial_cancellation_touches_only_the_named_line_and_refunds_its_discounted_share` | ✅ CLOSED |
| 8 | The six integration events are written to `platform.outbox_messages` **in the same transaction** as the change, from the keyed per-context outbox | `OrderLifecycleTests.The_six_order_events_are_written_to_the_outbox_with_the_change_that_caused_them`; `OrderLifecycleTests.An_order_completed_by_cancelling_its_last_open_part_still_announces_completion` | ✅ CLOSED — **found defect 2** |
| 9 | A **customer** cannot read, cancel or transition another shopper's order — every route answers 404, not 403 | `OrderAuthorisationTests.A_customer_reaching_another_shoppers_order_gets_the_same_404_an_invented_id_gets` | ✅ CLOSED — see §5 note |
| 10 | A **vendor** caller cannot read another seller's sub-order, cannot see the order shell of an order they have no part in, and cannot take an edge reserved for Operations | `OrderAuthorisationTests.A_seller_sees_only_their_own_part_and_never_the_shell_of_someone_elses_order`; `OrderStateMachineTests.A_seller_is_refused_the_edges_reserved_for_operations_and_the_platform` | ✅ CLOSED |
| 11 | Order and invoice numbers are **unique per tenant** and the money totals of an order equal the sum of its sub-orders' — asserted against the database | `OrderConstraintTests.Numbers_are_unique_per_tenant_and_an_orders_money_is_the_sum_of_its_parts` | ✅ CLOSED |
| 12 | The order lines' frozen snapshot is **unaffected by a later catalogue edit** | `OrderConstraintTests.A_later_catalogue_edit_does_not_reach_an_order_that_was_already_placed` | ✅ CLOSED |
| 13 | The invoice PDF renders with the statutory content, and the **CGST/SGST versus IGST columns follow the supply type** | `OrderInvoiceTests.An_intra_state_invoice_splits_the_tax_across_cgst_and_sgst_and_names_its_supplier`; `.An_inter_state_supply_is_invoiced_under_igst_and_carries_nothing_under_cgst_or_sgst`; `InvoiceDocumentTests` (4) | ✅ CLOSED — **found defect 1** |
| 14 | An invoice raised while the document store is unreachable still gets its number, with `file_id` null, and can be re-rendered | `OrderInvoiceTests.An_invoice_raised_without_a_document_store_keeps_its_number_and_is_rendered_later` | ✅ CLOSED — **found defect 4** |
| 15 | The lifecycle sweeper completes a delivered sub-order once its return window closes, cancels an unpaid order past the timeout, and is safe in more than one worker (`FOR UPDATE SKIP LOCKED`) | `OrderConstraintTests.The_sweeper_completes_a_lapsed_window_cancels_an_unpaid_order_and_is_safe_in_two_workers` | ✅ CLOSED |
| 16 | A prepaid placement answers `503 PAYMENTS_UNAVAILABLE` **and writes no order** while `IPaymentInitiation` is the refusal — and stops doing so once Step 15 lands | `PaymentInitiationFallbackTests.The_fallback_refuses_with_payments_unavailable_rather_than_throwing`; `OrderLifecycleTests.A_placement_that_fails_at_the_gateway_writes_no_order_and_gives_the_coupon_back` | ✅ CLOSED — see §5 note |
| 17 | Every permission the Orders endpoints declare appears in the Identity permission catalogue, and the system-role bundles grant what the step card says | `OrderAuthorisationTests.The_orders_permissions_are_catalogued_and_the_system_roles_grant_what_the_card_says` | ✅ CLOSED |
| 18 | The order and sub-order list endpoints keyset-paginate correctly across a page boundary, and the vendor filter applies to both | `OrderConstraintTests.Both_listings_page_across_a_boundary_and_confine_a_seller_to_their_own` | ✅ CLOSED |
| 19 | `GET /store/orders/{id}/timeline` never returns an entry with `is_customer_visible = false`, including operator notes | `OrderLifecycleTests.The_storefront_timeline_hides_every_entry_that_is_not_customer_visible` | ✅ CLOSED |
| 20 | An invoice download link is minted only after the ownership check, and a link for somebody else's invoice answers 404 | `OrderAuthorisationTests.An_invoice_link_is_minted_for_its_owner_and_refused_to_everybody_else` | ✅ CLOSED |

### Step 14's own Full acceptance criteria

| Criterion | Test |
|---|---|
| A two-vendor order splits into two sub-orders that transition independently | `OrderLifecycleTests.Two_sub_orders_transition_independently_and_the_order_status_is_derived_from_both` |
| Invalid transitions are rejected | `OrderStateMachineTests.Every_pair_the_machine_does_not_have_is_refused_as_a_conflict_at_every_state` (and the five beside it) |
| Invoice numbers are gapless per vendor per FY | `OrderConstraintTests.Invoice_numbers_are_gapless_per_seller_per_year_and_a_rollback_gives_one_back` |

---

## 2. Defects found and fixed

Four in the Ordering module. Two of them are the sort that leave no trace at all until somebody
looks for the thing that should have happened.

### 1. No invoice has ever had a PDF, and nothing said so

`InvoiceDocumentBuilder` asked for the Indian number format by name —
`CultureInfo.GetCultureInfo("en-IN")` — in a static field initialiser. The product builds with
`InvariantGlobalization=true`, and every host's `runtimeconfig.json` therefore carries
`System.Globalization.PredefinedCulturesOnly: true`, under which that call throws
`CultureNotFoundException`. The throw came out of the type initialiser on the first call to
`Build`, which is inside `InvoiceService.RenderAsync` — and that method swallows rendering failures
**on purpose**, so a briefly unreachable storage bucket cannot hold up a dispatched parcel.

The result: every tax invoice this platform had ever raised was stored with `file_id` null, there
was no document to download, and the only evidence was one log line per invoice. The Step 14 card
names the constraint as a future one — "Invoice rendering needs the `en-IN` culture — **Step 32 must
not build with `InvariantGlobalization`**" — without noticing that every build since Step 1 already
has.

**Fixed** in `src/backend/modules/KlaraHome.Modules.Orders/Infrastructure/Invoicing/InvoiceDocumentBuilder.cs:41`
(and the new `IndianFormat()` at :310). The culture is now built by cloning the invariant one and
setting `NumberGroupSizes = [3, 2]`, which is the whole of what `en-IN` was wanted for — lakh–crore
grouping, `1,23,456.00`. Dates were already written with an explicit `dd MMM yyyy` pattern whose
month names are identical in both cultures, and the currency symbol is deliberately never printed.

The same one-line mistake exists in two other modules; see §4.

### 2. An order completed by a cancellation never announced itself

`SubOrderWorkflow.CancelAsync` re-derived the parent order's status and then did not look at what it
had derived. `TransitionAsync` did, and published `Orders.OrderCompleted`; `CancelAsync` did not.

The state is reachable and ordinary: a two-vendor order where one seller has delivered and completed
and the other then cancels. §5.2 makes the order `Completed` — the shopper received something — the
`completed_at` stamp was written, the order list showed it as complete, and **no `OrderCompleted`
event was ever published**. Settlement, loyalty accrual and the shopper's own "your order is
complete" message all hang off that event, so for this shape of order none of them would ever have
happened.

**Fixed** in `src/backend/modules/KlaraHome.Modules.Orders/Infrastructure/Lifecycle/SubOrderWorkflow.cs`:
both paths now call one `AnnounceCompletion(order, derivedChanged)` (:315, from :139 and :281), gated on whether the
derivation actually *moved* the status rather than on the status it landed in — so the event is
published once by whichever write finished the order and never again by a later one that merely
found it finished.

### 3. The gapless invoice series was not gapless under concurrency — it was a 500

`OrderNumbering` takes its counter row with `SELECT … FOR UPDATE`, and its own documentation is
explicit that "the lock is held until the caller's transaction ends". Two of the three callers had
no transaction. `IssueInvoiceCommandHandler` and `TransitionSubOrderCommandHandler` (and, on the
Shipping-driven path, `OrderFulfilmentService.AdvanceAsync`) called the workflow and then
`SaveChangesAsync`, with no `BeginTransactionAsync` anywhere — so each statement ran in its own
implicit transaction and **the row lock was released by the very `SELECT` that took it**.

Two invoices raised at the same instant for one seller therefore both read the same `next_value`,
both formatted the same number, and the unique index
`ix_invoices_tenant_id_vendor_id_financial_year_invoice_number` rejected the loser — as an
unhandled `DbUpdateException`, i.e. a `500 UNEXPECTED_ERROR` on an operator's dispatch. The test that
found it does exactly what two pickers closing two parcels at once would do. The placement path was
always safe, because `OrderPlacementService` opens its own transaction; that is why order numbers
never showed the fault.

**Fixed** with a new
`src/backend/modules/KlaraHome.Modules.Orders/Infrastructure/Persistence/OrdersTransaction.cs`, which
runs a unit of work in one transaction and commits only on success — rolling back on a `Result`
failure, which is what gives a refused allocation its number back. Applied at
`Application/Orders/AdminOrderFeature.cs:384` (transition) and `:521` (manual invoice), and at
`Infrastructure/Fulfilment/OrderFulfilmentService.cs:127` (the courier-driven advance, which reaches
`Packed` and therefore invoices).

### 4. An invoice with no document could never be given one

Following from defect 1, `IssueInvoiceCommandHandler` answered `409 ORDER_ALREADY_INVOICED` for any
sub-order that already had an invoice row — including one whose render had failed and whose
`file_id` was null. There was no other route that produced the PDF, so an invoice raised during a
storage outage was a statutory document nobody could ever obtain.

**Fixed** in `src/backend/modules/KlaraHome.Modules.Orders/Infrastructure/Invoicing/InvoiceService.cs:82`:
an existing invoice with no `file_id` is re-rendered and the document attached to the invoice that
already exists — no second number, no hole in the series, and the conflict is still returned for an
invoice that does have its document. A re-render that fails again answers the new
`ORDER_INVOICE_RENDER_FAILED` (`503`, `Application/OrdersErrors.cs`) rather than a hollow success:
the automatic issue at dispatch swallows a rendering failure because a parcel matters more, but an
operator who pressed "raise invoice" specifically to repair the document is asking about the
document.

---

## 3. Defects found in the test suite itself

Four in files the harness owns, and one in an existing test. All of the harness ones were latent
because nothing had ever exercised the code: `CommerceTestBase.SignedInShopperAsync` and
`Rest.PostRawAsync` had no callers before this work.

1. **`CommerceTestBase.SignedInShopperAsync` posted to a route that does not exist** — `/store/auth/otp/start`; the endpoint is `/store/auth/otp/request`. Every call answered 404.
2. **…and the capability it uses ships switched off.** `identity.mobile-otp-login` is seeded `false` ("withdrawn from the storefront; needs an SMS provider"). The helper now pins the flag on its own host, the way `AuthenticationTests` already does, rather than through the admin API — the flag is a row shared by the whole collection.
3. **`CommerceTestBase.NewMobile()` produced a bare ten-digit number** while the platform stores and dispatches to E.164, so the code was sent to `+919…` and looked up under `9…`. It now mints E.164, matching `IdentityTestBase.NewMobile()`.
4. **`Rest.PostRawAsync` disposed the request before the send finished** — `using var request = …; return client.SendAsync(request, …);` hands back a task and disposes the body content on the way out, and the test host reads that content asynchronously. Every call threw `ObjectDisposedException`. It now awaits inside the `using`.
5. **`Platform/PlatformApiTests.cs:91` lowers the store's cash-on-delivery ceiling to ₹2,500 and never puts it back.** Store settings are rows in the database the whole collection shares, so from that test onwards every basket over ₹2,500 is refused at the payment-method step for the rest of the run. Four of these tests passed alone and failed in the full suite for exactly that reason, with a message that says nothing about ordering. **Not fixed** — the test is not mine and it asserts the value it writes immediately after writing it, so it is correct in isolation. Worked around instead: `OrderScenario.EnsureCashOnDeliveryAsync` pins the ceiling up before the first cash-on-delivery checkout, so these tests depend on a value they set rather than on one they inherit. **The suite would be better off if that test restored the section in a `finally`**, the way `CatalogPublishingTests` already does for the buy-box section — there will be other tests that trip on this.

---

## 4. Cross-module and shared-file edits

### Defects rooted in another agent's module — **not fixed**

**Carts — there is no way for a shopper to spend store credit, so two debt rows cannot be finished.**

`IStoreCredit` and the quote engine are complete: `QuoteEngine` redeems whenever
`QuoteRequest.WalletRedeemRequested > 0`, clamps it to the balance and the
`pricing.walletMaxRedeemPercent` ceiling, and `OrderPlacementService.SpendAsync` /
`UnspendAsync` spend and reverse it. Nothing ever asks for it:

- `src/backend/modules/KlaraHome.Modules.Carts/Infrastructure/Checkout/CheckoutWorkflow.cs:65` — `ContextFor(session)` builds a `CartRenderContext` with four arguments and leaves `WalletRedeemRequested` at its default of `0m`.
- `src/backend/modules/KlaraHome.Modules.Carts/Infrastructure/Carts/CartRenderer.cs:31` — that default.
- `src/backend/modules/KlaraHome.Modules.Carts/Application/Carts/CartFeature.cs:119,126` and `Application/Carts/AdminCartFeature.cs:225` — `new CartRenderContext()`, likewise.
- There is no storefront route that elects an amount of store credit. The only place a caller can name one is the *stateless* preview `POST /api/v1/store/quote`, which debits nothing.

So `quote.walletApplied` is always `0`, `orders.orders.wallet_applied` is always `0`, and the wallet
branches of `SpendAsync` and `SettleMoneyAndStockAsync` are unreachable from the API.

**Consequence:** debt rows 4 and 5 are closed for the coupon and left open for store credit. Their
Orders halves are written and correct; there is no product path to drive them.

**Proposed fix (Carts):** carry a redemption amount on the checkout session — a
`PUT /api/v1/store/checkout/{id}/store-credit` taking `{ "amount": … }`, stored on
`CheckoutSession` — and pass it through `CheckoutWorkflow.ContextFor` as
`WalletRedeemRequested`. The engine already clamps it, so an over-large or stale amount needs no
handling of its own.

### Defects observed in unowned modules — **not fixed**, one-line each

- **`src/backend/modules/KlaraHome.Modules.Returns/Infrastructure/Documents/CreditNoteDocumentBuilder.cs:32`** and **`src/backend/modules/KlaraHome.Modules.Settlements/Infrastructure/Invoicing/CommissionInvoiceDocumentBuilder.cs:248`** carry defect 1 verbatim: `CultureInfo.GetCultureInfo("en-IN")` under `InvariantGlobalization`. A credit note and a commission invoice therefore cannot render either. Left alone because they belong to Steps 17 and 18 and their owners will want the fix inside their own correctness pass; the fix is the `IndianFormat()` helper now in `InvoiceDocumentBuilder`.
- **`src/backend/modules/KlaraHome.Modules.Catalog/Application/Listings/ListingFeature.cs:311-324`** documents that "the variant's MRP is the default **and the ceiling**… a seller may declare a lower one but not a higher one, or the storefront would show a 'discount' against a number nobody printed" — and then only checks `price > mrp`, never `mrp > variant.Mrp`. A seller can declare an MRP above the printed one. Step 10's, not Step 14's; noted because it was found while reading.

### Files edited outside `KlaraHome.Modules.Orders`

| File | Change | Kind |
|---|---|---|
| `tests/…/Commerce/CommerceTestBase.cs` | `SignedInShopperAsync` route corrected, feature flag pinned; `NewMobile()` now E.164 | shared harness — **bug fix**, §3 items 1–3 |
| `tests/…/Commerce/Rest.cs` | `PostRawAsync` awaits inside its `using` | shared harness — **bug fix**, §3 item 4 |
| `tests/…/Step8/TestDoubles.cs` | `InMemoryFileStorage.IsAvailable` is now settable (was hard-coded `true`) | shared harness — **addition**, needed for row 14 |

`Platform/PlatformApiTests.cs` is **not** edited; §3 item 5 explains why and what was done instead.

No production code outside `KlaraHome.Modules.Orders` was changed. `CommerceApiFactory`, `Sql`,
`OutboxDrain`, `VendorScenario`, `CatalogScenario`, `FakePaymentProvider` and `FakeShippingProvider`
are untouched.

---

## 5. Specification problems found

Three, none of them changed (protocol rule 9).

1. **`02-domain-model.md` §5.1's diagram is a strict subset of the implemented machine.** Four edges exist in code and not in the diagram, each with a reason in the code: `PendingPayment → Cancelled` (the unpaid-order sweeper needs it, and §5.1's prose implies it), `Shipped → Cancelled` (Operations recalling a parcel — the prose says "Operations may cancel later with a reason", the diagram does not draw it), `RtoInitiated → Delivered` (a courier that delivers a parcel it had written off) and `ReturnRequested → Delivered` / `ReturnInProgress → Delivered` (a refused return going back where it was). The tests assert the implemented table. **The diagram should gain the four edges.**

2. **Row 16's premise has expired.** `503 PAYMENTS_UNAVAILABLE` is unreachable through the API: `PaymentsModule` registers `IPaymentInitiation` with `AddScoped`, which supersedes the `TryAddScoped` fallback Orders registers, so a prepaid placement now opens a real collection. The refusal is pinned as a unit test on the class that still implements it, and the "writes no order" half is proved through the API against the gateway's own `503 PAYMENT_PROVIDER_UNAVAILABLE`. **The row should be reworded rather than left implying the old behaviour.**

3. **Row 9's "every route answers 404, not 403" does not hold for the fulfilment routes, and should not.** A shopper reaching `POST /admin/sub-orders/{id}/transition` gets `403` from the authorisation policy, before any id is resolved — they hold no `orders.order.transition` permission. That is not an existence disclosure: the same 403 comes back for an id that never existed. Every route that *does* resolve an id — the six storefront ones — answers 404, and the test asserts all of them. **The row should say "every route that resolves an order id".**

Two smaller notes, neither a specification problem:

- The Step 14 card's "Invoice rendering needs the `en-IN` culture — Step 32 must not build with `InvariantGlobalization`" is no longer true of Orders after defect 1's fix, and was never a *future* constraint: the build already had it. The same sentence still applies to Returns and Settlements until they are fixed.
- `dotnet format --verify-no-changes` reports `IMPORTS: Fix imports ordering` on `KlaraHome.Modules.Inventory/InventoryModule.cs` and `KlaraHome.Modules.Search/SearchModule.cs`. Both are pre-existing and untouched by this work; every file this work changed is clean.
- `Persistence.MigrationPipelineTests.Running_the_migrator_twice_applies_nothing_the_second_time` failed once in a full-suite run with `NpgsqlException: Exception while reading from stream` / `EndOfStreamException` while *opening* a connection during TLS setup. It passes on its own (6/6) and touches nothing this work changed. It is a Docker resource flake: that class starts a second Postgres container beside the collection's, and this machine's Docker VM has 7.5 GB. Worth knowing about as the integration suite grows — it is the kind of failure that reads as a product defect and is not one.

---

## 6. Suite counts

| Suite | Before | After |
|---|---|---|
| Unit | 947 | 952 |
| Architecture | 14 | 14 |
| Integration | 254 | 282 |

`dotnet build -c Release src/backend/KlaraHome.sln` succeeds with **0 warnings**.
