# Step 4 — Database foundation, EF Core & migration pipeline

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** A · **Depends on:** Step 3
- **Objective:** Establish data-access conventions and a repeatable migration workflow before
  any table exists.
- **Deliverables:**
  - EF Core 10 + Npgsql; one `DbContext` per module, one Postgres **schema** per module.
  - Base entity types, UUIDv7 key generation, `created_at`/`updated_at`/`created_by` auditing
    interceptor, optimistic concurrency token, soft-delete convention + global query filter.
  - Money value object mapped to `numeric(18,4)` + currency code; snake_case naming convention.
  - Transactional **Outbox** table + dispatcher hosted service (for integration events).
  - Migration strategy: design-time factory, `--idempotent` SQL script generation, migration
    runner container/job (migrations never run automatically from the API in production).
  - Seed/bootstrap data mechanism (roles, settings, tax rates, states/PIN reference data).
- **Acceptance criteria:** A sample migration creates its schema and applies cleanly to the
  dev Postgres container; idempotent script generation verified; audit columns populate.
- **Outcome / Notes:** ✅ **DONE 2026-09-05.**

  **All three acceptance criteria met, verified by running the artefacts rather than by
  inspection:**
  1. **A sample migration creates its schema and applies cleanly to the dev Postgres container.**
     The `migrator` job container was built (`klarahome/migrator:dev`, 196 MB) and run against the
     live dev PostgreSQL after `DROP SCHEMA platform CASCADE`: exit **0**, one migration applied,
     `platform.outbox_messages`, `platform.inbox_messages` and
     `platform.__ef_migrations_history` created, four extensions installed. Run again immediately:
     exit **0**, *"Module Platform is up to date"*, **0** migrations applied.
  2. **Idempotent script generation verified.** `./tools/ef.sh script Platform` produced an
     idempotent SQL file, which was then applied **twice** to a freshly created database with
     `psql -v ON_ERROR_STOP=1`. Both runs succeeded and the resulting schema was correct — the
     guarantee tested, rather than the presence of `IF NOT EXISTS` in the text.
  3. **Audit columns populate.** Asserted against a real PostgreSQL 18 container: an insert that
     sets *neither* `tenant_id`, `created_at` nor `created_by` comes back with all three
     populated; an update stamps `updated_at`/`updated_by` and provably does **not** let a caller
     rewrite `created_at`/`created_by`.

  **Data-access conventions** — `docs/03-database-design.md` §1, applied to every module model
  from one place (`ModelConventions`), after the module's own mapping so a convention can be
  overridden deliberately but never has to be restated:
  - **snake_case** identifiers via `EFCore.NamingConventions` 10.0.1 (Apache-2.0, by the Npgsql
    provider's maintainer). Chosen over a hand-rolled convention because it rewrites *every*
    identifier kind — tables, columns, keys, indexes, constraints, sequences — including the ones
    nobody remembers to test.
  - **UUIDv7 keys** generated in application code through a single greppable call site
    (`UuidV7.New()`), so `Guid.NewGuid()` — which would scatter every insert across the
    primary-key B-tree — is visible in review. `UuidV7.TimestampOf` makes "was this key generated
    the way we require?" answerable in a test, and it correctly rejects a v4.
  - **Auditing interceptor** fills `tenant_id`, `created_at/by`, `updated_at/by` and the
    soft-delete columns on every save, for every context. An interceptor rather than a base-class
    override, so it cannot be bypassed by a context that forgets to call `base.SaveChangesAsync`.
    It also refuses to let an update rewrite creation, and refuses to let an update move a row
    between tenants — that is not an update, it is a data leak.
  - **Optimistic concurrency** on PostgreSQL's own `xmin` system column: no column, no trigger, no
    write amplification. Verified with two contexts racing one row.
  - **Soft delete** as an opt-in marker plus a global query filter; `Remove()` on a soft-deletable
    entity is converted into an update, so nobody has to know which entities are soft-deleted and
    no accidental `DELETE` escapes through a cascade.
  - **Tenant scoping** as a second, *named* global query filter (EF Core 10 supports several per
    entity) plus a `tenant_id` index. The filter closes over the context, not over the id, so one
    cached model serves every tenant.
  - **Money** as an EF complex property mapping to `numeric(18,4)` + `*_currency_code char(3)`
    defaulted to INR — a value, not an entity, so EF neither tracks it separately nor gives it a
    table. Round-tripped at four decimal places and at a magnitude no `double` could hold.
  - **`timestamptz`** for every instant, so "when did this happen" has one answer wherever the
    query ran.

  **Transactional outbox** (ADR-003, and **ADR-013** written at this step):
  - `platform.outbox_messages` and `platform.inbox_messages` per `03-database-design.md` §4.1,
    with the partial index the dispatcher's only query needs
    (`(occurred_at) WHERE processed_at IS NULL`), so polling stays the size of the backlog rather
    than the size of the history.
  - `IOutbox` is resolved **per `DbContext`**, so a publish joins the transaction the handler is
    already in. Publishing outside that transaction is not discouraged, it is unavailable.
    Proved both ways: a commit writes the row and the event together; a failure writes neither.
  - The DDL is owned by exactly one context (`OwnsMessagingTables`); every other module context
    maps the same tables with `ExcludeFromMigrations()`, centrally, so there is nothing for a
    module author to remember. A startup validator refuses to boot when zero or several contexts
    claim ownership.
  - **`OutboxDispatcher`** hosted service: claims rows with `FOR UPDATE SKIP LOCKED`, so more than
    one worker is safe by construction rather than by convention; delivers in `occurred_at` order;
    marks processed; records `attempts` and the last error; stops retrying at `MaxAttempts` so a
    poison message cannot hot-loop; and marks a message no handler claims as processed rather than
    letting it block the queue. It runs in the **worker only** — every API replica polling would
    multiply the work and contend for the same rows.

  **Migration pipeline:**
  - One `DbContext` per module, one Postgres schema per module, and — importantly — one
    `__ef_migrations_history` **per schema**. Sharing one history table would let the first module
    to migrate convince every later one that it was already up to date.
  - `MigrationRunner` resolves contexts from the descriptors that registration leaves behind, so
    the migrator composes modules without referencing any of them.
  - A **design-time factory** that reuses the *same* `ConfigureKlaraHome` call the runtime
    registration makes, so the model `dotnet ef` generates a migration from is the model the
    application runs.
  - `tools/ef.ps1` / `tools/ef.sh` derive the three `dotnet ef` arguments from a module name, and
    `script` writes the **idempotent SQL** that `03-database-design.md` §7 requires to be reviewed
    in the PR rather than only the C#.
  - `infra/docker/migrator.Dockerfile` — multi-stage, lockfile-first restore, chiseled, non-root,
    OCI-labelled, and deliberately **no HEALTHCHECK**: a health check on a job that is meant to
    exit reports unhealthy the moment it succeeds. The compose service sits in a `migrate`
    **profile**, so a plain `up` never races it against the API — the same shape the VPS deploy
    will use.
  - `dotnet-ef` 10.0.11 pinned in a **local tool manifest** (`.config/dotnet-tools.json`), so
    every clone and CI get the same tool version rather than whatever is installed globally.

  **Seeding mechanism:** `IDataSeeder` (idempotent and re-runnable by contract, upsert by natural
  key — never "insert if the table is empty"), ordered, run by the migrator after migrations, and
  switchable off for a schema-only restore drill. A failing seeder names itself in the error and
  stops the run rather than leaving a half-seeded database that looks like a success.
  **No production seeder ships at this step** — every candidate dataset (roles, permissions,
  settings, tax rates, states, PIN codes) belongs to the module whose step introduces it. The
  mechanism is proven by tests, not by inventing data early.

  **Fail-fast configuration:** `DatabaseOptions` is bound and validated at startup, and the host
  **refuses to boot in Production** with `MigrateOnStartup` or `EnableSensitiveDataLogging` set.
  The first would let a rolling restart run two versions of the code against two shapes of a
  table; the second writes personal data into the logs.

  **Tests — 179, all green** (up from 112), no third-party assertion library (ADR-012):
  - **102 unit** (+39) — the built EF model asserted without ever opening a connection: schema
    placement, snake_case tables and columns, Money's two columns and their types, "money is never
    a floating-point type", the `xmin` token, `timestamptz`, tenant index and filter, soft-delete
    filter, *and that an entity opting into nothing gets no filters at all*; the outbox's partial
    index and the inbox's composite key; per-schema migration history; UUIDv7 generation, ordering
    and rejection of a v4; outbox serialisation stability and type-name resolution; seeder
    ordering, re-runnability and failure reporting; deterministic tenant-id derivation.
  - **63 integration** (+26) — against a real PostgreSQL 18 started by Testcontainers and pinned
    to the image the dev stack runs: audit columns, update semantics, soft delete, tenant
    isolation, tenant immutability, `xmin` concurrency conflict, Money round-trips, snake_case in
    `information_schema`; the real `MigrationRunner` applying to an empty database and re-running
    clean, extensions present, history in the module schema, seeders run and skipped; the outbox
    committing and rolling back with its cause; and the **dispatcher driven end to end** through
    its own `StartAsync` — delivery, no re-delivery, failure recorded, retry budget exhausted,
    unhandled message not blocking the queue, and occurrence ordering.
  - **14 architecture** (+2) — the existing rules, plus **a module `DbContext` must be internal**
    and **a module must own the schema its context writes to**.

  ---

  **Four deviations, each recorded rather than absorbed:**

  1. **The outbox is written across a schema boundary, and that is deliberate.**
     `03-database-design.md` §4.1 places `outbox_messages` in the `platform` schema, while
     `01-architecture.md` §2.1 forbids cross-schema access. Both are honoured: the outbox is
     infrastructure, has no foreign keys, is never *read* by a module, and is only ever appended
     to — in the module's own transaction, which is the entire guarantee the pattern provides.
     Written up in full as **ADR-013 (Accepted)**, including the two alternatives rejected
     (a queue per module schema loses cross-module causal ordering; a separate `DbContext` loses
     the transaction, which is the whole point). **No change to docs `01`–`10`.**
  2. **The migrator image runs on `aspnet`, not `runtime`.** The migrator serves no HTTP, so the
     smaller base was the intent — but it references `KlaraHome.Infrastructure` for the module
     registry, options binding and logging, and that assembly carries a `FrameworkReference` to
     `Microsoft.AspNetCore.App`. The `runtime` image fails at launch. Recorded, with the
     Infrastructure split in the Parking Lot rather than smuggled into this step.
  3. **Four PostgreSQL extensions are created by the initial migration.** `03-database-design.md`
     §1 requires five; `pgcrypto`, `pg_trgm`, `unaccent` and `btree_gin` are created here because
     extensions are database-wide and the first module to migrate is the honest place for them —
     a `CREATE EXTENSION` discovered halfway through the Search module is an unplanned production
     change. The fifth, `pg_stat_statements`, needs `shared_preload_libraries`, which is a server
     setting rather than a migration; it belongs to the Postgres container configuration at
     Step 31 and is in the Parking Lot.
  4. **Generated EF code is exempted from two rules.** `.editorconfig` no longer applies the
     hand-written style rules to `**/Migrations/*.cs`, and the architecture rule *"a module
     exposes nothing publicly"* now exempts generated migration and model-snapshot classes.
     `dotnet ef` emits both as `public partial`, and partial declarations cannot disagree about
     accessibility, so the alternative was hand-editing every generated file — a step that gets
     forgotten once and then quietly never done again. The exemption is narrow and paid for: a
     **new** rule asserts that a module's `DbContext` itself is internal, which is the type that
     actually matters.

  **Two bugs the tests caught before they could ship** (recorded because they are the argument
  for writing them):
  - The `OutboxDispatcher` opened its own transaction while retry-on-failure was enabled. EF
    refuses that — it cannot re-run a unit of work whose boundaries it does not own — so the
    dispatcher would have thrown on its first poll in every environment. Fixed by giving the base
    context one sanctioned `ExecuteInTransactionAsync`, which wraps the transaction in the
    execution strategy in the required order, so no module rediscovers this.
  - `MigrationRunner` wrapped `MigrateAsync` in an explicit transaction, which suppresses the
    advisory lock EF takes to stop two migrator containers migrating at once, and aborted the
    transaction on a first run when the provider probed for the not-yet-existent history table.

  **Known gaps, deliberate and recorded (all in the Parking Lot):**
  - **No production seeder exists yet** — the mechanism is complete and tested; the data belongs
    to Steps 6, 7 and 12.
  - **No partitioning.** `03-database-design.md` §8 wants monthly range partitions on
    `audit_logs`, `stock_ledger_entries`, `tracking_events`, `notification_messages` and
    `search_queries`. None of those tables exists yet; partitioning is created with the table, not
    retrofitted.
  - **No Dapper.** `01-architecture.md` §4.3 pairs EF with Dapper for hot read paths. Nothing is
    hot yet, and adding a second data-access library before there is a query that needs it would
    be speculation.
  - **The outbox retry has no stored backoff.** Retries are spaced by the poll interval and capped
    by `MaxAttempts`, which is a budget in polls rather than milliseconds. `occurred_at`,
    `attempts` and `error` are exactly the columns §4.1 specifies; a `next_attempt_at` column
    would be a schema change beyond the spec.
  - **`platform.idempotency_keys` is not created.** It is listed in §4.1, but it serves the
    `Idempotency-Key` API concern from `01-architecture.md` §6, which no step has yet built. It is
    created by the step that enforces it.
