import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProfileStore } from '@klarahome/data-access-account';
import { OrdersService } from '@klarahome/data-access-orders';
import { Alert, Button, EmptyState, ErrorState, PageHeader, Skeleton } from '@klarahome/ui-primitives';
import { OrderCard, OrderCardView } from '@klarahome/ui-patterns';

import { CommerceMapper, isCashOnDelivery } from '../../core/commerce.mapper';

/**
 * The account dashboard — `/account`.
 *
 * It answers one question: **what is happening with my orders right now?** Everything else in the
 * account is a link in the menu beside it, so a dashboard that repeated those links as tiles would
 * be a screen whose only content is a second copy of the navigation.
 *
 * So it shows the three most recent orders and, when something needs attention, says so at the top.
 * An unpaid order is the case worth interrupting for — the customer thinks they have bought
 * something and the seller is holding stock that has not been paid for, and both are fixed by one
 * tap.
 */
@Component({
  selector: 'kh-account-dashboard-page',
  imports: [Alert, Button, EmptyState, ErrorState, OrderCard, PageHeader, RouterLink, Skeleton],
  template: `
    <kh-page-header title="Account" />

    @if (unpaid(); as order) {
      <kh-alert tone="warning" heading="One order is waiting for payment">
        Order {{ order.orderNumber }} is placed but not paid for.
        <a khButton variant="tertiary" size="sm" [routerLink]="['/account/orders', order.orderNumber]">
          Pay now
        </a>
      </kh-alert>
    }

    <section>
      <div class="head">
        <h2>Recent orders</h2>
        <a routerLink="/account/orders">See all</a>
      </div>

      @if (loading()) {
        <div class="skeletons">
          <kh-skeleton height="8rem" />
          <kh-skeleton height="8rem" />
        </div>
      } @else if (error()) {
        <kh-error-state (retry)="load()" />
      } @else if (recent().length === 0) {
        <kh-empty-state
          heading="You have not ordered anything yet"
          message="When you buy something it will show up here, with its tracking and invoice."
        >
          <a khButton variant="primary" routerLink="/">Start shopping</a>
        </kh-empty-state>
      } @else {
        <div class="orders">
          @for (order of recent(); track order.id) {
            <kh-order-card [order]="order" />
          }
        </div>
      }
    </section>
  `,
  styles: `
    :host {
      display: block;
    }

    section {
      /* Separates the section from whatever sits above it — the warning alert, or the page header
         when there is nothing to warn about. Neither renders its own bottom margin, so without this
         the alert's border and "Recent orders" touched directly. */
      margin-block-start: var(--space-6);
    }

    .head {
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: var(--space-3);
    }

    /* The heading yields, the link does not. A flex item's min-width is auto, so on a narrow phone
       the two of them fought over the row and the link — the shorter, later item — was the one
       that lost its last characters off the right edge. 'See a' is not a link anybody can read,
       and the heading is the half of the row that can be shortened without becoming meaningless. */
    h2 {
      margin: 0;
      min-inline-size: 0;
      font-size: var(--text-lg);
    }

    .head a {
      flex: none;
      white-space: nowrap;
    }

    .skeletons {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      margin-block-start: var(--space-3);
    }

    .orders {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      margin-block-start: var(--space-3);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountDashboardPage {
  private readonly orders = inject(OrdersService);
  private readonly profile = inject(ProfileStore);
  private readonly mapper = inject(CommerceMapper);

  protected readonly loading = signal(true);
  protected readonly error = signal(false);
  protected readonly recent = signal<readonly OrderCardView[]>([]);
  private readonly rows = signal<
    readonly { orderNumber: string; paymentStatus: string; paymentMethod: string }[]
  >([]);

  /**
   * The first order that is placed and unpaid, or null.
   *
   * Prepaid only: a cash-on-delivery order is unpaid by design until it arrives, and telling a
   * customer to pay for one now would be nonsense.
   */
  protected readonly unpaid = computed(
    () =>
      this.rows().find((order) => !isCashOnDelivery(order.paymentMethod) && order.paymentStatus !== 'Paid') ??
      null,
  );

  constructor() {
    this.profile.loadOnce();
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(false);

    this.orders.list({ size: 3 }).subscribe({
      next: (page) => {
        this.rows.set(
          page.items.map((order) => ({
            orderNumber: order.orderNumber,
            paymentStatus: order.paymentStatus,
            paymentMethod: order.paymentMethod,
          })),
        );
        this.recent.set(page.items.map((order) => this.mapper.orderCard(order)));
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(true);
      },
    });
  }
}
