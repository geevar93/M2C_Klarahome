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
| Traefik dashboard | `traefik:v3.6.25` | — | `https://traefik.klarahome.localhost` |
| **API** | `klarahome/api:dev` (built locally) | — | `https://api.klarahome.localhost` |

The API deliberately publishes **no host port**: Traefik is the only way in, exactly as on the
VPS. Its useful endpoints:

| Endpoint | Purpose |
|---|---|
| `/health/live` | Liveness. Answers while the process is running; used by the container HEALTHCHECK |
| `/health/ready` | Readiness. Also probes PostgreSQL and Redis |
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

Buckets are created automatically by the one-shot `minio-init` container:
`media-public` (anonymous read) and `docs-private` (private, versioning on), per
[`08-integrations.md`](08-integrations.md) §4.

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
| Secrets: signing key, encryption key, first administrator | `.env`: `AUTH_SIGNING_KEY_*`, `AUTH_ENCRYPTION_KEY*`, `AUTH_BOOTSTRAP_*` | A redeploy, and a key rotation |
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

**One-time codes are written to the API log**, because there is no SMS or email transport until the
Notifications module at Step 8. `docker logs klarahome-dev-api | grep "DEVELOPMENT ONLY"` shows the
customer OTP or the reset link token. This dispatcher is what
`docs/07-security-compliance.md` §3 forbids in production, and replacing it is a Step 8 deliverable.

`AUTH_REFRESH_COOKIE_SECURE=false` in the dev stack: a browser will not return a `Secure` cookie
over plain http, and sign-in would appear to fail with nothing in any log. It is never false in
staging or production.

> **`TENANT_ID` matters.** Left blank it is derived from `TENANT_CODE`, which survives a restart
> but not a change of code — every row already written keeps the old id and becomes invisible.
> Set it explicitly on anything holding data. The `platform-tenant` readiness check reports that
> mismatch rather than letting the API serve an empty catalogue.

---

## 5. Hostnames and TLS

Chromium and Firefox resolve `*.localhost` to the loopback address on their own, so
`https://mail.klarahome.localhost` works in a browser with **no hosts-file entry**.

The certificate is **self-signed by Traefik**, so the browser shows a warning the first time.
Either accept it, or issue a locally-trusted certificate with
[mkcert](https://github.com/FiloSottile/mkcert):

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
| A `{id}` route answers 404 for a resource you can see in `psql` | Object-level scope. Out-of-scope resources answer 404 rather than 403 so existence is not leaked | Working as intended (`docs/07-security-compliance.md` §2) |
| `The AuthorizationPolicy named: 'perm:...' was not found` | Something replaced `IAuthorizationPolicyProvider` after `AddKlaraHomeAuthorization`, or registered it with `TryAdd` | The provider manufactures every `perm:` policy. It is registered with `Replace` for exactly this reason |
| No OTP arrives anywhere | There is no SMS or email transport until Step 8 | Read it from the API log: `docker logs klarahome-dev-api \| grep "DEVELOPMENT ONLY"` |
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
