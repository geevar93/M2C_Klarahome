import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { isApiError } from '@klarahome/data-access-auth';
import { ReturnResponse, ReturnsService } from '@klarahome/data-access-orders';
import { KhDatePipe, MoneyPipe } from '@klarahome/i18n';
import { Alert, Badge, Button, Drawer, ErrorState, PageHeader, Skeleton } from '@klarahome/ui-primitives';
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
  imports: [Alert, Badge, Button, Drawer, ErrorState, KhDatePipe, MoneyPipe, PageHeader, RouterLink, Skeleton],
  template: `
    <kh-page-header [title]="headerTitle()" backHref="/account/returns" backLabel="All returns">
      @if (rma(); as request) {
        <span khPageHeaderStatus>
          <kh-badge [tone]="tone(request.status)">{{ label(request.status) }}</kh-badge>
        </span>
      }
    </kh-page-header>

    @if (loading()) {
      <kh-skeleton height="16rem" />
    } @else if (error()) {
      <kh-error-state (retry)="load(rmaNumber)" />
    } @else if (rma(); as request) {
      <p class="meta">
        From order
        <a [routerLink]="['/account/orders', request.orderNumber]">{{ request.orderNumber }}</a>
        · raised {{ request.requestedAt | khDate: 'd MMM y' }}
      </p>

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
              <span class="amount">{{ mapper.money(line.refundAmount, request.currencyCode) | khMoney }}</span>
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
                mapper.money(
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
              <dd>−{{ mapper.money(request.returnShippingFee, request.currencyCode) | khMoney }}</dd>
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
        <button khButton variant="tertiary" type="button" (click)="withdrawing.set(true)">
          Withdraw this return
        </button>
      }
    } @else {
      <kh-alert tone="danger" heading="We could not find that return">
        Check the number, or open your returns list.
      </kh-alert>
      <a khButton variant="primary" routerLink="/account/returns">Your returns</a>
    }

    <kh-drawer
      [open]="withdrawing()"
      side="bottom"
      label="Withdraw this return"
      labelledBy="withdraw-heading"
      (closed)="withdrawing.set(false)"
    >
      <div class="sheet">
        <h2 id="withdraw-heading">Withdraw this return?</h2>
        <p>
          We will stop processing this request. If a pickup was scheduled it will be cancelled, and you
          keep the items.
        </p>
        <div class="actions">
          <button khButton variant="danger" type="button" [disabled]="busy()" (click)="withdraw()">
            {{ busy() ? 'Withdrawing…' : 'Withdraw it' }}
          </button>
          <button khButton variant="tertiary" type="button" (click)="withdrawing.set(false)">Keep it</button>
        </div>
      </div>
    </kh-drawer>
  `,
  styles: `
    :host {
      display: block;
    }

    .meta {
      margin: 0 0 var(--space-4);
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

    .sheet {
      padding: var(--space-4);
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
export class ReturnDetailPage {
  private readonly api = inject(ReturnsService);
  protected readonly mapper = inject(CommerceMapper);
  private readonly toasts = inject(ToastService);
  private readonly trail = inject(BreadcrumbTrail);
  private readonly route = inject(ActivatedRoute);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly rmaNumber = this.route.snapshot.paramMap.get('rmaNumber') ?? '';

  protected readonly rma = signal<ReturnResponse | null>(null);
  protected readonly loading = signal(true);
  /** True only for a failure that is not "no such return" — a 404 falls through to the not-found panel. */
  protected readonly error = signal(false);
  protected readonly busy = signal(false);
  protected readonly withdrawing = signal(false);

  protected readonly headerTitle = computed(() => `Return ${this.rma()?.returnNumber ?? this.rmaNumber}`);

  /** Offered only when the return's own transition table still allows it. */
  protected readonly canWithdraw = computed(() => this.rma()?.nextStatuses.includes('Cancelled') ?? false);

  constructor() {
    if (this.rmaNumber) this.load(this.rmaNumber);
    else this.loading.set(false);
  }

  protected label(status: string): string {
    return this.mapper.returnStatus(status).label;
  }

  protected tone(status: string): StatusTone {
    return this.mapper.returnStatus(status).tone;
  }

  protected withdraw(): void {
    const request = this.rma();
    if (!request || this.busy()) return;

    this.busy.set(true);
    this.api.cancel(request.returnNumber, null).subscribe({
      next: () => {
        this.busy.set(false);
        this.withdrawing.set(false);
        this.toasts.success('That return is withdrawn.');
        this.load(request.returnNumber);
        this.focusHeading();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.toasts.danger(describeError(error, 'We could not withdraw that return.'));
      },
    });
  }

  protected load(rmaNumber: string): void {
    this.loading.set(true);
    this.error.set(false);
    this.api.get(rmaNumber).subscribe({
      next: (request) => {
        this.rma.set(request);
        this.loading.set(false);
        this.trail.setLeafLabel(request.returnNumber);
      },
      error: (error: unknown) => {
        this.rma.set(null);
        this.loading.set(false);
        if (!isApiError(error) || error.status !== 404) this.error.set(true);
      },
    });
  }

  /**
   * Moves focus to the page's own heading after a sheet closes on success.
   *
   * A drawer restores focus to whatever opened it, which is correct while cancelling — but a
   * completed withdrawal has removed that trigger's context, so focus is sent to the page's own
   * name instead of being left to fall back to the document body.
   */
  private focusHeading(): void {
    const heading = this.host.nativeElement.querySelector<HTMLElement>('h1');
    if (!heading) return;
    heading.setAttribute('tabindex', '-1');
    heading.focus();
  }
}
