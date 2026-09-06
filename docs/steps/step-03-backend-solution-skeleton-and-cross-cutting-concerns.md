# Step 3 — Backend solution skeleton & cross-cutting concerns

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** A · **Depends on:** Step 2
- **Objective:** A running ASP.NET Core 10 API host with the modular-monolith skeleton and all
  cross-cutting concerns in place — no business features.
- **Deliverables:**
  - Solution + projects per `01-architecture.md` §Backend Structure
    (`KlaraHome.Api`, `KlaraHome.SharedKernel`, `KlaraHome.Modules.*` template).
  - Module registration convention (each module self-registers endpoints, DI, DB schema).
  - Cross-cutting: structured logging (Serilog), OpenTelemetry wiring, global exception
    handler + RFC 9457 `application/problem+json` error model, request correlation IDs,
    FluentValidation pipeline, rate limiting, CORS, response compression, output caching.
  - Health endpoints: `/health/live`, `/health/ready`.
  - OpenAPI document generation + Scalar/Swagger UI in non-production.
  - Options/configuration binding with validation on startup (fail fast).
  - `Dockerfile` for the API (multi-stage, non-root, trimmed runtime image).
- **Acceptance criteria:** API container builds and runs; `/health/ready` returns 200;
  OpenAPI document renders; a deliberate exception returns a correct ProblemDetails payload.
