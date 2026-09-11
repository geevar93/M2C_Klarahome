# Deploying to a VPS with Docker and Caddy

> Everything the platform needs runs in Docker containers on one machine. **Caddy runs on the
> host** — it is the only process listening on 80 and 443, it obtains and renews the certificates,
> and it proxies to containers that publish on `127.0.0.1` and nowhere else.
>
> Files this guide uses:
> [`infra/compose/docker-compose.prod.yml`](../infra/compose/docker-compose.prod.yml) ·
> [`.env.prod.example`](../.env.prod.example) ·
> [`infra/caddy/Caddyfile`](../infra/caddy/Caddyfile) ·
> [`infra/scripts/backup-db.sh`](../infra/scripts/backup-db.sh)
>
> The database is in a container to begin with, on a named Docker volume. §9 moves it to Neon or
> any other managed PostgreSQL by editing **one variable**.

---

## 1. What you are deploying

```
                         Internet
                            │ 80 / 443
                   ┌────────▼────────┐
                   │  Caddy (host)   │  TLS, HTTP→HTTPS, security headers, access logs
                   └──┬───┬───┬───┬──┘
     127.0.0.1:4000 ──┘   │   │   └── 127.0.0.1:9000 ─► minio     (s3.<domain>)
     127.0.0.1:8080 ──────┘   └────── 127.0.0.1:8082 ─► imgproxy  (cdn.<domain>)
     127.0.0.1:8081 ──► admin
            │  │
       storefront  api ──────► worker (no ingress, no port)
                    │            │
       ┌────────────┴────┬───────┴──────┐
       ▼                 ▼              ▼
   postgres            redis          minio          ← `data` network, internal: true
   (volume)           (volume)       (volume)          no host ports, no internet
```

| Public hostname | Container | Loopback port |
|---|---|---|
| `<domain>` | storefront (Node SSR) | 4000 |
| `www.<domain>` | — (301 to the apex) | — |
| `api.<domain>` | api (ASP.NET Core 10) | 8080 |
| `admin.<domain>` | admin (nginx, static) | 8081 |
| `cdn.<domain>` | imgproxy | 8082 |
| `s3.<domain>` | MinIO S3 API | 9000 |

Two containers have no public name at all: **worker** (drains the outbox, sends notifications,
runs every polling loop) and **migrator** (a one-shot job that applies migrations and exits).

All five names must be subdomains of one registrable domain. The refresh-token cookie is
`SameSite=Lax`; a shop on one site and an API on another would never receive it.

**Sizing.** 4 vCPU / 8 GB / 160 GB NVMe for production at launch, 2 vCPU / 4 GB for staging
(`docs/06-infrastructure-devops.md` §2). Prefer an India region for latency and DPDP comfort.

---

## 2. Prepare the VPS

Ubuntu 24.04 LTS is assumed. Everything below is run once.

### 2.1 Accounts, SSH and firewall

```bash
adduser deploy && usermod -aG sudo deploy
rsync --archive --chown=deploy:deploy ~/.ssh /home/deploy

# /etc/ssh/sshd_config: PermitRootLogin no, PasswordAuthentication no
systemctl restart ssh

ufw default deny incoming && ufw default allow outgoing
ufw allow OpenSSH
ufw allow 80/tcp && ufw allow 443/tcp
ufw enable

apt install -y fail2ban unattended-upgrades
dpkg-reconfigure --priority=low unattended-upgrades
```

Only 22, 80 and 443 are ever open. The database, Redis and MinIO publish nothing to the outside:
`ufw` is the second lock, not the first.

> If you tighten 443 to specific source addresses, allow the Docker bridge network too
> (`ufw allow from 172.16.0.0/12 to any port 443`). The storefront container fetches the API over
> its public hostname — see §6.4 — and that request arrives from the bridge.

### 2.2 Docker

```bash
curl -fsSL https://get.docker.com | sh
usermod -aG docker deploy
docker --version          # Engine 24+ required
docker compose version    # Compose v2 required
```

### 2.3 Caddy

Already installed in your case. Confirm it is the systemd service and not something you will
fight with later:

