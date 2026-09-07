import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, Signal, computed, inject, signal } from '@angular/core';

/**
 * The breakpoint names, and the widths behind them.
 *
 * The same numbers as `_breakpoints.scss`, restated here because a media query cannot read a
 * custom property and TypeScript cannot read a SCSS map — so one of the two has to be a copy, and
 * both are `min-width` only (docs/05-frontend-architecture.md §3.3).
 *
 * **CSS is the first answer to a layout question, and this service is the second.** Reach for it
 * only when the difference is structural — a drawer on mobile and a sidebar from `lg`, where the
 * two are genuinely different components rather than the same one styled twice.
 */
export const BREAKPOINTS = {
  sm: 480,
  md: 768,
  lg: 1024,
  xl: 1280,
  '2xl': 1536,
} as const;

export type BreakpointName = keyof typeof BREAKPOINTS;

@Injectable({ providedIn: 'root' })
export class Breakpoints {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly document = inject(DOCUMENT);
  private readonly matches = new Map<BreakpointName, Signal<boolean>>();

  /**
   * Whether the viewport is at least this wide.
   *
   * **False during server rendering**, always. The build order is 360 × 640 first, so the
   * narrowest layout is the one the server sends; a client that turns out to be wider adds to it
   * on hydration. Guessing "probably desktop" on the server is how a hydration mismatch and a
   * visible reflow happen on the phone that mattered most.
   */
  isAtLeast(name: BreakpointName): Signal<boolean> {
    const existing = this.matches.get(name);
    if (existing) return existing;

    const state = signal(false);
    if (this.isBrowser) {
      const view = this.document.defaultView;
      const query = view?.matchMedia(`(min-width: ${BREAKPOINTS[name]}px)`);
      if (query) {
        state.set(query.matches);
        query.addEventListener('change', (event) => state.set(event.matches));
      }
    }

    const readonlySignal = state.asReadonly();
    this.matches.set(name, readonlySignal);
    return readonlySignal;
  }

  /** The inverse, for the mobile-only half of a structural split. */
  isBelow(name: BreakpointName): Signal<boolean> {
    const atLeast = this.isAtLeast(name);
    return computed(() => !atLeast());
  }
}
