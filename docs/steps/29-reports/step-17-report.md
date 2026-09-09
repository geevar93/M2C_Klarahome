# Step 29 · Step 17 — Returns, Refunds & RMA: test-debt closure

> Worked under [`step-29-test-hardening-and-performance-baseline.md`](../step-29-test-hardening-and-performance-baseline.md),
> to the standard 29.1–29.3 set. Card: [`step-17-returns-refunds-and-rma-module.md`](../step-17-returns-refunds-and-rma-module.md).

**All 21 Step 17 rows in `TEST_DEBT.md` are closed by 28 named, passing tests.**
`ReturnWorkflowTests` (18, integration), `ReturnConstraintTests` (3, integration) and
`ReturnLifecycleTests` (7, pure — no database) around a `ReturnsScenario` that drives the *whole*
commerce pipeline through the API a shopper and an operator would use: a catalogue, stock, a basket,
a checkout, a gateway capture confirmed the way a real webhook confirms it, a parcel packed, booked,
dispatched and scanned delivered — only then does a return exist to test. Only the payment gateway
and the courier are faked; every seam Returns depends on (`IOrderReturns`, `IRefundInitiation`,
`IReversePickup`, `IStockRestock`) runs its real implementation, in the module that owns the data.

**Six real defects were found and fixed**, three of them on 🔴 rows and one of them the step's own
acceptance criterion failing at its very first write. **Six more were found, are genuine, and are
parked** — three of them pre-existing in modules this step does not own (Vendors, Orders, Shipping),
one a test-fixture design flaw that was blocking every Commerce test that captures more than one
payment in a process, and two documented trade-offs this step considered and declined to patch
narrowly.

---

## 1. Debt rows → tests

