import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ProfileStore } from '@klarahome/data-access-account';
import { PaymentHandoff } from '@klarahome/data-access-checkout';
import { OrderResponse, OrdersService } from '@klarahome/data-access-orders';
import { KhDatePipe } from '@klarahome/i18n';
import { Alert, Badge, Button, Icon, Skeleton } from '@klarahome/ui-primitives';
import { AddressCard, OrderSummary, StatusTone } from '@klarahome/ui-patterns';
import { ToastService } from '@klarahome/util';

import { CommerceMapper, isCashOnDelivery } from '../../core/commerce.mapper';

/**
 * Order confirmation — `/checkout/confirmation/:orderNumber`.
 *
 * The page every checkout ends on, **whatever happened to the payment**. A prepaid order whose
 * gateway was dismissed, one whose card was declined, and one that went through all arrive here,
 * because in all three cases there is an order and the customer needs to see it. What differs is the
 * banner at the top and whether there is a "pay now" button under it.
 *
 * **The order is re-read from the API rather than passed through the router.** State handed between
 * routes is state that vanishes on a refresh, and this is a URL people bookmark, screenshot and send
 * to support. It also means the page shows what the *server* thinks, which after a payment is the
 * only opinion that counts — the gateway's callback has already been verified, and the webhook may
 * have arrived in between (Step 15).
 *
 * The payment status is polled once on arrival rather than continuously. A webhook that has not
 * landed yet resolves in seconds, and a page that polled forever would keep a phone awake on a
 * screen the customer has already left.
 */
