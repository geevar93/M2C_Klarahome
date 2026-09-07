import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  PayoutBatchResponse,
  PayoutFilters,
  SettlementCycleResponse,
  SettlementsAdminService,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  CellTemplate,
  DataTable,
  DataTableColumn,
  FilterBar,
  FilterDefinition,
  FilterValues,
  Modal,
  PageHeader,
  StatusBadge,
} from '@klarahome/ui-admin';
import { Alert, Button, Checkbox, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDate, tableDateTime, tableMoney } from '../../core/format';

/** The statuses a batch moves through, from `PayoutBatchStatus`. */
const BATCH_STATUSES = [
  { value: 'Draft', label: 'Draft — awaiting approval' },
  { value: 'Approved', label: 'Approved — not sent yet' },
  { value: 'Processing', label: 'With the provider' },
  { value: 'Completed', label: 'Completed' },
  { value: 'PartiallyFailed', label: 'Partly failed' },
  { value: 'Failed', label: 'Failed' },
  { value: 'Cancelled', label: 'Cancelled' },
] as const;

/**
 * Payout runs.
 *
 * **The whole screen is shaped by maker–checker.** Creating a batch and approving it are two acts
 * by two people, refused in the handler, in the aggregate and by a database `CHECK` (Step 18) — so
 * this screen never offers "pay these sellers" as one button, and the person who has just created
 * a batch will find approving it refused. That is the control working, not a bug, and the refusal
 * is shown as the API words it.
 *
 * **Creating a batch with nothing selected gathers every closed, unpaid cycle.** That is the
 * ordinary weekly run, and it is the default the dialogue offers; selecting cycles is for the case
 * where one seller is being paid out of band.
 *
 * `nextStatuses` on the batch carries what it will currently accept, so the actions on a row come
 * off the batch itself rather than from a table of statuses kept here.
 */
