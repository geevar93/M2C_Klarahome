import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { CartActions } from '@klarahome/data-access-cart';
import {
  ProductSearchParams,
  ProductSearchResponse,
  ProductSearchService,
} from '@klarahome/data-access-catalog';
import { WishlistStore } from '@klarahome/data-access-engagement';
import { Button, Drawer, EmptyState } from '@klarahome/ui-primitives';
import {
  FacetToggle,
  FilterPanel,
  ListingToolbar,
  ProductCardView,
  ProductGrid,
} from '@klarahome/ui-patterns';
import { AnalyticsEvents, AnalyticsService, LiveAnnouncer, SeoService, ToastService } from '@klarahome/util';

import { CatalogMapper } from '../../core/catalog.mapper';
import {
  SORT_OPTIONS,
  appliedFilters,
  applyFacet,
  clearFilters,
  fromQueryParams,
  toFacetGroups,
  toQueryParams,
} from '../../core/listing-query';

/** How many products a page of results holds. */
const PAGE_SIZE = 24;

/**
 * The faceted product listing — the body of the category page and the search results page.
 *
 * One component for both, because they are one page: the same endpoint, the same facets, the same
 * paging, differing only in whether the narrowing comes from a category in the path or a query in
 * the query string. Two implementations would have drifted by the second sprint.
 *
 * **The URL is the state** (`core/listing-query.ts`). Every filter change is a `router.navigate`
 * with `replaceUrl`, so the back button leaves the listing rather than walking back through twelve
 * filter states, and a shared link opens the same results. The component holds exactly one thing
 * the URL does not: the pages loaded after the first, because paging is keyset and a cursor in a
 * link would open somebody else's page four.
 *
 * On a phone the filters are a bottom sheet; from `lg` they are a sidebar. Both host the same
 * `FilterPanel`, and the sheet is only in the DOM when it is open — a closed drawer full of
 * focusable chips is a keyboard trap and forty rows of noise in the SSR HTML.
 */
