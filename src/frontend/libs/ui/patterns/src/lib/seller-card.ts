import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Rating } from '@klarahome/ui-primitives';

import { SellerView } from './catalog.model';

/**
 * Who is actually selling this.
 *
 * On a marketplace the seller is a material fact, not a footnote: the return window, the dispatch
 * promise and the recourse all belong to them rather than to the store. It is stated plainly and
 * linked to their storefront, which is both what a shopper needs and what a marketplace is obliged
 * to disclose.
 */
@Component({
  selector: 'kh-seller-card',
  imports: [Rating, RouterLink],
  template: `
    <h3 class="heading">Sold by</h3>
    <p class="name">
      <a [routerLink]="seller().href">{{ seller().name }}</a>
    </p>

    @if (seller().rating !== null) {
      <kh-rating size="sm" [average]="seller().rating" />
    }

    <dl>
      <div>
        <dt>Dispatch</dt>
        <dd>{{ seller().dispatchHours <= 24 ? 'Within 1 day' : 'Within ' + dispatchDays() + ' days' }}</dd>
      </div>
      <div>
        <dt>Returns</dt>
        <dd>
          @if (seller().returnWindowDays) {
            {{ seller().returnWindowDays }}-day return window
          } @else {
            This item cannot be returned
          }
        </dd>
      </div>
    </dl>

    @if (seller().about) {
      <p class="about">{{ seller().about }}</p>
    }
  `,
  styles: `
    :host {
      display: block;
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface);
    }

    .heading {
      margin: 0 0 var(--space-1);
      font-size: var(--text-sm);
      font-weight: var(--weight-regular);
      color: var(--color-text-muted);
    }

    .name {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
      font-weight: var(--weight-medium);
    }

    dl {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: var(--space-1) var(--space-3);
      margin: var(--space-3) 0 0;
      font-size: var(--text-sm);
    }

    dl > div {
      display: contents;
    }

    dt {
      color: var(--color-text-muted);
    }

    dd {
      margin: 0;
    }

    .about {
      margin: var(--space-3) 0 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SellerCard {
  readonly seller = input.required<SellerView>();

  protected dispatchDays(): number {
    return Math.ceil(this.seller().dispatchHours / 24);
  }
}
