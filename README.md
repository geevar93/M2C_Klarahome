# Klara Home

Multi-vendor e-commerce platform for the **Indian** market, built to be **re-distributable** to
other businesses on separate infrastructure.

Angular (mobile-first, SSR) · .NET 10 · PostgreSQL · Docker on VPS

---

## ⛔ How this project is built

Execution follows the gated plan in **[`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md)**.

After each step: implementation **stops**, the tracker and that step's file in
[`docs/steps/`](docs/steps/) are updated with status and outcome, and the User is asked for
explicit permission before the next step begins. Read
[`CONTRIBUTING.md`](CONTRIBUTING.md) before making any change.

Steps 9-28 run as an **MVP build sprint** — production code first, integration tests batched
into Step 29 and tracked in [`docs/TEST_DEBT.md`](docs/TEST_DEBT.md). See
[`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md) section 3.

**Current status: Step 8 complete — awaiting authorisation for Step 9.**

---

## Documentation

Start with [`docs/README.md`](docs/README.md). The specification set:

| Doc | Subject |
|---|---|
| [IMPLEMENTATION_PLAN](docs/IMPLEMENTATION_PLAN.md) | The gated plan and live status. Per-step detail is in [`docs/steps/`](docs/steps/); the ledgers are [`PARKING_LOT`](docs/PARKING_LOT.md), [`CHANGE_LOG`](docs/CHANGE_LOG.md) and [`TEST_DEBT`](docs/TEST_DEBT.md) |
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

> **One-time codes are no longer written to the log.** Step 8 replaced `LoggingOtpDispatcher`: a
> code is now rendered from an editable template and handed straight to a provider, and the row that
> records the attempt keeps neither the body nor any variable's value. Locally there is still no SMS
> account, so the message goes to Mailpit instead — see below.

### Media and documents

`POST /api/v1/admin/media` accepts a file and answers with its id, its URL and the responsive
renditions the storefront should ask for:

```bash
curl -k -X POST https://api.klarahome.localhost/api/v1/admin/media \
     -H "Authorization: Bearer $TOKEN" -F file=@hero.png
# 201 { "id": "...", "url": "https://s3.../hero.png",
#       "variants": [ { "name": "thumb", "width": 160, "url": "https://img.../rs:fit:160:0/..." }, ... ] }
```

- **What a file is, is decided by its bytes.** The declared content type and the filename both come
  from the caller, so neither is evidence: a `.png` that is really a PHP script is refused, and the
  extension a download is offered under comes from the content.
- **No rendition is stored.** The variant URL is an instruction that imgproxy carries out and caches,
  so re-tuning `Media__VariantWidths` changes what the next page load asks for and needs no
  regeneration job. **Set `IMGPROXY_KEY` and `IMGPROXY_SALT` in any deployed environment** — unsigned,
  imgproxy will resize for anybody who finds it, on this deployment's bandwidth and domain.
- **A private file has no URL.** Invoices, KYC documents and labels go to `docs-private` and are
  reached only through `GET /admin/media/{id}/link`, which mints a short-lived signed URL *after*
  the caller's authorisation has been checked.
- **Nothing scans uploads yet.** `IVirusScanner` has one implementation that records
  `scanState: "Skipped"` rather than pretending. Set `Media__RequireVirusScan=true` and uploads are
  refused while that is true, which is the state a deployment handling KYC documents should be in.

Generated documents — invoices, credit notes, labels — go through `IDocumentStore`: rendered with
PDFsharp and MigraDoc (ADR-015), stored privately, registered like any other file.

### Notifications

A caller names an **event**, never a message. What that event says, on which channels, in which
language, is a template an operator edits in `GET/PUT /admin/notification-templates` without a
deploy.

```bash
# Prove a channel works end to end, through the real pipeline.
POST /api/v1/admin/notifications/test   { "channel": "Email", "to": "you@example.com" }

# What was sent, what was not, and why. Recipients are masked.
GET  /api/v1/admin/notifications?status=Suppressed
```

