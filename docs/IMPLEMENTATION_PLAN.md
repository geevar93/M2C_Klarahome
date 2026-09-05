# Klara Home — Master Implementation Plan

> **Document owner:** Solution Architecture
> **Status:** DRAFT — awaiting client sign-off
> **Last updated:** 2026-09-05
> **Applies to:** Klara Home multi-vendor e-commerce platform (India)

---

## 0. How to use this document

This is the **single source of truth for execution order**. Every other document in `docs/`
describes *what* to build; this document describes *when* and *in what order*, and it records
*what has actually been done*.

---

## ⛔ MANDATORY EXECUTION PROTOCOL — READ BEFORE EVERY STEP

**These rules are non-negotiable and apply to every human and AI contributor on this project.**

1. **ONE STEP AT A TIME.** Work only on the single step that has been explicitly authorised.
   Never start the next step because it "seems obvious" or "is only a small change".

2. **STOP AT THE END OF EVERY STEP.** When the step's Acceptance Criteria are met, **halt all
   implementation immediately**. Do not begin, scaffold, stub, or "prepare" any part of a
   later step.

3. **UPDATE THIS FILE BEFORE ASKING.** On completion of a step you MUST:
   - Change that step's **Status** to `✅ DONE` in the Master Status Table (Section 2).
   - Fill in the **Completed On** date.
   - Fill in the **Outcome / Notes** in the step card itself: what was built, files/folders
     created, any deviations from the spec, any known gaps or technical debt.
   - Add an entry to the **Change Log** (Section 5) if the spec itself had to change.

4. **THEN ASK FOR PERMISSION.** After the file is updated, explicitly ask the User:
   > "Step `<N> — <Name>` is complete and the plan has been updated.
   >  May I proceed with Step `<N+1> — <Name>`?"

   Wait for an explicit **yes** from the User. Silence, ambiguity, or a related question is
   **not** approval.

5. **NO SCOPE CREEP.** If, mid-step, something outside the step's scope is discovered
   (a bug, a missing requirement, a better approach), **do not fix it inline**. Record it in
   Section 4 (Parking Lot) and raise it with the User at the step boundary.

6. **BLOCKED ≠ SKIPPED.** If a step cannot be completed (missing credentials, undecided
   requirement, third-party dependency), set the status to `⛔ BLOCKED`, record the exact
   blocker, and ask the User how to proceed. Never silently work around a blocker or move on
   to a different step.

7. **AESTHETICS ARE DEFERRED.** Colour, typography, imagery, brand identity, motion and
   visual polish are **explicitly out of scope until Step 30**. Until then use only the
   neutral placeholder tokens defined in `10-design-system-placeholder.md`. Do not "improve"
   the look of anything before Step 30.

8. **SPEC-FIRST.** If implementation reveals that a design document is wrong or incomplete,
   update the design document *first*, get the User's agreement, then implement.

---

## 1. Phase Overview

| Phase | Theme | Steps | Outcome |
|---|---|---|---|
| **A** | Foundations | 1–5 | Repos, containers, backend/DB skeleton that boots |
| **B** | Platform & Identity | 6–8 | Auth, tenancy/white-label config, media, notifications |
| **C** | Commerce Core (Backend) | 9–14 | Catalog, inventory, vendors, pricing, cart, orders |
| **D** | Money & Movement (Backend) | 15–18 | Payments, shipping, returns, settlements/payouts |
| **E** | Content & Discovery (Backend) | 19–21 | CMS, search, reviews, reporting read-models |
| **F** | Frontend — Storefront | 22–25 | Angular SSR storefront, mobile-first |
| **G** | Frontend — Admin & Vendor | 26–28 | Angular admin app + vendor portal |
| **H** | Hardening, Deploy, Brand | 29–33 | Tests, observability, VPS deploy, **visual design**, UAT |

---

## 2. Master Status Table

**Status legend:**
`⬜ NOT STARTED` · `🔵 IN PROGRESS` · `✅ DONE` · `⛔ BLOCKED` · `⏸️ DEFERRED`

