import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Badge, Button, Icon, Price, ProductImage, Rating } from '@klarahome/ui-primitives';

import { ProductCardView } from './catalog.model';

/**
 * One product, in a grid or a rail.
 *
 * The card is a heading and a link, not a clickable `<div>`. The whole card is tappable — a 44px
 * link on a phone is not enough target for a product — but the *link* is the product name, and
 * the card's surface extends it with a stretched pseudo-element. That is what keeps the tab order
 * one stop per product, the accessible name the product's name, and the browser's "open in new
 * tab" working, all of which a click handler on a container loses.
 *
 * The two actions that sit on top of it — wishlist and add-to-cart — are real buttons above the
 * stretched link in the stacking order, so they are reachable by tap and by keyboard without the
 * card's link swallowing them.
 */
@Component({
  selector: 'kh-product-card',
  imports: [Badge, Button, Icon, Price, ProductImage, Rating, RouterLink],
  template: `
    <article class="card" [class.is-unavailable]="!product().isPurchasable">
      <div class="media">
        <kh-product-image
          [source]="product().image"
          [placeholder]="product().reference ?? product().name"
          [priority]="priority()"
          [sizes]="imageSizes()"
        />

        @if (showWishlist()) {
          <button
            khButton
            variant="tertiary"
            [iconOnly]="true"
            type="button"
            class="wishlist"
            [attr.aria-pressed]="wishlisted()"
            [attr.aria-label]="wishlistLabel()"
            (click)="wishlistToggled.emit(product())"
          >
            <kh-icon name="heart" size="sm" />
          </button>
        }
      </div>

      <div class="body">
        @if (product().brand) {
          <p class="brand">{{ product().brand }}</p>
        }

        <h3 class="name">
          <a [routerLink]="product().href" (click)="opened.emit(product())">{{ product().name }}</a>
        </h3>

        @if (product().price !== null) {
          <kh-price size="sm" [price]="product().price!" [mrp]="product().mrp" />
        }

        @if (product().rating !== null) {
          <kh-rating size="sm" [average]="product().rating" [count]="product().ratingCount" />
        }

        @if (!product().isPurchasable) {
          <kh-badge tone="warning">Out of stock</kh-badge>
        } @else if (showAddToCart()) {
          <button
            khButton
            variant="secondary"
            size="sm"
            type="button"
            class="add"
            (click)="added.emit(product())"
          >
            Add to cart
          </button>
        }
      </div>
    </article>
  `,
  styles: `
    :host {
      display: block;
      block-size: 100%;
    }

    .card {
      position: relative;
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      block-size: 100%;
      padding: var(--space-2);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .media {
      position: relative;
    }

    .wishlist {
      position: absolute;
      inset-block-start: var(--space-1);
      inset-inline-end: var(--space-1);
      z-index: 2;
      background: var(--color-surface-raised);
      border-radius: var(--radius-full);
    }

    .body {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      flex: 1;
    }

    .brand {
      margin: 0;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }

    .name {
      margin: 0;
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
      line-height: var(--leading-normal);
    }

    .name a {
      color: var(--color-text);
      text-decoration: none;
      /* Two lines, then an ellipsis: a grid whose cards are different heights because one product
         has a long name reads as broken, and clamping is the only way to hold the row. */
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    /* The whole card is the target; the link is still the only thing in the tab order. */
    .name a::after {
      content: '';
      position: absolute;
      inset: 0;
      z-index: 1;
    }

    .add {
      margin-block-start: auto;
      /* Above the stretched link, or the card's own navigation eats the tap. */
      position: relative;
      z-index: 2;
      align-self: flex-start;
    }

    .is-unavailable .media {
      opacity: 0.6;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductCard {
  readonly product = input.required<ProductCardView>();
  /** Whether this card holds the LCP image. At most one card on a page may. */
  readonly priority = input(false);
  readonly showWishlist = input(true);
  readonly showAddToCart = input(false);
  readonly wishlisted = input(false);
  /** Passed to the image's `sizes`; a rail's cards are narrower than a grid's. */
  readonly imageSizes = input('(min-width: 1024px) 20rem, (min-width: 768px) 33vw, 50vw');

  /** The product was opened. Emitted for the search click report and `select_item`. */
  readonly opened = output<ProductCardView>();
  readonly added = output<ProductCardView>();
  readonly wishlistToggled = output<ProductCardView>();

  protected readonly wishlistLabel = computed(() =>
    this.wishlisted()
      ? `Remove ${this.product().name} from your wishlist`
      : `Save ${this.product().name} for later`,
  );
}
