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
| [CI pipeline](docs/ci-pipeline.md) | Quality gates, running them locally, branch protection |
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
tools/                 CI gates (ci.ps1 / ci.sh), codegen and database tooling
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
dotnet tool restore                        # once per clone: dotnet-ef and dotnet-coverage
dotnet build KlaraHome.sln                 # zero warnings is the standard
dotnet test --project tests/KlaraHome.UnitTests/KlaraHome.UnitTests.csproj
```

The integration tests start a throwaway PostgreSQL container, so **Docker must be running**.
Without it the database tests skip rather than fail. If `dotnet test` reports `Zero tests ran`
while the suites clearly contain tests, see the troubleshooting table in
[`docs/dev-setup.md`](docs/dev-setup.md) — and prefer the runner below, which does not depend on
the orchestrator that causes it.

Run the API outside Docker against the containerised backing services:

```bash
dotnet run --project src/backend/host/KlaraHome.Api --launch-profile api-with-stack
```

Rebuild and restart just the API container:

```bash
docker compose -f infra/compose/docker-compose.dev.yml --env-file .env up -d --build api
```

### Quality gates — run what CI runs

Every gate CI enforces is one script, so a failure can be reproduced in seconds without a push.

```bash
./tools/ci.sh                     # restore, build, format, test+coverage, lint, audit, builds
./tools/ci.sh format test         # just the two a code change usually trips
./tools/ci.sh package             # container images + Trivy scan
./tools/ci.ps1 -Stage format,test # Windows
```

Backend suites are run as executables rather than through `dotnet test`, and each asserts a
**minimum test count** so a suite that discovers nothing fails instead of passing. The line
coverage floor is **70 %** (`docs/09-nfr-testing-observability.md` §1.4); it is enforced inside the
script, so it holds locally as well as in CI.

Full detail, including the GitHub branch-protection setup the gates need in order to actually block
a merge: **[`docs/ci-pipeline.md`](docs/ci-pipeline.md)**.

### Database and migrations

One PostgreSQL database, one schema per module, one migration history per module. **The API
never migrates on startup** — a one-shot `migrator` job applies migrations before the new
version starts (`docs/03-database-design.md` §7).

```bash
# Apply every module's pending migrations, then run the seeders.
docker compose -f infra/compose/docker-compose.dev.yml --env-file .env   --profile migrate run --rm migrator            # exits 0 on success, 1 on failure

./tools/ef.ps1 add Platform AddTenantTable       # author a migration  (Windows)
./tools/ef.sh  add Platform AddTenantTable       # ...or bash/WSL2
./tools/ef.sh  list Platform                     # what exists, what is applied
./tools/ef.sh  script Platform                   # idempotent SQL -> artifacts/migrations/
```

The generated SQL is what gets reviewed in the pull request, not only the C#.

Migrations can also be applied from the host, without the container:

```bash
cd src/backend
ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=klarahome;Username=klarahome;Password=..."   dotnet run --project host/KlaraHome.Migrator
```

### White-labelling this deployment

Nothing about the business this instance is deployed for is compiled in. A second client is a
second installation with different values, not a fork.

**Configuration decides the deployment's identity** — `.env`, and only this much:

| Variable | Meaning |
|---|---|
| `TENANT_CODE` | Short lowercase code. Stamped on every log line, seeded into `platform.tenants` |
| `TENANT_NAME` | Display name. Becomes the initial store name in the branding settings |
| `TENANT_ID` | The `tenant_id` written on every row. Blank in dev: derived from the code |
| `TENANT_DEFAULT_LOCALE`, `TENANT_DEFAULT_TIMEZONE` | Seeded into the localization settings |

> Set `TENANT_ID` explicitly on anything that holds data. Left blank it is derived from
> `TENANT_CODE`, so **changing the code changes the id** and hides every row already written. The
> `platform-tenant` readiness check reports exactly that rather than letting a replica serve an
> empty catalogue.

**Everything else is data**, in `platform.store_settings`: legal entity, GSTIN, PAN, CIN,
addresses, support and grievance contacts, locale, currency, timezone, return window, COD cap,
free-shipping threshold, colours and logo references. It is editable at runtime and every change is
audited.

```bash
# The whole public configuration document the storefront renders from.
GET  /api/v1/store/config

# Read and replace settings. A section is edited as a whole.
GET  /api/v1/admin/settings
PUT  /api/v1/admin/settings/{branding|legal|support|localization|commerce}

# Feature switches, toggled without a deploy.
GET  /api/v1/admin/feature-flags
PUT  /api/v1/admin/feature-flags/{key}          { "enabled": false }

# The immutable audit trail: who changed what, before and after.
GET  /api/v1/admin/audit-logs?entityType=StoreSetting&entityId=branding

