import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { StorePageResponse } from '@klarahome/data-access-content';
import { EmptyState } from '@klarahome/ui-primitives';
import { CmsBlockRenderer, ProductCarousel, ProductCardView } from '@klarahome/ui-patterns';
import { AnalyticsEvents, AnalyticsService, SeoService } from '@klarahome/util';
import { map } from 'rxjs';

import { CmsBlockMapper } from '../core/cms-block.mapper';
import { RecentlyViewedStore } from '../core/recently-viewed.store';

/**
 * The home page.
 *
 * There is no hard-coded home page in this storefront: it is a CMS document like any other, an
 * ordered list of typed blocks a merchandiser publishes (Step 20), and this component is the frame
 * around `CmsBlockRenderer`. That is what makes a seasonal front page a publish rather than a
 * deploy.
 *
 * The document is resolved, so the server renders the real blocks — including the hero, which is
 * the LCP element on the storefront's most visited URL. A home page that fetched its own content
 * would server-render an empty `<main>` and then paint, which is the difference between a 1.2 s
 * and a 3 s LCP on the connection this shop is built for.
 *
 * **A missing document is an empty shop, not a 404.** The header, footer and search still work,
 * so the resolver answers `null` and this says so plainly rather than sending a visitor to an
 * error page over a merchandising gap.
 */
@Component({
  selector: 'kh-home-page',
  imports: [CmsBlockRenderer, EmptyState, ProductCarousel],
  template: `
    @if (blocks().length > 0) {
      <kh-cms-block-renderer
        [blocks]="blocks()"
        [prioritiseFirst]="true"
        (productOpened)="trackOpen($event)"
      />
    } @else {
      <h1>{{ page()?.title || storeHeading }}</h1>
      <kh-empty-state
        heading="Nothing has been published to the home page yet"
        message="Browse the categories in the menu, or search for what you are looking for."
      />
    }

    @if (recentlyViewed().length > 0) {
      <kh-product-carousel heading="Recently viewed" [products]="recentlyViewed()" />
    }
  `,
  styles: `
    h1 {
      padding-block-start: var(--space-6);
      font-size: var(--text-2xl);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomePage {
  private readonly route = inject(ActivatedRoute);
  private readonly seo = inject(SeoService);
  private readonly mapper = inject(CmsBlockMapper);
  private readonly analytics = inject(AnalyticsService);
  private readonly recent = inject(RecentlyViewedStore);

  protected readonly storeHeading = 'Welcome';

  protected readonly page = toSignal(
    this.route.data.pipe(map((data) => data['page'] as StorePageResponse | null)),
    { requireSync: true },
  );

  protected readonly blocks = computed(() => this.mapper.toViews(this.page()?.blocks ?? []));

  /**
   * The trail from `localStorage`.
   *
   * Empty during server rendering — there is no storage on the server and no visitor to attribute
   * one to — so the rail appears on hydration. That is deliberate: a server-rendered document
   * carrying one visitor's history could not be cached at the edge (Step 32).
   */
  protected readonly recentlyViewed = computed(() => this.recent.items());

  constructor() {
    const page = this.page();
    this.seo.apply({
      title: page?.seo.metaTitle || page?.title || 'Home',
      description: page?.seo.metaDescription || page?.summary || '',
      canonicalPath: '/',
      noIndex: page?.seo.noIndex ?? false,
    });
  }

  protected trackOpen(product: ProductCardView): void {
    this.analytics.track(AnalyticsEvents.selectItem, {
      item_id: product.variantId,
      item_name: product.name,
      item_list_name: 'Home',
    });
  }
}
