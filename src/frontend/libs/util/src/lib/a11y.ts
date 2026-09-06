import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject } from '@angular/core';

/**
 * Accessibility scaffolding shared by both apps.
 *
 * WCAG 2.2 AA is the target and it is in scope from day one, not from Step 30 — structure,
 * focus order and announcements are not "styling" (docs/10-design-system-placeholder.md §1.4).
 */

export type Politeness = 'polite' | 'assertive';

/**
 * Announces something to a screen reader that a sighted user learns from a visual change: a
 * cart total that moved, a filter that removed forty results, a toast.
 *
 * One live region per politeness, created once and reused. Creating a region and writing to it
 * in the same tick is the classic mistake — an assistive technology only announces changes to a
 * region it was already observing — so both regions exist from the first announcement onward and
 * the text is written on the next frame.
 */
@Injectable({ providedIn: 'root' })
export class LiveAnnouncer {
  private readonly document = inject(DOCUMENT);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly regions = new Map<Politeness, HTMLElement>();

  announce(message: string, politeness: Politeness = 'polite'): void {
    if (!this.isBrowser || !message) return;
    const region = this.regionFor(politeness);
    // Clearing first makes a repeated message announce again rather than read as no change.
    region.textContent = '';
    setTimeout(() => (region.textContent = message), 50);
  }

  private regionFor(politeness: Politeness): HTMLElement {
    const existing = this.regions.get(politeness);
    if (existing) return existing;

    const region = this.document.createElement('div');
    region.setAttribute('aria-live', politeness);
    region.setAttribute('aria-atomic', 'true');
    region.setAttribute('role', politeness === 'assertive' ? 'alert' : 'status');
    region.className = 'kh-visually-hidden';
    this.document.body.appendChild(region);
    this.regions.set(politeness, region);
    return region;
  }
}

/**
 * Where focus goes after a route change.
 *
 * A single-page app that navigates without moving focus leaves a keyboard or screen-reader user
 * on the link they just followed, reading the old page. Every route lands focus on the new page's
 * `<h1>` — or on `<main>` when a route has not got one yet — with `tabindex="-1"` so it can
 * receive focus without joining the tab order.
 */
@Injectable({ providedIn: 'root' })
export class RouteFocusManager {
  private readonly document = inject(DOCUMENT);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  focusMainContent(): void {
    if (!this.isBrowser) return;
    const target =
      this.document.querySelector<HTMLElement>('main h1') ?? this.document.querySelector<HTMLElement>('main');
    if (!target) return;
    if (!target.hasAttribute('tabindex')) target.setAttribute('tabindex', '-1');
    target.focus({ preventScroll: true });
  }
}

/**
 * Whether the viewer has asked for less motion.
 *
 * Read rather than assumed: the tokens already zero the durations under the media query, and
 * anything animated in TypeScript has to make the same check (`10-design-system-placeholder.md`).
 */
export function prefersReducedMotion(): boolean {
  return globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
}