# Reference data.
GET  /api/v1/store/states                       # 28 states + 8 UTs, with GST state codes
GET  /api/v1/store/pincodes/{pincode}           # city/district/state autofill
```

The India Post PIN code dataset is mounted rather than shipped: drop `pincodes.csv` in
[`infra/seed/`](infra/seed/README.md), point `PINCODE_DATA_PATH` at it and re-run the migrator.

### Signing in

Three actor classes, one set of endpoints, mapped under both `/store` and `/admin`
(`docs/07-security-compliance.md` §1).

| Actor | Primary credential | Second factor |
|---|---|---|
| Customer | Mobile number + 6-digit OTP, **Google**, or email + password | Optional |
| Vendor staff | Email + password | **Mandatory for `vendor-owner`** |
| Platform staff | Email + password | **Mandatory for `platform-admin`** |

```bash
# Customers: the code is the credential, and verifying it registers a new number.
POST /api/v1/store/auth/otp/request             { "mobile": "9876543210" }
POST /api/v1/store/auth/otp/verify              { "mobile": "...", "code": "123456" }

# Staff and vendors. A correct password may answer with a challenge rather than a session.
POST /api/v1/admin/auth/login                   { "email": "...", "password": "..." }
POST /api/v1/admin/auth/2fa/enrol               { "challengeToken": "..." }   -> secret + QR URI
POST /api/v1/admin/auth/2fa/verify              { "challengeToken": "...", "code": "123456" }

# The refresh token lives in an HttpOnly cookie and is rotated on every use.
POST /api/v1/admin/auth/refresh
POST /api/v1/admin/auth/logout

# Self-service, on both surfaces.
GET  /api/v1/store/me                           # identity, roles, permissions, profile
GET  /api/v1/store/me/sessions                  # signed-in devices; DELETE one, or all
GET/POST/PUT/DELETE /api/v1/store/me/addresses  # Indian address model, GSTIN per address

# Users and roles. A vendor caller is scoped to their own seller by their token.
GET/POST /api/v1/admin/users                    PUT /api/v1/admin/users/{id}/roles|status
GET/POST /api/v1/admin/roles                    GET /api/v1/admin/permissions
```

**Authorisation is permission-based and deny-by-default.** An endpoint asks for a permission with
`RequirePermission("platform.settings.manage")`, which both records it and attaches the policy that
checks it; an endpoint that declares nothing is closed by the fallback policy rather than opened.
Roles are editable data — a deployment can define its own — and enforcement is never on a role.

**Vendor scope is enforced in the data layer.** A row that belongs to one seller implements
`IVendorScoped`, and a global query filter compares it to the `vendor_id` claim: a vendor user who
asks for another seller's data gets no rows rather than someone else's, and a `{id}` route answers
404 rather than 403 so existence is not leaked.

**The first administrator** is created once, from `AUTH_BOOTSTRAP_EMAIL` and
`AUTH_BOOTSTRAP_PASSWORD`, on a deployment that has none. There is no default credential. Their
first sign-in enrols a second factor before it issues a session. Accounts an administrator creates
afterwards get no password at all — the new user sets their own from an emailed link.

```bash
# Sign in with an identity provider. Two redirects with a server-side token exchange between them.
GET  /api/v1/store/auth/external/providers      # which buttons to render
GET  /api/v1/store/auth/external/google/start   ?returnUrl=
GET  /api/v1/store/auth/external/google/callback   -> 302 back, refresh cookie set
GET/DELETE /api/v1/store/me/external-logins[/{id}]

# Replace a password. Also the way out of an administrator-issued temporary one.
POST /api/v1/admin/auth/password/change         { challengeToken?, currentPassword, newPassword }
PUT  /api/v1/admin/users/{id}/password          { temporaryPassword }
```

### Running without an SMS or email provider

SMS and transactional email are paid, and a deployment may run before they exist. Four flags in
`platform.feature_flags` turn the features that need them off **at runtime** — nothing is removed
from the code, and the day a provider is paid for the feature returns with no deploy (ADR-014):

| Flag | Off means |
|---|---|
| `identity.mobile-otp-login` | Customers sign in with Google, or email + password |
| `identity.email-verification` | An address stays unverified unless a provider asserted it |
| `identity.password-reset-email` | An administrator issues a temporary password instead |
| `identity.external-login` | The external sign-in surface disappears |

```bash
PUT /api/v1/admin/feature-flags/identity.mobile-otp-login   { "enabled": false }
```

A disabled feature answers `404 FEATURE_DISABLED`. **Google sign-in is what makes this workable**:
it is free, it needs no app review for `email`/`profile`, and the address it returns arrives already
verified — a stronger assertion than our own verification link would have been. Set
`AUTH_GOOGLE_CLIENT_ID` and `AUTH_GOOGLE_CLIENT_SECRET`; a provider with no client id is left off
the sign-in page rather than offered and broken.

External sign-in is **for customers only**. Staff and vendor users keep a password and a mandatory
second factor, so a compromised Google account cannot reach `platform-admin` — which is also why an
administrator has to be able to issue a temporary password while email is off. That password buys a
`password-change-required` challenge rather than a session, ends every existing session, and is
audited with the administrator's name on it. It is a knowingly weaker control than a reset link and
is withdrawn when email delivery returns.

> **While `identity.mobile-otp-login` or the email flags are on and Step 8 has not landed,** codes
> and links are written to the API log by `LoggingOtpDispatcher` — which is how you sign in locally
> and exactly what `docs/07-security-compliance.md` §3 forbids in production. The Notifications
> module replaces it; turning the flags off is what makes a deployment safe before it does.

The Angular workspace is in place (`src/frontend`, see its README). The storefront and admin
containers arrive in Phase F/G.
