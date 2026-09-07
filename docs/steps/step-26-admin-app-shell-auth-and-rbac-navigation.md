# Step 26 — Admin app shell, auth & RBAC navigation

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** G · **Depends on:** Step 25
- **Objective:** The back-office foundation, serving both platform staff and vendors.
- **Deliverables:**
  - Admin SPA shell: login with 2FA, session handling, responsive layout (desktop-first but
    usable on tablet), navigation driven by the user's permissions.
  - Reusable admin building blocks: data table (server-side paging/sorting/filtering, column
    config, bulk actions, export), form framework, drawer/modal patterns, file uploader,
    audit-trail viewer, confirmation patterns for destructive actions.
  - Global search, notifications centre, impersonation (audited) for support.
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
- **Full acceptance criteria (verified at Step 29, not now):** Two users with different roles see correctly different navigation
  and are blocked from unauthorised routes and API calls.

---

## Outcome / Notes

**Status: complete, with one named deliverable not built — impersonation. See “What was not
built” below; it needs a backend change and is raised for a decision at this boundary.**

This is a **frontend-only** step. Build-acceptance items 1 and 3 name entities, endpoints and an EF
migration; they are the shared card template's backend wording and do not apply here, exactly as at
Steps 23–25. Nothing in `src/backend` was touched.

### The central idea: one declaration, two consumers

The full acceptance criterion has two halves — *see correctly different navigation* and *be blocked
from unauthorised routes* — and they are two statements about the same fact. Written as two
artefacts (a menu array and a route table) they agree on the day they are written and diverge on
the day somebody adds a screen: a menu item with no guard is a 403 the user was invited into, and a
guard with no menu item is a screen nobody can find.

So `apps/admin/src/app/core/navigation.ts` holds **one list of 42 destinations**, each carrying its
path, its label, its section, its permissions and its scope. `adminRoutes()` builds the router
configuration from it; `visibleSections()` builds the sidebar from it. Neither can describe a
destination the other does not, and the guard on a route is derived from the same fields the menu
item is filtered by.

Two kinds of gate, and the distinction is load-bearing:

- **`permissions`** — any one of them will do, because a screen is usually reachable by more than
  one role and the finer distinctions inside it are made by hiding individual controls.
- **`platformOnly`** — a **scope**, not a permission. A vendor owner genuinely holds
  `vendors.vendor.read` (they read their own seller record with it), so a route guarded only on
  that permission would put the platform's *seller directory* in a seller's navigation. No
  permission separates those two; the token's `vendorId` does. This is why
  `platformOnlyGuard` / `vendorOnlyGuard` / `platformPermissionGuard` were added to
  `data-access-auth` rather than the existing `permissionGuard` being reused.

`platformPermissionGuard` composes both in one guard rather than stacking two, so
`requireSession` — and the silent refresh inside it — runs once per navigation, not twice.

### What was built

**Two new libraries.**

| Library | Path | Holds |
|---|---|---|
| `@klarahome/ui-admin` | `libs/ui/admin` (`type:ui`, `scope:shared`) | The 13 admin building blocks, all presentational |
| `@klarahome/data-access-admin` | `libs/data-access/admin` (`type:data-access`, `scope:shared`) | `CursorList`, and the four admin services |

**The building blocks** (`libs/ui/admin/src/lib`), every one of which takes view models rather than
DTOs, because the lint boundary forbids `ui` importing `data-access` — and that rule earns its keep
here more than anywhere: a table typed to `PagedResultOfAdminUserResponse` is a table for one
endpoint.

- `admin.model.ts` — the view models: nav sections, columns, sorts, pages, bulk actions, audit
  entries, upload items.
- `admin-shell.ts` / `admin-sidebar.ts` / `admin-top-bar.ts` — the frame. **One break at 60rem**,
  and it is a real one: above it the sidebar is a grid column that collapses to an icon rail; at or
  below it the same sidebar renders inside a `kh-drawer`, with the focus trapping, Escape handling
  and scroll locking a panel over content needs and a column does not. The same `AdminSidebar`
  renders in both, from the same sections — two sidebars would be two navigations, and one of them
  would be the one nobody updated.
