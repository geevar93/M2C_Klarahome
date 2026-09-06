# 09 — Non-Functional Requirements, Testing & Observability

---

## 1. Non-Functional Requirements

### 1.1 Performance

| Metric | Target | Measured by |
|---|---|---|
| API p50 / p95 / p99 (reads) | 80 ms / 300 ms / 600 ms | Prometheus histograms |
| API p95 (writes: cart, order) | 500 ms | Prometheus |
| Search / PLP query p95 | 400 ms on 50k SKUs | k6 + query plans |
| Place-order end-to-end p95 | 1.5 s (excluding gateway redirect) | k6 |
| Storefront LCP (mobile, 4G, mid-tier Android) | ≤ 2.5 s | Lighthouse CI + field RUM |
| Storefront INP / CLS | ≤ 200 ms / ≤ 0.1 | Lighthouse CI |
| SSR TTFB | ≤ 600 ms cached, ≤ 1.2 s uncached | Synthetic checks |
| Admin list render (1k rows, server-paged) | ≤ 1 s | Playwright timing |

### 1.2 Capacity (launch targets, re-baselined at Step 29)

| Dimension | Target |
|---|---|
| Concurrent storefront users | 500 sustained, 2,000 peak (sale event) |
| Orders per day | 5,000 sustained, 20,000 peak day |
| Catalogue | 100,000 variants, 500 vendors |
| API throughput | 300 req/s sustained on the production VPS |
| Database | < 60 % CPU at peak; connections < 60 % of `max_connections` |

Load tests must demonstrate headroom of at least 2× the sustained target before go-live.

### 1.3 Availability & Recovery

| Item | Target |
|---|---|
| Uptime (business hours, single VPS) | 99.5 % monthly |
| Planned maintenance | Announced, off-peak, ≤ 30 min |
| RPO | ≤ 15 minutes (WAL archiving) |
| RTO | ≤ 2 hours (documented, drilled at Step 31) |
| Deploy | Zero-downtime for app containers; migrations are backwards-compatible |

Single-VPS deployment cannot promise more than this. If the client needs higher availability,
the scale-out path in `06-infrastructure-devops.md` §2 becomes a costed change request — it is
not silently assumed.

### 1.4 Other NFRs

| Attribute | Requirement |
|---|---|
| **Security** | OWASP ASVS L2; no critical/high vulnerabilities at release |
| **Accessibility** | WCAG 2.2 AA on storefront critical paths; zero critical axe violations |
| **Compatibility** | Chrome/Edge/Firefox/Safari latest 2 versions; Android 9+; iOS 15+; graceful degradation without JS for SSR content |
| **Localisation** | `en-IN` at launch; every user-facing string externalised so a second locale is a translation task, not a code change |
| **Maintainability** | ≥ 70 % line coverage on domain/application layers; cyclomatic complexity gate; zero analyzer warnings |
| **Portability** | Runs on any Docker host; no VPS-specific assumptions; object storage and payment/courier providers all behind interfaces |
| **Redistributability** | A new client deployment requires only: new infrastructure, new `.env`, new tenant record, branding assets, and theme tokens. **No code changes.** This is verified explicitly at Step 30 |
| **Observability** | Every request traceable end-to-end by correlation id across API, worker, and outbound integrations |

---

## 2. Testing Strategy

### 2.1 Test pyramid

| Level | Scope | Tooling | Where it runs |
|---|---|---|---|
| **Unit** | Domain invariants, pricing/GST maths, state machines, validators, pure helpers | xUnit v3 (built-in `Assert`), NSubstitute / Vitest | Every commit |
| **Integration** | Module + real Postgres + real Redis; repositories, handlers, migrations, outbox | xUnit + **Testcontainers** | Every commit |
| **Contract** | API responses match the OpenAPI document; generated client compiles | Schema assertions + codegen diff | Every commit |
| **Component (FE)** | Every UI primitive/pattern incl. keyboard and a11y | Testing Library | Every commit |
| **E2E** | Real browser against a fully containerised stack with stubbed third parties | Playwright | PR to `develop`, nightly, pre-release |
| **Load** | Throughput, latency, soak, spike | k6 | Step 29, then pre-release for risky changes |
| **Security** | SAST, dependency, container, DAST, authz matrix | CI + ZAP | Continuous + weekly |
| **Visual regression** | Screenshot baselines | Playwright | **Only from Step 30** — pointless before the design exists |

> **No third-party assertion library.** Assertions use xUnit's built-in `Assert` (Apache-2.0,
> already a required dependency). FluentAssertions was the original choice here; version 8
> moved to a commercial licence, which conflicts with ADR-009 for a redistributed product. See
> [ADR-012](adr/ADR-012-no-assertion-library.md) — the decision is to depend on nothing that can
> impose a licence later, rather than to pin a version and hope.

### 2.2 Non-negotiable test scenarios

**Money and stock (these must never be wrong):**
1. GST intra-state (CGST+SGST) and inter-state (IGST) on a tax-inclusive price, with rounding.
2. Coupon + tax interaction: discount applied to taxable value, not to the gross, with the
   correct proportional allocation across lines.
