# Klara Home — Frontend workspace

Nx **23.2.0** monorepo · Angular **22.1** · zoneless · standalone · SCSS · Playwright e2e

Generated at Step 1. Feature work belongs to Steps 22–28 — see
[`docs/IMPLEMENTATION_PLAN.md`](../../docs/IMPLEMENTATION_PLAN.md).

## Projects

| Project | Path | Notes |
|---|---|---|
| `storefront` | `apps/storefront` | **SSR** (`src/server.ts`), hydration with event replay. Customer-facing, mobile-first, SEO-critical |
| `storefront-e2e` | `apps/storefront-e2e` | Playwright |
| `admin` | `apps/admin` | SPA, **no SSR** (correct: authenticated, non-indexable). Platform staff + vendors, role-scoped |
| `admin-e2e` | `apps/admin-e2e` | Playwright |

## Libraries

| Library | Import path | Tags |
|---|---|---|
| `data-access-api` | `@klarahome/data-access-api` | `type:data-access-api` — **generated** from OpenAPI at Step 22; never hand-edit |
| `data-access-auth` | `@klarahome/data-access-auth` | `type:data-access` |
| `data-access-cart` | `@klarahome/data-access-cart` | `type:data-access` |
| `data-access-catalog` | `@klarahome/data-access-catalog` | `type:data-access` |
| `data-access-orders` | `@klarahome/data-access-orders` | `type:data-access` |
| `data-access-content` | `@klarahome/data-access-content` | `type:data-access` |
| `ui-primitives` | `@klarahome/ui-primitives` | `type:ui` |
| `ui-patterns` | `@klarahome/ui-patterns` | `type:ui` |
| `ui-layout` | `@klarahome/ui-layout` | `type:ui` |
| `domain` | `@klarahome/domain` | `type:domain` — framework-free models, money/GST helpers |
| `util` | `@klarahome/util` | `type:util` |
| `i18n` | `@klarahome/i18n` | `type:util` |
| `testing` | `@klarahome/testing` | `type:util` |

All libraries are **empty placeholders**. They exist so the dependency graph and its
constraints are enforceable from the first line of feature code.

## Module boundaries (enforced at lint time)

`eslint.config.mjs` encodes the constraints from
[`docs/05-frontend-architecture.md`](../../docs/05-frontend-architecture.md) §1:

- an app may not import another app (`scope:storefront` ⇄ `scope:admin` isolation)
- `ui` may not import `data-access` — presentation stays free of transport concerns
- only `data-access` may import `data-access-api` — apps and `ui` never touch the generated client
- `domain` and `util` are framework-free leaves

Verified working: a deliberate `ui → data-access` import fails lint with
`@nx/enforce-module-boundaries`.

## Commands

```bash
npx nx build storefront      # SSR build (browser + server bundles)
npx nx build admin
npx nx serve storefront
npx nx lint <project>
npx nx e2e storefront-e2e
npx nx graph                 # visualise the dependency graph
npx nx show projects
```

## Notes for whoever picks this up

- **Node 22.22.3+ / 24.15+ is required** (Angular engine constraint). Verified on Node 24.20.0.
- The workspace uses the **classic TypeScript setup**, not TS project references: Angular does
  not support project references
  ([angular/angular#37276](https://github.com/angular/angular/issues/37276)). `tsconfig.base.json`
  carries the `paths` mappings; there is deliberately no root `tsconfig.json` solution file and
  no `@nx/js/typescript` plugin in `nx.json`.
- `baseUrl` is intentionally **absent** from `tsconfig.base.json` — TypeScript 6 deprecates it
  (TS5101) and the `paths` entries are already `./`-relative.
- Zoneless is the Angular 22 default; `zone.js` is not a dependency. Do not add it.
- Lint config currently carries **only** the boundary rule. The full Angular/TypeScript rule
  set and Prettier integration are Step 5 and Step 22.
