import { inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';

import { AdminNavHub, AdminNavItem, AdminNavSection } from './admin.model';

/**
 * Where the back office is right now, as a path with no query or fragment.
 *
 * Both halves of the navigation — the five doors and the row of tabs behind the open one — need
 * to know the same thing: which destination the current URL belongs to. Neither can use
 * `routerLinkActive` for it, because a door is active when *any* screen behind it is, and a
 * detail route (`/orders/123`) has no link of its own anywhere. So the membership test is written
 * once, here, and both read it.
 */
export function currentPath() {
  const router = inject(Router);
  const strip = (url: string) => url.split('?')[0].split('#')[0];

  return toSignal(
    router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => strip(event.urlAfterRedirects)),
    ),
    { initialValue: strip(router.url) },
  );
}

/** Whether a path is this item's screen or something beneath it. */
export function itemMatches(item: AdminNavItem, path: string): boolean {
  return path === item.path || path.startsWith(`${item.path}/`);
}

/** The section holding this path, or null. */
export function sectionFor(sections: readonly AdminNavSection[], path: string): AdminNavSection | null {
  return sections.find((section) => section.items.some((item) => itemMatches(item, path))) ?? null;
}

/**
 * The door the current path is behind, or null when no hub owns it.
 *
 * Membership first — a screen behind a door lights that door — and the hub's own landing path
 * second, which is how `/more` lights More although no item in it is `/more`.
 */
export function hubFor(hubs: readonly AdminNavHub[], path: string): AdminNavHub | null {
  return (
    hubs.find((hub) => sectionFor(hub.sections, path) !== null) ??
    hubs.find((hub) => path === hub.path || path.startsWith(`${hub.path}/`)) ??
    null
  );
}
