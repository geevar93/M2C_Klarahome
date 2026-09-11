# 10 — Klara Home Design System

> Supersedes `10-design-system-placeholder.md`, which governed Steps 1–29 and deliberately
> prevented any brand decision from being made early. This document records the decisions that
> were made instead, and — more usefully — the ones that were **rejected**, and why.
>
> The placeholder's central claim was that Step 30 would be a *token swap rather than a rewrite*.
> That claim held: the colour, type, radius, elevation and motion values below all changed, and
> the only files touched to change them were `_tokens.scss`, one new `_typography.scss`, and a
> short list of structural additions recorded in §5.

---

## 1. The palette

The client supplied six colours. Two further tints were mixed from the lightest of them so that a
page has a ground, a surface raised off that ground, and a band to separate sections with — three
levels being the minimum a commerce layout needs.

| Token | Hex | Source | Role |
|---|---|---|---|
| `--brand-sand-050` | `#faf6f1` | mixed | The page ground |
| `--brand-sand-100` | `#f4ece2` | mixed | A surface raised off it |
| `--brand-sand-200` | `#ecddce` | **client** | Section bands, quiet fills |
| `--brand-tan-300` | `#ddc2a6` | mixed | Hairline rules |
| `--brand-tan-400` | `#cda887` | **client** | Decorative rules, disabled edges |
| `--brand-bronze-500` | `#9e6d43` | **client** | Boundaries, focus, display accents |
| `--brand-coffee-600` | `#714c35` | **client** | The action colour, interactive text |
| `--brand-olive-700` | `#47351f` | **client** | Hover and pressed states |
| `--brand-ink-900` | `#340c00` | **client** | Body text, focus ring, inverse surfaces |

Components never reference `--brand-*`. They reference the semantic role — `--color-primary`,
`--color-border-strong`, `--color-text-muted` — because the semantic layer is what a re-theme
changes, and a component that reached past it into the ramp would have skipped the decision that
matters.

### 1.1 Two assignments the contrast check rejected

The obvious mapping of this ramp does not survive WCAG 2.1, and both failures are the kind that
look fine on a designer's monitor and fail on a phone in daylight.

**Bronze is not a text colour.** `#9e6d43` is **4.45:1** on white and **3.35:1** on the sand band —
below the 4.5:1 floor in both cases. It was the natural pick for links and prices, and it is used
for neither. It carries boundaries, rules, icons at 24px and display type only.
`--brand-coffee-600` is the interactive-text colour instead, at **7.53:1** on white.

**Tan is not a UI boundary.** `#cda887` is **2.20:1** on white, below the 3:1 floor for the edge of
a control. So `--color-border-strong` — the edge of every input, select and secondary button — is
bronze at **4.45:1**. Tan remains as `--color-border`, which separates things that are already
obviously separate and is allowed to be quiet.

**The focus ring is ink, not the primary.** A ring in `--color-primary` vanishes the instant it is
drawn around a primary button, which is the control most likely to be reached by keyboard. Ink
clears 13:1 against every ground in the theme, and the 2px `outline-offset` keeps the ring on the
page rather than on the control it marks.

### 1.2 Verified ratios

Every pairing below was computed before it was written, not checked afterwards.

| Pairing | Ratio | Floor |
|---|---|---|
| Body text (ink) on the page ground | 16.28:1 | 4.5 |
| Muted text (coffee) on the page ground | 7.00:1 | 4.5 |
| Muted text (coffee) on the sand band | 5.67:1 | 4.5 |
| Subtle text (olive) on the page ground | 10.86:1 | 4.5 |
| White on the primary button (coffee) | 7.53:1 | 4.5 |
| White on the primary hover (olive) | 11.69:1 | 4.5 |
| Input border (bronze) on the page ground | 4.13:1 | 3.0 |
| Focus ring (ink) on the sand band | 13.18:1 | 3.0 |
| Sand text on the ink footer | 13.18:1 | 4.5 |
| Every status colour on both grounds | ≥ 5.76:1 | 4.5 |
| `--color-text-on-image` on the hero scrim, over a *worst-case pure-white* photograph | 9.8:1 | 4.5 (3.0 for the display headline) |

