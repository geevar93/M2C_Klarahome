# Step 14 — Ordering module & order state machine

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** C · **Depends on:** Step 13
- **Objective:** The order aggregate and its lifecycle, split correctly for a marketplace.
- **Deliverables:**
  - Order → **Sub-order per vendor** → Order lines; immutable pricing/tax snapshot at
    placement time; captured customer + address snapshot.
  - Order state machine (Pending Payment → Confirmed → Processing → Packed → Shipped →
    Out for Delivery → Delivered → Completed; plus Cancelled/Failed/Returned branches) with
    explicit allowed transitions and per-role transition permissions.
  - Cancellation rules (customer-initiated pre-dispatch, vendor-initiated, platform-initiated),
    partial cancellation, stock release.
  - Order timeline/event history; internal notes.
  - GST-compliant invoice generation per sub-order (per-vendor invoice series, IRN-ready).
  - Order search/list APIs for customer, vendor and platform scopes.
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
- **Full acceptance criteria (verified at Step 29, not now):** A two-vendor order splits into two sub-orders that transition
  independently; invalid transitions are rejected; invoice numbers are gapless per vendor per FY.
- **Outcome / Notes:**

  **Status: ✅ DONE — 2026-09-06.** Build acceptance met: everything under Deliverables is written,
  `dotnet build src/backend/KlaraHome.sln` succeeds with no errors and no warnings, the module is
  referenced by all three hosts, the migration is generated and the migrator project compiles.
  644 unit tests green (611 before), 14 architecture tests green.

  ### What was built

  `src/backend/modules/KlaraHome.Modules.Orders/` — schema `orders`, module `Order = 100`.

  **Domain** (`Domain/`)
  - `OrderLifecycle.cs` — the state machine. `SubOrderStatus` (16 states), `OrderActor`
    (Customer/Vendor/Platform/System as `[Flags]`), and `SubOrderLifecycle` — **one table** that
    answers both "does this edge exist" and "may this actor take it". Nothing in the module moves a
    sub-order any other way. Alongside it `OrderStatusRules.Derive`, which computes the parent
    order's status from its parts exactly as `02-domain-model.md` §5.2 specifies.
  - `Order.cs` — the root: customer and address snapshots as `jsonb`, the frozen totals, the derived
    status, `Rederive()` and the timeline. `cart_id` is kept because it is the stock-reservation
    reference.
  - `SubOrder.cs` — one per seller: the frozen seller identity for the invoice, the delivery promise
    as the checkout quoted it, the lifecycle timestamps and the cancellation record. `TransitionTo`
    is the only method that assigns `Status`.
  - `OrderLine.cs` — the frozen line: price, tax split, commission and its plan, plus
    `Cancel`/`RecordReturn` and the pro-rata `CancelledValue`.
  - `OrderEvent.cs` — the append-only timeline. Internal notes live here as `type = 'note'` with
    `is_customer_visible = false`.
  - `Invoice.cs` — one per sub-order, IRN- and QR-ready.
  - `NumberSequence.cs` — the gapless counter, plus `FinancialYear` (April–March, computed in IST).

  **Infrastructure** (`Infrastructure/`)
  - `Numbering/OrderNumbering.cs` — `SELECT … FOR UPDATE` on a counter row, deliberately not a
    Postgres sequence: `nextval` is not transactional and a hole in a GST invoice series is an audit
    finding. Order numbers are `KH-2609-000184`; invoice numbers are `{VENDOR}/{FY}/{00001}`.
  - `Placement/OrderPlacementService.cs` — implements `IOrderPlacement`, the seam Cart declared at
    Step 13. Reads everything first, opens a transaction, allocates the number, writes the graph,
    spends the coupon and the store credit, then either confirms COD and commits the stock or asks
    Payments to open a collection. Every failure rolls the order back **and** reverses the
    redemption and the credit.
  - `Lifecycle/SubOrderWorkflow.cs` — the one place a sub-order moves, cancels or gets invoiced, so
    the storefront's cancel and the admin's cancel cannot drift apart.
  - `Invoicing/` — `InvoiceService` (number, tax split summed off the frozen lines, render, record)
    and `InvoiceDocumentBuilder` (rule 46 content, CGST+SGST or IGST columns by supply type).
  - `Events/OrdersEventPublisher.cs` — six integration events through the keyed per-context outbox.
  - `Payments/UnavailablePaymentInitiation.cs` — the polite refusal until Step 15.
  - `Jobs/OrderLifecycleSweeper.cs` — closes the two transitions time takes: `Delivered` →
    `Completed` when the return window shuts, and an unpaid order past its timeout → `Cancelled`.
  - `Persistence/` — context, design-time factory, configurations and the
    `20260906040750_InitialOrdersSchema` migration: six tables, `CHECK` constraints listing every
    state, and the indexes `03-database-design.md` §9 names.

  **Application / Endpoints** — 16 routes. Storefront: list, get, timeline, cancel order, cancel
  sub-order (whole or partial), list invoices, download invoice. Admin/vendor: list orders, get
  order, list order invoices, add note, list sub-orders (the fulfilment worklist, with an overdue
  filter), transition, cancel, issue invoice, download invoice. Four permissions —
  `orders.order.read`, `.transition`, `.cancel` and `orders.invoice.manage` — mirrored into
  `PermissionCatalog` and granted to Support (read), Operations (all four), Finance (read +
  invoice), Vendor owner (all four) and Vendor staff (read + transition).

  **Contracts** — `Contracts/Orders/OrderEvents.cs` (`OrderPlaced`, `SubOrderConfirmed`,
  `SubOrderCancelled`, `SubOrderStatusChanged`, `InvoiceIssued`, `OrderCompleted`) and
  `Contracts/Payments/IPaymentInitiation.cs`, the seam Step 15 fills.

  ### Decisions worth recording

  1. **The order's status is derived, never set.** Stored so a list can filter on it, recomputed
     from the sub-orders on every write so the two cannot disagree. An order split between a
     cancelled part and a completed part counts as **completed** — the shopper received something,
     and filing it under "cancelled" would hide it from their own order history.
  2. **Totals are frozen; cancellation is counted, not subtracted.** `quantity_cancelled` on the
     line is the record and `NetTotal` is derived. The cancelled value is a pro-rata share of the
     *discounted* line total, not `quantity × unit price`, which would over-refund every line that
     carried an allocated order discount.
  3. **The customer's cancellation right ends at `Packed`**, per §5.1. After dispatch only
     Operations may cancel, and only with a reason.
  4. **A partial cancellation does not move the sub-order.** It is still being packed and still
     being shipped; the line already says which units are gone.
  5. **Invoices are raised at `Packed`**, not at `Confirmed` — the invoice travels with the goods,
     and invoicing earlier means a credit note for every seller who later cannot fulfil.
     Configurable, and an operator can raise one by hand.
  6. **The gapless series is a locked counter row**, not a sequence, and its scope is per seller per
     financial year so the lock is per seller rather than platform-wide.
  7. **The stock commit sits inside the placement transaction.** Losing units from supply is
     correctable by an operator; releasing units for an order that did save is an oversell.
  8. **Nothing is re-priced.** Every figure is copied from the quote Cart handed over.

  ### Known gaps and technical debt

  - **`Orders.SubOrderCancelled` has no consumer**, so units already committed out of stock are not
    put back. A pre-confirmation cancellation releases the cart-scoped reservation directly and is
    complete; the post-confirmation restock needs an Inventory handler, which
    `02-domain-model.md` §6 assigns there. **The most important open item.**
  - **Prepaid placement answers `503 PAYMENTS_UNAVAILABLE`** until Step 15. Cash on delivery works
    end to end today.
  - **`QuoteRequest.IsFirstOrder` is still always false** — Step 13 parked it for this step and it
    is not delivered; it needs a small order-history contract.
  - **Loyalty accrual on completion is not written** — assigned to Orders by `PricingSettings` but
    not named in this step's deliverables, so it was parked rather than built.
  - `orders.payment_status` is a mirror with nothing writing to it until Step 15.
  - `Invoice.Cancel()` exists and nothing calls it; the credit note is Step 17's.
  - Invoice rendering needs the `en-IN` culture — **Step 32 must not build with
    `InvariantGlobalization`**.
  - Twenty rows added to [`../TEST_DEBT.md`](../TEST_DEBT.md); sixteen to
    [`../PARKING_LOT.md`](../PARKING_LOT.md).

  ### Specification changes

  `03-database-design.md` §4.8 was rewritten to the schema actually built, and
  `04-api-specification.md` gained a §3.4 for the storefront order routes it did not list. The
  substantive change is that **`order_number_sequences` and `invoice_number_sequences` are one
  `number_sequences` table with a `kind` discriminator**. Both documents were amended *after*
  implementation rather than before, contrary to protocol rule 9 — the amendments are descriptive,
  and they are logged in [`../CHANGE_LOG.md`](../CHANGE_LOG.md) as **Pending** the User's
  confirmation.
