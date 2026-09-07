import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { ActivatedRouteSnapshot, NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

/** One step of the trail. The last one is the current page and carries no link. */
export interface Breadcrumb {
  readonly label: string;
  /** Absolute path. Absent on the current page. */
  readonly path?: string;
}

/**
 * What a route declares about its place in the trail: either a fixed label, or a function of the
 * snapshot — which is how a resolved product's name becomes a crumb without the page having to
 * push it anywhere.
 */
export type BreadcrumbDefinition = string | ((snapshot: ActivatedRouteSnapshot) => string);

/** The route-data key. `data: { breadcrumb: 'Your orders' }`. */
export const BREADCRUMB_DATA_KEY = 'breadcrumb';

/**
 * The breadcrumb trail, derived from the routes rather than assembled by each page.
 *
 * A page that builds its own trail is a page that will disagree with the router the first time a
 * route moves, and the trail is not decoration: it is a navigational landmark, it is what a
 * mobile user reaching a PDP from a search result uses to go up a level rather than back, and it
 * is `BreadcrumbList` structured data — a wrong trail is a wrong rich result.
 *
 * The label may still be learned late (a product's name arrives with the product), which is what
 * `setLeafLabel` is for; it survives until the next navigation and no longer.
 */
@Injectable({ providedIn: 'root' })
export class BreadcrumbTrail {
  private readonly router = inject(Router);
  private readonly derived = signal<readonly Breadcrumb[]>([]);
  private readonly leafOverride = signal<string | null>(null);
  private readonly ancestors = signal<readonly Breadcrumb[]>([]);

  /** The trail for the current route, `Home` first. Empty on the home page itself. */
  readonly items: Signal<readonly Breadcrumb[]> = computed(() => {
    const trail = this.derived();
    if (trail.length === 0) return trail;

    const override = this.leafOverride();
    const labelled = override
      ? trail.map((crumb, index) => (index === trail.length - 1 ? { ...crumb, label: override } : crumb))
      : trail;

    const ancestors = this.ancestors();
    if (ancestors.length === 0) return labelled;

    // Between `Home` and the page's own crumb: `Home / Furniture / Seating / Armchairs`.
    return [labelled[0], ...ancestors, ...labelled.slice(1)];
  });

  constructor() {
    this.rebuild();
    // The shell lives as long as the application, so this subscription is never torn down.
    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)).subscribe(() => {
      this.leafOverride.set(null);
      this.ancestors.set([]);
      this.rebuild();
    });
  }

  /**
   * Replaces the label of the current page's crumb — for a title that is only known once the
   * route's data has loaded. Cleared automatically on the next navigation.
   */
  setLeafLabel(label: string): void {
    this.leafOverride.set(label.trim() || null);
  }

  /**
   * Inserts crumbs between `Home` and the current page's own.
   *
   * The router knows a product is at `/p/:slug`; it does not know the product is in Furniture,
   * then Seating, then Armchairs. That path is data — a category tree the page has loaded — and it
   * is what a shopper who arrived from a search result uses to go *up* rather than back. Cleared
   * on the next navigation, exactly like `setLeafLabel`, so a trail never outlives its page.
   */
  setAncestors(crumbs: readonly Breadcrumb[]): void {
    this.ancestors.set([...crumbs]);
  }

  private rebuild(): void {
    const crumbs: Breadcrumb[] = [];
    let path = '';

    for (let route = this.router.routerState.snapshot.root.firstChild; route; route = route.firstChild) {
      const segment = route.url.map((part) => part.path).join('/');
      if (segment) path += `/${segment}`;

      const definition = route.data[BREADCRUMB_DATA_KEY] as BreadcrumbDefinition | undefined;
      if (!definition) continue;

      const label = typeof definition === 'function' ? definition(route) : definition;
      if (label) crumbs.push({ label, path });
    }

    // The last crumb is where the user already is; a link to the current page is noise for a
    // sighted user and a wasted tab stop for everyone else.
    if (crumbs.length > 0) {
      crumbs[crumbs.length - 1] = { label: crumbs[crumbs.length - 1].label };
      crumbs.unshift({ label: 'Home', path: '/' });
    }

    this.derived.set(crumbs);
  }
}
