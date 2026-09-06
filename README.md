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

**The v1 courier is Shiprocket, and it is named in configuration and nowhere else.** Each adapter
publishes its own key — `shiprocket`, `manual`, and whatever comes next — and `SHIPPING_PROVIDER`
picks the one that books new parcels. A parcel records the adapter that booked it, so changing
courier leaves the ones already in flight tracking, labelling and cancelling through the courier
that actually holds them. Adding Delhivery later is one class and one key (ADR-018).

**It ships with no courier account, and that is the correct state for a fresh deployment.** With
`SHIPPING_PROVIDER` blank the manual adapter takes over: an operator books at a courier counter,
types the air waybill in, and the label prints, the order ships, the timeline fills in and the cash
reconciles exactly as they would through an API. Turning Shiprocket on is five values in `.env`
and a restart:

```bash
SHIPPING_PROVIDER=shiprocket           # the adapter key; blank or unknown falls back to `manual`
SHIPPING_BASE_URL=https://apiv2.shiprocket.in   # also the outbound allow-list: no other host is reachable
SHIPPING_API_USER=...                  # the API user's email; exchanged for a token that expires in days
SHIPPING_API_SECRET=...                # that user's password
SHIPPING_WEBHOOK_SECRET=...            # yours to choose; paste the same value into their dashboard
```

Then add the webhook in Shiprocket's dashboard, pointing at
`<public API origin>/api/v1/webhooks/shipping/shiprocket`. Shiprocket proves origin with that secret
in an `x-api-key` header rather than an HMAC, so the value above is the whole of the proof — treat it
like a password.

**Where the store delivers is not an environment variable.** *Whether* a courier can reach a PIN code
is Shiprocket's answer, cached; *whether this store will sell there* is a policy an operator edits in
admin, and the two are refused with different codes because one is reversible in a settings screen
and the other is not. It ships restricted to **Hyderabad** — cities `Hyderabad` and `Secunderabad`,
PIN prefix `500` — and opening another city is typing its prefix into a field. Turning the policy off
restores national trading.

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

### Sending things back

Returns are a first-class flow rather than an exception path. The module decides *that* goods are
coming back and *what that is worth*; it never sends money or moves stock itself. Both go through
the module that owns them — refunds through the Payments maker–checker control, units through the
Inventory ledger — over four seams this platform owns.

**How long a shopper has is resolved from three policies in a fixed order**: the product's own
window, frozen onto the order line at placement; then the seller's promise; then the store's
default. The first with an opinion wins — deliberately not the shortest, because the shortest is not
what the shopper read. A product sold as non-returnable is the one veto, because that was a
mandatory listing disclosure.

```bash
GET  /api/v1/store/sub-orders/{id}/returnable    # what is left, what it is worth, until when
POST /api/v1/store/returns                       # ask, with a reason and optional photographs
POST /api/v1/store/returns/{id}/cancel           # withdraw, up to the moment a courier has it
GET  /api/v1/admin/returns?status=Received       # the queue, and what is uninspected
POST /api/v1/admin/returns/{id}/approve          # agree, and say whether a courier collects
POST /api/v1/admin/returns/{id}/schedule-pickup  # a reverse parcel, or a hand-typed waybill
POST /api/v1/admin/returns/{id}/receive          # booked in. Nobody has opened the box yet
POST /api/v1/admin/returns/{id}/qc               # graded: restock, scrap or quarantine, per line
POST /api/v1/admin/returns/{id}/refund           # to the original instrument, or to store credit
GET  /api/v1/admin/credit-notes?financialYear=2026-27   # what a GST return is prepared from
```

**A reverse pickup is an ordinary parcel** with its two addresses inverted — booked by the same
adapters, tracked by the same webhook receiver, visible in the same list. A deployment with no
logistics account takes a waybill an operator typed in, exactly as a forward dispatch does.

**The credit note is raised whether or not money moves**, and before the money is asked for. Section
34 of the CGST Act is about the supply, not the card: a refund to store credit still reverses it,
and a note against a refund that later failed is something an operator can act on — whereas a refund
paid against a supply nobody reversed leaves the seller owing tax on goods they no longer have. Its
number is gapless per seller per financial year, from a counter row rather than a sequence.

Who pays the return freight, what is approved without a human, where the money goes by default and
what becomes of the goods are all edited at `PUT /admin/settings/returns` — no deploy.

### Paying the sellers

A seller's balance is **not a column anywhere.** It is `Σ credits − Σ debits` over an append-only
ledger, so it cannot drift from its own history, and a correction is a reversing entry rather than
an edit. Every entry names the document that caused it, and carries a key **derived from that
document** with a unique index behind it — which is what makes at-least-once event delivery safe on
a table that moves money: a redelivered "this parcel arrived" collides instead of paying twice.

**Nothing is earned before the money is the platform's.** A prepaid sale earns when the parcel is
delivered. A cash-on-delivery sale earns when the courier *remits*, which is days later — crediting
a seller when the cash is in a van would be paying out of pocket. What the platform charged was
decided when the order was placed and is read off the frozen order line, so a commission plan
changed this morning does not re-price last month.

