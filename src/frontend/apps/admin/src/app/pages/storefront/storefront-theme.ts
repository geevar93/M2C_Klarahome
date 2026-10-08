import { THEME_MARKER_TOKEN, THEME_PRESETS } from '@klarahome/util';

/**
 * The storefront's own look, as a set of inline custom properties for one wrapper element.
 *
 * **Why this exists.** The back office re-maps the shared semantic tokens on `:root`
 * (`_admin-theme.scss`: navy, terracotta, its own radii, shadows and a sans "display" face). A
 * storefront block drawn inside the admin would therefore wear the *admin's* palette - which is
 * exactly what the designer must not show. The canvas sets every token the admin overrides back to
 * the storefront's value on its own wrapper, so the cascade does the scoping: nothing outside the
 * wrapper changes, and the admin's skin is never touched.
 *
 * `sand` is the built-in palette and carries only the marker in `THEME_PRESETS` (the compiled
 * `_tokens.scss` defaults *are* sand on the storefront). The admin has no such defaults to fall
 * back to, so {@link SAND_TOKENS} restates them here from `libs/ui/primitives/src/styles/_tokens.scss`.
 * Every other preset is laid over that base, so a preset that omits a token inherits the
 * storefront's value rather than the admin's.
 */

/** The storefront's `_tokens.scss` colour, shape and type defaults (the "sand" palette). */
export const SAND_TOKENS: Readonly<Record<string, string>> = {
  [THEME_MARKER_TOKEN]: 'sand',
  '--color-surface-raised': '#fffcf8',
  '--color-bg': '#f4ece2',
  '--color-surface': '#ecddce',
  '--color-surface-sunken': '#e0ccb6',
  '--color-surface-muted': '#e0ccb6',
  '--color-surface-inverse': '#340c00',
  '--color-overlay': 'rgb(52 12 0 / 55%)',
  '--color-border': '#ddc2a6',
  '--color-border-strong': '#9e6d43',
  '--color-border-subtle': '#ecddce',
  '--color-border-inverse': 'rgb(255 255 255 / 22%)',
  '--color-text': '#340c00',
  '--color-text-muted': '#714c35',
  '--color-text-subtle': '#47351f',
  '--color-text-inverse': '#faf6f1',
  '--color-text-on-brand': '#ffffff',
  '--color-text-on-image': '#faf6f1',
  '--color-primary': '#714c35',
  '--color-primary-hover': '#47351f',
  '--color-primary-active': '#340c00',
  '--color-primary-subtle': '#f4ece2',
  '--color-primary-subtle-hover': '#ecddce',
  '--color-on-primary': '#ffffff',
  '--color-link': '#714c35',
  '--color-accent': '#9e6d43',
  '--color-accent-muted': '#cda887',
  '--color-accent-surface': '#ecddce',
  '--color-accent-text': '#714c35',
  '--color-price': '#340c00',
  '--color-price-was': '#714c35',
  '--color-savings': '#2f6b45',
  '--color-focus-ring': '#340c00',
  '--color-focus-ring-inverse': '#ecddce',

  // Type: the admin sets its display face to the system sans; the storefront's is Fraunces, which
  // the shared primitives already load for the admin bundle. Typography never changes per palette.
  '--font-display': "'Fraunces', 'Fraunces Fallback', 'Iowan Old Style', 'Palatino Linotype', Georgia, serif",
  '--weight-display': '600',
  '--tracking-display': '-0.02em',

  // Shape and depth: the admin tightens the radii and softens the shadows.
  '--radius-sm': '4px',
  '--radius-md': '8px',
  '--radius-lg': '14px',
  '--radius-xl': '20px',
  '--shadow-color': '52 12 0',
  '--shadow-sm': '0 1px 2px rgb(var(--shadow-color) / 6%)',
  '--shadow-md': '0 2px 4px rgb(var(--shadow-color) / 5%), 0 6px 16px rgb(var(--shadow-color) / 7%)',
  '--shadow-lg': '0 4px 8px rgb(var(--shadow-color) / 5%), 0 16px 40px rgb(var(--shadow-color) / 10%)',

  // Frame: the admin makes its header taller than the shop's.
  '--header-height': '56px',
  '--bottom-bar-height': '60px',

  // Status colours (the storefront's, not the admin's).
  '--color-success': '#2f6b45',
  '--color-success-subtle': '#e8f0e9',
  '--color-success-text': '#1f4d31',
  '--color-warning': '#8a5512',
  '--color-warning-subtle': '#f9eddb',
  '--color-warning-surface': '#f2dcb8',
  '--color-warning-text': '#734511',
  '--color-danger': '#a3281c',
  '--color-danger-subtle': '#fbe9e6',
  '--color-danger-text': '#8f2318',
  '--color-info': '#38585c',
  '--color-info-subtle': '#e9eeef',
  '--color-info-text': '#2f4a4d',

  // Hooks that exist only in the admin skin. `initial` is the guaranteed-invalid value, so every
  // `var(--badge-x, fallback)` in the shared primitives takes its own fallback again - the
  // storefront's behaviour - rather than the admin's soft-fill badges.
  '--shadow-card': 'initial',
  '--badge-radius': 'initial',
  '--badge-border-width': 'initial',
  '--badge-shadow': 'initial',
  '--badge-bg': 'initial',
  '--badge-text-primary': 'initial',
  '--badge-text-success': 'initial',
  '--badge-text-warning': 'initial',
  '--badge-text-danger': 'initial',
  '--badge-text-info': 'initial',
};

/** The preset ids the designer can show, in the order `THEME_PRESETS` lists them. */
export function presetIds(): readonly string[] {
  return THEME_PRESETS.map((preset) => preset.id);
}

/**
 * The custom properties for a canvas wrapper wearing this preset.
 *
 * An unknown or missing id is sand, because that is what a storefront with no theme chosen shows.
 * The preset's own tokens win over the base, and the marker is always the resolved preset's id.
 */
export function canvasTokens(presetId: string | null | undefined): Readonly<Record<string, string>> {
  const preset = THEME_PRESETS.find((candidate) => candidate.id === presetId);
  if (!preset || preset.id === 'sand') return SAND_TOKENS;
  return { ...SAND_TOKENS, ...preset.tokens };
}