@Component({
  selector: 'kh-product-listing',
  imports: [Button, Drawer, EmptyState, FilterPanel, ListingToolbar, ProductGrid],
  template: `
    <kh-listing-toolbar
      [total]="total()"
      [loading]="loading()"
      [sort]="params().sort || 'relevance'"
      [sortOptions]="sortOptions"
      [applied]="applied()"
      (filtersOpened)="filtersOpen.set(true)"
      (sorted)="sort($event)"
      (removed)="toggleFacet({ key: $event.key, value: $event.value, selected: false })"
    />

    <div class="layout">
      <aside class="sidebar">
        <kh-filter-panel
          [groups]="facets()"
          [applied]="applied()"
          (toggled)="toggleFacet($event)"
          (cleared)="clearAll()"
        />
      </aside>

      <div class="results">
        @if (!loading() && products().length === 0) {
          <kh-empty-state
            [heading]="emptyHeading()"
            message="Try removing a filter, checking the spelling, or searching for something broader."
          >
            @if (applied().length > 0) {
              <button khButton variant="secondary" type="button" (click)="clearAll()">
                Clear all filters
              </button>
            }
          </kh-empty-state>
        } @else {
          <kh-product-grid
            [products]="products()"
            [loading]="loading()"
            [loadingMore]="loadingMore()"
            [wishlistedIds]="wishlist.variantIds()"
            [showAddToCart]="true"
            [prioritiseFirst]="true"
            (opened)="openProduct($event)"
            (added)="addToCart($event)"
            (wishlistToggled)="toggleWishlist($event)"
          />

          @if (nextCursor()) {
            <!-- A button, not a scroll observer. Infinite scroll on its own makes the footer
                 unreachable and gives a keyboard user no way to ask for more; this is the control
                 that always works, and Step 29 may add viewport prefetching above it. -->
            <div class="more">
              <button
                khButton
                variant="secondary"
                type="button"
                [disabled]="loadingMore()"
                (click)="loadMore()"
              >
                {{ loadingMore() ? 'Loading…' : 'Show more products' }}
              </button>
            </div>
          }
        }
      </div>
    </div>

    <!-- The mobile filter sheet. Rendered only while open. -->
    <kh-drawer side="bottom" label="Filters" [open]="filtersOpen()" (closed)="filtersOpen.set(false)">
      <div class="sheet">
        <kh-filter-panel
          [groups]="facets()"
          [applied]="applied()"
          (toggled)="toggleFacet($event)"
          (cleared)="clearAll()"
        />
        <button khButton variant="primary" [block]="true" type="button" (click)="filtersOpen.set(false)">
          Show {{ total() }} products
        </button>
      </div>
    </kh-drawer>
  `,
  styles: `
    :host {
      display: block;
    }

    .layout {
      display: grid;
      gap: var(--space-6);
    }

    .sidebar {
      display: none;
    }

    .more {
      display: flex;
      justify-content: center;
      padding-block: var(--space-6);
    }

    .sheet {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
      padding: var(--space-4);
      padding-block-end: max(var(--space-4), env(safe-area-inset-bottom));
    }

    /* From 'lg' the sheet's contents become a permanent sidebar. 1024px is the \`lg\` breakpoint. */
    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: 16rem 1fr;
        align-items: start;
      }

      .sidebar {
        display: block;
        position: sticky;
        inset-block-start: var(--space-4);
        max-block-size: calc(100vh - var(--space-8));
        overflow-y: auto;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductListing {
  /** Narrows the listing to one category. The category page passes its resolved id. */
  readonly categoryId = input<string | null>(null);
  /**
   * Narrows the listing to one seller — a vendor storefront.
   *
   * Merged into the parameters rather than written into the URL: the seller is *which page this
   * is*, carried by the path, and a `?vendor=` beside `/vendor/acme` would be the same fact
   * spelled twice and two URLs for one page.
   */
  readonly vendorId = input<string | null>(null);
  /** What the empty state says. "No products in this category" reads differently from a search. */
  readonly emptyHeading = input('No products matched');
  /** Whether to publish `ItemList` structured data. True for a category, false for a search. */
  readonly publishItemList = input(false);

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly search = inject(ProductSearchService);
  private readonly cart = inject(CartActions);
  private readonly toasts = inject(ToastService);
  private readonly analytics = inject(AnalyticsService);
  private readonly announcer = inject(LiveAnnouncer);
  private readonly seo = inject(SeoService);
  private readonly mapper = inject(CatalogMapper);

  protected readonly wishlist = inject(WishlistStore);
  protected readonly sortOptions = SORT_OPTIONS;
  protected readonly filtersOpen = signal(false);

  private readonly response = signal<ProductSearchResponse | null>(null);
  private readonly extraPages = signal<readonly ProductSearchResponse[]>([]);
  private readonly currentParams = signal<ProductSearchParams>({});

  protected readonly loading = signal(false);
  protected readonly loadingMore = signal(false);

  protected readonly params = this.currentParams.asReadonly();
  protected readonly total = computed(() => this.response()?.total ?? 0);
  protected readonly nextCursor = computed(
    () => this.extraPages().at(-1)?.nextCursor ?? this.response()?.nextCursor ?? null,
  );

  /** The first page and every page loaded after it, in the order the API returned them. */
  protected readonly products = computed<readonly ProductCardView[]>(() => {
    const pages = [this.response(), ...this.extraPages()].filter((page) => page !== null);
    return pages.flatMap((page) => page.items.map((item) => this.mapper.searchItemToCard(item)));
  });

  protected readonly facets = computed(() => toFacetGroups(this.response()?.facets ?? [], this.params()));
  protected readonly applied = computed(() => appliedFilters(this.params(), this.facets()));

  constructor() {
    this.wishlist.loadOnce();

    // A page's structured data must not outlive the page. Leaving an `ItemList` behind means a
    // crawler reading a product page that claims to be a list of forty other products.
    inject(DestroyRef).onDestroy(() => this.seo.clearJsonLd('itemlist'));

    // Subscribed rather than run in an `effect`, and in the constructor rather than `ngOnInit`:
    // `queryParams` replays synchronously, so the first request is in flight before the server
    // renders — which is what makes the first page of a listing part of the SSR document rather
    // than something the browser fetches after hydrating.
    this.route.queryParams.pipe(takeUntilDestroyed()).subscribe((query) => {
      const params = fromQueryParams(query, this.categoryId());
      const vendor = this.vendorId();
      this.currentParams.set(vendor ? { ...params, vendor: [vendor] } : params);
      this.load();
    });
  }

  /** Loads the first page for the current parameters, discarding anything already paged in. */
  private load(): void {
    this.loading.set(true);
    this.extraPages.set([]);

    this.search.search({ ...this.params(), size: PAGE_SIZE }).subscribe((response) => {
      this.response.set(response);
      this.loading.set(false);
      this.announceResults(response);
      this.reportImpression(response);
    });
  }

  protected loadMore(): void {
    const cursor = this.nextCursor();
    if (!cursor || this.loadingMore()) return;

    this.loadingMore.set(true);
    this.search.search({ ...this.params(), size: PAGE_SIZE, cursor }).subscribe((response) => {
      this.extraPages.update((pages) => [...pages, response]);
      this.loadingMore.set(false);
    });
  }

  protected toggleFacet(toggle: FacetToggle): void {
    const value = this.response()
      ?.facets.find((facet) => facet.key === toggle.key)
      ?.values.find((entry) => entry.value === toggle.value);

    this.navigate(applyFacet(this.params(), toggle.key, toggle.value, toggle.selected, value ?? undefined));
  }

  protected sort(value: string): void {
    this.navigate({ ...this.params(), sort: value });
  }

  protected clearAll(): void {
    this.navigate(clearFilters(this.params()));
    this.filtersOpen.set(false);
  }

  /**
   * Writes the new state into the URL, which is what triggers the reload.
   *
   * `replaceUrl` on purpose: twelve filter taps must not become twelve history entries a shopper
   * has to press back through to leave the page. The trade is that undoing one filter is done by
   * untapping it rather than by going back, which is what the applied-filter chips are for.
   */
  private navigate(params: ProductSearchParams): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: toQueryParams(params),
      replaceUrl: true,
    });
  }

  protected openProduct(product: ProductCardView): void {
    const position = this.products().indexOf(product) + 1;
    this.search.reportClick(this.response()?.queryToken ?? null, position, product.variantId);
    this.analytics.track(AnalyticsEvents.selectItem, {
      item_id: product.variantId,
      item_name: product.name,
      index: position,
    });
  }

  protected addToCart(product: ProductCardView): void {
    if (!product.listingId) return;

    this.cart.add(product.listingId, 1).subscribe({
      next: () => {
        this.toasts.success(`${product.name} was added to your cart.`);
        this.analytics.track(AnalyticsEvents.addToCart, {
          item_id: product.variantId,
          item_name: product.name,
          price: product.price.amount,
          quantity: 1,
        });
      },
      error: (error: unknown) => this.toasts.warning(this.cart.describeFailure(error)),
    });
  }

  protected toggleWishlist(product: ProductCardView): void {
    const wasSaved = this.wishlist.isSaved(product.variantId);

    if (!this.wishlist.toggle(product.variantId)) {
      this.toasts.info('Sign in to save products to your wishlist.');
      return;
    }

    if (!wasSaved) {
      this.analytics.track(AnalyticsEvents.addToWishlist, {
        item_id: product.variantId,
        item_name: product.name,
      });
    }
  }

  /**
   * Tells a screen-reader user what changed.
   *
   * The toolbar's count is a live region, so the number announces itself; this adds the one thing
   * the number does not carry — that a spelling was corrected, which is otherwise visible only in
   * the results themselves.
   */
  private announceResults(response: ProductSearchResponse): void {
    if (response.corrected && response.query) {
      this.announcer.announce(`Showing results for ${response.query}.`);
    }
  }

  /**
   * `view_item_list`, and the `ItemList` structured data on a category page.
   *
   * Not on a search page: a search result is `noindex` (its URL is a query a bot invented), and
   * publishing an item list on a page that must not be indexed is markup for nobody.
   */
  private reportImpression(response: ProductSearchResponse): void {
    this.analytics.track(AnalyticsEvents.viewItemList, {
      item_list_name: this.emptyHeading(),
      items: response.items.slice(0, 10).map((item, index) => ({
        item_id: item.variantId,
        item_name: item.name,
        index: index + 1,
        price: item.price,
      })),
    });

    if (!this.publishItemList()) {
      this.seo.clearJsonLd('itemlist');
      return;
    }

    this.seo.setJsonLd('itemlist', {
      '@context': 'https://schema.org',
      '@type': 'ItemList',
      numberOfItems: response.items.length,
      itemListElement: response.items.map((item, index) => ({
        '@type': 'ListItem',
        position: index + 1,
        url: this.seo.absolute(`/p/${item.slug}`),
        name: item.name,
      })),
    });
  }
}
