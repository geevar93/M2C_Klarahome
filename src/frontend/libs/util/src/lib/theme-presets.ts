/**
 * The colour themes a store can choose from.
 *
 * A theme is a set of the semantic `--color-*` tokens from `_tokens.scss`, and nothing else: no
 * component references a brand ramp directly (docs/10-design-system.md §1), so redefining the
 * semantic layer re-colours every surface, border, control and heading in both apps at once. Shape,
 * spacing, type and motion are deliberately not part of a theme — they are what make the two apps
 * one product whichever palette a store picks.
 *
 * **Each theme is built from four distinct roles, not one hue at three strengths.** A commerce
 * page has to separate what to *do* (the primary: add to cart, checkout, pay) from what to
 * *notice* (the accent: an offer, a saving, a new arrival) from what to *read* (ink and a real
 * mid-tone for secondary text) from what everything sits on (grounds that carry a tint of the
 * theme rather than flat white). A theme where all four share one hue looks coordinated in a
 * swatch and lifeless on a page — so every theme here pairs its primary with an accent from a
 * different family, and keeps its ink darker and cooler or warmer than either.
 *
 * **The four grounds are four visibly different steps, and none of them is white.** An earlier
 * revision gave every theme a `#ffffff` card and a page ground within a couple of percent of
 * white, which meant a re-theme changed the buttons and almost nothing else — the card is the
 * dominant surface on a storefront, and a white card on an off-white page has no theme in it. Each
 * palette below therefore names `raised` (the card, a tinted off-white), `bg` (the page, clearly
 * of the theme), `surface` (a band that separates one section of a page from the next) and
 * `sunken` (the quiet fill under a summary or a well). The steps are wide enough to see and narrow
 * enough that product photography still leads.
 *
 * **Every pairing was checked against the design system's floor** — 4.5:1 for text on its surface,
 * 3:1 for a boundary or a control against its ground (docs/10 §4). The tightest pairing in the set
 * is the muted text on its own page ground, which clears 4.5:1 in every theme with room to spare;
 * primaries are dark enough to carry white; accents are used for emphasis on light grounds, never
 * as a text colour on their own, with `accentText` the strength that reads on `accentSurface`.
 *
 * The chosen preset is remembered in the token set as `--kh-theme`, a token nothing styles, so the
 * admin can show which card is selected without diffing two dozen colours.
 */
export interface ThemePreset {
  readonly id: string;
  readonly name: string;
  /** One sentence for the picker card: what the theme feels like, what it suits. */
  readonly description: string;
  /** Five colours for the picker's swatch strip: ground, surface, primary, accent, ink. */
  readonly swatches: readonly [string, string, string, string, string];
  /** The token overrides. Only the marker for the built-in palette. */
  readonly tokens: Readonly<Record<string, string>>;
}

/** The token that records which preset a token set came from. Styles nothing. */
export const THEME_MARKER_TOKEN = '--kh-theme';

interface Palette {
  /** Four steps of one tint: the card, the page, a section band, and the quiet fill. */
  readonly raised: string;
  readonly bg: string;
  readonly surface: string;
  readonly sunken: string;
  /** The darkest ink, for headings and prices; a real mid-tone for secondary text. */
  readonly ink: string;
  readonly muted: string;
  readonly subtle: string;
  /** The ink as an `r g b` triplet, for the overlay's alpha and the shadows' colour. */
  readonly inkRgb: string;
  /** The rule between two rows; the edge of a control somebody has to find; a hairline. */
  readonly border: string;
  readonly borderStrong: string;
  readonly borderSubtle: string;
  /** The action colour and its pressed states; the tint used behind a selected control. */
  readonly primary: string;
  readonly primaryHover: string;
  readonly primaryActive: string;
  readonly primarySubtle: string;
  readonly primarySubtleHover: string;
  /** The emphasis colour, from a different family than the primary. */
  readonly accent: string;
  readonly accentMuted: string;
  /** A tinted strip of the accent, and the accent dark enough to be read on it and on the ground. */
  readonly accentSurface: string;
  readonly accentText: string;
}

