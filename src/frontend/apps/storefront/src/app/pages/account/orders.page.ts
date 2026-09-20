import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { OrdersService } from '@klarahome/data-access-orders';
import {
  Button,
  Chip,
  Control,
  EmptyState,
  ErrorState,
  Field,
  PageHeader,
  Skeleton,
} from '@klarahome/ui-primitives';
import { OrderCard, OrderCardView } from '@klarahome/ui-patterns';

import { CommerceMapper } from '../../core/commerce.mapper';

/** The filters offered above the list. `null` is everything. */
const FILTERS: readonly { readonly label: string; readonly status: string | null }[] = [
  { label: 'All', status: null },
  { label: 'In progress', status: 'Processing' },
  { label: 'Delivered', status: 'Delivered' },
  { label: 'Cancelled', status: 'Cancelled' },
];

/**
 * Your orders — `/account/orders`.
 *
 * **Keyset-paged with a "load more" button**, not numbered pages and not an infinite scroller. A
 * cursor is what the API offers (docs/04-api-specification.md §1.1) and it is the right shape here:
 * a customer paging back through a year of orders while a new one is placed must not see a row twice
 * or miss one. A button rather than a scroll trigger because a list that keeps growing under the
 * thumb has no bottom, and the account menu is below it.
 *
 * The filter is a status the API understands, not one applied here. Filtering a page of ten rows in
 * the browser would produce a screen with three orders on it and a "load more" button, which is not
 * a filter — it is a puzzle.
 */
@Component({
  selector: 'kh-account-orders-page',
  imports: [
    Button,
    Chip,
    Control,
    EmptyState,
    ErrorState,
    Field,
    OrderCard,
    PageHeader,
    RouterLink,
    Skeleton,
  ],
  template: `
    <kh-page-header title="Orders" />

    <!-- Below 1024px: a native select, so the choice is a picker rather than four chips fighting
         for a 390px row. From 1024px: the chip group, which is the platform's usual filter and has
         room to lay out in one line there. CSS-only swap so the selection (status(), below) is
         never duplicated into two pieces of state. -->
    <div class="filters-select">
      <kh-field label="Show" for="orders-filter">
        <select
          khControl
          id="orders-filter"
          [value]="status() ?? ''"
          (change)="filterBy($any($event.target).value || null)"
        >
          @for (filter of filters; track filter.label) {
            <option [value]="filter.status ?? ''">{{ filter.label }}</option>
          }
        </select>
      </kh-field>
    </div>

    <div class="filters filters-chips" role="group" aria-label="Filter orders">
      @for (filter of filters; track filter.label) {
        <kh-chip
          [label]="filter.label"
          [selected]="filter.status === status()"
          (toggled)="filterBy(filter.status)"
        />
      }
    </div>

    @if (loading() && orders().length === 0) {
      <div class="list">
        <kh-skeleton height="8rem" />
        <kh-skeleton height="8rem" />
      </div>
    } @else if (error() && orders().length === 0) {
      <kh-error-state (retry)="load(true)" />
    } @else if (orders().length === 0) {
      <kh-empty-state
        [heading]="status() ? 'No orders here' : 'You have not ordered anything yet'"
        [message]="
          status()
            ? 'Try another filter, or look at all your orders.'
            : 'When you buy something it will show up here, with its tracking and invoice.'
        "
      >
        <a khButton variant="primary" routerLink="/">Start shopping</a>
      </kh-empty-state>
    } @else {
      <div class="list">
        @for (order of orders(); track order.id) {
          <kh-order-card [order]="order" />
        }
      </div>

      @if (error()) {
        <kh-error-state (retry)="loadMore()" />
      } @else if (cursor()) {
        <button khButton variant="secondary" type="button" [disabled]="loadingMore()" (click)="loadMore()">
          {{ loadingMore() ? 'Loading…' : 'Load more' }}
        </button>
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }

    /* Native select on a phone, chips from 1024px — the breakpoint this page family already uses
       (account.layout.ts). Four kh-chips in a row with overflow-x: auto did not fit a 390px screen;
       they scrolled sideways and hid the last option, which is not a filter a customer can see all
       of. The design system keeps a platform picker for a single choice from a known list
       (_control.scss), so below the breakpoint the choice is a labelled <select> instead. */
    .filters-select {
      display: block;
    }

    .filters-chips {
      display: none;
    }

    @media (min-width: 1024px) {
      .filters-select {
        display: none;
      }

      .filters-chips {
        display: flex;
      }
    }

    .filters {
      gap: var(--space-2);
      margin-block-end: var(--space-4);
      flex-wrap: wrap;
    }

    .list {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      margin-block-end: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountOrdersPage {
  private readonly api = inject(OrdersService);
  private readonly mapper = inject(CommerceMapper);

  protected readonly filters = FILTERS;
  protected readonly orders = signal<readonly OrderCardView[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly error = signal(false);
  protected readonly cursor = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  constructor() {
    this.load(true);
  }

  protected filterBy(status: string | null): void {
    if (status === this.status()) return;
    this.status.set(status);
    this.load(true);
  }

  protected loadMore(): void {
    if (!this.cursor() || this.loadingMore()) return;
    this.load(false);
  }

  protected load(reset: boolean): void {
    this.error.set(false);
    if (reset) {
      this.loading.set(true);
      this.orders.set([]);
      this.cursor.set(null);
    } else {
      this.loadingMore.set(true);
    }

    this.api.list({ status: this.status(), cursor: reset ? null : this.cursor() }).subscribe({
      next: (page) => {
        const mapped = page.items.map((order) => this.mapper.orderCard(order));
        this.orders.update((current) => (reset ? mapped : [...current, ...mapped]));
        this.cursor.set(page.page.nextCursor);
        this.loading.set(false);
        this.loadingMore.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadingMore.set(false);
        this.error.set(true);
      },
    });
  }
}
