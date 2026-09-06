# Step 15 — Payments module (Razorpay)

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** D · **Depends on:** Step 14
- **Objective:** Take money safely and reconcile it.
- **Deliverables:**
  - Razorpay Orders API integration; hosted/standard checkout handoff (no card data ever
    touches our servers — PCI-DSS SAQ-A posture).
  - Supported methods: UPI, cards, net banking, wallets, EMI; plus **COD** as an internal
    method with its own flow.
  - **Webhook receiver**: HMAC signature verification, replay protection, idempotent handling,
    raw-payload persistence, dead-letter queue for unprocessable events.
  - Payment aggregate: authorisation, capture, failure, retry links, partial and full refunds.
  - Payment ↔ order reconciliation job; settlement report ingestion; mismatch alerting.
  - Payment audit trail and PII/secret handling rules.
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
- **Full acceptance criteria (verified at Step 29, not now):** A sandbox end-to-end payment moves an order to Confirmed; a replayed
  webhook is a no-op; a refund is recorded and reconciles; a dropped webhook is recovered by
  the reconciliation job.
- **Status:** ✅ DONE · **Completed on:** 2026-09-06
- **Outcome / Notes:**

### What was built

The **Payments module** (`src/backend/modules/KlaraHome.Modules.Payments`), owning the `payments`
schema: **seven tables**, **24 endpoints**, three worker loops, and the two seams that connect it to
ordering.

| Area | What exists |
|---|---|
| Provider abstraction | `IPaymentProvider` — `CreatePaymentIntent`, `FetchPayment`, `FetchPaymentsForOrder`, `CapturePayment`, `Refund`, `FetchRefund`, `FetchSettlements`, `VerifyWebhookSignature`, `VerifyCheckoutSignature`, `ReadWebhook`. Two adapters: `RazorpayPaymentProvider` and `InternalCodPaymentProvider` |
| Payment aggregate | `payments` + append-only `payment_attempts`. Authorise, capture, fail, cancel, reopen, refund — every mutator idempotent, because every fact arrives at least once and often out of order |
| Refunds | `refunds` with maker–checker: above a threshold a refund waits for a **different** approver, refused by the handler *and* by `ck_refunds_approver`. Partial and full, `Idempotency-Key` required |
| Webhooks | `POST /api/v1/webhooks/razorpay` — raw body read before binding, HMAC-SHA256 constant-time compare, 5-minute skew window, replay protection on a unique event id, raw payload persisted **before** processing, dead-letter queue after 8 attempts |
| Cash on delivery | `cod_collections`, one per parcel. Collection and remittance are separate instants; batch remittance apportions a courier's net total |
| Reconciliation | `PaymentReconciliationService` — asks the gateway about collections open past the grace period. `SettlementIngestionService` — imports payout reports and matches them line by line |
| Storefront | `GET /store/payments/orders/{id}`, `POST …/retry`, `POST …/verify` |
| Admin | Payments, refunds, gateway events, settlements, cash — under six permissions |

### The three properties this module is built around

1. **No card data reaches these servers.** The instrument is collected by Razorpay's hosted widget
   (ADR-008, PCI-DSS SAQ-A). `RazorpayPayment` declares only the fields this platform is willing to
   keep, so there is nowhere for a PAN or a full VPA to land; `PaymentMethodDetail` stores a card
   network and last four, or the *domain half* of a UPI handle, and nothing else.
2. **Order truth is the webhook plus an API re-fetch, and nothing else.** The browser callback
   records an attempt and confirms nothing. A webhook body is never trusted either — the signature
   proves who sent a claim, not that the claim is current — so `GatewayEventProcessor` uses the
   envelope only to resolve *which* payment, then re-reads it from the gateway's API.
3. **Every route reduces to one workflow.** A webhook, the reconciliation sweep, an operator's
   *sync* and a manual capture all pass a `ProviderPayment` to `PaymentWorkflow.ApplyAsync`. If that
   logic lived in four handlers, the four would drift and the platform would have two answers to
   whether it holds a customer's money.

### The ordering that matters

`ApplyCaptureAsync` confirms the **order first**, through `IOrderPaymentSync`, which commits in its
own transaction; only then does the payment row move, and the caller commits that with the gateway
event. A failure between the two leaves the event `Pending` and the confirmation retryable, and
`MarkPaidAsync` is idempotent by contract. The other way round would leave the payment saying
*captured*, the order unconfirmed, and the event already marked processed — with nothing left to
retry it.

### Files and folders created

