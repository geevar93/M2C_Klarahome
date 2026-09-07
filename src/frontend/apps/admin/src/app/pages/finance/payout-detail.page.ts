import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import {
  PayoutBatchResponse,
  PayoutItemResponse,
  SettlementsAdminService,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  CellTemplate,
  ConfirmDialog,
  DataTable,
  DataTableColumn,
  PageHeader,
  StatusBadge,
} from '@klarahome/ui-admin';
import { Alert, Button, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime, tableMoney } from '../../core/format';

/** What each edge does, in the words of the person taking it. */
const ACTION_LABELS: Readonly<Record<string, string>> = {
  Approved: 'Approve this run',
  Processing: 'Send it to the provider',
  Cancelled: 'Cancel the run',
};

/**
 * One payout run: who is in it, and what has become of each payment.
 *
 * **The buttons are `nextStatuses`.** The batch carries the edges the server will accept from where
 * it is, so this screen never decides that an approved batch may be processed — it reads that it
 * may. Approving is refused for whoever created the run (Step 18's maker–checker, enforced in three
 * places), and that refusal arrives here as an ordinary error with the API's own wording.
 *
 * **Money leaves at "process", and only then.** Approving records a second person's agreement;
 * processing hands the batch to the payout provider. They are separate because they are separate
 * decisions, and because a batch approved on Friday is often sent on Monday.
 *
 * **A partly failed batch is the normal bad outcome**, not an error: one seller's account is
 * closed, the rest are paid. So the items table shows each payment's own status and its failure
 * reason, and the batch's `settledAmount` is what actually landed rather than what was attempted.
 * The UTR is the number a seller will quote when they ring.
 */
