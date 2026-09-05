# Klara Home

Multi-vendor e-commerce platform for the **Indian** market, built to be **re-distributable** to
other businesses on separate infrastructure.

Angular (mobile-first, SSR) · .NET 10 · PostgreSQL · Docker on VPS

---

## ⛔ How this project is built

Execution follows the gated plan in **[`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md)**.

After each step: implementation **stops**, the plan is updated with status and outcome, and
the User is asked for explicit permission before the next step begins. Read
[`CONTRIBUTING.md`](CONTRIBUTING.md) before making any change.

**Current status: Step 1 complete — awaiting authorisation for Step 2.**

---

## Documentation

Start with [`docs/README.md`](docs/README.md). The specification set:

| Doc | Subject |
|---|---|
| [IMPLEMENTATION_PLAN](docs/IMPLEMENTATION_PLAN.md) | The 33-step gated plan and live status |
| [01 Architecture](docs/01-architecture.md) | Containers, modules, technology choices, ADRs |
| [02 Domain model](docs/02-domain-model.md) | Bounded contexts, aggregates, state machines |
| [03 Database](docs/03-database-design.md) | PostgreSQL schema, indexes, migrations |
| [04 API](docs/04-api-specification.md) | REST conventions, endpoints, webhooks |
| [05 Frontend](docs/05-frontend-architecture.md) | Angular workspace, SSR, mobile-first rules |
| [06 Infrastructure](docs/06-infrastructure-devops.md) | Docker, VPS, CI/CD, backups |
| [07 Security](docs/07-security-compliance.md) | AuthN/Z, DPDP, GST and marketplace compliance |
| [08 Integrations](docs/08-integrations.md) | Razorpay, logistics, SMS/email, storage |
| [09 NFR & testing](docs/09-nfr-testing-observability.md) | Targets, test strategy, alerting |
| [10 Design placeholder](docs/10-design-system-placeholder.md) | Neutral tokens until Step 30 |

---

## Repository layout

```
docs/                  Specification set (read first)
src/backend/           .NET 10 modular monolith
  host/                Api, Worker, Migrator
  shared/              SharedKernel, Infrastructure, Contracts
  modules/             17 bounded-context modules
  tests/               Unit, Integration, Architecture, Load
src/frontend/          Nx workspace (Angular storefront + admin)
infra/                 Dockerfiles, compose, Traefik, observability, scripts
tools/                 Codegen and database tooling
.githooks/             Conventional Commit enforcement
```

---

## Prerequisites

| Tool | Required | Status on this machine |
|---|---|---|
| .NET SDK | 10.0.x | ✅ 10.0.203 |
| Git | 2.40+ | ✅ 2.46.1 |
| Docker | 24+ with Compose v2 | ✅ 29.4.0 |
| Node.js | **22.22.3+ / 24.15+** (Angular requirement) | ✅ 24.20.0 |

## Getting started

```bash
git clone <repo>
cd KlaraHome
git config core.hooksPath .githooks   # enable commit message validation
dotnet --version                      # expect 10.0.x (pinned in global.json)
```

The Angular workspace is in place (`src/frontend`, see its README). The local containerised
environment (Postgres, Redis, MinIO, Mailpit, Traefik) arrives in **Step 2**; the backend
solution in **Step 3**.
