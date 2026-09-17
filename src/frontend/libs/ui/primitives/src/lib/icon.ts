import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * The icon set, as path data.
 *
 * Inline SVG drawn from a registry, not an icon font and not one `<img>` per glyph: a font gives
 * a flash of unstyled text and reads as a letter to a screen reader if the CSS is late, and an
 * image is a request per icon on a connection where requests are the expensive part.
 *
 * A 24 × 24 box, stroked in `currentColor` so an icon inherits the colour of the control it sits
 * in — which is what keeps it correct after Step 30 changes every colour in the theme.
 */
const ICON_PATHS = {
  menu: ['M4 7h16', 'M4 12h16', 'M4 17h16'],
  close: ['M6 6l12 12', 'M18 6L6 18'],
  search: ['M17 11a6 6 0 1 1-12 0 6 6 0 0 1 12 0z', 'M15.5 15.5 20 20'],
  cart: [
    'M3 5h2l2.4 10.2a2 2 0 0 0 2 1.6h7.3a2 2 0 0 0 1.95-1.5L20.5 8H6',
    'M10 20a1 1 0 1 1-2 0 1 1 0 0 1 2 0z',
    'M19 20a1 1 0 1 1-2 0 1 1 0 0 1 2 0z',
  ],
  user: ['M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8z', 'M4 20c0-3.3 3.6-5 8-5s8 1.7 8 5'],
  heart: ['M12 20s-7-4.5-7-9a4 4 0 0 1 7-2.6A4 4 0 0 1 19 11c0 4.5-7 9-7 9z'],
  package: ['M3 7l9-4 9 4v10l-9 4-9-4V7z', 'M3 7l9 4 9-4', 'M12 11v10'],
  home: ['M4 11l8-7 8 7', 'M6 10v9h12v-9'],
  plus: ['M12 5v14', 'M5 12h14'],
  minus: ['M5 12h14'],
  filter: ['M4 6h16', 'M7 12h10', 'M10 18h4'],
  sort: ['M7 4v16', 'M4 8l3-4 3 4', 'M17 20V4', 'M14 16l3 4 3-4'],
  truck: [
    'M3 6h11v9H3z',
    'M14 9h4l3 3v3h-7z',
    'M8 18a1.5 1.5 0 1 1-3 0 1.5 1.5 0 0 1 3 0z',
    'M19 18a1.5 1.5 0 1 1-3 0 1.5 1.5 0 0 1 3 0z',
  ],
  'chevron-right': ['M9 5l7 7-7 7'],
  'chevron-left': ['M15 5l-7 7 7 7'],
  'chevron-down': ['M5 9l7 7 7-7'],
  'chevron-up': ['M5 15l7-7 7 7'],
  check: ['M4 12.5 9 18 20 6'],
  info: ['M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0z', 'M12 11v5', 'M12 8h.01'],
  alert: ['M12 4 2.5 20h19L12 4z', 'M12 10v4', 'M12 17h.01'],
  offline: ['M3 3l18 18', 'M5.5 12.5a10 10 0 0 1 4-2.7', 'M18.5 12.5a10 10 0 0 0-4.6-2.8', 'M12 19h.01'],
  // Added at Step 25 for the buying and account surfaces. Same 24x24 box, same stroke: an icon
  // set that grows by one glyph per screen ends up with three arrows that point the same way.
  download: ['M12 4v11', 'M8 12l4 4 4-4', 'M5 19h14'],
  wallet: ['M4 8a2 2 0 0 1 2-2h11v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8z', 'M17 10h4v4h-4z'],
  bell: ['M6 9a6 6 0 0 1 12 0c0 4 1.5 5.5 1.5 5.5h-15S6 13 6 9z', 'M10 18a2 2 0 0 0 4 0'],
  edit: ['M4 20h4L19 9a2.1 2.1 0 0 0-3-3L5 17v3z', 'M14.5 6.5l3 3'],
  trash: ['M4 7h16', 'M9 7V5h6v2', 'M6 7l1 13h10l1-13'],
  clock: ['M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0z', 'M12 7v5l3 2'],
  card: ['M3 7h18v10H3z', 'M3 11h18'],
  pin: ['M12 21s6-5.2 6-10a6 6 0 1 0-12 0c0 4.8 6 10 6 10z', 'M12 13a2 2 0 1 0 0-4 2 2 0 0 0 0 4z'],
  refresh: ['M20 12a8 8 0 1 1-2.4-5.7', 'M20 4v5h-5'],
  // The social marks, added at Step 31 for the footer's follow row. Drawn in the same 24x24
  // stroked box as everything else rather than pasted in as filled brand SVGs: a logo dropped into
  // this set would ignore `currentColor` and sit at a different weight beside the icons around it.
  // They are recognisable rather than exact, which is what a 20px footer icon can be.
  instagram: [
    'M7 3h10a4 4 0 0 1 4 4v10a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4V7a4 4 0 0 1 4-4z',
    'M16 12a4 4 0 1 1-4-4 4 4 0 0 1 4 4z',
    'M17.5 6.5h.01',
  ],
  facebook: ['M15 3h-2.5A4.5 4.5 0 0 0 8 7.5V10H5.5v4H8v7h4v-7h3l.5-4H12V8a1 1 0 0 1 1-1h2V3z'],
  // X's mark, as its one outline rather than two crossed strokes — stroked diagonals are the close
  // icon, which is what this looked like. The arms are uneven and taper, and only the real glyph
  // has that, so this one is filled (see FILLED_ICONS). Traced from the official mark and inset to
  // the same box as the rest of the set.
  x: [
    'M13.82 10.53L20.98 2.4h-1.7l-6.21 7.06L8.12 2.4H2.4l7.5 10.67L2.4 21.6h1.7l6.55-7.46 5.23 7.46h5.72l-7.78-11.07z',
  ],
  twitch: ['M4 3h16v11l-4 4h-3.5L9 21H7v-3H4V3z', 'M11 8v4', 'M15 8v4'],
  youtube: [
    'M3 8.5A3.5 3.5 0 0 1 6.5 5h11A3.5 3.5 0 0 1 21 8.5v7a3.5 3.5 0 0 1-3.5 3.5h-11A3.5 3.5 0 0 1 3 15.5v-7z',
    'M11 9.5l4 2.5-4 2.5v-5z',
  ],
} as const;

