# Klara Home — Frontend workspace

**Status: ⛔ BLOCKED — Nx/Angular workspace not yet generated (Node.js version).**

## Blocker

The current Angular release requires Node `^22.22.3 || ^24.15.0 || >=26.0.0`.
This machine has **Node v20.12.2**, so `create-nx-workspace` would either fail or silently
pin an outdated Angular. Generating it on an unsupported runtime was deliberately avoided.

**Resolution:** install **Node 22 LTS** (recommended) or Node 24, then generate the workspace.
Using `nvm-windows`:

```bash
nvm install 22.22.3
nvm use 22.22.3
node -v            # expect v22.x
```

## What gets generated here once Node is upgraded

Per [`docs/05-frontend-architecture.md`](../../docs/05-frontend-architecture.md) §1:

```
src/frontend/
  apps/
    storefront/        Angular + @angular/ssr, Node-served, SEO-critical, mobile-first
    storefront-e2e/    Playwright
    admin/             Angular SPA (nginx), platform staff + vendors, role-scoped
    admin-e2e/
  libs/
    data-access/
      api/             GENERATED from OpenAPI — never hand-edited
      auth/ cart/ catalog/ orders/ content/
    domain/            Framework-free models, enums, money/GST helpers
    ui/
      primitives/ patterns/ layout/
    util/ i18n/ testing/
```

Nx module-boundary tags to configure at generation time:
- an app may not import another app
- `ui` may not import `data-access`
- only `data-access` may import `data-access/api`

The `apps/` and `libs/` directories are already present so the repository tree matches the
specification; they are otherwise empty.
