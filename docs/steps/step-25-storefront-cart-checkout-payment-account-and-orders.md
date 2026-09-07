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
- **Outcome / Notes:**

  **Status: complete, with one named deliverable not built and raised — guest checkout (below).**

  ### What was built

  **The buying journey.** `/cart` groups the basket by seller because that is what it becomes — a
  cart with three sellers is three sub-orders, three parcels and three dispatch promises, and a flat
  list would make the three delivery charges on the summary look like an error. Quantity, removal,
  save-for-later, the coupon box and the itemised price panel are all there, and **every change is
  optimistic with the API's reason on refusal**: `CartStore` moves the line, the request goes out,
  and a rejection restores *exactly* what was there before — not a recomputed version of it, which
  would lose a second change the shopper made while the first was in flight. Four `+` taps in a
  second are four increments; a control that locked after the first would drop three of them.

  **Checkout is four steps over one server-side session**, and three decisions carry it. The **step
  is in the URL** (`?step=payment`), because a four-screen flow whose position lives in a component
  field is a flow where the back gesture leaves the shop. The **session id is in `sessionStorage`**,
  so a reload or a bounce out to the payment gateway resumes the same checkout rather than opening a
  new one. And the **furthest reachable step is derived from the session, never from history**: a
  bookmarked `?step=review` on a session with no address renders the address step, because the
  server would refuse anything else.

  **The order is placed before any money is asked for**, which is what makes the payment resilient.
  Dismissing the Razorpay modal leaves a placed, unpaid order — not a lost purchase — and the
  confirmation page and the order page both offer to retry it, against the same order and a fresh
  provider order. **No card data touches this application**: Razorpay's own script renders its own
  form on its own origin, loaded on demand and once, and what comes back is a signature that is
  posted to the API for verification. **The gateway's callback is not the truth; the API's verdict
  is** — and the webhook says the same thing independently, which is why a customer who closed the
  tab mid-payment still ends up with a paid order.

  **The account area** is a layout route with nine pages under it: a dashboard that answers "what is
  happening with my orders", the orders list (keyset-paged, filtered server-side), the order detail
  with its per-sub-order parcels, timeline, invoices, cancellation and return request, returns and
  one return, the address book, the profile, the wishlist, store credit and notification
  preferences. **Every action offered comes from the API's own answer** — cancellation because
  `isCancellable` said so, a return because the eligibility endpoint named the lines and the window,
  a withdrawal because the return's `nextStatuses` contains `Cancelled`. The transition tables are
  data (Steps 14 and 17), and reading them is what stops the buttons and the server's rules drifting
  apart when a status is added.

  **Auth is mobile-number-first.** Most customers of an Indian storefront have a number and no
  password, and the API creates the account on the first successful code — so for them there is no
  sign-up, only the sign-in screen. The password form is behind a link.
  `autocomplete="one-time-code"` on the code box is the single most valuable attribute on those
  screens. Nothing anywhere says whether an address or a number is registered.

  ### Files

  - **New library `data-access-checkout`** — `CheckoutStore` (the session and its four writes) and
    `PaymentHandoff` (the Razorpay script loader, the modal, the server-side verification and the
    retry).
  - **New library `data-access-account`** — `AddressBookStore`, `ProfileStore`, `WalletService`,
    `NotificationPreferencesStore`.
  - **`data-access-orders` filled in** — `OrdersService` (list, get, timeline, cancel, invoices) and
    `ReturnsService` (eligibility, raise, list, get, cancel). Its generator stub was deleted, as was
    the one still left in `data-access-cart` and `data-access-content`.
  - **`data-access-cart`** — `CartStore` beside the existing read-only `CartSummaryStore`.
  - **`data-access-content`** — `ReferenceDataService` (states, PIN-code lookup).
  - **`data-access-auth`** — the sign-in operations on `AuthService`: `requestOtp`, `verifyOtp`,
    `signIn`, `register`, `forgotPassword`, `resetPassword`, `verifyTwoFactor`.
  - **`util`** — `forms.ts`: `formField`, `formGroup` and eight validators.
  - **`ui-primitives`** — `Field` plus the `khControl` directive, `Checkbox`, `Stepper`, a
    `_control.scss` partial, and nine icons.
  - **`ui-patterns`** — `commerce.model.ts` and nine patterns: `CartLine`, `OrderSummary`,
    `AddressCard`, `AddressForm`, `PaymentMethodSelector`, `ShippingOptionSelector`,
    `OrderTimeline`, `OrderCard`, `ReturnRequestForm`.
  - **`storefront`** — `core/commerce.mapper.ts`, `core/sign-in.flow.ts`, `core/describe-error.ts`,
    and eighteen page components under `pages/cart.page.ts`, `pages/checkout/`, `pages/auth/` and
    `pages/account/`. Every Step 25 placeholder route is now a real `loadComponent`.

  ### Deviations from the specification

  1. **Guest checkout was not built, because the API does not offer one.** `/store/checkout` carries
     `.RequireAuthorization()` on the whole endpoint group and `StartCheckoutCommand` refuses an
     unauthenticated caller (`CartsOptions.RequireSignInToCheckout`, default `true`); the endpoint's
     own remarks state the reasoning. Building a guest front end against it would have produced a
     screen full of 401s. What the storefront does instead is the closest honest thing: `/checkout`
     carries `authenticatedGuard`, so an anonymous shopper is taken to sign-in with a `returnUrl`,
     signs in with one OTP, and is returned to the checkout with their anonymous basket merged.
     **Making it real is a backend change** and is raised in `PARKING_LOT.md` for a decision.
  2. **No `@angular/forms`.** Five forms are built on a 90-line signal helper in `util/forms.ts`
     instead. The app is zoneless and signal-based, the forms are short, and `ReactiveFormsModule`
     is ~30 kB plus a `ControlValueAccessor` on every input in an app with 20 kB of budget headroom.
     Recorded in `PARKING_LOT.md`, including the case it does *not* serve: Step 28's schema-driven
     CMS block editor should reopen the decision rather than extend the helper.
  3. **Return evidence cannot be uploaded.** A reason with `requiresEvidence` is honoured — the form
     says photographs are needed — but `evidenceFileIds` is always `null`; there is no media picker
     on any storefront screen yet. Parked.
  4. **A separate billing address is not offered.** The checkout always sends
     `billingAddressId: null`. The GSTIN *is* collected, so a business invoice in the company's name
     works; a distinct billing address is the B2B case, which is Phase 2.
  5. **Password reset lives on one route, not two.** `/auth/forgot-password` renders the request form
     or, when the emailed link comes back with a `token`, the new-password form. The route map lists
     one URL and there is no second one to add.

  ### Known gaps

  - **Nothing is proved against a live gateway.** The Razorpay credentials are deliberately blank
    (Step 15), so the handoff, the verification and the retry are written and unexercised. The COD
    path is complete and provable without them.
  - **A cart line does not link to its product** (`CartLineResponse` has no slug) and the **orders
    list has no thumbnails** (`OrderSummaryResponse` has no images). Both are small backend
    projection gaps, parked alongside Step 24's `brandName` row.
  - Banners and the announcement bar, and asking a question / writing a review, are **still not
    built** and are now carried across three step boundaries each. Both are parked and need an owner.

  ### Verification

  - `npx nx build storefront` and `admin` succeed; **160.5 kB gzipped initial JavaScript against the
    180 kB budget**, largest route chunk 5.9 kB against 80 kB.
  - 21 lint projects and 19 test projects green; `npx prettier --check .` clean.
  - The **built SSR server was booted and every new route requested**: `/cart`, `/checkout`,
    `/account/orders` and `/auth/login` all render 200 with no `NG0600` and no server-side error —
    the same check that caught a real bug at Step 24.
  - **Nothing is proved by a test a machine runs.** 28 `TEST_DEBT.md` rows, both halves of the
    headline criterion among them.
