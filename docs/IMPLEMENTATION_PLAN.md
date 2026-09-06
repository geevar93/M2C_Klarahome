# Klara Home — Master Implementation Plan

> **Document owner:** Solution Architecture
> **Status:** APPROVED — in execution (next: Step 16)
> **Last updated:** 2026-09-06 (Step 15 closed)
> **Applies to:** Klara Home multi-vendor e-commerce platform (India)

---

## 0. How to use this document

This is the **tracker**: execution order, current status, and pointers. It is deliberately small
so it can be read in full at the start of every turn.

**It does not contain the step details.** Each step's objective, deliverables, acceptance criteria
and outcome notes live in its own file under [`steps/`](steps/). The Master Status Table (§2)
links to them.

### 0.1 What to render, and when

| You are about to… | Render |
|---|---|
| Start or continue a step | **This file** + that step's file in `steps/` — nothing else |
| Look up how something must be built | Only the specific `docs/NN-*.md` design doc the step card names |
| Close a step | This file + the step file + [`PARKING_LOT.md`](PARKING_LOT.md) |
| Change a specification document | Also [`CHANGE_LOG.md`](CHANGE_LOG.md) |
| Defer a test (Steps 9–28) | [`TEST_DEBT.md`](TEST_DEBT.md) |
| Answer "what is outstanding?" | [`PARKING_LOT.md`](PARKING_LOT.md) + [`TEST_DEBT.md`](TEST_DEBT.md) |

**Do not render other steps' files.** A completed step's card is a historical record; if you need a
fact from it, the Notes column in §2 carries the one-line summary, and only if that is not enough
do you open the one card you need. Never open `steps/` wholesale.

### 0.2 The other ledgers

| File | Holds | Render when |
|---|---|---|
| [`steps/step-NN-*.md`](steps/) | One step's full card: phase, dependency, objective, deliverables, acceptance criteria, outcome notes | You are working on **that** step |
| [`PARKING_LOT.md`](PARKING_LOT.md) | Out-of-step discoveries, with their decisions | Closing a step; reviewing debt |
| [`CHANGE_LOG.md`](CHANGE_LOG.md) | Every specification change and who approved it | A design doc changes |
| [`TEST_DEBT.md`](TEST_DEBT.md) | Every test knowingly deferred during the build sprint | Deferring a test; Step 29 |

---

## ⛔ MANDATORY EXECUTION PROTOCOL — READ BEFORE EVERY STEP

**These rules are non-negotiable and apply to every human and AI contributor on this project.**

1. **ONE STEP AT A TIME.** Work only on the single step that has been explicitly authorised.
   Never start the next step because it "seems obvious" or "is only a small change".

2. **OPEN THE STEP FILE FIRST.** Before writing any code for Step `N`, render
   `docs/steps/step-NN-*.md` — the row for that step in §2 gives the exact path. That file is the
   contract. This tracker alone is **not** enough to implement from, and never enough to close a
   step.

3. **STOP AT THE END OF EVERY STEP.** When the step's acceptance criteria are met, **halt all
   implementation immediately**. Do not begin, scaffold, stub, or "prepare" any part of a
   later step.

4. **UPDATE BOTH FILES BEFORE ASKING.** On completion of a step you MUST:
   - In **this** file: set the step's **Status** to `✅ DONE`, fill **Completed On**, and write a
     **one-or-two-sentence** Notes summary. Keep it short — detail belongs in the step file.
   - In the **step file**: fill in **Outcome / Notes** — what was built, files/folders created,
     deviations from the spec, known gaps and technical debt. Length is unconstrained there.
   - Add any out-of-step discoveries to [`PARKING_LOT.md`](PARKING_LOT.md).
   - Add every deferred test to [`TEST_DEBT.md`](TEST_DEBT.md).
   - Add an entry to [`CHANGE_LOG.md`](CHANGE_LOG.md) if the spec itself had to change.

5. **THEN ASK FOR PERMISSION.** After the files are updated, explicitly ask the User:
   > "Step `<N> — <Name>` is complete and the plan has been updated.
   >  May I proceed with Step `<N+1> — <Name>`?"

   Wait for an explicit **yes** from the User. Silence, ambiguity, or a related question is
   **not** approval.

