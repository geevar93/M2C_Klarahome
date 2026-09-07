import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRouteSnapshot, NavigationEnd, ResolveEnd, Router, RouterOutlet } from '@angular/router';
import {
  AnnouncementBar,
  Breadcrumbs,
  MiniCart,
  MobileNavDrawer,
  OfflineNotice,
  SearchEntry,
  SiteFooter,
  SiteHeader,
  StickyActionBar,
  StickyActionBarService,
  SuggestionView,
} from '@klarahome/ui-patterns';
import { Container } from '@klarahome/ui-layout';
import { ProgressBar, ToastHost } from '@klarahome/ui-primitives';
import {
  AnalyticsService,
  BreadcrumbTrail,
  LoadingIndicator,
  RouteFocusManager,
  SeoMetadata,
  SeoService,
} from '@klarahome/util';
import { filter } from 'rxjs';

import { ShellStore } from './core/shell.store';

/**
 * The storefront shell.
 *
 * Everything that is on every page and outlives a navigation: the landmarks, the header and its
 * two drawers, the breadcrumb trail, the footer, the sticky action bar, the toast region and the
 * offline notice. The routed page is the only thing that changes.
 *
 * The router wiring is three separate concerns, deliberately hung on two different events:
 *
 *  - **`ResolveEnd`** applies the route's declared SEO. It fires *before* the page component is
 *    constructed, so a page with real data — a product, a CMS page — overwrites it and wins. The
 *    reverse order would have the route's generic title clobber the product's name.
 *  - **`NavigationEnd`** moves focus to the new page and publishes the breadcrumb trail as
 *    structured data. Both need the route to have actually activated.
 */
@Component({
  selector: 'kh-root',
  imports: [
    AnnouncementBar,
    Breadcrumbs,
    Container,
    MiniCart,
    MobileNavDrawer,
    OfflineNotice,
    ProgressBar,
    RouterOutlet,
    SearchEntry,
    SiteFooter,
    SiteHeader,
    StickyActionBar,
    ToastHost,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  private readonly router = inject(Router);
  private readonly focus = inject(RouteFocusManager);
  private readonly seo = inject(SeoService);
  private readonly analytics = inject(AnalyticsService);
  private readonly trail = inject(BreadcrumbTrail);

  protected readonly shell = inject(ShellStore);
  protected readonly loading = inject(LoadingIndicator).isLoading;
  protected readonly breadcrumbs = this.trail.items;
  protected readonly hasStickyAction = inject(StickyActionBarService).active;

  constructor() {
    this.shell.initialise();

    // `takeUntilDestroyed` is not needed on either: the shell lives as long as the application.
    this.router.events.pipe(filter((event) => event instanceof ResolveEnd)).subscribe((event) => {
      this.applyRouteSeo(event.state.root, event.urlAfterRedirects);
    });

    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)).subscribe((event) => {
      // A single-page app that navigates without moving focus leaves a keyboard user reading the
      // old page.
      this.focus.focusMainContent();
      this.publishBreadcrumbStructuredData();
      this.analytics.page(event.urlAfterRedirects);
      this.shell.closeNav();
      this.shell.closeCart();
    });
  }

  protected search(query: string): void {
    this.shell.clearSuggestions();
    void this.router.navigate(['/search'], { queryParams: { q: query } });
  }

  /**
   * A suggestion was taken.
   *
   * The suggestion already carries where it goes — a product goes to its PDP, a brand or a past
   * query goes to a search — so the shell navigates by URL rather than deciding again what each
   * kind means. `navigateByUrl` because the href includes a query string the mapper built.
   */
  protected openSuggestion(suggestion: SuggestionView): void {
    this.shell.clearSuggestions();
    void this.router.navigateByUrl(suggestion.href);
  }

  /**
   * Applies the deepest matched route's `data.seo`.
   *
   * The canonical path defaults to the URL without its query string, because a filtered or paged
   * variant of a listing is the same page for indexing purposes — the alternative is the same
   * catalogue indexed under a thousand facet combinations (§3.5).
   */
  private applyRouteSeo(root: ActivatedRouteSnapshot, url: string): void {
    let route: ActivatedRouteSnapshot | null = root;
    let metadata: SeoMetadata = {};

    // Merged down the tree so a parent can set what a whole section shares — `noIndex` on
    // `/account`, for instance — and a child can still say more.
    while (route) {
      metadata = { ...metadata, ...((route.data['seo'] as SeoMetadata | undefined) ?? {}) };
      route = route.firstChild;
    }

    this.seo.apply({ canonicalPath: url.split('?')[0], ...metadata });
  }

  private publishBreadcrumbStructuredData(): void {
    const items = this.breadcrumbs();
    if (items.length === 0) {
      this.seo.clearJsonLd('breadcrumbs');
      return;
    }

    this.seo.setJsonLd('breadcrumbs', {
      '@context': 'https://schema.org',
      '@type': 'BreadcrumbList',
      itemListElement: items.map((crumb, index) => ({
        '@type': 'ListItem',
        position: index + 1,
        name: crumb.label,
        // The last crumb has no path; a `ListItem` without an `item` is valid and is how the
        // current page is expressed.
        item: crumb.path ? this.seo.absolute(crumb.path) : undefined,
      })),
    });
  }
}
