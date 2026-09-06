# Step 18 — Settlements, Commission & Vendor Payouts

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** D · **Depends on:** Step 17
- **Objective:** Pay the vendors correctly and defensibly.
- **Deliverables:**
  - Commission calculation per order line using the vendor's plan; platform fee, payment
    gateway fee, shipping cost allocation.
  - Statutory marketplace deductions modelled: **TCS under GST (Sec 52)** and
    **TDS under Sec 194-O**, with configurable rates and reporting extracts.
  - Vendor ledger (double-entry style): earnings, deductions, refunds/chargebacks, adjustments,
    opening/closing balance per settlement cycle.
  - Settlement cycle scheduler; payout execution via Razorpay Route / RazorpayX behind a
    provider interface; payout status reconciliation.
  - Vendor-facing statements and downloadable reports; platform revenue reporting.
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
- **Full acceptance criteria (verified at Step 29, not now):** For a sample month, vendor ledger balances tie out to orders,
  returns and payouts to the paisa; the TCS/TDS extract matches expected values.
- **Outcome / Notes:**

  **Status: ✅ DONE (build acceptance).** The `settlements` schema is built — five tables — the
  solution compiles at **0 warnings**, the EF migration is generated and the migrator compiles,
  **756 unit tests** pass (34 new) and the **14 architecture tests** pass. Nothing is proved against
  a database, a gateway or a bank: **26 rows in [`TEST_DEBT.md`](../TEST_DEBT.md)**.

  ### What was built

  **The ledger, and the one idea the module rests on.** `settlements.ledger_entries` is append-only
  and a seller's balance is **not a column anywhere** — it is `Σ credits − Σ debits` over the rows,
  so it cannot drift from its own history and a correction is a reversing entry rather than an edit.
  Twelve entry types (`sale`, `commission`, `platform_tax`, `platform_fee`, `payment_fee`,
  `shipping_fee`, `refund`, `refund_commission_reversal`, `tcs`, `tds`, `adjustment`, `payout`),
  each always a credit or always a debit except the adjustment, which is the one entry a human
  writes. Every row carries a `source_key` **derived from the fact** rather than generated, with a
  unique index behind it — which is what makes at-least-once integration-event delivery safe on a
  table that moves money.

  **What a sale earns and what comes off it.** `SettlementCalculator` is a pure function of a sale,
  the store's policy and — only for a line frozen without one — a commission quote. Commission is
  **read off the frozen order line, never re-resolved**: re-resolving would charge last month's sale
  at today's rate. The seller is credited what the shopper paid for their part including delivery,
  and the freight is charged straight back as its own entry, because "what the sale was worth" is a
  supply on a GST return and "what the courier cost" is a charge on an invoice, and netting them
  loses both. `platform_tax` carries the GST on commission, marketplace fee and gateway fee together,
  because the platform raises one tax invoice for the three and the seller claims credit against it.

  **When a sale is earned.** A prepaid parcel earns on `SubOrderStatusChanged → Delivered`. A
  cash-on-delivery parcel earns **only** on `CodCashRecorded` with `IsRemitted` — the money exists
  when the courier collects it and is the platform's when they hand it over, and crediting a seller
  in between would be paying out of pocket. Both go through one method with one source key.

  **What reverses it.** `SubOrderCancelled` and `Returns.CreditNoteIssued`, both proportional and
  both clamped to what is left of the sale. `Payments.RefundProcessed` is **deliberately not
  consumed** — this ledger reverses supplies, not payments, and subscribing to all three would
  reverse the same sale twice. Freight is never reversed: a parcel that was delivered and then
  returned was still delivered.

  **The two statutory deductions, on two different bases.** `StatutoryDeductions` computes TCS under
  section 52 of the CGST Act on the **net value of taxable supplies** and TDS under section 194-O on
  the **gross amount including GST**, from the same period, and the single commonest way of getting
  this wrong is to use one base for both. Section 206AA's higher rate applies where the seller has
  furnished no PAN; the annual threshold is evaluated on the financial year to date across cycles,
  not on the period. Both rates are store settings, because a rate changes by a notification in the
  Gazette and a deployment that needed a rebuild to follow one would file a wrong return.

  **The cycle.** Half-open periods — weekly, fortnightly or monthly — computed in India Standard
  Time, so consecutive periods neither overlap nor leave a gap. A cycle sweeps **every unassigned
  entry older than its end**, not only those inside it, so an entry posted late lands in exactly one
  cycle rather than in a frozen one. The opening balance is the signed sum over entries already
  assigned to a cycle, never the previous cycle's net payable — which would forget a failed payout.
  Closing writes the totals down, which is the one place a computed figure becomes a stored one: a
  statement a seller was sent must still read the same next year. `SettlementCycleWorker` closes the
  previous period once its hold expires; the hold defaults to the store's return window.

  **The payout.** A batch built from closed cycles, signed off by somebody who did not build it —
  refused in the handler, in the aggregate **and** by a database `CHECK`, which is more places than
  any other rule in this platform gets — and then sent. Sending is **resumable and deliberately not
  atomic**: each item is saved as soon as the gateway answers, so a process that dies halfway leaves
  four hundred transfers in a known state. A completed transfer posts a `payout` debit and marks its
  cycle paid; a failed one releases the cycle to be paid in a **new** batch, because two attempts on
  one row would leave one row with two outcomes.

  **Two rails behind one interface we own.** `RazorpayRoutePayoutProvider` (`/v1/transfers` to a
  linked account) and `RazorpayXPayoutProvider` (`/v1/payouts` from a funded merchant account, with
  `queue_if_low_balance` deliberately false), keyed by the rail they are and selected by
  `Payouts:Provider` — the arrangement ADR-018 settled on for couriers, applied to money. A
  deployment with neither gets `UnconfiguredPayoutProvider`, which sends nothing and says so.
  `PayoutReconciliationWorker` re-reads in-flight transfers every fifteen minutes and **reports
  rather than repairs** a stuck one: declaring it failed because it is slow would free the cycle to
  be paid twice.

  **The documents.** Vendor statements (opening balance, movements, closing balance — a bank
  statement, because that is what a seller already knows how to read), the same as CSV, the TCS/TDS
  extract with both bases published beside their tax and its CSV for the GSTR-8 and 26Q/27EQ
  filings, and a platform-revenue report that reconciles by construction: what it reports as revenue
  is exactly the sum of the charge entries on the sellers' ledgers.

  ### Files and folders

  `src/backend/modules/KlaraHome.Modules.Settlements/` — `Domain/` (`LedgerEntry`,
  `SettlementCycle`, `PayoutBatch` + `PayoutItem`, `PayoutLifecycle`, `NumberSequence`),
  `Application/` (`SettlementsErrors`, `SettlementsContracts`, `Ledger/`, `Cycles/`, `Payouts/`,
  `Reports/`), `Endpoints/` (`SettlementsPermissions`, `AdminSettlementEndpoints`),
  `Infrastructure/` (`Accounting/` — `SettlementCalculator`, `StatutoryDeductions`, `CyclePlanner`,
  `SettlementPoster`, `SettlementCycleService`; `Payouts/` — `IPayoutProvider`,
  `UnconfiguredPayoutProvider` + registry, `PayoutWorkflow`, `Razorpay/`; `Events/`, `Jobs/`,
  `Numbering/`, `Reporting/Csv`, `Persistence/`), `SettlementsModule.cs`.

  **18 endpoints** under `/admin`, four permissions (`settlements.settlement.read|manage`,
  `settlements.payout.manage|approve`), one migration (`InitialSettlementsSchema`), one settings
  section (`settlements`) with its validator, and three integration events.

  **Two new shared contracts.** `IOrderSettlement` (implemented by
  `Orders/Infrastructure/Settlements/OrderSettlementService`) is the fourth of ordering's outward
  seams and the only one that **cannot write** — a settlement run must not be able to change the
  sale it is settling. `IVendorPayouts` (implemented by
  `Vendors/Infrastructure/Directory/VendorPayoutDirectory`) answers where a seller's money goes and
  whether it can go anywhere, and carries **no account number**: the last four digits and the IFSC
  are on it, the plaintext never crosses the boundary.

  ### Tests written during the step

  Thirty-four, all under build-sprint rule 1 — the cheapest way to get an algorithm right while
  writing it. `StatutoryDeductionTests` (the two bases, section 206AA, the annual threshold, a
  negative period, rounding), `CyclePlannerTests` (Indian midnight, tiling across all three
  frequencies, the hold), `PayoutLifecycleTests` (the transition table, self-approval, derived
  outcomes) and `SettlementCalculatorTests` (earning, pro-rating, the commission fallback, clamped
  reversals). Everything else is Step 29's and is in `TEST_DEBT.md`.

  ### Deviations from the card

  1. **Entry types expanded beyond the ten §4.12 listed.** `platform_tax` and `platform_fee` were
     added, and `commission_tax` was renamed to `platform_tax` before it shipped. §4.12 named a
     "platform fee" in the deliverables and did not have a type for it, and the GST on the
     platform's own charges had nowhere to live. §4.12 is rewritten to match.
  2. **`taxable_value` added to `ledger_entries`.** The two statutory deductions read from different
     bases and neither can be derived from the other without a GST rate that may since have changed.
  3. **`source_key` added to `ledger_entries`.** Idempotent posting needs a unique key derived from
     the fact; without it a redelivered event credits a seller twice.
  4. **`opening_balance`, `taxable_sales`, `taxable_refunds`, `total_adjustments`, `total_payouts`
     and `entry_count` added to `settlement_cycles`**, so the movements on a statement add up to its
     own net figure — which is the first thing a seller checks.
  5. **A `Cancelled` state added to the payout machine.** §5.3 listed six; a batch built in error and
     never sent has to be abandonable, and its cycles have to become payable again.
  6. **`ChargeShippingToVendor` defaults to on.** The seller is credited the whole amount the shopper
     paid and the freight is charged back, which is the default arrangement where the platform books
     the courier. A store whose sellers ship on their own account turns it off.

  ### Known gaps

  - **Nothing is proved against a live gateway.** `Payouts:Provider` is blank by the User's standing
    instruction, so neither adapter has ever made a call. The send, the outcome mapping, the
    idempotency header and the reconciliation re-read are all unexercised.
  - **`IVendorPayoutAccounts` is still unimplemented**, so no seller has a `gateway_account_id` and a
    Route payout would record every item as `Skipped`. Step 9 left the seam for this step; it is
    named in the Parking Lot rather than quietly skipped.
  - **`transfer.processed` / `transfer.failed` webhooks are not received.** Outcomes are learnt by
    the fifteen-minute sweep alone.
  - **The platform's own commission invoice is not rendered.** The numbers exist; the PDF a seller
    claims input credit against does not.
  - **Nothing consumes the three `Settlements.*` events.** No seller is told their statement is ready
    or that they have been paid.
  - **The nightly balance-assertion job §4.12 names is not written.** It is Step 29's, and it is the
    headline row of this step's test debt.

