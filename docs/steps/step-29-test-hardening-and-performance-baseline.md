# Step 29 — Test hardening & performance baseline

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **This step pays the MVP build sprint's bill.** Steps 9–28 wrote production code and deferred
> their verification here; Step 28A made it compile and boot. This step makes it *provably*
> correct. Start by rendering [`../TEST_DEBT.md`](../TEST_DEBT.md) — that is the worklist.

- **Phase:** H · **Depends on:** Step 28A
- **Objective:** Prove the system behaves under real conditions before it is dressed up.

- **Deliverables:**

  **Part 1 — the deferred test debt (do this first).**
  - Every row of [`../TEST_DEBT.md`](../TEST_DEBT.md) closed by a named, passing test, worked in
    risk order: 🔴 (money, stock, auth, data loss) before 🟡 before 🟢.
  - Every **Full acceptance criteria** line from Steps 9–28 demonstrably met. Those criteria were
    moved here, not waived. Work step by step, open one card at a time, and check its criteria off
    against a real test.
  - Defects found while closing debt are **fixed here** — this step owns the correctness pass, so
    the no-scope-creep rule yields to the acceptance criteria it is proving.

  **Part 2 — coverage and suites.**
  - Coverage gaps closed: unit tests on domain/pricing/state machines; integration tests on
    every module with Testcontainers; contract tests on the OpenAPI surface.
  - Architecture tests extended to every module added during the sprint (no module references
    another module's internals; no framework leakage into domain assemblies).
  - End-to-end Playwright suites for the critical journeys (browse→buy, COD, return, vendor
    dispatch, admin order ops).
  - **CI gates restored**: per-suite test-count floors raised to the real counts and
    `-CoverageMinimum` back to the agreed **70 %** from `09-nfr-testing-observability.md` §1.4,
    committed in `tools/ci.ps1`. A full `pwsh tools/ci.ps1 all` passes with no flags.

  **Part 3 — the NFR baseline.**
  - Load and soak testing (k6) against target concurrency; database index review from real
    query plans; N+1 elimination.
  - Security testing: OWASP ASVS checklist pass, dependency and container scanning, authz
    matrix test (every endpoint × every role), rate-limit verification.
  - Accessibility audit (WCAG 2.2 AA) on storefront critical paths.

- **Acceptance criteria:**
  1. **No 🔴 row is open in [`../TEST_DEBT.md`](../TEST_DEBT.md)**, and every remaining open row
     has been moved to [`../PARKING_LOT.md`](../PARKING_LOT.md) with a named blocker and the
     User's agreement.
  2. Every Step 9–28 full acceptance criterion is met and evidenced by a named test.
  3. `pwsh tools/ci.ps1 all` passes with the committed gates — no `-SkipIntegrationTests`, no
     lowered coverage minimum.
  4. All NFR targets in `09-nfr-testing-observability.md` are met and evidenced; no critical or
     high vulnerabilities open.

- **Outcome / Notes:** _(in progress — filled as each part lands)_

### Progress log

#### 29.1 — The integration harness (done)

Nothing in Part 1 was reachable without this. `KlaraHomeSchemaFixture` composed **four** module
assemblies, so the shared test database carried Platform, Identity, Media and Notifications and
nothing else: not one of the 345 integration rows could have been written against it. It now
composes all eighteen, and the collection's database is the database the product runs on.

Around it, in `tests/KlaraHome.IntegrationTests/Commerce/`:

| File | What it is |
|---|---|
| `CommerceApiFactory` | The real API host — all eighteen modules — over the migrated database, with **only** the four network boundaries replaced: object storage, the channel senders, the payment gateway and the courier. Everything that decides whether a seller may be activated, stock reserved, an order transitioned or a refund approved is the production implementation. Background processors are off by default, because a test that races a timer fails one run in twenty for a reason nobody can reproduce. |
| `FakePaymentProvider` | Razorpay at the boundary. Remembers the collections it opened and the payments made against them, so the recovery paths have something truthful to find; signs webhooks with a real HMAC, so a *wrong* signature can be proved to be refused. |
| `FakeShippingProvider` | The aggregator at the boundary, under the name `Shipping:Provider` selects. Serviceability is a set the test controls, so `PINCODE_NOT_SERVICEABLE` stays provable and stays distinct from the coverage refusal beside it. |
| `CommerceTestBase` | Sign-in as administrator, as a shopper (real OTP flow), or as a vendor owner (real password + TOTP); a single deterministic pass of a background processor; a second host over the same database, for key rotation. |
| `VendorScenario` | "An active seller exists", built by driving apply → KYC → bank → pickup → submit → approve → activate through the API. Not a row-writer: a catalogue test standing on hand-written rows would be standing on a seller the product could never have created. |
| `Rest` / `Sql` | Response reading that keeps the problem document in the assertion message, and direct SQL for the handful of criteria that are explicitly about what is *in* the database — ciphertext at rest, an outbox row, a partial unique index. |

#### 29.2 — Step 9 (done): 15 rows closed by 35 tests

`VendorOnboardingTests` (14), `VendorAuthorisationTests` (6), `VendorConstraintTests` (15).
Every Step 9 row in `TEST_DEBT.md` now names a passing test. `SchemaMigrationTests` (4) is
cross-cutting: it migrates every registered module, and asserts each schema exists, that a re-run
applies nothing, that re-seeding changes nothing, and that no migration history table lives outside
a module schema.

**Three real defects were found and fixed** — this step owns the correctness pass, so the
no-scope-creep rule yields to the criteria being proved:

1. **PAN and GSTIN validation had never run on any request.** `VendorIdentifierRules` put all of its
   logic in an override of `Validate()`, and `AbstractValidator.Include()` merges another
   validator's *rules* — a validator with no `RuleFor` contributes nothing, so the override was dead
   code on both `CreateVendorCommand` and `UpdateVendorBusinessCommand`. The format checks, the
   GSTIN state-code check and the GSTIN↔PAN consistency check were all inert. A seven-character PAN
   reached the database and came back as a 500 from `ck_vendors_pan`; a ten-character non-PAN like
   `12345ABCDE` was stored happily. Rewritten as a `Custom` rule on the command, which `Include`
   does pick up.
2. **A cross-vendor read answered 422, not 404.** `VendorScope.Resolve` returned
   `VendorErrors.OutOfScope`, which was built with `Error.Validation`. The class's own
   documentation already claimed 404, citing `07-security-compliance.md` §2 — the code simply did
   not do it, and the difference between "422" and "404" is itself the existence disclosure §2
   rules out.
3. **A platform-only operation answered 422, not 403.** The same error was doing two different
   jobs. Seven handlers refuse an operation outright — a seller creating a seller, approving
   themselves, verifying their own KYC or bank account, choosing their own commission plan,
   creating or editing a commission plan — and that is a permission refusal about an operation, not
   a scope refusal about a resource. Split into `VendorErrors.PlatformOnly` (403), leaving
   `NotFound` (404) for scope.

The 403/404 split surfaced a contradiction **inside the specification**: `04-api-specification.md`
§1's status table says `403 … (incl. cross-vendor access attempts)`, while §2 of the same document
and `07-security-compliance.md` §2 both say 404. Implemented per the two that agree; parked for the
User's decision on striking the parenthetical (protocol rule 9 — a specification change needs
agreement first).