- `data-table.ts` — the central one. Server-side paging/sorting/filtering, column chooser
  (remembered per browser under a `storageKey`), bulk selection with a bulk bar, CSV export, sticky
  header, skeleton rows, cursor pager, and a `khCell` directive so a `custom` column can be
  projected without the column definition ceasing to be data. Three decisions worth naming:
  - **Paging is Previous/Next over cursors, never page numbers.** Every list endpoint here pages by
    keyset and `PageInfo.total` is explicitly nullable, so "page 7 of 42" cannot be built honestly
    and would be wrong exactly when the data is busiest.
  - **Selection is cleared whenever the rows change.** A bulk action carried across a filter change
    acts on rows the user can no longer see — that is how "delete 12 selected" deletes something
    else.
  - **A column is sortable only if it declares a `sortKey`**, because a header that produces a 400
    is worse than a header that does not move.
  - CSV export quotes per RFC 4180, writes a UTF-8 BOM (so Excel renders ₹ rather than mojibake),
    and prefixes `= + - @` with a tab so a cell cannot be read as a formula.
- `filter-bar.ts` — search debounced, selects not (typing is a stream of intentions; choosing
  "Cancelled" is one decision). Active filters render as removable chips, because a list quietly
  filtered by something scrolled out of view is the commonest "the data is missing" ticket a back
  office generates.
- `form-shell.ts` — the frame every admin form sits in: an error **summary** above the form,
  pinned actions, a disabled-and-labelled save, a visible unsaved marker, and
  `unsavedChangesGuard` (a `CanDeactivateFn`). Autosave is deliberately absent — what counts as a
  draft is per-entity, so it belongs to the screens that have one.
- `modal.ts`, `entity-drawer.ts`, `confirm-dialog.ts` — the dialog family. `ConfirmDialog`
  implements §4.3's requirement literally: **a typed confirmation phrase and a reason**, each
  independently demandable. The typed phrase defeats muscle memory (somebody who has clicked
  "Delete → Confirm" forty times does not read the forty-first); the reason is the audit trail —
  "cancelled by Priya, 14:02" answers nothing a week later.
- `file-uploader.ts` — a real `<input type="file">` merely hidden (a drop zone with no input behind
  it cannot be reached by a keyboard at all), type and size checked before anything is sent,
  dragging as an enhancement only. It uploads nothing: the admin's uploads go to two different
  contracts, so the transport belongs to the screen.
- `audit-trail.ts` — embeddable on any entity page and the body of the audit-log screen, because
  they are the same list with a different filter. A change shows **both sides**.
- `status-badge.ts`, `kpi-card.ts`, `page-header.ts` — the small ones. `StatusBadge` maps ~60
  API status spellings to tone by suffix and keyword rather than an exhaustive list, so an unseen
  status still lands somewhere sensible; the word is always rendered, so colour is never the only
  signal. `KpiCard` has three states, and the third is the one usually skipped: **no value the
  server would give** renders an em dash, not a zero.

**`CursorList<TRow, TFilters>`** (`data-access-admin`) is the paging engine behind every admin list.
Keyset paging means the server cannot go backwards — only the client knows where page two started,
and only because it was there — so this keeps a stack of the cursors it has used, empties it on a
filter change, and **cancels the in-flight request when a newer one is issued**, without which a
slow unfiltered page arrives after a fast filtered one and the results contradict the filter bar
above them.

**The services.** `AdminSessionService` (sessions, TOTP, password), `AuditLogService`,
`NotificationCentreService`, `GlobalSearchService`, `DashboardService`.

**The screens.** `/login` (with the TOTP step), `/forgot-password` (both halves of a reset),
`/dashboard`, `/notifications`, `/settings/audit-log`, `/profile`, `/403`, `/404`, and the
placeholder for the 36 destinations Steps 27 and 28 own.

### Decisions worth recording

1. **`/login` is outside the shell, as a sibling route.** `ShellLayout` is a routed layout guarded
   by `authenticatedGuard`; everything else is its child. A shell wrapped around the sign-in form
   would be a menu of screens the visitor cannot open, with an identity in its top bar naming
   nobody. It also means an unauthenticated visitor to *any* URL — including one that does not
   exist — is sent to sign in with their destination in `returnUrl`, rather than shown a 404 that
   would have been a real page had they been signed in.

2. **One route with two steps for the second factor, not two routes.** The `challengeToken` a
   password earns lives in a signal and never in a URL: a URL is bookmarked, pasted into chat and
   written to server logs. A reload returns to the password step, which is correct — the challenge
   is short-lived, and re-entering a password is not the hard part.

