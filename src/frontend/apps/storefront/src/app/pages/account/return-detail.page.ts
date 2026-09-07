import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ReturnResponse, ReturnsService } from '@klarahome/data-access-orders';
import { INR, Money, money } from '@klarahome/domain';
import { KhDatePipe, MoneyPipe } from '@klarahome/i18n';
import { Alert, Badge, Button, Skeleton } from '@klarahome/ui-primitives';
import { StatusTone } from '@klarahome/ui-patterns';
import { BreadcrumbTrail, ToastService } from '@klarahome/util';

import { CommerceMapper } from '../../core/commerce.mapper';
import { describeError } from '../../core/describe-error';

/**
 * One return — `/account/returns/:rmaNumber`.
 *
 * What was asked for, what stage it is at, and the one thing the customer can still do about it.
 *
 * **Withdrawal is offered because the return's own `nextStatuses` allows it**, not because this page
 * decided that a "Requested" return is cancellable. The transition table is data on the server
 * (Step 17), it comes down with every return, and reading it is what keeps the button and the rule
 * from drifting apart when a status is added.
 *
 * The pickup, when there is one, is the most useful thing on the page — a customer with a reverse
 * pickup scheduled wants the date and the waybill, and both are here rather than in an email they
 * would have to go and find.
 */
@Component({
  selector: 'kh-return-detail-page',
  imports: [Alert, Badge, Button, KhDatePipe, MoneyPipe, RouterLink, Skeleton],
  template: `
    @if (loading()) {
      <kh-skeleton height="16rem" />
    } @else if (rma(); as request) {
      <header class="head">
        <div>
          <h1>Return {{ request.returnNumber }}</h1>
          <p class="meta">
            From order
            <a [routerLink]="['/account/orders', request.orderNumber]">{{ request.orderNumber }}</a>
            · raised {{ request.requestedAt | khDate }}
          </p>
        </div>
        <kh-badge [tone]="tone(request.status)">{{ label(request.status) }}</kh-badge>
      </header>

      @if (request.rejectedReason) {
        <kh-alert tone="danger" heading="This return was not accepted">{{ request.rejectedReason }}</kh-alert>
      }

      @if (request.isPickupRequired && request.pickupScheduledFor) {
        <kh-alert tone="info" heading="Pickup scheduled">
          A courier will collect this on {{ request.pickupScheduledFor | khDate: 'd MMM y' }}.
          @if (request.pickupAwb) {
            Waybill {{ request.pickupAwb }}.
          }
          Please keep the items in their original packaging.
        </kh-alert>
      }

      <section class="block">
        <h2>What you are sending back</h2>
        <ul class="lines">
          @for (line of request.lines; track line.id) {
            <li>
              <span>{{ line.quantity }} × {{ line.name }}</span>
              <span class="amount">{{ amount(line.refundAmount, request.currencyCode) | khMoney }}</span>
              @if (line.qcNote) {
                <span class="note">{{ line.qcNote }}</span>
              }
            </li>
          }
        </ul>
      </section>

      <section class="block">
        <h2>Refund</h2>
        <dl>
          <div>
            <dt>{{ request.refundAmount > 0 ? 'Refunded' : 'Estimated refund' }}</dt>
            <dd>
              {{
                amount(
                  request.refundAmount || request.approvedAmount || request.estimatedRefund,
                  request.currencyCode
                ) | khMoney
              }}
            </dd>
          </div>
          @if (request.refundMode) {
            <div>
              <dt>Going to</dt>
              <dd>{{ request.refundMode === 'StoreCredit' ? 'Your store credit' : 'How you paid' }}</dd>
            </div>
          }
          @if (request.returnShippingFee > 0) {
            <div>
              <dt>Return shipping</dt>
              <dd>−{{ amount(request.returnShippingFee, request.currencyCode) | khMoney }}</dd>
            </div>
          }
        </dl>

        @if (request.refundAmount === 0 && request.status !== 'Rejected') {
          <p class="note">
            The final amount is confirmed once the items reach the seller and pass their check.
          </p>
        }
      </section>

      @if (canWithdraw()) {
        <button khButton variant="tertiary" type="button" [disabled]="busy()" (click)="withdraw()">
          {{ busy() ? 'Withdrawing…' : 'Withdraw this return' }}
        </button>
      }
    } @else {
      <kh-alert tone="danger" heading="We could not find that return">
        Check the number, or open your returns list.
      </kh-alert>
      <a khButton variant="primary" routerLink="/account/returns">Your returns</a>
    }
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

    .block {
      padding-block: var(--space-4);
      border-block-end: 1px solid var(--color-border);
    }

    .block h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-base);
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

    dl {
      margin: 0;
      font-size: var(--text-sm);
    }

    dl div {
      display: flex;
      justify-content: space-between;
      gap: var(--space-4);
      padding-block: var(--space-1);
    }

    dt {
      color: var(--color-text-muted);
    }

    dd {
      margin: 0;
      font-variant-numeric: tabular-nums;
    }

    .amount {
      font-variant-numeric: tabular-nums;
    }

    .note {
      inline-size: 100%;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReturnDetailPage {
  private readonly api = inject(ReturnsService);
  private readonly mapper = inject(CommerceMapper);
  private readonly toasts = inject(ToastService);
  private readonly trail = inject(BreadcrumbTrail);
  private readonly route = inject(ActivatedRoute);

  protected readonly rma = signal<ReturnResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  /** Offered only when the return's own transition table still allows it. */
  protected readonly canWithdraw = computed(() => this.rma()?.nextStatuses.includes('Cancelled') ?? false);

  constructor() {
    const rmaNumber = this.route.snapshot.paramMap.get('rmaNumber');
    if (rmaNumber) this.load(rmaNumber);
    else this.loading.set(false);
  }

  protected label(status: string): string {
    return this.mapper.status(status).label;
  }

  protected tone(status: string): StatusTone {
    return this.mapper.status(status).tone;
  }

  protected withdraw(): void {
    const request = this.rma();
    if (!request || this.busy()) return;

    this.busy.set(true);
    this.api.cancel(request.returnNumber, null).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.rma.set(updated);
        this.toasts.success('That return is withdrawn.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.toasts.danger(describeError(error, 'We could not withdraw that return.'));
      },
    });
  }

  private load(rmaNumber: string): void {
    this.loading.set(true);
    this.api.get(rmaNumber).subscribe({
      next: (request) => {
        this.rma.set(request);
        this.loading.set(false);
        this.trail.setLeafLabel(request.returnNumber);
      },
      error: () => {
        this.rma.set(null);
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
