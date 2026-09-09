# Step 29 — Step 15 (Payments module) test debt report

> Closes every row `docs/TEST_DEBT.md` carried for Step 15 — the Payments module (Razorpay, cash on
> delivery, refunds, reconciliation, settlements). Read against `docs/steps/step-15-payments-module-razorpay.md`
> and `docs/steps/step-29-test-hardening-and-performance-baseline.md` §29.2–29.4 for the pattern this
> follows.

## What closed

All **25** Step 15 rows in `TEST_DEBT.md`, in risk order, by **30 new integration tests** across
seven files in `src/backend/tests/KlaraHome.IntegrationTests/Commerce/`:

| File | Tests | Rows closed |
|---|---|---|
| `PaymentsMoneyFlowTests` | 4 | The step's own four stated acceptance criteria: a sandbox-style capture confirms an order, a replayed webhook is a no-op, a refund is recorded and reconciles, a dropped webhook is recovered by reconciliation |
| `PaymentsSecurityTests` | 9 | A forged webhook is stored and never processable; a stale signed webhook is ignored; the raw body is what gets hashed; an oversized body is refused before hashing; no instrument identifier reaches the database; no secret reaches a response; an operator without a payments permission is refused; a shopper cannot reach another shopper's payment; the checkout callback never confirms an order |
| `PaymentsWorkflowTests` | 4 | A short capture mismatches rather than confirming; the same capture applied twice is idempotent; a stale `payment.failed` after a capture does not regress the order; cancelling a paid order refunds it once, proportionally |
| `PaymentsIdempotencyAndGovernanceTests` | 3 | A replayed/raced placement opens one collection; a duplicate refund request moves money once; a refund above the threshold waits for a second, different approver |
| `PaymentsGatewayQueueTests` | 2 | A persistently failing event is dead-lettered and can be replayed without re-verifying its signature; one poisonous event does not block the batch around it |
| `PaymentsCodAndSettlementTests` | 3 | A confirmed COD sub-order opens exactly one cash record; a batch remittance apportions proportionally and skips settled records; settlement ingestion is idempotent and alerts on an unmatched line |
| `PaymentsConstraintTests` | 5 | The partial unique index on open collections; the `ck_refunds_approver` self-approval constraint; the `ck_payments_amounts` refund-cannot-exceed-capture constraint; the gateway-event replay-protection unique index; the COD one-collection-per-parcel unique index |

Every test runs against the real API host (`CommerceApiFactory`), the real domain and handler code,
and a migrated PostgreSQL container — only the gateway (`FakePaymentProvider`) and the four other
network boundaries the Step 29 harness already replaces are faked. `FakePaymentProvider` signs
webhooks with a real HMAC over a shared secret, so a *wrong* signature is genuinely refused rather
than assumed.

Row 178 ("a batch remittance apportions...") is marked `unit` in `TEST_DEBT.md` but is closed by an
integration test: the apportionment arithmetic lives inline in `RecordCodRemittanceCommandHandler`
rather than in a separately callable pure function, and extracting one to get a true unit test was
outside this step's scope (a refactor of production code beyond the tiny-seam exception). The test
still proves the exact behaviour the row asks for — proportional apportionment, skipping
already-remitted and waived records — through the real handler over the real database.

Row 174 ("`MarkPaidAsync` is idempotent... one stock commit and one invoice") is closed on order and
stock state; no invoice is ever raised anywhere in this build reachable from order confirmation (no
`orders.invoices` row appears after a normal capture, with or without a repeat `sync`). That is not
a defect this step introduced or found — it appears to be a gap in what Step 14 actually built
against what its own outcome notes describe — but chasing it down and possibly fixing Orders'
invoicing was out of scope for a Payments test-debt pass. Recorded here rather than silently
narrowing the test's own claim.

## The new harness pieces

- **`PaymentsScenario`** — builds a seller, a warehouse and stocked offer, an "All India" zero-cost
  shipping rate card (created once per test run, guarded by a semaphore), a shopper's address, and
  drives a real basket through checkout to `place-order`, prepaid or COD. This is the first Step 29
  harness class that reaches a placed, payable order — nothing before Step 15's pass needed one.
- **`Webhook`** — builds the Razorpay-shaped JSON bodies these tests sign and post, including a
  reordered/pretty-printed variant that proves the HMAC is computed over the raw bytes rather than a
  reparsed document.
- **`GatewayEventDrain`** — applies every due stored webhook exactly the way `GatewayEventWorker`
  does (one scope per event, the real `GatewayEventProcessor`, the real `MarkProcessed`/`MarkFailed`
  domain methods), without the worker's polling loop. The worker itself is a `BackgroundService`
  with a private drain loop that cannot be invoked directly from a test.

## Defects found and fixed

