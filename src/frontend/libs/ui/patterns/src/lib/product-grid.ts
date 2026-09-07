import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { Grid } from '@klarahome/ui-layout';
import { Skeleton } from '@klarahome/ui-primitives';

import { ProductCardView } from './catalog.model';
import { ProductCard } from './product-card';

/**
 * A grid of product cards, and the two states that are not "here are the products": loading, and
 * loading more.
 *
 * The list is an `<ol>` because the order is the ranking — a search result in a different order is
 * a different answer — and because a screen reader then announces "list, 24 items", which is the
 * result count restated where a non-visual user is actually reading.
 *
 * Skeletons are rendered at the same card size as the real thing, so the grid does not resize when
 * results land (docs/05-frontend-architecture.md §3.6). While *more* results are loading the
 * existing ones stay put and the skeletons are appended — replacing them would throw away the
 * shopper's scroll position, which is the classic infinite-scroll bug.
 */
@Component({
  selector: 'kh-product-grid',
  imports: [Grid, ProductCard, Skeleton],
  template: `
    <kh-grid [minColumnWidth]="minColumnWidth()" [gap]="3">
      @if (loading() && products().length === 0) {
        @for (placeholder of skeletons(); track $index) {
          <div class="skeleton-card">
            <kh-skeleton height="10rem" radius="var(--radius-md)" />
            <kh-skeleton [lines]="2" height="0.75rem" />
            <kh-skeleton width="40%" height="1rem" />
          </div>
        }
      } @else {
        <ol class="list">
          @for (product of products(); track product.variantId) {
            <li>
              <kh-product-card
                [product]="product"
                [priority]="$first && prioritiseFirst()"
                [showWishlist]="showWishlist()"
                [showAddToCart]="showAddToCart()"
                [wishlisted]="wishlistedIds().includes(product.variantId)"
                [imageSizes]="imageSizes()"
                (opened)="opened.emit($event)"
                (added)="added.emit($event)"
                (wishlistToggled)="wishlistToggled.emit($event)"
              />
            </li>
          }
        </ol>

        @if (loadingMore()) {
          @for (placeholder of skeletons(); track $index) {
            <div class="skeleton-card">
              <kh-skeleton height="10rem" radius="var(--radius-md)" />
              <kh-skeleton [lines]="2" height="0.75rem" />
            </div>
          }
        }
      }
    </kh-grid>
  `,
  styles: `
    :host {
      display: block;
    }

    /* \`display: contents\` on both, so the cards are the grid's own children and the list is
       still an ordered list to a screen reader. A wrapper element per card would make the grid
       one column of lists instead. */
    .list,
    .list > li {
      display: contents;
    }

    .skeleton-card {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      padding: var(--space-2);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductGrid {
  readonly products = input.required<readonly ProductCardView[]>();
  readonly loading = input(false);
  /** A further page is in flight. The grid keeps what it has and appends placeholders. */
  readonly loadingMore = input(false);
  readonly skeletonCount = input(8);
  readonly minColumnWidth = input('10rem');
  readonly showWishlist = input(true);
  readonly showAddToCart = input(false);
  readonly wishlistedIds = input<readonly string[]>([]);
  readonly imageSizes = input('(min-width: 1024px) 20rem, (min-width: 768px) 33vw, 50vw');
  /**
   * Whether the first card carries the LCP image. True on a listing page, false inside a rail
   * further down a page — only one element on a page may claim it.
   */
  readonly prioritiseFirst = input(false);

  readonly opened = output<ProductCardView>();
  readonly added = output<ProductCardView>();
  readonly wishlistToggled = output<ProductCardView>();

  protected readonly skeletons = computed(() => Array.from({ length: this.skeletonCount() }));
}
