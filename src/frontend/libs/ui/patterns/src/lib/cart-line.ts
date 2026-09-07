import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MoneyPipe } from '@klarahome/i18n';
import { Alert, Button, Icon, ProductImage, QuantityStepper } from '@klarahome/ui-primitives';

import { CartLineView } from './commerce.model';

/** What the shopper asked for on a line. The page decides whether the API agrees. */
export interface CartLineChange {
  readonly lineId: string;
  readonly quantity: number;
}

/**
 * One line of the basket: what it is, how many, what it costs, and what is wrong with it.
 *
 * **The quantity stepper is not disabled while a change is in flight.** A shopper going from 1 to 4
 * taps plus four times in about a second, and a control that locks after the first tap loses the
 * other three. The page updates optimistically and rolls back on refusal
 * (docs/05-frontend-architecture.md §3.6), which is the behaviour that makes this safe; the line
 * only shows a busy state, and only so the total below it does not look wrong for a moment.
 *
 * **Issues are rendered on the line, not as a toast.** `CART_ITEM_OUT_OF_STOCK` is about *this*
 * product, and a message that floats over the corner of the screen while the shopper is looking at
 * the row it concerns is a message about nothing. A blocking issue is an alert; a non-blocking one
 * is a note.
 *
 * `readOnly` is the checkout review's mode: same line, same layout, no controls — because the
 * basket is frozen once a checkout session has quoted it.
 */
@Component({
  selector: 'kh-cart-line',
  imports: [Alert, Button, Icon, MoneyPipe, ProductImage, QuantityStepper, RouterLink],
  template: `
    <div class="media">
      <kh-product-image
        [source]="line().image"
        [placeholder]="line().sku"
        sizes="(min-width: 768px) 8rem, 25vw"
      />
    </div>

    <div class="detail">
      @if (line().href; as href) {
        <a class="name" [routerLink]="href">{{ line().name }}</a>
      } @else {
        <span class="name">{{ line().name }}</span>
      }

      <p class="seller">Sold by {{ line().sellerName }}</p>

      <p class="price">
        <span class="unit">{{ line().unitPrice | khMoney }}</span>
        @if (line().quantity > 1) {
          <span class="each">each</span>
        }
      </p>

      @if (!readOnly()) {
        <div class="controls">
          <kh-quantity-stepper
            [quantity]="line().quantity"
            [max]="maxQuantity()"
            [inputId]="'qty-' + line().id"
            [label]="'Quantity of ' + line().name"
            (quantityChange)="quantityChanged.emit({ lineId: line().id, quantity: $event })"
          />

          <button khButton variant="tertiary" size="sm" type="button" (click)="removed.emit(line().id)">
            <kh-icon name="trash" size="sm" />
            Remove
          </button>

          <button khButton variant="tertiary" size="sm" type="button" (click)="savedToggled.emit(line().id)">
            <kh-icon name="heart" size="sm" />
            {{ line().savedForLater ? 'Move to cart' : 'Save for later' }}
          </button>
        </div>
      } @else {
        <p class="quantity">Qty {{ line().quantity }}</p>
      }

      @for (issue of line().issues; track issue.code) {
        <kh-alert class="issue" [tone]="issue.isBlocking ? 'danger' : 'warning'">{{
          issue.message
        }}</kh-alert>
      }
    </div>

    <p class="total">{{ line().lineTotal | khMoney }}</p>
  `,
  styles: `
    :host {
      display: grid;
      grid-template-columns: 5rem 1fr auto;
      gap: var(--space-3);
      padding-block: var(--space-4);
      border-block-end: 1px solid var(--color-border);
    }

    :host([data-busy='true']) {
      opacity: 0.6;
    }

    @media (min-width: 768px) {
      :host {
        grid-template-columns: 8rem 1fr auto;
        gap: var(--space-4);
      }
    }

    .media {
      grid-row: span 1;
    }

    .detail {
      min-width: 0;
    }

    .name {
      display: block;
      font-weight: var(--weight-medium);
      color: var(--color-text);
      text-decoration: none;
    }

    .name:hover {
      text-decoration: underline;
    }

    .seller,
    .quantity {
      margin: var(--space-1) 0 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .price {
      margin: var(--space-1) 0 0;
      font-size: var(--text-sm);
    }

    .each {
      margin-inline-start: var(--space-1);
      color: var(--color-text-muted);
    }

    .controls {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
      margin-block-start: var(--space-3);
    }

    .issue {
      margin-block-start: var(--space-2);
    }

    .total {
      margin: 0;
      font-weight: var(--weight-medium);
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-busy]': 'busy()' },
})
export class CartLine {
  readonly line = input.required<CartLineView>();
  /** True while a change to this line is in flight. Dims the row; never disables the stepper. */
  readonly busy = input(false);
  /** The checkout review's mode: the same line without the controls. */
  readonly readOnly = input(false);

  readonly quantityChanged = output<CartLineChange>();
  readonly removed = output<string>();
  readonly savedToggled = output<string>();

  /**
   * The ceiling on the stepper.
   *
   * Stock on hand, capped at ten so a line with 900 in stock does not offer 900. It is a courtesy
   * either way: the authoritative check happens when the order is placed and the reservation is
   * taken (Step 13).
   */
  protected readonly maxQuantity = computed(() => Math.max(1, Math.min(10, this.line().quantityAvailable)));
}
