# Step 30 — Frontend visual consistency and CMS-block responsiveness review

> Card: [`../step-30-design-system-theming-and-visual-identity.md`](../step-30-design-system-theming-and-visual-identity.md).
> Companion to [`responsive-qa-report.md`](responsive-qa-report.md) in this folder, which swept nine
> storefront routes for horizontal overflow against the seeded demo content and explicitly did not
> stress-test the CMS block renderer against merchant-configurable content (arbitrary column counts,
> item counts, text length). This pass closes that gap and does a token/spacing/typography
> consistency audit across the storefront and admin UI libraries.

**Scope was read first, then fixed in place**: `docs/10-design-system.md`, the token source
(`libs/ui/primitives/src/styles/_tokens.scss`, `_typography.scss`, `_base.scss`,
`_breakpoints.scss`), the CMS block renderer and everything it delegates to
(`kh-grid`, `kh-product-image`, `kh-disclosure`, `kh-product-carousel`, `khButton`), the two pages
that mount it (`home.page.ts`, `cms-page.page.ts`), and a targeted grep sweep of every `.ts`
component style block in `libs/ui/**` and `apps/**` for hard-coded colours, spacing and font sizes
that should have been tokens.

> **Correction (2026-09-11 follow-up pass).** This section originally claimed there is no
> admin-side CMS editor. That was a grep miss, not a fact: the composer exists at
> `apps/admin/src/app/pages/content/page-composer.page.ts`, routed at `content/pages/:id`
> (`apps/admin/src/app/core/navigation.ts:321`), and is reviewed in full in the follow-up section
> below (§8). The grep that produced the original claim searched for `page-editor` / `page-builder`
> / `CmsBlock` — none of which is the name this codebase actually gave it (`page-composer`,
> `PageComposerPage`).

## 1. Findings

| # | Issue | File:line | Severity | Status |
|---|---|---|---|---|
| 1 | `bannerGrid`/`categoryTiles` bound a merchant-configured `columns` straight to `kh-grid`'s `fixedColumns`, which emitted a literal `repeat(N, 1fr)` — a merchant setting 4 columns got 4 fixed columns **at every viewport width down to 360px**, i.e. unreadable slivers with no reflow at all. This is the CMS-block responsiveness gap the brief named as the top priority. | `libs/ui/layout/src/lib/grid.ts:39` (old), used from `libs/ui/patterns/src/lib/cms-block-renderer.ts:57,101` (old) | **High** | **Fixed** — see §2.1 |
| 2 | `richText` and `faq` blocks rendered sanitised CMS/merchant HTML through a bare `<div [innerHTML]>` with no styling at all: no `overflow-wrap`, no table handling (a wide `<table>` would push the whole page wider at 360px), no token-based blockquote/list/image treatment. `.kh-prose` exists elsewhere in the design system for exactly this and was never reused here. | `libs/ui/patterns/src/lib/cms-block-renderer.ts:121,130` (old) | **High** | **Fixed** — see §2.1 |
| 3 | No text in any CMS block (`hero` headline/sub, `bannerGrid`/`categoryTiles` captions, `testimonial` quote/caption) had `overflow-wrap`, so a merchant entering one long unbroken word or a bare URL — very plausible in a caption or a quote — could overflow its container at narrow widths. | `libs/ui/patterns/src/lib/cms-block-renderer.ts` (whole template) | **Medium** | **Fixed** — see §2.1 |
| 4 | `kh-product-carousel` used as the `productCarousel` block case carries its own `margin-block: var(--space-8)` for its standalone uses (PDP, home page's "recently viewed" rail). Nested as one flex child among others under `CmsBlockRenderer`'s `gap`-based column, that margin does **not** collapse with the gap (flex items never collapse margins), so a carousel next to any other block got roughly double the rhythm of two consecutive non-carousel blocks — a direct violation of "vertical rhythm stays consistent regardless of order." | `libs/ui/patterns/src/lib/cms-block-renderer.ts:88` (old) | **Medium** | **Fixed** — see §2.1 |
| 5 | The gap between consecutive CMS blocks was `var(--space-8)` (a fixed 2rem), not `--space-section` — the token `_tokens.scss` itself defines as "the vertical gap between one band of a page and the next... so that home, category and product pages breathe identically." The renderer whose whole job is stacking bands was the one place not using it. | `libs/ui/patterns/src/lib/cms-block-renderer.ts:168` (old) | **Medium** | **Fixed** — see §2.2 |
| 6 | `Container`'s `narrow` size and `.rich[data-width='narrow']` both hard-coded the literal `68ch` instead of referencing `--measure` (`_tokens.scss`, also `68ch`) — two independent copies of the same number with no link between them. Three more independent copies of the same literal existed on `category.page.ts`, `vendor.page.ts` and `collection.page.ts`'s `.description`/`.about` prose blocks. Exactly the "ad-hoc value that drifts between components" the brief called out. | `libs/ui/layout/src/lib/container.ts:35`, `libs/ui/patterns/src/lib/cms-block-renderer.ts:212`, `apps/storefront/src/app/pages/{category,vendor,collection}.page.ts` (old) | **Low** | **Fixed** — see §2.2 |
| 7 | `.banner`/`.tile`/`.quote` grid items had no `min-width: 0`. Grid items default to `min-width: auto`, so an unbroken caption could in principle push a narrow column wider than its track and overflow the grid, independent of fix #3. | `libs/ui/patterns/src/lib/cms-block-renderer.ts` (`.banner`, `.tile`, `.quote` rules) | **Low** | **Fixed** — see §2.1 |
| 8 | `kh-stepper`'s mobile label-hiding used `@media (max-width: 479px)`. ~~The one `max-width` media query in the whole codebase.~~ **Correction (follow-up pass): wrong — it was the first found, not the only one.** `libs/ui/admin/src/lib/admin-top-bar.ts:231` (`@media (max-width: 60rem)`, paired with `admin-shell.ts`'s sidebar breakpoint) and `apps/storefront/src/app/pages/cart.page.ts:267` (`@media (max-width: 1023px)`) were both still there; the admin app's rem-based breakpoints were also never checked against the token scale at all in this original pass. All three are now fixed — see the follow-up section's §F.2. | `libs/ui/primitives/src/lib/stepper.ts:129` (old) | **Low** | **Fixed** — see §2.4 (original) and §F.2 (follow-up) |
| 9 | An SVG chart axis tick label used a bare `font-size: 11px` instead of a type-scale token. | `libs/ui/admin/src/lib/report-chart.ts:184` (old) | **Low** | **Fixed** (`var(--text-xs)`, 12px — 1px off the original, visually negligible) |
| 10 | `entity-picker.ts`'s dropdown option uses `gap: 2px` between its title and subtitle line — a genuine ad-hoc value. | `libs/ui/admin/src/lib/entity-picker.ts:177` | **Low** | **Not fixed** — no spacing token exists below `--space-1` (4px); doubling a deliberately tight 2px micro-gap to 4px is itself a visual change this pass wasn't asked to make, and inventing a new token for one call site is disproportionate. Left as a named exception rather than silently reformatted. |
| 11 | ~~The installed `.stylelintrc.json`/`.stylelintignore`… only lints `**/*.scss`, and `postcss-styled-syntax`… does not reach Angular's `styles:` string, so almost nothing is actually enforced.~~ **Correction (follow-up pass): overstated.** `eslint.config.mjs` already had `local/no-hardcoded-color-in-styles` at the time of the original pass — a plain AST rule that *does* walk every `styles:` template literal and *does* catch hard-coded colour there (see `tools/ci.ps1` ~line 496, which this review should have read). What was actually unguarded, and is now fixed, is spacing, type size and breakpoints — see the follow-up section's §F.3. | `src/frontend/eslint.config.mjs` | **Medium** (tooling gap, colour half already covered) | **Fixed for spacing/type-scale/breakpoints** — see §F.3. Colour was never the gap. |
| 12 | `richText` blocks with `width: 'wide'` render identically to `width: 'full'` — only `'narrow'` has any CSS effect. Not a responsiveness defect (nothing overflows), but likely not the intended three-tier distinction the content model implies. | `libs/ui/patterns/src/lib/cms-block-renderer.ts:234` (`.rich[data-width='narrow']` is the only rule) | **Low** | **Fixed in the follow-up pass** — the product decision was taken: `wide` is a new `--container-wide: 64rem` tier between `narrow` (`var(--measure)`) and `full` (the container), all three centred. See the Follow-up pass section. |
| 13 | `bannerGrid`/`categoryTiles` with fewer items than the merchant's configured column count still leave a trailing blank-column-width gap in the last row (an inherent property of `auto-fill`, not something this pass introduced — the original literal `repeat(N, 1fr)` had the identical property). | `libs/ui/layout/src/lib/grid.ts` (fixed-columns path) | **Low** | **Not fixed, and now empirically confirmed correct in the follow-up pass's real-browser stress test (§F.7):** a "6 columns configured, 1 item" banner at 1280px sits at its true one-sixth width with the trailing five-sixths empty (`closeup-desktop-1280-05.png`) rather than stretching — exactly the trade-off this row already reasoned through, now seen rendered rather than only reasoned about. |
| 14 | ~~The `hero` CMS block stacks the image above the copy rather than overlaying text on the image… converting it would be a visual-redesign decision, explicitly out of scope, not fixed here.~~ **Superseded (follow-up pass): the User made exactly that call.** Every hero now overlays the copy on the image behind a token-built scrim, deliberately, as a Step 30 visual-identity decision — see the follow-up section's §F.5. The finding's original reasoning (no contrast problem existed *in the stacked layout*, no short-viewport height problem) was correct for the layout as it stood; it is why the rewrite had to add a new, verified contrast obligation (`docs/10-design-system.md` §1.2) rather than assume the old layout's safety carried over. | `libs/ui/patterns/src/lib/cms-block-renderer.ts` (hero case) | **Info → done** | **Implemented** — see §F.5 |
| 15 | Hard-coded colours, `rgb()`/`hsl()` literals, non-token font families, and non-token `margin`/`padding`/`gap`/`font-size`/`border-radius` values: swept across every `.ts` component style block in `libs/ui/**` and `apps/**`. | — | — | **No further findings** — the one exception already listed at #9/#10. The five hard-coded `fill="#..."` hex values in `apps/storefront/src/app/pages/auth/social-sign-in.ts` are the fixed third-party brand colours for the Google/Facebook "sign in with" icons, which are correctly exempt (they are not this product's palette). |