| # | Step | Phase | Status | Completed On | Notes |
|---|---|---|---|---|---|
| 0 | Specification review & sign-off | — | ⬜ NOT STARTED | | Awaiting User review of all `docs/` |
| 1 | Repository & monorepo scaffolding | A | ⬜ NOT STARTED | | |
| 2 | Local containerised dev environment | A | ⬜ NOT STARTED | | |
| 3 | Backend solution skeleton & cross-cutting concerns | A | ⬜ NOT STARTED | | |
| 4 | Database foundation, EF Core & migration pipeline | A | ⬜ NOT STARTED | | |
| 5 | CI pipeline & quality gates | A | ⬜ NOT STARTED | | |
| 6 | Platform module — tenancy, settings, branding, audit | B | ⬜ NOT STARTED | | |
| 7 | Identity & Access module | B | ⬜ NOT STARTED | | |
| 8 | Media, file storage & Notifications module | B | ⬜ NOT STARTED | | |
| 9 | Vendor / Seller module | C | ⬜ NOT STARTED | | |
| 10 | Catalog module | C | ⬜ NOT STARTED | | |
| 11 | Inventory & Warehouse module | C | ⬜ NOT STARTED | | |
| 12 | Pricing, Tax & Promotions module | C | ⬜ NOT STARTED | | |
| 13 | Cart & Checkout module | C | ⬜ NOT STARTED | | |
| 14 | Ordering module & order state machine | C | ⬜ NOT STARTED | | |
| 15 | Payments module (Razorpay) | D | ⬜ NOT STARTED | | |
| 16 | Shipping, Fulfilment & Logistics module | D | ⬜ NOT STARTED | | |
| 17 | Returns, Refunds & RMA module | D | ⬜ NOT STARTED | | |
| 18 | Settlements, Commission & Vendor Payouts | D | ⬜ NOT STARTED | | |
| 19 | Search & Browse module | E | ⬜ NOT STARTED | | |
| 20 | CMS & Merchandising module | E | ⬜ NOT STARTED | | |
| 21 | Reviews, Q&A, Wishlist & Reporting read-models | E | ⬜ NOT STARTED | | |
| 22 | Angular workspace, shared libs & API client generation | F | ⬜ NOT STARTED | | |
| 23 | Storefront shell, SSR, routing & mobile-first layout | F | ⬜ NOT STARTED | | |
| 24 | Storefront — browse, search, PDP | F | ⬜ NOT STARTED | | |
| 25 | Storefront — cart, checkout, payment, account & orders | F | ⬜ NOT STARTED | | |
| 26 | Admin app shell, auth & RBAC navigation | G | ⬜ NOT STARTED | | |
| 27 | Admin — catalog, inventory, orders, fulfilment, returns | G | ⬜ NOT STARTED | | |
| 28 | Admin — promotions, CMS, reports + Vendor portal | G | ⬜ NOT STARTED | | |
| 29 | Test hardening & performance baseline | H | ⬜ NOT STARTED | | |
| 30 | **Design system, theming & visual identity** | H | ⬜ NOT STARTED | | Aesthetics unlocked here |
| 31 | Observability, backups & operational runbook | H | ⬜ NOT STARTED | | |
| 32 | Production deployment to VPS | H | ⬜ NOT STARTED | | |
| 33 | UAT, launch checklist & handover | H | ⬜ NOT STARTED | | |

---

## 3. Step Cards

Each card is the contract for that step. Do not treat anything outside "Deliverables" as in scope.

---

### Step 0 — Specification review & sign-off
- **Phase:** —
- **Depends on:** —
- **Objective:** The User reviews every document in `docs/` and confirms or amends the
  architecture, domain model, data model, API contract, infrastructure plan and scope.
- **Deliverables:**
  - Written confirmation (or list of change requests) covering docs `01`–`10`.
  - Confirmed list of third-party accounts to be provisioned (Razorpay, SMS/WhatsApp provider,
    email provider, logistics aggregator, domain, VPS).
- **Acceptance criteria:** User states the specification is approved (or approved with the
  recorded amendments applied).
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 1 — Repository & monorepo scaffolding
- **Phase:** A · **Depends on:** Step 0
- **Objective:** Create the physical repository layout for backend, frontend and infrastructure
  with conventions locked in.
- **Deliverables:**
  - Git repository initialised; branch strategy (`main`, `develop`, `feature/*`, `release/*`)
    documented in `CONTRIBUTING.md`.
  - Folder structure per `01-architecture.md` §Repository Layout.
  - `.editorconfig`, `.gitignore`, `.gitattributes`, `Directory.Build.props`,
    `Directory.Packages.props` (central package management), `global.json` pinning .NET 10 SDK.
  - Nx workspace created for Angular apps (`storefront`, `admin`) with shared lib placeholders.
  - `README.md` at repo root with the "how to run" skeleton.
  - Commit message convention (Conventional Commits) + PR template.