**The hero scrim is the one pairing on this list computed against an input the theme does not
control.** Every other row sits on a known token background; a CMS hero's background is whatever a
merchant uploads, from a black product shot to a blown-out white one. So its floor is checked
against the adversarial case rather than a typical one: `--color-surface-inverse` (ink-900, relative
luminance ≈ 0.0099) mixed at 92% over pure white (luminance 1.0), blended in linear light per the
WCAG formula itself, leaves a background luminance of ≈ 0.083 — `--color-text-on-image` (sand-050,
luminance ≈ 0.926) against that is 9.8:1, clearing both the subheadline's 4.5:1 and the display
headline's 3.0:1 with margin to spare for a photo that is merely very light rather than literally
blank. See `CmsBlockRenderer`'s `.scrim` rule for the mix and the gradient it sits inside.

Status colours are warm-shifted to sit in the same world as the brand, and each has a subtle
companion for alert backgrounds. `--color-info` is desaturated well past the usual notification
teal for the same reason: a saturated one is the only cool thing on a warm page and reads as a
foreign element rather than as information. Status is never signalled by colour alone: an alert carries an
icon and a role, and an invalid control carries `aria-invalid`.

---

## 2. Typography

**Fraunces for headings and the wordmark. The system sans for everything else.**

Fraunces is self-hosted, latin-subset, one weight (600), variable on the optical-size axis, and
**35 KB**. Four decisions are packed into that sentence:

- **Self-hosted, not `fonts.googleapis.com`.** A third-party font host is a second origin on the
  critical path, a CSP entry, and a stylesheet round trip that must complete before the browser
  even learns which font file it needs. It also fails in deployments where Google's CDN is
  blocked, which is not hypothetical for a store intended to sell into China.
- **One weight.** The display face sets headings and the wordmark and nothing else. Shipping 400
  as well would double the bytes to style text the system sans already sets better. Nothing may
  request a Fraunces weight other than 600 — a weight that was not shipped gets synthesised into a
  smeared fake bold.
- **Variable on optical size.** One file sets a 3rem hero and a 1.125rem card title with the
  contrast each size actually wants, rather than one drawing stretched across both.
- **The interface stays on the system stack.** This is the largest thing the theme does *not*
  spend against the 2.5s LCP budget (`09-nfr-testing-observability.md`): it renders on the first
  frame, and on a mid-range Android it is the face the OS already has hinted for that screen.

A metric-adjusted `Fraunces Fallback` alias sits second in the stack so that `font-display: swap`
repaints the heading without moving it. If the display face is ever changed, those overrides must
be re-derived or removed, not left to drift.

`h1`–`h3` take the display face. `h4`–`h6` are interface, not editorial — a card title, a form
section — and stay on the sans. Display sizes are fluid (`clamp`), which removes the breakpoints
that would otherwise exist only to change a font size.

---

## 3. Shape, elevation and motion

| | Placeholder | Now | Why |
|---|---|---|---|
| Radius | 2 / 4 / 8 | 4 / 8 / 14 / 20 | Most of what makes this read as "home goods" rather than "enterprise console". Not softer: a 24px radius on a product card starts cropping the photograph inside it. |
| Shadow | Neutral black | Cast in the ramp's ink | A neutral black shadow over a sand ground reads as grey dirt. Ink reads as shade. |
| Motion | 120 / 200ms | 120 / 200 / 320ms, plus `--ease-out` | Unchanged in principle; `prefers-reduced-motion` still zeroes every duration at the token. |

**Hover never moves anything.** Every interactive surface transitions `background-color`,
`border-color` and `box-shadow` only. A `transform: scale()` on a card shifts its neighbours in a
grid, and on a touch screen — where hover sticks after a tap — leaves the card enlarged until
something else is touched.

---

## 4. Accessibility baseline

Unchanged from the placeholder document, and re-verified after theming:

- Contrast ≥ 4.5:1 for text, ≥ 3:1 for UI boundaries. See §1.2.
- Visible focus ring on every interactive element, 2px with 2px offset; never removed without an
  equivalent replacement. Inverse surfaces override the ring colour rather than dropping it.
- Full keyboard operability; logical tab order; focus trapped in dialogs and returned on close.
- Semantic HTML and landmarks; every input has a programmatically associated label; errors linked
  via `aria-describedby`.