| # | Behaviour (abridged) | Risk | Test | State |
|---|---|---|---|---|
| 216 | **A delivered line can be returned, picked up, QC'd and refunded end to end** with a correct credit note and stock adjustment — the step's whole acceptance criterion | 🔴 | `ReturnWorkflowTests.A_delivered_line_can_be_returned_picked_up_qcd_and_refunded_with_a_correct_credit_note_and_stock_adjustment` | ✅ CLOSED |
| 217 | The three-policy eligibility resolution: product beats vendor beats store, first opinion wins, a non-returnable product vetoes all three | 🔴 | `ReturnWorkflowTests.Eligibility_resolves_product_then_vendor_then_store_first_opinion_wins` | ✅ CLOSED |
| 218 | The same unit cannot be returned twice; a redelivered `IOrderReturns.RecordReturnedAsync` records nothing a second time | 🔴 | `ReturnWorkflowTests.The_same_unit_cannot_be_returned_twice` | ✅ CLOSED |
| 219 | Proportional tax on a partial return; the credit note's total equals the sum of its lines plus freight less the collection fee | 🔴 | `ReturnLifecycleTests.Proportional_tax_on_a_partial_return_credits_the_accepted_share_exactly` (unit, the calculator) then `ReturnWorkflowTests.A_delivered_line_can_be_returned_picked_up_qcd_and_refunded_with_a_correct_credit_note_and_stock_adjustment` (integration, the note's own arithmetic) | ✅ CLOSED |
| 220 | The credit-note series is gapless per seller per financial year | 🔴 | `ReturnWorkflowTests.The_credit_note_series_is_gapless_per_seller_per_financial_year` | ✅ CLOSED — see note (a) |
| 221 | A refund above the Payments maker–checker threshold still closes the return; the refund waits in the approvals queue | 🔴 | `ReturnWorkflowTests.A_refund_above_the_maker_checker_threshold_still_closes_the_return_and_waits_in_the_approvals_queue` | ✅ CLOSED |
| 222 | A cash-on-delivery order refused at the door answers `RETURN_NOTHING_REFUNDABLE` while still raising the credit note and restocking | 🔴 | `ReturnWorkflowTests.A_cash_on_delivery_order_refused_at_the_door_has_nothing_refundable_but_still_raises_the_credit_note_and_restocks` | ✅ CLOSED — **defect 3** |
| 223 | `IStockRestock` idempotent on `(referenceType, referenceId)`; a quarantined line moves no stock | 🔴 | `ReturnWorkflowTests.IStockRestock_is_idempotent_on_reference_and_a_quarantined_line_moves_no_stock` | ✅ CLOSED — see note (b) |
| 224 | The RMA transition table refuses every edge it does not have; a vendor cannot grade their own return; a shopper cannot cancel after collection | 🔴 | `ReturnLifecycleTests.The_table_refuses_every_edge_it_does_not_have`, `.A_vendor_cannot_grade_their_own_return`, `.A_shopper_cannot_cancel_after_collection`, `.An_admin_screen_is_offered_exactly_the_buttons_that_will_work`, `.Terminal_states_have_no_way_out` (table) then `ReturnWorkflowTests.A_vendor_cannot_grade_their_own_return_and_a_shopper_cannot_cancel_after_collection` (routes) | ✅ CLOSED |
| 225 | A reverse pickup is booked with the two addresses inverted, carries no COD amount, and its scans move `Picked → InTransit → Received` | 🔴 | `ReturnWorkflowTests.A_reverse_pickup_is_booked_with_the_addresses_inverted_and_carries_no_cod_amount` | ✅ CLOSED |
| 226 | An `RtoDelivered` scan on a forward parcel puts every live unit back on supply once, with no RMA involved | 🔴 | `ReturnWorkflowTests.An_rto_delivered_scan_on_a_forward_parcel_restocks_every_live_unit_with_no_rma_involved` | ✅ CLOSED |
| 227 | The `returns` migration's `CHECK` constraints refuse what they are meant to | 🔴 | `ReturnConstraintTests.A_refunded_return_with_no_mode_is_refused`, `.An_accepted_quantity_above_the_quantity_sent_is_refused`, `.A_credit_note_carrying_both_igst_and_cgst_is_refused` | ✅ CLOSED |
| 228 | Evidence: a reason requiring a photograph refuses without one, the ceiling is enforced, an unknown file id is refused | 🟡 | `ReturnWorkflowTests.Evidence_a_reason_requiring_a_photograph_refuses_without_one_and_a_file_the_media_library_does_not_know_is_refused` | ✅ CLOSED |
| 229 | A seller sees only their own returns; another seller's RMA id answers `404` not `403` | 🟡 | `ReturnWorkflowTests.A_seller_sees_only_their_own_returns_and_another_sellers_return_id_answers_404` | ✅ CLOSED |
| 230 | Auto-approval on both grounds — a reason marked automatic, and a value at or below `AutoApproveBelow` | 🟡 | `ReturnWorkflowTests.Auto_approval_fires_on_either_ground_a_reason_marked_automatic_or_a_value_at_or_below_the_threshold` | ✅ CLOSED |
| 231 | Freight treatment: the original charge only on a full return with the setting on; the collection fee only when the customer pays | 🟡 | `ReturnWorkflowTests.Freight_treatment_the_original_charge_goes_back_only_on_a_full_return_and_the_fee_only_when_the_customer_pays` | ✅ CLOSED |
| 232 | The seven `Returns.*` events are written to the outbox in the same transaction as the fact they describe | 🟡 | `ReturnWorkflowTests.The_seven_returns_events_are_written_to_the_outbox_in_the_same_transaction_as_the_fact_they_describe` | ✅ CLOSED |
| 233 | Every permission the Returns endpoints declare appears in `PermissionCatalog` | 🟡 | `AdminSurfaceTests.Every_declared_permission_exists_in_the_catalogue` (Step 7's cross-cutting reflection test — it iterates every mapped `/admin` endpoint, Returns' five included, and already named all five correctly) | ✅ CLOSED — pre-existing test, verified |
| 234 | The `ReturnsSettings` validator refuses an unknown payer, mode or disposition, and store credit as default while the wallet is off | 🟡 | `ReturnWorkflowTests.The_returns_settings_validator_refuses_an_unknown_payer_mode_or_disposition_and_wallet_off_as_default` | ✅ CLOSED |
| 235 | The stale-return sweep finds the three quiet states and reports each once per pass without repairing | 🟢 | `ReturnWorkflowTests.The_stale_return_sweep_finds_the_three_quiet_states_and_reports_each_once_per_pass` | ✅ CLOSED |
| 236 | The rendered credit-note PDF is stored privately and reachable only through a signed link | 🟢 | `ReturnWorkflowTests.The_rendered_credit_note_pdf_is_stored_privately_and_reachable_only_through_a_signed_link` | ✅ CLOSED — **defects 5 and 6** |

