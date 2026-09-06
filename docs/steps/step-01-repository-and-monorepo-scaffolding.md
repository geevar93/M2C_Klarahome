# Step 1 — Repository & monorepo scaffolding

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** A · **Depends on:** Step 0
- **Objective:** Create the physical repository layout for backend, frontend and infrastructure
  with conventions locked in.
- **Deliverables:**
  - Git repository initialised; branch strategy (`main`, `develop`, `feature/*`, `release/*`)
    documented in `CONTRIBUTING.md`.
  - Folder structure per `01-architecture.md` §Repository Layout.
  - `.editorconfig`, `.gitignore`, `.gitattributes`, `Directory.Build.props`,
    `Directory.Packages.props` (central package management), `global.json` pinning .NET 10 SDK.
  - Nx workspace created for Angular apps (`storefront`, `admin`) with shared lib placeholders.
  - `README.md` at repo root with the "how to run" skeleton.
  - Commit message convention (Conventional Commits) + PR template.
- **Acceptance criteria:** `git log` shows an initial commit; folder tree matches the spec;
  `dotnet --version` and `npx nx --version` both resolve from the repo.
- **Outcome / Notes:** ✅ **DONE 2026-09-05.** Commits `a61acf9` (scaffold) and `98e5b7f`
  (frontend workspace) on `main`; working tree clean.
  **All four acceptance criteria met:** initial commit ✅ · folder tree matches spec ✅ ·
  `dotnet --version` → 10.0.203 ✅ · `npx nx --version` → Local v23.2.0 ✅.

  **Part 1 — repository scaffold** (commit `a61acf9`, 61 files):
  - Git repository initialised (`main`). Branch strategy documented in `CONTRIBUTING.md`.
  - Repository tree created per `01-architecture.md` §4.1 and §8: `src/backend`
    (`host/` × 3, `shared/` × 3, `modules/` × 17, `tests/` × 4), `src/frontend/{apps,libs}`,
    `infra/{docker,compose,traefik,observability,scripts}`, `tools/`, `.github/workflows/`,
    `docs/adr/`. Empty directories tracked via `.gitkeep`.
  - `global.json` pinning SDK **10.0.203**, `rollForward: latestFeature`. Verified:
    `dotnet --version` → `10.0.203` from the repo root.
  - `src/backend/Directory.Build.props` — `net10.0`, nullable, implicit usings, NET analyzers
    at `latest-recommended`, `EnforceCodeStyleInBuild`, deterministic builds, lock files.
    `TreatWarningsAsErrors` is conditioned on `ContinuousIntegrationBuild` so analyzers are
    errors in CI (Step 5) without blocking local development.
  - `src/backend/Directory.Packages.props` — central package management enabled with
    transitive pinning. **Deliberately no speculative version pins**: entries are added when a
    package is first genuinely needed and its version verified against the feed. The
    MediatR/AutoMapper licensing guard from ADR-009 is written into the file itself, where it
    will actually be seen.
  - `.editorconfig` (88 lines) encoding the C# conventions the architecture assumes:
    file-scoped namespaces as an error, `_camelCase` private fields, `Async` suffix rule,
    accessibility modifiers required.
  - `.gitignore` with secrets listed **first** (`.env*`, `*.pfx`, `*.key`, `*.pem`,
    `appsettings.Local.json`), plus .NET, Node/Angular/Nx, Docker and IDE sections.
  - `.gitattributes` — LF normalisation, binary declarations, generated API client marked
    `linguist-generated` so it stays out of diffs and language stats.
  - Conventional Commits enforced by `.githooks/commit-msg`, a **POSIX shell hook with no Node
    dependency** (deliberate: husky/commitlint would have been unusable given the Node
    blocker below). Verified against three cases — invalid subject rejected, valid accepted,
    `!` without a `BREAKING CHANGE:` footer rejected — and it validated the initial commit.
    Enabled per clone with `git config core.hooksPath .githooks`.
  - `.gitmessage` commit template wired via `git config commit.template`.
  - `CONTRIBUTING.md` — step protocol restated first, branch strategy, commit convention with
    the module scope list, code conventions per stack, secrets policy.
  - `.github/pull_request_template.md` whose checklist **is** the Definition of Done from
    `09-nfr-testing-observability.md` §2.4, plus an explicit scope check against step creep.
  - Root `README.md` with layout, prerequisites (actual verified versions) and next steps.

  **Part 2 — Nx/Angular workspace** (commit `98e5b7f`, 205 files). Initially blocked on
  Node v20.12.2 being below Angular's engine range; **User upgraded to Node 24.20.0 via the
  official MSI**, clearing it. Note the target moved from Node 22 to 24: `OpenJS.NodeJS.LTS`
  now resolves to the 24 line, which is the current Active LTS and satisfies `^24.15.0`.

  Generated with **Nx 23.2.0 / Angular 22.1**:
  - `apps/storefront` — SSR (`src/server.ts`), hydration with `withEventReplay()`, zoneless,
    standalone, routing, SCSS, `kh` selector prefix.
  - `apps/admin` — SPA, deliberately **no SSR** (authenticated and non-indexable, per
    `05-frontend-architecture.md` §3.1).
  - `apps/storefront-e2e`, `apps/admin-e2e` — Playwright.
  - 13 placeholder libraries exactly as specified in `05-frontend-architecture.md` §1:
    `data-access/{api,auth,cart,catalog,orders,content}`, `ui/{primitives,patterns,layout}`,
    `domain`, `util`, `i18n`, `testing`. Import paths `@klarahome/*`.
    `domain` and `util` are `@nx/js` libraries (framework-free by design); the rest are
    Angular libraries.
  - **Module boundaries enforced at lint time** via project tags and
    `@nx/enforce-module-boundaries` in `eslint.config.mjs`, encoding all three constraints
    from the spec. **Verified, not assumed:** a deliberate `ui → data-access` import was
    introduced, confirmed to fail lint with the correct message, and reverted.
  - **Both apps build.** storefront **72.35 kB** gzipped initial, admin **60.73 kB** — against
    the 180 kB budget in `09-nfr-testing-observability.md` §1.1. Zoneless verified structurally:
    `zone.js` is not a dependency at all (Angular 22 default).

  **Three deviations forced by current tooling** (recorded in `src/frontend/README.md`; none
  change the specification):
  1. **Nx 23 replaced legacy presets with opinionated templates.** `--preset=angular-monorepo`
     silently ignored `--appName` and generated `apps/shop` **and `apps/api`** — a Node backend
     directly contradicting our .NET backend — plus unrequested AI-agent scaffolding
     (`.claude/`, `CLAUDE.md`, `.cursor/`, `.codex/`, `.gemini/`, `.opencode/`, `AGENTS.md`)
     and a `packages/` layout. Discarded; rebuilt from the empty template with explicit
     generators so every project is one we actually specified.
  2. **The empty template ships the TypeScript project-references setup, which Angular does
     not support** ([angular/angular#37276](https://github.com/angular/angular/issues/37276));
     the app generator refuses to run against it. Converted to the classic setup —
     `tsconfig.base.json` with `paths`, no root solution `tsconfig.json`, `@nx/js/typescript`
     plugin removed from `nx.json`. The documented escape hatch
     (`NX_IGNORE_UNSUPPORTED_TS_SETUP=true`, "at your own risk") was **not** used.
  3. **`baseUrl` omitted** from `tsconfig.base.json` — TypeScript 6 deprecates it (TS5101) and
     it broke both builds; the generated `paths` are already `./`-relative so it is redundant.

  Environment verified at close: .NET SDK 10.0.203, Git 2.46.1, Docker 29.4.0,
  **Node v24.20.0**, npm 11.19.0.

  **Carried into later steps:** `eslint.config.mjs` currently holds *only* the boundary rule —
  the full Angular/TypeScript rule set, Prettier integration and CI wiring remain owned by
  Step 5 and Step 22, as does generating the `data-access-api` client from OpenAPI.
