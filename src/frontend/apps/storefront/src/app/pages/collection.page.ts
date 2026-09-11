import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { CartActions } from '@klarahome/data-access-cart';
import { StoreCollectionResponse, StoreContentService } from '@klarahome/data-access-content';
import { WishlistStore } from '@klarahome/data-access-engagement';
import { Button, EmptyState, ProductImage } from '@klarahome/ui-primitives';
import { ProductCardView, ProductGrid } from '@klarahome/ui-patterns';
import {
  AnalyticsEvents,
  AnalyticsService,
  BreadcrumbTrail,
  ImageUrls,
  SeoService,
  ToastService,
} from '@klarahome/util';
import { map } from 'rxjs';

import { CatalogMapper } from '../core/catalog.mapper';

/**
 * A curated collection — `/collections/:slug`.
 *
 * A collection is **merchandised, not searched**: its members are rows a rule materialised or a
 * merchandiser pinned (Step 20), in an order somebody chose. So it is read from the CMS rather
 * than from the search projection, and it deliberately has no facets — filtering a curated set
 * would undo the curation, which is the whole value of the page.
 *
 * What it keeps from the listing page is keyset paging, because a collection can be long, and the
 * same product grid, because a product card must look the same everywhere it appears.
 */
@Component({
  selector: 'kh-collection-page',
  imports: [Button, EmptyState, ProductGrid, ProductImage],
  template: `
    <header class="head">
      @if (heroImage()) {
        <kh-product-image [source]="heroImage()" [priority]="true" ratio="21 / 9" sizes="100vw" />
      }
      <h1>{{ collection().name }}</h1>
      @if (collection().description) {
        <p class="description">{{ collection().description }}</p>
      }
      <p class="count">
        {{ collection().itemCount }} {{ collection().itemCount === 1 ? 'product' : 'products' }}
      </p>
    </header>

    @if (products().length === 0) {
      <kh-empty-state
        heading="This collection is empty"
        message="Nothing has been added to it yet. Try the categories in the menu."
      />
    } @else {
      <kh-product-grid
        [products]="products()"
        [loadingMore]="loadingMore()"
        [wishlistedIds]="wishlist.variantIds()"
        [showAddToCart]="true"
        [prioritiseFirst]="!heroImage()"
        (opened)="trackOpen($event)"
        (added)="addToCart($event)"
        (wishlistToggled)="toggleWishlist($event)"
      />

      @if (nextCursor()) {
        <div class="more">
          <button khButton variant="secondary" type="button" [disabled]="loadingMore()" (click)="loadMore()">
            {{ loadingMore() ? 'Loading…' : 'Show more products' }}
          </button>
        </div>
      }
    }
  `,
  styles: `
    .head {
      padding-block: var(--space-4) var(--space-6);
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
    }

    h1 {
      margin: 0;
      font-size: var(--text-2xl);
    }

    .description {
      margin: 0;
      max-inline-size: var(--measure);
      color: var(--color-text-muted);
    }

    .count {
      margin: 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .more {
      display: flex;
      justify-content: center;
      padding-block: var(--space-6);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CollectionPage {
  private readonly route = inject(ActivatedRoute);
  private readonly content = inject(StoreContentService);
  private readonly cart = inject(CartActions);
  private readonly mapper = inject(CatalogMapper);
  private readonly images = inject(ImageUrls);
  private readonly seo = inject(SeoService);
  private readonly breadcrumbs = inject(BreadcrumbTrail);
  private readonly analytics = inject(AnalyticsService);
  private readonly toasts = inject(ToastService);

  protected readonly wishlist = inject(WishlistStore);

  protected readonly collection = toSignal(
    this.route.data.pipe(map((data) => data['collection'] as StoreCollectionResponse)),
    { requireSync: true },
  );

  private readonly extraPages = signal<readonly StoreCollectionResponse[]>([]);
  protected readonly loadingMore = signal(false);

  protected readonly heroImage = computed(() =>
    this.images.sourceForImage(this.collection().heroImage, this.collection().name),
  );

  protected readonly nextCursor = computed(
    () => this.extraPages().at(-1)?.nextCursor ?? this.collection().nextCursor,
  );

  protected readonly products = computed<readonly ProductCardView[]>(() =>
    [this.collection(), ...this.extraPages()].flatMap((page) =>
      page.products.map((product) => this.mapper.productCardToCard(product)),
    ),
  );

  constructor() {
    this.wishlist.loadOnce();

    this.route.data.pipe(takeUntilDestroyed()).subscribe((data) => {
      // A new slug reuses this component, so the paged-in tail of the previous collection has to
      // go with it.
      this.extraPages.set([]);
      this.apply(data['collection'] as StoreCollectionResponse);
    });
  }

  protected loadMore(): void {
    const cursor = this.nextCursor();
    if (!cursor || this.loadingMore()) return;

    this.loadingMore.set(true);
    this.content.collection(this.collection().slug, cursor).subscribe({
      next: (page) => {
        this.extraPages.update((pages) => [...pages, page]);
        this.loadingMore.set(false);
      },
      error: () => this.loadingMore.set(false),
    });
  }

  protected addToCart(product: ProductCardView): void {
    if (!product.listingId) return;

    this.cart.add(product.listingId, 1).subscribe({
      next: () => this.toasts.success(`${product.name} was added to your cart.`),
      error: (error: unknown) => this.toasts.warning(this.cart.describeFailure(error)),
    });
  }

  protected toggleWishlist(product: ProductCardView): void {
    if (!this.wishlist.toggle(product.variantId)) {
      this.toasts.info('Sign in to save products to your wishlist.');
    }
  }

  protected trackOpen(product: ProductCardView): void {
    this.analytics.track(AnalyticsEvents.selectItem, {
      item_id: product.variantId,
      item_name: product.name,
      item_list_name: this.collection().name,
    });
  }

  private apply(collection: StoreCollectionResponse): void {
    const path = `/collections/${collection.slug}`;

    this.seo.apply({
      title: collection.seo.metaTitle || collection.name,
      description: collection.seo.metaDescription || collection.description || '',
      canonicalPath: collection.seo.canonicalUrl || path,
      noIndex: collection.seo.noIndex,
      imageUrl: collection.seo.ogImage?.url ?? collection.heroImage?.url ?? undefined,
    });

    this.breadcrumbs.setLeafLabel(collection.name);
  }
}
