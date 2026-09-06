# Step 17 — Returns, Refunds & RMA module

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** D · **Depends on:** Step 16
- **Objective:** The post-delivery lifecycle, which for Indian marketplaces is a first-class flow.
- **Deliverables:**
  - Return/replacement request with reason codes, evidence images, eligibility window from
    policy configuration (per category / vendor).
  - RMA state machine (Requested → Approved/Rejected → Pickup Scheduled → Picked → In Transit
    → Received → QC Passed/Failed → Refunded/Replaced/Closed).
  - Reverse pickup via logistics provider; QC disposition and restock-or-scrap decision.
  - Refund orchestration: original payment method via Razorpay, or store credit; partial
    refunds including proportional tax and shipping treatment; credit note generation.
  - Customer-cancelled-after-dispatch (RTO) handling.
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
- **Full acceptance criteria (verified at Step 29, not now):** A delivered line can be returned, picked up, QC'd and refunded with
  a correct credit note and stock adjustment.
- **Outcome / Notes:**

  **Closed 2026-09-06.** The post-delivery lifecycle is built end to end: a shopper can ask, a queue
  can decide, a courier can collect, a warehouse can grade, and the money and the paperwork both
  follow. `dotnet build src/backend/KlaraHome.sln` is clean — **0 errors, 0 warnings** — 722 unit
  tests and all 14 architecture tests pass, and the `returns` migration generates.

  ### What was built

  **`KlaraHome.Modules.Returns`** (schema `returns`, module order 130 — after Shipping, before
  Settlements):

  - **Domain.** `ReturnRequest` (aggregate) with `ReturnLine`; `ReturnReason` carrying its own
    policy; `CreditNote`; and a module-local `NumberSequence` + `FinancialYear`.
  - **`ReturnLifecycle`** — the transition table as *data*, saying both which edges exist and which
    actor may take each. Thirteen states, and `NextFor(status, actor)` is projected onto every
    response so an admin screen never draws a button for an edge that does not exist.
  - **`ReturnWorkflow`** — the single place a return moves. Every route (endpoint, courier scan,
    sweep) goes through it, and each transition also writes the *order's* timeline and, for the
    three states that mean something to a sale, advances the sub-order.
  - **`ReturnPolicyService`** — the three-policy eligibility resolution (product → vendor → store,
    first opinion wins), who pays the reverse freight, what is auto-approved, and the default
    disposition.
  - **`ReturnRefundCalculator`** — the only arithmetic in the module, and deliberately arithmetic on
    frozen numbers rather than a call to the pricing engine.
  - **`ReturnInspectionService`** — records QC, tells the sale which units came back, then moves the
    stock, in that order.
  - **`ReversePickupCoordinator`**, **`ReturnRefundService`**, **`CreditNoteService`** +
    `CreditNoteDocumentBuilder` (rule 53 particulars), **`StaleReturnWorker`**,
    **`ShippingLifecycleHandlers`** (courier scans + return-to-origin restock),
    **`ReturnReasonSeeder`** (eight reason codes with the policy an Indian consumer forum expects).
  - **21 endpoints** — 7 storefront, 14 admin — behind five permissions.
  - **One migration**, `InitialReturnsSchema`: five tables, every `CHECK` constraint from the design.

  ### Four new shared seams

  Declared in `KlaraHome.Contracts` and implemented in the module that owns the data, which is the
  arrangement Step 16 established:

  | Seam | Implemented in | Why it points this way |
  |---|---|---|
  | `IOrderReturns` | Orders (`OrderReturnsService`) | The sale is the authority on what a line is worth and how much is left. Deliberately narrower than the other ordering seams: no price may be changed and nothing may be cancelled through it |
  | `IRefundInitiation` | Payments (`RefundInitiationService`) | The maker–checker threshold, the idempotency index and the gateway conversation all live there. It resolves the collection itself, so a returns queue never learns what an order was paid with |
  | `IReversePickup` | Shipping (`ReversePickupService`) | A reverse pickup is an ordinary `Shipment` with `is_return` and its two addresses inverted — same adapters, same webhooks, same tracking |
  | `IStockRestock` | Inventory (`StockRestockService`) | `stock_ledger_entries` is append-only and has exactly one writer |

  Plus `IVendorDirectory.ReturnPolicyAsync`, reading the seller's existing `return_policy` document
  (no schema change — the column has existed since Step 9), and a `ReturnsSettings` platform
  settings section with its validator.

  ### The decisions worth knowing

  1. **Three amounts on a return, not one.** `estimated_refund` (what the shopper was quoted),
     `approved_amount` (what somebody agreed to), `refund_amount` (what actually went back). Only
     the last is money. Collapsing them makes "why did I get less than the screen said"
     unanswerable.
  2. **The tax credited is the tax that was charged**, apportioned from the frozen order line and
     never recomputed. A rate that moved between sale and return would put the credit note out of
     agreement with the invoice it credits.
  3. **The credit note is raised before the money is asked for, and whether or not money moves.**
     Section 34 of the CGST Act is about the supply, not the card: a refund to store credit still
     reverses it. A note against a refund that then failed is recoverable; a refund against a supply
     nobody reversed leaves the seller owing tax on goods they no longer have.
  4. **`Received` and `QcPassed` are two states.** The parcel arriving and somebody opening it are
     different days and different people, and collapsing them would lose the number a returns
     operation is run on — how much is uninspected in the receiving bay.
  5. **A vendor may never grade their own return.** `returns.qc.manage` is separate from
     `returns.return.manage` for exactly that reason, and `ReturnsScope.Actor` resolves a caller
     carrying a vendor id as a vendor *even when they also hold a platform permission*.
  6. **This module calculates; it never sends money or moves stock itself.** Both go through the
     owning module's own controls.

  ### Deviations from the card

  - **The specification was extended while implementing** (`CHANGE_LOG.md`, eleventh entry). The
    original spec had **no storefront returns surface at all** — §3.4a is new — and §4 named four
    admin routes where the workflow needs twelve. `02` §6 and `03` §4.11 were expanded to match what
    was built. All additive; nothing was weakened.
  - **A replacement is recorded, not placed.** `POST /admin/returns/{id}/replace` takes an order id
    an operator supplies. Placing an order from inside Returns would mean a returns queue that can
    create sales. Parked for Step 27/28.

  ### Known gaps

  - **Nothing here has been proved against a database or a live courier or gateway.** 21 rows in
    [`TEST_DEBT.md`](../TEST_DEBT.md), and the step's full acceptance criterion is the first of them.
  - **Notifications has no templates for any of the seven `Returns.*` events** — the same gap
    `Shipping.*` had at Step 16. A shopper whose return was approved is told only by the order
    timeline.
  - **`FinancialYear` and the gapless counter are now written twice**, in Orders and in Returns. The
    module boundary's price; the two must agree exactly, and Step 29 should assert it.
  - **A scrapped line writes a zero-quantity ledger entry.** Correct arithmetically — the units
    never went back on supply — but scrapped-on-return is not a figure any report can reach today.
  - **The stale-return sweep reports and never repairs**, into the log rather than into an inbox.
    Step 31 should turn its three warnings into alerts.
