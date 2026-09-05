# 10 — Placeholder Design System (Steps 1–29) & Step 30 Handover

> **Purpose of this document is to prevent design work from happening early.**
> Everything here is deliberately plain. It exists so that layout, behaviour, accessibility and
> data can be built and tested without anyone making a brand decision, and so that the real
> visual design at **Step 30** is a token swap rather than a rewrite.

---

## 1. Rules until Step 30

1. **Use only the tokens below.** No new colours, fonts, shadows, gradients, illustrations or
   animations may be introduced before Step 30.
2. **Never hard-code a colour, font, radius or spacing value** in a component. Every value
   comes from a CSS custom property. This is what makes Step 30 (and white-labelling) cheap.
3. **No brand assets.** The logo slot renders a text wordmark read from store settings. No
   marketing imagery — use neutral grey placeholder boxes with correct aspect ratios so layout
   and CLS behave exactly as they will with real images.
4. **Structure and semantics are not "styling" and are always in scope**: correct headings,
   landmarks, focus order, labels, contrast ≥ 4.5:1, touch targets, responsive layout. These
   are built correctly from day one and are *not* deferred.
5. **No visual-regression tests before Step 30** — baselines would be thrown away.
6. If something looks unfinished, that is intended. Do not "just tidy it up".

---

## 2. Placeholder Tokens

Defined once on `:root`, overridable per tenant at runtime.

```css
:root {
  /* ---- Colour: neutral greys + one functional blue. Deliberately unbranded ---- */
  --color-bg:              #ffffff;
  --color-surface:         #f7f7f8;
  --color-surface-raised:  #ffffff;
  --color-border:          #d9d9de;
  --color-border-strong:   #b0b0b8;

  --color-text:            #1a1a1e;
  --color-text-muted:      #5c5c66;
  --color-text-inverse:    #ffffff;

  --color-primary:         #2f5bd7;   /* placeholder only */
  --color-primary-hover:   #2447ab;
  --color-primary-subtle:  #eaf0ff;
  --color-on-primary:      #ffffff;

  --color-success:         #1f7a44;
  --color-warning:         #9a6400;
  --color-danger:          #b3261e;
  --color-info:            #17607d;
  --color-focus-ring:      #2f5bd7;

  /* ---- Typography: system stack, zero webfont cost, decided at Step 30 ---- */
  --font-sans: system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans",
               "Helvetica Neue", Arial, sans-serif;
  --font-mono: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;

  --text-xs:   0.75rem;   --text-sm:  0.875rem;  --text-base: 1rem;
  --text-lg:   1.125rem;  --text-xl:  1.25rem;   --text-2xl:  1.5rem;
  --text-3xl:  1.875rem;  --text-4xl: 2.25rem;

  --leading-tight: 1.25;  --leading-normal: 1.5;  --leading-relaxed: 1.7;
  --weight-regular: 400;  --weight-medium: 500;   --weight-bold: 700;

  /* ---- Spacing: 4px base ---- */
  --space-0: 0;      --space-1: 0.25rem; --space-2: 0.5rem;  --space-3: 0.75rem;
  --space-4: 1rem;   --space-5: 1.25rem; --space-6: 1.5rem;  --space-8: 2rem;
  --space-10: 2.5rem;--space-12: 3rem;   --space-16: 4rem;   --space-20: 5rem;

  /* ---- Radius / elevation: minimal on purpose ---- */
  --radius-sm: 2px;  --radius-md: 4px;  --radius-lg: 8px;  --radius-full: 9999px;
  --shadow-sm: 0 1px 2px rgba(0,0,0,.06);
  --shadow-md: 0 2px 8px rgba(0,0,0,.08);
  --shadow-lg: 0 8px 24px rgba(0,0,0,.10);

  /* ---- Layout ---- */
  --container-max: 1280px;
  --header-height: 56px;          /* mobile */
  --bottom-bar-height: 60px;
  --z-header: 100; --z-drawer: 200; --z-modal: 300; --z-toast: 400;

  /* ---- Motion: minimal, and always respects prefers-reduced-motion ---- */
  --duration-fast: 120ms; --duration-base: 200ms;
  --ease-standard: cubic-bezier(.2,0,0,1);
}

@media (prefers-reduced-motion: reduce) {
  :root { --duration-fast: 0ms; --duration-base: 0ms; }
}
```