6. **NO SCOPE CREEP.** If, mid-step, something outside the step's scope is discovered
   (a bug, a missing requirement, a better approach), **do not fix it inline**. Record it in
   [`PARKING_LOT.md`](PARKING_LOT.md) and raise it with the User at the step boundary.

7. **BLOCKED ≠ SKIPPED.** If a step cannot be completed (missing credentials, undecided
   requirement, third-party dependency), set the status to `⛔ BLOCKED`, record the exact
   blocker, and ask the User how to proceed. Never silently work around a blocker or move on
   to a different step.

8. **AESTHETICS ARE DEFERRED.** Colour, typography, imagery, brand identity, motion and
   visual polish are **explicitly out of scope until Step 30**. Until then use only the
   neutral placeholder tokens defined in `10-design-system-placeholder.md`. Do not "improve"
   the look of anything before Step 30.

9. **SPEC-FIRST.** If implementation reveals that a design document is wrong or incomplete,
   update the design document *first*, get the User's agreement, then implement.

10. **BUILD FIRST, TEST LAST.** Steps 9–28 run under the MVP Build Sprint rules in §3.
    Write production code; do not write integration tests. Read §3 before starting any of them.

---

## 1. Phase Overview

| Phase | Theme | Steps | Outcome |
|---|---|---|---|
| **A** | Foundations | 1–5 | Repos, containers, backend/DB skeleton that boots |
| **B** | Platform & Identity | 6–8 (+7A) | Auth, tenancy/white-label config, media, notifications |
| **C** | Commerce Core (Backend) | 9–14 | Catalog, inventory, vendors, pricing, cart, orders |
| **D** | Money & Movement (Backend) | 15–18 | Payments, shipping, returns, settlements/payouts |
| **E** | Content & Discovery (Backend) | 19–21 | CMS, search, reviews, reporting read-models |
| **F** | Frontend — Storefront | 22–25 | Angular SSR storefront, mobile-first |
| **G** | Frontend — Admin & Vendor | 26–28 | Angular admin app + vendor portal |
| **H** | Hardening, Deploy, Brand | 28A–33 | Build repair, tests, observability, VPS deploy, **visual design**, UAT |

---

## 2. Master Status Table

**Status legend:**
`⬜ NOT STARTED` · `🔵 IN PROGRESS` · `✅ DONE` · `⛔ BLOCKED` · `⏸️ DEFERRED`

Open the **Detail** file for the step you are working on. Do not open the others.

