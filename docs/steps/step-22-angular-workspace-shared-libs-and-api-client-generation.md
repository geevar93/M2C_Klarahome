# Step 22 — Angular workspace, shared libs & API client generation

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** F · **Depends on:** Step 21
- **Objective:** Frontend foundations shared by both apps.
- **Deliverables:**
  - Nx workspace with apps `storefront` and `admin`; libs `ui`, `data-access`, `domain`,
    `util`, `i18n`, `testing`.
  - Typed API client **generated from the backend OpenAPI document** as a CI step (no
    hand-written DTOs).
  - HTTP interceptors: auth/refresh, correlation ID, error normalisation, loading state, retry.
  - Core services: auth/session, runtime config bootstrap (`config.json`, so one image serves
    any environment), feature flags, analytics abstraction, toast/notification.
  - Placeholder design tokens wired as CSS custom properties (per
    `10-design-system-placeholder.md`) — **strictly neutral, no branding**.
  - Testing setup: Jest/Vitest + Testing Library, Playwright for e2e, MSW for API mocking.
  - Accessibility and i18n scaffolding (`en-IN` default), currency/number/date pipes for INR.
- **Build acceptance - what closes this step during the sprint:**
  1. Every item under **Deliverables** is written: entities, configuration, handlers, endpoints,
     module registration and DI wiring, and the EF migration for this module.
  2. The code compiles - `dotnet build src/backend/KlaraHome.sln` (backend) or
     `npx nx build <app>` (frontend) succeeds with no errors.
  3. The endpoints are registered and declare their permissions; the module is referenced by the
     host, and the migrator project compiles with the new migration.
  4. Every behaviour named in the full acceptance criteria below that has not been proved has a
     row in [`../TEST_DEBT.md`](../TEST_DEBT.md).
  5. Outcome / Notes filled in, Parking Lot updated, permission requested.
- **Full acceptance criteria (verified at Step 29, not now):** Both apps build and serve; the generated client compiles against the
  live OpenAPI document; a sample authenticated request succeeds through the interceptor chain.
- **Outcome / Notes:**

## Outcome

**Status: ✅ DONE — 2026-09-06.** The frontend now has a foundation both apps build on: a typed
client generated from the API's own contract, the interceptor chain every request goes through,
the services that make one image run anywhere, and a test harness that exercises all of it rather
than stubbing past it.

### The contract is now a build artefact

The central deliverable was "a typed API client generated from the backend OpenAPI document as a
CI step (no hand-written DTOs)". Three things had to exist for that, and none of them did:

1. **The document.** `OpenApiDocumentsDirectory` was set in `KlaraHome.Api.csproj` back at Step 3
   but was inert, because `Microsoft.Extensions.ApiDescription.Server` — the package that acts on
   it — was never referenced. It is referenced now, with `PrivateAssets="all"` so nothing it
   brings reaches the runtime image, and it writes `artifacts/openapi/KlaraHome.Api.json`.

   The export is **off for an ordinary build** (`OpenApiGenerateDocuments` defaults to false) and
   on only for the codegen step. That is not tidiness: exporting starts the host in-process to
   read its route table, which is seconds of work and a set of moving parts that no developer
   pressing F5 should have to care about.

2. **A generator.** `tools/generate-api-client.mjs`, ~470 lines, no dependencies. Reads the
   document and writes `libs/data-access/api/src/generated/` — one `models.ts` (494 interfaces and
   string-union enums) and one injectable client per OpenAPI tag (21 of them, 489 methods).

   Off-the-shelf was considered and rejected on two grounds. `openapi-generator` needs a JVM the
   frontend toolchain does not otherwise have. The JVM-free generators each impose their own HTTP
   abstraction, and this project has a specific one — an interceptor chain plus a
   `HttpContext`-driven retry and loading policy that generated code has to opt into per call.
   The deciding factor is that the input is **not arbitrary OpenAPI**: it is what ASP.NET Core 10
   emits, which is a narrow and stable subset (`$ref`, objects with `required`, arrays, string
   enums, `type: ["null", T]`, `oneOf: [null, $ref]`). The generator handles exactly that and
   **throws on anything else** rather than guessing — so an unhandled construct is a failed build
   with a message, not a silently wrong type.