export type IconName = keyof typeof ICON_PATHS;

/**
 * The icons whose shape is the fill, not the stroke.
 *
 * The set is stroked, which is what keeps a cart and a chevron the same weight. A wordmark is the
 * exception: its strokes are uneven and tapered by design, and outlining one at 1.75px turns it
 * into a hollow smudge at 20 pixels. These are drawn as a filled path in `currentColor` instead,
 * so they still take the colour of the control they sit in.
 */
const FILLED_ICONS: ReadonlySet<string> = new Set<IconName>(['x']);

/** The names, for a caller that wants to validate one at runtime. */
export const ICON_NAMES = Object.keys(ICON_PATHS) as readonly IconName[];

@Component({
  selector: 'kh-icon',
  template: `
    <svg
      [attr.width]="pixels()"
      [attr.height]="pixels()"
      viewBox="0 0 24 24"
      [attr.fill]="filled() ? 'currentColor' : 'none'"
      [attr.stroke]="filled() ? 'none' : 'currentColor'"
      stroke-width="1.75"
      stroke-linecap="round"
      stroke-linejoin="round"
      focusable="false"
      aria-hidden="true"
    >
      @for (path of paths(); track path) {
        <path [attr.d]="path" />
      }
    </svg>
  `,
  styles: `
    :host {
      display: inline-flex;
      flex: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Icon {
  readonly name = input.required<IconName>();
  /** 20px inline with text, 24px in a control. The two sizes from the design placeholder §3. */
  readonly size = input<'sm' | 'md'>('md');

  protected readonly paths = computed(() => ICON_PATHS[this.name()] ?? []);
  protected readonly filled = computed(() => FILLED_ICONS.has(this.name()));
  protected readonly pixels = computed(() => (this.size() === 'sm' ? 20 : 24));
}
