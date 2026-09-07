import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CycleFilters, SettlementCycleResponse, SettlementsAdminService } from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  CellTemplate,
  DataTable,
  DataTableColumn,
  EntityDrawer,
  FilterBar,
  FilterDefinition,
  FilterValues,
  Modal,
  PageHeader,
  StatusBadge,
} from '@klarahome/ui-admin';
import { Alert, Button, Checkbox, Icon } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDate, tableDateTime, tableMoney } from '../../core/format';

/** The statuses a cycle moves through, from `SettlementCycleStatus`. */
const CYCLE_STATUSES = [
  { value: 'Open', label: 'Open — still accruing' },
  { value: 'Closed', label: 'Closed — waiting to be paid' },
  { value: 'Paid', label: 'Paid' },
] as const;

/**
 * Settlement periods, and what each one came to.
 *
 * **A cycle is a half-open period, and closing it is a decision rather than a clock tick.** The
 * scheduler closes periods on their timetable, but somebody can close one early — and the `force`
 * flag on that is the one control on this screen worth being careful with: **it overrides the
 * return hold**, which is the amount held back because a delivered order can still come back
 * within its return window. Forcing a close pays out money that may have to be clawed back, so the
 * override is a deliberate second act inside the dialogue and the button turns destructive when it
 * is on — rather than a flag on a form somebody sets once and forgets.
 *
 * **Every figure is an arithmetic identity, not a stored total.** `netPayable` is opening balance
 * plus sales less commission, fees, refunds and adjustments, less TCS and TDS — and the drawer
 * lays it out in that order so it can be checked by reading down the column. TCS and TDS are shown
 * separately because they are computed on **two different bases** (Step 18) and adding them
 * together produces a number that means nothing.
 */