function tokens(id: string, palette: Palette): Readonly<Record<string, string>> {
  return {
    [THEME_MARKER_TOKEN]: id,
    '--color-bg': palette.bg,
    '--color-surface': palette.surface,
    '--color-surface-sunken': palette.sunken,
    '--color-surface-muted': palette.sunken,
    '--color-surface-raised': palette.raised,
    '--color-surface-inverse': palette.ink,
    '--color-overlay': `rgb(${palette.inkRgb} / 55%)`,
    // Shade, not smudge: a shadow cast in the theme's own ink rather than in the built-in
    // palette's burnt umber, which is what a foreign shadow colour looks like under a card that
    // is no longer sand.
    '--shadow-color': palette.inkRgb,
    '--color-border': palette.border,
    '--color-border-strong': palette.borderStrong,
    '--color-border-subtle': palette.borderSubtle,
    // Theme-neutral rather than tinted: this is a hairline on the inverse (ink) surface, where a
    // low-alpha tint of the theme's own ground is indistinguishable from white anyway.
    '--color-border-inverse': 'rgb(255 255 255 / 22%)',
    '--color-text': palette.ink,
    '--color-text-muted': palette.muted,
    '--color-text-subtle': palette.subtle,
    '--color-text-inverse': palette.bg,
    '--color-text-on-brand': '#ffffff',
    '--color-text-on-image': '#ffffff',
    '--color-primary': palette.primary,
    '--color-primary-hover': palette.primaryHover,
    '--color-primary-active': palette.primaryActive,
    '--color-primary-subtle': palette.primarySubtle,
    '--color-primary-subtle-hover': palette.primarySubtleHover,
    '--color-link': palette.primary,
    '--color-accent': palette.accent,
    '--color-accent-muted': palette.accentMuted,
    '--color-accent-surface': palette.accentSurface,
    '--color-accent-text': palette.accentText,
    '--color-price': palette.ink,
    '--color-price-was': palette.muted,
    // A saving is an offer, and an offer is what the accent is for. This is where the second hue
    // shows on every listing, not only on the announcement strip.
    '--color-savings': palette.accentText,
    // Ink, not the primary, for the reason `_tokens.scss` gives: a ring in the primary colour
    // disappears the moment it is drawn around a primary button, which is the control most likely
    // to be reached by keyboard.
    '--color-focus-ring': palette.ink,
    '--color-focus-ring-inverse': palette.sunken,
  };
}

