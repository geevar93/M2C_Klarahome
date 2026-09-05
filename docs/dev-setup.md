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

**Start clean**

```bash
./infra/scripts/dev.ps1 reset    # deletes every volume, then run `up` again
```

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
