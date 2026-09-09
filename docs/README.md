# Klara Home — Technical Specification & Design

Multi-vendor e-commerce platform for the **Indian** market, built to be **re-distributable** to
other businesses on separate infrastructure.

**Status:** Specification approved (Step 0). Execution is under way — see the Master Status
Table in [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) for what is actually done.

Steps 9-28 run as an **MVP build sprint**: production code is written, integration tests are
deferred to Step 29 and recorded in [TEST_DEBT.md](TEST_DEBT.md). See
[IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) §3.

---

## Read in this order

| # | Document | What it answers |
|---|---|---|
| ⭐ | **[IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)** | **The gated execution plan and live status - the tracker. Read this first and last, every time. Step details live in [steps/](steps/), one file per step; open only the step you are working on.** |
| 01 | [01-architecture.md](01-architecture.md) | System architecture, containers, module boundaries, technology choices, ADRs |
| 02 | [02-domain-model.md](02-domain-model.md) | Bounded contexts, aggregates, invariants, state machines, integration events, India-specific domain rules |
| 03 | [03-database-design.md](03-database-design.md) | PostgreSQL schema design, tables, indexes, constraints, migrations, retention |
| 04 | [04-api-specification.md](04-api-specification.md) | REST conventions, error model, endpoint catalogue, webhooks, rate limits |
| 05 | [05-frontend-architecture.md](05-frontend-architecture.md) | Angular workspace, SSR strategy, mobile-first rules, routes, components, performance budgets |
| 06 | [06-infrastructure-devops.md](06-infrastructure-devops.md) | Docker topology, VPS sizing, CI/CD, backups, DR, hardening |
| 07 | [07-security-compliance.md](07-security-compliance.md) | AuthN/AuthZ, application security, DPDP privacy, GST and marketplace compliance, auditing |
| 08 | [08-integrations.md](08-integrations.md) | Razorpay, logistics, SMS/WhatsApp/email, storage — behind provider interfaces |
| 09 | [09-nfr-testing-observability.md](09-nfr-testing-observability.md) | Performance/capacity/availability targets, test strategy, metrics and alerting |
| 10 | [10-design-system-placeholder.md](10-design-system-placeholder.md) | The neutral placeholder tokens used until Step 30, and what happens at Step 30 |

### Operational guides

Written as the corresponding step lands, not up front.

| Guide | Added at | What it covers |
|---|---|---|
| [dev-setup.md](dev-setup.md) | Step 2 | Running the local containerised environment: prerequisites, commands, hostnames and TLS, troubleshooting. Extended at Step 3 with the API container |
| [deployment-vps.md](deployment-vps.md) | Ahead of Step 32 | Deploying the whole stack to a VPS: Docker containers behind a host-installed Caddy, the database volume, migrating and seeding, backups and restore, and the one-variable move to Neon or another managed PostgreSQL |
| [ci-pipeline.md](ci-pipeline.md) | Step 5 | The quality gates: what each one checks, running them locally with `tools/ci.sh`, how coverage is measured and enforced, the GitHub branch-protection setup they need, and what is deliberately deferred to a later step |

### Plan files and ledgers

Split out of `IMPLEMENTATION_PLAN.md` so a working turn loads the tracker and one step card,
not the whole history.

| File | What it holds | Render when |
|---|---|---|
| [steps/](steps/) | One file per step: objective, deliverables, acceptance criteria, outcome notes | You are working on **that** step - never the folder as a whole |
| [PARKING_LOT.md](PARKING_LOT.md) | Out-of-step discoveries and their decisions | Closing a step; reviewing debt |
| [CHANGE_LOG.md](CHANGE_LOG.md) | Every specification change and who approved it | A design doc changes |
| [TEST_DEBT.md](TEST_DEBT.md) | Every test deferred during the build sprint, and Step 29's worklist | Deferring a test; working Step 29 |

### Architecture decision records

| Location | What it holds |
|---|---|
| [adr/](adr/README.md) | ADR-011 onward, one file per decision. ADR-001 – ADR-010 are tabulated in [`01-architecture.md` §9](01-architecture.md) |

---

## The decisions this specification is built on

| Decision | Value |
|---|---|
| Market | India only (INR, `en-IN`, GST, Asia/Kolkata) |
| Seller model | Multi-vendor marketplace |
| Redistribution | Single-tenant-per-deployment, tenant-aware code; zero hard-coded branding |
| Payments | Razorpay (UPI, cards, net banking, wallets, EMI) + Cash on Delivery; vendor payouts via Razorpay Route |
| v1 scope | Core commerce **+** inventory & warehouse ops **+** CMS & merchandising **+** promotions & pricing engine |
| Frontend | Angular, mobile-first, SSR storefront + **separate** admin/vendor SPA (Nx monorepo) |
| Backend | .NET 10, ASP.NET Core, modular monolith |
| Database | PostgreSQL, one schema per module |
| Hosting | Docker containers on a VPS, Traefik at the edge |
| Aesthetics | **Deferred to Step 30.** Neutral placeholders until then |

---

## ⛔ The working agreement

**After completing any step, implementation stops.** The implementer updates the tracker
(`IMPLEMENTATION_PLAN.md`: status and completion date) **and** that step's file in `steps/`
(outcome notes), then **asks the User for explicit permission** before starting the next step. No step is started, scaffolded, or
"prepared" ahead of approval.

The full protocol is Section "⛔ MANDATORY EXECUTION PROTOCOL" in
[IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).

---

## Open items for the client

Item 1 is closed (the specification was approved at Step 0). The rest are still open, and
items 2–3 become blocking at Step 15 (Razorpay) and Step 16 (logistics).

1. ~~Approve or amend this specification set.~~ ✅ Approved at Step 0.
2. Confirm the third-party providers and open the accounts listed in
   [08-integrations.md](08-integrations.md) §7 — logistics aggregator, SMS (with DLT
   registration), email, WhatsApp (optional), VPS provider and region, domain.
3. Confirm Razorpay **Route** enablement for marketplace vendor payouts.
4. Confirm commission model (flat / category-wise / tiered) and settlement cycle.
5. Confirm return window, COD limits and cancellation policy defaults.
6. Confirm the legal entity details, GSTIN and grievance officer for compliance pages.
7. Have the tax treatment (GST, TCS §52, TDS §194-O) reviewed by the client's chartered
   accountant.
8. Confirm the target go-live date so the steps can be sequenced against it.