**A channel with no provider is suppressed, not failed (ADR-017).** Every message is rendered,
queued and recorded whether or not anybody can carry it; one that nobody could send ends
`Suppressed` with a reason — `NoProvider`, `ChannelDisabled`, `OptedOut`, `NoRecipient`,
`NoTemplate` — which does not retry and is not an incident. That is what lets this platform be
operated before the SMS row of `docs/08-integrations.md` §7 is complete, and what makes "what did we
fail to tell people" a query rather than a guess.

| Channel | State here | Notes |
|---|---|---|
| Email | ✅ Works | SMTP; Mailpit locally, any host in production. Needs no paid account |
| SMS | ⛔ No account | Recorded as `Suppressed / NoProvider` in production. Outside Production it goes to Mailpit so a developer can complete a mobile sign-in |
| WhatsApp | ⛔ No account | Same, and no adapter written |
| In-app | ✅ Works | The delivery log is the inbox; there is no provider to configure |

The queue is drained by the **worker**, which is the only host with `Notifications__DispatcherEnabled`
set: failures retry with exponential backoff and jitter, and a permanent refusal — a rejected mailbox
— is not retried at all. A **one-time code is the exception**: it is sent inline, because it cannot
wait for a poll and its body must not be stored for one.

When an SMS account is bought: write the adapter, set `Sms__Provider`, and register a DLT template id
against each SMS template. An active SMS template with no id is refused, because an Indian operator
**drops** a non-conforming message rather than rejecting it.

### Taking payments

Prepaid goes through **Razorpay hosted checkout**, and no card data ever reaches these servers
(ADR-008 — the merchant stays in PCI-DSS SAQ-A scope). Cash on delivery is a second provider behind
the same interface, with nothing on the other side of it.

**It ships with no credentials, and that is the correct state for a fresh deployment.** With
`RAZORPAY_KEY_ID` blank, the adapter reports itself unusable, a prepaid `place-order` answers
`503 PAYMENT_PROVIDER_UNAVAILABLE` naming the reason, and cash on delivery works end to end. Turning
online payment on is three values in `.env` and a restart:

```bash
RAZORPAY_KEY_ID=rzp_test_xxxxxxxx      # publishable; the browser widget needs it
RAZORPAY_KEY_SECRET=xxxxxxxx           # never leaves the server
RAZORPAY_WEBHOOK_SECRET=xxxxxxxx       # yours to choose; paste the same value into the dashboard
```

Then add the webhook in the Razorpay dashboard, pointing at
`<public API origin>/api/v1/webhooks/razorpay`, subscribed to `payment.authorized`,
`payment.captured`, `payment.failed`, `order.paid`, `refund.created`, `refund.processed`,
`refund.failed` and `settlement.processed`.

**Order truth comes from the webhook plus an API re-fetch, and from nothing else.** The browser
callback is a UX signal: `POST /store/payments/orders/{id}/verify` checks the gateway's handshake
signature, records the attempt and confirms nothing. A webhook body is likewise never trusted on its
own — the signature proves who sent a claim, not that the claim is current — so every event is
resolved to a payment and then re-read from the gateway's API before an order moves.

Three loops in the **worker** make that survivable when a webhook goes missing:

| Loop | Cadence | What it is for |
|---|---|---|
| Gateway event processor | 5 s | Applies stored webhooks, retries failures, dead-letters what will never work |
| Reconciliation | 15 min | Asks the gateway about collections open longer than 20 minutes. **Recovers a lost webhook** |
| Settlement ingestion | daily | Imports the payout reports and matches them line by line against what was captured |

**A mismatch is alerted and never repaired.** A short capture does not confirm an order; a settled
line that does not agree with our figure becomes an addressable row and a
`PaymentMismatchDetected` event. Rewriting our number to match the gateway's would destroy the only
evidence the two ever disagreed.

