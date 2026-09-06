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
- **Outcome / Notes:** _(to be filled on completion)_