- **Acceptance criteria:** `git log` shows an initial commit; folder tree matches the spec;
  `dotnet --version` and `npx nx --version` both resolve from the repo.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 2 — Local containerised dev environment
- **Phase:** A · **Depends on:** Step 1
- **Objective:** A single command brings up every backing service a developer needs, matching
  what will run on the VPS.
- **Deliverables:**
  - `docker-compose.dev.yml` with: PostgreSQL 18, Redis 8, MinIO (S3-compatible),
    Mailpit (SMTP capture), Traefik v3 (reverse proxy + local TLS).
  - `.env.example` with every variable documented; `.env` git-ignored.
  - Named volumes for data persistence; health checks on every service.
  - `docs/dev-setup.md` — prerequisites and troubleshooting for Windows/WSL2.
- **Acceptance criteria:** `docker compose -f docker-compose.dev.yml up -d` starts all services
  healthy; Postgres reachable; MinIO console reachable; Mailpit UI reachable.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 3 — Backend solution skeleton & cross-cutting concerns
- **Phase:** A · **Depends on:** Step 2
- **Objective:** A running ASP.NET Core 10 API host with the modular-monolith skeleton and all
  cross-cutting concerns in place — no business features.
- **Deliverables:**
  - Solution + projects per `01-architecture.md` §Backend Structure
    (`KlaraHome.Api`, `KlaraHome.SharedKernel`, `KlaraHome.Modules.*` template).
  - Module registration convention (each module self-registers endpoints, DI, DB schema).
  - Cross-cutting: structured logging (Serilog), OpenTelemetry wiring, global exception
    handler + RFC 9457 `application/problem+json` error model, request correlation IDs,
    FluentValidation pipeline, rate limiting, CORS, response compression, output caching.
  - Health endpoints: `/health/live`, `/health/ready`.
  - OpenAPI document generation + Scalar/Swagger UI in non-production.
  - Options/configuration binding with validation on startup (fail fast).
  - `Dockerfile` for the API (multi-stage, non-root, trimmed runtime image).
- **Acceptance criteria:** API container builds and runs; `/health/ready` returns 200;
  OpenAPI document renders; a deliberate exception returns a correct ProblemDetails payload.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 4 — Database foundation, EF Core & migration pipeline
- **Phase:** A · **Depends on:** Step 3
- **Objective:** Establish data-access conventions and a repeatable migration workflow before
  any table exists.
- **Deliverables:**
  - EF Core 10 + Npgsql; one `DbContext` per module, one Postgres **schema** per module.
  - Base entity types, UUIDv7 key generation, `created_at`/`updated_at`/`created_by` auditing
    interceptor, optimistic concurrency token, soft-delete convention + global query filter.
  - Money value object mapped to `numeric(18,4)` + currency code; snake_case naming convention.
  - Transactional **Outbox** table + dispatcher hosted service (for integration events).
  - Migration strategy: design-time factory, `--idempotent` SQL script generation, migration
    runner container/job (migrations never run automatically from the API in production).
  - Seed/bootstrap data mechanism (roles, settings, tax rates, states/PIN reference data).
- **Acceptance criteria:** A sample migration creates its schema and applies cleanly to the
  dev Postgres container; idempotent script generation verified; audit columns populate.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 5 — CI pipeline & quality gates
- **Phase:** A · **Depends on:** Step 4
- **Objective:** Every commit is built, tested and analysed automatically.
- **Deliverables:**
  - CI workflow: restore → build → unit tests → integration tests (Testcontainers Postgres)
    → lint/format check → Docker image build → vulnerability scan (Trivy) → publish to registry.
  - Angular pipeline: lint, unit tests, build (both apps).
  - Code coverage reporting with an agreed minimum threshold.
  - Static analysis: .NET analyzers as errors, ESLint + Prettier for Angular.
  - Secret scanning; dependency update automation.
- **Acceptance criteria:** A pull request triggers the full pipeline and blocks merge on failure.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 6 — Platform module: tenancy, settings, branding, audit
- **Phase:** B · **Depends on:** Step 5
- **Objective:** Make the platform **re-distributable**: nothing about "Klara Home" is
  hard-coded, and a second business can be onboarded onto separate infrastructure by
  configuration alone.
- **Deliverables:**
  - `tenant` table + `tenant_id` propagation convention (ambient tenant context, EF query
    filters), defaulted to a single tenant for this deployment.
  - Store/brand configuration: legal entity, GSTIN, addresses, support contacts, logos,
    locale, currency, timezone, business rules (returns window, COD limits).
  - Typed application settings store (DB-backed, cached, admin-editable) + feature-flag service.
  - Immutable audit log (who/what/when/before/after) with a queryable API.
  - Reference data: Indian states + union territories, PIN code metadata, HSN chapters.