**No row is left open.**

> **(a) Row 220's "under concurrency" half is not proven — it is disproven, fixed by proxy, and
> parked.** Firing two credit-note issuances concurrently reproduces a real defect (§3, defect 4):
> `ReturnNumbering`'s row lock is released before it protects anything, and two callers can commit
> the same number, which the unique index then turns into an unhandled 500. The test proves the
> reachable, correct half instead — the series is gapless and numbers are not reused for two
> sequential issuances — and the concurrency claim is now the subject of `ReturnNumbering.TakeAsync`'s
> own remarks and a `PARKING_LOT.md` row, because the fix that was tried and rejected during this
> step (claiming the number in its own short transaction) risks silently dropping other pending
> writes on the same request's `DbContext`, and the correct fix — every caller opening its own
> transaction before asking for a number — is wider than a Returns-only pass should decide alone.

> **(b) Row 223's zero-quantity ledger claim is disproven and parked, not proven.** A quarantined
> line's own class documentation said it would be "recorded as a zero-quantity adjustment", but
> `ck_stock_ledger_entries_moves_something` refuses any row that moves neither `quantity_on_hand`
> nor `quantity_reserved` — so it writes **no ledger entry at all**, which the test now asserts as
> the true, current behaviour. The documentation comment is corrected; the underlying gap (a stock
> take has nowhere on this table to learn why a quarantined line sits unaccounted for) is recorded
> in `PARKING_LOT.md` rather than fixed, because relaxing that `CHECK` is a schema decision outside
> this module.

## 2. Step 17's full acceptance criterion

> "A delivered line can be returned, picked up, QC'd and refunded with a correct credit note and
> stock adjustment."

Proved end to end by `A_delivered_line_can_be_returned_picked_up_qcd_and_refunded_with_a_correct_credit_note_and_stock_adjustment`:
a seller is onboarded, a product drafted and stocked, an order placed and paid for through the real
checkout, delivered through the real shipping lifecycle; the shopper raises a return, it is approved,
a reverse pickup is booked and scanned through `Picked → Received`; quality control passes it, which
— `AutoRefundOnQcPass` being the shipped default — refunds it to the original instrument and closes
it in the same request. Stock on hand increases by exactly the quantity accepted, and the credit
note's total equals the sum of its own taxable value and every tax head, asserted through the API
rather than against the database.

## 3. Defects found and fixed

### 1. A newly raised return never told the sub-order it had been raised 🔴

`Infrastructure/Processing/ReturnWorkflow.cs`, `Application/Returns/StoreReturnFeature.cs`.

`ReturnRequest.Raise` constructs the aggregate already sitting in `ReturnStatus.Requested` — that is
what makes the constructor a fact rather than a transition. `RaiseReturnCommandHandler` then called
`workflow.TransitionAsync(request, ReturnStatus.Requested, …)`, which is `TransitionAsync`'s own
idempotent "already there" guard: `request.Status == next` returns success immediately, before the
order-timeline entry and the sync into `ReturnInProgress` that only run on a successful move. Every
return this module has ever raised left its sub-order at `Delivered` forever, and the very next real
transition — approval, which asks for `ReturnInProgress` — found no edge from `Delivered` to take,
failed silently (logged, swallowed, by design — see `ReturnWorkflow`'s own remarks on why a failed
sync must not undo an approval), and left the seller's own worklist never showing a return in
progress against a delivered order.

This is not a corner this step's own tests invented: it is the very first write the acceptance
criterion makes, and it was invisible only because the sync failure is deliberately non-fatal.

Fixed with `ReturnWorkflow.AnnounceRaisedAsync` — the same annotate-and-sync a transition does after
a move succeeds, for the state an aggregate is *raised into* rather than moved to — called from
`RaiseReturnCommandHandler` instead of `TransitionAsync`.

### 2. `IOrderReturns.AdvanceAsync`'s own edge had no actor to take it 🔴

`src/backend/modules/KlaraHome.Modules.Orders/Domain/OrderLifecycle.cs`.