@Component({
  selector: 'kh-payouts-page',
  imports: [
    Alert,
    Button,
    CellTemplate,
    Checkbox,
    DataTable,
    FilterBar,
    HasPermission,
    Icon,
    Modal,
    PageHeader,
    RouterLink,
    Skeleton,
    StatusBadge,
  ],
  template: `
    <kh-page-header heading="Payouts" description="What has been sent to sellers, and what is waiting to be.">
      <button
        khButton
        type="button"
        variant="primary"
        *khHasPermission="'settlements.payout.manage'"
        (click)="startCreate()"
      >
        <kh-icon name="plus" size="sm" />
        New payout run
      </button>
    </kh-page-header>

    <kh-alert tone="info" heading="Two people, two steps">
      Whoever creates a run may not approve it. The API refuses it, and so does the database.
    </kh-alert>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Payout runs could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not take">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Payout runs"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="payout-batches"
      exportMode="page"
      emptyMessage="No payout run matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        [searchable]="false"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="reference" let-row>
        <a class="link" [routerLink]="['/payouts', row.id]">{{ row.reference }}</a>
        <span class="note">
          {{ row.vendorCount }} seller{{ row.vendorCount === 1 ? '' : 's' }} · requested
          {{ dateTime(row.requestedAt) }}
        </span>
      </ng-template>

      <ng-template khCell="status" let-row>
        <kh-status-badge [status]="row.status" />
        @if (row.provider) {
          <span class="note">via {{ row.provider }}</span>
        } @else {
          <span class="note">No payout provider configured</span>
        }
      </ng-template>
    </kh-data-table>

    <kh-modal [open]="creating()" heading="New payout run" width="42rem" (closed)="creating.set(false)">
      @if (createError(); as message) {
        <kh-alert tone="danger" heading="It could not be created">{{ message }}</kh-alert>
      }

      <p class="hint">
        A run gathers closed cycles that have not been paid. Leave everything unticked to take all of them,
        which is the ordinary weekly run.
      </p>

      @if (loadingCycles()) {
        <kh-skeleton height="8rem" />
      } @else if (payableCycles().length === 0) {
        <p class="empty">No closed, unpaid cycle is waiting. Close a period first.</p>
      } @else {
        <ul class="cycles">
          @for (cycle of payableCycles(); track cycle.id) {
            <li>
              <kh-checkbox
                [label]="cycle.vendorName ?? cycle.vendorCode ?? cycle.vendorId"
                [description]="cycleDescription(cycle)"
                [inputId]="'cycle-' + cycle.id"
                [checked]="selected().includes(cycle.id)"
                (checkedChange)="toggleCycle(cycle.id, $event)"
              />
            </li>
          }
        </ul>
        <p class="note">
          {{ selected().length === 0 ? 'Taking every cycle listed' : selected().length + ' selected' }}
          · {{ money(selectedTotal(), currency()) }}
        </p>
      }

      <div slot="footer">
        <button khButton type="button" variant="tertiary" (click)="creating.set(false)">Cancel</button>
        <button
          khButton
          type="button"
          variant="primary"
          [disabled]="busy() || payableCycles().length === 0"
          (click)="create()"
        >
          {{ busy() ? 'Creating…' : 'Create the run' }}
        </button>
      </div>
    </kh-modal>
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .link {
      display: block;
      font-weight: var(--weight-medium);
    }

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .hint {
      margin: 0 0 var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .empty {
      margin: 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .cycles {
      max-block-size: 20rem;
      overflow-y: auto;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .cycles li {
      padding-block: var(--space-1);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PayoutsPage {
  private readonly settlements = inject(SettlementsAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly dateTime = tableDateTime;

  protected readonly list = this.settlements.payoutBatches();
  protected readonly values = signal<FilterValues>({});
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected readonly creating = signal(false);
  protected readonly createError = signal<string | null>(null);
  protected readonly selected = signal<readonly string[]>([]);

  private readonly closedCycles = this.settlements.cycles({ status: 'Closed' }, 100);
  protected readonly loadingCycles = this.closedCycles.loading;

  /**
   * The cycles a run could take.
   *
   * Filtered again here for `payoutBatchId === null`: a cycle can be closed *and* already gathered
   * into a run that nobody has approved yet, and offering it twice would produce a second run that
   * pays the same period.
   */
  protected readonly payableCycles = computed(() =>
    this.closedCycles.rows().filter((cycle) => cycle.payoutBatchId === null),
  );

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly currency = computed(() => this.payableCycles()[0]?.currencyCode ?? 'INR');

  /** What the run will send: the ticked cycles, or all of them when none is ticked. */
  protected readonly selectedTotal = computed(() => {
    const chosen = this.selected();
    const cycles = this.payableCycles();
    const counted = chosen.length === 0 ? cycles : cycles.filter((cycle) => chosen.includes(cycle.id));
    return counted.reduce((total, cycle) => total + cycle.netPayable, 0);
  });

  protected readonly rowKey = (row: PayoutBatchResponse) => row.id;
  protected readonly rowLabel = (row: PayoutBatchResponse) => row.reference;

  protected readonly columns: readonly DataTableColumn<PayoutBatchResponse>[] = [
    { key: 'reference', label: 'Run', kind: 'custom' },
    { key: 'status', label: 'Status', kind: 'custom', width: '15rem' },
    {
      key: 'totalAmount',
      label: 'Amount',
      kind: 'number',
      value: (row) => tableMoney(row.totalAmount, row.currencyCode),
    },
    {
      key: 'settledAmount',
      label: 'Settled',
      kind: 'number',
      value: (row) => tableMoney(row.settledAmount, row.currencyCode),
    },
    {
      key: 'approvedBy',
      label: 'Approved by',
      value: (row) => row.approvedBy ?? 'Not yet',
      hiddenByDefault: true,
    },
    {
      key: 'completedAt',
      label: 'Completed',
      kind: 'date',
      value: (row) => tableDateTime(row.completedAt),
      hiddenByDefault: true,
    },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'status',
      label: 'Status',
      kind: 'select',
      options: BATCH_STATUSES.map((entry) => ({ value: entry.value, label: entry.label })),
    },
    { key: 'from', label: 'Requested from', kind: 'date' },
    { key: 'to', label: 'Requested to', kind: 'date' },
  ];

  constructor() {
    this.list.load();
  }

  protected money(amount: number, currency: string): string {
    return tableMoney(amount, currency);
  }

  protected cycleDescription(cycle: SettlementCycleResponse): string {
    return `${tableDate(cycle.periodStart)} → ${tableDate(cycle.periodEnd)} · ${tableMoney(cycle.netPayable, cycle.currencyCode)}`;
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: PayoutFilters = {
      status: values['status'],
      from: values['from'],
      to: values['to'],
    };
    this.list.setFilters(filters);
  }

  protected toggleCycle(id: string, on: boolean): void {
    this.selected.update((current) =>
      on ? [...new Set([...current, id])] : current.filter((entry) => entry !== id),
    );
  }

  protected startCreate(): void {
    this.createError.set(null);
    this.selected.set([]);
    this.creating.set(true);
    this.closedCycles.load();
  }

  protected create(): void {
    if (this.busy()) return;

    this.busy.set(true);
    this.createError.set(null);

    const chosen = this.selected();
    this.settlements.createPayoutBatch(chosen.length > 0 ? chosen : null).subscribe({
      next: (batch) => {
        this.busy.set(false);
        this.creating.set(false);
        this.toasts.success(`Run ${batch.reference} created. Somebody else has to approve it.`);
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.createError.set(describeError(error, 'The run could not be created.'));
      },
    });
  }
}