3. **A gate.** `tools/generate-api-client.ps1 -Check` regenerates and compares; `tools/ci.ps1
   -Stage codegen` runs it, and the workflow runs that in the **backend** job, because exporting
   needs the API assembly that job has just built. A change to an endpoint that is not matched by
   a regenerated client now fails CI with a message naming the drift.

### What was built, by library

| Library | What is in it now |
|---|---|
| `data-access/api` | The generated client; `ApiTransport` (the one place generated code touches `HttpClient`); the `HttpContext` tokens that carry per-request policy; `ApiError`/`ProblemDetails`; four of the five interceptors |
| `data-access/auth` | `SessionStore` (access token in memory only), `AuthService` with the **single-flight** refresh, `authInterceptor`, three route guards, and `provideKlaraHomeHttp` — the one place the interceptor order is decided |
| `util` | Runtime config (browser and server), feature flags, analytics facade, toasts, loading counter, SSR-safe storage, UUIDs, and the a11y scaffolding (`LiveAnnouncer`, `RouteFocusManager`) |
| `domain` | Framework-free `Money` (compare and display; **no** money arithmetic) and the Indian format validators — PIN code, mobile, GSTIN, PAN, IFSC |
| `i18n` | `en-IN` locale registration and the `khMoney` / `khDate` / `khRelativeTime` / `khNumber` / `khCompactNumber` pipes |
| `ui/primitives` | `styles/` — the placeholder tokens, the accessibility baseline, and the breakpoint mixin. No components yet; those are Step 23's |
| `testing` | MSW handlers and server, `configureKlaraHomeTestingModule`, and the harness test that proves the whole chain |

### Decisions worth recording

- **The interceptor order is `loading → retry → error normalisation → correlation id → auth`,**
  and it is stated once in `provideKlaraHomeHttp` rather than repeated per app. Loading is
  outermost so the bar spans the retries instead of blinking off between attempts; retry sits
  above error normalisation so a retried-then-successful request never toasts its first failure;
  the correlation id is set *below* retry so every attempt of one logical request shares an id in
  the server's log; auth is innermost because it is the only one that replays a request itself.

- **Retry is decided by the HTTP method, not the URL.** A GET may be repeated because the server
  promises it changes nothing — and a POST may too, **but only if it carries an
  `Idempotency-Key`**, which is the server's promise that the same key twice is the same order
  once. That is exactly what the key is for, and it is why a dropped connection during checkout
  does not cost the customer a second order. `Retry-After` always beats the computed backoff, and
  the backoff has full jitter so a recovering API is not knocked over by its own clients.

- **The refresh is single-flight.** Five requests finding an expired token at once produce one
  refresh, not five. With a rotating refresh token, five racing refreshes would invalidate four
  of themselves, and the server — correctly — would read the reuse as a stolen token and revoke
  the family. This is not an optimisation; without it the app signs the user out whenever a page
  loads two things at once.

- **The access token is held in memory and nowhere else.** What survives a reload is the HttpOnly
  refresh cookie the page cannot read, so a new tab proves itself by calling `/auth/refresh`.
  `withCredentials` is on for every API call, because the cookie is on the API's origin and not
  the app's.

- **`apiBaseUrl` carries no path.** The version segment is part of each generated method's route,
  because `/api/v1` belongs to the contract the client was generated from rather than to where
  that contract is deployed. Documented in `.env.example` with the failure mode spelled out.

- **Configuration is read before the injector exists.** `loadRuntimeConfig()` fetches
  `config.json` in `main.ts` before `bootstrapApplication`; the server entry reads `process.env`
  instead. That is what allows `API_BASE_URL` to be a real DI token rather than a mutable global
  every consumer must remember to read late — and it is what makes one image serve every
  environment and every tenant.

- **Zoneless change detection** in both apps, per `05-frontend-architecture.md` §2.

### Files and configuration changed outside the deliverables