- **Acceptance criteria:** All branding/legal strings resolve from configuration; an audit
  entry is written for a settings change; a feature flag toggles observable behaviour.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 7 — Identity & Access module
- **Phase:** B · **Depends on:** Step 6
- **Objective:** Authentication and authorisation for three actor classes: Customer, Vendor
  staff, Platform staff.
- **Deliverables:**
  - Registration/login: mobile number + OTP (primary for customers), email + password
    (secondary), password reset, email/phone verification.
  - JWT access tokens (short-lived) + rotating refresh tokens (HttpOnly, secure cookie),
    device/session listing and revocation.
  - RBAC: roles, granular permissions, permission-based authorisation policies; vendor-scoped
    authorisation (a vendor user may only ever see their own data).
  - Mandatory TOTP 2FA for platform-admin and vendor-owner roles.
  - Brute-force protection, OTP throttling, account lockout, security event logging.
  - Customer profile, saved addresses (Indian address model), GSTIN for B2B invoices.
- **Acceptance criteria:** All three actor types can authenticate; a vendor user is provably
  denied access to another vendor's data; refresh-token rotation and revocation verified.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 8 — Media, file storage & Notifications module
- **Phase:** B · **Depends on:** Step 7
- **Objective:** Central services every later module depends on.
- **Deliverables:**
  - Media service: S3-compatible upload (MinIO), validation, virus-scan hook, image
    derivatives/responsive variants (imgproxy), CDN-ready public URLs, signed private URLs.
  - Notification service: templated Email / SMS / WhatsApp with provider abstraction,
    per-event templates, localisation, queued + retried delivery, delivery status log,
    customer notification preferences, DLT-compliant SMS template registry (India TRAI).
  - Document generation service (invoices, credit notes, labels) — PDF pipeline.
- **Acceptance criteria:** An image uploads and returns responsive variants; a templated
  transactional email and SMS are dispatched and logged; failures retry with backoff.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 9 — Vendor / Seller module
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
- **Acceptance criteria:** A vendor can be onboarded end-to-end through the API and reaches
  Active; a commission plan resolves correctly for a given vendor + category.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 10 — Catalog module
- **Phase:** C · **Depends on:** Step 9
- **Objective:** The product information model.
- **Deliverables:**
  - Category tree (materialised path), brands, attribute sets, attributes (typed, filterable,
    variant-defining), attribute values.
  - Product → Variant (SKU) model; per-vendor **listings/offers** against a variant so multiple
    vendors can sell the same product; buy-box selection rule.
  - Media galleries, rich descriptions, specifications, SEO slugs/meta, canonical URLs.
  - India compliance fields: HSN code, GST rate, MRP, net quantity, country of origin,
    manufacturer/packer/importer details, expiry/shelf-life where applicable
    (Legal Metrology + Consumer Protection (E-Commerce) Rules 2020).
  - Product lifecycle/state machine (Draft → Pending Approval → Active → Archived) with
    platform moderation of vendor-submitted products.
  - Bulk import/export (CSV/XLSX) with validation report.
- **Acceptance criteria:** A variant with attributes, media and two competing vendor offers can
  be created, moderated, published and retrieved; bulk import of 1,000 SKUs validates and loads.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 11 — Inventory & Warehouse module
- **Phase:** C · **Depends on:** Step 10
- **Objective:** Accurate, auditable stock across vendors and locations.
- **Deliverables:**
  - Warehouses/locations per vendor; stock item per (listing, location).
  - Stock ledger: every movement is an immutable entry (inbound, sale, reservation, release,
    return, adjustment, damage, transfer) — quantity on hand is derived and reconcilable.
  - Reservation model with TTL for carts/checkout; oversell prevention under concurrency.
  - Low-stock thresholds and alerts; backorder / pre-order flags.
  - Purchase orders, suppliers, goods receipt (GRN), stock takes / cycle counts.
  - Batch/lot and serial tracking flags (design present, enforcement optional per category).
- **Acceptance criteria:** A concurrency test proves no oversell; ledger sum always equals
  on-hand quantity; reservations expire and release stock.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 12 — Pricing, Tax & Promotions module
