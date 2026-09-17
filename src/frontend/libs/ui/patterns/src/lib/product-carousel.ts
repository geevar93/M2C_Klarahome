import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ProductCardView } from './catalog.model';
import { ProductCard } from './product-card';

/**
 * A titled set of products — related items, recently viewed, a merchandised block.
 *
 * **It wraps; it does not scroll sideways.** This used to be a horizontal `scroll-snap` rail, and
 * on a phone that hid all but the first card and a half behind a swipe most shoppers never make —
 * the products below the fold of a sideways scroller are products nobody sees. Every large
 * marketplace stacks them instead: as many columns as the width allows (two on a 360px phone, five
 * or six on a desktop) and as many rows as there are products, so the page scrolls in the one
 * direction a shopper is already scrolling. Still no timer and no auto-advance, which
 * docs/05-frontend-architecture.md §3.3 bans on mobile anyway.
 *
 * The selector and inputs keep the old name, so the CMS `productCarousel` block and every page
 * that renders one are unchanged.
 *
 * The column floor is 9rem, not the listing grid's old 10rem: two 10rem columns plus the gap are
 * 332px, 4px more than a 360px phone has inside its 16px gutters, and the grid silently fell back
 * to one enormous card per row.
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

    <ol class="items" [attr.aria-label]="heading()">
      @for (product of products(); track product.variantId) {
        <li>
          <kh-product-card
            [product]="product"
            [showWishlist]="showWishlist()"
            imageSizes="(min-width: 1024px) 14rem, (min-width: 768px) 25vw, 50vw"
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
      min-inline-size: 0;
      margin: 0 0 var(--space-3);
      font-size: var(--text-xl);
      overflow-wrap: anywhere;
    }

    .view-all {
      display: inline-flex;
      align-items: center;
      min-block-size: var(--touch-target-min);
      font-size: var(--text-sm);
      white-space: nowrap;
    }

    /* The same auto-fill rule \`kh-grid\` uses, so the column count follows the container rather
       than the viewport: a set inside the PDP's full-width band and one inside a narrower CMS
       column each fit what they actually have. \`min(9rem, 100%)\` keeps a single column from
       overflowing a container narrower than the floor. */
    .items {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(9rem, 100%), 1fr));
      gap: var(--space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .items > li {
      display: flex;
      min-inline-size: 0;
    }

    .items > li > kh-product-card {
      flex: 1;
      min-inline-size: 0;
    }

    @media (min-width: 768px) {
      .items {
        grid-template-columns: repeat(auto-fill, minmax(12rem, 1fr));
        gap: var(--space-4);
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
