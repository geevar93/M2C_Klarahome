import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ProductCardView } from './catalog.model';
import { ProductCard } from './product-card';

/**
 * A titled set of products — related items, recently viewed, a merchandised block.
 *
 * **Two presentations, one list.** From 'md' it is a true horizontal rail: scroll-snap, one card
 * per snap point, sized from the block's `limit`, not from how many cards there are (six across a desktop, or four
 * when the limit is four or fewer; four on a tablet), so every rail on a page has the same card
 * width and a short rail leaves space on the right. Any further products are a swipe or a trackpad
 * away. On a phone a sideways scroller hides everything past the
 * first card and a half behind a swipe most shoppers never make, so there it is a two-column grid
 * of at most the first four with "View all" beneath; a set with nowhere to send the shopper (no
 * `viewAllHref`) shows every product instead of hiding some behind a link that does not exist.
 * Still no timer and no auto-advance, which docs/05-frontend-architecture.md §3.3 bans on mobile.
 *
 * `limit` caps the set (the CMS block's own field); the selector and the other inputs keep their
 * old names, so every page that renders one is unchanged.
 */
@Component({
  selector: 'kh-product-carousel',
  imports: [ProductCard, RouterLink],
  template: `
    <div class="head">
      <h2 class="kh-section-heading">{{ heading() }}</h2>
      @if (viewAllHref()) {
        <a class="view-all view-all--head" [routerLink]="viewAllHref()">View all</a>
      }
    </div>

    <ol
      class="items"
      [class.capped]="!!viewAllHref()"
      [attr.aria-label]="heading()"
      [style.--cols-md]="mdColumns()"
      [style.--cols-lg]="lgColumns()"
    >
      @for (product of shown(); track product.variantId) {
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

    @if (viewAllHref() && shown().length > 4) {
      <a class="view-all view-all--foot" [routerLink]="viewAllHref()">View all</a>
    }
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

    /* On a phone the link sits beneath the grid, where the shopper's thumb has just stopped; from
       'md' it is in the heading row beside the rail. */
    .view-all--head {
      display: none;
    }

    .view-all--foot {
      margin-block-start: var(--space-2);
    }

    @media (min-width: 768px) {
      .view-all--head {
        display: inline-flex;
      }

      .view-all--foot {
        display: none;
      }
    }

    /* Phone: the same auto-fill rule \`kh-grid\` uses, so the column count follows the container.
       \`min(9rem, 100%)\` keeps a single column from overflowing a container narrower than the
       floor. */
    .items {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(9rem, 100%), 1fr));
      gap: var(--space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    /* Only the first four on a phone, and only when "View all" is there to carry the rest. */
    .items.capped > li:nth-child(n + 5) {
      display: none;
    }

    .items > li {
      display: flex;
      min-inline-size: 0;
    }

    .items > li > kh-product-card {
      flex: 1;
      min-inline-size: 0;
    }

    /* 'md' and up: the rail. Each card is a share of the visible width, computed from the number
       of columns the template put in \`--cols-md\` / \`--cols-lg\` (never more than there are
       products, never fewer than three or four), so the first screenful is always whole cards and
       the rest scroll in. Snapping is mandatory on the card edge; the scrollbar is thin but
       visible, because a mouse user with no trackpad needs something to drag. The block padding is
       room for the card's hover shadow, which a scroll container would otherwise clip. */
    @media (min-width: 768px) {
      .items {
        --gap: var(--space-4);
        --cols: var(--cols-md, 4);
        display: grid;
        grid-auto-flow: column;
        grid-template-columns: none;
        grid-auto-columns: calc((100% - (var(--cols) - 1) * var(--gap)) / var(--cols));
        gap: var(--gap);
        overflow-x: auto;
        overscroll-behavior-x: contain;
        scroll-snap-type: x mandatory;
        scrollbar-width: thin;
        padding-block: var(--space-1) var(--space-3);
      }

      .items > li {
        scroll-snap-align: start;
      }

      .items.capped > li:nth-child(n + 5) {
        display: flex;
      }
    }

    @media (min-width: 1024px) {
      .items {
        --cols: var(--cols-lg, 6);
      }
    }

    @media (prefers-reduced-motion: no-preference) {
      .items {
        scroll-behavior: smooth;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductCarousel {
  readonly heading = input.required<string>();
  readonly products = input.required<readonly ProductCardView[]>();
  readonly viewAllHref = input<string | null>(null);
  /** The most products to show. Null or zero means all of them. */
  readonly limit = input<number | null>(null);
  readonly showWishlist = input(false);

  protected readonly shown = computed(() => {
    const limit = this.limit();
    const all = this.products();
    return limit !== null && limit > 0 ? all.slice(0, limit) : all;
  });
  /**
   * Columns in view on a tablet: four. It is fixed, not read from how many cards came back, so every
   * rail on a page has the same card width and a short rail leaves space on the right.
   */
  protected readonly mdColumns = computed(() => 4);
  /**
   * Columns in view on a desktop, from the block's `limit` and not from the card count: four across
   * when the limit is four or fewer, otherwise six.
   */
  protected readonly lgColumns = computed(() => {
    const limit = this.limit();
    return limit !== null && limit > 0 && limit <= 4 ? 4 : 6;
  });

  readonly opened = output<ProductCardView>();
  readonly wishlistToggled = output<ProductCardView>();
}