- **Phase:** C · **Depends on:** Step 11
- **Objective:** Deterministic, explainable price and tax calculation.
- **Deliverables:**
  - Price lists (base, sale, scheduled), per-vendor pricing, MRP vs selling price, tiered
    quantity pricing.
  - **GST engine:** HSN → rate resolution, place-of-supply logic, CGST/SGST vs IGST split,
    inclusive-of-tax pricing (Indian retail norm), rounding rules, cess support.
  - Promotion engine: coupon codes, automatic cart rules, category/brand/vendor scoping,
    BOGO/bundles, free shipping, first-order, stacking rules and priority, usage limits
    (global / per-customer), validity windows, flash-sale scheduling.
  - Loyalty / store-credit wallet (accrual + redemption) — design now, enable via feature flag.
  - **Price quote API** returning a fully itemised, auditable breakdown (line, discount, tax,
    shipping, total) — one calculation engine shared by cart, checkout, orders and invoices.
- **Acceptance criteria:** A golden-file test suite of ~30 pricing/tax scenarios (intra-state,
  inter-state, coupon + tax interaction, rounding) passes exactly.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 13 — Cart & Checkout module
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
- **Acceptance criteria:** A multi-vendor cart produces a correct grouped, priced checkout
  summary; duplicate place-order requests create exactly one order.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 14 — Ordering module & order state machine
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
- **Acceptance criteria:** A two-vendor order splits into two sub-orders that transition
  independently; invalid transitions are rejected; invoice numbers are gapless per vendor per FY.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 15 — Payments module (Razorpay)
- **Phase:** D · **Depends on:** Step 14
- **Objective:** Take money safely and reconcile it.
- **Deliverables:**
  - Razorpay Orders API integration; hosted/standard checkout handoff (no card data ever
    touches our servers — PCI-DSS SAQ-A posture).
  - Supported methods: UPI, cards, net banking, wallets, EMI; plus **COD** as an internal
    method with its own flow.
  - **Webhook receiver**: HMAC signature verification, replay protection, idempotent handling,
    raw-payload persistence, dead-letter queue for unprocessable events.
  - Payment aggregate: authorisation, capture, failure, retry links, partial and full refunds.
  - Payment ↔ order reconciliation job; settlement report ingestion; mismatch alerting.
  - Payment audit trail and PII/secret handling rules.
- **Acceptance criteria:** A sandbox end-to-end payment moves an order to Confirmed; a replayed
  webhook is a no-op; a refund is recorded and reconciles; a dropped webhook is recovered by
  the reconciliation job.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 16 — Shipping, Fulfilment & Logistics module
- **Phase:** D · **Depends on:** Step 15
- **Objective:** Get parcels moving and keep customers informed.
- **Deliverables:**
  - Shipping zones and rate rules (weight/value/zone/free-shipping thresholds), per-vendor
    overrides, volumetric weight.
  - PIN-code serviceability and ETA estimation (prepaid vs COD serviceability differ).
  - Logistics aggregator integration behind a provider interface (Shiprocket / Delhivery /
    Blue Dart class): create shipment, generate AWB + label + manifest, schedule pickup,
    cancel shipment.
  - Packing/fulfilment workflow: pick list, pack, weight capture, multi-package shipments,
    partial shipments.
  - Tracking webhook/polling ingestion → order timeline + customer notifications; NDR
    (non-delivery report) handling and re-attempt workflow.
  - COD remittance tracking per shipment.
- **Acceptance criteria:** A confirmed order produces a shipment with an AWB in the provider
  sandbox; tracking updates flow into the order timeline and trigger notifications.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 17 — Returns, Refunds & RMA module
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
- **Acceptance criteria:** A delivered line can be returned, picked up, QC'd and refunded with
  a correct credit note and stock adjustment.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 18 — Settlements, Commission & Vendor Payouts
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
- **Acceptance criteria:** For a sample month, vendor ledger balances tie out to orders,
  returns and payouts to the paisa; the TCS/TDS extract matches expected values.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 19 — Search & Browse module
- **Phase:** E · **Depends on:** Step 18
- **Objective:** Fast, faceted product discovery.
- **Deliverables:**
  - PostgreSQL full-text search (`tsvector`, weighted fields) + `pg_trgm` fuzzy matching,
    synonyms, stop words; ranking that blends relevance, availability and popularity.
  - Faceted filtering (category, brand, price bands, attributes, vendor, rating, discount,
    availability) with facet counts; sorting; keyset pagination.
  - Autocomplete/suggestions; zero-result handling and query logging for merchandising.
  - Denormalised search projection table kept current via the outbox/event pipeline.
  - Documented, feature-flagged upgrade path to a dedicated engine (Meilisearch / OpenSearch)
    if catalogue size or latency demands it.