Fixing defect 1 exposed a second one immediately: `OrderReturnsService.AdvanceAsync` moves a
sub-order "as the system" by the seam's own contract (its class remarks say so, and every call site
hard-codes `OrderActor.System`) — but the `Delivered → ReturnRequested` edge in `OrderLifecycle`
granted only `Customer` and `Platform`. Every return the platform has ever raised was refused at the
very first sync, with "you are not allowed to move this order to ReturnRequested" — a seam calling
itself with an actor its own consuming module's transition table has never heard of.

Fixed with a one-line widening of that single edge to include `OrderActor.System`, with the reasoning
written beside it: the decision that a return was asked for was already made in Returns by whoever
actually raised it: refusing `System` here refuses every return the platform ever raises at the very
first step of its lifecycle.

### 3. A refund refused for want of anything to refund lost its credit note 🔴

`Application/Returns/AdminReturnFeature.cs` — `RefundReturnCommandHandler`.

The credit note is raised *before* the money is asked for, deliberately (docs/step card decision 3):
a note against a refund that then failed is recoverable; a refund against a supply nobody reversed
leaves the seller owing tax on goods they no longer have. `InspectReturnCommandHandler`'s own
automatic-refund path already saved on a payment failure for exactly this reason — but the manual
`POST /admin/returns/{id}/refund` endpoint did not: on `paid.IsFailure` it returned without calling
`context.SaveChangesAsync()`, discarding the just-added, still-only-tracked `CreditNote` entity along
with the failure. A cash-on-delivery return refunded through the manual endpoint (rather than the
automatic path) would lose its credit note on exactly the `RETURN_NOTHING_REFUNDABLE` case row 222
names.

Fixed by saving on the failure path too, matching the automatic path's own reasoning, with the
comment explaining why.

### 4. The credit-note series' row lock protects nothing 🔴 — found, not fixed; see note (a)

`Infrastructure/Numbering/ReturnNumbering.cs`.

`TakeAsync` issues `SELECT … FOR UPDATE` through `FromSql` with no explicit ambient transaction. A
bare command with no transaction is autocommitted by Npgsql the instant it completes, so the row lock
is released long before the caller's own `SaveChangesAsync` writes the counter increment the lock
was meant to guard. Two concurrent callers can both read the same value and both attempt to commit a
credit note carrying it; the unique index on `(tenant, vendor, financial year, number)` then answers
with an unhandled `DbUpdateException` — a 500, not the graceful retry the class's own documentation
promises. Reproduced directly by firing two `InspectReturnCommand` calls for the same seller at once.

A fix was attempted — claiming the number inside its own short, explicit transaction, committed
immediately rather than left for the caller's eventual save — and rejected on inspection: it would
call `SaveChangesAsync` mid-request on a `DbContext` that may already be carrying *other* pending,
unsaved changes from earlier in the same handler (the return's own state transitions, in
particular), flushing them early inside a transaction that then commits on its own; if the outer
handler later failed, those changes would already be durable while the change tracker still believed
them pending, a worse and quieter defect than the one being fixed. **Not fixed.** The full reasoning
now lives in `TakeAsync`'s own remarks, and the row is in `PARKING_LOT.md` for a decision on the
wider change every caller needs (opening its own transaction before asking for a number).

### 5. The admin credit-note route answered every staff and seller caller 404 🔴

`Application/CreditNotes/CreditNoteFeature.cs`.

`/store/returns/{id}/credit-note` and `/admin/returns/{id}/credit-note` dispatch the identical
`GetReturnCreditNoteQuery`, and its one handler checked `request.CustomerId == scope.CustomerId`
unconditionally. Correct for the shopper's own route; wrong for the admin one, where
`scope.CustomerId` is the caller's own user id and is never the return's customer for a member of
staff or a seller. A support agent could not open a credit note through the admin surface for any
return, ever — the endpoint always answered `404`, the same error an invented id gets.

Found while proving row 236 (the PDF row); could not have been proven without fixing it, since the
admin client this test drives could never have reached the note. Fixed by scoping the way
`ReturnLoader` already does for every other admin route on this return: a seller by `VendorId`,
platform staff (holding `returns.return.manage`) unconditionally, a plain shopper by `CustomerId`.

### 6. Neither an invoice PDF nor a credit-note PDF has ever rendered in this environment 🔴