**The two statutory deductions are taken on two different numbers**, and using one base for both is
the single commonest way this arithmetic goes wrong. TCS under section 52 of the CGST Act is
collected on the *net value of taxable supplies* — the consideration, which excludes the GST inside
the price. TDS under section 194-O is deducted from the *gross amount of sales*, which includes it.
Section 206AA's higher rate applies to a seller who has furnished no PAN. Both rates are store
settings, because a rate changes by a notification in the Gazette and a deployment that needed a
rebuild to follow one would file a wrong return.

```bash
GET  /api/v1/admin/vendors/{id}/ledger           # opening balance, movements, closing balance
GET  /api/v1/admin/vendors/{id}/ledger/export    # the same as a spreadsheet
POST /api/v1/admin/settlements/cycles/close      # total a period and apply TCS and TDS
POST /api/v1/admin/settlements/adjustments       # a correction. An append, and it needs a reason
POST /api/v1/admin/payout-batches                # build a run from closed periods. Sends nothing
POST /api/v1/admin/payout-batches/{id}/approve   # never by the person who raised it
POST /api/v1/admin/payout-batches/{id}/process   # hand it to the gateway. Resumable
GET  /api/v1/admin/reports/tcs-tds/export        # for the GSTR-8 and the 26Q/27EQ filings
GET  /api/v1/admin/reports/platform-revenue      # exactly what the sellers were charged
```

**A settlement period is closed on a hold, not on a date.** It is half-open and drawn in India
Standard Time, so consecutive periods neither overlap nor leave a gap; it becomes closable its hold
in days after it ends, which gives a shopper the store's whole return window before the seller is
paid for goods they might send back. A cycle sweeps every entry older than its end, including one
posted late against a period already frozen, so every entry lands in exactly one cycle.

**Money leaving needs two people.** A payout batch is built by one person and approved by another —
refused in the handler, again in the aggregate, and a third time by a database `CHECK`, which is
more places than any other rule in this platform gets. Sending is deliberately not atomic: each
transfer is written down the moment the gateway answers, so a process that dies halfway leaves four
hundred payments in a known state rather than an unknown one.

Razorpay Route and RazorpayX sit behind one interface this platform owns, keyed by the rail they
are. **A deployment with neither configured still works**: periods close, batches are built and
approved, the ledger says to the paisa what every seller is owed, and only the transfer refuses —
with a named `503`, not a pretence that money moved.

### Search and browse (Step 19)

The storefront's product listing is served from a **denormalised projection with one row per
variant**, carrying the offer that won its buy box — not one row per listing, because a marketplace
shows one offer per sellable thing and a page with four rows for the same cushion is not a search
result. Which offer won is answered by the Catalog module with the operator's configured rule, so a
search result and the product page it links to can never name two different sellers.

```bash
GET  /api/v1/store/products        ?q= &category= &brand= &minPrice= &attr.color=beige &sort=
GET  /api/v1/store/search/suggest  ?q=          # products, popular searches, brands, categories
POST /api/v1/store/search/click                 # which result was opened. Anonymous, 204
GET  /api/v1/admin/search/queries/zero-results  # searched for, not found. The buying team's list
POST /api/v1/admin/search/synonyms              # "settee" also means "sofa". Takes effect in a minute
POST /api/v1/admin/search/index/rebuild         # bounded and resumable; says where it reached
```

The free-text index is a **generated, stored `tsvector`** with four weights — the product name
outranks the brand, which outranks the category, which outranks an attribute value — so there is no
write path, the bulk rebuild included, that can leave it stale. A search that matches nothing is
retried against a **trigram** index, because "cushin" and "cushion" share no stem and eight
trigrams; the storefront is told it was a correction rather than shown results that look wrong.

**Facet counts are computed with each facet's own filter lifted and every other filter applied.** It
is the only definition that agrees with what happens when a shopper clicks one: somebody who has
chosen beige still has to be told how many creams there are, *within* the price band they also
chose. Two filtered attributes get a branch each, because "how many creams in size M" and "how many
size Ls in beige" are two different result sets.

The index owns no truth. Every column is a copy of something Catalog, Pricing, Inventory or Vendors
owns; six integration events keep it current within seconds, and the whole table can be rebuilt from
the catalogue at any time — so a wrong row is an operational nuisance rather than a loss. The one
original record is the **query log**, partitioned by month and retained a year: what a shopper typed
exists nowhere else, and the queries that returned nothing are the most valuable rows in it. It is
behind a switch, because a search term is personal data under the DPDP Act.

PostgreSQL full text is the engine, behind an interface this platform owns. A dedicated engine —
Meilisearch, OpenSearch — is a class, a configuration key and a feature flag away (ADR-019), and
unlike the gateway, the courier and the payout rail there is **no degraded mode here at all**:
search needs no credentials, so it works in every deployment on the day it is installed.