3. **`AuthService` became surface-aware for four more methods.** `refresh` and `signOut` already
   chose their endpoint from `surface`; `signIn`, `verifyTwoFactor`, `forgotPassword` and
   `resetPassword` now do too, and `enrolTwoFactor` was added. The two surfaces issue different
   cookies with different session policies, and a storefront login that minted a staff session
   would be the most serious defect this application could have. No storefront behaviour changes —
   its surface is `'store'`, the default.

4. **`SIGN_IN_PATH` is an injection token.** The storefront signs in at `/auth/login`, the back
   office at `/login` (§4.2). Hard-coding either would make the shared guards usable by one app;
   duplicating the guards would make them drift.

5. **`*khHasPermission` lives in `data-access-auth`, not in `ui-admin`.** §4.3 calls it a
   `PermissionDirective` and it reads the session, so the boundary decides: `ui` may not import
   `data-access`. It sits next to the guards, which are the same concern at a different
   granularity. Structural rather than a CSS class, because a hidden-with-CSS button is still
   focusable by Tab and still read out — a keyboard user would tab onto a control they cannot use
   and be told nothing. It re-evaluates on session change, so a role edited under a signed-in user
   takes the controls away rather than waiting for a reload.

6. **Global search is a client-side fan-out, and that is the correct implementation, not a
   workaround.** There is no `GET /admin/search`, and there cannot easily be one: a cross-module
   search endpoint would query five schemas, which this architecture forbids. So the panel calls
   the four list endpoints that already accept a search term — orders (by number only, the sole
   term that endpoint takes), products, sellers, users — **each gated on the caller's own
   permission before the request is sent**, so a support user's results are not a row of 403s. A
   source that fails is dropped rather than fatal. The dialog implements the WAI-ARIA combobox
   keyboard model: the input keeps focus, `aria-activedescendant` moves an active option, Enter
   opens it, Escape closes. `/` opens it from anywhere, guarded so it does not steal the slash out
   of somebody's typing.

7. **"Notifications centre" is the message log, because that is what the API has.** There is no
   per-user in-app notification endpoint; `GET /admin/notifications` is every email and SMS the
   platform has tried to send and what became of each. That is the more useful surface anyway: it
   answers the second question of nearly every support conversation, and **a failed notification is
   otherwise completely invisible** — nothing breaks when a provider rejects a number, the customer
   simply never hears. Retry is confirmation-gated and names the recipient, and only `Failed` and
   `Bounced` rows offer it: retrying a `Suppressed` one would override an opt-out. The badge count
   is refreshed on entry, **not polled** — forty open tabs polling every thirty seconds is a
   self-inflicted load test, and this number does not move that fast.

8. **The dashboard shows work queues, not statistics.** Each tile is a count of things somebody has
   to deal with, linking to the screen where they deal with it; revenue and conversion belong to
   the reporting screens, which have an API built for them. Each tile is requested only if the
   caller may see it, so the page is already the clearest demonstration of the acceptance
   criterion. Counts come from `page.total` with a page of one, and render an em dash where the
   server declined to count.

9. **`forms.ts` gained server-error mapping.** `FormField.setServerError` and
   `FormGroup.applyServerErrors` take a 422's `ProblemDetails.errors`, match keys on the last
   dotted segment case-insensitively (the server sends `RecipientName`, sometimes
   `body.recipientName`), and **return the messages that matched no field** so the caller can show
   them in a summary. A validation message the user never sees is a form that will not submit and
   will not say why. A server error outranks the client rules, shows immediately rather than on
   blur (the value has already been sent — there is nothing left to wait for), and clears the
   moment the value changes.

10. **The admin now installs `provideKlaraHomeErrorHandling()`**, which it did not. An unhandled
    error there was a console line and a screen that did not change.

11. **`AdminTitleStrategy`.** People who use a back office keep eight tabs of it open, and eight
    tabs reading "admin" is eight tabs they have to click through. The title comes from the route's
    `data.title`, which is the label already declared in `navigation.ts` — one declaration, so a
    screen renamed in the sidebar is renamed in the tab.

### What was NOT built

