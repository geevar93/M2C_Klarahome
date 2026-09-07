import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { KhDatePipe, MoneyPipe } from '@klarahome/i18n';
import { Badge, ProductImage } from '@klarahome/ui-primitives';

import { OrderCardView } from './commerce.model';

/**
 * One order in the orders list.
 *
 * The whole card is a link, and it is an `<a>` around the content rather than a `<div (click)>`
 * with a nested link: a customer looking for a two-month-old order scans thumbnails, and every one
 * of them has to be part of the same target. It also means middle-click, long-press and "open in
 * new tab" work, which on a list people return to repeatedly is not a small thing.
 *
 * The **status tone is decided by the app**, not here. Whether `PartiallyCancelled` is a warning or
 * merely information is a domain judgement about a state machine this library does not know
 * (Step 14); a component that pattern-matched on the string would be one new status away from
 * rendering a payment failure in green.
 *
 * Thumbnails are capped, and the count is shown alongside rather than a "+3" badge on the last
 * image — a number in words is readable, and a badge over a picture is not.
 */
@Component({
  selector: 'kh-order-card',
  imports: [Badge, KhDatePipe, MoneyPipe, ProductImage, RouterLink],
  template: `
    <a class="card" [routerLink]="['/account/orders', order().orderNumber]">
      <span class="head">
        <span class="number">{{ order().orderNumber }}</span>
        <kh-badge [tone]="order().statusTone">{{ order().statusLabel }}</kh-badge>
      </span>

      <span class="thumbs" aria-hidden="true">
        @for (image of order().images; track $index) {
          <kh-product-image [source]="image" sizes="4rem" />
        }
      </span>

      <span class="meta">
        <span>Placed {{ order().placedAt | khDate: 'd MMM y' }}</span>
        <span>
          {{ order().itemCount }} {{ order().itemCount === 1 ? 'item' : 'items' }}
          @if (order().sellerCount > 1) {
            from {{ order().sellerCount }} sellers
          }
        </span>
        <span>{{ order().paymentLabel }}</span>
      </span>

      <span class="total">{{ order().total | khMoney }}</span>
    </a>
  `,
  styles: `
    :host {
      display: block;
    }

    .card {
      display: grid;
      grid-template-areas:
        'head head'
        'thumbs thumbs'
        'meta total';
      grid-template-columns: 1fr auto;
      gap: var(--space-3);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      color: var(--color-text);
      text-decoration: none;
    }

    .card:hover {
      border-color: var(--color-border-strong);
    }

    .head {
      grid-area: head;
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
    }

    .number {
      font-weight: var(--weight-medium);
      font-variant-numeric: tabular-nums;
    }

    .thumbs {
      grid-area: thumbs;
      display: flex;
      gap: var(--space-2);
    }

    .thumbs kh-product-image {
      inline-size: var(--space-16);
      flex: none;
    }

    .meta {
      grid-area: meta;
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .total {
      grid-area: total;
      align-self: end;
      font-weight: var(--weight-medium);
      font-variant-numeric: tabular-nums;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderCard {
  readonly order = input.required<OrderCardView>();
}