```bash
GET  /api/v1/admin/payments?q=KH-2609-000184      # every collection against an order
POST /api/v1/admin/payments/{id}/sync             # re-read from the gateway. The repair for a lost webhook
POST /api/v1/admin/payments/{id}/refunds          # Idempotency-Key required
POST /api/v1/admin/refunds/{id}/approve           # the second signature, above the threshold
GET  /api/v1/admin/gateway-events?status=DeadLettered   # webhooks that could not be applied
POST /api/v1/admin/cod-collections/remit          # a courier's remittance, matched in one batch
```

**Refunds above a threshold need two people.** The value lives in the `payments` store-settings
section rather than in configuration, because it is a governance decision a business revisits — and
a refund whose approver is the person who raised it is refused by the handler *and* by a check
constraint.

### Moving parcels

Delivery is priced from **this platform's own rate card** and carried by whatever courier an
aggregator picks. The two figures are deliberately different numbers and both land on the parcel:
`freight_charged` is what the shopper paid, `freight_cost` is what the aggregator invoiced, and the
margin on delivery is the difference. Weight is the same story — what the packer weighed and what the
courier billed for are both kept, because that is what a weight dispute is argued from.

**It ships with no aggregator account, and that is the correct state for a fresh deployment.** With
`SHIPPING_PROVIDER` blank the manual adapter takes over: an operator books at a courier counter,
types the air waybill in, and the label prints, the order ships, the timeline fills in and the cash
reconciles exactly as they would through an API. Turning an aggregator on is four values in `.env`
and a restart:

```bash
SHIPPING_PROVIDER=aggregator           # anything non-blank selects the aggregator adapter
SHIPPING_BASE_URL=https://...          # also the outbound allow-list: no other host is reachable
SHIPPING_API_USER=...                  # exchanged for a token; SHIPPING_API_KEY works for a static one
SHIPPING_API_SECRET=...
SHIPPING_WEBHOOK_SECRET=...            # yours to choose; paste the same value into their dashboard
```

Then add the webhook in the aggregator's dashboard, pointing at
`<public API origin>/api/v1/webhooks/shipping/aggregator`.

**A confirmed sub-order opens a parcel; a human closes it.** The draft appears on the pick list with
its lines, its destination and its cash figure already on it, and nothing is asked of a courier until
somebody has put the box on a scale — a booking made on a guessed weight is a dispute with a courier
who has both the parcel and the invoice.

**Serviceability is never asked on a request path.** A product page and a checkout read a cached
table; a nightly job refreshes it. A live API call there would make every page view depend on
somebody else's uptime.

Three loops in the **worker**, for the same reason payments has three:

| Loop | Cadence | What it is for |
|---|---|---|
| Courier event processor | 5 s | Applies stored webhooks, retries failures, dead-letters what will never work |
| Tracking poll | 30 min | Asks couriers about parcels silent for 24 h. **Recovers a lost webhook** |
| Serviceability refresh | daily | Re-asks about the PIN codes whose answers are oldest |

```bash
GET  /api/v1/store/shipping/serviceability/560001   # anonymous, cached, never calls a courier
POST /api/v1/admin/sub-orders/{id}/shipments        # pack, weigh and book in one call
GET  /api/v1/admin/shipments/pick-list              # what is waiting to be packed, soonest first
GET  /api/v1/admin/shipments/{id}/label             # the courier's label, or ours; a signed link
POST /api/v1/admin/shipments/{id}/dispatch          # the courier has it. This ships the order
POST /api/v1/admin/manifests                        # the handover sheet a driver signs
GET  /api/v1/admin/ndr                              # failed deliveries waiting for a decision
POST /api/v1/admin/shipping/cod-remittances         # a courier's cash, matched by air waybill
```

**A courier's word never sets a status directly.** Every scan goes through the shipment machine and
then through the ordering machine over `IOrderFulfilment`, so a webhook, the polling fallback and an
operator's click all write the same timeline. A scan the machine has no edge for — a delivery on a
parcel already returned — is recorded, marked unapplied, and left for a human rather than discarded.

The Angular workspace is in place (`src/frontend`, see its README). The storefront and admin
containers arrive in Phase F/G.
