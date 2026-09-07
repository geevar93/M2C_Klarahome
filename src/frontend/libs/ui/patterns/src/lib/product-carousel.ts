import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ProductCardView } from './catalog.model';
import { ProductCard } from './product-card';

/**
 * A horizontal row of products — related items, recently viewed, a merchandised carousel.
 *
 * **It scrolls; it does not rotate.** No timer, no auto-advance, no previous/next buttons that
 * animate past content the shopper was reading: auto-rotation is banned on mobile by
 * docs/05-frontend-architecture.md §3.3, and a native scroller is what every phone user already
 * knows how to drive. `scroll-snap` makes it land on a card rather than halfway through one.
 *
 * A scrolling region has to be reachable without a mouse wheel or a swipe, so the list carries
 * `tabindex="0"` and an accessible name — that is what lets a keyboard user scroll it with the
 * arrow keys, and it is a WCAG 2.2 requirement rather than a nicety.
 */
@Component({
  selector: 'kh-product-carousel',
  imports: [ProductCard, RouterLink],
  template: `
    <div class="head">
      <h2>{{ heading() }}</h2>
      @if (viewAllHref()) {
        <a class="view-all" [routerLink]="viewAllHref()">View all</a>
      }
    </div>

    <ol class="rail" tabindex="0" [attr.aria-label]="heading()">
      @for (product of products(); track product.variantId) {
        <li>
          <kh-product-card
            [product]="product"
            [showWishlist]="showWishlist()"
            imageSizes="(min-width: 768px) 14rem, 45vw"
            (opened)="opened.emit($event)"
            (wishlistToggled)="wishlistToggled.emit($event)"
          />
        </li>
      }
    </ol>
  `,
  styles: `
    :host {
      display: block;
      margin-block: var(--space-8);
    }

    .head {
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: var(--space-4);
    }

    h2 {
      margin: 0 0 var(--space-3);
      font-size: var(--text-xl);
    }

    .view-all {
      font-size: var(--text-sm);
      white-space: nowrap;
    }

    .rail {
      display: flex;
      gap: var(--space-3);
      margin: 0;
      padding: 0 0 var(--space-2);
      list-style: none;
      overflow-x: auto;
      scroll-snap-type: x mandatory;
      /* The gutter is negative-margined out and padded back in, so the first card starts at the
         page edge and the last one can scroll past it — a rail that stops short of the edge looks
         like it has ended when it has not. */
      overscroll-behavior-x: contain;
    }

    .rail > li {
      flex: 0 0 auto;
      inline-size: 45vw;
      max-inline-size: 14rem;
      scroll-snap-align: start;
    }

    @media (min-width: 768px) {
      .rail > li {
        inline-size: 14rem;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductCarousel {
  readonly heading = input.required<string>();
  readonly products = input.required<readonly ProductCardView[]>();
  readonly viewAllHref = input<string | null>(null);
  readonly showWishlist = input(false);

  readonly opened = output<ProductCardView>();
  readonly wishlistToggled = output<ProductCardView>();
}