**Suite state:** 941 unit, 14 architecture, **232 integration** (was 193), all green; solution
builds at 0 warnings.

#### 29.3 — Step 10 (done): 15 rows closed by 22 tests

`CatalogPublishingTests` (5), `CatalogAuthorisationTests` (4), `CatalogConstraintTests` (8),
`CatalogImportTests` (5), around a `CatalogScenario` that builds a taxonomy, a product and its
offers through the API a merchandiser would use, and an `OutboxDrain` that runs the real
`OutboxDispatcher` over the host under test — the API never dispatches its own outbox, so a test
that needs an integration event to reach its handler has to supply the worker's half. Five unit
tests on `CatalogJob` cover the arithmetic that needs no engine.

Every Step 10 row in `TEST_DEBT.md` now names a passing test. The step's own acceptance criterion is
proved end to end: a seller drafts a product with two variants, attributes and galleries, submits
it, the platform approves it, two sellers offer against the same variant, and the storefront serves
the page with the cheaper offer in the buy box. The other — "bulk import of 1,000 SKUs validates and
loads" — runs a real thousand-row file through the real worker and reads the rows back.

**Six real defects were found and fixed.** Two of them were security-shaped, two were data loss, and
two were documented behaviour that had never worked:

1. **A seller could not see the platform's shared products at all.** The module is built around the
   opposite: `products.vendor_id` is nullable so the platform can publish a product several sellers
   offer against, `CatalogScope.CanWrite` exists solely to refuse the *write* to a row a seller can
   *read*, and the Step 10 card, the context, the endpoints and the debt row all say so. The generic
   vendor query filter says something else — "a row with no vendor belongs to the platform, and a
   vendor user has no business seeing it either" — and it wins, because the conventions are applied
   after a module has configured its model. `CanWrite`'s platform branch was dead code and the
   shared catalogue did not exist. Fixed with a new `IPlatformShared` marker: an opt-in, per table,
   that widens the *read* only, declared by `Product` and by nothing else. Widening the convention
   itself would have exposed platform warehouses, price lists, rate cards and staff role assignments
   to every seller on the marketplace.