- **Acceptance criteria:** p95 search latency within the NFR target on a seeded catalogue of
  50,000 SKUs; facet counts provably correct against SQL ground truth.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 20 — CMS & Merchandising module
- **Phase:** E · **Depends on:** Step 19
- **Objective:** Let the business change the storefront without a deployment.
- **Deliverables:**
  - Block-based page composer (hero, banner grid, product carousel, category tiles, rich text,
    FAQ, testimonial, custom HTML) with typed block schemas.
  - Home page layout, landing pages, static/legal pages, navigation menus, footer builder.
  - Curated collections (manual + rule-based), product ranking/pinning per collection.
  - Banners with scheduling and targeting; announcement bar.
  - Draft → Preview → Scheduled → Published workflow with version history and rollback.
  - SEO: per-page meta, Open Graph, `sitemap.xml`, `robots.txt`, structured data (Product,
    Offer, BreadcrumbList, Organization), 301 redirect manager.
  - Blog/lookbook (optional, feature-flagged).
- **Acceptance criteria:** A home page is composed, previewed, scheduled and published; the
  storefront renders it server-side; sitemap and structured data validate.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 21 — Reviews, Q&A, Wishlist & Reporting read-models
- **Phase:** E · **Depends on:** Step 20
- **Objective:** Social proof, saved intent, and the numbers the business runs on.
- **Deliverables:**
  - Verified-purchase reviews with ratings, images, moderation queue, vendor replies,
    helpfulness votes; aggregate rating projections.
  - Product Q&A; report-abuse workflow.
  - Wishlist / save-for-later; back-in-stock and price-drop subscriptions.
  - Reporting read-models & APIs: sales by day/category/vendor, GMV vs net revenue, AOV,
    conversion funnel, cart abandonment, top/slow SKUs, stock ageing, return rate by reason,
    settlement summary, COD vs prepaid mix.
  - Scheduled report exports (CSV) and email delivery.
- **Acceptance criteria:** Reports reconcile against transactional data for a seeded dataset;
  a review can only be posted against a delivered purchase.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 22 — Angular workspace, shared libs & API client generation
- **Phase:** F · **Depends on:** Step 21
- **Objective:** Frontend foundations shared by both apps.
- **Deliverables:**
  - Nx workspace with apps `storefront` and `admin`; libs `ui`, `data-access`, `domain`,
    `util`, `i18n`, `testing`.
  - Typed API client **generated from the backend OpenAPI document** as a CI step (no
    hand-written DTOs).
  - HTTP interceptors: auth/refresh, correlation ID, error normalisation, loading state, retry.
  - Core services: auth/session, runtime config bootstrap (`config.json`, so one image serves
    any environment), feature flags, analytics abstraction, toast/notification.
  - Placeholder design tokens wired as CSS custom properties (per
    `10-design-system-placeholder.md`) — **strictly neutral, no branding**.
  - Testing setup: Jest/Vitest + Testing Library, Playwright for e2e, MSW for API mocking.
  - Accessibility and i18n scaffolding (`en-IN` default), currency/number/date pipes for INR.
- **Acceptance criteria:** Both apps build and serve; the generated client compiles against the
  live OpenAPI document; a sample authenticated request succeeds through the interceptor chain.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 23 — Storefront shell, SSR, routing & mobile-first layout
- **Phase:** F · **Depends on:** Step 22
- **Objective:** The storefront skeleton: fast, indexable, mobile-first.
- **Deliverables:**
  - Angular SSR (`@angular/ssr`) with hydration; transfer-state caching; route-level
    prerendering where appropriate.
  - App shell: header, mobile navigation drawer, search entry, mini-cart, sticky bottom action
    bar (mobile), footer, breadcrumbs.
  - Responsive layout primitives and breakpoint strategy (mobile-first, 360px baseline).
  - Routing map with lazy-loaded feature routes and route-level data resolvers.
  - Global states: loading skeletons, empty states, error boundary, offline notice, 404/500.
  - SEO service (title/meta/canonical/JSON-LD); performance budgets enforced in CI.
- **Acceptance criteria:** Lighthouse mobile performance/SEO/a11y meet the NFR thresholds on
  the shell; SSR output contains meaningful HTML (verified with JS disabled).
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 24 — Storefront: browse, search, PDP
- **Phase:** F · **Depends on:** Step 23
- **Objective:** The discovery journey.
- **Deliverables:**
  - Home page rendering CMS blocks; category landing pages.
  - Product listing page: mobile filter sheet, facets, sort, infinite scroll or pagination,
    URL-synced state, result count, no-results recovery.
  - Search with autocomplete and recent/trending searches.
  - Product detail page: responsive gallery/zoom, variant selection, price with MRP/discount,
    tax note, delivery estimate by PIN code, offers, vendor/seller info, stock messaging,
    specifications, reviews and Q&A, related/recently viewed.
  - Add-to-cart interactions and wishlist.