**Breakpoints** (mobile-first, `min-width` only):
`sm 480px` · `md 768px` · `lg 1024px` · `xl 1280px` · `2xl 1536px`.

---

## 3. Placeholder Component Conventions

| Element | Placeholder treatment |
|---|---|
| Buttons | Solid `--color-primary` (primary), 1px border (secondary), text-only (tertiary). One size on mobile: 44px tall, full-width for primary actions |
| Inputs | 1px `--color-border`, `--radius-md`, 44px tall, visible label above (never placeholder-as-label), error text below in `--color-danger` |
| Cards | `--color-surface-raised`, 1px border, `--radius-md`, `--shadow-sm`. No image treatments |
| Product image | Grey box at a fixed 1:1 aspect ratio with the SKU as centred text |
| Logo | Text wordmark from store settings in `--text-xl`, `--weight-bold` |
| Icons | Simple monochrome outline SVGs, `currentColor`, 20/24px |
| Charts | Default library styling, single-hue, no custom theming |
| Empty/error states | Plain text + a single action button. No illustrations |
| Loading | Grey skeleton blocks matching the final content geometry |
| Dark mode | **Not implemented before Step 30.** The token structure supports it; the decision is the client's |

---

## 4. Accessibility Baseline (in scope from day one)

- Contrast ≥ 4.5:1 for text, ≥ 3:1 for UI boundaries — the placeholder palette already satisfies
  this, and must be **re-verified after Step 30 theming**.
- Visible focus ring on every interactive element (`--color-focus-ring`, 2px offset outline);
  focus is never removed without an equivalent replacement.
- Full keyboard operability; logical tab order; focus trapped in dialogs and returned on close.
- Semantic HTML and landmarks (`header`, `nav`, `main`, `footer`, `search`).
- Every input has a programmatically associated label; errors linked via `aria-describedby`.
- Live regions for cart updates, toasts and async results.
- Touch targets ≥ 44 × 44 px with ≥ 8 px separation.
- `prefers-reduced-motion` honoured everywhere.

---

## 5. Step 30 — What actually happens

**Inputs required from the client** (requested at Step 0, delivered before Step 30):
brand identity or a mandate to create one, logo files (SVG preferred), colour direction,
typography preference or licences, photography/imagery style and assets, tone of voice for
UI copy, any competitor or reference sites, and a dark-mode decision.

**Work at Step 30**
1. Brand discovery workshop; moodboard and two or three visual directions on real screens
   (home, PLP, PDP, checkout) rather than abstract mockups.
2. Client picks a direction; tokens are finalised — colour ramps (with semantic roles),
   type scale and font pairing, spacing/radius/elevation, iconography, imagery rules, motion.
3. `10-design-system-placeholder.md` is superseded by `10-design-system.md`.
4. Components are restyled **by changing token values only**; component markup changes only
   where the design genuinely requires new structure (and each such change is listed explicitly).
5. Storefront and admin are themed consistently; email and PDF templates follow.
6. Accessibility contrast re-verified; visual-regression baselines captured for the first time.
7. Assets produced: favicon set, app icons, PWA manifest, Open Graph images, 404/500 art.
8. Responsive visual QA across the device matrix.

**Exit criteria**
- Client signs off the visual design **on the working application**, not on static mockups.
- Contrast and a11y checks pass post-theming.
- **White-label proof:** a second, visually distinct demo theme is applied end-to-end purely by
  swapping the tenant's token values and assets — no code change, no rebuild. This is the
  formal test that the redistribution requirement has been met.

---

## 6. Theming Mechanism (built now, exercised at Step 30)

- All tokens are CSS custom properties on `:root`, emitted from the tenant's theme
  configuration at runtime (a small `<style>` block in the SSR document head plus the runtime
  `config.json`), **not** compiled into the CSS bundle.
- Tenant branding assets (logo, favicon, OG image, hero imagery) live in object storage and are
  referenced by URL from store settings.
- Consequence: one Docker image serves every tenant; a rebrand is a database and asset change.
- Guardrail: a lint rule fails the build on any hard-coded hex colour, `px` font size, or raw
  spacing value inside component styles.