- **`eslint.config.mjs`** — `domain` is now a **true leaf** (`onlyDependOnLibsWithTags: []`) and
  `util` may depend on it, which is the correct DAG and what lets `khMoney` take a `Money`.
  `testing` gained its own `type:testing` tag, allowed to reach every layer because a harness
  legitimately spans them. Nothing may depend on it in return: a **`no-restricted-imports`** rule
  refuses `@klarahome/testing` and `msw` from any file that is not a spec, because a tag sees a
  project and a spec file lives inside the library it tests. **Both guards were verified to fire**
  against a deliberate violation.
- **`jest.preset.js`** — `jest-fixed-jsdom` (jsdom lacks `Request`/`Response`, and MSW is built on
  them), `customExportConditions: ['']`, and an explicit ESM-package transform list. Put in the
  shared preset rather than one project's config because every library that tests through MSW or
  touches Angular's locale data hits the same three failures.
- **`.prettierrc`** — `printWidth: 110`, matching `.editorconfig`'s `max_line_length = 120`.
  `.prettierignore` excludes `generated/`, so `--check` compares the client to the contract rather
  than to a style rule.
- **`.env.example`** — a new FRONTEND section documenting the `KH_*` variables and the
  `KH_FEATURE_<key>` encoding (`__` is a dot, `_` is a hyphen).
- **`10-design-system-placeholder.md` §2** — one token added, `--touch-target-min: 44px`. §4
  already required 44 × 44; this makes it a value a component can read instead of a number
  retyped in every stylesheet. Recorded in `CHANGE_LOG.md`.
- **`apps/*/project.json`** — `stylePreprocessorOptions.includePaths: ['libs']` and the
  `@angular/localize/init` polyfill.
- **`app.routes.server.ts`** — `RenderMode.Prerender` → `RenderMode.Server`. A prerender runs at
  build time, where there is no API and no runtime configuration, so a prerendered catalogue page
  would ship whatever the build machine failed to fetch. The real per-route split is Step 23's.

### One backend fix, and why it could not wait

`SearchOptions.BaseUrl` carried `[Url]`, which **rejects the empty string**. Blank is the
documented and normal value — `.env.example` ships it blank, and ADR-019 says PostgreSQL's own
full text is the only supported engine today — so options validation failed at start-up for every
deployment that is not running a dedicated search engine, which is all of them. It is now a
`[RegularExpression]` that admits blank or an absolute http/https URL.

This was found because the OpenAPI export starts the host, and it blocked the step's central
deliverable outright. Fixed rather than parked, and called out here because protocol rule 6 would
otherwise have sent it to the Parking Lot.

### Verification

| Gate | Result |
|---|---|
| `nx run-many -t build --projects=storefront,admin --configuration=production` | ✅ storefront **81.6 kB** gzipped initial, admin **71.0 kB** — both well inside the 180 kB budget |
| `nx run-many -t lint --all` | ✅ 17 projects |
| `nx run-many -t test --all --configuration=ci` | ✅ 15 projects |
| `prettier --check .` | ✅ clean |
| `dotnet build KlaraHome.sln -c Release -warnaserror` | ✅ 0 warnings, 0 errors |
| Backend unit + architecture suites | ✅ **941** unit tests; 13 of 14 architecture tests — the one failure is the Content module's public DTOs, **RED since Step 20** and already parked |
| `tools/ci.ps1 -Stage codegen` | ✅ exports 489 operations / 494 schemas and reports the client in sync |

The two headline behaviours the harness test proves end to end: a generated method's request
reaches MSW carrying a correlation id the interceptor stamped, and a ProblemDetails failure comes
back as an `ApiError` carrying the server's `code`.

### Known gaps

- **Performance budgets are not yet enforced.** `05-frontend-architecture.md` §3.4 states the
  budget in **gzipped** kilobytes and Angular's budget mechanism measures **raw** bytes, so a
  literal 180 kB budget would fail a build that is comfortably inside the real limit. Recorded in
  `TEST_DEBT.md` for Step 29 rather than encoded wrongly now.
- **Nothing is proved against the live API.** The full acceptance criteria — both apps serving,
  the client compiling against a running document, an authenticated request through the chain —
  need a booted stack, which is Step 28A's. 18 `TEST_DEBT.md` rows.
- The feature-facing `data-access` libraries (cart, catalog, content, orders) and the `ui`
  component libraries still hold Step 1's placeholder components. They belong to Steps 23–28.
