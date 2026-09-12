import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ProfileStore } from '@klarahome/data-access-account';
import { PaymentHandoff } from '@klarahome/data-access-checkout';
import {
  OrderResponse,
  OrdersService,
  RaiseReturnBody,
  ReturnEligibilityResponse,
  ReturnsService,
  SubOrderResponse,
} from '@klarahome/data-access-orders';
import { INR, Money, money } from '@klarahome/domain';
import { KhDatePipe, MoneyPipe } from '@klarahome/i18n';
import { Alert, Badge, Button, Drawer, Icon, Skeleton } from '@klarahome/ui-primitives';
import {
  AddressCard,
  OrderSummary,
  OrderTimeline,
  ReturnRequestForm,
  ReturnRequestValue,
  StatusTone,
} from '@klarahome/ui-patterns';
import { BreadcrumbTrail, ToastService } from '@klarahome/util';

import { CommerceMapper, isCashOnDelivery } from '../../core/commerce.mapper';
import { describeError } from '../../core/describe-error';

/**
 * One order — `/account/orders/:orderNumber`.
 *
 * The page a customer opens when something is not where they expected it, so it is organised around
 * that question rather than around the data model: **where is it, what did I pay, what can I do
 * about it.**
 *
 * **A sub-order is the unit of everything.** The parcel, the courier, the status, the invoice, the
 * cancellation and the return all belong to one seller's part of the order (Step 14), and this page
 * says so rather than flattening three parcels into one progress bar that could only ever describe
 * one of them.
 *
 * **Every action offered comes from the API's own answer.** Cancellation is offered because
 * `isCancellable` said so; a return is offered because the eligibility endpoint said the window is
 * open and named the lines. The transition table is data (Step 14, Step 17), and reading it is what
 * stops this page's buttons and the server's rules drifting apart.
 *
 * The invoice is a **short-lived signed URL** the API mints on request, opened in a new tab rather
 * than proxied — which is what keeps the private bucket private and means no long-lived public URL
 * for somebody's GST invoice ever exists (Step 8).
 */
