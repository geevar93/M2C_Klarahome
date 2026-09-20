import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ReturnSummaryResponse, ReturnsService } from '@klarahome/data-access-orders';
import { KhDatePipe, MoneyPipe } from '@klarahome/i18n';
import { Badge, Button, EmptyState, ErrorState, PageHeader, Skeleton } from '@klarahome/ui-primitives';
import { StatusTone } from '@klarahome/ui-patterns';

import { CommerceMapper } from '../../core/commerce.mapper';

/**
 * Your returns — `/account/returns`.
 *
 * A list rather than a set of cards with pictures, because that is what a customer wants from it:
 * which request, for which order, at what stage, and how much is coming back. The photographs are on
 * the order the return came from.
 *
 * The refund shown is the **estimate** until the return is approved, and the approved figure after —
 * they can differ, because QC decides what actually comes back (Step 17) and a page that showed only
 * the estimate would be promising money the seller has not agreed to.
 */
@Component({
  selector: 'kh-account-returns-page',
  imports: [Badge, Button, EmptyState, ErrorState, KhDatePipe, MoneyPipe, PageHeader, RouterLink, Skeleton],
  template: `
    <kh-page-header title="Returns" />

    @if (loading() && rows().length === 0) {
      <div class="list">
        <kh-skeleton height="8rem" />
        <kh-skeleton height="8rem" />
      </div>
    } @else if (error() && rows().length === 0) {
      <kh-error-state (retry)="load(true)" />
    } @else if (rows().length === 0) {
      <kh-empty-state
        heading="You have not requested any returns yet"
        message="You can request a return from any delivered order, while its return window is open."
      >
        <a khButton variant="primary" routerLink="/account/orders">Your orders</a>
      </kh-empty-state>
    } @else {
      <ul class="list">
        @for (row of rows(); track row.id) {
          <li>
            <a [routerLink]="['/account/returns', row.returnNumber]">
              <span class="head">
                <span class="number">{{ row.returnNumber }}</span>
                <kh-badge [tone]="tone(row.status)">{{ label(row.status) }}</kh-badge>
              </span>
              <span class="meta">
                Order {{ row.orderNumber }} · {{ row.units }} {{ row.units === 1 ? 'item' : 'items' }} ·
                raised {{ row.requestedAt | khDate: 'd MMM y' }}
              </span>
              <span class="amount">
                {{ mapper.money(row.refundAmount || row.estimatedRefund, row.currencyCode) | khMoney }}
                <span class="qualifier">{{ row.refundAmount ? 'refunded' : 'estimated refund' }}</span>
              </span>
            </a>
          </li>
        }
      </ul>

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

    .list {
      list-style: none;
      margin: 0 0 var(--space-4);
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
    }

    a {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      color: var(--color-text);
      text-decoration: none;
    }

    a:hover {
      border-color: var(--color-border-strong);
    }

    .head {
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

    .meta,
    .qualifier {
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .amount {
      font-variant-numeric: tabular-nums;
    }

    .qualifier {
      margin-inline-start: var(--space-1);
      font-size: var(--text-xs);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountReturnsPage {
  private readonly api = inject(ReturnsService);
  protected readonly mapper = inject(CommerceMapper);

  protected readonly rows = signal<readonly ReturnSummaryResponse[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly error = signal(false);
  protected readonly cursor = signal<string | null>(null);

  constructor() {
    this.load(true);
  }

  protected label(status: string): string {
    return this.mapper.returnStatus(status).label;
  }

  protected tone(status: string): StatusTone {
    return this.mapper.returnStatus(status).tone;
  }

  protected loadMore(): void {
    if (!this.cursor() || this.loadingMore()) return;
    this.load(false);
  }

  protected load(reset: boolean): void {
    this.error.set(false);
    if (reset) {
      this.loading.set(true);
    } else {
      this.loadingMore.set(true);
    }

    this.api.list({ cursor: reset ? null : this.cursor() }).subscribe({
      next: (page) => {
        this.rows.update((current) => (reset ? page.items : [...current, ...page.items]));
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
