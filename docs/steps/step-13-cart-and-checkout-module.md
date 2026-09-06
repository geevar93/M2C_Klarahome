# Step 13 — Cart & Checkout module

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** C · **Depends on:** Step 12
- **Objective:** From "add to cart" to a validated, priced, ready-to-pay checkout.
- **Deliverables:**
  - Persistent cart for logged-in users, anonymous cart with merge-on-login, cart expiry.
  - Cart validation (availability, price change, vendor active, serviceability) with clear
    user-facing reasons.
  - Multi-vendor cart handling: grouping by vendor, per-vendor shipping and dispatch SLA.
  - Checkout session: address selection, shipping option selection, coupon application,
    payment method selection (prepaid vs COD), COD eligibility rules and limits.
  - Idempotent "place order" command with reservation of stock.
  - Abandoned-cart capture for later marketing.
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
- **Full acceptance criteria (verified at Step 29, not now):** A multi-vendor cart produces a correct grouped, priced checkout
  summary; duplicate place-order requests create exactly one order.
- **Outcome / Notes:**

  **Status: ✅ DONE (2026-09-06).** The Cart module is built: `src/backend/modules/KlaraHome.Modules.Carts`,
  schema `carts`, five tables, **24 endpoints**, one EF migration
  (`20260906032558_InitialCartsSchema`). `dotnet build src/backend/KlaraHome.sln` succeeds;
  **611 unit tests** green (20 new) and **14 architecture tests** green.

  ### What was built

  | Deliverable | Where it lives |
  |---|---|
  | Persistent cart, anonymous cart, merge on login, expiry | `Domain/Cart.cs`, `Infrastructure/Carts/CartResolver.cs`, `Infrastructure/Carts/CartCookie.cs` |
  | Cart validation with user-facing reasons | `Infrastructure/Carts/CartRenderer.cs`, `Application/Carts/CartContracts.cs` (`CartIssue`, `CartIssueCodes`) |
  | Multi-vendor grouping, per-vendor shipping and dispatch SLA | `CartRenderer.Group`, `Domain/CheckoutSession.cs` (`CheckoutShipment`), `Contracts/Shipping/IShippingOptions.cs` |
  | Checkout session: address, shipping, coupon, payment method, COD rules | `Application/Checkout/CheckoutFeature.cs`, `Infrastructure/Checkout/CheckoutWorkflow.cs` |
  | Idempotent place-order with stock reservation | `Application/Checkout/PlaceOrderFeature.cs`, `Domain/CheckoutSession.cs` (`CheckoutPlacement`) |
  | Abandoned-cart capture | `Infrastructure/Jobs/AbandonedCartSweeper.cs`, `Contracts/Carts/CartEvents.cs` (`CartAbandoned`) |

  ### The decisions worth knowing

  - **A cart holds no stock.** The reservation is taken in `place-order` and nowhere else. Holding
    at add-to-cart would make every browsing shopper a denial of service against every buying one,
    and the hold is against the **cart id** with reference type `cart`, which is what Orders settles
    when the order is confirmed or cancelled.
  - **The anonymous token is stored hashed**, not in the clear — it is a bearer capability, so it is
    treated the way `identity.user_sessions.refresh_token_hash` is. The value lives only in the
    `kh_cart` cookie.
  - **Merge on login happens on the first read**, not only on `POST /store/cart/merge`. A shopper
    who signs in and finds an empty basket does not file a bug, they leave; the explicit endpoint
    exists so a storefront can do it in one call and get the merged basket back.
  - **One renderer serves the cart page, the checkout review and the operator view.** Two would
    eventually disagree about whether an item is in stock, and the disagreement would surface at the
    payment screen.
  - **This module computes no money.** Every figure comes from `IPriceQuoteEngine`; the snapshot
    taken at each checkout step is what Orders will copy onto the order.
  - **Idempotency is a table, not a cache.** `carts.checkout_placements` with a unique index on
    `(tenant_id, idempotency_key)` is the race winner; a key replayed against a different basket is
    a 409, and a key whose attempt *failed* may be reused, because an idempotency key promises *at
    most one* order and a failure created none.

  ### Two seams this step declares and does not fill

  - **`IOrderPlacement`** (`Contracts/Orders`) — Step 14 implements it. Until then the Cart module
    registers `UnavailableOrderPlacement`, which returns `503 ORDERING_UNAVAILABLE`. **`POST
    /store/checkout/{id}/place-order` therefore cannot succeed on this build**, and the whole
    basket-to-order path is untestable end to end until Step 14.
  - **`IShippingOptions`** (`Contracts/Shipping`) — Step 16 implements it. Until then
    `StandardShippingOptions` offers one free "Standard delivery" at the seller's own dispatch SLA
    with a conservative 3-7 day window. Charging zero rather than guessing is the same position
    `QuoteRequest.ShippingAmount` already takes.

  Both are registered with `TryAdd`, so the owning module replaces them without this module being
  touched.

  ### Shared-layer additions (all recorded in the Parking Lot)

  - **`ICustomerDirectory`** — a checkout has an address id and needs an address, and may not join
    to `identity.addresses`. Implemented by `Identity/Infrastructure/Directory/CustomerDirectory.cs`.
    Every address read is keyed on **(customer, address)**, so an id belonging to somebody else
    simply does not resolve.
  - **`IVendorDirectory.IsServiceableAsync`** — "serviceability" is a named Step 13 deliverable and
    the contract had no way to ask. Implemented in `VendorDirectory`; an exclusion rule wins over an
    inclusion wherever both match.

  ### Known gaps at this boundary

  1. **`place-order` cannot complete** — see the seam above.
  2. **The reminder half of abandoned-cart capture is not delivered.** `CartAbandoned` is published
     and `Cart.RecordReminder` exists, but nothing consumes the event yet.
  3. **Store credit cannot be applied at checkout.** `CartRenderContext.WalletRedeemRequested` is
     plumbed through to the quote engine but no endpoint sets it.
  4. **`IsFirstOrder` is always false**, so Pricing's first-order campaigns cannot fire. Only Orders
     can answer that question (Step 14).
  5. **Stock holds and the cart transaction are not one atomic unit** — two contexts. Every failure
     path after the hold calls the compensating release explicitly, and Inventory's sweeper is the
     backstop.

  Twenty-four `TEST_DEBT.md` rows and sixteen Parking Lot entries were recorded.
