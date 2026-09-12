# Local Development Environment

> Owner: Solution Architecture · Introduced at **Step 2** of
> [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md)

One command brings up every backing service the platform needs — PostgreSQL, Redis, MinIO,
Mailpit and Traefik — in the same shapes they will take on the VPS. Application containers
(`api`, `worker`, `storefront`, `admin`) are **not** part of this stack yet; they arrive with
Step 3.

---

## 1. Prerequisites

| Tool | Version | Check |
|---|---|---|
| Docker Desktop (with Compose v2) | 24+ | `docker compose version` |
| .NET SDK | 10.0.x (pinned in `global.json`) | `dotnet --version` |
| Node.js | 22.22.3+ or 24.15+ | `node --version` |
| Git | 2.40+ | `git --version` |

**Windows:** Docker Desktop must be running and using the **WSL 2** backend
(*Settings → General → Use the WSL 2 based engine*). Give WSL at least 4 GB of RAM in
`%UserProfile%\.wslconfig`:

```ini
[wsl2]
memory=6GB
processors=4
```

---

## 2. Start the stack

```powershell
git clone <repo> && cd KlaraHome
git config core.hooksPath .githooks     # once per clone
./infra/scripts/dev.ps1 up              # Windows
```

```bash
./infra/scripts/dev.sh up               # bash / WSL2 / macOS / Linux
```

Or drive Compose directly:

```bash
docker compose -f infra/compose/docker-compose.dev.yml --env-file .env up -d
```

The helper script is thin — it resolves the repository root, passes `.env` when one exists,
waits for every service to report **healthy**, and prints the URLs.

| Command | Effect |
|---|---|
| `dev up` | Start everything and wait for health |
| `dev status` | `docker compose ps` |
| `dev logs [service]` | Follow logs |
| `dev restart [service]` | Restart |
| `dev down` | Stop; **volumes are kept** |
| `dev reset` | Stop and **delete all data** (asks for confirmation) |
| `dev urls` | Reprint the service URLs |

---

## 3. What is running

| Service | Image | Direct (always works) | Via Traefik |
|---|---|---|---|
| PostgreSQL 18 | `postgres:18.6-alpine` | `127.0.0.1:5432` | — |
| Redis 8 | `redis:8.10.1-alpine` | `127.0.0.1:6379` | — |
| MinIO — S3 API | `minio/minio:RELEASE.2025-09-07T16-13-09Z` | `http://127.0.0.1:9000` | `https://s3.klarahome.localhost` |
| MinIO — console | same | `http://127.0.0.1:9001` | `https://minio.klarahome.localhost` |
| Mailpit — SMTP | `axllent/mailpit:v1.31.0` | `127.0.0.1:1025` | — |
| Mailpit — UI | same | `http://127.0.0.1:8025` | `https://mail.klarahome.localhost` |
| imgproxy | `ghcr.io/imgproxy/imgproxy:v3.30` | — | `https://img.klarahome.localhost` |
| Traefik dashboard | `traefik:v3.6.25` | — | `https://traefik.klarahome.localhost` |
| **API** | `klarahome/api:dev` (built locally) | — | `https://api.klarahome.localhost` |
| **Worker** | `klarahome/worker:dev` (built locally) | — | none, by design |
| **Storefront** | `klarahome/storefront:dev` (built locally) | — | `https://klarahome.localhost` |
| **Admin** | `klarahome/admin:dev` (built locally) | — | `https://admin.klarahome.localhost` |

The API deliberately publishes **no host port**: Traefik is the only way in, exactly as on the
VPS. The **worker** publishes no port and carries no Traefik label at all: nothing may reach it. It
drains the transactional outbox and the notification queue, and it is the only host with
`Outbox__Enabled` and `Notifications__DispatcherEnabled` set — two hosts polling the same queues
would contend for the same rows for no gain. `docker logs klarahome-dev-worker` is where a message
that will not send explains itself.

The API's useful endpoints:

| Endpoint | Purpose |
|---|---|
| `/health/live` | Liveness. Answers while the process is running; used by the container HEALTHCHECK |
| `/health/ready` | Readiness. Also probes PostgreSQL, Redis and both object-storage buckets |
| `/api/v1/meta` | API version, build version, environment, server time |
| `/openapi/v1.json` | The OpenAPI 3.1 document — the contract the Angular client is generated from |
| `/scalar` | Browsable API reference. Non-production only |
| `/api/v1/diagnostics/*` | Development-only proofs of the error contract (see §6) |

Default development credentials (all overridable in `.env`):

```
Postgres   klarahome / klarahome_dev_password   database: klarahome
Redis      password: klarahome_dev_password
MinIO      klarahome / klarahome_dev_password
```

These are **development-only defaults committed on purpose**, so a fresh clone runs with no
setup. Production credentials never live in a file in this repository — see
[`06-infrastructure-devops.md`](06-infrastructure-devops.md) §4.1.

**The two Angular apps are built once and configured at start-up.** There is no
`environment.prod.ts` and no per-environment build: the browser reads `/config.json` before the
app bootstraps, and in a container that file is written from the `KH_*` variables by
`infra/docker/write-runtime-config.sh`. The storefront also renders on Node, where there is no
file to fetch, so the renderer reads the same variables directly — the names match on both
sides because the two must agree. Rebuild after a frontend change:
`docker compose -f infra/compose/docker-compose.dev.yml --env-file .env up -d --build storefront admin`.

**The storefront's renderer fetches the API by its public hostname**, so that a page rendered on
the server and the transfer cache the browser reads it out of are keyed alike. Two things in the
dev compose file make that work and are both replaced by real infrastructure at Step 32: Traefik
carries network aliases for the four public hostnames, because Docker's DNS has never heard of
`api.klarahome.localhost` and would otherwise answer NXDOMAIN; and the storefront sets
`NODE_TLS_REJECT_UNAUTHORIZED=0`, because Node — unlike a browser — is never offered the chance
to accept a self-signed certificate. Issue a locally-trusted certificate (§5) and the second one
can go.

Buckets are created automatically by the one-shot `minio-init` container:
`media-public` (anonymous read) and `docs-private` (private, versioning on), per
[`08-integrations.md`](08-integrations.md) §4.

**imgproxy is unsigned locally.** `IMGPROXY_KEY` and `IMGPROXY_SALT` are blank in `.env.example`, so
variant URLs carry `/insecure/`. That is fine on a loopback and is a defect anywhere else: unsigned,
imgproxy resizes for anybody who finds it. Generate a pair with `openssl rand -hex 32` and set both
the container's variables and the API's `Media__Imgproxy__Key` / `__Salt` — they must match, because
the API signs the URL and imgproxy verifies it.