```bash
caddy version             # 2.7 or newer: the Caddyfile uses `import` arguments
systemctl status caddy
mkdir -p /var/log/caddy && chown caddy:caddy /var/log/caddy
```

### 2.4 The repository

```bash
sudo mkdir -p /opt/klarahome && sudo chown deploy:deploy /opt/klarahome
git clone <repo-url> /opt/klarahome
cd /opt/klarahome
```

Every command in this guide runs from `/opt/klarahome`. `--env-file` is resolved relative to the
working directory, so running from elsewhere silently gets you a different configuration.

### 2.5 DNS

Six records, all pointing at the VPS, all in place **before** Caddy is reloaded — Caddy fetches a
certificate the first time a name is requested, and an unresolvable name fails the ACME challenge
and backs off.

```
A   klarahome.example        -> <vps-ip>
A   www.klarahome.example    -> <vps-ip>
A   api.klarahome.example    -> <vps-ip>
A   admin.klarahome.example  -> <vps-ip>
A   cdn.klarahome.example    -> <vps-ip>
A   s3.klarahome.example     -> <vps-ip>
```

Add the matching `AAAA` records if the VPS has IPv6.

---

## 3. Configure the stack

```bash
cp .env.prod.example .env.prod
chmod 600 .env.prod
```

`.env.prod` holds every secret the platform has, and it is the only place they live. It is
git-ignored; it is never committed, never copied into an image, and never pasted into a ticket.
Back it up to a password manager separately from the database — several of its values cannot be
regenerated (§4).

Compose refuses to start when a required value is missing, naming it:

```
error while interpolating services.api.environment.[]: required variable IMAGE_TAG is missing a value:
IMAGE_TAG is required - deploy a specific tag, never `latest`
```

That is the intended behaviour. Fill in what it names and run the command again.

---

## 4. Generate the secrets

Do this once, on a machine you trust, and record every value in a password manager before you
paste it into `.env.prod`.

```bash
# Passwords: database, Redis, MinIO
openssl rand -base64 24        # POSTGRES_PASSWORD  (also goes in DATABASE_CONNECTION_STRING)
openssl rand -base64 24        # REDIS_PASSWORD
openssl rand -base64 24        # MINIO_ROOT_PASSWORD   (MINIO_ROOT_USER can be `klarahome`)

# imgproxy URL signing - 64 hex characters each. Without them cdn.<domain> is a free image
# proxy for anyone who finds it, billed to this server.
openssl rand -hex 32           # IMGPROXY_KEY
openssl rand -hex 32           # IMGPROXY_SALT

# Encryption keys - 32 bytes of base64 each, and DIFFERENT from each other.
openssl rand -base64 32        # AUTH_ENCRYPTION_KEY  (authenticator secrets, provider tokens)
openssl rand -base64 32        # ENCRYPTION_KEY       (sellers' bank account numbers)

# The RSA key access tokens are signed with. Print it as one line with \n escapes, then paste
# that line over the empty AUTH_SIGNING_KEY_PEM="" placeholder in .env.prod.
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out signing.pem
printf 'AUTH_SIGNING_KEY_PEM="%s"\n' "$(awk 'BEGIN{ORS="\\n"}1' signing.pem)"
shred -u signing.pem
```

(Compose's `.env` parser also accepts a real multi-line PEM inside double quotes, if you would
rather paste the file as it is. Both forms reach the container as the same key.)

| Value | Losing it costs |
|---|---|
| `AUTH_SIGNING_KEY_PEM` | Every signed-in user is signed out. Recoverable: generate a new one. |
| `AUTH_ENCRYPTION_KEY` | Enrolled authenticators stop working; users re-enrol. |
| `ENCRYPTION_KEY` | **Every seller's bank account number becomes unreadable, permanently.** |
| `IMGPROXY_KEY` / `SALT` | Every image URL already in a page 403s until they are restored. |

The key *identifiers* are written literally as `prod` in the compose file, because Compose
interpolates environment values but not their names. Rotating a key is therefore an edit to
`docker-compose.prod.yml`: add a second entry (`Encryption__Keys__prod2`) beside the current one,
then point `Encryption__CurrentKeyId` at it. The old entry stays until nothing encrypted with it
is left.

Also set, before the first migrator run:

