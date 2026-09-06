# Step 9 — Vendor / Seller module

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** C · **Depends on:** Step 8
- **Objective:** Onboard and govern the sellers that make this a marketplace.
- **Deliverables:**
  - Vendor entity, onboarding workflow with state machine
    (Applied → Under Review → Approved → Active → Suspended → Offboarded).
  - KYC document capture (PAN, GSTIN, bank account, cancelled cheque, address proof) with
    verification status; Razorpay Route linked-account creation hook (completed in Step 18).
  - Vendor staff users and role assignment; vendor storefront profile (name, logo, policies).
  - Commission plan definition (category-wise / flat / tiered) and assignment to vendors.
  - Vendor-level operational settings: dispatch SLA, pickup addresses, return policy,
    serviceable regions.
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
- **Full acceptance criteria (verified at Step 29, not now):** A vendor can be onboarded end-to-end through the API and reaches
  Active; a commission plan resolves correctly for a given vendor + category.
- **Outcome / Notes:**

  **Status: ✅ DONE (2026-09-05).** The `vendors` schema, eight tables, a sequence, 39 endpoints,
  three published contracts and the first integration events this platform raises. Backend builds
  clean under CI-strict analyzers; `dotnet format --verify-no-changes` passes; 449 unit tests and
  14 architecture tests green.

  ### What was built

  **Domain (`Domain/`)**
  - `Vendor` — the aggregate root, with the life cycle as a declared transition table
    (`IsTransitionAllowed`) rather than a chain of `if`s. `TransitionTo` returns `false` for an
    illegal move instead of throwing, because an operator racing another operator is a conflict to
    report, not a programming error. `onboarded_at` is set on the first activation and never moved.
  - `VendorUser`, `VendorKycDocument`, `VendorBankAccount`, `VendorPickupLocation`,
    `VendorServiceableRegion` — all `IVendorScoped`, so the global query filter confines a vendor
    caller in the data layer.
  - `CommissionPlan` + `CommissionPlanRule` — the plan owns `Resolve`, so the rule that decides
    money is a pure function over the plan that a unit test drives without a database.

  **Persistence** — `VendorsDbContext` (first context to take `ICallerContext` for real work: five
  of its tables carry the vendor filter), the design-time factory, one configuration file, and
  `20260905181037_InitialVendorsSchema`. `vendors.vendor_code_seq` backs the generated `VND-000017`
  codes: a sequence rather than a count, because a count is a race, and safe here because the code
  is not an invoice number and a gap costs nothing.

  **Application (`Application/`)** — six feature files: vendors and the life cycle, KYC, banking,
  logistics (pickup locations + serviceability), staff, commission plans. `VendorReadinessService`
  owns which transitions are *earned* — the aggregate owns which *exist* — and reports every
  blocker rather than the first, so an operator working a queue is not sent round twice.

  **Endpoints** — `/admin/vendors` (+ nine sub-resources), `/admin/commission-plans`, and one
  storefront route. One set of URLs for two audiences: platform staff name a seller in the path, a
  vendor owner uses the same routes with their own id and cannot address anybody else's. A seller
  outside scope answers **404, not 403** (`07-security-compliance.md` §2).

  **Published contracts** — `IVendorDirectory` ("may this seller trade") and `ICommissionResolver`
  ("what do we charge them") in `KlaraHome.Contracts.Vendors`, plus `VendorActivated`,
  `VendorSuspended` and `VendorOffboarded`. These are what Catalog, Inventory, Orders and
  Settlements will hold a `vendor_id` and ask.

  **Permissions** — five: `vendors.vendor.read`, `.manage`, `.approve`, `vendors.kyc.verify`,
  `vendors.commission.manage`. `.approve` is deliberately **not** part of `.manage`: a vendor owner
  holds `.manage` so they can edit their own profile, and must not be able to approve themselves.
  Operations gained onboarding, Finance gained the commission plan and the bank-account check,
  Catalog manager and vendor staff gained `.read`.

  ### Deviations and decisions worth knowing

  1. **A keyed `IOutbox` was added to the shared layer.** `AddModuleDbContext` registered `IOutbox`
     with `TryAddScoped`, which is first-wins — so it belonged to the Platform module, and any
     other module publishing through it would have added the row to a *different* context's change
     tracker and lost the event at save time, silently. `VendorActivated` is the first event this
     platform publishes, so this was found rather than shipped. Fixed minimally: a keyed
     registration per context, asked for by name.
  2. **`IFieldProtector` was promoted to `KlaraHome.Infrastructure`** with its own `Encryption`
     configuration section. A bank account number is "Sensitive" under §5, cannot be hashed
     (a payout has to reach it) and cannot be stored in the clear. Identity keeps its own envelope
     under `Auth:Encryption` for the TOTP secret; folding the two together is parked for Step 29/31
     rather than editing Step 7 code mid-sprint.
  3. **The full PAN/GSTIN on a KYC document is never stored** — only the mask. Verification compares
     the scan against what was typed, and nothing afterwards needs the number, so keeping it would
     be collecting a government identifier for no purpose. The seller's own PAN and GSTIN, which
     invoicing genuinely needs, live on `vendors`.
  4. **`vendor_serviceable_regions` did not exist in the design.** Added to `03-database-design.md`
     §4.3 first, then implemented (protocol rule 9). It records the seller's commercial willingness
     to deliver, which is a different question from the courier's reach — that stays Step 16's.
  5. **`ToSlug` uses an explicit fold table, not Unicode decomposition.** `InvariantGlobalization`
     is on, so `Normalize(FormD)` does not decompose and "Café" became `caf-`. Caught by a test.
  6. **Search uses `ILIKE` with escaped wildcards** rather than a lowered `Contains`, so case
     folding happens in Postgres instead of depending on the column's collation.
  7. **Activation never fails because payout provisioning did.** A gateway outage is not a reason to
     refuse a seller permission to trade; they trade and are not yet payable, which is a state
     Settlements has to handle anyway.

  ### Known gaps (all recorded in `../TEST_DEBT.md` and `../PARKING_LOT.md`)

  No integration tests, per the sprint rules — fifteen rows, six of them 🔴, covering the end-to-end
  onboarding, the activation refusals, the outbox transaction, the encryption round-trip and the
  vendor-scope authorisation matrix. Nothing checks a bank account (no penny drop until Step 15/18);
  no Razorpay linked account is created (Step 18); `vendors.rating` is written by nobody until Step
  21; serviceable regions are stored and not yet evaluated (Step 16); `VendorSuspended` has no
  consumer until Catalog and Search exist.
