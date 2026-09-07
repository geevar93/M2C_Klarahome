import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CartActions } from '@klarahome/data-access-cart';
import { WishlistStore } from '@klarahome/data-access-engagement';
import { Button, EmptyState, Skeleton } from '@klarahome/ui-primitives';
import { ProductCardView, ProductGrid } from '@klarahome/ui-patterns';
import { INR, money } from '@klarahome/domain';
import { AnalyticsEvents, AnalyticsService, ImageUrls, ToastService } from '@klarahome/util';

/**
 * Your wishlist — `/account/wishlist`.
 *
 * The same `ProductGrid` the category and search pages use, and that is the point: a saved product
 * should look and behave exactly as it does everywhere else, including its price, its rating and its
 * add-to-cart. A bespoke "wishlist row" would be a second product card to keep in step with the
 * first.
 *
 * **An unavailable item stays on the list**, marked unavailable rather than quietly dropped. Wanting
 * something that is out of stock is the ordinary case for a wishlist, and removing it would delete
 * the customer's own note to themselves.
 *
 * The heart on each card removes it, through the same optimistic store the PDP uses — so a tap here
 * and a tap there behave identically and cannot get out of step.
 */
@Component({
  selector: 'kh-account-wishlist-page',
  imports: [Button, EmptyState, ProductGrid, RouterLink, Skeleton],
  template: `
    <h1>Your wishlist</h1>

    @if (!store.current()) {
      <kh-skeleton height="12rem" />
    } @else if (products().length === 0) {
      <kh-empty-state
        heading="Nothing saved yet"
        message="Tap the heart on anything you want to come back to. It stays on every device you sign in on."
      >
        <a khButton variant="primary" routerLink="/">Find something</a>
      </kh-empty-state>
    } @else {
      <kh-product-grid
        [products]="products()"
        [showAddToCart]="true"
        [wishlistedIds]="store.variantIds()"
        (added)="addToCart($event)"
        (wishlistToggled)="remove($event)"
      />
    }
  `,
  styles: `
    :host {
      display: block;
    }

    h1 {
      font-size: var(--text-2xl);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountWishlistPage {
  protected readonly store = inject(WishlistStore);
  private readonly cart = inject(CartActions);
  private readonly images = inject(ImageUrls);
  private readonly toasts = inject(ToastService);
  private readonly analytics = inject(AnalyticsService);

  /**
   * The saved items, as product cards.
   *
   * Mapped here rather than in `CommerceMapper` because a wishlist item is a catalogue shape, not a
   * commerce one — it is the same `ProductCardView` the grids draw, assembled from the fields the
   * wishlist projection happens to carry.
   */
  protected readonly products = computed<readonly ProductCardView[]>(() =>
    (this.store.current()?.items ?? []).map((item) => ({
      variantId: item.variantId,
      productId: item.productId,
      listingId: item.listingId,
      name: item.name ?? 'Saved item',
      href: item.slug ? `/p/${item.slug}` : '/',
      brand: null,
      price: money(item.price ?? 0, item.currencyCode || INR),
      mrp:
        item.mrp !== null && item.price !== null && item.mrp > item.price
          ? money(item.mrp, item.currencyCode || INR)
          : null,
      // The projection carries whichever of the two it has — a resolved URL from the media module,
      // or a bare file id from the catalogue row. `sourceForImage` prefers the URL and falls back.
      image: this.images.sourceForImage({ url: item.imageUrl, fileId: item.imageFileId }, item.name ?? ''),
      rating: item.ratingAverage,
      ratingCount: item.ratingCount,
      isPurchasable: item.isPurchasable,
      reference: item.sku,
    })),
  );

  constructor() {
    this.store.load();
  }

  protected addToCart(product: ProductCardView): void {
    if (!product.listingId) return;

    this.cart.add(product.listingId).subscribe({
      next: () => {
        this.toasts.success(`${product.name} was added to your cart.`);
        this.analytics.track(AnalyticsEvents.addToCart, {
          item_id: product.variantId,
          item_name: product.name,
        });
      },
      error: (error: unknown) => this.toasts.warning(this.cart.describeFailure(error)),
    });
  }

  protected remove(product: ProductCardView): void {
    this.store.toggle(product.variantId);
  }
}