3. Multi-vendor order: totals reconcile order → sub-orders → lines exactly.
4. Concurrent add-to-cart and checkout on the last unit → exactly one order, no oversell.
5. Reservation expiry releases stock and writes a ledger entry.
6. Duplicate `place-order` with the same `Idempotency-Key` → one order, identical response.
7. Duplicate/replayed Razorpay webhook → single state transition.
8. Lost webhook → the reconciliation job recovers the order within one cycle.
9. Partial cancellation → correct refund amount, correct tax reversal, correct stock return.
10. Partial return → proportional refund incl. tax and shipping treatment, correct credit note.
11. Vendor ledger for a full cycle (sales, commission, fees, refunds, TCS, TDS) balances to zero
    against the payout.
12. Invoice numbering is gapless per vendor per financial year under concurrent order placement.

**Authorisation:**
13. Vendor A cannot read, update or transition any of Vendor B's data — asserted for every
    vendor-scoped endpoint.
14. A customer cannot access another customer's orders, addresses, returns or invoices.
15. Every endpoint rejects an unauthenticated call unless explicitly public.

**Frontend journeys:**
16. Guest browse → PDP → add to cart → login (cart merges) → checkout → prepaid payment → order
    visible in account, on a 360 px viewport.
17. COD checkout with a non-serviceable PIN → blocked with a clear reason.
18. Payment failure → retry → success, with no duplicate order.
19. Return request → admin approval → pickup → QC → refund, visible to the customer throughout.
20. Vendor dispatch flow: accept → pack → generate label → mark shipped.

### 2.3 Test data

- Deterministic seed dataset: 10 categories, 20 brands, 500 products / ~1,500 variants,
  5 vendors, 3 warehouses, 50 customers, a spread of orders in every state.
- A large-catalogue generator (50k–100k variants) for search and load testing.
- No production data in non-production environments; if ever required, it is anonymised first.

### 2.4 Definition of Done (every step)

- [ ] Acceptance criteria in the step card demonstrably met
- [ ] Unit + integration tests written and passing; coverage threshold held
- [ ] API changes reflected in the OpenAPI document and the generated client
- [ ] Migrations reviewed as SQL, applied and rolled back cleanly on a scratch database
- [ ] Authorisation applied and tested for every new endpoint
- [ ] Logging, metrics and audit entries added for new significant actions
- [ ] No new analyzer/lint warnings; no new critical/high vulnerabilities
- [ ] Documentation updated (`docs/`, ADR if a decision changed)
- [ ] **`IMPLEMENTATION_PLAN.md` status updated, the step's file in `docs/steps/` filled in,
      any deferred test recorded in `TEST_DEBT.md`, and User permission requested**

---

## 3. Observability

### 3.1 Logging

- Serilog → JSON to stdout → Promtail → Loki.
- Mandatory enrichers: `correlationId`, `tenantId`, `userId`, `vendorId`, `traceId`, `spanId`,
  `module`, `environment`, `version`.
- Levels: `Information` for business milestones (order placed, payment captured, shipment
  created), `Warning` for handled anomalies, `Error` for failures needing attention.
  Debug/verbose is off in production and toggleable per module without a redeploy.
- PII masking is applied by a Serilog destructuring policy — masking is not left to the caller.

### 3.2 Metrics

**Technical:** request rate/latency/error by endpoint, DB query duration and connection pool
usage, cache hit ratio, outbox depth and dispatch lag, job success/failure/duration, GC and
thread-pool health, container CPU/memory/disk.

**Business (these are the ones the client will actually watch):** orders per hour, GMV,
payment success rate by method, checkout abandonment, COD share, average order value,
out-of-stock rate at add-to-cart, search zero-result rate, dispatch SLA breaches, NDR rate,
return rate, vendor payout backlog.

### 3.3 Tracing

OpenTelemetry spans across HTTP → handler → database → outbound integration, with the
correlation id propagated into Razorpay and courier calls where the provider supports it, so a
single customer complaint can be traced end-to-end from one order number.

### 3.4 Alerting (initial thresholds, tuned after two weeks live)

| Alert | Condition | Severity |
|---|---|---|
| API error rate | > 2 % over 5 min | Critical |
| API p95 latency | > 1 s over 10 min | Warning |
| Payment success rate | < 90 % over 15 min | Critical |
| Webhook processing lag | > 5 min | Critical |
| Outbox backlog | > 1,000 unprocessed | Warning |
| Orders stuck in `PendingPayment` | > 20 for over 30 min | Critical |
| Job failure | Any job failing 3× consecutively | Warning |
| Disk usage | > 80 % | Warning; > 90 % Critical |
| Postgres connections | > 80 % of max | Warning |
| Certificate expiry | < 14 days | Warning |
| Backup job | Any failure | Critical |
| Payout batch failure | Any | Critical |

Every alert must name an owner and link to a runbook section. An alert nobody acts on gets
deleted, not muted.