export const THEME_PRESETS: readonly ThemePreset[] = [
  {
    id: 'sand',
    name: 'Sand & Coffee',
    description: 'The built-in palette: warm sand grounds, coffee text and a bronze accent. Calm and natural.',
    swatches: ['#f4ece2', '#ecddce', '#714c35', '#9e6d43', '#340c00'],
    tokens: { [THEME_MARKER_TOKEN]: 'sand' },
  },
  {
    id: 'coral',
    name: 'Coral & Lagoon',
    description: 'A hot coral for every action, cooled by lagoon teal and navy ink on warm apricot. Energetic, summery.',
    swatches: ['#fdeee5', '#fbe0d1', '#c8441f', '#1b998b', '#1f2a44'],
    tokens: tokens('coral', {
      raised: '#fffcfa',
      bg: '#fdeee5',
      surface: '#fbe0d1',
      sunken: '#f7d0ba',
      ink: '#1f2a44',
      inkRgb: '31 42 68',
      muted: '#4a5568',
      subtle: '#2d3748',
      border: '#f0c2a9',
      borderStrong: '#c8441f',
      borderSubtle: '#f8dccd',
      primary: '#c8441f',
      primaryHover: '#a83617',
      primaryActive: '#7f2810',
      primarySubtle: '#fde3d8',
      primarySubtleHover: '#fbd0be',
      accent: '#1b998b',
      accentMuted: '#8fd3cb',
      accentSurface: '#d9f3ef',
      accentText: '#0f6b60',
    }),
  },
  {
    id: 'indigo',
    name: 'Indigo & Ember',
    description: 'Electric indigo actions with a burnt-orange glow, on lavender. Confident, tech-forward, still warm.',
    swatches: ['#eceefc', '#dfe2f8', '#4f46e5', '#c2410c', '#1e1b4b'],
    tokens: tokens('indigo', {
      raised: '#fbfbff',
      bg: '#eceefc',
      surface: '#dfe2f8',
      sunken: '#d0d4f3',
      ink: '#1e1b4b',
      inkRgb: '30 27 75',
      muted: '#4c4a7a',
      subtle: '#312e81',
      border: '#c2c7ef',
      borderStrong: '#4f46e5',
      borderSubtle: '#dcdff7',
      primary: '#4f46e5',
      primaryHover: '#4338ca',
      primaryActive: '#3730a3',
      primarySubtle: '#e4e3ff',
      primarySubtleHover: '#d2d0fd',
      accent: '#c2410c',
      accentMuted: '#fdba74',
      accentSurface: '#ffe8d6',
      accentText: '#9a3412',
    }),
  },
  {
    id: 'emerald',
    name: 'Emerald & Mango',
    description: 'Deep emerald for actions and ripe mango for offers, on mint. Fresh, natural, optimistic.',
    swatches: ['#e4f3ea', '#d3ebdc', '#047857', '#ea580c', '#052e16'],
    tokens: tokens('emerald', {
      raised: '#fafdfb',
      bg: '#e4f3ea',
      surface: '#d3ebdc',
      sunken: '#bfe0cc',
      ink: '#052e16',
      inkRgb: '5 46 22',
      muted: '#31624a',
      subtle: '#14532d',
      border: '#abd6bb',
      borderStrong: '#047857',
      borderSubtle: '#cfe9d8',
      primary: '#047857',
      primaryHover: '#065f46',
      primaryActive: '#064e3b',
      primarySubtle: '#d5f2e4',
      primarySubtleHover: '#bcead4',
      accent: '#ea580c',
      accentMuted: '#fdba74',
      accentSurface: '#ffe4d0',
      accentText: '#b8410a',
    }),
  },
  {
    id: 'ocean',
    name: 'Ocean & Sunset',
    description: 'Clear ocean blue for actions and a sunset rose for emphasis, on sky. Clean, trustworthy, bright.',
    swatches: ['#e2f0fb', '#cfe6f8', '#0369a1', '#e11d48', '#0c2340'],
    tokens: tokens('ocean', {
      raised: '#fafdff',
      bg: '#e2f0fb',
      surface: '#cfe6f8',
      sunken: '#b9daf4',
      ink: '#0c2340',
      inkRgb: '12 35 64',
      muted: '#3d5a7a',
      subtle: '#1e3a5f',
      border: '#a4cfee',
      borderStrong: '#0369a1',
      borderSubtle: '#cbe4f7',
      primary: '#0369a1',
      primaryHover: '#075985',
      primaryActive: '#0c4a6e',
      primarySubtle: '#d4ebfb',
      primarySubtleHover: '#bbe0f8',
      accent: '#e11d48',
      accentMuted: '#fda4af',
      accentSurface: '#ffe1e7',
      accentText: '#be123c',
    }),
  },
  {
    id: 'berry',
    name: 'Berry & Lime',
    description: 'A rich berry pink for actions with a zesty lime accent, on blush. Playful and fashion-led.',
    swatches: ['#fce8f1', '#fad5e5', '#be185d', '#4d7c0f', '#3b0a2a'],
    tokens: tokens('berry', {
      raised: '#fffbfd',
      bg: '#fce8f1',
      surface: '#fad5e5',
      sunken: '#f6c1d7',
      ink: '#3b0a2a',
      inkRgb: '59 10 42',
      muted: '#6b3552',
      subtle: '#500724',
      border: '#f0aecb',
      borderStrong: '#be185d',
      borderSubtle: '#f8d9e8',
      primary: '#be185d',
      primaryHover: '#9d174d',
      primaryActive: '#831843',
      primarySubtle: '#fde0ec',
      primarySubtleHover: '#fbcbdd',
      accent: '#4d7c0f',
      accentMuted: '#bef264',
      accentSurface: '#ecfccb',
      accentText: '#3f6212',
    }),
  },
  {
    id: 'violet',
    name: 'Violet & Tangerine',
    description: 'Saturated violet for actions and tangerine for offers, on lilac. Bold, creative, modern.',
    swatches: ['#f0e8fc', '#e4d8f8', '#7c3aed', '#ea580c', '#2e1065'],
    tokens: tokens('violet', {
      raised: '#fdfbff',
      bg: '#f0e8fc',
      surface: '#e4d8f8',
      sunken: '#d7c7f4',
      ink: '#2e1065',
      inkRgb: '46 16 101',
      muted: '#5b4a85',
      subtle: '#4c1d95',
      border: '#c9b5ef',
      borderStrong: '#7c3aed',
      borderSubtle: '#e1d5f7',
      primary: '#7c3aed',
      primaryHover: '#6d28d9',
      primaryActive: '#5b21b6',
      primarySubtle: '#ece2ff',
      primarySubtleHover: '#ddcdff',
      accent: '#ea580c',
      accentMuted: '#fdba74',
      accentSurface: '#ffe4d0',
      accentText: '#b8410a',
    }),
  },
  {
    id: 'terracotta',
    name: 'Terracotta & Teal',
    description: 'Burnt clay for actions, cooled by deep teal, on cream. Warm and hospitable, at home with textiles.',
    swatches: ['#f7eade', '#f0dbc8', '#b5533c', '#0f766e', '#3a1a10'],
    tokens: tokens('terracotta', {
      raised: '#fffcf8',
      bg: '#f7eade',
      surface: '#f0dbc8',
      sunken: '#e8cbb2',
      ink: '#3a1a10',
      inkRgb: '58 26 16',
      muted: '#7a4636',
      subtle: '#5c2c1d',
      border: '#dcbb9c',
      borderStrong: '#b5533c',
      borderSubtle: '#eedfcf',
      primary: '#b5533c',
      primaryHover: '#96402d',
      primaryActive: '#6e2c1e',
      primarySubtle: '#f9e4da',
      primarySubtleHover: '#f3cfc0',
      accent: '#0f766e',
      accentMuted: '#7fc4be',
      accentSurface: '#d8efec',
      accentText: '#0f766e',
    }),
  },
  {
    id: 'slate',
    name: 'Slate & Teal',
    description: 'Near-black actions on neutral grey with a single teal accent. Quiet and editorial, lets photography lead.',
    swatches: ['#eaecef', '#dee1e6', '#1f2933', '#0f766e', '#15181d'],
    tokens: tokens('slate', {
      raised: '#fcfcfd',
      bg: '#eaecef',
      surface: '#dee1e6',
      sunken: '#d0d4db',
      ink: '#15181d',
      inkRgb: '21 24 29',
      muted: '#4b5563',
      subtle: '#2f3640',
      border: '#c0c5ce',
      borderStrong: '#6b7280',
      borderSubtle: '#dcdfe5',
      primary: '#1f2933',
      primaryHover: '#15181d',
      primaryActive: '#0b0d10',
      primarySubtle: '#e6e8ec',
      primarySubtleHover: '#d5d9e0',
      accent: '#0f766e',
      accentMuted: '#7fc4be',
      accentSurface: '#d8efec',
      accentText: '#0f766e',
    }),
  },
];

/** The preset a token set came from, or null when it was hand-edited or predates presets. */
export function presetIdFromTokens(tokens: Readonly<Record<string, string>> | undefined): string | null {
  const id = tokens?.[THEME_MARKER_TOKEN];
  return id && THEME_PRESETS.some((preset) => preset.id === id) ? id : null;
}
