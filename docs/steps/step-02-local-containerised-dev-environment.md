# Step 2 — Local containerised dev environment

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** A · **Depends on:** Step 1
- **Objective:** A single command brings up every backing service a developer needs, matching
  what will run on the VPS.
- **Deliverables:**
  - `docker-compose.dev.yml` with: PostgreSQL 18, Redis 8, MinIO (S3-compatible),
    Mailpit (SMTP capture), Traefik v3 (reverse proxy + local TLS).
  - `.env.example` with every variable documented; `.env` git-ignored.
  - Named volumes for data persistence; health checks on every service.
  - `docs/dev-setup.md` — prerequisites and troubleshooting for Windows/WSL2.
- **Acceptance criteria:** `docker compose -f docker-compose.dev.yml up -d` starts all services
  healthy; Postgres reachable; MinIO console reachable; Mailpit UI reachable.
- **Outcome / Notes:** ✅ **DONE 2026-09-05.**

  **All four acceptance criteria met, verified by execution rather than by inspection:**
  1. `docker compose -f infra/compose/docker-compose.dev.yml --env-file .env up -d` — all five
     long-running services report `(healthy)`: traefik, postgres, redis, minio, mailpit. The
     one-shot `minio-init` exits `0`.
  2. **Postgres reachable** from the host: `select version()` → `PostgreSQL 18.6`. The database
     is created with `datlocprovider=i`, `datlocale=en-IN`, `UTF8`, server timezone `UTC`.
  3. **MinIO console reachable** — confirmed in Chrome at `http://127.0.0.1:9001` (login page
     renders) and over TLS through Traefik (`HTTP 200`).
  4. **Mailpit UI reachable** — confirmed in Chrome at `http://mail.klarahome.localhost:8025`.

  **Files created:**
  - `infra/compose/docker-compose.dev.yml` — the five services plus the `minio-init` one-shot.
    Every service carries `restart: unless-stopped`, a healthcheck with a `start_period`,
    `deploy.resources.limits`, `no-new-privileges`, and json-file logging capped at 10 MB × 3,
    per `06-infrastructure-devops.md` §4. Two networks: `edge` (Traefik-facing) and `data`.
    Four named volumes, all prefixed with the compose project name.
  - `infra/traefik/traefik.dev.yml` — static config: JSON logs, `web` → `websecure` redirect,
    TLS on `websecure`, the `api@internal` dashboard, and a `ping` entrypoint
    (container-internal only, :8082) so the `traefik healthcheck` CLI works. Docker provider
    with `exposedByDefault: false`, plus a watched file provider.
  - `infra/traefik/dynamic/middlewares.yml` — security headers, compression, and a rate-limit
    middleware defined ready for the Step 3 routers.
  - `infra/traefik/dynamic/tls.yml` — TLS options (minimum TLS 1.2). No certificate store, so
    Traefik serves its own self-signed certificate.
  - `infra/traefik/dynamic/certs.yml.example`, `certs/.gitkeep`, `dynamic/.gitignore` — the
    opt-in mkcert path to a locally-trusted certificate. Certificates are never committed.
  - `.env.example` — every variable documented, in two clearly separated groups: the dev-stack
    variables consumed today, and the application variables from
    `06-infrastructure-devops.md` §4.1 that the API will bind from Step 3. `.env` is
    git-ignored (verified with `git check-ignore`).
  - `infra/scripts/dev.ps1` and `infra/scripts/dev.sh` — `up | down | restart | status | logs |
    reset | urls`. They resolve the repository root themselves, pass `--env-file` only when a
    `.env` exists, and print the live URLs using the actual ports read from `.env`.
  - `docs/dev-setup.md` — prerequisites, commands, credentials, configuration, hostnames and
    TLS, everyday tasks, a troubleshooting table, and an explicit table of the deliberate
    differences between this environment and production.

  **Verified beyond the acceptance criteria:**
  - **Traefik routing over TLS** for all four hostnames (`traefik.`, `minio.`, `s3.`, `mail.`
    `klarahome.localhost`): `HTTP 200` each, the `s3` health endpoint `200`, HTTP→HTTPS
    redirect working, and the security-header middleware asserted on the response
    (`X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`,
    `Permissions-Policy`).
  - **Buckets bootstrapped**: `media-public` (anonymous `download` policy) and `docs-private`
    (private, versioning enabled), per `08-integrations.md` §4.
  - **End-to-end SMTP capture**: a message sent to `mailpit:1025` from a throwaway container
    appeared in the Mailpit REST API. Nothing leaves the machine.
  - **Volume persistence**: a row, an object, a Redis key and a captured mail all survived a
    full `down` / `up` cycle. Every probe artefact was removed afterwards.
  - **Image tags pinned to exact versions**, each pulled and version-checked:
    `postgres:18.6-alpine`, `redis:8.10.1-alpine`,
    `minio/minio:RELEASE.2025-09-07T16-13-09Z`, `minio/mc:RELEASE.2025-08-13T08-35-41Z`,
    `axllent/mailpit:v1.31.0`, `traefik:v3.6.25`.

  **Four deviations, none of which change the specification:**
  1. **Compose file path.** The card writes the command as
     `docker compose -f docker-compose.dev.yml up -d`, but `01-architecture.md` §8 places
     compose files in `infra/compose/`. The §8 layout wins; the command becomes
     `docker compose -f infra/compose/docker-compose.dev.yml --env-file .env up -d`, wrapped by
     `infra/scripts/dev.{ps1,sh} up` so the objective (a single command) still holds. No
     duplicate file was placed at the repository root.
  2. **`docker-compose.base.yml` was not created.** `06-infrastructure-devops.md` §4 splits
     service definitions (base) from dev overrides. There is nothing to share yet — the
     application services arrive at Step 3 — so creating an empty base file now would be
     scaffolding a later step. The dev file is self-contained; the split happens at Step 3.
  3. **Every variable has a default baked into the compose file** (`${VAR:-default}`), so a
     fresh clone runs with no `.env` at all. Compose resolves its default env file relative to
     the compose file rather than the repository root, so the root `.env` is passed explicitly
     with `--env-file`; the helper scripts do this automatically.
  4. **`read_only: true` root filesystems were not applied.** §4 lists them as a standard, but
     Postgres, Redis and MinIO all write outside their volumes and would need a tmpfs matrix
     that only pays off under production constraints. `no-new-privileges` **is** applied to
     every service. Read-only roots belong to Step 32 and are in the Parking Lot.

  **Known local-environment note (not a code issue):** this machine runs a native PostgreSQL on
  5432, so the local git-ignored `.env` sets `POSTGRES_PORT=5433`. The committed default stays
  5432; the override is documented in `docs/dev-setup.md` §4 and §7.