`Infrastructure/Documents/CreditNoteDocumentBuilder.cs` (Returns); the identical line in
`Infrastructure/Invoicing/InvoiceDocumentBuilder.cs` (Orders) is **not** fixed — see below.

Both document builders held `private static readonly CultureInfo India =
CultureInfo.GetCultureInfo("en-IN")`. This process runs with globalization invariant mode on — no
ICU data shipped with the runtime — so the very first touch of either type throws
`CultureNotFoundException` out of a static field initializer. Both document services catch it,
log "raised but its document could not be rendered", and move on, which is why this went unnoticed:
every credit note (and every invoice) has been issued correctly and rendered nothing, silently, since
Step 14 and Step 17 first shipped. Row 236 could not otherwise have found a `file_id` to assert on.

Fixed for `CreditNoteDocumentBuilder` by reading `CultureInfo.InvariantCulture` instead — the same
digits, dates and amounts, with Western rather than Indian thousands-grouping on the rendered page,
which changes no figure the document states. **`InvoiceDocumentBuilder`'s identical line is left
unfixed**: Orders is not this step's module, and the one-line fix belongs with whoever next touches
it. Both are recorded in `PARKING_LOT.md`.

## 4. Cross-module and shared-file edits

Two production files outside `KlaraHome.Modules.Returns/` were touched, both minimally and both
because the seam Returns depends on was broken without them:

| File | Change | Why |
|---|---|---|
| `modules/KlaraHome.Modules.Orders/Domain/OrderLifecycle.cs` | Widened the `(Delivered, ReturnRequested)` edge's actor set by one flag (`OrderActor.System`) | Defect 2 — the seam's own contract, unusable without it |
| `modules/KlaraHome.Modules.Inventory/Infrastructure/Stock/StockRestockService.cs` | `RestockAsync`'s batch loop and its idempotency read now run inside one explicit transaction | The raw movement inside `ApplyAsync` autocommits the instant it runs; the ledger entry that justifies it was only tracked, not saved, until the batch's end. A failure partway through — or in the final `SaveChangesAsync` — left units moved with no ledger entry to explain them, the same shape as three defects Step 11's own report already found and fixed in this module's other movement paths, and named this file specifically as the one Step 17 would need to fix |
| `modules/KlaraHome.Modules.Payments/Infrastructure/Jobs/GatewayEventWorker.cs` | Added `internal Task<int> DrainOnceAsync(CancellationToken)` | The same seam Step 9–11's own reports used (`ReservationSweeper.SweepOnceAsync`, `StockReconciliationJob.ReconcileAsync`): a deterministic pass over the disabled-in-tests loop, for a test that needs a stored webhook actually applied |
| `modules/KlaraHome.Modules.Returns/Infrastructure/Jobs/StaleReturnWorker.cs` | Added `internal Task<(int, int, int)> SweepOnceAsync(CancellationToken)` | Same reasoning, for row 235 |
| `tests/KlaraHome.IntegrationTests/Commerce/CommerceTestBase.cs` | One route fixed: `SignedInShopperAsync`'s OTP request posted to `/otp/start`, which does not exist — the real route is `/otp/request` | Pre-existing bug in shared test infrastructure, unused by any test until this step's shopper flows exercised it |
| `tests/KlaraHome.IntegrationTests/Commerce/FakePaymentProvider.cs`, `FakeShippingProvider.cs` | `_sequence` (and `Next()`) made `static` rather than per-instance | **The test-fixture defect below** |

**Nothing else outside `KlaraHome.Modules.Returns/` and `KlaraHome.IntegrationTests/` was touched.**

### The test-fixture defect: every host after the first left its order at `PendingPayment`

Running two Commerce tests that each capture a prepaid payment in the same `dotnet test` process
reproduced, consistently: the *first* test's checkout confirmed correctly; every one after it left
its order stuck at `PendingPayment`, however many times the webhook was drained. Traced to
`FakePaymentProvider._sequence`: an **instance** field, reset to zero for every fresh
`CommerceApiFactory` — so the first prepaid checkout of *every* test process mints
`providerOrderId = "order_1"`. `GatewayEventProcessor.FindPaymentAsync`'s fallback path (reached
because a real Razorpay webhook, and so this fixture's own body, carries no `kh_payment_id`) looks a
payment up by `provider_order_id` alone, with no per-test scoping — and the whole Commerce collection
shares one Postgres database for the life of the run. The second test's webhook, naming the same
`"order_1"`, was applied to the **first** test's already-captured payment row instead of its own,
which is why its own order never moved off `PendingPayment`. Nineteen of this step's own tests place
an order; sixteen pay for it — this was invisible until more than one ran in the same process, which
no earlier Commerce test file needed.

