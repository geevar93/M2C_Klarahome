# Step 6 — Platform module: tenancy, settings, branding, audit

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** B · **Depends on:** Step 5
- **Objective:** Make the platform **re-distributable**: nothing about "Klara Home" is
  hard-coded, and a second business can be onboarded onto separate infrastructure by
  configuration alone.
- **Deliverables:**
  - `tenant` table + `tenant_id` propagation convention (ambient tenant context, EF query
    filters), defaulted to a single tenant for this deployment.
  - Store/brand configuration: legal entity, GSTIN, addresses, support contacts, logos,
    locale, currency, timezone, business rules (returns window, COD limits).
  - Typed application settings store (DB-backed, cached, admin-editable) + feature-flag service.
  - Immutable audit log (who/what/when/before/after) with a queryable API.
  - Reference data: Indian states + union territories, PIN code metadata, HSN chapters.
- **Acceptance criteria:** All branding/legal strings resolve from configuration; an audit
  entry is written for a settings change; a feature flag toggles observable behaviour.
- **Outcome / Notes:** ✅ **DONE 2026-09-05.**

  **Every deliverable is built, and all three acceptance criteria were demonstrated against the
  running dev stack rather than only in tests.** 46 files added, 17 changed, 2 migrations,
  7 endpoints, 73 new tests.

  #### Acceptance criteria - verified against the running containers

  | Criterion | Evidence |
  |---|---|
  | All branding/legal strings resolve from configuration | `GET /api/v1/store/config` returns branding, legal, support, localization and commerce as data. A fresh database seeds the store name from `TENANT_NAME` and the locale/timezone from `TENANT_DEFAULT_*`. Nothing about Klara Home is compiled into a code path |
  | An audit entry is written for a settings change | `PUT /api/v1/admin/settings/branding` then `GET /api/v1/admin/audit-logs?entityType=StoreSetting&entityId=branding` returned the entry with the real before/after documents, the client IP, the user agent and the request's correlation id |
  | A feature flag toggles observable behaviour | `PUT /api/v1/admin/feature-flags/platform.public-store-config {"enabled":false}` then the next `GET /api/v1/store/config` answered **404 `FEATURE_DISABLED`**; turning it back on returned **200**. No deploy, no restart |

  #### What was built

  **Tenancy.** `platform.tenants` (code unique, name, status), seeded from configuration by
  `TenantSeeder`. **Configuration remains the source of the tenant's identity, deliberately** - see
  deviation 1 below. `ConfiguredTenantContext` is unchanged, so the query filters and the auditing
  interceptor from Step 4 needed no edit at all; that indirection did its job.

  **Store settings.** `platform.store_settings` (`tenant_id`, `key`, `value jsonb`, `is_public`,
  unique on `(tenant_id, key)`), read through a typed section contract in `KlaraHome.Contracts`:
  `BrandingSettings`, `LegalSettings`, `SupportSettings`, `LocalizationSettings`,
  `CommerceSettings`. A section carries its own key, its own visibility and its own defaults as
  static interface members, so one place answers "what is this called, may the storefront see it,
  and what does it hold before anyone edits it". Adding a section is a record in Contracts plus one
  line in `SettingsCatalog`; the seeder, the admin surface, the public document and the typed
  reader all follow from that list.

  Inbound documents are parsed with `UnmappedMemberHandling.Disallow`, so a misspelled field in a
  settings `PUT` is a **422 naming the field** rather than a silent no-op that leaves an operator
  believing they changed something. Every section has a FluentValidation validator: GSTIN, PAN and
  CIN formats, E.164 phone numbers, hex colours, ISO currency and country codes, and the statutory
  48-hour / one-month grievance SLAs from `07-security-compliance.md` §6. Every rule permits an
  *empty* value and rejects only a value that is present and wrong, because a fresh install has to
  be configurable in whatever order the operator works.

  **Feature flags.** `platform.feature_flags` (`key`, `enabled`, `rollout jsonb`, `description`)
  with an evaluator supporting a master switch, an explicit user allow-list, named segments and a
  percentage rollout bucketed by SHA-256 over `(key, userId)` - stable per user, and different per
  flag so two 10 % rollouts do not land on the same tenth of the audience. An unknown key is
  **off**, so a typo hides a feature rather than exposing an unfinished one. Flags are *declared in
  code* by the module that reads them and then seeded; the admin surface reconfigures them but
  cannot invent one, because a flag nothing is wired to looks exactly like a flag that does not
  work.

  **Audit trail.** `platform.audit_logs` is the one table EF does not generate, and its migration
  is hand-written for three reasons EF cannot express:

  - it is `PARTITION BY RANGE (occurred_at)` with **26 monthly partitions** created from the month
    before the deploy (`03-database-design.md` §8), plus
    `platform.ensure_audit_log_partition(date)` to create more;
  - it is **append-only, enforced by a trigger** that rejects `UPDATE` and `DELETE` outright, so
    the guarantee holds against `psql` and not only against our own code
    (`07-security-compliance.md` §7). Retention stays possible: dropping a partition is DDL;
  - it carries a **`DEFAULT` partition**, so an insert can never fail because nobody created next
    month's - an audit write that threw would take the operation it is recording down with it.

  `IAuditLogger` in Contracts is what other modules will use. An entry is written in **its own
  scope and its own transaction**, after the change it describes has committed: the caller's
  context may be carrying unsaved work, and an audit call that quietly committed it would be a bug
  nobody would look for there. Actor, IP, user agent, correlation id, tenant and timestamp are read
  from ambient context, never passed by the caller.

  The query API is keyset-paginated on `(occurred_at, id)` descending - the table's own partition
  and index order - filtered by entity type, entity id, actor, action and time range. No `total`:
  counting years of append-only history would cost more than the page, which is exactly what
  `04-api-specification.md` §1.1 allows a collection to say.

  **Reference data.** 28 states and 8 union territories with their **GST state codes**, and the 99
  HSN chapters. Both are compiled in and seeded, because a wrong GST state code is a wrong tax on
  every invoice for that state. Codes 25 and 28 are deliberately absent - GSTN retains them only so
  historic returns parse. Reference tables carry **no `tenant_id`**: they are identical in every
  deployment and nobody edits them.

  **Endpoints** (`04-api-specification.md` §3.6, §4):

  | Method | Route | Notes |
  |---|---|---|
  | `GET` | `/api/v1/store/config` | Public settings and feature flags. Gated by `platform.public-store-config` |
  | `GET` | `/api/v1/store/states` | Output-cached with the reference-data policy |
  | `GET` | `/api/v1/store/pincodes/{pincode}` | Gated by `platform.pincode-lookup`. Serviceability is Step 16 |
  | `GET` | `/api/v1/admin/settings` | All sections with their current values |
  | `PUT` | `/api/v1/admin/settings/{key}` | Replaces a section as a whole; audited |
  | `GET` / `PUT` | `/api/v1/admin/feature-flags[/{key}]` | Audited |
  | `GET` | `/api/v1/admin/audit-logs` | Keyset-paginated search |

  #### The Step 7 gap, held open on purpose

  The admin surface exists before the authorisation that protects it does. Rather than leave that
  to be noticed later, it is made impossible to deploy:

  - every admin endpoint declares its permission with
    `.RequirePermission("platform.settings.manage")`;
  - `UnsecuredEndpointGuard` **refuses to start any non-Development host** while endpoints declare
    permissions and no authentication scheme is registered. Development logs a warning on every
    start instead;
  - an integration test fails if an endpoint under `/api/v1/admin` stops declaring one, so the list
    cannot grow silently.

  Step 7 registers the scheme and turns these declarations into policies; the guard then goes quiet
  on its own.

  #### Two real defects found and fixed along the way

  1. **`TenantOptions` was never bound in the worker or the migrator.** Both call
     `AddKlaraHomePersistence` but not `AddKlaraHomeInfrastructure`, so `IOptions<TenantOptions>`
     fell back to its built-in defaults. It had gone unnoticed because the default code happens to
     be `klarahome`; set `TENANT_CODE=acme` and the migrator would have seeded a *different tenant*
     from the one the API serves, silently. Binding moved into `AddKlaraHomePersistence`, where the
     ambient tenant is registered.
  2. **The connection string was captured at registration time** in `AddModuleDbContext`. A host
     whose configuration is completed after registration - which is exactly what
     `WebApplicationFactory` does - registered every context against an empty string. It is now
     resolved from the container when the context is built.

  Neither was reachable before this step; both are the kind that surface as "the deployment is
  mysteriously empty" rather than as an error.

  #### Deviations from the specification

  | # | Deviation | Why |
  |---|---|---|
  | 1 | **Configuration, not the database, is the source of the tenant's identity.** The Step 4 Parking Lot anticipated "a database-backed tenant"; the row is seeded *from* configuration instead | v1 is single-tenant-per-deployment (§6 defers real multi-tenant SaaS), and this step's objective is that a second business is onboarded *by configuration alone*. A tenant id also has to exist before any table can be written to, including the tenants table itself. The risk that entry recorded - changing `Tenant:Code` strands every row - is closed instead by a **`platform-tenant` readiness check**, so a replica that would serve an empty catalogue is *not ready* rather than quietly wrong |
  | 2 | **`tenants.settings jsonb` from `03-database-design.md` §4.1 is not created** | It would be a second, untyped, unaudited place to put settings alongside `store_settings`, which is typed, audited and admin-editable. One of the two had to win |
  | 3 | **`hsn_codes` is seeded at chapter level with `default_gst_rate` null** | GST rates are notified at four, six and eight digits. A plausible-looking chapter rate is exactly the kind of wrong number that reaches an invoice. Step 12 attaches real rates to real tariff items |
  | 4 | **`pincodes` ships empty**; the dataset is mounted, not built in | Roughly 19,000 rows that change without notice from India Post. A stale copy inside a container is worse than an empty table an operator knows to fill. `infra/seed/README.md` documents the format, `PINCODE_DATA_PATH` points at it, and the `platform.pincode-lookup` flag exists so the endpoint can stay off until the import has run |
  | 5 | **Enums are serialised as their names across the whole API** (`JsonStringEnumConverter`) | `"actorType": 4` breaks the day a value is inserted into the middle of an enum, and tells a support engineer nothing. Not a specification change - §1 does not say either way - but it is an API-wide convention, recorded so it is not rediscovered |
  | 6 | **`IAppendOnly` added to the SharedKernel**, excusing an entity from the `xmin` concurrency token | PostgreSQL refuses to return a system column from a partitioned table: `INSERT ... RETURNING xmin` fails with `0A000`. An append-only table has no lost update to detect either, so the marker is honest rather than a workaround |
  | 7 | **`CacheReferenceData()` wraps `CacheOutput()` in `KlaraHome.Infrastructure`** | `Microsoft.AspNetCore.OutputCaching` is not on a module project's compile reference set even with an explicit `FrameworkReference`, though `KlaraHome.Infrastructure` resolves it. Wrapping it there is better anyway: the policy name now lives next to the policy |
  | 8 | **The `store_settings` and `feature_flags` caches are in-process** | Exact for the single-replica deployment this ships as; eventually-consistent within the TTL (5 min and 1 min) the moment there are two. Redis-backed `HybridCache` is the fix already deferred from Step 3 |

  #### Tests and coverage

  **252 backend tests green** (was 179): 139 unit, 99 integration, 14 architecture. The integration
  suite gained a migrated-and-seeded PostgreSQL fixture and drives the real host over HTTP against
  it, so a settings change that is not stored, or a flag that does not reach an endpoint, fails
  rather than passing against a stub.

  | Assembly | Line % | Branch % |
  |---|--:|--:|
  | KlaraHome.Api | 98.0 | 84.8 |
  | KlaraHome.Contracts | 98.1 | 100.0 |
  | KlaraHome.Modules.Platform | 94.2 | 77.5 |
  | KlaraHome.Infrastructure | 92.4 | 75.3 |
  | KlaraHome.SharedKernel | 70.6 | 61.8 |
  | **Total** | **92.44** | **75.54** |

  Every assembly is now above the 70 % line floor, which the Step 5 Parking Lot recorded as the
  reason a per-assembly gate could not be turned on. Turning it on is still a Step 29 decision, but
  nothing is standing in its way.

  `tools/ci.ps1 all` passes end to end: format, 252 tests, coverage, lint, `dotnet list package
  --vulnerable` clean, and both Angular production builds.