### Reviews, Q&A and saved intent (Step 21)

**A review exists if and only if the customer received the thing.** Not ordered it and not paid for
it — received it, resolved through `IOrderPurchases` by the module that owns the state machine
deciding what *delivered* means. Every identifier on the review comes off what that contract
returned rather than off the request, so a caller cannot write a five-star review of anything by
quoting one line they genuinely bought. One review per order line is a **unique index**, not a check
in a handler: two submissions racing each other is the ordinary case on a slow connection.

```bash
GET  /api/v1/store/products/{id}/reviews             ?rating= &withImages= &sort=helpful
GET  /api/v1/store/products/{id}/rating              # the average and the 1..5 histogram
GET  /api/v1/store/products/{id}/reviews/eligibility # which purchases may still be reviewed
POST /api/v1/store/products/{id}/reviews             { orderLineId, rating, title, body, images[] }
POST /api/v1/store/reviews/{id}/helpful              { isHelpful }   # a row per voter, not a counter
POST /api/v1/store/wishlist/items                    { variantId }
POST /api/v1/store/stock-subscriptions               { variantId, kind, targetPrice? }
GET  /api/v1/admin/reviews                           ?status=Pending  # oldest first; the queue
POST /api/v1/admin/reviews/{id}/moderate             { approve, note }
```

**Moderation is on by default and it is a configuration value rather than a store setting.**
Publishing user content unreviewed puts an intermediary in a different position under the IT Rules,
which is not a switch a merchandiser should be able to flip from an admin screen on a Friday
afternoon. A seller may reply to a review of their own sale and can never remove one — a seller who
could refuse reviews of their own goods would be curating their own rating.

A **rating is recomputed in full** on every change and published as an event carrying the aggregate
rather than a delta, so a rejection, an edit, an upheld complaint and a redelivered message all
produce the same correct number in Catalog, Vendors and the search index. That is what finally fills
the `RatingAverage` that has been null since Step 19.

A wishlist **stores no price**: a saved item is priced from today's buy box at read time, because a
list quoting last month's figure makes the "add to basket" button a surprise. A back-in-stock alert
fires **once** and is Marketing rather than transactional — nobody ordered anything, and treating
interest in one product as consent to be messaged is what a preference centre exists to prevent.

### The numbers the business runs on (Step 21)

Reporting owns no business rule and may change nothing. It also may not read another module's
tables, so it **keeps its own facts**: seven tables written by fourteen integration-event
subscriptions, one row per transactional row, denormalised at the moment the event lands with the
category, the seller and the payment method already on it. A report is then a filtered aggregation
over a single table with **no join anywhere in the module** — which is also why the numbers
reconcile, because every fact row corresponds to something that really happened.

```bash
GET /api/v1/admin/reports                       # the declared catalogue: columns and groupings
GET /api/v1/admin/reports/sales-by-day          ?from= &to=
GET /api/v1/admin/reports/gmv-vs-net-revenue    ?from= &to=      # GMV, net, commission, take rate
GET /api/v1/admin/reports/stock-ageing          ?groupBy=bucket  # 0-29, 30-59, 60-89, 90-179, 180+
GET /api/v1/admin/reports/cod-vs-prepaid        ?from= &to=      # by value, not by count
GET /api/v1/admin/reports/sales-by-day          ?format=csv      # produces a run; returns the run
GET /api/v1/admin/report-runs/{id}/download     # a signed link, fifteen minutes
POST /api/v1/admin/report-schedules             { reportKey, frequency, hourUtc, recipients[] }
```

There are **no materialised views and no rollups** (ADR-021). `03-database-design.md` §4.18 asked
for seven `mv_*` views, and a materialised view named `reporting.mv_daily_sales` has to select from
`orders`, `payments` and `catalog` — a cross-schema read with a different word in front of it, and
one the architecture tests cannot see because it is declared in DDL. Thirteen reports, served as
data the admin app reads back, so the report picker, the column headings and the CSV all come off
one declaration.

The one number no event carries is **how long the stock on a shelf has been there** — a stock level
says what the balance *is*, never when the units making it up arrived. That is a nightly snapshot
through `IInventoryAgeing`, a read-only seam over Inventory's ledger, and it is a series rather than
a state on purpose: "is that getting better or worse" is the question a buying team actually asks.

A scheduled export is CSV with a **byte-order mark** and its formula characters defused — a cell
beginning `=` is executed by a spreadsheet when the file is opened, and the values include product
names a seller supplied. It goes into the private bucket rather than the media library, which
identifies what it accepts by sniffing magic numbers that CSV does not have, and it is emailed as a
short-lived link rather than an attachment.

There is **no back-fill**. Reporting begins the day it is deployed with `reporting.fact-ingest` on,
which makes that the one feature flag in the platform that is not safe to leave off.

The Angular workspace is in place (`src/frontend`, see its README). The storefront and admin
containers arrive in Phase F/G.
