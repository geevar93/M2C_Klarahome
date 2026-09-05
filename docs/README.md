# Klara Home — Technical Specification & Design

Multi-vendor e-commerce platform for the **Indian** market, built to be **re-distributable** to
other businesses on separate infrastructure.

**Status:** Specification complete — **awaiting client review and sign-off (Step 0).**
No implementation has begun, and none should begin until Step 0 is approved.

---

## Read in this order

| # | Document | What it answers |
|---|---|---|
| ⭐ | **[IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)** | **The 33-step gated execution plan and live status. Read this first and last, every time.** |
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

**After completing any step, implementation stops.** The implementer updates
`IMPLEMENTATION_PLAN.md` (status, completion date, outcome notes) and then **asks the User for
explicit permission** before starting the next step. No step is started, scaffolded, or
"prepared" ahead of approval.

The full protocol is Section "⛔ MANDATORY EXECUTION PROTOCOL" in
[IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).

---

## Open items for the client (Step 0)

1. Approve or amend this specification set.
2. Confirm the third-party providers and open the accounts listed in
   [08-integrations.md](08-integrations.md) §7 — logistics aggregator, SMS (with DLT
   registration), email, WhatsApp (optional), VPS provider and region, domain.
3. Confirm Razorpay **Route** enablement for marketplace vendor payouts.
4. Confirm commission model (flat / category-wise / tiered) and settlement cycle.
5. Confirm return window, COD limits and cancellation policy defaults.
6. Confirm the legal entity details, GSTIN and grievance officer for compliance pages.
7. Have the tax treatment (GST, TCS §52, TDS §194-O) reviewed by the client's chartered
   accountant.
8. Confirm the target go-live date so the 33 steps can be sequenced against it.
