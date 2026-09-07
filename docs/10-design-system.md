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

---

## 6. Theming mechanism

Unchanged, and still the point: every token is a CSS custom property on `:root`, emitted from the
tenant's theme configuration at runtime — a `<style>` block in the SSR document head plus the
runtime `config.json` — **not** compiled into the CSS bundle. One Docker image serves every tenant;
a rebrand is a database and asset change.

---

## 7. Outstanding

- **Dark mode: not implemented.** The token structure supports it — every colour has a semantic
  role and no component reads the ramp directly — but the client's decision was light only for
  now. Adding it means redefining the semantic block under `prefers-color-scheme` and re-running
  §1.2 against the dark grounds.
- **Brand assets.** The wordmark is still text (`.kh-wordmark`, in the display face) pending the
  client's SVG logo. Favicon set, app icons, PWA manifest and OG images follow from that.
- **Photography.** Products render through `.kh-placeholder-media`, now a warm tinted box at the
  correct aspect ratio rather than a grey one. Layout and CLS behave as they will with real images.
- **Visual-regression baselines.** Not yet captured. The placeholder document deferred them to
  after theming precisely so they would not be thrown away; they can now be taken.
- **The hard-coded-value lint rule** described in the placeholder's §6 was never implemented.
  There is no stylelint configuration in the workspace, so "never hard-code a colour" is currently
  a convention rather than a guardrail.
- **White-label proof.** The formal exit criterion — a second, visually distinct demo theme applied
  end-to-end purely by swapping token values — has not been exercised against this palette.
