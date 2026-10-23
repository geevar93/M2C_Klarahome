# Running on the shared VPS infrastructure

How Klara Home runs against the PostgreSQL, Redis and MinIO instances that every application on a
VPS shares (`/opt/infra`). It supersedes the per-app data containers described in
[deployment-vps.md](deployment-vps.md). Production configuration lives outside this repository.

Images are built on a developer machine and pulled by the VPS:
`ghcr.io/geevar93/klarahome-{api,worker,migrator,storefront,admin}`. The migrator runs once before
the API on every deploy. The server never builds.

## What the application needs from each shared service

### PostgreSQL

- A database `klarahome` owned by a login that is **not** a superuser (`NOSUPERUSER NOCREATEDB
  NOCREATEROLE NOBYPASSRLS`). The same login is used by the API, the worker and the migrator.
- Every migration and every seeder runs under such a login. The four extensions the schema uses
  (`btree_gin`, `pg_trgm`, `pgcrypto`, `unaccent`) are *trusted* extensions in PostgreSQL 13 and
  later, so the database owner may create them; nothing needs `CREATE ROLE`, `ALTER SYSTEM`,
  server-side `COPY` or any other superuser capability.
  `NonSuperuserMigrationTests` proves it on every CI run.
- Migrations are applied **only** by the migrator. `Database__MigrateOnStartup` is refused in
  Production by the API and worker, and nothing in either host would act on it anyway.
- The migrator exits `0` on success and `1` on any failure; a deploy must stop on a non-zero exit.
- Seeding runs only in the migrator (`Database__RunSeeders`, default `true`), after the migrations.
  The seeders are idempotent: tenant, permissions, system roles, reference data, store settings,
  notification templates, feature flags, commission plans, shipping rate card, return reasons, and
  the first administrator (created only while no administrator exists). Demo seeders are off unless
  `DemoData__SeedCatalog=true`, and are refused in Production.
- Restoring a dump taken under the old superuser role into the new database: use
  `pg_restore --no-owner --role=klarahome_user` so objects are owned by the new login.

### Redis

- The only Redis use is the readiness probe (`PING`). No key is written and no channel is
  published today.
- The connection string carries the ACL user: `redis:6379,user=klarahome,password=...`.
- `Redis__KeyPrefix` (default `klarahome:`) is applied to every channel the connection uses, so a
  feature added later cannot step outside the ACL's `~klarahome:*` / `&klarahome:*`.
- The client is configured so that normal operation issues no command that ACL refuses
  (`RedisConnection.CreateOptions`): no `CONFIG GET`, no read of the `__Booksleeve_TieBreak` key
  and no subscription to the `__Booksleeve_MasterChanged` channel. `RedisSharedServerTests` runs
  the probe against Redis 8 with the shared server's ACL and asserts the ACL log stays empty.

### MinIO (S3)

- The application never creates buckets, sets policies or changes versioning, and never lists
  buckets. It uses `PutObject`, `GetObject`, `DeleteObject` and pre-signed `GetObject` URLs only.
- A policy that works, verified against MinIO with a user holding nothing else:

  ```json
  {"Version":"2012-10-17","Statement":[
   {"Effect":"Allow","Action":["s3:ListBucket","s3:GetBucketLocation"],
    "Resource":["arn:aws:s3:::media-public","arn:aws:s3:::docs-private"]},
   {"Effect":"Allow","Action":["s3:GetObject","s3:PutObject","s3:DeleteObject"],
    "Resource":["arn:aws:s3:::media-public/*","arn:aws:s3:::docs-private/*"]}]}
  ```

  `s3:ListBucket` is required even though nothing lists: without it a read of a missing key
  answers 403 instead of 404, which the storage health check and every "is it there" read treat
  as a failure.
- `media-public` must stay anonymously readable (imgproxy fetches from it without credentials,
  and `Storage__PublicBaseUrl` points browsers at it). `docs-private` must not be.
- Nothing relies on `docs-private` being versioned. A delete on a versioned bucket leaves a
  delete marker and the application reads the key as absent, as it should.
- Pre-signed URLs are signed for `Storage__SignedUrlEndpoint` (`https://s3.klarahome.in`). MinIO
  validates them against the `Host` header it receives, so the proxy must pass the original host
  through (Caddy does by default); `MINIO_SERVER_URL` is not needed.