- Live regions for cart updates, toasts and async results.
- Touch targets ≥ 44 × 44 px with ≥ 8 px separation. The `sm` button variant reduces the type, not
  the hit area.
- `prefers-reduced-motion` honoured at the token, so no component needs its own media query.

---

## 5. Structural additions

The placeholder promised that component markup would change only where the design genuinely
required new structure, and that each such change would be listed explicitly. This is that list.

| Addition | Why it needed structure rather than a token |
|---|---|
| `.kh-card` / `.kh-card--interactive` | One surface treatment shared by the product card, the order summary and every admin panel, so they are recognisably the same object. |
| `.kh-section`, `.kh-band`, `.kh-band--sunken`, `.kh-band--inverse` | Page rhythm and tinted full-bleed bands. Previously every page picked its own margin. |
| `.kh-display`, `.kh-wordmark`, `.kh-eyebrow`, `.kh-prose`, `.kh-tabular` | The display face's allowed uses, as classes, so nothing applies it ad hoc. |
| `.kh-container--narrow` | A form or a policy page is one column. A 1280px-wide login form is not a login form anybody wants to fill in. |
| `kh-button--inverse` | The primary button is 3.2:1 against the ink band and would take its own label below the floor. An action on an inverse surface needs a different variant, not a different token. |
| `::selection`, `hr` | Browser defaults that read as bugs against a warm ground. |

### 5.1 A `richText` CMS block's three width tiers

Added in the 2026-09-11 follow-up review, `CmsBlockRenderer`'s `richText` block's `width` field
(`'narrow' | 'wide' | 'full'`) now does three different, deliberate things instead of one:

| `width` | Cap | Alignment |
|---|---|---|
| `narrow` | `var(--measure)` (68ch) — the reading measure `.kh-prose` and `khContainer`'s own narrow size share. | Centred (`margin-inline: auto`). |
| `wide` | `var(--container-wide)` (64rem / 1024px, `lg`) — a merchandising page with a wide photograph or a table that still should not run the full container width a listing page needs. | Centred. |
| `full` | None — the block fills `khContainer`, the same as every other CMS block type. | N/A. |

Both capped tiers are centred rather than left-hugging: a bare `max-inline-size` with no
`margin-inline` insets a block from the *right* edge only, so next to a full-width hero or banner
grid above and below it, the page's content would visibly jog left. Centring keeps a narrower block
symmetrical inside the same container every other block already fills.

---

## 6. Theming mechanism

Every token is a CSS custom property on `:root`, so nothing about `_tokens.scss` itself had to
change to be overridable at runtime. What was missing until the white-label proof (below) is
narrower than this section used to claim: `BrandingSettings` carried `PrimaryColor`/`AccentColor`,
but the storefront never read them anywhere — the compiled defaults were the only theme that ever
rendered. Closed by `BrandingSettings.ThemeTokens`, a validated dictionary of CSS custom-property
overrides keyed by the same names declared above, and a storefront `ThemeService`
(`libs/util/src/lib/theme.service.ts`) that applies them to `document.documentElement` during the
same SSR pass that sets the store name — so the "before" response a crawler receives already
carries the tenant's palette. One Docker image serves every tenant; a rebrand is now genuinely a
`platform.store_settings` change, not a rebuild. See the white-label proof note in
[`steps/step-30-design-system-theming-and-visual-identity.md`](steps/step-30-design-system-theming-and-visual-identity.md)
and `infra/scripts/verify-white-label-theming.sh`.

**Now also true of the admin app.** The admin has no SSR pass to piggy-back on — it is CSR-only
(no `server` build target: `docs/05-frontend-architecture.md` §4.1) — so it reuses the same
`StoreConfigService`/`ThemeService` pair from a `provideAppInitializer` in
`apps/admin/src/app/app.config.ts` instead: the tokens are applied before the router activates the
first route, rather than during a server render. `verify-white-label-theming.sh` proves this too,
but necessarily differently in kind — there is no rendered HTML response to grep, so it drives a
real headless browser (`src/frontend/scripts/check-admin-theme-tokens.mjs`) against the sign-in
screen and asserts the tokens land on `document.documentElement` after bootstrap. That check needs
the admin Docker image rebuilt once to carry this wiring — the same one-time rebuild the storefront
and API images needed when `ThemeService` itself was introduced.