```ini
DOMAIN=klarahome.example
TENANT_NAME=Klara Home
AUTH_BOOTSTRAP_EMAIL=you@klarahome.example
AUTH_BOOTSTRAP_PASSWORD=<a real password, at least 10 characters>
EMAIL_FROM_ADDRESS=no-reply@klarahome.example
```

The bootstrap password is validated: outside Development the seeder **refuses** any of the example
values from this repository's documentation, and refuses anything shorter than the configured
minimum.

---

## 5. Point Caddy at the stack

```bash
sudo cp infra/caddy/Caddyfile /etc/caddy/Caddyfile

sudo mkdir -p /etc/systemd/system/caddy.service.d
sudo tee /etc/systemd/system/caddy.service.d/override.conf >/dev/null <<'EOF'
[Service]
Environment=KH_DOMAIN=klarahome.example
Environment=KH_ACME_EMAIL=ops@klarahome.example
EOF

sudo systemctl daemon-reload
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

`KH_DOMAIN` must equal `DOMAIN` in `.env.prod`, and the ports in the Caddyfile must equal the
`*_PORT` values there. They are the same two numbers written in two files; when a site answers 502
this is the first thing to check.

What the Caddyfile does and deliberately does not do:

- **Terminates TLS** with an automatically issued and renewed Let's Encrypt certificate, and
  redirects HTTP to HTTPS.
- **Asserts security headers** — HSTS, `nosniff`, `DENY` framing, referrer and permissions policy.
  It does *not* assert a Content-Security-Policy: the applications own that, because they know
  their own script and image origins.
- **Does not rate-limit.** The API rate-limits per endpoint class — sign-in, OTP, cart writes,
  order placement — because only it can tell them apart (`appsettings.json`, `RateLimiting`).
- **Passes `Host` through unchanged** and sets `X-Forwarded-For` / `-Proto`. The API
  (`Api:TrustProxyHeaders`) and the storefront (`KH_TRUST_PROXY_HEADERS`) both depend on this.
- **Leaves the MinIO console unpublished.** Reach it through an SSH tunnel when you need it:
  `ssh -L 9001:127.0.0.1:9001 deploy@<vps>`.

After the first reload, watch the certificates arrive:

```bash
journalctl -u caddy -f | grep -i certificate
```

---

## 6. The first deploy

### 6.1 Choose where images come from

**Option A — build on the VPS.** Simplest, no registry.

```bash
export IMAGE_TAG=$(git rev-parse --short HEAD)
sed -i "s/^IMAGE_TAG=.*/IMAGE_TAG=$IMAGE_TAG/" .env.prod

docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod build
```

The Angular builds are memory-hungry; on a 4 GB box add swap first
(`fallocate -l 4G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile`), or
build elsewhere. A cold build of all five images takes 10–20 minutes.

**Option B — build in CI, pull here.** What you want once there is more than one deploy a week.
Set `REGISTRY=ghcr.io/your-org` and `IMAGE_TAG=<commit-sha>` in `.env.prod`, then
`docker login ghcr.io` and `docker compose ... pull`.

Either way, **tag with a commit SHA or a release version, never `latest`** — a rollback needs a
previous tag to go back to.

### 6.2 Create the database volume before anything else

Skip to §6.3 if the default location is fine — Docker creates the volume on first use, under
`/var/lib/docker/volumes/`. Read §7 first if you have a separate data disk: the volume must be
created *before* the first start, and moving it afterwards is a dump and restore.

### 6.3 Migrate and seed, then start

```bash
cd /opt/klarahome

# 1. Bring up the backing services only, and wait for them to be healthy.
docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod \
  up -d --wait postgres redis minio

# 2. Apply every module's migrations and run the seeders. This container exits.
docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod \
  --profile migrate run --rm migrator
echo "migrator exit: $?"     # 0 = go on. 1 = STOP, read the log, fix, re-run.

# 3. Start the applications.
docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod up -d

docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod ps
```

The API never migrates at startup — it refuses to boot in Production if
`Database:MigrateOnStartup` is true. Migrations are always this separate job, run to completion
and checked, so a restart never has two versions of the code disagreeing about the shape of a
table.

### 6.4 Smoke test

```bash
curl -fsS https://api.klarahome.example/health/live   && echo  # process is up
curl -fsS https://api.klarahome.example/health/ready  && echo  # postgres, redis, storage answer
curl -fsSI https://klarahome.example/ | head -1                # storefront: 200
curl -fsSI https://admin.klarahome.example/ | head -1          # back office: 200
curl -fsSI https://www.klarahome.example/ | head -1            # 301 to the apex
```

Then sign in as the administrator the seeder created. `platform-admin` is a mandatory-two-factor
role, so the first sign-in returns a challenge rather than a session:

```bash
# 1. Password. Answers with a challenge token, not a session.
curl -sX POST https://api.klarahome.example/api/v1/admin/auth/login \
  -H 'content-type: application/json' \
  -d '{"email":"you@klarahome.example","password":"..."}'

# 2. Enrol. Returns a base32 secret and an otpauth:// URI to scan.
curl -sX POST https://api.klarahome.example/api/v1/admin/auth/2fa/enrol \
  -H 'content-type: application/json' -d '{"challengeToken":"<from step 1>"}'

# 3. The code from your authenticator app completes the sign-in.
curl -sX POST https://api.klarahome.example/api/v1/admin/auth/2fa/verify \
  -H 'content-type: application/json' \
  -d '{"challengeToken":"<from step 1>","code":"123456"}'
```

Once that account exists and has a second factor enrolled, **blank
`AUTH_BOOTSTRAP_EMAIL` and `AUTH_BOOTSTRAP_PASSWORD` in `.env.prod`** and redeploy. The seeder
already does nothing while any administrator exists; removing the credential removes a live
password from a file on disk.

Finally, register the webhook URLs with the providers you use:

| Provider | URL |
|---|---|
| Razorpay | `https://api.<domain>/api/v1/webhooks/razorpay` |
| Shiprocket | `https://api.<domain>/api/v1/webhooks/shipping/shiprocket` |
| Google / Facebook sign-in | `https://api.<domain>/api/v1/store/auth/external/<provider>/callback` |

---

## 7. The database volume

The database lives on a **named Docker volume**, not a bind mount, and not inside the container:

```
volume name : klarahome-postgres-data          (COMPOSE_PROJECT_NAME + "-postgres-data")
mounted at  : /var/lib/postgresql              inside the container
PGDATA      : /var/lib/postgresql/18/docker    set by the image — never override it
on the host : /var/lib/docker/volumes/klarahome-postgres-data/_data
```

```bash
docker volume inspect klarahome-postgres-data     # where it is, when it was created
docker system df -v | grep postgres-data          # how big it has grown
```

Three things about it that are worth knowing before you learn them the hard way:

**`PGDATA` is set by the image.** The PostgreSQL 18 images declare the volume at
`/var/lib/postgresql` and put the cluster in `18/docker` beneath it. Setting `PGDATA` to something
else initialises a *second*, empty cluster inside the same volume, and the first one looks lost
even though it is still there.

**The collation is fixed at creation.** `POSTGRES_INITDB_ARGS` (`--locale-provider=icu
--icu-locale=en-IN`) applies the first time the volume is initialised and never again. It matches
development so that index and `ORDER BY` behaviour is identical; changing it later means a dump
and restore.

**`docker compose down` keeps the volume. `docker compose down -v` destroys it.** There is no
confirmation and no undo. Never type `-v` against production; use `down` alone, or
`restart`/`up -d` for everything routine.

### 7.1 Putting the volume on a separate disk

If the VPS has a second disk (or you want the database off the root filesystem), create the volume
**before the first `up`** — an existing volume with the right name is reused as it is:

```bash
sudo mkdir -p /mnt/data/klarahome-postgres

docker volume create \
  --driver local \
  --opt type=none \
  --opt device=/mnt/data/klarahome-postgres \
  --opt o=bind \
  klarahome-postgres-data
```

Make sure `/mnt/data` is mounted at boot in `/etc/fstab`; a volume whose device is missing at
start gives you an empty database rather than an error you would notice.

To move an existing volume, take a dump (§8), recreate the volume against the new device, and
restore. Copying `_data` between filesystems while PostgreSQL is running does not produce a usable
cluster.