| # | Step | Phase | Status | Completed On | Detail | Notes |
|---|---|---|---|---|---|---|
| 0 | Specification review & sign-off | — | ✅ DONE | 2026-09-05 | [card](steps/step-00-specification-review-and-sign-off.md) | Approved by User: "Proceed with the implementation". Third-party accounts still unprovisioned — blocks Steps 15/16 |
| 1 | Repository & monorepo scaffolding | A | ✅ DONE | 2026-09-05 | [card](steps/step-01-repository-and-monorepo-scaffolding.md) | Node 24.20.0; Nx 23.2.0 / Angular 22.1 workspace. All four criteria met |
| 2 | Local containerised dev environment | A | ✅ DONE | 2026-09-05 | [card](steps/step-02-local-containerised-dev-environment.md) | Postgres 18 / Redis 8 / MinIO / Mailpit / Traefik v3 healthy. All four criteria met |
| 3 | Backend solution skeleton & cross-cutting concerns | A | ✅ DONE | 2026-09-05 | [card](steps/step-03-backend-solution-skeleton-and-cross-cutting-concerns.md) | API builds, runs and is healthy behind Traefik; 112 tests green |
| 4 | Database foundation, EF Core & migration pipeline | A | ✅ DONE | 2026-09-05 | [card](steps/step-04-database-foundation-ef-core-and-migration-pipeline.md) | Migrator applies the schema and re-runs clean; 179 tests green |
| 5 | CI pipeline & quality gates | A | ✅ DONE | 2026-09-05 | [card](steps/step-05-ci-pipeline-and-quality-gates.md) | Every gate proven to fail correctly; 88.81% line coverage. **Branch protection not demonstrable — no GitHub remote** |
| 6 | Platform module — tenancy, settings, branding, audit | B | ✅ DONE | 2026-09-05 | [card](steps/step-06-platform-module-tenancy-settings-branding-audit.md) | Tenancy, typed settings, feature flags, partitioned audit trail, Indian reference data; 7 endpoints; 252 tests, 92.44% line |
| 7 | Identity & Access module | B | ✅ DONE | 2026-09-05 | [card](steps/step-07-identity-and-access-module.md) | OTP + password/TOTP auth, rotating refresh tokens, deny-by-default permissions, vendor scope; 50 routes; 421 tests, 89.06% line |
| 7A | External identity providers & degraded-delivery mode | B | ✅ DONE | 2026-09-05 | [card](steps/step-07A-external-identity-providers-and-degraded-delivery-mode.md) | Google sign-in; four flags that turn the SMS/email-dependent features off; temporary passwords. ADR-014. 511 tests |
| 8 | Media, file storage & Notifications module | B | ✅ DONE | 2026-09-05 | [card](steps/step-08-media-file-storage-and-notifications-module.md) | Media + Notifications modules, worker container, imgproxy. ADR-015/016/017. 613 tests, 89.91% line |
| 9 | Vendor / Seller module | C | ✅ DONE | 2026-09-05 | [card](steps/step-09-vendor-seller-module.md) | Onboarding state machine, KYC, encrypted bank accounts, commission plans and their resolution, staff, pickup points, serviceability; 39 endpoints; the first integration events. 449 unit tests |
| 10 | Catalog module | C | ✅ DONE | 2026-09-05 | [card](steps/step-10-catalog-module.md) | Materialised-path taxonomy, typed attributes, Product → Variant → Listing, the buy box, India disclosures, moderation and CSV bulk import; 55 endpoints; the first integration-event consumer. 508 unit tests. **XLSX deferred** |
| 11 | Inventory & Warehouse module | C | ✅ DONE | 2026-09-06 | [card](steps/step-11-inventory-and-warehouse-module.md) | Warehouses, an append-only partitioned stock ledger, TTL reservations whose single conditional UPDATE is the platform’s oversell boundary, purchasing and stock takes; 36 endpoints; `IStockAvailability` for Cart and Orders. 545 unit tests. **`StockRunningLow` has no consumer yet** |
| 12 | Pricing, Tax & Promotions module | C | ✅ DONE | 2026-09-06 | [card](steps/step-12-pricing-tax-and-promotions-module.md) | Price lists with windows, sellers and quantity tiers; the GST engine that back-calculates an inclusive price and decides place of supply; promotions with stacking, scoping and usage limits; the store-credit wallet behind a flag; and `IPriceQuoteEngine` — the one calculation Cart, Orders and invoices all share. 33 endpoints. 591 unit tests |
| 13 | Cart & Checkout module | C | ✅ DONE | 2026-09-06 | [card](steps/step-13-cart-and-checkout-module.md) | Persistent and anonymous baskets with merge-on-login, validation that names every reason, per-seller grouping and dispatch promises, the checkout session and COD rules, and an idempotent place-order whose guarantee is a unique index; 24 endpoints. Stock is held at placement and never at add-to-cart. **`IOrderPlacement` (Step 14) and `IShippingOptions` (Step 16) are declared and unfilled, so `place-order` cannot yet succeed.** 611 unit tests |
| 14 | Ordering module & order state machine | C | ✅ DONE | 2026-09-06 | [card](steps/step-14-ordering-module-and-order-state-machine.md) | Order → sub-order-per-seller → frozen lines; one transition table that says both which edges exist and who may take them; the order's status derived from its parts; cancellation whole and partial with the stock and money it gives back; the append-only timeline; per-vendor GST invoices on a gapless counter row; 16 endpoints. `IOrderPlacement` is filled, so **COD place-order now works end to end**. 644 unit tests. **`SubOrderCancelled` has no consumer, so post-confirmation restock does not happen; prepaid is 503 until Step 15** |
| 15 | Payments module (Razorpay) | D | ✅ DONE | 2026-09-06 | [card](steps/step-15-payments-module-razorpay.md) | Razorpay hosted checkout behind an interface we own (no card data on our servers), cash on delivery as a second provider, the webhook receiver with replay protection and a dead-letter queue, maker–checker refunds, and the reconciliation and settlement jobs that recover a lost webhook; 24 endpoints. `IPaymentInitiation` is filled, so **prepaid place-order works once credentials exist**. 685 unit tests. **The gateway credentials are deliberately blank: the adapter reports itself unusable and a prepaid placement answers a named 503, so none of the four full acceptance criteria has been proved against a live gateway** |
| 16 | Shipping, Fulfilment & Logistics module | D | ⬜ NOT STARTED | | [card](steps/step-16-shipping-fulfilment-and-logistics-module.md) | Build sprint. **Needs logistics aggregator credentials** |
| 17 | Returns, Refunds & RMA module | D | ⬜ NOT STARTED | | [card](steps/step-17-returns-refunds-and-rma-module.md) | Build sprint |
| 18 | Settlements, Commission & Vendor Payouts | D | ⬜ NOT STARTED | | [card](steps/step-18-settlements-commission-and-vendor-payouts.md) | Build sprint |
| 19 | Search & Browse module | E | ⬜ NOT STARTED | | [card](steps/step-19-search-and-browse-module.md) | Build sprint |
| 20 | CMS & Merchandising module | E | ⬜ NOT STARTED | | [card](steps/step-20-cms-and-merchandising-module.md) | Build sprint |
| 21 | Reviews, Q&A, Wishlist & Reporting read-models | E | ⬜ NOT STARTED | | [card](steps/step-21-reviews-qanda-wishlist-and-reporting-read-models.md) | Build sprint |
| 22 | Angular workspace, shared libs & API client generation | F | ⬜ NOT STARTED | | [card](steps/step-22-angular-workspace-shared-libs-and-api-client-generation.md) | Build sprint |
| 23 | Storefront shell, SSR, routing & mobile-first layout | F | ⬜ NOT STARTED | | [card](steps/step-23-storefront-shell-ssr-routing-and-mobile-first-layout.md) | Build sprint |
| 24 | Storefront — browse, search, PDP | F | ⬜ NOT STARTED | | [card](steps/step-24-storefront-browse-search-pdp.md) | Build sprint |
| 25 | Storefront — cart, checkout, payment, account & orders | F | ⬜ NOT STARTED | | [card](steps/step-25-storefront-cart-checkout-payment-account-and-orders.md) | Build sprint |
| 26 | Admin app shell, auth & RBAC navigation | G | ⬜ NOT STARTED | | [card](steps/step-26-admin-app-shell-auth-and-rbac-navigation.md) | Build sprint |
| 27 | Admin — catalog, inventory, orders, fulfilment, returns | G | ⬜ NOT STARTED | | [card](steps/step-27-admin-catalog-inventory-orders-fulfilment-returns.md) | Build sprint |
| 28 | Admin — promotions, CMS, reports + Vendor portal | G | ⬜ NOT STARTED | | [card](steps/step-28-admin-promotions-cms-reports-vendor-portal.md) | Build sprint — **last build step** |
| 28A | **Build repair & boot verification** | H | ⬜ NOT STARTED | | [card](steps/step-28A-build-repair-and-boot-verification.md) | **The gate: everything compiles, migrates and boots.** Ends the build sprint |
| 29 | Test hardening & performance baseline | H | ⬜ NOT STARTED | | [card](steps/step-29-test-hardening-and-performance-baseline.md) | **Pays down every row of `TEST_DEBT.md`**, then the NFR/load/security/a11y work |
| 30 | **Design system, theming & visual identity** | H | ⬜ NOT STARTED | | [card](steps/step-30-design-system-theming-and-visual-identity.md) | Aesthetics unlocked here |
| 31 | Observability, backups & operational runbook | H | ⬜ NOT STARTED | | [card](steps/step-31-observability-backups-and-operational-runbook.md) | |
| 32 | Production deployment to VPS | H | ⬜ NOT STARTED | | [card](steps/step-32-production-deployment-to-vps.md) | |
| 33 | UAT, launch checklist & handover | H | ⬜ NOT STARTED | | [card](steps/step-33-uat-launch-checklist-and-handover.md) | |