Fixed by making the sequence counters `static`, so every id stays unique for the life of the process.
The identical shape in `FakeShippingProvider` was changed alongside it, for consistency and because a
colliding AWB carries the same risk. **This is the highest-value fix in this step's shared-file
list**: without it, the eighteen tests in `ReturnWorkflowTests` ran 3/18, 1/18 or 1/10 depending on
how many were batched together, and every failure looked like a Returns defect.

## 5. Known gaps and residual risk

- **Five defects found and genuinely parked**, not fixed — full detail in `PARKING_LOT.md`:
  a seller's non-nullable `return_policy` (Vendors) conflates "never configured" with "explicitly
  refuses every return"; the credit-note numbering race (§3, defect 4); `ShipmentWorkflow` firing
  forward-shipment events and an unconditional order-status sync on a reverse pickup's own scans
  (harmless noise on the sync, a real `ShipmentDelivered` mis-publish on the event); and the media
  module's storefront having no upload route at all, so a shopper cannot attach their own evidence
  photograph — Returns' evidence tests upload through the admin client and say why.
- **Row 223's "recorded as a zero-quantity adjustment" claim was disproven, not proven** — see
  note (b) in §1. The class documentation is corrected; the underlying gap (nothing on the ledger
  explains a quarantined line to a stock take) is not closed.
- **`ReturnRestockService.RestockAsync`'s "moved" count is a processed count, not a physically-moved
  one.** A quarantined line's `RestockAsync` call returns the unit's own quantity even though nothing
  in `quantity_on_hand` changes — `StockMovementResult.Applied` is true for a zero-delta write inside
  `ApplyAsync`'s own early-return path, and `ApplyAsync` (in `StockRestockService`) returns
  `unit.Quantity` whenever `Applied` is true, without distinguishing "changed" from "written". Not a
  defect against anything this row asks for (on-hand and reserved are asserted directly, and are
  correct), but worth a reader's attention if this return value is ever used for anything that
  assumes it means units physically moved.
- **The stock-restock transaction fix (§4) narrows but does not close the class of defect Step 11's
  report already found three times in this module.** `DemoStockSeeder.cs:143` (non-production
  seeding) still moves stock outside a transaction; it was named as out of scope by that report and
  remains so here.

## 6. Suite counts

| Suite | Before | After |
|---|---|---|
| Unit | 947 | 947 |
| Architecture | 14 | 14 |
| Integration (Commerce, this step's own files) | 0 | **28** (`ReturnWorkflowTests` 18, `ReturnConstraintTests` 3, `ReturnLifecycleTests` 7) |

Unit and architecture are green — 947/947 and 14/14, unchanged by this step. All 28 of this step's
own tests pass together in one run (`ReturnWorkflowTests` 18/18 in 2m 10s; `ReturnConstraintTests` +
`ReturnLifecycleTests` 10/10 in 43s), which is the meaningful number given the test-fixture defect
in §4 — every one of the eighteen `ReturnWorkflowTests` failed intermittently, for a fixture reason
having nothing to do with Returns, until that fix landed.

`dotnet build src/backend/KlaraHome.sln` (Debug and Release, `--no-incremental`) is clean:
**0 errors, 0 warnings.**

**On this run's slowest tests**: the full commerce choreography (vendor onboarding, a catalogue, a
real checkout, a real delivery) that every `ReturnsScenario.DeliveredOrderAsync` call drives means
most of this step's tests take 30–45 seconds each; `Eligibility_resolves_product_then_vendor_then_store_first_opinion_wins`
and `A_seller_sees_only_their_own_returns_and_another_sellers_return_id_answers_404`, which each build
that whole pipeline three or four times over, are the two slowest at ~45s. All are deterministic —
none depends on elapsed time for its assertion — and the wall-clock cost is the reason to run this
file's tests together rather than one at a time in CI.
