# Step 30 — Responsive visual QA across the device matrix

> Card: [`../step-30-design-system-theming-and-visual-identity.md`](../step-30-design-system-theming-and-visual-identity.md).
> Closes the last open item in that step's deliverable list.

**44 route×breakpoint combinations checked against the live themed dev stack (not `nx serve`), all
green. One real finding, and it isn't a bug: a `fullPage` screenshot of a `position: fixed` mobile
buy-bar looked like an overlap defect until it was checked against real scroll behaviour, which is
correct. No CSS was changed by this pass.**

## 1. Breakpoint matrix

Not invented for this pass — every value is one this repo already committed to:

| Breakpoint | Viewport | Source |
|---|---|---|
| `mobile` | 360×640 | `docs/05-frontend-architecture.md` §3.3, "design and build at 360×640 first" — and the exact viewport `docs/09-nfr-testing-observability.md`'s journey #16 names ("…on a 360 px viewport") |
| `tablet` | 768×1024 | the `md` token in §3.3's breakpoint list (`xs 0, sm 480, md 768, lg 1024, xl 1280, 2xl 1536`) |
| `laptop` | 1024×900 | the `lg` token — the one breakpoint named as a behaviour change, not just a width: §3.3 says the filter panel switches from a bottom sheet to a sidebar exactly at `lg`, and the sticky mobile buy-bar (§3 below) switches from `position: fixed` to `position: sticky` at the same `1024px` |
| `desktop` | 1280×900 | the `xl` token, and the same width the `desktop` project already screenshots in both `playwright.visual.config.mts` files, so this sweep and the existing pixel baselines agree on what "desktop" means |

`sm` (480) and `2xl` (1536) were not added as separate projects: nothing in the codebase names a
behaviour change at either (no component media-queries on them), and four breakpoints times the
route count below was already the right size for a QA pass, not a combinatorial one.

## 2. Method

Two things, not one, because they find different classes of defect:

1. **A programmatic overflow assertion, at every route×breakpoint combination.** `document.
   documentElement.scrollWidth <= document.documentElement.clientWidth` — cheap, exhaustive, and
   it is the single most common mobile layout defect (a fixed-width element, an un-wrapped long
   string, an image without `max-width: 100%`). New spec: `responsive-qa.responsive.spec.ts` in
   both `apps/storefront-e2e/src` and `apps/admin-e2e/src`, run via a third Playwright config
   (`playwright.responsive.config.mts` in each) pointed at the same live docker dev stack the
   Step 30 visual-regression baselines use — a baseline taken against `nx run storefront:serve`
   would be checking the placeholder, not the themed product the client signed off on, and the
   same is true of an overflow check.
