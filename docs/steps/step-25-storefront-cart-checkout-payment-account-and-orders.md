# Step 25 — Storefront: cart, checkout, payment, account & orders

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** F · **Depends on:** Step 24
- **Objective:** Conversion and post-purchase self-service.
- **Deliverables:**
  - Cart page/drawer: quantity edit, remove, save for later, coupon entry, vendor grouping,
    itemised price summary, validation messaging.
  - Checkout: mobile-optimised stepper (address → delivery → payment → review), address book
    with PIN-code autofill of city/state, guest checkout, COD option with eligibility messaging,
    Razorpay checkout handoff, failure/retry handling, order confirmation.
  - Account area: profile, addresses, orders list, order detail with live tracking timeline,
    invoice download, cancellation request, return/replacement request, wishlist,
    wallet/credits, notification preferences.
  - Auth screens: OTP login, register, password reset.
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
- **Full acceptance criteria (verified at Step 29, not now):** A complete purchase (prepaid via sandbox, and COD) is possible on
  mobile; the order appears in the account with tracking and a downloadable invoice.
- **Outcome / Notes:** _(to be filled on completion)_