- **Outcome / Notes:** ✅ **DONE 2026-09-05.**

  **All four acceptance criteria met, verified against the running container rather than by
  inspection:**
  1. **API container builds and runs.** `docker compose ... up -d --build api` produces
     `klarahome/api:dev` (202 MB) and the container reports `(healthy)`. It runs as a non-root
     user and publishes **no host port** — Traefik at `https://api.klarahome.localhost` is the
     only way in, exactly as on the VPS.
  2. **`/health/ready` returns 200**, and reports all three checks: `self`, `postgres`, `redis`.
     `/health/live` returns 200 independently, so a database blip cannot restart the container.
  3. **OpenAPI document renders**: `/openapi/v1.json` → `200`, `openapi: 3.1.1`, correct
     `info.title`/`version`, `operationId: metaGet`. The Scalar reference at `/scalar` → `200`.
  4. **A deliberate exception returns a correct ProblemDetails payload**:
     `GET /api/v1/diagnostics/boom` → `500 application/problem+json` with
     `type`, `title`, `status`, `detail`, `instance`, `code: UNEXPECTED_ERROR` and a
     `correlationId`. Asserted in the integration tests to contain **no** exception message and
     **no** stack trace.

  **Solution created** — `src/backend/KlaraHome.sln`, 10 projects, folder layout exactly per
  `01-architecture.md` §4.1:
  - `shared/KlaraHome.SharedKernel` — `Result`/`Result<T>`, `Error` + `ErrorType`, `Money`
    (`decimal`, scale 4, banker's rounding, currency-safe arithmetic), `IClock`/`SystemClock`,
    `Guard`, `Entity<TId>`/`AggregateRoot<TId>`, `IDomainEvent`. **Zero dependencies** — no
    package, no framework reference — enforced by an architecture test.
  - `shared/KlaraHome.Contracts` — `IIntegrationEvent` / `IntegrationEvent` base record
    (UUIDv7 event id, occurred-at, correlation id). The only assembly a module may use to reach
    another module.
  - `shared/KlaraHome.Infrastructure` — every cross-cutting concern (below).
  - `modules/KlaraHome.Modules.Platform` — **the module template**: the `Domain/ Application/
    Infrastructure/ Endpoints/` layout plus `PlatformModule : IModule`. It registers no services
    and maps no endpoints; the Platform feature set is Step 6. It exists so the discovery
    convention is exercised end to end by a real module assembly.
  - `host/KlaraHome.Api` — composition root. `host/KlaraHome.Worker` and
    `host/KlaraHome.Migrator` — host shells with configuration, logging and their exit-code
    contract; their actual work belongs to the steps that introduce it.
  - `tests/KlaraHome.UnitTests`, `tests/KlaraHome.IntegrationTests`,
    `tests/KlaraHome.ArchitectureTests`.

  **Module registration convention** — `IModule` (Name, Schema, Order, `AddServices`,
  `MapEndpoints`), `ModuleRegistry` (assembly discovery, deterministic ordering, and a startup
  failure if two modules claim the same name or the same Postgres schema), and
  `AddModules`/`MapModules`. Each module is handed the `/api/v1` route **group**, never the raw
  route builder, so no module can escape the versioned prefix. Adding a module is a project
  reference plus one line in `ModuleAssemblies`.

  **Cross-cutting concerns, all in place:**
  - **Logging** — Serilog to stdout; `CompactJsonFormatter` in containers, a readable template
    for `dotnet run`. Every mandatory enricher from `09-nfr-testing-observability.md` §3.1 is
    present and was verified on a real log line: `correlationId`, `tenantId`, `userId`,
    `vendorId`, `traceId`, `spanId`, `module`, `environment`, `version`. **PII masking is a
    Serilog destructuring policy**, not the caller's responsibility: a `[Pii]` attribute with
    Full/LastFour/Email strategies, plus a name-based safety net (`password`, `otp`, `mobile`,
    `email`, `token`, …) that masks un-annotated DTOs anyway.
  - **OpenTelemetry** — traces and metrics for ASP.NET Core, HttpClient and the runtime, one
    `ActivitySource`/`Meter` named `KlaraHome`, parent-based ratio sampling, health probes
    filtered out. The OTLP exporter is wired only when an endpoint is configured, so local
    development pays nothing and Step 31 is a configuration change.
  - **Error model** — RFC 9457 in one place. `ErrorType` → status/`type`/`title` is the single
    implementation of the table in `04-api-specification.md` §1.2, with a stable machine-readable
    `code`, `correlationId`, and field-level `errors`. A global `IExceptionHandler` turns
    anything unhandled into a 500 that carries only a correlation id. Framework-produced
    responses (404, 405, 415) are rewritten to the same shape, so a client cannot tell whether a
    404 came from routing or from a handler.
  - **Correlation** — `X-Correlation-Id` accepted or minted, published on a scoped context and on
    the current `Activity`, echoed on every response including error responses. An inbound value
    that could forge a log line or a header is discarded rather than propagated.
  - **Dispatcher** — in-house `ICommand`/`ICommand<T>`/`IQuery<T>` with handler interfaces, an
    open-generic pipeline, and assembly-scanned registration (ADR-009; no MediatR). Reflection is
    a one-off cached wrapper per request type, never per request.
  - **FluentValidation pipeline** — a validation behaviour ahead of every handler, producing one
    422 shape from one place, with .NET property paths converted to the JSON paths clients see
    (`Lines[2].Quantity` → `lines[2].quantity`).
  - **Rate limiting** — all seven buckets from `04-api-specification.md` §6 as **configuration**,
    a global per-IP backstop, and named policies endpoints opt into. A refusal returns
    `429 application/problem+json` with `Retry-After`, `X-RateLimit-Limit`, `-Remaining`,
    `-Reset`.
  - **CORS** — explicit origin allow-list per environment, credentials for the refresh cookie,
    and the rate-limit and correlation headers exposed to the browser.
  - **Response compression** (Brotli + gzip) and **output caching** with an opt-in-only base
    policy, so nothing authenticated can be cached by accident.
  - **Options binding with fail-fast validation** — `AddValidatedOptions<T>` binds, validates
    data annotations and cross-field rules, and runs it all at host start. A misconfigured
    container refuses to boot instead of serving errors nobody traces back to an env var.
    Covered by four integration tests.
  - **Health** — `/health/live` (process only) and `/health/ready` (adds PostgreSQL and Redis
    when connection strings are configured), both anonymous, both exempt from rate limiting,
    both excluded from the OpenAPI document, both `no-store`.
  - **OpenAPI** — .NET 10 generation with document and operation transformers (document
    metadata, guaranteed `operationId`, universal error responses); Scalar UI in non-production
    only.

  **`infra/docker/api.Dockerfile`** — multi-stage; lockfile-first restore so a `.cs` edit does
  not invalidate it; publishes with `ReadyToRun`; runtime layer is
  `aspnet:10.0-noble-chiseled` (no shell, no package manager), non-root, OCI-labelled with
  version and commit SHA. The image build sets `ContinuousIntegrationBuild=true`, so **analyzer
  warnings are errors in the image build** — it caught a real issue immediately (see deviation 4).
  Because a chiseled image has no shell or curl, the container HEALTHCHECK is the application
  itself: `KlaraHome.Api --healthcheck` performs an HTTP probe and exits 0/1. Verified: exit 0,
  Docker reports `healthy`. `.dockerignore` added at the repository root, secrets first.

  **Tests — 112, all green, stable across repeated runs, and with no third-party assertion
  library (ADR-012):**
  - **63 unit** — Money arithmetic, rounding and currency safety; Result/Error semantics;
    dispatcher routing for commands, commands-with-response and queries; behaviour ordering;
    validator-before-handler; missing-handler failure; validation path camel-casing; module
    discovery, ordering, duplicate-name and duplicate-schema rejection; PII masking; the full
    `ErrorType` → status/type mapping.
  - **37 integration** — the real host through `WebApplicationFactory`: both probes, the meta
    endpoint, the OpenAPI document, correlation echo/generation/sanitisation, the 500 contract
    (asserting the exception message and stack trace are *absent*), every error type on the
    wire, 422 field errors, a business-rule refusal, a malformed body, rate-limit refusal with
    its headers, probes exempt from limiting, and four fail-fast configuration tests.
  - **12 architecture** (NetArchTest + reflection) — no module references another module or a
    host; exactly one `IModule` per module assembly, constructible, with a distinct lowercase
    schema; **a module exposes nothing publicly except its module class**; a module `Domain`
    namespace has no dependency on EF Core, ASP.NET Core, Npgsql or Redis; SharedKernel depends
    on nothing outside the BCL; Contracts depends on nothing but SharedKernel and carries no
    open implementation; Infrastructure never references a module.

  **Compose** — an `api` service added to `infra/compose/docker-compose.dev.yml`: built from the
  repository root, joined to both networks, `depends_on` the three data services being
  *healthy*, resource-limited, health-checked, and routed by Traefik at
  `api.${DEV_DOMAIN}` through the existing security-header and compression middlewares.

  **Docs updated** — `README.md` (API URLs, backend build/test/run commands), `docs/dev-setup.md`
  (API endpoint table, rebuild loop, hand-checks for the error contract, request tracing, four
  new troubleshooting rows, two new dev/prod differences), `.env.example` (the new API container
  knobs), `docs/adr/` (index + ADR-011).

  ---

  **Five deviations, each recorded rather than absorbed:**

  1. **No third-party assertion library — a specification change, made with the User.**
     `09-nfr-testing-observability.md` §2.1 named FluentAssertions; version **8.0.0 onward is
     commercially licensed** (Xceed), which is exactly the liability ADR-009 exists to prevent
     for a redistributed product. Three options were measured rather than estimated, including
     the Apache-2.0 fork **AwesomeAssertions**, which proved to be a one-line-per-file migration
     with all tests passing. **The User chose to remove the dependency entirely** — the objection
     being that pinning or forking still leaves the product holding a package that has already
     re-licensed once. Assertions now use **xUnit v3's built-in `Assert`**, which is Apache-2.0
     and already required to run the tests at all. ~300 assertions across 12 files were
     rewritten; the suite went from 108 to **112** tests (one assertion became a 5-case theory)
     and all pass. Recorded as **ADR-012 (Accepted)**, with the rejected pin kept as **ADR-011**
     so the reasoning trail survives. `09-nfr-testing-observability.md` §2.1 updated accordingly
     — the only change to docs `01`–`10` in this step.
     **Verified, not assumed:** a module boundary rule was deliberately broken to confirm the
     rewritten assertions still fail correctly. They do, and the failure message now names the
     rule *and* the offending type — better diagnostics than the fluent form gave.
  2. **`docker-compose.base.yml` was still not created**, contrary to the note left at Step 2
     that the base/dev split would happen here. There is still nothing to share: the dev file
     remains the only compose file, and staging/production compose files are created at Step 32.
     A base file whose only consumer is the dev file would be ceremony, not structure. The split
     now belongs to Step 32 and is in the Parking Lot.
  3. **The solution file is a classic `.sln`, not the `.slnx` the .NET 10 SDK now defaults to.**
     `01-architecture.md` §4.1 specifies `KlaraHome.sln`; `dotnet new sln -f sln` was used to
     honour it, and to keep every tool that reads a solution working.
  4. **`.editorconfig` changed in two ways** (a Step 1 artefact). `dotnet_style_require_
     accessibility_modifiers` moved from `always` to `for_non_interface_members`, because
     interface members cannot idiomatically carry a modifier and the rule flagged every one of
     them; and `CA1716` was silenced, because `Error` and `Result` are the SharedKernel's domain
     vocabulary and the rule only protects VB/F# consumers this C#-only codebase does not have.
     Separately, `.editorconfig` is now copied into the Docker build — without it the image
     build applied different rule severities from a developer's machine, which is exactly the
     kind of drift warnings-as-errors is meant to catch.
  5. **`dotnet test` now needs the Microsoft.Testing.Platform runner**, declared in
     `global.json` (`"test": { "runner": "Microsoft.Testing.Platform" }`). The .NET 10 SDK no
     longer runs xUnit v3 through VSTest. Consequences worth knowing at Step 5: the CLI is
     `dotnet test --project <path>` (a bare path argument is rejected), and
     `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are **not** referenced — xUnit v3
     is its own runner.

  **Known gaps, deliberate and recorded (all in the Parking Lot):**
  - `X-RateLimit-*` headers are emitted on a **429 only**. Reporting `Remaining` on a successful
    response needs a custom limiter that surfaces the lease; the framework's does not.
  - Output caching is **in-memory**. It moves to Redis with the `HybridCache` work; only
    `OutputCacheExtensions` changes.
  - Handler registration is **assembly scanning at startup**, not the source-generated
    registration `01-architecture.md` §4.3 anticipates. One-off cost at boot, nothing per
    request; source generation is a later optimisation, not a correctness matter.
  - `TenantOptions` is configuration-bound so logs can carry `tenantId` today. Step 6 replaces
    it with database-backed, admin-editable settings; the variable names do not change.
  - `Api__EnableDiagnosticsEndpoints` gates a small Development-only surface
    (`/api/v1/diagnostics/{boom,echo,error}`) used to prove the error contract by hand and in
    tests. It requires **both** the Development environment and the flag, and is excluded from
    the OpenAPI document.
