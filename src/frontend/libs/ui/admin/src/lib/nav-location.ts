import { inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';

import { AdminNavCategory, AdminNavItem, AdminNavSection } from './admin.model';

/**
 * Where the back office is right now, as a path with no query or fragment.
 *
 * Both halves of the navigation — the sidebar's categories and the tabs above the page — need to
 * know the same thing: which destination the current URL belongs to. Neither can use
 * `routerLinkActive` for it, because a category is active when *any* screen in it is, and a
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
 * The category the current path is in, or null (`/more`, `/403`, a page nothing lists).
 *
 * Membership is by screen, and a detail route belongs to its list's screen by prefix, so
 * `/orders/123` keeps Sell lit and keeps the Orders tab as the active one.
 */
export function categoryFor(categories: readonly AdminNavCategory[], path: string): AdminNavCategory | null {
  return categories.find((category) => sectionFor(category.sections, path) !== null) ?? null;
}