@Component({
  selector: 'kh-order-confirmation-page',
  imports: [AddressCard, Alert, Badge, Button, Icon, KhDatePipe, OrderSummary, RouterLink, Skeleton],
  template: `
    @if (loading()) {
      <kh-skeleton height="16rem" />
    } @else if (order(); as placed) {
      <div class="head">
        @if (isPaid()) {
          <kh-icon name="check" class="tick" />
          <h1>Thank you — your order is confirmed</h1>
          <p class="lead">
            Order {{ placed.orderNumber }}, placed {{ placed.placedAt | khDate }}. We have emailed you the
            details.
          </p>
        } @else if (isCod()) {
          <kh-icon name="check" class="tick" />
          <h1>Thank you — your order is confirmed</h1>
          <p class="lead">
            Order {{ placed.orderNumber }}. Please keep {{ dueOnDelivery() }} ready for the delivery partner.
          </p>
        } @else {
          <h1>Your order is placed, but not paid for yet</h1>
          <p class="lead">
            Order {{ placed.orderNumber }} is being held for you. It will be confirmed as soon as the payment
            goes through.
          </p>
        }
      </div>

      @if (!isPaid() && !isCod()) {
        <kh-alert tone="warning" heading="Payment not completed">
          Nothing has been charged. You can pay for this order now, or from the order page at any time before
          it expires.
        </kh-alert>

        <div class="actions">
          <button khButton variant="primary" type="button" [disabled]="retrying()" (click)="retryPayment()">
            {{ retrying() ? 'Opening…' : 'Pay now' }}
          </button>
        </div>
      }

      <div class="layout">
        <div class="detail">
          @for (subOrder of placed.subOrders; track subOrder.id) {
            <section class="parcel">
              <h2>
                {{ subOrder.vendorName || 'Seller' }}
                <kh-badge [tone]="statusTone(subOrder.status)">{{ statusLabel(subOrder.status) }}</kh-badge>
              </h2>

              <p class="promise">
                @if (subOrder.promisedMaxDays > 0) {
                  Arrives in {{ subOrder.promisedMinDays }}–{{ subOrder.promisedMaxDays }} days
                } @else {
                  We will confirm a delivery date once it is dispatched
                }
              </p>

              <ul class="lines">
                @for (line of subOrder.lines; track line.id) {
                  <li>{{ line.quantity }} × {{ line.name }}</li>
                }
              </ul>
            </section>
          }

          @if (deliverTo(); as address) {
            <section class="parcel">
              <h2>Delivering to</h2>
              <kh-address-card [address]="address" />
            </section>
          }
        </div>

        <aside>
          @if (summary(); as details) {
            <kh-order-summary [summary]="details" heading="What you paid" />
          }

          <div class="actions">
            <a khButton variant="secondary" [routerLink]="['/account/orders', placed.orderNumber]">
              Track this order
            </a>
            <a khButton variant="tertiary" routerLink="/">Continue shopping</a>
          </div>
        </aside>
      </div>
    } @else {
      <kh-alert tone="danger" heading="We could not find that order">
        If you have just placed one, it will be in your account in a moment.
      </kh-alert>
      <div class="actions">
        <a khButton variant="primary" routerLink="/account/orders">Your orders</a>
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
      padding-block: var(--space-6) var(--space-10);
    }

    .head {
      margin-block-end: var(--space-6);
    }

    .tick {
      color: var(--color-success);
    }

    h1 {
      font-size: var(--text-2xl);
    }

    .lead {
      color: var(--color-text-muted);
    }

    .layout {
      display: grid;
      gap: var(--space-6);
      margin-block-start: var(--space-6);
    }

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: minmax(0, 1fr) 22rem;
        align-items: start;
      }
    }

    .parcel {
      padding-block: var(--space-4);
      border-block-end: 1px solid var(--color-border);
    }

    .parcel h2 {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
      margin: 0 0 var(--space-2);
      font-size: var(--text-base);
    }

    .promise {
      margin: 0 0 var(--space-2);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .lines {
      margin: 0;
      padding-inline-start: var(--space-5);
      font-size: var(--text-sm);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin-block-start: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderConfirmationPage {
  private readonly orders = inject(OrdersService);
  private readonly payments = inject(PaymentHandoff);
  private readonly profile = inject(ProfileStore);
  private readonly mapper = inject(CommerceMapper);
  private readonly toasts = inject(ToastService);
  private readonly route = inject(ActivatedRoute);

  protected readonly order = signal<OrderResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly retrying = signal(false);

  protected readonly isCod = computed(() => isCashOnDelivery(this.order()?.paymentMethod));

  protected readonly isPaid = computed(() => {
    const status = this.order()?.paymentStatus;
    return status === 'Paid' || status === 'Captured';
  });

  protected readonly summary = computed(() => {
    const order = this.order();
    return order ? this.mapper.summaryFromOrder(order) : null;
  });

  protected readonly deliverTo = computed(() => {
    const order = this.order();
    if (!order) return null;
    const address = order.shippingAddress;

    return {
      id: '',
      label: null,
      recipientName: address.recipientName,
      mobile: address.mobile,
      lines: [
        address.line1,
        address.line2 ?? '',
        address.landmark ?? '',
        [address.city, address.stateName ?? '', address.pincode].filter(Boolean).join(', '),
      ].filter((line) => line.trim().length > 0),
      line1: address.line1,
      line2: address.line2,
      landmark: address.landmark,
      city: address.city,
      stateId: address.stateId,
      pincode: address.pincode,
      gstin: address.gstin,
      type: 'Home' as const,
      isDefaultShipping: false,
      isDefaultBilling: false,
    };
  });

  /** What the delivery partner will collect. The order's own figure, never a recomputation. */
  protected readonly dueOnDelivery = computed(() => {
    const order = this.order();
    if (!order) return '';
    return new Intl.NumberFormat('en-IN', {
      style: 'currency',
      currency: order.currencyCode || 'INR',
    }).format(order.amountPayable);
  });

  constructor() {
    this.profile.loadOnce();

    const orderNumber = this.route.snapshot.paramMap.get('orderNumber');
    if (!orderNumber) {
      this.loading.set(false);
    } else {
      this.load(orderNumber);
    }
  }

  protected statusLabel(status: string): string {
    return this.mapper.status(status).label;
  }

  protected statusTone(status: string): StatusTone {
    return this.mapper.status(status).tone;
  }

  /**
   * Re-opens the gateway for an order that was placed and not paid.
   *
   * The API mints a fresh provider order against the same one of ours — a gateway order cannot be
   * reopened once it has been walked away from — so this is a retry of the payment and never a
   * second order.
   */
  protected retryPayment(): void {
    const order = this.order();
    if (!order || this.retrying()) return;

    this.retrying.set(true);
    const user = this.profile.user();

    this.payments
      .retry(order.id, {
        name: this.profile.displayName(),
        email: user?.email ?? null,
        mobile: user?.mobile ?? null,
      })
      .subscribe({
        next: (outcome) => {
          this.retrying.set(false);
          if (outcome.kind === 'paid') {
            this.toasts.success('Payment received. Your order is confirmed.');
            this.load(order.orderNumber);
          } else if (outcome.kind === 'failed') {
            this.toasts.warning(outcome.reason);
          }
        },
        error: () => {
          this.retrying.set(false);
          this.toasts.danger('We could not start the payment. Please try again in a moment.');
        },
      });
  }

  private load(orderNumber: string): void {
    this.loading.set(true);
    this.orders.get(orderNumber).subscribe({
      next: (order) => {
        this.order.set(order);
        this.loading.set(false);
        // Asked once, not polled: a webhook that has not landed yet resolves in seconds, and the
        // order page is where somebody who waited longer than that would look.
        if (!isCashOnDelivery(order.paymentMethod) && order.paymentStatus !== 'Paid')
          this.confirmPayment(order);
      },
      error: () => {
        this.order.set(null);
        this.loading.set(false);
      },
    });
  }

  private confirmPayment(order: OrderResponse): void {
    this.payments.status(order.id).subscribe({
      next: (payment) => {
        if (!payment.isPaid) return;
        // The webhook landed after the order was read. Re-reading is cheaper than merging two
        // opinions about the same order into one view model.
        this.load(order.orderNumber);
      },
      error: () => undefined,
    });
  }
}