---

## 7. Outstanding

- **Dark mode: not implemented.** The token structure supports it — every colour has a semantic
  role and no component reads the ramp directly — but the client's decision was light only for
  now. Adding it means redefining the semantic block under `prefers-color-scheme` and re-running
  §1.2 against the dark grounds.
- **Brand assets: done.** A hand-authored mark/wordmark SVG, favicon set (SVG + ICO + 16/32/48
  PNG), PWA icons (192/512, including maskable), `manifest.webmanifest`, a default OG image and
  404/500 art now live under `src/frontend/apps/storefront/public/brand/` (favicon also copied to
  `apps/admin/public/brand/`), built from this document's exact palette and Fraunces, with no new
  colours or typeface introduced. The wordmark *in the header* stays text on purpose — see the
  brand-assets note in
  [`steps/step-30-design-system-theming-and-visual-identity.md`](steps/step-30-design-system-theming-and-visual-identity.md)
  for why a tenant's store name can't be baked into an SVG without breaking §6's white-label
  mechanism, and for what was and wasn't rasterised and with what tool.
- **Photography.** Products render through `.kh-placeholder-media`, now a warm tinted box at the
  correct aspect ratio rather than a grey one. Layout and CLS behave as they will with real images.
- **Email and PDF template styling: done.** Transactional email is wrapped in a shared inline-CSS
  shell (`EmailLayout.Wrap()`, `KlaraHome.Modules.Notifications`) carrying the exact colour tokens
  from §1 and an email-safe fallback of `--font-display` (Fraunces itself does not travel with a
  mail message); the header mark is a CID-embedded PNG rather than a remote image, since there is
  no public origin to host one at yet. Generated PDFs (invoices, credit notes, commission invoices
  — all through the one shared `MigraDocRenderer`) use the same border/surface/text-muted tokens on
  rules, captions and table shading, deliberately without Fraunces or an accent colour on monetary
  figures — see the step-30 note and the remarks on `MigraDocRenderer` for why. Verified against a
  real send through the dev stack's Mailpit and a real rendered PDF, not simulated.
- **Visual-regression baselines.** Not yet captured. The placeholder document deferred them to
  after theming precisely so they would not be thrown away; they can now be taken.
- **The hard-coded-value lint rule: done, in two parts.** stylelint (`.stylelintrc.json`) covers
  the handful of real `.scss` files. Almost all component styling in this workspace, though, lives
  in the `styles:` template literal of an Angular `@Component` decorator, which stylelint's usual
  CSS-in-JS bridge (`postcss-styled-syntax`) does not reach — it parses tagged templates like
  `styled.div\`...\`` and does not recognise a plain `styles: \`...\`` property. Two eslint rules
  close that gap instead (`eslint.config.mjs`, both a plain AST walk over `styles:` template
  literals rather than a CSS parser): `local/no-hardcoded-color-in-styles` forbids a hex/`rgb()`/
  `hsl()` colour literal, and `local/no-hardcoded-spacing-in-styles` (added in the 2026-09-11
  follow-up review) forbids a raw `px`/`rem`/`em` literal on `font-size`, `padding`, `margin`,
  `gap`, `row-gap`, `column-gap` or `inset*` — `0` is always fine, and a hairline offset inside
  `calc()` alongside a token (`calc(var(--space-3) - 1px)`, `order-timeline.ts`) is allowed, since
  that pattern exists to shim a border's width off a token, not to skip the token — and forbids an
  `@media` width outside the six token breakpoints in `_breakpoints.scss`'s own unit
  (`docs/05-frontend-architecture.md` §3.3), always flagging a `max-width` query outright since
  that file is `min-width`-only by design. Both are AST-based rather than real CSS parsers, so a rare accepted exception is an
  `eslint-disable` block comment with a reason (`libs/ui/admin/src/lib/entity-picker.ts`'s
  `.option`'s 2px title/subtitle gap, below the smallest spacing token) rather than a weakened rule.
- **White-label proof: done for both the storefront and the admin app.** See §6 — the admin's
  proof is a headless-browser DOM check rather than a server-rendered-HTML check, because the admin
  is CSR-only.
