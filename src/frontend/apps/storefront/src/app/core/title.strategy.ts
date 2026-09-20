import { Injectable, Injector, inject } from '@angular/core';
import { ActivatedRouteSnapshot, RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { SeoService } from '@klarahome/util';

/**
 * The browser tab's title, owned by the router rather than by `app.ts`.
 *
 * Previously `App` read `route.data['seo']?.title` on `ResolveEnd` and handed it to
 * `SeoService.apply`, which also set the description, canonical and Open Graph tags from the same
 * object. That worked, but it meant the *document title* — a router-level concern Angular has a
 * dedicated extension point for — was decided by application code reacting to a router event
 * rather than by the router itself. This class is that extension point: `Router` calls
 * `updateTitle` once activation has picked the route tree, and nothing in `app.ts` sets a title
 * from route data any more.
 *
 * A page that knows better than its route's static title — a product's name, a CMS page's
 * heading — still wins: `SeoService.apply()` is called from the page's own constructor. With
 * resolvers that constructor runs *before* this strategy, so the strategy asks `SeoService` whether
 * the page has already named itself and stands down if it has.
 */
@Injectable({ providedIn: 'root' })
export class AppTitleStrategy extends TitleStrategy {
  // Resolved lazily: `SeoService` listens to the `Router`, and the `Router` owns this strategy, so
  // asking for the service while the router is still being built is a cycle (NG0200).
  private readonly injector = inject(Injector);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    // The router runs this after `NavigationEnd`, by which time a page constructed during
    // activation may already have named itself. The static title is the fallback, not the last word.
    const seo = this.injector.get(SeoService);
    if (seo.pageOwnsCurrentTitle()) return;
    seo.setRouteTitle(this.buildTitle(snapshot) ?? null);
  }

  /**
   * Walks the activated route tree for the deepest declared title.
   *
   * `data.seo?.title` wins over the plainer `data.title` — most routes carry the former, set
   * alongside the rest of their static SEO metadata, and the latter exists for anything that
   * genuinely has nothing else to say about itself.
   */
  override buildTitle(snapshot: RouterStateSnapshot): string | undefined {
    let route: ActivatedRouteSnapshot | undefined = snapshot.root;
    let title: string | undefined;

    while (route) {
      const seo = route.data['seo'] as { title?: string } | undefined;
      title = seo?.title ?? (route.data['title'] as string | undefined) ?? title;
      route = route.firstChild ?? undefined;
    }

    return title;
  }
}