---

## 8. Seeding, backups and restore

### 8.1 What the seeders write

The migrator applies migrations and then runs every registered seeder, in order. All of them are
**idempotent**: they are re-run on every deploy and write only what is missing.

| Seeder | What it puts in an empty database |
|---|---|
| `Identity.Permissions`, `Identity.SystemRoles` | The permission catalogue and the built-in roles |
| `Identity.BootstrapAdmin` | The first platform administrator, from `AUTH_BOOTSTRAP_*`. Does nothing once any administrator exists |
| `Platform.Tenant` | The tenant row, from `TENANT_*` |
| `Platform.ReferenceData` | Indian GST state and union-territory jurisdictions, and the HSN chapters |
| `Platform.StoreSettings` | Each store-settings section that has never been saved, at its defaults. Never overwrites one an operator has edited |
| `Platform.FeatureFlags` | Declared flags at their default state. Never re-asserts a flag an operator has changed |
| `Platform.Pincodes` | India Post PIN codes, **only** when `PINCODE_DATA_PATH` is set — see below |
| `Notifications.Templates` | The email and SMS templates |
| `Returns.Reasons` | The return-reason catalogue |

**Demo data is refused in Production**, at registration and again inside the seeder, whatever
`DemoData:SeedCatalog` says. A production catalogue is loaded through the back office or the
product import, not by a seeder.

### 8.2 PIN codes

The India Post dataset is roughly nineteen thousand rows that change without notice, so the
product does not ship it (`infra/seed/README.md` has the column format).

```bash
scp pincodes.csv deploy@<vps>:/opt/klarahome/infra/seed/pincodes.csv
```

Then set `PINCODE_DATA_PATH=/app/seed/pincodes.csv` in `.env.prod` and re-run the migrator. The
import matches on the PIN code and updates in place, so refreshing the file and re-running
corrects the data rather than duplicating it; rows whose `stateCode` is not a known jurisdiction
are counted, logged and skipped, so one bad row cannot fail a deploy.

Until the file exists, keep the `platform.pincode-lookup` feature flag off — otherwise every
lookup answers `PINCODE_NOT_FOUND`, which reads as a bug rather than as missing data.

### 8.3 Nightly backups

```bash
sudo install -m 700 -d /var/backups/klarahome
chmod +x /opt/klarahome/infra/scripts/backup-db.sh
sudo crontab -e
```

```cron
15 2 * * *  /opt/klarahome/infra/scripts/backup-db.sh >> /var/log/klarahome-backup.log 2>&1
```

The script dumps in PostgreSQL's custom format, **verifies the dump is readable** with
`pg_restore --list`, keeps 30 days, and prints what it did. Its last lines are where to add the
off-site copy: a backup that lives only on the VPS does not survive the VPS.

Object storage is backed up separately — `mc mirror` from the MinIO container to an off-site
bucket; the private bucket is versioned, so an overwrite is recoverable there.

### 8.4 Restoring

```bash
# Into a scratch database first. A restore drill that has never been done is not a plan.
docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod \
  exec -T postgres createdb -U klarahome klarahome_restore_test

docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod \
  exec -T postgres pg_restore -U klarahome -d klarahome_restore_test --no-owner \
  < /var/backups/klarahome/klarahome-20260901T021500Z.dump
```

To restore *over* production: stop `api` and `worker` first so nothing writes during the restore,
`dropdb`/`createdb`, `pg_restore`, then run the migrator (it will report nothing to do if the dump
is current) and start the applications again.

---

## 9. Moving the database to Neon or another managed provider

The application reads its database address from exactly one variable,
`DATABASE_CONNECTION_STRING`, which the API, the worker and the migrator all share. Nothing else
in the stack knows where the database is, so the move is an edit to `.env.prod`.

### 9.1 The connection string

Npgsql format — the provider gives you a `postgresql://` URL; translate it:

