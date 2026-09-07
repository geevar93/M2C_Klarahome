import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MoneyPipe } from '@klarahome/i18n';

import { OrderSummaryView } from './commerce.model';

/**
 * The itemised price panel, shared by the cart, the checkout review, the confirmation and an order.
 *
 * It is one component because it is one thing: the customer must be shown the same breakdown at
 * every point, and four templates that each list a subtotal, a discount, a shipping charge and a
 * tax total will eventually disagree about which of them includes GST.
 *
 * **Nothing is computed here.** Every row was produced by the pricing engine and arrives already
 * decided — including which rows exist at all, because a basket with no COD fee has no COD row
 * rather than a row reading ₹0. That is the same rule the API states (docs/04-api-specification.md
 * §1): the client displays money, it does not derive it.
 *
 * The markup is a `<dl>` rather than a table. Each row is a label and its amount — a description
 * list is exactly that, it reads correctly to a screen reader without column headers nobody wrote,
 * and it stacks on a 360px screen without a horizontal scrollbar.
 */
@Component({
  selector: 'kh-order-summary',
  imports: [MoneyPipe],
  template: `
    <h2 class="heading">{{ heading() }}</h2>

    <dl>
      @for (row of summary().rows; track row.label) {
        <div class="row" [class.discount]="row.isDiscount">
          <dt>
            {{ row.label }}
            @if (row.note) {
              <span class="note">{{ row.note }}</span>
            }
          </dt>
          <dd>{{ row.isDiscount ? '−' : '' }}{{ row.amount | khMoney }}</dd>
        </div>
      }

      @if (summary().walletApplied; as credit) {
        <div class="row discount">
          <dt>Store credit</dt>
          <dd>−{{ credit | khMoney }}</dd>
        </div>
      }

      <div class="row total">
        <dt>{{ summary().totalLabel }}</dt>
        <dd>{{ summary().total | khMoney }}</dd>
      </div>

      <!-- Shown only when store credit made the two differ. Repeating the same figure twice under
           two labels is how a customer starts wondering which one they are being charged. -->
      @if (summary().amountPayable; as payable) {
        <div class="row payable">
          <dt>To pay now</dt>
          <dd>{{ payable | khMoney }}</dd>
        </div>
      }
    </dl>

    @if (summary().savings; as saved) {
      <p class="savings">You saved {{ saved | khMoney }} on this order.</p>
    }

    <ng-content />
  `,
  styles: `
    :host {
      display: block;
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
    }

    .heading {
      margin: 0 0 var(--space-3);
      font-size: var(--text-base);
    }

    dl {
      margin: 0;
    }

    .row {
      display: flex;
      justify-content: space-between;
      gap: var(--space-4);
      padding-block: var(--space-2);
      font-size: var(--text-sm);
    }

    dt {
      color: var(--color-text-muted);
    }

    .note {
      display: block;
      font-size: var(--text-xs);
    }

    dd {
      margin: 0;
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }

    .discount dd {
      color: var(--color-success);
    }

    .total {
      margin-block-start: var(--space-2);
      padding-block-start: var(--space-3);
      border-block-start: 1px solid var(--color-border);
      font-size: var(--text-base);
      font-weight: var(--weight-bold);
    }

    .total dt {
      color: var(--color-text);
    }

    .payable dd {
      font-weight: var(--weight-medium);
    }

    .savings {
      margin: var(--space-3) 0 0;
      font-size: var(--text-sm);
      color: var(--color-success);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderSummary {
  readonly summary = input.required<OrderSummaryView>();
  readonly heading = input('Price details');
}
