# Contributing to Klara Home

## ⛔ The step protocol comes first

This project is executed through the gated plan in
[`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md).

**Work only on the step that has been explicitly authorised.** When a step's acceptance
criteria are met: stop, update the plan (status, date, outcome notes), then ask the User for
permission before starting the next step. Anything discovered outside the current step's
scope goes to the plan's Parking Lot — it is not fixed inline.

Aesthetics (colour, typography, imagery, motion) are **out of scope until Step 30**.

---

## Branch strategy

| Branch | Purpose | Protection |
|---|---|---|
| `main` | Production. Always deployable. Tagged releases (`v1.4.0`) | PR only, reviewed, CI green |
| `develop` | Staging. Integration branch | PR only, CI green |
| `feature/<step-N>-<slug>` | One step or one unit of work | — |
| `fix/<slug>` | Bug fix | — |
| `hotfix/<slug>` | Urgent production fix, branched from `main` | Merged to `main` **and** `develop` |
| `chore/<slug>` | Tooling, docs, dependencies | — |

Flow: `feature/*` → PR → `develop` → (release) → PR → `main` → tag.
Merge style: **squash merge**. The squashed subject must be a valid Conventional Commit.

---

## Commit messages — Conventional Commits

```
<type>(<scope>): <subject>

[optional body — the WHY, not the what]

[optional footer: BREAKING CHANGE: ..., Refs: ...]
```

**Types:** `feat`, `fix`, `docs`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`.

**Scopes** (use the module or app name): `api`, `worker`, `catalog`, `orders`, `payments`,
`inventory`, `pricing`, `shipping`, `returns`, `settlements`, `identity`, `platform`,
`content`, `search`, `reviews`, `notifications`, `reporting`, `storefront`, `admin`,
`infra`, `db`, `docs`, `deps`.

Rules enforced by the `commit-msg` hook:
- subject in the imperative mood, lower case, no trailing full stop, ≤ 72 characters
- a breaking change is marked `!` after the scope **and** a `BREAKING CHANGE:` footer

Examples:
```
feat(catalog): add vendor listing buy-box resolution
fix(payments): verify razorpay signature against the raw request body
refactor(orders)!: split order totals into per-vendor sub-orders

BREAKING CHANGE: /api/v1/store/orders response now nests lines under subOrders
```

### Enable the hooks (once per clone)

```bash
git config core.hooksPath .githooks
```

---

## Pull requests

- One step or one coherent change per PR; keep them reviewable.
- Fill in the PR template — the Definition of Done checklist is not optional.
- CI must be green: build, unit, integration, architecture tests, lint, OpenAPI client diff,
  container scan.
- At least one review. Anything touching money, stock, auth or personal data needs a reviewer
  who did not write it.

**Definition of Done** for every change is in
[`docs/09-nfr-testing-observability.md`](docs/09-nfr-testing-observability.md) §2.4.

---

## Code conventions

**Backend (.NET 10)**
- `.editorconfig` is authoritative; analyzers are errors in CI.
- File-scoped namespaces, nullable enabled, `internal` by default — a type is `public` only
  when another project genuinely needs it.
- **Module boundaries are law** (`docs/01-architecture.md` §2.1): a module owns its schema;
  cross-module access goes through `KlaraHome.Contracts` or an integration event. Never a
  direct reference to another module's internals. Architecture tests enforce this.
- No `DateTime.Now`/`UtcNow` — inject `IClock`.
- Money is the `Money` value object, never `decimal` on its own, never `double`.
- Do not add MediatR or AutoMapper (commercial licences — see `Directory.Packages.props`).

**Frontend (Angular)**
- Standalone components, signals-first, zoneless change detection.
- No hard-coded colours, font sizes or spacing — CSS custom properties only
  (`docs/10-design-system-placeholder.md`). A lint rule fails the build on violations.
- Never edit `libs/data-access/api` by hand — it is generated from the OpenAPI document.
- Mobile-first: build at 360 px, enhance upward. `min-width` media queries only.

**Database**
- `snake_case`, plural tables, UUIDv7 keys, `tenant_id` on every business table.
- Migrations are reviewed as generated SQL, not just as C#.
- Breaking changes follow expand → migrate → contract across two releases.

---

## Secrets

Never commit secrets. `.env` is git-ignored; `.env.example` documents every variable with a
safe placeholder. Secret scanning runs in CI. If a secret is ever committed, rotate it first,
then rewrite history.