```
src/backend/modules/KlaraHome.Modules.Payments/
  PaymentsModule.cs · KlaraHome.Modules.Payments.csproj
  Domain/            Payment · PaymentAttempt · Refund · GatewayEvent · CodCollection ·
                     GatewaySettlement (+Entry) · PaymentLifecycle
  Application/       PaymentsErrors · Payments/{Contracts,StorePaymentFeature,AdminPaymentFeature}
                     Refunds/RefundFeature · Gateway/GatewayEventFeature
                     Settlements/SettlementFeature · Cod/CodCollectionFeature
  Endpoints/         PaymentsPermissions · StorePaymentEndpoints · AdminPaymentEndpoints ·
                     WebhookEndpoints
  Infrastructure/    PaymentsOptions (+RazorpayOptions) · PaymentsScope
                     Gateway/{IPaymentProvider, InternalCodPaymentProvider,
                              Razorpay/{RazorpayHttp, RazorpayModels, RazorpayPaymentProvider}}
                     Processing/{PaymentWorkflow, GatewayEventProcessor, RefundDispatcher,
                                 PaymentReconciliation, SettlementIngestionService}
                     Initiation/PaymentInitiationService · Events/{Publisher, OrderLifecycleHandlers}
                     Jobs/{GatewayEventWorker, PaymentReconciliationWorker(+SettlementIngestionWorker)}
                     Persistence/{PaymentsDbContext, Factory, Configurations, Migrations}
shared/KlaraHome.Contracts/Orders/IOrderPaymentSync.cs        (new seam)
shared/KlaraHome.Contracts/Payments/PaymentEvents.cs          (5 integration events)
modules/KlaraHome.Modules.Orders/Infrastructure/Payments/OrderPaymentSyncService.cs
tests/KlaraHome.UnitTests/Payments/{RazorpaySignatureTests, PaymentLifecycleTests}.cs
```

### Deviations from the specification

1. **`gateway_settlement_entries` is an eighth table §4.9 did not name.** "Settlement report
   ingestion" and "mismatch alerting" are per-line questions, and a `jsonb` array on the report is
   not addressable by the human who has to act on one line of it. §4.9 was amended before the code
   was written.
2. **`gateway_events` is append-only in its *payload* only.** §4.9 says "append-only"; the payload is
   written once and never rewritten, and the processing columns beside it move. That is what lets one
   table be the replay index, the evidence and the dead-letter queue at once. Recorded in §4.9.
3. **No Polly pipeline on the gateway client.** `08-integrations.md` asks for timeout → retry with
   jitter → circuit breaker. This build has the bounded timeout and the host allow-list; a blind
   retry on a money-moving POST is worse than none, and the safe retries already exist as the
   reconciliation sweep. Parked for Step 29/31.
4. **No health check for the gateway**, which the common adapter contract names. Parked for Step 31 —
   an unconfigured gateway must not make a fresh deployment report unhealthy.
5. **The refund-approval threshold is a store setting, not configuration.** `07-security-compliance.md`
   §4 says "configurable"; a governance decision a business revisits after an incident must not need
   a deploy, so it is the `payments` settings section.
6. **The webhook route is provider-named** rather than `{provider}`. The header, the algorithm and the
   body shape are all provider-specific, and a generic route would have to sniff which before it
   could verify anything.

### Known gaps

- **No Razorpay credentials.** By the User's instruction everything is wired and blank. With
  `RAZORPAY_KEY_ID` empty the adapter reports itself unusable, a prepaid placement answers
  `503 PAYMENT_PROVIDER_UNAVAILABLE`, and cash on delivery works end to end. **Nothing has been
  exercised against a live gateway**, so all four full acceptance criteria are open.
- **Nothing consumes the five payment events**, `PaymentMismatchDetected` included — so a
  reconciliation mismatch is a log line rather than an inbox until Notifications carries it.
- **Vendor payouts are Step 18's.** `Razorpay__RouteEnabled` is read and recorded and nothing acts on it.
- **A cash-on-delivery refund has no route out** (Step 17), and `cod_collections.shipment_id` is
  never set (Step 16).

### Verification

`dotnet build src/backend/KlaraHome.sln` succeeds with **0 errors, 0 warnings**. The EF migration
`InitialPaymentsSchema` is generated and the migrator compiles; whether it *applies* is Step 28A's
question. **685 unit tests green** (41 new) and **14 architecture tests green**. Under sprint rule 1
the four things tested while writing them are the paise conversion, the webhook HMAC, the payment
transition table and the maker–checker threshold — all pure, all with a right answer, and all silent
when wrong. Everything else is in `TEST_DEBT.md` (25 rows).