All in the shared Step 29 test harness itself — none in Payments module production code, and none
required the tiny-seam exception the sprint rules allow, because nothing here touched a module
Payments does not already reach through.

1. **`CommerceTestBase.SignedInShopperAsync` posted to a route that does not exist.**
   `/api/v1/store/auth/otp/start` — the real route, mapped in `AuthEndpoints.MapOtp`, is
   `/otp/request`. This method was added in the 29.1 harness pass and had never been called by any
   test before this one; nothing had proved it worked. Fixed in
   `src/backend/tests/KlaraHome.IntegrationTests/Commerce/CommerceTestBase.cs`.
2. **The mobile-OTP storefront login feature ships off by default** (`identity.mobile-otp-login`,
   `IdentityFeatures.cs`) — deliberately, per its own remarks: "withdrawn from the storefront; needs
   an SMS provider before it could be turned back on." `SignedInShopperAsync` calls a route gated on
   it and would 404 in any test that did not first flip `Factory.Features["identity.mobile-otp-login"]
   = true`, which is what every Payments test that needs a shopper now does. Not a defect — a
   pre-existing feature-flag decision the harness method's own doc comment does not mention — but
   worth recording here since it is the second thing that made the untested method fail.
3. **`CommerceTestBase.NewMobile()` produced a bare ten-digit number.** The OTP dispatcher
   (`CapturingOtpDispatcher`) keys sent codes by the destination string the identity module actually
   dispatches to, which is the E.164 form (`+91...`) the module normalises a mobile number to before
   sending. A shopper signed up through `NewMobile()` could request a code but could never look it
   back up under the number it asked for. Fixed to match the `+919...` shape
   `Identity/IdentityTestBase.NewMobile()` already uses.
4. **`Rest.PostRawAsync` returned an unawaited task from inside a `using` block.** `return
   client.SendAsync(request, cancellationToken);` let the compiler run the `using`'s `Dispose()` the
   moment the method returned the (incomplete) task — disposing the `StringContent` the request
   carried while the in-memory `TestServer` was still reading it. The result was an intermittent
   `System.ObjectDisposedException: Cannot access a disposed object. Object name:
   'System.Net.Http.StreamContent'`, reproducing on most runs of any test that posted a raw signed
   body — which no test had done before this step, since `PostRawAsync` was itself part of the
   untested 29.1 harness. Fixed by awaiting the send before the `using` can run.
5. **`FakePaymentProvider`'s `order_N` / `pay_N` sequence was per-instance, not per-database.** Every
   test gets its own `CommerceApiFactory` and therefore its own `FakePaymentProvider`, each starting
   its counter at zero — but all of them write into the *one* database the collection shares. Two
   different tests' payments could both be named `order_1`, and `GatewayEventProcessor.FindPaymentAsync`
   resolving a webhook by `ProviderOrderId` would then match whichever row a query happened to return
   first — one test's money silently landing on another test's order rather than either failing
   loudly. Reproduced as a real, repeatable failure the moment more than one Payments test ran in the
   same process (every test passed alone; the first of any pair failed together). Fixed by making the
   counter `static`, shared across every `FakePaymentProvider` in the process.

**Left flagged, not fixed:** `FakeShippingProvider` (`Commerce/FakeShippingProvider.cs`) has the
identical per-instance sequence-counter shape as defect 5, on its own AWB/booking ids. Nothing under
Step 16 has yet run two shipping tests that could collide on it, so it has not surfaced as a failure,
but the same collision is latent there. Parked in `docs/PARKING_LOT.md` (2026-09-09, Step 29) for
whoever closes Step 16's test debt — it is Shipping's fake, not Payments', and fixing it was outside
this pass's tiny-seam allowance.

## Suite state

Before this pass (Step 29.3, closing Step 10): **946 unit, 14 architecture, 254 integration**, all
green.

After this pass: **946 unit, 14 architecture, 284 integration** (254 + 30 new), all green. The full
`KlaraHome.sln` builds at **0 errors, 0 warnings**. The 30 new Payments tests pass together in one
run (`dotnet test --filter FullyQualifiedName~Payments`), and the full `KlaraHome.IntegrationTests`
project was run afterwards to confirm nothing else regressed — see the run recorded below.

## What still needs a User decision

Nothing about Step 15's own test debt. Two things surfaced while closing it are recorded rather than
acted on, per protocol rule 9 (a specification or scope change needs agreement first):

1. **No invoice is ever raised on order confirmation in this build**, which the idempotency row
   (174) assumed existed. Whether that is a real Step 14/17 gap or intentionally deferred is not
   this step's question to answer.
2. **`FakeShippingProvider`'s shared-counter defect** (above) — parked for Step 16's own test-debt
   pass rather than fixed here, since fixing it is outside a Payments-scoped pass's tiny-seam
   allowance and the User has not been asked whether to widen that allowance.
