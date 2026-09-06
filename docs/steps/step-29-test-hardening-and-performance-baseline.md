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

- **Outcome / Notes:** _(to be filled on completion)_