- **Acceptance criteria:** The full browse → search → PDP → add-to-cart journey works on a
  360px viewport; the PDP is server-rendered with correct structured data.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 25 — Storefront: cart, checkout, payment, account & orders
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
- **Acceptance criteria:** A complete purchase (prepaid via sandbox, and COD) is possible on
  mobile; the order appears in the account with tracking and a downloadable invoice.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 26 — Admin app shell, auth & RBAC navigation
- **Phase:** G · **Depends on:** Step 25
- **Objective:** The back-office foundation, serving both platform staff and vendors.
- **Deliverables:**
  - Admin SPA shell: login with 2FA, session handling, responsive layout (desktop-first but
    usable on tablet), navigation driven by the user's permissions.
  - Reusable admin building blocks: data table (server-side paging/sorting/filtering, column
    config, bulk actions, export), form framework, drawer/modal patterns, file uploader,
    audit-trail viewer, confirmation patterns for destructive actions.
  - Global search, notifications centre, impersonation (audited) for support.
- **Acceptance criteria:** Two users with different roles see correctly different navigation
  and are blocked from unauthorised routes and API calls.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 27 — Admin: catalog, inventory, orders, fulfilment, returns
- **Phase:** G · **Depends on:** Step 26
- **Objective:** Daily operations screens.
- **Deliverables:**
  - Catalog: category/brand/attribute management, product & variant editor, media manager,
    bulk import/export with error report, moderation queue for vendor submissions.
  - Inventory: stock by location, adjustments with reason, stock ledger view, low-stock queue,
    purchase orders and GRN, stock takes.
  - Orders: list with rich filters, order detail, timeline, notes, manual state transitions,
    cancellations, invoice/credit-note actions.
  - Fulfilment: pick/pack workflow, shipment creation, label/manifest printing, NDR queue.
  - Returns: RMA queue, approval, pickup scheduling, QC disposition, refund initiation.
- **Acceptance criteria:** An operator can take an order from placed to delivered, and a return
  from request to refund, entirely through the admin UI.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 28 — Admin: promotions, CMS, reports + Vendor portal
- **Phase:** G · **Depends on:** Step 27
- **Objective:** Merchandising, analytics, and the vendor-facing experience.
- **Deliverables:**
  - Promotions: coupon and cart-rule builder with live preview/simulator, scheduling,
    usage analytics.
  - CMS: page composer UI with block library, drag-order, preview, scheduling, version history,
    menu/banner/collection management, redirect and SEO tools.
  - Reports dashboards (placeholder-styled charts only; visual polish deferred to Step 30).
  - Vendor portal (same app, vendor-scoped roles): onboarding/KYC, listings, inventory,
    orders and dispatch, returns, ledger and settlement statements, performance metrics.
  - Platform administration: users/roles, vendors, commission plans, settings, feature flags,
    tax rates, shipping zones, audit log.
- **Acceptance criteria:** A vendor completes a full day's operations in the portal; a
  merchandiser publishes a campaign (coupon + banner + collection) without engineering help.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 29 — Test hardening & performance baseline
- **Phase:** H · **Depends on:** Step 28
- **Objective:** Prove the system behaves under real conditions before it is dressed up.
- **Deliverables:**
  - Coverage gaps closed: unit tests on domain/pricing/state machines; integration tests on
    every module with Testcontainers; contract tests on the OpenAPI surface.
  - End-to-end Playwright suites for the critical journeys (browse→buy, COD, return, vendor
    dispatch, admin order ops).
  - Load and soak testing (k6) against target concurrency; database index review from real
    query plans; N+1 elimination.
  - Security testing: OWASP ASVS checklist pass, dependency and container scanning, authz
    matrix test (every endpoint × every role), rate-limit verification.
  - Accessibility audit (WCAG 2.2 AA) on storefront critical paths.
- **Acceptance criteria:** All NFR targets in `09-nfr-testing-observability.md` are met and
  evidenced; no critical/high vulnerabilities open.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 30 — Design system, theming & visual identity ⭐
- **Phase:** H · **Depends on:** Step 29
- **Objective:** **This is the step where aesthetics are finally decided and applied.** Until
  this step, everything uses neutral placeholders.