```ini
# In Docker, today:
DATABASE_CONNECTION_STRING=Host=postgres;Port=5432;Database=klarahome;Username=klarahome;Password=<POSTGRES_PASSWORD>;Maximum Pool Size=40;Timeout=15;Command Timeout=30

# Neon:
DATABASE_CONNECTION_STRING=Host=ep-cool-name-123456-pooler.ap-southeast-1.aws.neon.tech;Port=5432;Database=klarahome;Username=klarahome_owner;Password=<from Neon>;Ssl Mode=Require;Maximum Pool Size=20;Timeout=15;Command Timeout=30

# Any other managed provider (RDS, Supabase, DigitalOcean) - the same shape:
DATABASE_CONNECTION_STRING=Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<pass>;Ssl Mode=Require
```

| Keyword | Why |
|---|---|
| `Ssl Mode=Require` | Mandatory off-box. Npgsql 8+ validates the server certificate under `Require`, and Neon's is publicly trusted, so nothing else is needed. Do **not** add `Trust Server Certificate=true` — it turns validation off. |
| `Maximum Pool Size` | Each of the API and the worker keeps its own pool. Keep the total under the provider's connection limit; Neon's free tier is small, so 20 each is a sane start. |
| `Timeout` / `Command Timeout` | Connecting across a network, not a bridge. 15 s to connect and 30 s per command matches `Database:CommandTimeoutSeconds`. |
| *(pooled endpoint)* | Neon's `-pooler` host is PgBouncer in transaction mode. It is right for the API and the worker. |
| `No Reset On Close=true` | Add it **only** if the pooler complains about `DISCARD ALL`; it is the documented Npgsql setting for PgBouncer. |

**Run migrations against the direct (unpooled) endpoint.** DDL through a transaction-mode pooler
is the one thing that behaves differently. On Neon that is the same host without `-pooler`. Keep
both strings in `.env.prod` and point the migrator at the direct one if you split them.

### 9.2 The move

```bash
cd /opt/klarahome

# 1. Stop writers. The site is down for this window; it is a few minutes.
docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod stop api worker

# 2. Dump the container database.
docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod \
  exec -T postgres pg_dump -U klarahome -d klarahome -Fc > /tmp/cutover.dump

# 3. Restore into the managed database. --no-owner because the role names differ there.
docker run --rm -i -v /tmp:/tmp postgres:18.6-alpine \
  pg_restore --no-owner --dbname "postgresql://user:pass@<new-host>/klarahome?sslmode=require" \
  /tmp/cutover.dump

# 4. Point the application at it: edit DATABASE_CONNECTION_STRING in .env.prod.

# 5. Prove the schema is current against the new database. It should report nothing to apply.
docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod \
  --profile migrate run --rm migrator

# 6. Start the applications and smoke test (§6.4).
docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod up -d
```

### 9.3 Afterwards

- The `postgres` service is still declared, and `api`, `worker` and `migrator` still wait for it to
  be healthy. Leave it running for a week — it costs a little RAM and it is your rollback: put the
  old connection string back and restart, and you are on it again.
- When you are confident: `docker compose ... stop postgres`, then delete the `postgres` service
  block and the three `depends_on: postgres` entries from `docker-compose.prod.yml`, and remove
  `postgres-data` from the `volumes:` section. Keep the volume itself until a restore from the
  managed provider has been drilled — `docker volume rm klarahome-postgres-data` is final.
- `infra/scripts/backup-db.sh` no longer applies: the provider's point-in-time restore replaces
  it. Keep taking a periodic `pg_dump` anyway, so a restore is possible into something that is not
  them.
- `POSTGRES_*` in `.env.prod` then describe only the (stopped) container. The password in
  `DATABASE_CONNECTION_STRING` is the one that matters.

---

## 10. Routine operations

### Deploying a new version

```bash
cd /opt/klarahome
git pull

# 1. New tag.
sed -i "s/^IMAGE_TAG=.*/IMAGE_TAG=$(git rev-parse --short HEAD)/" .env.prod

# 2. Build or pull.
docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod build   # or: pull

# 3. Migrate first, and stop if it fails.
docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod \
  --profile migrate run --rm migrator || { echo "migration failed - not deploying"; exit 1; }

# 4. Recreate the applications. Compose replaces only what changed.
docker compose -f infra/compose/docker-compose.prod.yml --env-file .env.prod up -d

# 5. Smoke test (§6.4).
```