**Impersonation.** `07-security-compliance.md` §2 requires time-boxed, reason-carrying, audited
support impersonation, and this card names it. **The API does not offer it**: there is no
impersonation endpoint anywhere in the generated client, no impersonation permission in
`PermissionCatalog`, and no impersonation flag on `AuthenticatedUserResponse` for a session to be
marked with. Every part of the feature — starting it, carrying it, ending it, auditing it, showing
it — is a backend capability first. This is the same shape as Step 25's guest checkout, and it is
handled the same way: not faked, recorded in `PARKING_LOT.md` with the exact blocker, given a
`TEST_DEBT.md` row, and **raised with the User at this boundary**. The one honest half that *was*
built is the slot it will occupy: `AdminTopBar` states the session's scope permanently, which is
where an impersonation banner belongs.

Deliberately deferred, and why:

- **Saved views** and **virtual scroll** (both in §4.3's DataTable list, neither in this card's
  Deliverables). Saved views need a place to store them per user; virtual scroll is a performance
  answer to a list nobody has measured yet. Parked.
- **`JobMonitor`, `RichTextEditor`, `MediaManager`, `PageComposer`, `CouponBuilder`, `LedgerTable`,
  `PayoutWizard`, `ImportWizard`** — §6's admin pattern inventory, all owned by the steps that
  need them (27 and 28).
- **Users, roles and feature-flag screens.** The step's title is *RBAC navigation* — navigation
  driven by RBAC, not RBAC management. Those routes exist, are guarded and are on the placeholder.

### Files

**New — `libs/ui/admin`:** `admin.model.ts`, `admin-shell.ts`, `admin-sidebar.ts`,
`admin-top-bar.ts`, `audit-trail.ts`, `confirm-dialog.ts`, `data-table.ts`, `entity-drawer.ts`,
`file-uploader.ts`, `filter-bar.ts`, `form-shell.ts`, `kpi-card.ts`, `modal.ts`, `page-header.ts`,
`status-badge.ts` + `index.ts`, `project.json`, `jest.config.cts`, three tsconfigs, `test-setup.ts`,
`README.md`.

**New — `libs/data-access/admin`:** `cursor-list.ts`, `admin-session.service.ts`,
`audit-log.service.ts`, `notification-centre.service.ts`, `global-search.service.ts`,
`dashboard.service.ts` + the same scaffolding.

**New — `apps/admin`:** `core/navigation.ts`, `core/navigation.spec.ts`, `core/shell.layout.ts`,
`core/global-search.panel.ts`, `core/sign-in.flow.ts`, `core/audit.mapper.ts`,
`core/describe-error.ts`, `core/title.strategy.ts`; `pages/dashboard.page.ts`,
`pages/notifications.page.ts`, `pages/audit-log.page.ts`, `pages/profile.page.ts`,
`pages/placeholder.page.ts`, `pages/auth/login.page.ts`, `pages/auth/forgot-password.page.ts`,
`pages/errors/forbidden.page.ts`, `pages/errors/not-found.page.ts`.

**Changed:** `apps/admin/src/app/{app.ts,app.html,app.config.ts,app.routes.ts,app.spec.ts}`;
`libs/data-access/auth/src/lib/{auth.service.ts,auth.guards.ts}` + new
`has-permission.directive.ts` + `index.ts`; `libs/util/src/lib/forms.ts`; `tsconfig.base.json`.

### Verification

| Gate | Result |
|---|---|
| `npx nx build admin --configuration=production` | ✅ **94.4 kB gzipped initial** against a 300 kB budget; largest route chunk 4.07 kB against 120 kB |
| `npx nx build storefront --configuration=production` | ✅ 144.2 kB gzipped (was 160.5 kB at Step 25 — the shared-lib changes did not grow it) |
| `npx nx run-many --target=lint --all` | ✅ 23 projects, zero errors and zero warnings |
| `npx nx run-many --target=test --all` | ✅ 21 projects green |
| `npx prettier --check` on every touched file | ✅ clean |

**One unit test was written**, under §3.2's single exception (a test is written where it is the
cheapest way to get an algorithm right): `core/navigation.spec.ts`, eight cases over `canReach`,
`visibleSections` and `adminRoutes`. It is the RBAC rule table — two roles' navigation comes out of
it, and the failure mode if it is wrong is a menu item leading to a 403 or a seller offered another
seller's screen. `app.spec.ts` was updated: the landmarks moved into `kh-admin-shell`, so it now
asserts the routed outlet instead.

**Nothing is proved against a live API or a browser.** 22 `TEST_DEBT.md` rows, including both
halves of the headline criterion.
