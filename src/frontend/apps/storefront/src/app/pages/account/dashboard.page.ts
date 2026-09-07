import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProfileStore } from '@klarahome/data-access-account';
import { OrdersService } from '@klarahome/data-access-orders';
import { Alert, Button, Skeleton } from '@klarahome/ui-primitives';
import { OrderCard, OrderCardView } from '@klarahome/ui-patterns';

import { CommerceMapper } from '../../core/commerce.mapper';

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
  imports: [Alert, Button, OrderCard, RouterLink, Skeleton],
  template: `
    <h1>Your account</h1>

    @if (unpaid(); as order) {
      <kh-alert tone="warning" heading="One order is waiting for payment">
        Order {{ order.orderNumber }} is placed but not paid for.
        <a khButton variant="tertiary" size="sm" [routerLink]="['/account/orders', order.orderNumber]">
          Pay for it now
        </a>
      </kh-alert>
    }

    <section>
      <div class="head">
        <h2>Recent orders</h2>
        <a routerLink="/account/orders">See all</a>
      </div>

      @if (loading()) {
        <kh-skeleton height="8rem" />
      } @else if (recent().length === 0) {
        <p class="empty">
          You have not ordered anything yet.
          <a routerLink="/">Have a look around</a>.
        </p>
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

    h1 {
      font-size: var(--text-2xl);
    }

    .head {
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: var(--space-3);
    }

    h2 {
      margin: 0;
      font-size: var(--text-lg);
    }

    .orders {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      margin-block-start: var(--space-3);
    }

    .empty {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountDashboardPage {
  private readonly orders = inject(OrdersService);
  private readonly profile = inject(ProfileStore);
  private readonly mapper = inject(CommerceMapper);

  protected readonly loading = signal(true);
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
      this.rows().find((order) => order.paymentMethod !== 'COD' && order.paymentStatus !== 'Paid') ?? null,
  );

  constructor() {
    this.profile.loadOnce();

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
      error: () => this.loading.set(false),
    });
  }
}