@Component({
  selector: 'kh-payout-detail-page',
  imports: [
    Alert,
    Button,
    CellTemplate,
    ConfirmDialog,
    DataTable,
    HasPermission,
    PageHeader,
    Skeleton,
    StatusBadge,
  ],
  template: `
    <kh-page-header
      [heading]="batch()?.reference ?? 'Payout run'"
      [description]="subtitle()"
      [crumbs]="[{ label: 'Payouts', path: '/payouts' }]"
    >
      @if (batch(); as current) {
        <kh-status-badge [status]="current.status" />

        <ng-container *khHasPermission="['settlements.payout.approve', 'settlements.payout.manage']">
          @for (status of current.nextStatuses; track status) {
            <button
              khButton
              type="button"
              size="sm"
              [variant]="status === 'Cancelled' ? 'danger' : 'primary'"
              [disabled]="busy()"
              (click)="pending.set(status)"
            >
              {{ actionLabel(status) }}
            </button>
          }
        </ng-container>
      }
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="This run could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="18rem" />
    } @else if (batch(); as current) {
      @if (actionError(); as message) {
        <kh-alert tone="danger" heading="That did not take">{{ message }}</kh-alert>
      }

      @if (!current.provider) {
        <kh-alert tone="warning" heading="No payout provider is configured">
          The run can be created and approved, but nothing will actually be sent until a provider is
          configured. Its items will report as unsent rather than as paid.
        </kh-alert>
      }

      <section class="summary">
        <dl>
          <dt>Requested by</dt>
          <dd>{{ current.requestedBy ?? '—' }}</dd>
          <dd class="note">{{ dateTime(current.requestedAt) }}</dd>

          <dt>Approved by</dt>
          <dd>{{ current.approvedBy ?? 'Not yet' }}</dd>
          <dd class="note">{{ current.approvedAt ? dateTime(current.approvedAt) : '' }}</dd>

          <dt>Sent</dt>
          <dd>{{ current.processedAt ? dateTime(current.processedAt) : 'Not yet' }}</dd>
          <dd class="note">{{ current.providerBatchId ?? '' }}</dd>

          <dt>Amount</dt>
          <dd>{{ money(current.totalAmount, current.currencyCode) }}</dd>
          <dd class="note">{{ money(current.settledAmount, current.currencyCode) }} actually settled</dd>
        </dl>

        @if (current.cancelledReason) {
          <p class="note">Cancelled: {{ current.cancelledReason }}</p>
        }
      </section>

      <kh-data-table
        label="Payments in this run"
        [columns]="columns"
        [rows]="current.items"
        [rowKey]="rowKey"
        [rowLabel]="rowLabel"
        exportMode="page"
        emptyMessage="This run has no payments in it."
      >
        <ng-template khCell="vendor" let-row>
          <span class="name">{{ row.vendorName ?? row.vendorCode ?? row.vendorId }}</span>
          @if (row.destinationLast4) {
            <span class="note">••••{{ row.destinationLast4 }}</span>
          } @else {
            <span class="note warn">No verified account on file</span>
          }
        </ng-template>

        <ng-template khCell="status" let-row>
          <kh-status-badge [status]="row.status" />
          @if (row.failureReason) {
            <span class="note warn">{{ row.failureReason }}</span>
          } @else if (row.utr) {
            <span class="note">UTR {{ row.utr }}</span>
          }
        </ng-template>
      </kh-data-table>
    }

    <kh-confirm-dialog
      [open]="pending() !== null"
      [heading]="actionLabel(pending() ?? '')"
      [message]="pendingMessage()"
      [confirmLabel]="actionLabel(pending() ?? '')"
      [tone]="pending() === 'Cancelled' ? 'danger' : 'warning'"
      [requireReason]="pending() === 'Cancelled'"
      [busy]="busy()"
      (confirmed)="run($event.reason)"
      (cancelled)="pending.set(null)"
    />
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .summary {
      margin-block-end: var(--space-4);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .summary dl {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: var(--space-1) var(--space-4);
      margin: 0;
    }

    .summary dt {
      grid-column: 1;
      color: var(--color-text-muted);
    }

    .summary dd {
      grid-column: 2;
      margin: 0;
    }

    .name {
      display: block;
      font-weight: var(--weight-medium);
    }

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .note.warn {
      color: var(--color-warning);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PayoutDetailPage {
  private readonly settlements = inject(SettlementsAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly toasts = inject(ToastService);

  protected readonly dateTime = tableDateTime;
  private readonly id = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly batch = signal<PayoutBatchResponse | null>(null);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly pending = signal<string | null>(null);

  protected readonly subtitle = computed(() => {
    const current = this.batch();
    if (!current) return null;
    return `${current.vendorCount} seller${current.vendorCount === 1 ? '' : 's'} · ${tableMoney(current.totalAmount, current.currencyCode)}`;
  });

  protected readonly pendingMessage = computed(() => {
    switch (this.pending()) {
      case 'Approved':
        return 'You are recording your agreement that these payments should be made. Whoever created the run cannot approve it, and the API will refuse it if that is you.';
      case 'Processing':
        return 'The run is handed to the payout provider and money leaves. Individual payments can still fail; the run reports what actually settled.';
      case 'Cancelled':
        return 'The run is abandoned. The cycles in it go back to waiting and can be gathered into another run.';
      default:
        return 'The run moves on.';
    }
  });

  protected readonly rowKey = (row: PayoutItemResponse) => row.id;
  protected readonly rowLabel = (row: PayoutItemResponse) => row.vendorName ?? row.vendorCode ?? row.vendorId;

  protected readonly columns: readonly DataTableColumn<PayoutItemResponse>[] = [
    { key: 'vendor', label: 'Seller', kind: 'custom' },
    {
      key: 'amount',
      label: 'Amount',
      kind: 'number',
      value: (row) => tableMoney(row.amount, row.currencyCode),
      width: '10rem',
    },
    { key: 'status', label: 'Status', kind: 'custom', width: '16rem' },
    {
      key: 'sentAt',
      label: 'Sent',
      kind: 'date',
      value: (row) => tableDateTime(row.sentAt),
      hiddenByDefault: true,
    },
    {
      key: 'settledAt',
      label: 'Settled',
      kind: 'date',
      value: (row) => tableDateTime(row.settledAt),
      hiddenByDefault: true,
    },
    {
      key: 'providerPayoutId',
      label: 'Provider reference',
      value: (row) => row.providerPayoutId ?? '',
      hiddenByDefault: true,
    },
  ];

  constructor() {
    this.load();
  }

  protected money(amount: number, currency: string): string {
    return tableMoney(amount, currency);
  }

  protected actionLabel(status: string): string {
    return ACTION_LABELS[status] ?? status;
  }

  protected run(reason: string): void {
    const status = this.pending();
    if (!status || this.busy()) return;

    this.busy.set(true);
    this.actionError.set(null);

    const request =
      status === 'Approved'
        ? this.settlements.approvePayoutBatch(this.id)
        : status === 'Processing'
          ? this.settlements.processPayoutBatch(this.id)
          : this.settlements.cancelPayoutBatch(this.id, reason.trim() || null);

    request.subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.pending.set(null);
        this.batch.set(saved);
        this.toasts.success(`${this.actionLabel(status)} — done.`);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.pending.set(null);
        this.actionError.set(describeError(error, 'That was refused.'));
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.settlements.payoutBatch(this.id).subscribe({
      next: (batch) => {
        this.loading.set(false);
        this.batch.set(batch);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'That run could not be loaded.'));
      },
    });
  }
}