@Component({
  selector: 'kh-order-detail-page',
  imports: [
    AddressCard,
    Alert,
    Badge,
    Button,
    Drawer,
    Icon,
    KhDatePipe,
    MoneyPipe,
    OrderSummary,
    OrderTimeline,
    ReturnRequestForm,
    RouterLink,
    Skeleton,
  ],
  template: `
    @if (loading()) {
      <kh-skeleton height="20rem" />
    } @else if (order(); as placed) {
      <header class="head">
        <div>
          <h1>Order {{ placed.orderNumber }}</h1>
          <p class="meta">
            Placed {{ placed.placedAt | khDate }} · {{ paymentLabel() }} ·
            {{ amount(placed.grandTotal, placed.currencyCode) | khMoney }}
          </p>
        </div>
        <kh-badge [tone]="tone(placed.status)">{{ label(placed.status) }}</kh-badge>
      </header>

      @if (needsPayment()) {
        <kh-alert tone="warning" heading="This order has not been paid for">
          Nothing has been charged yet. The seller is holding your items until it is.
          <button khButton variant="tertiary" size="sm" type="button" [disabled]="paying()" (click)="pay()">
            {{ paying() ? 'Opening…' : 'Pay now' }}
          </button>
        </kh-alert>
      }

      <div class="layout">
        <div class="parcels">
          @for (subOrder of placed.subOrders; track subOrder.id) {
            <section class="parcel">
              <h2>
                {{ subOrder.vendorName || 'Seller' }}
                <kh-badge [tone]="tone(subOrder.status)">{{ label(subOrder.status) }}</kh-badge>
              </h2>

              <p class="promise">
                @if (subOrder.deliveredAt) {
                  Delivered {{ subOrder.deliveredAt | khDate: 'd MMM y' }}
                } @else if (subOrder.shippedAt) {
                  Dispatched {{ subOrder.shippedAt | khDate: 'd MMM y' }}
                  @if (subOrder.carrier) {
                    · {{ subOrder.carrier }}
                  }
                } @else if (subOrder.promisedMaxDays > 0) {
                  Arrives in {{ subOrder.promisedMinDays }}–{{ subOrder.promisedMaxDays }} days
                }
              </p>

              <ul class="lines">
                @for (line of subOrder.lines; track line.id) {
                  <li>
                    <span>{{ line.quantity }} × {{ line.name }}</span>
                    <span class="amount">{{ amount(line.lineTotal, placed.currencyCode) | khMoney }}</span>
                    @if (line.quantityCancelled > 0) {
                      <span class="note">{{ line.quantityCancelled }} cancelled</span>
                    }
                    @if (line.quantityReturned > 0) {
                      <span class="note">{{ line.quantityReturned }} returned</span>
                    }
                  </li>
                }
              </ul>

              <div class="actions">
                @if (subOrder.invoice) {
                  <button
                    khButton
                    variant="secondary"
                    size="sm"
                    type="button"
                    (click)="downloadInvoice(subOrder)"
                  >
                    <kh-icon name="download" size="sm" />
                    Invoice {{ subOrder.invoice.invoiceNumber }}
                  </button>
                }

                @if (subOrder.isCancellable) {
                  <button khButton variant="tertiary" size="sm" type="button" (click)="askToCancel(subOrder)">
                    Cancel this parcel
                  </button>
                }

                @if (subOrder.status === 'Delivered') {
                  <button khButton variant="tertiary" size="sm" type="button" (click)="openReturn(subOrder)">
                    Return or replace
                  </button>
                }
              </div>
            </section>
          }

          <section class="parcel">
            <h2>Tracking</h2>
            <kh-order-timeline [entries]="timeline()" />
          </section>
        </div>

        <aside>
          @if (summary(); as details) {
            <kh-order-summary [summary]="details" heading="Payment" />
          }

          @if (deliverTo(); as address) {
            <section class="block">
              <h2>Delivering to</h2>
              <kh-address-card [address]="address" />
            </section>
          }

          <a khButton variant="tertiary" routerLink="/account/orders">All your orders</a>
        </aside>
      </div>
    } @else {
      <kh-alert tone="danger" heading="We could not find that order">
        Check the order number, or open your orders list.
      </kh-alert>
      <a khButton variant="primary" routerLink="/account/orders">Your orders</a>
    }

    <!-- Cancellation and the return form are both sheets rather than routes: each is a decision
         about the order that is on the screen behind it, and losing that context is what makes a
         customer abandon the request. -->
    <kh-drawer
      [open]="cancelling() !== null"
      side="bottom"
      label="Cancel this parcel"
      (closed)="cancelling.set(null)"
    >
      <div class="sheet">
        <h2>Cancel this parcel?</h2>
        <p>
          The whole of {{ cancelling()?.vendorName || 'this seller' }}'s part of the order will be cancelled.
          Anything you have paid for it is refunded to how you paid.
        </p>
        <div class="actions">
          <button khButton variant="danger" type="button" [disabled]="busy()" (click)="confirmCancel()">
            {{ busy() ? 'Cancelling…' : 'Yes, cancel it' }}
          </button>
          <button khButton variant="tertiary" type="button" (click)="cancelling.set(null)">Keep it</button>
        </div>
      </div>
    </kh-drawer>

    <kh-drawer
      [open]="eligibility() !== null"
      side="bottom"
      label="Return or replace"
      (closed)="eligibility.set(null)"
    >
      <div class="sheet">
        <h2>Return or replace</h2>

        @if (eligibility(); as check) {
          @if (check.isEligible) {
            <kh-return-request-form
              [lines]="returnableLines()"
              [reasons]="returnReasons()"
              [saving]="busy()"
              (submitted)="raiseReturn($event)"
              (cancelled)="eligibility.set(null)"
            />
          } @else {
            <kh-alert tone="info">
              {{ check.reason || 'This parcel can no longer be returned.' }}
            </kh-alert>
          }
        }
      </div>
    </kh-drawer>
  `,
  styles: `
    :host {
      display: block;
    }

    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--space-3);
    }

    h1 {
      margin: 0;
      font-size: var(--text-xl);
    }

    .meta {
      margin: var(--space-1) 0 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .layout {
      display: grid;
      gap: var(--space-6);
      margin-block-start: var(--space-4);
    }

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: minmax(0, 1fr) 20rem;
        align-items: start;
      }
    }

    .parcel,
    .block {
      padding-block: var(--space-4);
      border-block-end: 1px solid var(--color-border);
    }

    .parcel h2,
    .block h2 {
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
      list-style: none;
      margin: 0;
      padding: 0;
      font-size: var(--text-sm);
    }

    .lines li {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      gap: var(--space-2);
      padding-block: var(--space-1);
    }

    .amount {
      font-variant-numeric: tabular-nums;
    }

    .note {
      inline-size: 100%;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin-block-start: var(--space-3);
    }

    .sheet {
      padding: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderDetailPage {
  private readonly orders = inject(OrdersService);
  private readonly returns = inject(ReturnsService);
  private readonly payments = inject(PaymentHandoff);
  private readonly profile = inject(ProfileStore);
  private readonly mapper = inject(CommerceMapper);
  private readonly toasts = inject(ToastService);
  private readonly trail = inject(BreadcrumbTrail);
  private readonly document = inject(DOCUMENT);
  private readonly route = inject(ActivatedRoute);

  protected readonly order = signal<OrderResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly paying = signal(false);
  protected readonly cancelling = signal<SubOrderResponse | null>(null);
  protected readonly eligibility = signal<ReturnEligibilityResponse | null>(null);

  protected readonly summary = computed(() => {
    const order = this.order();
    return order ? this.mapper.summaryFromOrder(order) : null;
  });

  protected readonly timeline = computed(() => this.mapper.timeline(this.order()?.timeline ?? []));

  protected readonly paymentLabel = computed(() => {
    const order = this.order();
    return order ? this.mapper.paymentLine(order.paymentMethod, order.paymentStatus) : '';
  });

  protected readonly needsPayment = computed(() => {
    const order = this.order();
    if (!order) return false;
    return (
      !isCashOnDelivery(order.paymentMethod) && order.paymentStatus !== 'Paid' && order.status !== 'Cancelled'
    );
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

  protected readonly returnableLines = computed(() => {
    const check = this.eligibility();
    if (!check) return [];
    return check.lines.map((line) => this.mapper.returnableLine(line, check.currencyCode));
  });

  protected readonly returnReasons = computed(() =>
    (this.eligibility()?.reasons ?? []).map((reason) => this.mapper.returnReason(reason)),
  );

  constructor() {
    this.profile.loadOnce();
    const orderNumber = this.route.snapshot.paramMap.get('orderNumber');
    if (orderNumber) this.load(orderNumber);
    else this.loading.set(false);
  }

  protected label(status: string): string {
    return this.mapper.status(status).label;
  }

  protected tone(status: string): StatusTone {
    return this.mapper.status(status).tone;
  }

  /**
   * Opens the invoice.
   *
   * The URL is asked for at the moment it is needed rather than held on the page: it is short-lived
   * by design, and one fetched when the order loaded would have expired by the time somebody
   * scrolled to it.
   *
   * `noopener` on the new tab — a page opened with `window.open` can otherwise reach back through
   * `window.opener` (docs/07-security-compliance.md).
   */
  protected downloadInvoice(subOrder: SubOrderResponse): void {
    this.orders.invoiceDownload(subOrder.subOrderNumber).subscribe({
      next: (invoice) => this.document.defaultView?.open(invoice.url, '_blank', 'noopener,noreferrer'),
      error: (error: unknown) =>
        this.toasts.danger(describeError(error, 'We could not open that invoice. Please try again.')),
    });
  }

  protected askToCancel(subOrder: SubOrderResponse): void {
    this.cancelling.set(subOrder);
  }

  protected confirmCancel(): void {
    const subOrder = this.cancelling();
    const order = this.order();
    if (!subOrder || !order || this.busy()) return;

    this.busy.set(true);
    // Whole-parcel cancellation: `lines: null` is what tells the API this is not a partial one.
    this.orders.cancelSubOrder(subOrder.subOrderNumber, { reason: null, lines: null }).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.cancelling.set(null);
        this.order.set(updated);
        this.toasts.success('That parcel is cancelled. Any payment for it will be refunded.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.toasts.danger(describeError(error, 'We could not cancel that just now.'));
      },
    });
  }

  protected openReturn(subOrder: SubOrderResponse): void {
    this.returns.eligibility(subOrder.subOrderNumber).subscribe({
      next: (check) => this.eligibility.set(check),
      error: (error: unknown) =>
        this.toasts.danger(describeError(error, 'We could not check whether that can be returned.')),
    });
  }

  protected raiseReturn(request: ReturnRequestValue): void {
    const check = this.eligibility();
    if (!check || this.busy()) return;

    this.busy.set(true);
    const body: RaiseReturnBody = {
      subOrderId: check.subOrderId,
      type: request.type,
      reasonCode: request.reasonCode,
      reasonNote: request.reasonNote,
      lines: request.lines.map((line) => ({ orderLineId: line.orderLineId, quantity: line.quantity })),
      // Evidence upload is the media picker's job and is not built yet; the API accepts a request
      // without it and the team asks for photographs by email (recorded in TEST_DEBT.md).
      evidenceFileIds: null,
      refundMode: request.refundMode,
    };

    this.returns.raise(body).subscribe({
      next: (raised) => {
        this.busy.set(false);
        this.eligibility.set(null);
        this.toasts.success(`Return ${raised.returnNumber} raised. We will email you what happens next.`);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.toasts.danger(describeError(error, 'We could not raise that return. Please try again.'));
      },
    });
  }

  protected pay(): void {
    const order = this.order();
    if (!order || this.paying()) return;

    this.paying.set(true);
    const user = this.profile.user();

    this.payments
      .retry(order.id, {
        name: this.profile.displayName(),
        email: user?.email ?? null,
        mobile: user?.mobile ?? null,
      })
      .subscribe({
        next: (outcome) => {
          this.paying.set(false);
          if (outcome.kind === 'paid') {
            this.toasts.success('Payment received. Your order is confirmed.');
            this.load(order.orderNumber);
          } else if (outcome.kind === 'failed') {
            this.toasts.warning(outcome.reason);
          }
        },
        error: () => {
          this.paying.set(false);
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
        this.trail.setLeafLabel(order.orderNumber);
      },
      error: () => {
        this.order.set(null);
        this.loading.set(false);
      },
    });
  }

  /**
   * A raw amount with its currency attached.
   *
   * `khMoney` takes a `Money` so a price can never be rendered without its currency, and these
   * responses carry the two apart. Pairing them here is the boundary doing its job, not arithmetic.
   */
  protected amount(value: number, currency: string): Money {
    return money(value, currency || INR);
  }
}