2. **A competitor's variant was an existence oracle.** A variant row is not vendor-scoped — the
   catalogue is shared, so the filter is on the product — and three handlers collapsed "no such
   product" and "not yours to write" into one 422. An invented variant id answered 404 and a real
   one belonging to a competitor answered 422, which is precisely the disclosure
   `07-security-compliance.md` §2 rules out. Split: 404 when the product is invisible, the scope
   error when it is visible and platform-owned.
3. **A seller's import could have rewritten the platform's shared product.** Not reachable before
   (1), and reachable the moment it landed: an import is the one write path that does not go through
   a handler, so it does not get `CanWrite` for free. A seller uploading a file whose slug the
   platform already owns would have silently rewritten the platform's copy for every other seller.
4. **A refused row lost the whole upload.** Recovering from a row the database rejected cleared the
   change tracker, which detached the *job* along with the failed entity: every later count and the
   completion itself were applied to an entity nothing was tracking, the dispatcher's final save
   wrote nothing, and the job stayed `Running` for ever — no report, and never re-claimed, because
   the poller only looks at `Queued`. One bad row in a thousand was enough. The job is re-attached
   now, exactly as the dispatcher's own catch already did.
5. **A two-column price file was rejected.** The card's "a blank cell means leave what is there, so
   a two-column price file cannot wipe a catalogue's descriptions" was true of blank *cells* and
   false of the file: the product was resolved by slug only, so `sku,mrp` named nothing and every
   row was refused for want of a product name. It now falls back to the SKU's own variant.
6. **The import cannot create a multi-variant product**, and never could — the template has no
   attribute columns, so every variant it creates carries the same "no options" hash and the unique
   index refuses the second row for a slug. The card claimed otherwise. Refused now with a message
   a merchandiser can act on rather than a constraint name; the card is corrected and attribute
   columns are parked.

Two smaller notes. `GET /admin/attributes` and `GET /admin/brands` declare `bool` where every other
module declares `bool?`, so three filters are *required* query parameters and the endpoints answer
400 without them; nothing is broken because the generated client always sends them, and the fix is
parked rather than opening a codegen front inside this step. And the query-count half of the
`IProductCatalog.FindListingsAsync` row is asserted as behaviour rather than as a measurement —
nothing in this host counts commands, and the implementation is two set-based reads whatever the
batch size.

**Suite state:** 946 unit, 14 architecture, **254 integration** (was 232), all green; solution builds
at 0 warnings.