@Component({
  selector: 'kh-settlement-cycles-page',
  imports: [
    Alert,
    Button,
    CellTemplate,
    Checkbox,
    DataTable,
    EntityDrawer,
    FilterBar,
    HasPermission,
    Icon,
    Modal,
    PageHeader,
    StatusBadge,
  ],
  template: `
    <kh-page-header
      heading="Settlement cycles"
      description="What each seller earned in a period, and what is payable on it."
    >
      <button
        khButton
        type="button"
        *khHasPermission="'settlements.settlement.manage'"
        (click)="closingAll.set(true)"
      >
        <kh-icon name="clock" size="sm" />
        Close the current period
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Cycles could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not take">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Settlement cycles"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="settlement-cycles"
      exportMode="page"
      emptyMessage="No settlement cycle matches these filters."
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

      <ng-template khCell="vendor" let-row>
        <button type="button" class="link" (click)="open(row)">
          {{ row.vendorName ?? row.vendorCode ?? row.vendorId }}
        </button>
        <span class="note">{{ tableDate(row.periodStart) }} → {{ tableDate(row.periodEnd) }}</span>
      </ng-template>

      <ng-template khCell="status" let-row>
        <kh-status-badge [status]="row.status" />
        @if (row.status === 'Closed' && !row.payoutBatchId) {
          <span class="note">Not in a payout run yet</span>
        }
      </ng-template>
    </kh-data-table>

    @if (selected(); as cycle) {
      <kh-entity-drawer
        heading="Settlement cycle"
        [subtitle]="cycle.vendorName ?? cycle.vendorCode"
        (closed)="selected.set(null)"
      >
        <p class="period">
          {{ tableDate(cycle.periodStart) }} → {{ tableDate(cycle.periodEnd) }} ·
          {{ cycle.entryCount }} ledger entr{{ cycle.entryCount === 1 ? 'y' : 'ies' }}
        </p>

        <dl class="ledger">
          <dt>Opening balance</dt>
          <dd>{{ money(cycle.openingBalance, cycle.currencyCode) }}</dd>

          <dt>Gross sales</dt>
          <dd>{{ money(cycle.grossSales, cycle.currencyCode) }}</dd>
          <dt class="sub">of which taxable</dt>
          <dd class="sub">{{ money(cycle.taxableSales, cycle.currencyCode) }}</dd>

          <dt>Commission</dt>
          <dd>−{{ money(cycle.totalCommission, cycle.currencyCode) }}</dd>
          <dt>Fees</dt>
          <dd>−{{ money(cycle.totalFees, cycle.currencyCode) }}</dd>
          <dt>Refunds</dt>
          <dd>−{{ money(cycle.totalRefunds, cycle.currencyCode) }}</dd>
          <dt>Adjustments</dt>
          <dd>{{ money(cycle.totalAdjustments, cycle.currencyCode) }}</dd>

          <dt>TCS — on net taxable supplies</dt>
          <dd>−{{ money(cycle.tcs, cycle.currencyCode) }}</dd>
          <dt>TDS — on gross sales</dt>
          <dd>−{{ money(cycle.tds, cycle.currencyCode) }}</dd>

          <dt>Already paid out</dt>
          <dd>−{{ money(cycle.totalPayouts, cycle.currencyCode) }}</dd>

          <dt class="grand">Net payable</dt>
          <dd class="grand">{{ money(cycle.netPayable, cycle.currencyCode) }}</dd>
        </dl>

        <p class="note">
          TCS and TDS sit on different bases and are never added together — one is under CGST s.52 and the
          other under s.194-O.
        </p>

        @if (cycle.closedAt) {
          <p class="note">Closed {{ dateTime(cycle.closedAt) }}</p>
        }
        @if (cycle.paidAt) {
          <p class="note">Paid {{ dateTime(cycle.paidAt) }}</p>
        }

        <div slot="footer">
          @if (cycle.status === 'Open') {
            <button
              khButton
              type="button"
              size="sm"
              variant="primary"
              *khHasPermission="'settlements.settlement.manage'"
              (click)="closingOne.set(cycle)"
            >
              Close this cycle
            </button>
          }
        </div>
      </kh-entity-drawer>
    }

    <kh-modal
      [open]="closingOne() !== null || closingAll()"
      [heading]="closingAll() ? 'Close the current period for every seller' : 'Close this settlement cycle'"
      (closed)="cancelClose()"
    >
      <p>{{ closeMessage() }}</p>

      <kh-checkbox
        label="Override the return hold"
        description="Pays out money that a return still inside its window could claw back. Recovering it afterwards is a debit against the seller."
        inputId="close-force"
        [checked]="force()"
        (checkedChange)="force.set($event)"
      />

      @if (force()) {
        <kh-alert tone="warning" heading="The hold is being overridden">
          Only do this when the returns for the period are known to be settled.
        </kh-alert>
      }

      <div slot="footer">
        <button khButton type="button" variant="tertiary" (click)="cancelClose()">Cancel</button>
        <button
          khButton
          type="button"
          [variant]="force() ? 'danger' : 'primary'"
          [disabled]="busy()"
          (click)="confirmClose()"
        >
          {{ busy() ? 'Closing…' : closingAll() ? 'Close the period' : 'Close the cycle' }}
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
      padding: 0;
      border: none;
      background: none;
      color: var(--color-link);
      font: inherit;
      font-weight: var(--weight-medium);
      text-align: start;
      cursor: pointer;
    }

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .period {
      margin: 0 0 var(--space-4);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .ledger {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: var(--space-2) var(--space-4);
      margin: 0 0 var(--space-4);
    }

    .ledger dt {
      color: var(--color-text-muted);
    }

    .ledger dd {
      margin: 0;
      font-variant-numeric: tabular-nums;
      text-align: end;
    }

    .ledger .sub {
      padding-inline-start: var(--space-4);
      font-size: var(--text-sm);
    }

    .ledger .grand {
      padding-block-start: var(--space-2);
      border-block-start: 1px solid var(--color-border);
      font-weight: var(--weight-bold);
      color: var(--color-text);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettlementCyclesPage {
  private readonly settlements = inject(SettlementsAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly tableDate = tableDate;
  protected readonly dateTime = tableDateTime;

  protected readonly list = this.settlements.cycles();
  protected readonly values = signal<FilterValues>({});
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected readonly selected = signal<SettlementCycleResponse | null>(null);
  protected readonly closingOne = signal<SettlementCycleResponse | null>(null);
  protected readonly closingAll = signal(false);
  protected readonly force = signal(false);

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly closeMessage = computed(() =>
    this.force()
      ? 'The return hold is overridden. Money that a return could still claw back becomes payable, and recovering it afterwards is a debit against the seller.'
      : 'The period is drawn to a close and what is payable is computed. The return hold keeps back what a return inside its window could still claw back.',
  );

  protected readonly rowKey = (row: SettlementCycleResponse) => row.id;
  protected readonly rowLabel = (row: SettlementCycleResponse) =>
    row.vendorName ?? row.vendorCode ?? row.vendorId;

  protected readonly columns: readonly DataTableColumn<SettlementCycleResponse>[] = [
    { key: 'vendor', label: 'Seller and period', kind: 'custom' },
    { key: 'status', label: 'Status', kind: 'custom', width: '14rem' },
    {
      key: 'grossSales',
      label: 'Gross sales',
      kind: 'number',
      value: (row) => tableMoney(row.grossSales, row.currencyCode),
    },
    {
      key: 'totalCommission',
      label: 'Commission',
      kind: 'number',
      value: (row) => tableMoney(row.totalCommission, row.currencyCode),
    },
    {
      key: 'netPayable',
      label: 'Net payable',
      kind: 'number',
      value: (row) => tableMoney(row.netPayable, row.currencyCode),
    },
    {
      key: 'tcs',
      label: 'TCS',
      kind: 'number',
      value: (row) => tableMoney(row.tcs, row.currencyCode),
      hiddenByDefault: true,
    },
    {
      key: 'tds',
      label: 'TDS',
      kind: 'number',
      value: (row) => tableMoney(row.tds, row.currencyCode),
      hiddenByDefault: true,
    },
    {
      key: 'closedAt',
      label: 'Closed',
      kind: 'date',
      value: (row) => tableDateTime(row.closedAt),
      hiddenByDefault: true,
    },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'status',
      label: 'Status',
      kind: 'select',
      options: CYCLE_STATUSES.map((entry) => ({ value: entry.value, label: entry.label })),
    },
    { key: 'from', label: 'Period from', kind: 'date' },
    { key: 'to', label: 'Period to', kind: 'date' },
  ];

  constructor() {
    this.list.load();
  }

  protected money(amount: number, currency: string): string {
    return tableMoney(amount, currency);
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: CycleFilters = {
      status: values['status'],
      from: values['from'],
      to: values['to'],
    };
    this.list.setFilters(filters);
  }

  protected open(row: SettlementCycleResponse): void {
    this.selected.set(row);
  }

  protected cancelClose(): void {
    this.closingOne.set(null);
    this.closingAll.set(false);
    this.force.set(false);
  }

  /** One dialogue, two operations: close one seller's cycle, or close the period for everybody. */
  protected confirmClose(): void {
    if (this.closingAll()) this.closeAll();
    else this.closeOne();
  }

  private closeOne(): void {
    const cycle = this.closingOne();
    if (!cycle) return;

    this.busy.set(true);
    this.actionError.set(null);

    this.settlements.closeCycle(cycle.id, this.force()).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.closingOne.set(null);
        this.force.set(false);
        this.selected.set(saved);
        this.toasts.success('Cycle closed.');
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.closingOne.set(null);
        this.actionError.set(describeError(error, 'That cycle could not be closed.'));
      },
    });
  }

  private closeAll(): void {
    this.busy.set(true);
    this.actionError.set(null);

    this.settlements.closePeriod(null, this.force()).subscribe({
      next: () => {
        this.busy.set(false);
        this.closingAll.set(false);
        this.force.set(false);
        this.toasts.success('The period has been closed.');
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.closingAll.set(false);
        this.actionError.set(describeError(error, 'The period could not be closed.'));
      },
    });
  }
}