2. **Full-page screenshots of the three named risk areas** (category listing's filter panel, the
   header/mobile-nav area, the cart), at all four breakpoints, actually looked at rather than only
   overflow-asserted. Saved under `src/frontend/test-output/responsive-review/` (gitignored
   `test-output/`, not a committed baseline — this is a QA artifact, not a pixel-regression
   contract like `theming.visual.spec.ts`'s baselines).

Wired as Nx targets: `npx nx run storefront-e2e:responsive-qa`, `npx nx run admin-e2e:responsive-qa`.

**Coverage is nine storefront routes, not the four the visual-regression baselines cover**: home,
category listing, search results, product detail, vendor page, cart, the `/checkout` guard
redirect, sign-in, register. Account (`/account/**`) and the authenticated parts of checkout are
not reachable — same reason the visual-regression admin baselines stop at sign-in/forgot-password:
`authenticatedGuard` sends an anonymous caller to `/auth/login`, and there is no scripted-login
Playwright fixture anywhere in this workspace yet (storefront or admin) to get past it. That gap is
recorded once, honestly, in §4 rather than worked around with a fake session.

Admin coverage is the same two screens the visual-regression baselines already justify not going
past: sign-in and forgot-password. Every other admin screen sits behind `authenticatedGuard` and a
mandatory-two-factor `platform-admin` role (`docs/dev-setup.md`), which this turn does not script,
for the same reason the visual-regression admin config already gives.

## 3. Results

### 3.1 Overflow assertion — 36 storefront + 8 admin = 44 combinations, all pass

| Route | 360 | 768 | 1024 | 1280 |
|---|---|---|---|---|
| Storefront home | ✅ | ✅ | ✅ | ✅ |
| Category listing (`/c/cushions`, filters visible) | ✅ | ✅ | ✅ | ✅ |
| Search results (`/search?q=cushion`) | ✅ | ✅ | ✅ | ✅ |
| Product detail (`/p/linen-cushion-cover`, variant selector) | ✅ | ✅ | ✅ | ✅ |
| Vendor page (`/vendor/linen-and-loom`) | ✅ | ✅ | ✅ | ✅ |
| Cart (`/cart`) | ✅ | ✅ | ✅ | ✅ |
| `/checkout` (anonymous → guard redirect) | ✅ | ✅ | ✅ | ✅ |
| Sign in (`/auth/login`) | ✅ | ✅ | ✅ | ✅ |
| Register (`/auth/register`) | ✅ | ✅ | ✅ | ✅ |
| Admin sign in (`/login`) | ✅ | ✅ | ✅ | ✅ |
| Admin forgot password (`/forgot-password`) | ✅ | ✅ | ✅ | ✅ |

No route overflows horizontally at any of the four breakpoints.

### 3.2 Risk-area visual review — 12 storefront screenshots (3 areas × 4 breakpoints)

- **Filter panel (category listing).** Confirmed the documented behaviour actually holds: a
  single-column product grid with a "Filters" toggle button at 360 and 768, and a persistent
  left sidebar with the toggle gone at 1024 and 1280 — exactly the bottom-sheet-to-sidebar switch
  §3.3 names at `lg`. Product cards wrap cleanly at every width checked (4 columns at 1024/1280, 2
  at 768, 1 at 360); no truncated titles, no overlapping "Add to cart" buttons, no orphaned cards.
- **Header / mobile nav.** Hamburger + wordmark + account/cart icons at 360; same layout holds
  through 768 and 1024 (the header does not attempt a horizontal nav bar at any width checked —
  there is currently no category mega-menu / desktop nav variant to switch to, which is a content
  gap from an earlier step, not a responsive-layout defect this pass introduces or is scoped to
  fix). No icon crowding, no header overflow, no touch target under 44px on the hamburger, search,
  account or cart icons at 360 (all render at the design system's `--space` button-size tokens,
  which are ≥44px — verified visually, not by a separate axe/target-size assertion this turn).
- **Cart.** Empty-cart state (no line items were added by this pass — it is a read-only sweep) is
  centred and legible at all four widths; no layout collapse.

### 3.3 One thing that looked like a defect and was not

`test-output/responsive-review/filter-panel-mobile-360.png`'s sibling capture of the PDP under the
same method showed what looked like an overlapping "₹899 / Add to cart" bar sitting on top of the
price/tax line, rather than pinned to the bottom of the screen. Investigated rather than filed
blind:

- `libs/ui/patterns/src/lib/sticky-action-bar.ts` renders the page's primary action in
  `position: fixed; inset-block-end: 0` below `1024px` (switching to `position: sticky` at `lg`,
  by design — the file's own comment: "a fixed bar across a 1440px screen is a lot of furniture for
  one button"). That is the intended "thumb-zone action" the component's doc comment describes.
- Playwright's `fullPage: true` screenshot stitches the whole document height, and a
  `position: fixed` element is composited once, at the screen offset it occupied at the scroll
  position captured — so a full-page capture of a page taller than one viewport shows the fixed
  bar "floating" wherever the first viewport happened to be, not repeated at the true bottom of
  every screenful. That is a `fullPage` screenshot artifact, not a rendering bug.
- Checked against real scrolling to be sure: a one-off script drove a real 360×640 Chromium page to
  `/p/linen-cushion-cover`, took a viewport (not full-page) screenshot at the top, scrolled 500px,
  and took another. The bar stays correctly pinned to the bottom of the viewport in both, with no
  overlap of the content above it — the shell reserves space for it, as
  `StickyActionBarService`'s `active` signal comment says it does.

No code changed here. Recorded because a future reader of a `fullPage` PDP screenshot should not
rediscover this from scratch.

## 4. Deferred, named rather than silently dropped

- **No scripted authenticated session exists yet for either app's e2e suite** (storefront customer
  login, admin `platform-admin` TOTP). This turn's sweep is therefore limited to what an anonymous
  visitor reaches — the same limitation the Step 30 visual-regression baselines already carry and
  already justify for admin. Account pages, the authenticated checkout steps, admin dashboard, data
  tables, forms and modals are not covered by this responsive pass. A scripted-login fixture that
  would unlock this coverage is left for whoever next touches the e2e suites, not invented here to
  force a green checkbox.
- **Touch-target sizing was checked visually, not with a dedicated `getBoundingClientRect()`
  assertion** against a 44px floor for every interactive element. Given the design system's own
  button/icon tokens are already ≥44px and nothing in the screenshots showed a cramped target, a
  per-element assertion was judged not worth adding for this pass; it is a reasonable follow-up if
  a future component introduces a smaller custom control.
- **No desktop horizontal navigation / category mega-menu exists to check.** Noted in §3.2 as an
  existing content-architecture gap from an earlier step (the header renders the same
  hamburger-drawer pattern at every width checked), not something this responsive-CSS pass is
  scoped to add.

## 5. Files

- `apps/storefront-e2e/playwright.responsive.config.mts`, `apps/storefront-e2e/src/
  responsive-qa.responsive.spec.ts`
- `apps/admin-e2e/playwright.responsive.config.mts`, `apps/admin-e2e/src/
  responsive-qa.responsive.spec.ts`
- `apps/storefront-e2e/project.json`, `apps/admin-e2e/project.json` — new `responsive-qa` Nx target
  in each