### Email

`Email__Provider=none` (or empty) sends nothing. Each message is recorded in the delivery log as
`Suppressed / NoProvider`, and one line is logged: `A <event> message on <channel> was not sent:
<reason>.` It carries no recipient, subject or body. Any value other than `smtp`, `none` or empty
stops the host at startup, so a misspelling cannot silently stop all email.

## Signing keys

`Auth__Tokens__SigningKeys__N__KeyId` / `__PrivateKeyPem` form a ring of RSA keys for RS256 access
tokens. **The key at index 0 signs; every key in the ring validates.** The signing key's `KeyId`
is written into each token's `kid` header.

- Format: an RSA private key in PEM (PKCS#8 `BEGIN PRIVATE KEY` or PKCS#1 `BEGIN RSA PRIVATE KEY`),
  2048 bits or more; use 3072.

  ```
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out signing.pem
  ```

  The value may be supplied multi-line, on one line with spaces between the lines, or on one line
  with literal `\n` between them; all three parse.
- Access tokens last 15 minutes. Refresh tokens are opaque random values stored hashed in the
  database, **not** signed, so replacing the key does not invalidate any session: for up to 15
  minutes after the change a held access token is refused with 401, and a client that then calls
  the refresh endpoint with its cookie receives a token signed by the new key. A client that does
  not retry after a 401 shows one sign-in prompt instead. A support session started by
  impersonation (it has no refresh token) and a half-finished two-factor sign-in end, which is
  acceptable.
- **Replacing a key that must be treated as compromised**: put the new key at index 0 with a new
  `KeyId` and **leave no other entry**. Do not keep the old key "to validate in-flight tokens": it
  would keep accepting tokens forged with it.
- Rotating a healthy key without any 401: new key at index 0, old key at index 1, deploy; after
  the longest access-token lifetime (15 minutes, plus 30 seconds of clock skew) remove index 1.
- Outside Development a host with no usable key refuses to mint or validate a token, but the key
  ring is built when it is first needed rather than at startup, so a missing or malformed key may
  not stop the container from reporting healthy. Sign in once after any deploy that touches the
  keys.

## Data-at-rest keys: never change

`Auth__Encryption__CurrentKeyId` / `Auth__Encryption__Keys__<id>` and
`Encryption__CurrentKeyId` / `Encryption__Keys__<id>` are AES-256-GCM keys (base64 of 32 bytes)
that protect values the application must read back:

| Section | Protects | If the key is lost or replaced |
|---|---|---|
| `Auth__Encryption__*` | authenticator (TOTP) secrets and the external sign-in state cookie | every user with two-factor enabled can no longer pass the second factor; the cookie only affects sign-ins in progress |
| `Encryption__*` | vendors' bank account numbers | stored accounts decrypt to nothing; payouts to those vendors cannot be made until the numbers are re-entered |

Each stored value carries the id of the key that wrote it, so the id and the key bytes are both
part of the data. Carry over the **same id and the same bytes** from the existing environment. To
rotate, add a second key under a new id, point `CurrentKeyId` at it, and keep the old one in the
ring for as long as any row written under it exists. Never remove or overwrite an entry.
Unlike the signing key, these are not rotated as a response to a leak of the signing key.

## Containers

| Image | Port | User | Health check |
|---|---|---|---|
| `klarahome-api` | 8080 | `app` (chiseled, non-root) | `/app/KlaraHome.Api --healthcheck` (exit 0 when `/health/live` answers 200); readiness at `/health/ready` |
| `klarahome-worker` | none | `app` (chiseled, non-root) | none: it serves no HTTP; `restart: unless-stopped` and the process exiting on a fatal error are the signal |
| `klarahome-migrator` | none | `app` (chiseled, non-root) | not applicable: one-off, exit code is the result |
| `klarahome-storefront` | 4000 | `node` | `wget -q -O /dev/null http://127.0.0.1:4000/favicon.ico` |
| `klarahome-admin` | 8080 | nginx (master runs as root) | `wget -q -O /dev/null http://127.0.0.1:8080/config.json` |

The chiseled runtime images contain no shell and no curl, which is why the API probes itself.
The admin's nginx listens on IPv4 only (`listen 8080;`); address it as `127.0.0.1` or by container
name, never `localhost`, which resolves to `::1` first in Alpine.