Migrations follow expand → migrate → contract, so the previous image stays compatible with the new
schema for the length of a rollback window. **Rollback** is therefore: put the previous
`IMAGE_TAG` back and `up -d`.

### Everyday commands

```bash
# A shorter alias, since every command repeats the same two flags.
alias khc='docker compose -f /opt/klarahome/infra/compose/docker-compose.prod.yml --env-file /opt/klarahome/.env.prod'

khc ps                              # what is running, and whether it is healthy
khc logs -f --tail 100 api          # follow one service
khc logs --since 1h worker          # the background loops
khc restart api
khc exec postgres psql -U klarahome -d klarahome
khc exec -T postgres psql -U klarahome -d klarahome -c 'select count(*) from catalog.products;'
khc stop                            # stop everything, keep the volumes
khc down                            # remove containers, KEEP the volumes
```

Never `khc down -v` on this machine.

### Changing configuration

Edit `.env.prod`, then `khc up -d`. Compose recreates only the containers whose configuration
changed. A change to the Caddyfile needs `sudo caddy validate --config /etc/caddy/Caddyfile &&
sudo systemctl reload caddy` instead — a reload is not a restart and drops no connection.

---

## 11. When something is wrong

| Symptom | Cause | Fix |
|---|---|---|
| `required variable X is missing a value` | A `${VAR:?}` in the compose file is unset | Fill it in `.env.prod`; the message names it |
| Every storefront URL answers **400** | `KH_ALLOWED_HOSTS` does not list the hostname Caddy passes through | It is derived from `DOMAIN`; check `DOMAIN` matches `KH_DOMAIN` in Caddy's systemd override |
| **502** from Caddy | The container is not up, or the port in the Caddyfile disagrees with `*_PORT` | `khc ps`; `curl -I http://127.0.0.1:8080/health/live` on the host |
| Browser blocks API calls with a CORS error | The origin is not in `Cors__AllowedOrigins` | They are derived from `DOMAIN`; if the shop is served from a different domain, add it |
| Sign-in appears to work, next request is anonymous | The refresh cookie was not stored | The API and the apps must be subdomains of one registrable domain; `Auth__Tokens__RefreshCookieSecure` must be true and the site HTTPS |
| Image URLs 403 at `cdn.<domain>` | `IMGPROXY_KEY`/`SALT` differ between imgproxy and the API | Both read the same two variables — check for a stale container: `khc up -d imgproxy api` |
| Uploads fail with a signature error | The browser reached MinIO under a name the API did not sign for | `MINIO_SERVER_URL`, `Storage__SignedUrlEndpoint` and Caddy's `s3.<domain>` must all be the same host |
| Migrator exits 1 | A migration or a seeder threw | Read the log it printed; nothing has started against the new schema, so fix and re-run |
| API restarts in a loop | A Production guard refused the configuration | `khc logs api` — it names the setting: `MigrateOnStartup`, sensitive logging, or a missing signing key |
| Storefront pages render empty with a network error | The renderer cannot reach `api.<domain>` from inside its container | Check the `extra_hosts` entry resolves and that 443 is reachable from the Docker bridge (§2.1) |
| Certificates never arrive | DNS does not resolve to this VPS, or 80 is blocked | `dig +short api.<domain>`; `ufw status`; `journalctl -u caddy -n 100` |

---

## 12. What this guide deliberately does not cover yet

These belong to steps that have not been executed. Doing them early is fine; pretending they are
done is not.

- **Observability** (Prometheus, Loki, Grafana, alerts) — Step 31. Today logs are JSON on stdout,
  captured by the Docker log driver and rotated at 10 MB × 5 per container.
- **A staging environment** — a second `COMPOSE_PROJECT_NAME` and `DOMAIN` on the same box gets
  you one with separate volumes and a separate database.
- **CI-driven deploys, image scanning, automated rollback and smoke tests** — Step 32.
- **Read-only container root filesystems** — `docs/06-infrastructure-devops.md` §4 calls for them;
  each image needs its writable paths established first, so they are not enabled here.
- **A Docker socket proxy** — nothing in this stack mounts the Docker socket, which is why there is
  none. Do not add a container that needs it without one.