## 2. Changes made, grouped by concern

### 2.1 Responsiveness — CMS building blocks

- **`libs/ui/layout/src/lib/grid.ts`** — `fixedColumns` no longer emits a literal `repeat(N, minmax(0,1fr))`. It now computes the width one of the `N` columns would have at the container's full size (`calc((100% - (N-1)*gap) / N)`), floors that by the existing `minColumnWidth` input, and feeds the result into the same `auto-fill`/`minmax(min(..., 100%), 1fr)` formula the uncapped path already used. On a wide container this resolves to exactly the merchant's chosen column count (the calculated width wins); on a narrow one the floor wins and `auto-fill` packs however many columns of that floor width actually fit — the same graceful degradation every other grid in this component already had, now extended to the one input that previously bypassed it entirely. Only two call sites use `fixedColumns` (both in `cms-block-renderer.ts`), so this is a behaviour-preserving change for every other consumer (there are none) and a bug fix for both actual ones. No existing unit test covered `Grid` or `CmsBlockRenderer` (checked; none exist), so nothing needed updating for the new formula.
- **`libs/ui/patterns/src/lib/cms-block-renderer.ts`**:
  - `bannerGrid` and `categoryTiles` now pass an explicit `minColumnWidth` (`14rem` and `7rem` respectively) instead of `kh-grid`'s generic `16rem` default. The floors were picked from the blocks' own existing `sizes` hints, which already assumed this shape: `categoryTiles` ships `sizes="(min-width: 768px) 20vw, 45vw"` — `45vw` below `768px` already assumes **two** tiles per row on mobile, which `7rem` reproduces (two ~144px tiles fit a 328px mobile content area; one didn't). `bannerGrid`'s `100vw` hint below `768px` assumes a single column, which `14rem` reproduces at 360px while still reaching 3–4 columns at tablet/desktop widths for a typical 3–4-column merchant configuration.
  - Every text-bearing element (`h2`, `p`, `span`, `figcaption`, `blockquote`) gets `overflow-wrap: anywhere`, so a merchant's headline, caption, label or quote can break mid-word instead of overflowing at narrow widths.
  - `.banner`, `.tile` and `.quote` (all grid items) get `min-width: 0`, closing the classic "grid item won't shrink below its content's intrinsic width" failure mode independently of the wrap fix above.
  - The `productCarousel` case now sets `style="margin-block: 0"` directly on `<kh-product-carousel>` to cancel its standalone-context margin when it's nested as a flex child of the renderer (see finding #4).
  - `richText`'s content `<div>` and each FAQ answer `<div>` now carry a shared `.rich-body` class (previously bare, unstyled `<div>`s) with: `overflow-wrap: anywhere` and `min-width: 0`; token-styled headings, lists, images (`max-width: 100%; height: auto`) and blockquotes (a left rule in `--color-border-strong`, muted text); and — the one genuinely new capability — tables rendered `display: block; overflow-x: auto`, so a merchant-authored table scrolls inside its own block instead of forcing the whole page wider at 360px, plus token-based cell padding and borders.

### 2.2 Spacing / padding

- **`cms-block-renderer.ts`**: the gap between consecutive blocks changed from a fixed `var(--space-8)` to `var(--space-section)` — the fluid `clamp(2.5rem, 1.5rem + 4vw, 4.5rem)` token the design system already defines specifically as "the gap between one band of a page and the next." A page assembled as hero → banner grid → rich text now breathes identically to one assembled hero → carousel → FAQ, and identically to the section rhythm every other page on the site already uses via `.kh-section`/`.kh-band`.
- **`container.ts`**, **`cms-block-renderer.ts`**, **`category.page.ts`**, **`vendor.page.ts`**, **`collection.page.ts`**: the five independent hard-coded `68ch` literals all now read `var(--measure)`. No visual change (the token's value is the same `68ch`) — this removes the drift risk the brief specifically flagged: if the measure is ever retuned, all five now follow it instead of the four that weren't touched leaving the reading width inconsistent between a CMS page and a category/vendor/collection page.

### 2.3 Typography

- No type-scale, line-height or heading-hierarchy violations were found in the CMS block renderer or the pages around it — `h1`–`h3` already resolve to `--font-display` and `h4`+ to `--font-sans` globally via `_typography.scss`, and every `font-size` in the reviewed files was already a `--text-*` token. The one non-token font-size found anywhere in the sweep was the SVG chart tick fix in §2.4.
- The richText/FAQ hardening in §2.1 is also a typography fix in effect: those two block types previously rendered arbitrary CMS HTML with **zero** applied heading/list/paragraph styling (no line-height, no token spacing between elements), so any heading a merchant put in a rich-text block or FAQ answer rendered at the browser default rather than the design system's scale. That's now consistent with the rest of the block renderer.

### 2.4 Other visual consistency

- **`libs/ui/admin/src/lib/report-chart.ts`**: SVG axis tick `font-size: 11px` → `var(--text-xs)` (12px). The 1px difference is visually negligible; the point is removing the one hard-coded type size left in the sweep.
- **`libs/ui/primitives/src/lib/stepper.ts`**: rewrote the mobile label-hiding rule from `@media (max-width: 479px) { .label { ...hide... } }` to the mobile-first equivalent (hidden by default, restored `@media (min-width: 480px)`), matching `_breakpoints.scss`'s stated `min-width`-only convention. Behaviourally identical at every width; purely an authoring-consistency fix.
- No hard-coded colours, radii, shadows, or non-token border widths were found anywhere in the reviewed libraries beyond what's listed in the findings table. Focus rings, hover/pressed states and button/input sizing (`_base.scss`, `_button.scss`) were read in full and are already 100% token-driven with no changes needed.

## 3. Verification

### 3.1 Static checks (all real, all ran clean)

```
npx nx run-many -t lint -p ui-layout ui-patterns ui-admin ui-primitives storefront
```
→ **5/5 succeeded** (0 cache hits, 6.8s).

```
npx nx run-many -t test -p ui-layout ui-patterns ui-admin ui-primitives storefront
```
→ **5/5 succeeded** (0 cache hits, 41.4s). Neither `Grid` nor `CmsBlockRenderer` has an existing spec file, so this run is a regression check on everything downstream (product grid, filter panel, listing pages, etc.), not new coverage of the fixed code — flagged rather than silently presented as "tested."

```
npx nx run-many -t build -p storefront
npx nx run-many -t build -p admin
```
→ **Both succeeded** (production configuration). This is the strongest available check for the CSS/template edits themselves: an Angular production build fails on template binding errors and on `styles` that don't compile, and both apps compiled clean with every changed file in their dependency graph.

### 3.2 Live-stack visual check — attempted, partially blocked by an environment issue unrelated to this review's code

The dev stack (`docker ps`) was already up. To see the actual fixes rendered rather than relying on static review alone, `docker compose -f infra/compose/docker-compose.dev.yml build storefront` was run — **the production build embedded in that image succeeded** with these changes (same Angular build as §3.1, inside the container). Restarting the container (`docker compose ... up -d storefront`) then triggered Compose to recreate `postgres` and `api` as well (a dependency-graph side effect, not something this review's file changes caused), and the recreated `postgres` container failed to rebind host port `5432`:

```
Error response from daemon: ports are not available: exposing port TCP 127.0.0.1:5432 -> 127.0.0.1:0:
listen tcp4 127.0.0.1:5432: bind: An attempt was made to access a socket in a way forbidden by its access permissions.
```

Investigated rather than retried blindly: a native Windows service, **`postgresql-x64-18`** (`Status: Running`, `StartType: Automatic`) — a PostgreSQL install on this machine entirely unrelated to this project's Docker Compose stack — was already bound to `0.0.0.0:5432` (confirmed via `netstat`/`Get-CimInstance`/`Get-Service`). This is not something this session started or touched, and it was **not** stopped or reconfigured — doing that to a running, auto-start Windows service outside this repository's ownership is outside what this review is authorised to do, particularly since other worktrees for this same repo exist on this machine (`.claude/worktrees/...`), implying other concurrent work this session has no visibility into may depend on it.

**Net effect on the dev stack**: `klarahome-dev-postgres`, `klarahome-dev-api` and `klarahome-dev-storefront` are currently `Created` (stopped) rather than the `Up (healthy)` state they were in before this review touched them. `klarahome-dev-admin` and every other service (`redis`, `minio`, `mailpit`, `traefik`, `imgproxy`, `worker`) were not touched and remain `Up (healthy)`. **This needs a human to resolve** — either stop/reconfigure whatever put `postgresql-x64-18` on port 5432, or remap the compose file's Postgres port — after which `docker compose -f infra/compose/docker-compose.dev.yml up -d` will bring the three affected containers back. No code change in this review caused or depends on this; it is recorded here in full rather than glossed over.

**Consequently, the actual rendered-pixel check (`storefront-e2e:responsive-qa` / `:visual-regression` against 360/768/1024/1280) did not happen this pass.** Everything in §2 was verified by: reading every token and component involved, hand-tracing the CSS (including the `calc()`/`min()`/`max()` arithmetic in the new `Grid` formula against concrete viewport numbers at 360/768/1024/1280 for both `14rem` and `7rem` floors — shown working out to 1/2 columns at 360px, reflowing up to the merchant's configured count by ~1024px, for representative column counts of 3 and 4), and the static checks in §3.1. That is a materially weaker guarantee than an actual screenshot, and is named as such rather than implied to be equivalent.

### 3.3 Visual-regression baselines that would legitimately change

`apps/storefront-e2e/src/theming.visual.spec.ts`'s **`home.png`** baseline (full-page screenshot of `/`) renders through `CmsBlockRenderer` and will shift because of the `--space-section` gap change (§2.2) and, if the seeded home page uses a `bannerGrid`/`categoryTiles`/`productCarousel` block, the column-floor and margin fixes in §2.1. **Not regenerated by this pass** — per instructions, baselines are for a human to review and commit deliberately, not to be silently replaced by the same change that invalidated them. `category-listing.png`, `pdp.png` and `checkout.png` are not expected to move: none of those routes render through `CmsBlockRenderer`, and the PDP's standalone "Recently viewed" carousel was deliberately left untouched (its own `margin-block` is still doing real work there — see §2.1's `productCarousel` fix, which only neutralises that margin *inside* the renderer).

## 4. Files touched (original pass)

- `src/frontend/libs/ui/layout/src/lib/grid.ts` — responsive `fixedColumns`
- `src/frontend/libs/ui/layout/src/lib/container.ts` — `68ch` → `var(--measure)`
- `src/frontend/libs/ui/patterns/src/lib/cms-block-renderer.ts` — grid floors, carousel margin, section-rhythm gap, `overflow-wrap`/`min-width`, rich-text/FAQ `.rich-body` styling, `68ch` → `var(--measure)`
- `src/frontend/libs/ui/admin/src/lib/report-chart.ts` — `11px` → `var(--text-xs)`
- `src/frontend/libs/ui/primitives/src/lib/stepper.ts` — `max-width` media query → mobile-first `min-width`
- `src/frontend/apps/storefront/src/app/pages/category.page.ts`, `vendor.page.ts`, `collection.page.ts` — `68ch` → `var(--measure)`

---

## Follow-up pass (2026-09-11)

Everything left open in the report above, worked through in one pass: the page composer's own
responsiveness, admin's breakpoint scale, a lint guard against regressing either, the rich-text
`wide` tier, the hero-overlay rewrite the User chose, parking the composer's visual preview, a
real-browser stress test of every CMS block against hostile content, and the admin/baseline
verification the first pass could not do. §0 above corrected findings #8, #11 and #14 and the
"no admin editor" claim in place; this section is everything new.

### F.1 The page composer, at 360 / 768 / 1024 / 1280

Read in full (`apps/admin/src/app/pages/content/page-composer.page.ts`) and its child components
(`kh-reorder-list`, `kh-schema-field`, `kh-modal`, `kh-media-picker`, `kh-page-header`). Four real
issues, one thing checked and confirmed already safe, one deliberately left alone:

| Issue | Fix |
|---|---|
| `.row` (the two `datetime-local` "Shows from"/"Shows until" fields) had no `flex-wrap`. A `datetime-local` input's browser-drawn intrinsic minimum width is large enough that two side by side overflow well before 360px. The identical pattern existed in `banners.page.ts`'s own scheduling row. | Both now `flex-wrap: wrap`, with `.row > kh-field { flex: 1 1 12rem; min-inline-size: 0; }` — wraps to one field per line instead of squeezing or overflowing. |
| `.item > header` (the index number plus Up / Down / Remove), `.block header` (the block-type heading plus its type badge), and `.versions li` (a version's timestamp/author line plus its Preview/Restore buttons) all had no `flex-wrap` either. | All three now wrap (`flex-wrap: wrap` plus a small `gap`), so three touch targets or a type badge drop to their own line rather than squeezing text into an unreadable sliver. |
| Four nested padded boxes — the admin shell's own gutter, `.block`, `.items`, `.item` — each add their own `padding`, eating the narrowest width fastest. At 360px the stack was 56px of padding per side before a single field or button. | `.panel`/`.block`/`.library` drop from `--space-4` to `--space-3` below `md`, and `.items`/`.item` from `--space-3` to `--space-2`, both restoring their fuller padding at `min-width: 768px` — the same mobile-first pattern the rest of the design system already uses for section rhythm, applied here to a screen that had never had it. |
| `reorder-list.ts`'s `.label`/`.sublabel`/`.meta` had no `overflow-wrap`, and `.controls` (the three move/remove buttons) had no `flex-shrink: 0` — a long unbroken block label could in principle push the buttons out of a comfortable tap width instead of the label wrapping around them. | `overflow-wrap: anywhere` added to the three text spans; `.controls` now `flex-shrink: 0` so the buttons hold their size while `.body` (already `min-inline-size: 0`) is what shrinks. |
| **Checked, not changed:** `kh-modal`'s `width="48rem"` (the preview dialog). | Confirmed safe by CSS reasoning: `.panel` is `width: 100%; max-width: <the input>` inside a `.wrap` that is itself `padding: var(--space-4)` — at any viewport, `width: 100%` resolves against the padded, already-narrower `.wrap`, so `max-width: 48rem` can only ever make the panel *narrower*, never force it past the viewport. No fix needed. |
| **Checked, and fixed defensively:** `kh-media-picker`'s `.bar` (Upload/Refresh) and `.pager` (Previous/Next) had no `flex-wrap`, and its `.grid` used a bare `minmax(8rem, 1fr)` rather than the `min(8rem, 100%)`-guarded form `kh-grid` itself uses. | `flex-wrap: wrap` added to both; the grid's floor now reads `minmax(min(8rem, 100%), 1fr)`, matching the guard `libs/ui/layout/src/lib/grid.ts`'s own doc comment names as "the standard bug in this pattern." |
| `kh-schema-field` — every branch (`kh-checkbox`, `select`, `textarea`, `input`) renders inside `kh-field`, which is already block-level and full-width. No responsiveness issue found; nothing to fix. | — |

**Other content screens, a quick look for the same patterns** (`content/banners`, `menus`,
`menu-editor`, `collections`, `collection-detail`, `pages`): `menus.page.ts`, `collections.page.ts`
and `pages.page.ts` have no `display: flex` rows at all (list/table screens, out of this pattern's
reach). `banners.page.ts` had the identical un-wrapped `datetime-local` `.row` as the composer —
fixed the same way — plus a defensive `flex-wrap` added to `.image-actions`.
`collection-detail.page.ts`'s `.row` already wrapped; `.actions` and `.hero-actions` got the same
defensive `flex-wrap` `.image-actions` did. None of these had a live user report behind them — they
are the same shape of bug the composer had, caught by pattern-matching rather than by running each
screen, since none of the content-admin screens are reachable without the same admin login this
pass could not complete (§F.8).

### F.2 Admin breakpoints, normalised to the token scale

`libs/ui/primitives/src/styles/_breakpoints.scss` is the canonical source: **six breakpoints, in
`px`, `min-width`-only** — `sm 480, md 768, lg 1024, xl 1280, 2xl 1536` (`xs` is `0` and has no
query). The storefront already wrote every query in exactly that scale and unit. Admin did not: a
grep of every `@media` in `apps/admin` and `libs/ui/admin` found `32rem, 40rem, 48rem, 56rem, 60rem,
60.0625rem, 64rem, 90rem` — eight different values, in `rem`, none of them literally matching the
canonical six even where the *pixel* value happened to coincide (`48rem` = `768px` = `md`, by
accident of arithmetic, not by design).

Every one of them is now `px`, and mapped onto the six-value scale — multi-column splits rounded
**up**, so the extra column only appears once there is room for it:

| File | Old | New | Why |
|---|---|---|---|
| `catalog/variant-editor.ts` | `40rem` | `768px` (`md`) | Two/three-field form pair — 40rem (640px) sits between `sm` and `md`; rounded up. |
| `fulfilment/shipments.page.ts` | `40rem` | `768px` (`md`) | Same `.pair` two-column pattern. |
| `inventory/suppliers.page.ts` | `40rem` | `768px` (`md`) | Same. |
| `inventory/warehouses.page.ts` | `40rem` | `768px` (`md`) | Same. |
| `settings/shipping-zones.page.ts` | `40rem` | `768px` (`md`) | Same. |
| `vendors/vendor-detail.page.ts:460` | `32rem` | `768px` (`md`) | `.pair` — 32rem (512px) is *above* `sm` (480px), so `sm` would be rounding down; `md` is the next value at or above it. |
| `inventory/adjustments.page.ts` | `56rem` | `1024px` (`lg`) | `.columns` two-up — 56rem (896px) sits between `md` and `lg`; rounded up. |
| `content/menu-editor.page.ts` | `60rem` | `1024px` (`lg`) | `.layout` sidebar split — 60rem (960px) sits between `md` and `lg`. |
| `pricing/price-list-detail.page.ts` | `60rem` | `1024px` (`lg`) | Same `.layout` pattern. |
| `pricing/promotion-detail.page.ts` | `60rem` | `1024px` (`lg`) | Same. |
| `pricing/tax-rates.page.ts` | `60rem` | `1024px` (`lg`) | Same. |
| `settings/user-detail.page.ts` | `60rem` | `1024px` (`lg`) | Same. |
| `vendors/commission-plans.page.ts` | `60rem` | `1024px` (`lg`) | Same. |
| `libs/ui/admin/admin-shell.ts` (sidebar column) | `60.0625rem` | `1024px` (`lg`), and **`min-width`** stays `min-width` | See below — moved as a pair with the next row. |
| `libs/ui/admin/admin-top-bar.ts` (search/identity collapse) | `max-width: 60rem` | `min-width: 1024px`, **inverted** | See below. |
| `apps/storefront/pages/cart.page.ts` | `max-width: 1023px` | `min-width: 1024px`, **inverted** | See below — storefront, not admin, but the same `max-width` defect. |
| `libs/ui/admin/admin-shell.ts` (`main` reading-width cap) | `90rem` | `1536px` (`2xl`) | 90rem (1440px) sits between `xl` and `2xl`; rounded up. |
| *(already on-scale, unit only)* `catalog/product-detail.page.ts` | `48rem` | `768px` | Value already matched `md`; only the unit changed, for one consistent unit across every admin query. |
| *(unit only)* `content/collection-detail.page.ts`, `content/page-composer.page.ts`, `orders/order-detail.page.ts`, `returns/return-detail.page.ts`, `vendor/onboarding.page.ts`, `vendor/profile.page.ts`, `vendors/vendor-detail.page.ts:422` | `64rem` | `1024px` | Value already matched `lg`; unit-only conversion, six files. |
| **Confirmed correct, no change:** `libs/ui/primitives/lib/stepper.ts` | `min-width: 480px` | — | Already exactly `sm`, already `min-width`, already `px`. |

**The admin shell's sidebar breakpoint is the one pair that needed more than a value swap.**
`admin-shell.ts`'s `@media (min-width: 60.0625rem)` (sidebar becomes a grid column) and
`admin-top-bar.ts`'s `@media (max-width: 60rem)` (search box and identity collapse to icons) were
one offset breakpoint apart *on purpose* — `60rem` and `60.0625rem` (1px) never both match nor both
miss at the same viewport width, so the sidebar and the top bar never disagreed about which side of
the line a given width was on. That precision was only ever needed because the pair used opposite
directions of query. Moved onto the same value (`1024px`, `lg`) **and the same direction**
(`min-width`), the 1px offset is no longer needed at all: `admin-top-bar.ts`'s compact state
(icon-only search, hidden identity) is now the mobile-first *base* rule, restored at
`min-width: 1024px` — the exact width `admin-shell.ts` switches the sidebar from a drawer to a
column at. The two now literally share one breakpoint value instead of two adjacent ones.
`cart.page.ts`'s `.checkout-desktop` (the panel's own checkout button, redundant with the sticky
bar below `lg`) got the identical treatment: hidden by default, `display: flex` (matching
`khButton [block]`'s own rendered display) restored at `min-width: 1024px`.

### F.3 The lint guard: `local/no-hardcoded-spacing-in-styles`

A sibling to the existing `local/no-hardcoded-color-in-styles` (`eslint.config.mjs`), same
AST-walk-not-CSS-parser approach, same `styles:` template-literal target. It flags three things:

1. `font-size` set to a raw `px`/`rem`/`em` literal.
2. `padding` / `margin` / `gap` / `row-gap` / `column-gap` / `inset*` set to a raw `px`/`rem`/`em`
   literal. `0` is always allowed (there is no token for "none"); a hairline offset inside `calc()`
   alongside a token — `calc(var(--space-3) - 1px)`, `order-timeline.ts:69` — is allowed too, since
   that pattern exists to shim a hairline off a token, not to skip the token.
3. An `@media` width that is not one of the six token breakpoints, in `_breakpoints.scss`'s own
   unit (`px`) — and a `max-width` query is **always** flagged, regardless of its value, since that
   file is `min-width`-only by design and the fix is always to invert the rule.

A real bug was found writing it (an off-by-one in the `calc()`-detection helper: `'calc('.length`
is 5, not 4, and using 4 meant the depth-counting loop started on the calc's own opening paren
instead of just past it, so it never found the matching close and every `calc(var(...) ± Npx)`
pattern was flagged as if it were bare) — fixed before the rule shipped, verified against
`order-timeline.ts:69`'s real hairline-offset call site, which does **not** get flagged.

Run against the whole workspace, it caught exactly the one violation already named in this brief:
`libs/ui/admin/src/lib/entity-picker.ts:177`'s `.option { gap: 2px; }` — a deliberate micro-gap
between a result's title and subtitle line, below the smallest spacing token (`--space-1`, 4px).
Doubling it to 4px would be a visual change of its own, and one call site does not justify a new
token below `--space-1`. Disabled with a reason: `/* eslint-disable
local/no-hardcoded-spacing-in-styles -- ... */` before the `styles:` property and `/* eslint-enable
*/` after it closes — a block disable rather than a line disable, because the violation sits deep
inside a multi-line template literal, where a directive comment cannot be attached to just that
one line (the same constraint the colour rule's own doc comment already names for its exceptions).

`npx nx run-many -t lint --all` is clean across all 23 projects with the new rule active — see §F.9
for the actual run.

`tools/ci.ps1`'s lint-stage comment and `docs/10-design-system.md` §7 are both updated to describe
what the two eslint rules now cover between them, and that stylelint's job is narrower than the
comment used to say (the real `.scss` files only — the inline `styles:` literals are eslint's job).

### F.4 Rich text's third width tier: `wide`

Added `--container-wide: 64rem` to `_tokens.scss` (`lg` in rem, the same value the breakpoint scale
already uses, not a number invented for this one class), documented in the same comment style as
its neighbours. `.rich[data-width='wide']` now caps at it. All three tiers documented in a new
`docs/10-design-system.md` §5.1:

| `width` | Cap | Alignment |
|---|---|---|
| `narrow` | `var(--measure)` (68ch) | Centred |
| `wide` | `var(--container-wide)` (64rem / 1024px) | Centred |
| `full` | None — fills `khContainer` | N/A |

**Alignment was inconsistent before this and is not now.** `narrow` previously had a bare
`max-inline-size` with no `margin-inline`, which insets a block from the *right* edge only — next
to a full-width hero or banner grid, the text visibly jogs left instead of sitting centred in the
same container every other block fills. Both `narrow` and `wide` now get `margin-inline: auto`.

### F.5 Every hero: text over the image (the User's visual-identity decision)

Storefront-only, `libs/ui/patterns/src/lib/cms-block-renderer.ts`'s `hero` case, no schema change.

- **Stacked CSS grid, not `position: absolute`.** `.hero-media` is `display: grid`; the image, a
  scrim layer and `.hero-copy` all share `grid-area: 1 / 1`. A grid row auto-sizes to the tallest
  content sharing it, so a long headline + subheadline + CTA that needs more height than the
  image's aspect ratio provides simply makes the row — and the image beneath it, `object-fit: cover`
  — taller, rather than clipping or overlapping.

  **Correction — as first shipped in this pass, it did clip.** The claim above was originally
  written as "verified against a deliberately long headline in the stress fixture", but the
  verification was a horizontal-overflow assertion and a full-page screenshot looked at at too
  small a scale. Cropped to full size, the long-headline hero had its subheadline cut off
  mid-sentence at 360px, and at 1280px the subheadline was clipped away entirely — and the new
  assertion below measured it as worse than it looked: **every** photograph hero clipped at
  **every** breakpoint, not only the long one. Copy ran 89px past the box at 360px, and at 768 /
  1024 / 1280px the long-headline hero overran by 412 / 244 / 180px while even the short,
  typical centred and right-aligned heroes overran by 14–99px. The cause:
  `.hero-media` carried `overflow: hidden` for its rounded corners, and a box with an
  `aspect-ratio` only grows past that ratio to fit its content while it is *not* a scroll container
  — `overflow: hidden` makes it one, so the ratio became a fixed height. Fixed by dropping
  `overflow` from `.hero-media`, clipping the corners on the layers that fill it instead
  (`border-radius: inherit` on `.hero-image` and `.scrim`), and giving `.hero-image`
  `contain: size` so the photograph fills the box without sizing it (otherwise
  `kh-product-image`'s default square ratio would make a 1248px-wide desktop hero 1248px tall). The
  stress spec gained a test that fails when any `.hero-copy` extends outside its box or is clipped
  inside itself. It was run against the old build first and failed there, so it is known to catch
  this. The coloured band along the top and bottom edges of the stress fixture's heroes is not a
  layering fault: it is the stripe baked into the stand-in image (`/brand/og-default.png`), cropped
  into view by `object-fit: cover`.
- **Responsive aspect ratio, not a responsive `ratio` input.** `.hero-media` is `aspect-ratio: 4/5`
  below `md` (16:9 at 360px is ~200px tall — not enough room for real copy before it already
  crowds the photo) and `16/9` from `md` up. `kh-product-image` (`libs/ui/primitives/src/lib/
  product-image.ts`) gained two small, backward-compatible CSS hooks for this rather than a new
  input: `block-size: var(--kh-image-block-size, auto)` and `object-fit: var(--kh-image-fit,
  contain)`, both hardcoded `var()` references in the component's own static styles rather than
  bound through Angular (`[style.x]` bindings produce an *inline* style, which no external rule —
  responsive or otherwise — can then override; a plain `var()` reference has no such problem, and
  every other caller of `kh-product-image` is completely unaffected since nothing else sets either
  custom property). `CmsBlockRenderer` sets both on the hero's image via an ordinary class selector.
- **The scrim is `color-mix()` against a semantic token, not a hex/rgb literal** (which the eslint
  rule above forbids and which would not survive a re-theme regardless): `color-mix(in srgb,
  var(--color-surface-inverse) 92%, transparent)`. **The 92% was computed, not guessed** — see the
  new `docs/10-design-system.md` §1.2 entry: blended over a worst-case *pure white* photograph in
  linear light (WCAG's own method), it leaves a background luminance of ≈0.083; the new
  `--color-text-on-image` token (sand-050, luminance ≈0.926) against that is 9.8:1, clearing both
  the subheadline's 4.5:1 and the display headline's 3.0:1 with real margin. A new token rather than
  reusing `--color-text-inverse`: the two happen to share a value today, but the contract is
  different — one promises legibility on `--color-surface-inverse`, a fixed known background; the
  new one promises legibility over the scrim on top of a merchant's own, re-themeable photograph,
  and a future re-theme may need to diverge them.
- **`align` moves both the copy and the scrim's gradient.** Below `md` the scrim is a flat, fully
  opaque-enough tint regardless of `align` (the copy runs close to the full width of the box there,
  so a gradient sized for a narrower desktop column would not stay safely opaque under a full-width
  mobile one). From `md` up, `align: left/right` produces a gradient darkest on that side, fading
  out on the other; `centre` is dark in the middle, fading at both edges. `.hero-copy`'s own
  `max-inline-size: 38%` (a percentage, not a fixed length, so the relationship holds at any width
  in this tier, not only at `md`'s own edge) stays inside the gradient's guaranteed-opaque plateau
  with margin at every point.
- **`image: null` gets the same copy treatment on `--color-surface-inverse`**, no scrim needed
  (there is no photograph to guarantee contrast against), sized by generous padding and a
  `min-block-size` rather than the aspect ratio a photograph would otherwise drive.
- **The CTA switched from `variant="primary"` to `variant="inverse"`.** `docs/10-design-system.md`
  already explains why: the primary button's own background (coffee) is only 3.2:1 against
  `--color-surface-inverse` — the ink the scrim is built from — which is a non-text **boundary**
  contrast problem (the button itself would be hard to make out as a distinct control against a
  similarly dark background), not a text-contrast one. `kh-button--inverse` (light background, dark
  text) is the variant the design system already names for exactly this situation.
- **`:focus-visible` switches to `--color-focus-ring-inverse`** inside `.hero-copy`, the same
  override `.kh-band--inverse` (`_base.scss`) already uses for the footer, for the identical reason:
  the default ink ring is close to invisible against the same ink the scrim is mixed from.
- **LCP `priority`/`sizes` behaviour is unchanged** — still `[priority]="first && prioritiseFirst()"`
  and `sizes="100vw"` on the hero's image, untouched by the layout rewrite.

### F.6 Parked: the composer's visual preview

Added to `docs/PARKING_LOT.md` (2026-09-11, Step 30 frontend-visual-consistency-review row),
matching the ledger's existing table format: the preview modal shows the raw block JSON via
`<pre>{{ pretty(block.config) }}</pre>`, not a rendering a merchandiser could actually judge. Real
work — a storefront preview route or an in-admin embedded render of `CmsBlockRenderer`, either way
paired with a signed draft token — filed as **Backlog (post-MVP)**, not attempted here.

### F.7 The real-browser stress test — and what it actually found

`apps/storefront-e2e/src/cms-blocks-stress.responsive.spec.ts`, wired into the same
`responsive-qa` Nx target and `playwright.responsive.config.mts` the rest of this suite uses.

**How the fixture reaches a real page, without an intercepted API response or an admin-authored
page.** Both of the brief's own suggestions turned out to be blocked, not merely inconvenient:
`page.route()` cannot intercept the CMS page's data at all, because `cmsPageResolver` fetches it
**server-side** during SSR — Playwright can only intercept requests the *browser* makes, and the
storefront's SSR process talks to the API container directly over the Docker network, a request the
browser-facing Playwright session never sees. Creating a real page through the admin API hits a
different wall: `platform-admin` is mandatory-two-factor, this account's authenticator is already
enrolled from earlier work, there is no record of that original secret to compute a code from, and
neither re-enrolling nor resetting a live account's 2FA is something a stress-test fixture should
be doing (§F.8 has the full account of what was tried and why each avenue was correctly refused).
So the fixture is a new, unlinked, `noIndex: true` route,
`apps/storefront/src/app/pages/dev/cms-stress-test.page.ts` at `/__test/cms-blocks`, passing a
literal `CmsBlockView[]` straight to `CmsBlockRenderer` — no API round trip to intercept and nothing
to authenticate, the same component and the same global styles a real page gets, in a real browser,
at every breakpoint.

**The route only exists in `local`/`development`.** As first written it was registered
unconditionally, so a page of deliberately hostile content would have shipped to production behind
nothing but `noIndex`. It now carries a `canMatch` on `RUNTIME_CONFIG.environment` (the field
`runtime-config.ts` documents as "for hiding developer affordances in production"): on staging and
production the route never matches, its lazy chunk is never requested, and the URL falls through to
`**`. The dev stack runs as `local` (`KH_ENVIRONMENT`, `.env`), while `write-runtime-config.sh`
defaults to `production` when the variable is unset, so a deployment that forgets the variable fails
closed. It is deliberately absent from `app.routes.server.ts`, so the server answers it under the
catch-all's 404 status even where it renders — listing it would turn the production fall-through
into a soft 404 (200 status, not-found body).

**The fixture covers everything the brief asked for**: long unbroken words and a long unbroken URL
(in a hero headline, a hero subheadline, a banner caption, a category-tile label, an FAQ answer and
a rich-text paragraph); `bannerGrid` at columns `{1, 2, 4, 6}` crossed with items `{1, 2, 12}`;
`categoryTiles` at columns `{1, 2, 3, 6}` crossed with items `{1, 2, 5, 12}`; a rich-text block with
an 8-column table, an embedded image, a blockquote and a list, at all three width tiers; a
nine-question FAQ including one deliberately long question and a long, list-carrying answer; five
testimonials from one word to one long paragraph, with missing ratings and missing locations; four
heroes covering every `align` value plus one with no image; and an eight-product carousel with the
same missing-price/missing-rating/missing-image/no-purchasable edge cases `product-grid` already
has to survive.

**Two real bugs, found by looking at the screenshots rather than only trusting the overflow
assertion — which passed at every breakpoint throughout, on both bugs, because neither is a
horizontal-overflow defect:**

1. **Every `.rich-body :where(...)` rule added in the original pass (§2.1) had never actually
   applied, to anything, ever.** `[innerHTML]` content is parsed by the browser directly and never
   receives the `_ngcontent-X` attribute Angular's emulated view encapsulation stamps onto
   everything it renders itself; a scoped selector reaching for a descendant of `.rich-body`
   compiles to `.rich-body[_ngcontent-X] :where(table[_ngcontent-X])` and can never match a table
   that carries no such attribute. Confirmed directly:
   `document.querySelector('.rich-body table').getAttributeNames()` returned `[]` on the live page.
   Every heading, list, image, blockquote and table style the original pass added to `richText`/
   `faq` content was dead code that compiled cleanly, shipped, and did nothing — with no error from
   Angular or the browser. **Fixed**: the descendant rules moved out of `cms-block-renderer.ts`'s
   scoped `styles:` into `libs/ui/primitives/src/styles/_base.scss`, globally, under `.rich-body` —
   unscoped CSS has no `_ngcontent` requirement to fail to match, which is exactly how `.kh-prose`
   (`_typography.scss`) already solves the identical problem for a product description or a policy
   page. Only `.rich-body`'s own rule (`overflow-wrap`, `min-width` — targeting the container
   `<div>` itself, which *is* Angular-rendered) stays in the component.
2. **The table fix specifically (§2.1's `.rich-body :where(table)`) was also the wrong CSS even
   once it could match.** `display: block; max-width: 100%; overflow-x: auto` is the commonly-cited
   recipe for a scrollable table with no wrapper element, and it still was not enough: with plain,
   breakable header words (`Material`, `Weave`, …) and no long unbroken run forcing a column wider,
   the browser's own automatic table layout shrinks and wraps every cell to fit the available width
   *first*, and only escapes into a scrollbar once it truly cannot shrink further. An 8-column
   stress table rendered every header as a vertical stack of single letters, at 360px, with no
   scrollbar at all — verified before concluding it was fixed, not assumed: `min-width:
   max-content` was tried first (a commonly-suggested alternative) and made no difference either,
   confirmed by reading the table's own computed styles rather than only its screenshot. **The fix
   that actually worked**: `white-space: nowrap` on every `th`/`td`. A column can then no longer
   shrink below its own unbroken content, the table's true total width exceeds its `max-width: 100%`
   container, and `overflow-x: auto` finally has something real to scroll — confirmed via
   `scrollWidth: 850 > clientWidth: 328` at 360px (and `scrollWidth === clientWidth` at 1280px,
   where all eight columns comfortably fit and no scrolling is needed at all), and visually: headers
   read as `Material | Weave | Width | Weight (gs…`, clipped at the scrollable edge, rather than a
   column of individually-wrapped letters.

**Everything else held up as designed, confirmed by the screenshots rather than only by reasoning
about the CSS beforehand:**

- The responsive `fixedColumns` formula (`grid.ts`, §2.1) reflows correctly at every combination
  tried: a "6 columns configured, 1 item" banner is full-width at 360px (the floor genuinely only
  fits one column at that width, which is correct, not a bug — nothing merchant-configured is
  visually distinguishable from a 1-column layout below the width where a second column could ever
  fit) and sits at its true one-sixth width with the trailing five-sixths left empty at 1280px,
  never stretched (`closeup-mobile-360-05.png`, `closeup-desktop-1280-05.png`) — exactly finding
  #13's reasoning, now seen rather than only argued. A "4 columns configured, 12 items" block
  reflows to a real 4-column grid at 1280px and stacks to 1 column at 360px
  (`closeup-desktop-1280-07.png`, `closeup-mobile-360-07.png`). A "6 columns configured, 2 items"
  category-tile block shows exactly 2 tiles at their true one-sixth width at 1280px, not stretched
  to fill the row (`closeup-desktop-1280-09.png`) — including one tile carrying the long unbroken
  word, which wraps cleanly inside its own narrow column.
- The hero rewrite (§F.5) holds up in all four configurations: a very long headline wraps mid-word
  and grows the box rather than clipping (`closeup-mobile-360-00.png`); `centre` and `right` align
  both position the copy and its CTA correctly, with the right-aligned URL subheadline wrapping at
  its own hyphens; the no-image hero renders the flat ink surface with the inverse CTA correctly.
- The FAQ's long question wraps its `<summary>` onto multiple lines without the chevron marker
  drifting off the row (`closeup-mobile-360-15.png`). The testimonial grid holds a one-word quote
  and a six-sentence quote in the same row without either stretching or overflowing
  (`closeup-mobile-360-16.png`).

Screenshots are saved under the gitignored `test-output/responsive-review/` — both the four
committed full-page captures (`cms-blocks-stress-{mobile-360,tablet-768,laptop-1024,
desktop-1280}.png`, from the actual `responsive-qa` Nx target run) and the per-block close-ups
quoted above (captured with a temporary, deleted-before-commit spec — not part of the permanent
suite, since the permanent one only needs the overflow assertion and the full-page captures to do
its job on every future run).

### F.8 Admin verification — mostly blocked, and why, in detail

Rebuilt cleanly (`docker compose -f infra/compose/docker-compose.dev.yml --env-file .env up -d
--build --no-deps admin`, then the same for `storefront` after each round of fixes — `--no-deps`
and `--env-file .env` used on every rebuild this pass, per the correction the coordinator gave;
`postgres`/`api` were never recreated). `npx nx run admin-e2e:responsive-qa` passes 8/8 — but that
suite only reaches `/login` and `/forgot-password`, the same anonymous-only limit
`responsive-qa-report.md` already documented; it says nothing about the composer.

**A real attempt was made to reach it live**, not skipped on the first sign of friction:

1. Scripted the documented login flow (`docs/dev-setup.md`'s three-step password → 2FA enrol →
   2FA verify) in Node, computing the TOTP code from the enrolment secret with `node:crypto`
   (HMAC-SHA1, RFC 6238) — no external dependency, per the brief's own suggestion.
2. Node cannot resolve `*.klarahome.localhost` (a documented, pre-existing gap —
   `docs/dev-setup.md` §5 — browsers special-case `.localhost`, non-browser clients do not) and
   adding it to the Windows hosts file is a system-level change outside this session's authority;
   worked around entirely within the script instead, with a custom DNS `lookup` forcing
   `127.0.0.1` while keeping the real hostname as the TLS SNI / `Host` header (the same thing
   `curl --resolve` does) — no system file touched.
3. The password step succeeded and returned a 2FA challenge, as documented. The enrolment step
   did not: `409 IDENTITY_TWO_FACTOR_ALREADY_ENABLED` — "this account already has an authenticator.
   Sign in with a code, or ask an administrator to reset it." The bootstrap admin was already
   enrolled from earlier work on this project, and this session has no record of that original
   secret.
4. Two ways to get past that were identified and **both correctly refused**: resetting
   `identity.users.totp_enabled`/`totp_secret_encrypted` directly in the dev database (a single
   `UPDATE`, technically trivial) was refused as weakening the account's security posture, even on
   local dev data; re-running the enrolment against a different flow was not attempted for the
   same reason. Neither was appealed or worked around — the correct response to a security
   guardrail doing its job is to stop, not to route around it.

**Net effect**: the composer, the sidebar/top-bar breakpoint pair, and every other
`authenticatedGuard`-gated admin screen were verified **statically only** — full-file reads,
`nx build`/`lint`/`test` (which compile the templates and catch a broken binding or a template
error, though not a rendering defect), and the CSS reasoning in §F.1/§F.2 above. This is a real,
named gap, not a silent one: a scripted-login fixture for the admin e2e suite (the same gap
`responsive-qa-report.md` already carried forward once) would close it, and is left for whoever
next touches that suite, the same way that report left it.

### F.9 Verification — commands actually run, and their real results

```
npx nx run-many -t lint --all
```
→ **23/23 succeeded** (19 cache hits, 4 fresh — `storefront-e2e`, `ui-admin`, `admin`, `admin-e2e`
had changed since the last run). This is the run that proves `local/no-hardcoded-spacing-in-styles`
is clean workspace-wide, not just on the files this pass touched.

```
npx nx run-many -t test --all
```
→ **21/21 succeeded** (4 cache hits, 17 fresh).

```
npx nx run-many -t build -p storefront admin
```
→ **Both succeeded** (`storefront` cache-hit on an unchanged final state, `admin` fresh).

```
npx nx run storefront-e2e:responsive-qa   # 56/56 passed, includes the new stress spec at all 4 breakpoints
npx nx run admin-e2e:responsive-qa        # 8/8 passed (sign-in, forgot-password only — see §F.8)
npx nx run storefront-e2e:visual-regression
npx nx run admin-e2e:visual-regression    # 4/4 passed
```

`storefront-e2e:visual-regression`: **7/8 passed, 1 failed — `home` at `mobile`**, unchanged from
the state the coordinator described before this follow-up pass began (`892px` expected vs `844px`
received, no pixel-level diff reported, height only). Confirmed this pass did not make it worse:
the same command, run again after every fix in this section including the full hero rewrite,
produces the identical `892→844` result. That the hero rewrite did not move this baseline further
is consistent with the seeded home page's blocks not including a `hero` block at all — nothing in
`desktop`'s passing `home` baseline or `mobile`'s single height-only failure shows any sign of the
scrim/overlay change. The `-48px` at mobile is explained by the original pass's own disclosed,
intended changes: removing `kh-product-carousel`'s doubled margin inside `CmsBlockRenderer` (§2.1)
and the `--space-8` → `--space-section` gap change (§2.2), both already named in §3.3 above as
baselines that would legitimately move. **Not updated** — per instruction, left for the User to
review and commit deliberately.

**Live-stack visual check: yes, this time, for the storefront.** Every CSS fix in this section
(the composer's own styles cannot be seen live, per §F.8, but everything in `cms-block-renderer.ts`,
`grid.ts`, `product-image.ts`, `_base.scss` and `_tokens.scss` can) was rebuilt into the running
`klarahome-dev-storefront` container and inspected with real Playwright screenshots and computed-
style queries against it — not simulated, and not assumed correct from the CSS alone, which is
exactly how the two bugs in §F.7 were caught rather than shipped a second time.

## 5. Files touched (follow-up pass)

- `apps/admin/src/app/pages/content/page-composer.page.ts` — responsive `.row`/header/padding fixes (§F.1)
- `apps/admin/src/app/pages/content/banners.page.ts`, `collection-detail.page.ts` — same pattern (§F.1)
- `apps/admin/src/app/pages/catalog/media-picker.ts` — defensive `flex-wrap`, grid floor guard (§F.1)
- `libs/ui/admin/src/lib/reorder-list.ts` — `overflow-wrap`, `.controls` `flex-shrink: 0` (§F.1)
- `libs/ui/admin/src/lib/admin-shell.ts`, `admin-top-bar.ts` — sidebar/top-bar breakpoint pair, `px`, mobile-first (§F.2)
- `apps/admin/src/app/pages/{catalog/variant-editor,catalog/product-detail,content/menu-editor,content/collection-detail,content/page-composer,fulfilment/shipments,inventory/adjustments,inventory/suppliers,inventory/warehouses,orders/order-detail,pricing/price-list-detail,pricing/promotion-detail,pricing/tax-rates,returns/return-detail,settings/shipping-zones,settings/user-detail,vendor/onboarding,vendor/profile,vendors/commission-plans,vendors/vendor-detail}.page.ts` — breakpoint → token scale, `px` (§F.2)
- `apps/storefront/src/app/pages/cart.page.ts` — `max-width` → mobile-first `min-width` (§F.2)
- `src/frontend/eslint.config.mjs` — `local/no-hardcoded-spacing-in-styles` (§F.3)
- `libs/ui/admin/src/lib/entity-picker.ts` — documented `eslint-disable` block (§F.3)
- `tools/ci.ps1`, `docs/10-design-system.md` §1.2, §5.1, §7 — lint coverage, hero contrast, rich-text tiers documented (§F.3, §F.4, §F.5)
- `libs/ui/primitives/src/styles/_tokens.scss` — `--container-wide`, `--color-text-on-image` (§F.4, §F.5)
- `libs/ui/patterns/src/lib/cms-block-renderer.ts` — `wide` tier, full hero rewrite, `.rich-body` fix (§F.4, §F.5, §F.7)
- `libs/ui/primitives/src/lib/product-image.ts` — `--kh-image-block-size`/`--kh-image-fit` hooks (§F.5)
- `libs/ui/primitives/src/styles/_base.scss` — global `.rich-body` descendant styling (§F.7)
- `docs/PARKING_LOT.md` — composer preview parked (§F.6)
- `apps/storefront/src/app/app.routes.ts`, `apps/storefront/src/app/pages/dev/cms-stress-test.page.ts` — the stress fixture route (§F.7)
- `apps/storefront-e2e/src/cms-blocks-stress.responsive.spec.ts` — the permanent stress spec (§F.7)

## 6. Checked again after the follow-up pass

The follow-up pass was checked independently. That meant looking at its stress screenshots cropped
to full size rather than the downscaled full-page image, comparing the failing home baseline
against the old one by eye, and reading the diff for anything shipped to production. It found
three defects the pass had reported as clean, plus two smaller issues.

### 6.1 The stress fixture route was reachable in production — fixed

`/__test/cms-blocks` was registered unconditionally, guarded by nothing but `noIndex`. It now has a
`canMatch` on `RUNTIME_CONFIG.environment` (`local`/`development` only). The dev stack runs as
`local`, and `write-runtime-config.sh` falls back to `production` when `KH_ENVIRONMENT` is unset, so
a deployment that forgets the variable fails closed. Detail in §F.7.

### 6.2 Every photograph hero clipped its copy — fixed

§F.5 claimed the hero grows to fit its copy. It did not: `overflow: hidden` on an `aspect-ratio`
box turns the ratio into a fixed height. Every photograph hero clipped at every breakpoint, by up to
412px. Detail, the fix and the measurements are in the correction under §F.5. The stress spec now
has a clipping test. It was run against the unfixed build first and failed at all four breakpoints,
so it is known to catch this.

### 6.3 The home page's last block touched the footer — fixed

The first pass's `margin-block: 0` on the carousel block (finding #4) was right about the rhythm
*between* blocks. But that margin had also been the home page's only space before the footer:
the seeded home page ends in a carousel. In the mobile baseline the gap between the last card and
the footer's top border went from ~40px to ~9px. `home.page.ts` now ends with
`padding-block-end: var(--space-10)`, the same end padding `cms-page.page.ts` already gives the
other page that frames `CmsBlockRenderer`. The two frames now match, and neither depends on which
block type happens to come last.

### 6.4 An empty block rendered a heading over blank space — fixed

The seeded home page's "Shop by room" block is a `CategoryTiles` block saved with `"items": []`
(checked in `content.page_blocks`). The storefront drew its heading above an empty grid, visible in
the committed mobile baseline. `CmsBlockRenderer` now draws a `visibleBlocks` list that skips any
`bannerGrid`/`categoryTiles`/`faq`/`testimonial` block with no items and any `productCarousel` with
no products. This is the same silent treatment the renderer already gives an unknown block type.
The merchant still sees the empty block in the admin composer, which is where it gets filled in.
**The seed itself is unchanged.** Whoever owns the demo content should give that block some
categories.

### 6.5 Left-aligned heroes stretched their CTA across the copy column — fixed

`.hero-copy` is a flex column, and only the centred and right-aligned variants set `align-items`.
So a left-aligned hero, which is the default, stretched its button to the full copy width: over
400px on desktop. The base rule now sets `align-items: flex-start`, so all three alignments keep
the button at its natural width.

### 6.6 Not defects

- The coloured band along the top and bottom edge of every stress-fixture hero is the stripe baked
  into the stand-in image (`/brand/og-default.png`, brown along the top and dark along the bottom),
  cropped into view by `object-fit: cover`. The layers are aligned.
- The rich-text table scrolls inside its own block at 360px, and the long FAQ question wraps
  cleanly (§F.7's `.rich-body` fixes, confirmed at full size).

### 6.7 A test-environment trap that points to a production risk (backend, not fixed here)

One visual-regression run failed 5 of 8, with the category-listing and product pages rendered at
exactly the viewport height. The storefront's logs showed `[plp] category load failed
RATE_LIMITED` and `[pdp] product load failed RATE_LIMITED`. The run had followed the 60-test
responsive sweep within the same minute. The API's `StorefrontRead` policy allows 300 requests per
60-second fixed window **per client IP** (`appsettings.json`), and `ClientKey` is
`Connection.RemoteIpAddress` (`RateLimitingExtensions.cs:126`). The storefront's SSR process calls
the API directly, so every server-rendered page — every visitor's — is counted against the
storefront container's own IP. Two consequences:

- **For these suites:** run visual regression on its own, or at least a minute after the
  responsive sweep. A run that follows a sweep can fail on rate limits rather than on layout, and
  the responsive suite cannot notice, because an error page has no horizontal overflow either.
- **For production:** all shoppers' SSR traffic shares one 300-per-minute bucket (and the
  600-per-minute global one). Past a few hundred API calls a minute of server-rendered pages
  across the whole store, category and product pages would start rendering their error states to
  real visitors. The fix belongs to the backend, for example exempting or separately budgeting the
  storefront's server identity, or having SSR forward the shopper's address to an API that trusts
  the storefront as a proxy. It is recorded here for the Step 29 / load-testing owner, not changed.

### 6.8 Files touched (this section)

- `apps/storefront/src/app/app.routes.ts`: environment `canMatch` on the fixture route (§6.1)
- `libs/ui/patterns/src/lib/cms-block-renderer.ts`: hero clipping fix, CTA alignment,
  `visibleBlocks` (§6.2, §6.4, §6.5)
- `apps/storefront/src/app/pages/home.page.ts`: end padding (§6.3)
- `apps/storefront-e2e/src/cms-blocks-stress.responsive.spec.ts`: the hero clipping test (§6.2)

### 6.9 Final verification: commands run, real results

| Check | Result |
|---|---|
| `nx run-many -t lint test --all` | ✅ 23/23 projects |
| `nx run-many -t lint test build -p ui-patterns storefront storefront-e2e` | ✅ |
| Hero clipping test against the **unfixed** build | ❌ 4/4 breakpoints failed, as intended: copy overran its box by 14–412px |
| `storefront-e2e:responsive-qa` against the rebuilt storefront | ✅ 60/60: 11 routes × 4 breakpoints, the risk-area screenshots, and the stress fixture's overflow, clipping and screenshot tests |
| `storefront-e2e:visual-regression`, run on its own after the rate-limit window (0 `RATE_LIMITED` in the storefront log during the run) | 7/8, one expected failure (below) |
| Stress screenshots cropped to full size and looked at: heroes at 360 and 1280, the rich-text table and the FAQ at 360 | ✅ after the §6.2 and §6.5 fixes |

**Baselines: none updated.** One fails, and it is intended:

- **`home` at `mobile`: 892px → 808px.** Compared with the committed baseline, the empty "Shop by
  room" heading is gone (§6.4), the gap between blocks is `--space-section` rather than
  `--space-8` (finding #5), and the space between the last card and the footer is ~48px, where the
  baseline had ~40px and the intermediate build ~9px (§6.3). Accept it with
  `npx nx run storefront-e2e:visual-regression -- --update-snapshots` once reviewed.
- **`home` at `desktop` passes, but it did change.** The page is shorter than the 900px viewport,
  so the screenshot keeps the baseline's size, and the same changes stay under the config's
  `maxDiffPixelRatio: 0.02`. Treat that pass as "within 2%", not "unchanged". Re-baseline it
  alongside the mobile one rather than leave a baseline that no longer matches what ships.

Admin was not re-run after this section: nothing here touches an admin project. The follow-up
pass reported `admin-e2e:responsive-qa` 8/8 and admin visual regression 4/4. The page composer
itself is still verified statically only (§F.8).