**Presigned URLs are signed for `https://s3.klarahome.localhost`, not for `http://minio:9000`.**
SigV4 covers the host, so a URL signed for the address the API writes through would be unreachable
from a browser and could not be rewritten. `Storage__SignedUrlEndpoint` is what makes the two
differ; MinIO accepts it because `MINIO_SERVER_URL` names the same host.

---

## 4. Configuration

Everything has a working default baked into the compose file, so **`.env` is optional**. Create
one when you need to override something:

```bash
cp .env.example .env
```

`.env` is git-ignored. `.env.example` documents every variable, including the application
variables the API will bind from Step 3 onwards.

The most common override on Windows is the PostgreSQL port, because a native PostgreSQL
installation already owns 5432:

```ini
POSTGRES_PORT=5433
```

Other useful ones: `COMPOSE_PROJECT_NAME` (run two isolated stacks side by side),
`BIND_ADDRESS=0.0.0.0` (reach the stack from a phone on the same network), `DEV_DOMAIN`.

### What is configuration and what is data

From Step 6 the line is drawn deliberately, because it is what makes the product
re-distributable:

| | Lives in | Changed by |
|---|---|---|
| Which business this deployment is | `.env`: `TENANT_CODE`, `TENANT_NAME`, `TENANT_ID`, `TENANT_DEFAULT_LOCALE`, `TENANT_DEFAULT_TIMEZONE` | A redeploy |
| Secrets: signing key, encryption keys, first administrator | `.env`: `AUTH_SIGNING_KEY_*`, `AUTH_ENCRYPTION_KEY*` (credentials), `ENCRYPTION_KEY*` (business columns — a vendor's bank account), `AUTH_BOOTSTRAP_*` | A redeploy, and a key rotation |
| What a marketplace demands of a seller before they trade | `.env` / `appsettings.json`: `Vendors:RequireGstin`, `RequireVerifiedKyc`, `RequireVerifiedBankAccount`, `RequirePickupLocation` | A redeploy. Deliberately **not** a store setting — a shopkeeper must not be able to lower the platform's compliance posture from an admin screen |
| Everything that business would want to change | `platform.store_settings` | `PUT /api/v1/admin/settings/{key}`, audited |
| Whether a feature is on | `platform.feature_flags` | `PUT /api/v1/admin/feature-flags/{key}`, audited |
| Who may do what | `identity.roles` and `identity.user_roles` | `PUT /api/v1/admin/roles/{id}`, `PUT /api/v1/admin/users/{id}/roles`, audited |

Branding, legal entity, GSTIN, PAN, CIN, addresses, support and grievance contacts, locale,
currency, timezone, return window, COD cap, free-shipping threshold and colours are all in the
second row. None of them is a variable, and none of them is compiled in.

### Signing in locally (Step 7)

A fresh database has nobody in it. Set the bootstrap credential before the first migrator run:

```ini
AUTH_BOOTSTRAP_EMAIL=you@example.in
AUTH_BOOTSTRAP_PASSWORD=a-password-of-at-least-ten-characters
```

The seeder creates that account once, as `platform-admin`, and does nothing on every deploy after
it. `platform-admin` is a mandatory-two-factor role, so the first sign-in returns a challenge
rather than a session:

```bash
# 1. Password. Answers with a challenge, not a token.
curl -sX POST http://localhost:8080/api/v1/admin/auth/login   -H 'content-type: application/json'   -d '{"email":"you@example.in","password":"..."}'

# 2. Enrol. Returns a base32 secret and an otpauth:// URI to scan.
curl -sX POST http://localhost:8080/api/v1/admin/auth/2fa/enrol   -H 'content-type: application/json' -d '{"challengeToken":"<from step 1>"}'

# 3. The code from your authenticator app completes the sign-in.
curl -sX POST http://localhost:8080/api/v1/admin/auth/2fa/verify   -H 'content-type: application/json'   -d '{"challengeToken":"<from step 1>","code":"123456"}'
```

### Storefront sign-in: email first, no one-time codes

**A customer's account is keyed on their email address.** They sign in with an identity provider,
or with an email and a password. There is no sign-in-with-a-code option, and the storefront no
longer offers one anywhere.

Mobile-number-plus-OTP was the original Step 7 primary and has been withdrawn: it needs a
DLT-registered SMS route this deployment does not have, and the development stand-in wrote one-time
codes to the API log, which `docs/07-security-compliance.md` §3 forbids outright in a deployed
environment. The endpoints are switched off by the `identity.mobile-otp-login` flag rather than
deleted, so the day an SMS provider is paid for the capability returns without a deploy
(ADR-014 decision 4).

`/auth/otp` still exists and is still reachable — but only as a **second factor**. A password
sign-in that answers with a challenge instead of a session sends the customer there to enter the
code from their authenticator app.

> **A database created before this change still has mobile-OTP switched on.** The flag seeder never
> reasserts a flag an operator may have set, so the new `false` default applies to new databases
> only. Turn it off on an existing one with the audited endpoint below, or on a throwaway local
> database with
> `docker exec klarahome-dev-postgres psql -U klarahome -d klarahome -c "UPDATE platform.feature_flags SET enabled = false WHERE key = 'identity.mobile-otp-login';"`
> followed by an API restart, because the flag service caches.

### Running without an SMS or email provider (Step 7A)

There is no free local path for SMS, and a production email provider costs money. Four flags turn
the features that need one off at runtime (ADR-014):

```bash
# As an administrator, with a bearer token:
PUT /api/v1/admin/feature-flags/identity.mobile-otp-login      { "enabled": false }
PUT /api/v1/admin/feature-flags/identity.email-verification    { "enabled": false }
PUT /api/v1/admin/feature-flags/identity.password-reset-email  { "enabled": false }
```

Those endpoints then answer `404 FEATURE_DISABLED`, and the change is audited. Turning one back on
brings the feature back on the next request — the code behind it is what shipped at Step 7.

With email off, an account an administrator creates has no password and no link:
`passwordSetupPending` is true on the response, and the way in is a temporary password.

```bash
PUT  /api/v1/admin/users/{id}/password        { "temporaryPassword": "..." }
# The next sign-in returns a password-change-required challenge, not a session:
POST /api/v1/admin/auth/password/change       { "challengeToken": "...", "currentPassword": "...", "newPassword": "..." }
```

**Google sign-in** replaces the OTP for customers. Create an OAuth client at
`console.cloud.google.com` (Web application), add
`<AUTH_EXTERNAL_CALLBACK_BASE_URL>/api/v1/store/auth/external/google/callback` as an authorised
redirect URI, then set `AUTH_GOOGLE_ENABLED=true` with the client id and secret. It works against
real Google from the dev stack — the only outbound host the API is allowed to reach is the provider
allow-list, so nothing else is reachable even by mistake.

> **No client id means no button, and that is the design.** The sign-in page asks
> `GET /store/auth/external/providers` and renders one button per provider it answers with. A
> provider with no client id and secret is deliberately not answered with, because a button that
> fails when somebody presses it is worse than no button. So a stack that has never been given an
> OAuth client shows an email and password form and nothing else — which is correct, not a missing
> feature in the front end. Check the endpoint before looking anywhere else:
>
> ```bash
> curl -sk https://api.klarahome.localhost/api/v1/store/auth/external/providers
> # []                                  -> not configured; set AUTH_GOOGLE_* in .env
> # [{"provider":"google",...}]         -> configured; the button will render
> ```

**Facebook / Meta** is designed for and left disabled. It cannot leave development mode until the
deployment's owner completes Business Verification and publishes a privacy-policy URL, and enabling
it also needs the schema note in ADR-014 addressed, because a Facebook account may carry no email
address at all. When it is switched on, the button reads "Continue with Meta".

**One-time codes are no longer written to any log** (Step 8). They are rendered from a template,
handed straight to a provider, and the delivery-log row keeps neither the body nor any variable's
value. See §4.4 for where to read them locally.

`AUTH_REFRESH_COOKIE_SECURE=false` in the dev stack: a browser will not return a `Secure` cookie
over plain http, and sign-in would appear to fail with nothing in any log. It is never false in
staging or production.

> **`TENANT_ID` matters.** Left blank it is derived from `TENANT_CODE`, which survives a restart
> but not a change of code — every row already written keeps the old id and becomes invisible.
> Set it explicitly on anything holding data. The `platform-tenant` readiness check reports that
> mismatch rather than letting the API serve an empty catalogue.

### Media, documents and notifications (Step 8)

**Uploading.** `POST /api/v1/admin/media` takes a multipart `file` and answers `201` with the file's
id, its public URL and its renditions. `?visibility=private` puts it in `docs-private` instead,
where it has no URL at all and is reached only through `GET /admin/media/{id}/link`.

```bash
curl -k -X POST https://api.klarahome.localhost/api/v1/admin/media \
     -H "Authorization: Bearer $TOKEN" -F file=@hero.png
```

The bytes decide what a file is. A `.png` carrying a script is refused with
`422 MEDIA_UNSUPPORTED_TYPE`, and the extension a download is offered under comes from the content
rather than from the upload. Nothing scans uploads: `scanState` reads `Skipped`, and
`Media__RequireVirusScan=true` refuses uploads rather than accepting them unchecked.

**Reading mail.** Everything this stack sends goes to Mailpit —
[`https://mail.klarahome.localhost`](https://mail.klarahome.localhost) or
`http://127.0.0.1:8025`. Nothing leaves the machine.

**Reading an SMS.** There is no SMS account (`08-integrations.md` §7), so outside Production an SMS
is delivered to Mailpit instead, addressed to `<digits>@sms.invalid` with a subject naming the
number and the event. **This is how you complete a mobile-OTP sign-in locally**, and it is why the
code is neither logged nor stored:

```bash
POST /api/v1/store/auth/otp/request   { "mobile": "+919876500456" }
# then open https://mail.klarahome.localhost and read 919876500456@sms.invalid
```

In Production that same message is recorded as `Suppressed / NoProvider` and nothing is sent — the
development route is bound to the environment, not to a setting.

**Watching the queue.** The worker polls every five seconds. A message is `Queued`, then `Sent`;
a one-time code skips the queue and is sent inline, because it cannot wait for a poll and its body
must not be stored for one.

```bash
# What was sent, what was not, and why. Recipients are masked.
GET /api/v1/admin/notifications?status=Suppressed
GET /api/v1/admin/notifications/{id}

# Prove a channel end to end, through the real pipeline rather than round it.
POST /api/v1/admin/notifications/test   { "channel": "Email", "to": "you@example.com" }
```

**Editing what a message says.** Templates are data: `GET /admin/notification-templates` lists them
with the placeholders each expects, and `PUT` rewrites one. A missing placeholder value fails the
message and names what was missing rather than rendering a gap. An **active SMS template must carry
its DLT id** — outside Production they are seeded active without one because no operator is
involved, but the editor refuses to activate one and the `dltViolation` field says why.

### Taking a payment locally (Step 15)

**Without credentials — which is how it arrives.** `RAZORPAY_KEY_ID` is blank in `.env.example`, so
a prepaid `place-order` answers `503 PAYMENT_PROVIDER_UNAVAILABLE` and says so in the body. Cash on
delivery is unaffected and works end to end today, which is enough to exercise the whole order
lifecycle without a merchant account.

**With a test key pair.** Razorpay has no separate sandbox host: a **Test** key pair against the live
API *is* the sandbox, and a Test key can be generated before the account is activated.

1. `dashboard.razorpay.com` -> Settings -> API Keys -> **Generate Test Key**.
2. Put the pair and a webhook secret of your own choosing into `.env`:

```bash
RAZORPAY_KEY_ID=rzp_test_xxxxxxxx
RAZORPAY_KEY_SECRET=xxxxxxxx
RAZORPAY_WEBHOOK_SECRET=pick-something-long
```

3. `docker compose -f infra/compose/docker-compose.dev.yml up -d --force-recreate api worker`
4. Place a prepaid order. The response now carries a real `providerOrderId` and the publishable key.

**Webhooks on a laptop.** Razorpay cannot reach `*.localhost`, so nothing will confirm the order by
itself. Two ways round it, and the second is the one to reach for:

```bash
# 1. A tunnel: point the dashboard webhook at <tunnel>/api/v1/webhooks/razorpay with the same
#    secret, and the real path runs.

# 2. No tunnel: let reconciliation find the payment. It asks the gateway about every collection
#    open longer than 20 minutes, and confirms the order exactly as a webhook would - which is
#    also the production recovery path for a webhook that was never delivered.
POST /api/v1/admin/payments/reconcile        # runs the sweep now, needs payments.gateway.manage
POST /api/v1/admin/payments/{id}/sync        # or re-read one collection
```

An attempt whose `source` reads `Reconciliation` is one no webhook ever arrived for. A run of them
in production means the webhook endpoint has stopped working.

**Watching what arrived.** Every webhook is stored before it is processed, verified or not:

```bash
GET  /api/v1/admin/gateway-events                        # the log
GET  /api/v1/admin/gateway-events?status=DeadLettered    # what could not be applied, after 8 tries
GET  /api/v1/admin/gateway-events/{id}                   # the raw body, exactly as it arrived
POST /api/v1/admin/gateway-events/{id}/replay            # re-queue one after fixing the cause
```

A forged webhook is stored with `signatureValid: false`, answered `401`, and can **never** be
processed — replaying it does not re-verify it.

**Refunds.** Anything at or below the `payments` settings section's `refundApprovalThreshold`
(5,000 by default) is sent immediately; anything above waits in `GET /admin/refunds?status=Requested`
for somebody else to approve. Approving your own is refused by the handler and by a check
constraint, so testing the two-person path needs two accounts.

```bash
POST /api/v1/admin/payments/{id}/refunds      -H "Idempotency-Key: $(uuidgen)"      -d '{ "amount": 100, "reason": "Damaged on arrival" }'
```

---

### Dispatching a parcel locally (Step 16)

**Without an aggregator account — which is how it arrives.** `SHIPPING_PROVIDER` is blank in
`.env.example`, so the manual adapter takes over and a parcel is booked from an air waybill you
supply. That is the whole flow: the order ships, the timeline fills in, the cash reconciles.

A confirmed sub-order opens a draft parcel by itself, so start from the pick list:

```bash
GET  /api/v1/admin/shipments/pick-list                # what is waiting to be packed

POST /api/v1/admin/sub-orders/{subOrderId}/shipments \
     -d '{ "weight": 750, "dimensions": { "lengthCm": 30, "widthCm": 20, "heightCm": 10 },
           "manualAwb": "TESTAWB0001", "manualCourier": "Local courier" }'

POST /api/v1/admin/shipments/{id}/dispatch            # the courier has it: this ships the order
POST /api/v1/admin/shipments/{id}/tracking \
     -d '{ "status": "OutForDelivery", "remark": "On the van" }'
POST /api/v1/admin/shipments/{id}/tracking \
     -d '{ "status": "Delivered" }'                   # closes the parcel, starts the return window
```

The last call is what proves the seams: it moves the **sub-order** through the ordering state
machine, writes the order's timeline, and — on a cash-on-delivery order — marks the
`payments.cod_collections` row collected. Check all three:

```bash
GET /api/v1/admin/orders/{orderId}                    # timeline, and the sub-order at Delivered
GET /api/v1/admin/cod-collections?status=Collected    # the cash the courier is now carrying
```

**With a Shiprocket account.** Set `SHIPPING_PROVIDER=shiprocket`, `SHIPPING_BASE_URL` and the
`SHIPPING_API_USER`/`SHIPPING_API_SECRET` pair (the API user's email and password — a static
`SHIPPING_API_KEY` also works where a courier issues long-lived tokens), then restart. Omit
`manualAwb` from the booking call above and the parcel goes to Shiprocket instead. Any other value
of `SHIPPING_PROVIDER`, including a typo, falls back to the manual adapter rather than failing —
check `GET /api/v1/admin/shipments/pick-list` still books by hand if you expected an API call.

`SHIPPING_BASE_URL` is **also the outbound allow-list**: the client refuses to talk to any other
host, so a wrong value fails at the socket rather than at somebody else's server with your token in
the header.

**Webhooks on a laptop.** A courier cannot reach `*.localhost`, so nothing will move a parcel by
itself. Three options, same as payments:

```bash
# 1. A tunnel: point their dashboard webhook at <tunnel>/api/v1/webhooks/shipping/shiprocket
#    with the same secret as SHIPPING_WEBHOOK_SECRET. Shiprocket sends it back in an
#    x-api-key header, not as an HMAC over the body.
# 2. Ask the courier directly, which is what the polling fallback does anyway:
POST /api/v1/admin/shipments/{id}/sync
# 3. Record the movement by hand, exactly as the manual flow above does.
```

A forged webhook is stored with `signatureValid: false`, answered `401`, and can **never** be
processed — replaying it does not re-verify it. `GET /api/v1/admin/courier-events?status=DeadLettered`
is the queue of what could not be applied.

**Rates and serviceability.** One catch-all zone in three weight bands is seeded, so checkout offers
a real delivery charge from the first order. `GET /api/v1/store/shipping/serviceability/500081` reads
a cache and never calls a courier; with no courier configured it answers optimistically, because
refusing an order for a destination nobody has checked is worse than one apology.

**Delivery coverage (Step 16A).** Separately from what a courier can reach, the store ships
restricted to **Hyderabad** — so `500081` is deliverable and `560001` answers
`DELIVERY_AREA_NOT_COVERED` at every gate from the PDP check to `place-order`. It is a store setting,
not configuration: widen it, or switch it off entirely, without a restart.

```bash
GET /api/v1/admin/shipping/coverage                   # the policy as it stands
GET /api/v1/admin/shipping/coverage/test/560001       # which of the two checks refuses it

# Edited where every other store policy is edited, so it is validated and audited once:
PUT /api/v1/admin/settings/delivery-coverage \
    -d '{ "enabled": true, "allowedCities": ["Hyderabad", "Secunderabad"],
          "allowedPincodePrefixes": ["500", "560"], "allowedPincodes": [], "blockedPincodes": [],
          "message": "We deliver within Hyderabad and Bengaluru." }'
```

```bash
GET  /api/v1/admin/shipping/zones                     # the map, in the precedence order it is applied
GET  /api/v1/admin/shipping/rates                     # the tariff
POST /api/v1/admin/shipping/serviceability/560001/refresh   # ask now, rather than waiting for the job
```

**A failed delivery** is the one courier outcome that needs a person. `GET /api/v1/admin/ndr` is the
queue; `POST /api/v1/admin/ndr/{id}/action` takes `Reattempt`, `Rescheduled`, `AddressUpdated` or
`ReturnToOrigin`. A parcel delivered on a later attempt closes its own report.

---

## 5. Hostnames and TLS

Chromium and Firefox resolve `*.localhost` to the loopback address on their own, so
`http://mail.klarahome.localhost` works in a browser with **no hosts-file entry**.

### The default edge: Caddy, plain HTTP (`DEV_EDGE=caddy`)

The stack is fronted by **Caddy on port 80 with no TLS at all**, in the same topology as the VPS
(one reverse proxy container, one hostname per surface, `infra/caddy/Caddyfile.local` next to the
production `Caddyfile`). `docker-compose.local.yml` is an override on the dev file: it parks
Traefik behind a profile, adds Caddy, and rewrites every URL the containers and the browser are
told about to `http://`. Same volumes, same database — switching edges loses nothing.

```
docker compose -f infra/compose/docker-compose.dev.yml \
               -f infra/compose/docker-compose.local.yml --env-file .env up -d
```

or simply `./infra/scripts/dev.ps1 up` (it reads `DEV_EDGE` from `.env`).

Plain HTTP is the point. The self-signed certificate below is refused by Chrome for **every XHR
and every `<img>`** from a subdomain it has not been told to trust, silently, with no interstitial
to click through: the shop looks empty, sign-in does nothing, and every product image is a broken
icon. Over HTTP there is nothing to trust, so every one of these works first time:

| Surface | URL |
|---|---|
| Storefront | `http://klarahome.localhost` |
| Admin back office | `http://admin.klarahome.localhost` |
| API | `http://api.klarahome.localhost` |
| Images (imgproxy) | `http://img.klarahome.localhost` (also `cdn.`) |
| Object storage (S3 API) | `http://s3.klarahome.localhost` |
| MinIO console | `http://minio.klarahome.localhost` |
| Mailpit | `http://mail.klarahome.localhost` |

The cookies are issued without `Secure` on this edge (`Auth__Tokens__RefreshCookieSecure`,
`Carts__CartCookieSecure`), because a browser drops a Secure cookie set over http.

### The original edge: Traefik, self-signed HTTPS (`DEV_EDGE=traefik`)

The certificate is **self-signed by Traefik**, so the browser shows a warning the first time.
Either accept it, or issue a locally-trusted certificate with
[mkcert](https://github.com/FiloSottile/mkcert).

**If you accept the warning, accept it on `https://api.klarahome.localhost` as well** — visit it
once directly and click through. The storefront and the admin app call the API with XHR, and an
XHR to a host whose certificate has not been accepted is refused with no interstitial to click
and no error a user can act on: the shop looks empty and the sign-in button appears to do
nothing. This is the one failure in the local stack that looks like a bug in the application and
is not, which is the reason for mkcert:

```bash
mkcert -install
mkcert -cert-file infra/traefik/certs/local-cert.pem \
       -key-file  infra/traefik/certs/local-key.pem \
       "klarahome.localhost" "*.klarahome.localhost" localhost 127.0.0.1
cp infra/traefik/dynamic/certs.yml.example infra/traefik/dynamic/certs.yml
```

Traefik watches the dynamic directory and picks the certificate up without a restart.
`infra/traefik/certs/` is git-ignored (`*.pem`, `*.key`) — certificates are never committed,
and `dynamic/certs.yml` is ignored too.

**Non-browser clients do not get the `*.localhost` shortcut.** `ping`, `psql`, `curl` and
.NET's `HttpClient` all use the OS resolver, which does not resolve subdomains of `localhost`
on Windows. Use the direct `127.0.0.1` ports (recommended), or add hosts entries — PowerShell
**as Administrator**:

```powershell
$hosts = "$env:SystemRoot\System32\drivers\etc\hosts"
$names = "klarahome.localhost www.klarahome.localhost api.klarahome.localhost " +
         "admin.klarahome.localhost s3.klarahome.localhost minio.klarahome.localhost " +
         "mail.klarahome.localhost traefik.klarahome.localhost"
Add-Content -Path $hosts -Value "`n127.0.0.1 $names"
```

---

## 6. Everyday tasks

**Seed the demonstration catalogue**

A freshly migrated database is an empty shop, and an empty shop cannot be reviewed. Set
`DEMO_SEED_CATALOG=true` in `.env` and run the migrator:

```bash
docker compose -f infra/compose/docker-compose.dev.yml --env-file .env --profile migrate run --rm migrator
```

That writes one active seller (`DEMO-ATELIER`), two category branches, three brands, ten products
with live offers, 120 units of stock against each, a published home page, and the search projection
over all of it. Every row is keyed on a `DEMO-` SKU or a `demo-` slug.

| It is | It is not |
|---|---|
| Idempotent — re-running adds only what is missing | An updater: it never rewrites a row it did not create, so copy you edit in the back office survives |
| Refused outright in Production, at registration and again inside each seeder | Governed by `Database__RunSeeders` alone — it needs `DemoData__SeedCatalog` as well |
| Visible as `Vendors.DemoVendor`, `Catalog.DemoCatalogue`, `Inventory.DemoStock`, `Content.DemoHomePage` and `Search.DemoIndex` in the migrator log | Silent about what it skipped — each seeder logs why |

Two things worth knowing:

- **It writes no product images.** Every product renders through the placeholder treatment, which
  is what they should look like until real photography is supplied.
- **It will not overwrite an existing home page.** If this database already has one — a
  hand-authored one, say — `Content.DemoHomePage` logs that it skipped and leaves it alone. Delete
  that page and re-run the migrator if you want the demo front page instead.

To remove it, `dev reset` and migrate again without the flag. There is no targeted undo: everything
it writes is prefixed, but products have listings, listings have stock and stock has a ledger, so
unpicking it by hand is more work than recreating the database.

**Connect to PostgreSQL**

```bash
docker exec -it klarahome-dev-postgres psql -U klarahome -d klarahome
# or from the host (adjust the port if you overrode it)
psql "postgresql://klarahome:klarahome_dev_password@127.0.0.1:5432/klarahome"
```

The database is created with the ICU locale provider and the `en-IN` locale, so collation and
sort order match the VPS exactly. The server timezone is **UTC**; India-local rendering is the
application's job.

**Read or change the store settings**

```bash
# The public document the storefront renders from.
curl -sk https://api.klarahome.localhost/api/v1/store/config | jq

# Every section, including the private ones.
curl -sk https://api.klarahome.localhost/api/v1/admin/settings | jq

# Replace one section. A section is edited as a whole: fields you omit go back to their defaults.
curl -sk -X PUT https://api.klarahome.localhost/api/v1/admin/settings/support   -H 'Content-Type: application/json'   -d '{"email":"help@example.in","phone":"+919876543210"}'

# What changed, and what it looked like before.
curl -sk 'https://api.klarahome.localhost/api/v1/admin/audit-logs?entityType=StoreSetting' | jq
```

**Turn a feature off without a deploy**

```bash
curl -sk -X PUT https://api.klarahome.localhost/api/v1/admin/feature-flags/platform.public-store-config   -H 'Content-Type: application/json' -d '{"enabled":false}'
```

The next request to `/api/v1/store/config` answers `404 FEATURE_DISABLED`. The flag set is cached
in-process for a minute, and the write invalidates it, so a single replica changes immediately.

**Import the PIN code dataset**

`platform.pincodes` ships empty: the India Post dataset is roughly nineteen thousand rows and
changes without notice, so it is mounted rather than built into the image. Drop `pincodes.csv`
into `infra/seed/` (format in [`infra/seed/README.md`](../infra/seed/README.md)), then:

```ini
# .env
PINCODE_DATA_PATH=/app/seed/pincodes.csv
```

```bash
docker compose -f infra/compose/docker-compose.dev.yml --env-file .env   --profile migrate run --rm migrator
```

The import is idempotent — matched on the PIN code and updated in place. Until it has run, keep the
`platform.pincode-lookup` flag off, or every valid code answers `PINCODE_NOT_FOUND`.

**Inspect Redis**

```bash
docker exec -it klarahome-dev-redis redis-cli -a klarahome_dev_password
```

**Read captured mail** — open `http://127.0.0.1:8025`. Every message sent to
`mailpit:1025` (from a container) or `127.0.0.1:1025` (from the host) is captured; nothing
ever leaves the machine. Mailpit also exposes a JSON API, useful in integration tests:

```bash
curl -s http://127.0.0.1:8025/api/v1/messages
```

**Browse object storage** — `http://127.0.0.1:9001`, or use `mc` inside the container:

```bash
docker exec -it klarahome-dev-minio mc ls local/media-public
```

**Rebuild and restart the API after a code change**

```bash
docker compose -f infra/compose/docker-compose.dev.yml --env-file .env up -d --build api
docker logs -f klarahome-dev-api
```

For a normal edit-run-debug loop, skip the container and run the host directly against the
containerised backing services — it starts in about a second and attaches a debugger:

```bash
dotnet run --project src/backend/host/KlaraHome.Api --launch-profile api-with-stack
# http://localhost:5080/scalar
```

**Check the error contract by hand** (Development only, and only while
`Api__EnableDiagnosticsEndpoints` is true):

```bash
# 500 ProblemDetails from a deliberate exception - no stack trace, correlation id only
curl -sk https://api.klarahome.localhost/api/v1/diagnostics/boom

# 422 with camelCased field errors from the FluentValidation pipeline
curl -sk -X POST https://api.klarahome.localhost/api/v1/diagnostics/echo \
  -H 'Content-Type: application/json' \
  -d '{"message":"","repeat":99}'

# Any status in the contract, on demand: Malformed|Unauthorized|Forbidden|NotFound|
# Conflict|Gone|Validation|RateLimited|Unexpected|Unavailable
curl -sk https://api.klarahome.localhost/api/v1/diagnostics/error/Conflict
```

**Trace one request end to end** — send your own correlation id and grep for it:

```bash
curl -sk -H 'X-Correlation-Id: my-trace-1' https://api.klarahome.localhost/api/v1/meta
docker logs klarahome-dev-api 2>&1 | grep my-trace-1
```

**Apply database migrations** — the API never migrates on startup, in any environment. A
one-shot `migrator` job does it, and it sits in the `migrate` compose profile so a plain `up`
never races it:

```bash
docker compose -f infra/compose/docker-compose.dev.yml --env-file .env   --profile migrate run --rm migrator
echo $?     # 0 = applied or already up to date, 1 = failed and nothing was committed
```

Re-running it is a no-op — that is the contract, and the deploy pipeline depends on it.

You will see one line on every run:

```
Cannot load library libgssapi_krb5.so.2
```

That is Npgsql probing for Kerberos on a chiseled image that has no krb5. It falls back to
SCRAM, which is what the server offers. Harmless, and noted here so nobody spends an afternoon
on it.

**Author a migration**

```bash
./tools/ef.ps1 add Platform AddTenantTable    # Windows
./tools/ef.sh  add Platform AddTenantTable    # bash / WSL2
./tools/ef.sh  list Platform                  # what exists, and what is applied
./tools/ef.sh  remove Platform                # undo the last one, if unapplied
./tools/ef.sh  script Platform                # idempotent SQL -> artifacts/migrations/
```

Each module owns its own `DbContext`, its own Postgres schema and its own
`__ef_migrations_history` table inside that schema, so the scripts take a module name and derive
the rest. **Review the generated SQL, not only the C#** (`docs/03-database-design.md` §7); the
`script` command exists to make that easy.

Migrations can also be applied from the host, without building the image:

```bash
cd src/backend
ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=klarahome;Username=klarahome;Password=klarahome_dev_password"   dotnet run --project host/KlaraHome.Migrator
```

**Inspect what the migrations created**

```bash
docker exec klarahome-dev-postgres psql -U klarahome -d klarahome -c "\dt platform.*"
docker exec klarahome-dev-postgres psql -U klarahome -d klarahome -c "\d platform.outbox_messages"
docker exec klarahome-dev-postgres psql -U klarahome -d klarahome -c "\dx"
```

**Run the database tests** — they start their own throwaway PostgreSQL through Testcontainers,
pinned to the same image the stack runs, so they never touch your dev data:

```bash
cd src/backend
dotnet test --project tests/KlaraHome.IntegrationTests/KlaraHome.IntegrationTests.csproj
```

With no Docker daemon they skip rather than fail.

**Run the quality gates CI runs** — the same script CI calls, so a CI failure reproduces locally in
seconds instead of by pushing another commit:

```bash
./tools/ci.sh                     # everything except the container images
./tools/ci.sh format test         # the two a code change usually trips
./tools/ci.sh package             # docker build of both images, then a Trivy scan
./tools/ci.sh --skip-integration  # no Docker: skip those suites, coverage becomes advisory
./tools/ci.ps1 -Stage format,test # Windows
```

The backend suites are run **as executables**, not through `dotnet test`, and each asserts a
minimum test count — which is what makes the `Zero tests ran` problem in §7 impossible to mistake
for a pass. Coverage must hold **70 %** line coverage over our own assemblies.
Full detail: [`ci-pipeline.md`](ci-pipeline.md).

**Start clean**

```bash
./infra/scripts/dev.ps1 reset    # deletes every volume, then run `up` again
```

A reset drops the database, so run the migrator again before expecting the API to serve data.

---

## 7. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Ports are not available: 0.0.0.0:5432` | A native PostgreSQL (or another stack) owns the port | Set `POSTGRES_PORT=5433` in `.env`, or stop the native service |
| `Ports are not available: ... :80` / `:443` | IIS, another Traefik, or Windows' `http.sys` | Set `HTTP_PORT` / `HTTPS_PORT` in `.env` |
| `error during connect ... dockerDesktopLinuxEngine` | Docker Desktop is not running | Start Docker Desktop and wait for the whale icon to settle |
| Browser warns about the certificate | Traefik's self-signed default certificate | Expected — accept it, or use mkcert (§5) |
| `ERR_NAME_NOT_RESOLVED` for `*.klarahome.localhost` | A browser that does not special-case `.localhost` | Add hosts entries (§5) or use the `127.0.0.1` ports |
| `psql: could not translate host name "s3.klarahome.localhost"` | The OS resolver, not the browser | Use `127.0.0.1:9000`, or add hosts entries |
| Postgres container restarts after changing `POSTGRES_*` | Credentials are only applied to an **empty** data directory | `dev reset`, then `up` |
| Everything is slow on Windows | The repository lives on `/mnt/c` inside WSL | Keep the clone on the Windows filesystem and use Docker Desktop's WSL integration |
| `mc: Unable to initialize new alias` in `minio-init` | MinIO was not healthy yet | It has `depends_on: service_healthy`; check `dev logs minio` |
| `api` container is `unhealthy` | It failed configuration validation at startup | `docker logs klarahome-dev-api` — a bad setting is reported by name and the host refuses to start on purpose |
| `404` from `https://api.klarahome.localhost` | Traefik has not picked the router up yet, or the container is not on the `edge` network | `dev logs traefik`, then check the dashboard router list |
| `/health/ready` is `Unhealthy` but `/health/live` is fine | PostgreSQL or Redis is down | That is the probe working. `dev status` and restart the offending service |
| API image build fails on an analyzer warning | The image build runs with warnings-as-errors, as CI does | Fix the warning; `dotnet build` locally shows the same message as a warning |
| A notification sits at `Queued` for ever | The worker is not running; the API never drains the queue | `dev status`, then `docker logs klarahome-dev-worker`. Only the worker has `Notifications__DispatcherEnabled` |
| A mobile OTP never arrives | There is no SMS account, and outside Production the message goes to the mail catcher instead | Open `https://mail.klarahome.localhost` and read `<digits>@sms.invalid` |
| An SMS is `Suppressed / NoProvider` | Correct, and expected: no SMS account is provisioned (ADR-017) | Nothing to fix. Set `Sms__Provider` and write the adapter when an account exists |
| An SMS is `Suppressed / NoTemplate` | The template for that event and channel is inactive — in Production, because it has no DLT id | Register the id in `PUT /admin/notification-templates/{id}` and activate it |
| A message is `Failed` with "missing values for: x" | The caller did not supply a placeholder the template uses | Fix the caller. A missing value fails the message rather than rendering a gap |
| `MEDIA_UNSUPPORTED_TYPE` for a file that opens fine | The bytes are not what the extension claims, or it is a format not on the allow-list (JPEG, PNG, GIF, WebP; PDF for documents) | Convert it. The declared type is never trusted |
| `MEDIA_STORAGE_UNAVAILABLE` | MinIO is down, or `Storage__AccessKey` / `__SecretKey` are unset | `dev status`; `/health/ready` names the buckets it could not reach |
| An imgproxy URL answers `422` | The stored original is not a decodable image — a truncated upload, usually | Re-upload. imgproxy reports the reason in `docker logs klarahome-dev-imgproxy` |
| An imgproxy URL answers `403` | The signature does not match | `IMGPROXY_KEY`/`IMGPROXY_SALT` and `Media__Imgproxy__Key`/`__Salt` must be the same pair |
| A signed document link answers `SignatureDoesNotMatch` | The URL was signed for a different host from the one it was fetched over | `Storage__SignedUrlEndpoint` must be the address the browser uses, and MinIO's `MINIO_SERVER_URL` must agree |
| `No usable font was found for PDF rendering` | The container has no font family, or `Documents__FontDirectories` points somewhere empty | The image copies DejaVu into `/app/fonts` at build time; rebuild it, or point the setting at a directory that has one |
| `dotnet test` reports `Zero tests ran` but the suites have tests | The Microsoft.Testing.Platform runner talks to the test host over a loopback JSON-RPC connection; a firewall or endpoint-security agent can block it silently, and the orchestrator reports zero rather than an error | Run the test executable directly — it is the same runner without the RPC hop: `./tests/KlaraHome.UnitTests/bin/Debug/net10.0/KlaraHome.UnitTests.exe`. Verify with `<exe> --help`: if that works, the tests are fine and only the orchestrator is blocked |
| `Cannot load library libgssapi_krb5.so.2` from the migrator | Npgsql probes for Kerberos; a chiseled image has no krb5 | Harmless. Authentication proceeds over SCRAM. Not a failure and not worth an image change — the `-extra` chiseled variant does not carry krb5 either |
| `relation "platform.outbox_messages" does not exist` | The migrator has not been run against this database | Run the `migrator` job (§6). After `dev reset` it always has to be run again |
| Migrator exits 1 with `password authentication failed` | `.env` credentials differ from the ones the Postgres volume was initialised with | Credentials are only applied to an **empty** data directory: `dev reset`, then `up`, then migrate |
| `The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support user-initiated transactions` | Retry-on-failure is enabled, so EF will not let you open a transaction it cannot re-run | Use `KlaraHomeDbContext.ExecuteInTransactionAsync(...)` instead of `BeginTransactionAsync`. It wraps the transaction in the execution strategy, which is the required order |
| A query returns nothing though the rows are visible in `psql` | The global tenant and soft-delete filters | Expected. `IgnoreQueryFilters()` in a query, deliberately and locally, or check `tenant_id` and `deleted_at` on the row |
| `/health/ready` reports `platform-tenant` as `Unhealthy` after changing `TENANT_CODE` | The tenant id is derived from the code when `TENANT_ID` is blank, so a new code is a new tenant and every existing row belongs to the old one | Put the old code back, or set `TENANT_ID` to the id the data was written under (`select id from platform.tenants`) and keep it pinned from then on |
| `cannot retrieve a system column in this context` when inserting | An entity mapped to a **partitioned** table picked up the `xmin` concurrency token, and PostgreSQL will not return a system column from one | Mark the entity `IAppendOnly`. That is what it is for, and an append-only table has no lost update to detect anyway |
| `platform.audit_logs is append-only; UPDATE is not permitted` | A trigger enforcing docs/07-security-compliance.md §7 | Working as intended. Retention is `DROP` of a monthly partition, never `DELETE` |
| An audit insert fails with `no partition of relation "audit_logs" found` | The migration created two years of monthly partitions and the default partition was dropped | Recreate it, or create the month: `select platform.ensure_audit_log_partition(current_date);` |
| A signed-in user suddenly gets 401 on everything after an API restart | No `AUTH_SIGNING_KEY_PEM` is configured, so the host minted an ephemeral RSA key that died with the process. It warns about this on every start | Expected in Development. Sign in again, or configure a real key. Outside Development the host refuses to start rather than doing this |
| Sign-in appears to succeed but every refresh is 401 | The refresh cookie is marked `Secure` and the browser will not send it back over plain http | `AUTH_REFRESH_COOKIE_SECURE=false` for local http work. Never false in a deployed environment |
| `Auth:Encryption:CurrentKeyId is '...', which is not in Auth:Encryption:Keys` | Two-factor enrolment needs a key to protect the secret with | Set `AUTH_ENCRYPTION_KEY` to base64 of exactly 32 bytes: `openssl rand -base64 32` |
| An enrolled authenticator stops being accepted after a config change | `AUTH_ENCRYPTION_KEY` changed, so the stored secret no longer decrypts | Put the old key back, or keep it listed alongside the new one — the key id travels in each stored value so both can be read |
| A vendor user sees an empty list where rows plainly exist | The vendor query filter, comparing the row's `vendor_id` to the token's | Working as intended. Check the `vendor_id` claim in the token and on the row |
| `Encryption:CurrentKeyId is '...', which is not a 32-byte base64 key in Encryption:Keys` | A vendor bank account cannot be stored without a column-encryption key | Set `ENCRYPTION_KEY` to base64 of exactly 32 bytes: `openssl rand -base64 32`. Every other Vendors endpoint works without one, so this is refused per request rather than at boot |
| `POST /admin/vendors/{id}/activate` answers 422 `VENDOR_NOT_READY` | An onboarding requirement is unmet | `GET /admin/vendors/{id}/readiness` lists every blocker at once. The requirements themselves are the `Vendors:Require*` settings |
| A seller's KYC upload is refused with "must be uploaded as a private file" | The scan went to the public bucket, where a PAN card would be served to anybody with the URL | Upload with `visibility=private`, then post the returned file id |
| A `{id}` route answers 404 for a resource you can see in `psql` | Object-level scope. Out-of-scope resources answer 404 rather than 403 so existence is not leaked | Working as intended (`docs/07-security-compliance.md` §2) |
| `The AuthorizationPolicy named: 'perm:...' was not found` | Something replaced `IAuthorizationPolicyProvider` after `AddKlaraHomeAuthorization`, or registered it with `TryAdd` | The provider manufactures every `perm:` policy. It is registered with `Replace` for exactly this reason |
| No OTP arrives anywhere | There is no SMS or email transport until Step 8 | Read it from the API log: `docker logs klarahome-dev-api \| grep "DEVELOPMENT ONLY"` |
| Google sign-in button does not appear | The provider is switched on but has no client id, so it is listed as unusable rather than offered and broken | Set `AUTH_GOOGLE_CLIENT_ID` and `AUTH_GOOGLE_CLIENT_SECRET`. `GET /api/v1/store/auth/external/providers` shows what is actually on offer |
| `redirect_uri_mismatch` from Google | The URI Google was given does not match one registered on the OAuth client | It is built from `AUTH_EXTERNAL_CALLBACK_BASE_URL`, never from the request Host. Register `<that>/api/v1/store/auth/external/google/callback` exactly |
| Every external sign-in ends in `IDENTITY_EXTERNAL_CALLBACK_INVALID` | The state cookie is not coming back — most often `AUTH_REFRESH_COOKIE_SECURE=true` over plain http, or a callback origin that differs from the one the sign-in started on | Same fix as the refresh cookie: false for local http. The cookie is `SameSite=Lax` and scoped to `/api/v1/store/auth/external` |
| `IDENTITY_RETURN_URL_NOT_ALLOWED` on start | `returnUrl` is neither a relative path nor a configured origin | Add the origin to `AUTH_EXTERNAL_ALLOWED_RETURN_URL`. It is an allow-list because an open redirect on the endpoint that has just issued a session is a phishing tool |
| A Google user ends up with a second account | The provider did not state that it had verified the address, so it was not allowed to link to the existing one | Working as intended (`docs/07-security-compliance.md` §1). Linking on an unverified address is how accounts get stolen |
| An outbound request fails with "not in the identity-provider allow-list" | Something tried to reach a host outside `ExternalHttp.AllowedHosts` | That is the SSRF control working. Add the host deliberately if a new provider needs it |
| `dotnet format` reports thousands of `ENDOFLINE` errors, only on Windows | Stale CRLF in the working tree. `.gitattributes` normalises `*.cs` to LF in the repository, so a file written with CRLF and then committed is clean in git but still CRLF on disk | Re-checkout the files: `git ls-files -z '*.cs' \| xargs -0 rm -f && git checkout -- '*.cs'`. Verify with `dotnet format --verify-no-changes` |
| `dotnet format` fails with `CHARSET` on a file under `Migrations/` | `dotnet ef` writes the migration with a UTF-8 BOM and the Designer/snapshot without one, and neither is configurable | Already handled: `.editorconfig` sets `charset = unset` for `**/Migrations/*.cs`. If it reappears, that section was lost |
| Coverage numbers look wrong, and the log says `Coverage settings file is not a valid file` | `src/backend/coverage.settings.xml` is invalid XML, most often a `--` inside a comment, which XML forbids. The tool warns once and then measures with default filters | Fix the XML. Re-run with `--log-level Verbose --log-file <path>` and check that warning is gone |
| npm or Nx output is interleaved with debugger chatter | `NODE_OPTIONS` carries the VS Code JS-debugger bootloader | `tools/ci.ps1` and the CI workflow clear it. In your own shell: `NODE_OPTIONS= npx ...` |
| `tools/ci.sh` cannot find `ci.ps1`, showing a `/c/work/...` path | Git Bash handed a POSIX path to a native Windows `pwsh` | Already handled by `cygpath` in the wrapper. If it reappears, run `tools/ci.ps1` directly |

---

## 8. Deliberate differences from staging and production

Recorded so nothing here is mistaken for a production pattern.

| Development | Staging / production (Step 32) |
|---|---|
| Data services publish host ports (bound to `127.0.0.1`) | No published ports; the data network is `internal: true` |
| Credentials are defaults in `.env.example` | Docker secrets / SOPS, injected via the `*__File` convention |
| Traefik reads the Docker socket directly (read-only) | Behind a socket proxy; least privilege |
| Self-signed TLS, no HSTS | Let's Encrypt, HSTS, full security-header set |
| Traefik dashboard open on loopback | Removed, or behind auth and an IP allow-list |
| Mailpit captures all mail | A real transactional email provider |
| No resource pressure enforcement beyond soft limits | Hard `deploy.resources.limits` tuned to the VPS |
| API serves `/scalar` and the diagnostics endpoints | Both are off; the OpenAPI document is published as a generated client instead |
| API image built from the working tree by compose | Image built once in CI, tagged with the commit SHA, pulled by the VPS |
| Migrations are applied by hand, whenever you remember | The `migrator` job runs to completion in the deploy pipeline, and the new API version does not start unless it exits 0 |
| `Database__EnableSensitiveDataLogging` may be on, so EF logs parameter values | Refused: the host will not boot in Production with it set, because those values include personal data |
| `Tenant__Id` is derived from `Tenant__Code` if unset | Set explicitly. A derived id changes if the code ever changes, which would strand every row already written |
