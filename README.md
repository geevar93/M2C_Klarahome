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

**Current status: Step 3 complete — awaiting authorisation for Step 4.**

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
| [Dev setup](docs/dev-setup.md) | Running the local containerised environment |
| [ADRs](docs/adr/README.md) | Architecture decision records |

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

Start the backing services — PostgreSQL, Redis, MinIO, Mailpit and Traefik:

```powershell
./infra/scripts/dev.ps1 up            # Windows
```

```bash
./infra/scripts/dev.sh up             # bash / WSL2 / macOS / Linux
```

| Service | URL |
|---|---|
| **API** | `https://api.klarahome.localhost/api/v1/meta` |
| **API reference (Scalar)** | `https://api.klarahome.localhost/scalar` |
| **API health** | `/health/live` · `/health/ready` |
| MinIO console | `http://127.0.0.1:9001` · `https://minio.klarahome.localhost` |
| Mailpit (captured email) | `http://127.0.0.1:8025` · `https://mail.klarahome.localhost` |
| Traefik dashboard | `https://traefik.klarahome.localhost` |
| PostgreSQL | `127.0.0.1:5432` |
| Redis | `127.0.0.1:6379` |

Full details, credentials, TLS notes and troubleshooting: **[`docs/dev-setup.md`](docs/dev-setup.md)**.

### Working on the backend

```bash
cd src/backend
dotnet build KlaraHome.sln                 # zero warnings is the standard
dotnet test --project tests/KlaraHome.UnitTests/KlaraHome.UnitTests.csproj
dotnet test --project tests/KlaraHome.IntegrationTests/KlaraHome.IntegrationTests.csproj
dotnet test --project tests/KlaraHome.ArchitectureTests/KlaraHome.ArchitectureTests.csproj
```

Run the API outside Docker against the containerised backing services:

```bash
dotnet run --project src/backend/host/KlaraHome.Api --launch-profile api-with-stack
```

Rebuild and restart just the API container:

```bash
docker compose -f infra/compose/docker-compose.dev.yml --env-file .env up -d --build api
```

The Angular workspace is in place (`src/frontend`, see its README). The storefront and admin
containers arrive in Phase F/G.
