import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Badge, Button, Price, Rating } from '@klarahome/ui-primitives';

import { OfferView } from './catalog.model';

/**
 * The other sellers of this variant.
 *
 * A marketplace has to show them, and it has to show them honestly: the buy-box winner is marked
 * rather than silently first, the dispatch promise is stated in the units a shopper thinks in
 * ("dispatched in 2 days", not "48 SLA hours"), and cash on delivery is per offer because it is a
 * seller's decision and not the store's.
 *
 * Every row's action is "Buy from this seller", which selects that listing rather than adding it
 * — the quantity and the add still belong to the page's one add-to-cart control, so a shopper can
 * never have two different sellers' quantities in flight at once.
 */
@Component({
  selector: 'kh-offer-list',
  imports: [Badge, Button, Price, Rating, RouterLink],
  template: `
    <ul>
      @for (offer of offers(); track offer.listingId) {
        <li [class.is-selected]="offer.listingId === selectedListingId()">
          <div class="seller">
            <a [routerLink]="offer.sellerHref">{{ offer.sellerName }}</a>
            @if (offer.isBuyBox) {
              <kh-badge tone="success">Best offer</kh-badge>
            }
            @if (offer.sellerRating !== null) {
              <kh-rating size="sm" [average]="offer.sellerRating" />
            }
          </div>

          <kh-price size="sm" [price]="offer.price" [mrp]="offer.mrp" />

          <p class="terms">
            Dispatched in {{ dispatchDays(offer) }} ·
            {{ offer.isCodAllowed ? 'Cash on delivery' : 'Prepaid only' }}
          </p>

          @if (offer.listingId === selectedListingId()) {
            <p class="chosen">Selected</p>
          } @else {
            <button khButton variant="tertiary" size="sm" type="button" (click)="chosen.emit(offer)">
              Buy from {{ offer.sellerName }}
            </button>
          }
        </li>
      }
    </ul>
  `,
  styles: `
    :host {
      display: block;
    }

    ul {
      margin: 0;
      padding: 0;
      list-style: none;
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }

    li {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    li.is-selected {
      border-color: var(--color-primary);
      border-width: 2px;
      background: var(--color-primary-subtle);
    }

    .seller {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: var(--space-2);
      font-weight: var(--weight-medium);
    }

    .terms,
    .chosen {
      margin: 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .chosen {
      font-weight: var(--weight-medium);
      color: var(--color-text);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OfferList {
  readonly offers = input.required<readonly OfferView[]>();
  readonly selectedListingId = input<string | null>(null);

  readonly chosen = output<OfferView>();

  /**
   * Hours as a shopper reads them.
   *
   * Rounded **up**: a 30-hour promise shown as "1 day" is a promise the seller has not made.
   */
  protected dispatchDays(offer: OfferView): string {
    if (offer.dispatchHours <= 24) return '1 day';
    const days = Math.ceil(offer.dispatchHours / 24);
    return `${days} days`;
  }
}