- **Deliverables:**
  - Brand discovery with the client: positioning, audience, references, moodboard direction.
  - Design tokens finalised: colour system (with dark-mode decision), typography scale and
    font pairing, spacing/radius/elevation scales, iconography, imagery and photography rules,
    motion principles.
  - Component library restyled against the tokens; storefront and admin themed consistently.
  - **White-label theming mechanism verified**: tokens delivered as runtime CSS custom
    properties per tenant, so a second client can be rebranded via configuration and assets
    only — no code changes, no rebuild.
  - Responsive visual QA across the device matrix; contrast/accessibility re-verified after
    theming; email/PDF template styling; favicon, app icons, PWA manifest, OG images.
  - Design documentation handed over.
- **Acceptance criteria:** Client signs off the visual design on the real application; a
  second demo theme can be applied end-to-end purely by swapping configuration.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 31 — Observability, backups & operational runbook
- **Phase:** H · **Depends on:** Step 30
- **Objective:** Make the system operable and recoverable.
- **Deliverables:**
  - Centralised logs (Loki), metrics (Prometheus), dashboards (Grafana), traces (OTel/Tempo),
    uptime checks and alert routing.
  - Business/technical alerts: payment webhook failure rate, orders stuck in a state, error
    rate, latency, queue depth, disk/CPU/memory, certificate expiry, failed payouts.
  - Automated encrypted Postgres backups with off-VPS copies, PITR/WAL archiving, MinIO backup,
    and a **documented, tested restore drill**.
  - Runbooks: deploy, rollback, restore, rotate secrets, scale, incident response, on-call.
- **Acceptance criteria:** A restore drill succeeds from backup into a clean environment; a
  simulated failure triggers the correct alert.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 32 — Production deployment to VPS
- **Phase:** H · **Depends on:** Step 31
- **Objective:** Live infrastructure.
- **Deliverables:**
  - VPS hardening (SSH keys only, firewall, fail2ban, unattended security updates, non-root
    container users, resource limits).
  - Production `docker-compose.prod.yml` (or Swarm) with Traefik + Let's Encrypt, all services,
    resource limits, restart policies, log rotation.
  - Secrets management (Docker secrets / SOPS-encrypted env); separate staging and production.
  - Release pipeline: image build → registry → migration job → rolling restart → smoke tests
    → automated rollback on failure.
  - DNS, TLS, CDN/caching configuration, rate limiting and WAF rules at the edge.
- **Acceptance criteria:** Staging and production are both live on HTTPS; a full deploy and a
  rollback are each executed successfully and timed.
- **Outcome / Notes:** _(to be filled on completion)_

---

### Step 33 — UAT, launch checklist & handover
- **Phase:** H · **Depends on:** Step 32
- **Objective:** Go live with confidence.
- **Deliverables:**
  - UAT script covering every role; defect triage and fix cycle to zero criticals.
  - Production data readiness: real catalogue load, vendor onboarding, tax/HSN verification,
    shipping rates, legal/policy pages, GST and payment credentials switched to live.
  - Launch checklist sign-off (legal, compliance, analytics, SEO, monitoring, support).
  - Training and handover: admin/vendor user guides, architecture walkthrough, runbooks,
    credential handover, support/SLA agreement.
  - Post-launch hypercare plan and the Phase-2 backlog.
- **Acceptance criteria:** Client signs off UAT and the launch checklist; the platform is live.
- **Outcome / Notes:** _(to be filled on completion)_

---

## 4. Parking Lot (out-of-step discoveries)

Anything found mid-step that is **not** in that step's Deliverables goes here, and is discussed
with the User at the step boundary. Do not act on these items without explicit approval.

| Date | Raised during | Item | Decision |
|---|---|---|---|
| | | | |

---

## 5. Change Log

| Date | Step | Document(s) changed | Reason | Approved by |
|---|---|---|---|---|
| 2026-09-05 | — | All | Initial specification set created | Pending |

---

## 6. Explicitly Deferred to Phase 2

Recorded so they are not accidentally built now: native mobile apps, multi-currency and
international shipping, subscriptions/recurring orders, B2B/wholesale portal with credit terms,
AI recommendations and semantic search, live chat, affiliate programme, gift cards,
multi-language storefront beyond `en-IN`, ONDC integration, marketplace ads / sponsored listings,
warehouse scanner apps, and true multi-tenant SaaS hosting (single-tenant-per-deployment is the
v1 redistribution model).