---

## 3. MVP Build Sprint — Steps 9 to 28

**Purpose:** get a demonstrable MVP standing in the fewest turns. Verification is not skipped —
it is **batched** at Steps 28A and 29, where doing it once is cheaper than doing it twenty times.

### 3.1 The two-part acceptance contract

Every card from Step 9 to Step 28 now carries two acceptance sections:

- **Build acceptance** — what closes the step *now*, during the sprint.
- **Full acceptance criteria (verified at Step 29)** — the original criteria, unchanged. They are
  not weakened, only moved. Step 29 fails if any of them is unmet.

### 3.2 Rules during the sprint

1. **Write production code, not tests.** No integration tests, no Testcontainers fixtures, no
   Playwright specs, no architecture tests for the new module. Write a unit test **only** when it
   is the cheapest way to get an algorithm right while writing it (pricing/tax maths, a state
   machine's transition table, a checksum). If it costs more than the code, it is Step 29's.
2. **Every skipped test is recorded.** Anything the full acceptance criteria would have demanded
   and you did not do gets a row in [`TEST_DEBT.md`](TEST_DEBT.md), naming the step, the behaviour,
   and the kind of test it needs. **An undocumented gap is a protocol violation** — that ledger is
   the only thing standing between "deferred" and "forgotten".
3. **Compile what you wrote.** Before closing a step, `dotnet build src/backend/KlaraHome.sln`
   (backend) or `npx nx build <app>` (frontend) must succeed. It costs seconds and it stops twenty
   steps of drift from landing on Step 28A at once. **Do not** run `tools/ci.ps1` per step.
4. **Generate migrations; do not verify them.** Add the EF migration for your module and make sure
   the migrator project compiles. Whether it *applies* against a live database is Step 28A's
   question.
5. **Code quality is not deferred.** Optimise, name well, keep module boundaries, honour the
   architecture and security rules as you write. What is deferred is *proving* it, not *doing* it.
   A deferred test is a schedule decision; sloppy code is a defect.
6. **CI gates are frozen, not lowered.** Do not raise the per-suite test-count floors in
   `tools/ci.ps1` (currently 380 / 14 / 180) and do not lower `-CoverageMinimum` in the committed
   defaults. Coverage *will* fall as untested modules land; that is expected, and Step 29 restores
   it. For a green local sweep during the sprint, pass flags on the command line —
   `pwsh tools/ci.ps1 -Stage build`, or
   `pwsh tools/ci.ps1 -SkipIntegrationTests -CoverageMinimum 0` — never by editing the file.
7. **The rest of the protocol still applies.** One step at a time, stop at the boundary, ask
   permission, no scope creep, parking-lot everything out of scope, spec-first.

### 3.3 Where the sprint ends

- **Step 28A — Build repair & boot verification.** The first time the whole thing is built,
  migrated and booted together. Fix compilation errors, DI wiring, migration collisions and
  container startup until the full stack comes up. **No new features. No new tests.**
- **Step 29 — Test hardening.** Only once it builds and boots. Works
  [`TEST_DEBT.md`](TEST_DEBT.md) top to bottom, then the NFR, load, security and accessibility
  work that always belonged to that step.

### 3.4 The risk being accepted, stated plainly

Deferring integration tests across twenty steps means defects compound silently: a wrong
assumption in Step 10's catalog model surfaces at Step 29, after Steps 11–28 have built on it.
Rule 3 (compile every step) is the cheap mitigation, and it catches shape errors, not behavioural
ones. **This is a deliberate speed-for-rework trade** taken to reach a demo sooner. Budget Steps
28A and 29 generously — the repair pile will be real.

---

## 4. Ledgers

| Ledger | File | Rows today |
|---|---|---|
| Parking Lot — out-of-step discoveries | [`PARKING_LOT.md`](PARKING_LOT.md) | 218 |
| Specification Change Log | [`CHANGE_LOG.md`](CHANGE_LOG.md) | 29 |
| Deferred test debt | [`TEST_DEBT.md`](TEST_DEBT.md) | 141 open, plus 4 carried in from before the sprint |

---

## 5. Explicitly Deferred to Phase 2

Recorded so they are not accidentally built now: native mobile apps, multi-currency and
international shipping, subscriptions/recurring orders, B2B/wholesale portal with credit terms,
AI recommendations and semantic search, live chat, affiliate programme, gift cards,
multi-language storefront beyond `en-IN`, ONDC integration, marketplace ads / sponsored listings,
warehouse scanner apps, and true multi-tenant SaaS hosting (single-tenant-per-deployment is the
v1 redistribution model).
