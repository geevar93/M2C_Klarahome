import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  AdjustmentBody,
  DocumentPrintService,
  LedgerEntryResponse,
  LedgerFilters,
  LedgerStatementResponse,
  PlatformRevenueResponse,
  SettlementsAdminService,
  StatutoryExtractResponse,
  VendorBalanceResponse,
} from '@klarahome/data-access-admin';
import { HasPermission, SessionStore } from '@klarahome/data-access-auth';
import {
  CellTemplate,
  DataTable,
  DataTableColumn,
  FilterBar,
  FilterDefinition,
  FilterValues,
  KpiCard,
  Modal,
  PageHeader,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Control, Field, Icon } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDate, tableDateTime, tableMoney } from '../../core/format';

/** The entry types the ledger records, from `LedgerEntryTypes`. */
const ENTRY_TYPES = [
  { value: 'sale', label: 'Sale' },
  { value: 'commission', label: 'Commission' },
  { value: 'platform_tax', label: 'Tax on platform charges' },
  { value: 'platform_fee', label: 'Marketplace fee' },
  { value: 'payment_fee', label: 'Gateway fee' },
  { value: 'shipping_fee', label: 'Freight' },
  { value: 'refund', label: 'Refund' },
  { value: 'refund_commission_reversal', label: 'Commission reversed' },
  { value: 'tcs', label: 'TCS' },
  { value: 'tds', label: 'TDS' },
  { value: 'adjustment', label: 'Adjustment' },
  { value: 'payout', label: 'Payout' },
] as const;

/**
 * The vendor ledger, and what can be read off it.
 *
 * **A balance is `Σ credits − Σ debits`, and there is no column for it.** Nothing on this screen
 * sets a balance; the one write it offers is an adjustment, which is a signed row carrying its
 * reason. That is the entire point of an append-only ledger and it is why "correcting" a seller's
 * balance means explaining the correction.
 *
 * **A statement is not the entry list.** The entries are every movement across every seller; the
 * statement is one seller's opening balance, their movements and their closing balance for a
 * period, which is the document an accountant reconciles against. Both are here because "show me
 * the entries" and "send me the statement" are asked by different people about the same rows.
 *
 * **The CSV exports go through `DocumentPrintService`.** Both statement endpoints stream bytes
 * behind the bearer token rather than answering a signed URL — the same shape of problem as the
 * shipping label, and recorded in `PARKING_LOT.md` under the same row.
 *
 * The platform revenue panel is the store's own side of the same rows: what it took, what it
 * charged, and what it has paid out.
 */
@Component({
  selector: 'kh-ledger-page',
  imports: [
    Alert,
    Badge,
    Button,
    CellTemplate,
    Control,
    DataTable,
    Field,
    FilterBar,
    HasPermission,
    Icon,
    KpiCard,
    Modal,
    PageHeader,
  ],
  template: `
    <kh-page-header heading="Ledger" description="Every movement of money between the store and its sellers.">
      <button
        khButton
        type="button"
        *khHasPermission="'settlements.settlement.manage'"
        (click)="adjusting.set(true)"
      >
        <kh-icon name="edit" size="sm" />
        Post an adjustment
      </button>
    </kh-page-header>

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not take">{{ message }}</kh-alert>
    }

    @if (isVendor()) {
      <kh-alert tone="info" heading="Your own ledger">
        These are your movements only. The API scopes them to your seller account.
      </kh-alert>
    }

    <div class="statement">
      <div class="row">
        <kh-field label="Seller id" for="ledger-vendor" [optional]="isVendor()">
          <input
            khControl
            id="ledger-vendor"
            type="text"
            [value]="vendorId()"
            (input)="vendorId.set($any($event.target).value)"
          />
        </kh-field>
        <kh-field label="From" for="ledger-from" [optional]="true">
          <input
            khControl
            id="ledger-from"
            type="date"
            [value]="from()"
            (input)="from.set($any($event.target).value)"
          />
        </kh-field>
        <kh-field label="To" for="ledger-to" [optional]="true">
          <input
            khControl
            id="ledger-to"
            type="date"
            [value]="to()"
            (input)="to.set($any($event.target).value)"
          />
        </kh-field>
        <button khButton type="button" [disabled]="loadingStatement()" (click)="loadStatement()">
          {{ loadingStatement() ? 'Loading…' : 'Show the statement' }}
        </button>
        <button
          khButton
          type="button"
          variant="tertiary"
          [disabled]="exporting()"
          (click)="exportStatement()"
        >
          {{ exporting() ? 'Preparing…' : 'Download CSV' }}
        </button>
      </div>

      @if (statementError(); as message) {
        <kh-alert tone="danger" heading="The statement could not be loaded">{{ message }}</kh-alert>
      }

      @if (balance(); as current) {
        <div class="tiles">
          <kh-kpi-card label="Balance now" [value]="money(current.currentBalance, current.currencyCode)" />
          <kh-kpi-card
            label="Not yet settled"
            [value]="money(current.unsettledBalance, current.currencyCode)"
            hint="Earned, but the period is still open"
          />
          <kh-kpi-card
            label="Awaiting a payout"
            [value]="money(current.awaitingPayout, current.currencyCode)"
            hint="Closed and payable"
          />
          <kh-kpi-card label="Last paid" [value]="current.lastPaidAt ? dateTime(current.lastPaidAt) : '—'" />
        </div>
      }

      @if (statement(); as current) {
        <div class="totals">
          <h2>{{ tableDate(current.from) }} → {{ tableDate(current.to) }}</h2>
          <dl>
            <dt>Opening balance</dt>
            <dd>{{ money(current.openingBalance, current.currencyCode) }}</dd>
            @for (total of current.totals; track total.entryType) {
              <dt>{{ entryLabel(total.entryType) }} ({{ total.count }})</dt>
              <dd>{{ money(total.signedAmount, current.currencyCode) }}</dd>
            }
            <dt class="grand">Closing balance</dt>
            <dd class="grand">{{ money(current.closingBalance, current.currencyCode) }}</dd>
          </dl>
        </div>
      }
    </div>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Entries could not be loaded">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Ledger entries"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="ledger-entries"
      exportMode="page"
      emptyMessage="No ledger entry matches these filters."
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

      <ng-template khCell="entryType" let-row>
        <span class="name">{{ entryLabel(row.entryType) }}</span>
        <span class="note">{{ row.referenceType }}{{ row.note ? ' · ' + row.note : '' }}</span>
      </ng-template>

      <ng-template khCell="signedAmount" let-row>
        <kh-badge [tone]="row.direction === 'Credit' ? 'success' : 'neutral'">
          {{ row.direction }}
        </kh-badge>
        <span class="amount">{{ money(row.signedAmount, row.currencyCode) }}</span>
      </ng-template>
    </kh-data-table>

    <section class="panels" *khHasPermission="'settlements.settlement.read'">
      <div class="panel">
        <header>
          <h2>What the platform kept</h2>
          <button khButton type="button" size="sm" [disabled]="loadingRevenue()" (click)="loadRevenue()">
            {{ loadingRevenue() ? 'Loading…' : 'Work it out' }}
          </button>
        </header>
        <p class="hint">For the period in the statement controls above.</p>

        @if (revenue(); as current) {
          <dl>
            <dt>Gross merchandise value</dt>
            <dd>{{ money(current.grossMerchandiseValue, current.currencyCode) }}</dd>
            <dt>Commission</dt>
            <dd>{{ money(current.commission, current.currencyCode) }}</dd>
            <dt>Marketplace fee</dt>
            <dd>{{ money(current.platformFee, current.currencyCode) }}</dd>
            <dt>Gateway fees</dt>
            <dd>{{ money(current.paymentFee, current.currencyCode) }}</dd>
            <dt>Freight</dt>
            <dd>{{ money(current.shippingFee, current.currencyCode) }}</dd>
            <dt>Refunds</dt>
            <dd>−{{ money(current.refunds, current.currencyCode) }}</dd>
            <dt>Charges reversed</dt>
            <dd>−{{ money(current.chargesReversed, current.currencyCode) }}</dd>
            <dt class="grand">Net revenue</dt>
            <dd class="grand">{{ money(current.netRevenue, current.currencyCode) }}</dd>
            <dt>Paid out to sellers</dt>
            <dd>{{ money(current.paidOut, current.currencyCode) }}</dd>
          </dl>
        }
      </div>

      <div class="panel">
        <header>
          <h2>TCS and TDS</h2>
          <div class="header-actions">
            <button
              khButton
              type="button"
              size="sm"
              [disabled]="loadingStatutory()"
              (click)="loadStatutory()"
            >
              {{ loadingStatutory() ? 'Loading…' : 'Work it out' }}
            </button>
            <button
              khButton
              type="button"
              size="sm"
              variant="tertiary"
              [disabled]="exporting()"
              (click)="exportStatutory()"
            >
              CSV
            </button>
          </div>
        </header>
        <p class="hint">
          Two taxes on two different bases: TCS on net taxable supplies under CGST s.52, TDS on gross sales
          under s.194-O. They are never added together.
        </p>

        @if (statutory(); as current) {
          <dl>
            <dt>Total TCS</dt>
            <dd>{{ money(current.totalTcs, current.currencyCode) }}</dd>
            <dt>Total TDS</dt>
            <dd>{{ money(current.totalTds, current.currencyCode) }}</dd>
            <dt>Sellers</dt>
            <dd>{{ current.rows.length }}</dd>
          </dl>
        }
      </div>
    </section>

    <kh-modal [open]="adjusting()" heading="Post an adjustment" (closed)="adjusting.set(false)">
      @if (adjustError(); as message) {
        <kh-alert tone="danger" heading="It could not be posted">{{ message }}</kh-alert>
      }

      <p class="hint">
        An adjustment is a signed row on the ledger. It is the only way a balance moves other than by trading,
        and the reason is what somebody reads a year from now.
      </p>

      <kh-field label="Seller id" for="adjust-vendor">
        <input
          khControl
          id="adjust-vendor"
          type="text"
          [value]="adjustVendorId()"
          (input)="adjustVendorId.set($any($event.target).value)"
        />
      </kh-field>

      <kh-field label="Direction" for="adjust-direction">
        <select
          khControl
          id="adjust-direction"
          [value]="adjustDirection()"
          (change)="adjustDirection.set($any($event.target).value)"
        >
          <option value="Credit">Credit — the seller is owed more</option>
          <option value="Debit">Debit — the seller owes the platform</option>
        </select>
      </kh-field>

      <kh-field label="Amount" for="adjust-amount">
        <input
          khControl
          id="adjust-amount"
          type="number"
          min="0"
          step="0.01"
          [value]="adjustAmount()"
          (input)="adjustAmount.set($any($event.target).value)"
        />
      </kh-field>

      <kh-field label="Reason" for="adjust-reason" hint="Required. It goes on the row and on the statement.">
        <textarea
          khControl
          id="adjust-reason"
          rows="3"
          [value]="adjustReason()"
          (input)="adjustReason.set($any($event.target).value)"
        ></textarea>
      </kh-field>

      <div slot="footer">
        <button khButton type="button" variant="tertiary" (click)="adjusting.set(false)">Cancel</button>
        <button khButton type="button" variant="primary" [disabled]="busy()" (click)="postAdjustment()">
          {{ busy() ? 'Posting…' : 'Post it' }}
        </button>
      </div>
    </kh-modal>
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .statement {
      margin-block-end: var(--space-4);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .row {
      display: flex;
      gap: var(--space-3);
      align-items: flex-end;
      flex-wrap: wrap;
    }

    .row > kh-field {
      flex: 1 1 9rem;
    }

    .tiles {
      display: grid;
      gap: var(--space-3);
      grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
      margin-block-start: var(--space-4);
    }

    .totals {
      margin-block-start: var(--space-4);
      padding-block-start: var(--space-4);
      border-block-start: 1px solid var(--color-border);
    }

    h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
    }

    dl {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: var(--space-1) var(--space-4);
      margin: 0;
    }

    dt {
      color: var(--color-text-muted);
    }

    dd {
      margin: 0;
      font-variant-numeric: tabular-nums;
      text-align: end;
    }

    .grand {
      padding-block-start: var(--space-2);
      border-block-start: 1px solid var(--color-border);
      font-weight: var(--weight-bold);
      color: var(--color-text);
    }

    .panels {
      display: grid;
      gap: var(--space-4);
      grid-template-columns: repeat(auto-fit, minmax(20rem, 1fr));
      margin-block-start: var(--space-4);
    }

    .panel {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .panel header {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      justify-content: space-between;
    }

    .header-actions {
      display: flex;
      gap: var(--space-2);
    }

    .hint {
      margin: 0 0 var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
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

    .amount {
      display: block;
      font-variant-numeric: tabular-nums;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LedgerPage {
  private readonly settlements = inject(SettlementsAdminService);
  private readonly documents = inject(DocumentPrintService);
  private readonly session = inject(SessionStore);
  private readonly toasts = inject(ToastService);

  protected readonly tableDate = tableDate;
  protected readonly dateTime = tableDateTime;

  protected readonly list = this.settlements.ledger();
  protected readonly values = signal<FilterValues>({});
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected readonly vendorId = signal(this.session.session()?.vendorId ?? '');
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly statement = signal<LedgerStatementResponse | null>(null);
  protected readonly balance = signal<VendorBalanceResponse | null>(null);
  protected readonly loadingStatement = signal(false);
  protected readonly statementError = signal<string | null>(null);
  protected readonly exporting = signal(false);

  protected readonly revenue = signal<PlatformRevenueResponse | null>(null);
  protected readonly loadingRevenue = signal(false);
  protected readonly statutory = signal<StatutoryExtractResponse | null>(null);
  protected readonly loadingStatutory = signal(false);

  protected readonly adjusting = signal(false);
  protected readonly adjustError = signal<string | null>(null);
  protected readonly adjustVendorId = signal('');
  protected readonly adjustDirection = signal('Credit');
  protected readonly adjustAmount = signal('');
  protected readonly adjustReason = signal('');

  protected readonly isVendor = computed(() => (this.session.session()?.vendorId ?? null) !== null);

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: LedgerEntryResponse) => row.id;

  protected readonly columns: readonly DataTableColumn<LedgerEntryResponse>[] = [
    {
      key: 'occurredAt',
      label: 'When',
      kind: 'date',
      value: (row) => tableDateTime(row.occurredAt),
      width: '13rem',
    },
    { key: 'entryType', label: 'What', kind: 'custom' },
    { key: 'signedAmount', label: 'Amount', kind: 'custom', width: '12rem' },
    {
      key: 'taxableValue',
      label: 'Taxable value',
      kind: 'number',
      value: (row) => tableMoney(row.taxableValue, row.currencyCode),
      hiddenByDefault: true,
    },
    {
      key: 'vendorId',
      label: 'Seller',
      value: (row) => row.vendorId,
      hiddenByDefault: true,
    },
    {
      key: 'referenceId',
      label: 'Reference',
      value: (row) => row.referenceId ?? '',
      hiddenByDefault: true,
    },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'entryType',
      label: 'Kind',
      kind: 'select',
      options: ENTRY_TYPES.map((entry) => ({ value: entry.value, label: entry.label })),
    },
    { key: 'from', label: 'From', kind: 'date' },
    { key: 'to', label: 'To', kind: 'date' },
  ];

  constructor() {
    this.list.load();
    const own = this.session.session()?.vendorId;
    if (own) this.loadStatement();
  }

  protected money(amount: number, currency: string): string {
    return tableMoney(amount, currency);
  }

  protected entryLabel(type: string): string {
    return ENTRY_TYPES.find((entry) => entry.value === type)?.label ?? type;
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: LedgerFilters = {
      vendorId: this.vendorId() || undefined,
      entryType: values['entryType'],
      from: values['from'],
      to: values['to'],
    };
    this.list.setFilters(filters);
  }

  // ---- The statement ----------------------------------------------------------------------------

  protected loadStatement(): void {
    const vendorId = this.vendorId().trim();
    if (!vendorId) {
      this.statementError.set('A statement is one seller’s. Give a seller id.');
      return;
    }

    this.loadingStatement.set(true);
    this.statementError.set(null);

    const range = { from: this.isoOrUndefined(this.from()), to: this.isoOrUndefined(this.to()) };

    this.settlements.statement(vendorId, range).subscribe({
      next: (statement) => {
        this.loadingStatement.set(false);
        this.statement.set(statement);
      },
      error: (error: unknown) => {
        this.loadingStatement.set(false);
        this.statement.set(null);
        this.statementError.set(describeError(error, 'It could not be loaded.'));
      },
    });

    this.settlements.balance(vendorId).subscribe({
      next: (balance) => this.balance.set(balance),
      // Not fatal: the statement below still says what the period came to.
      error: () => this.balance.set(null),
    });
  }

  protected exportStatement(): void {
    const vendorId = this.vendorId().trim();
    if (!vendorId) {
      this.statementError.set('A statement is one seller’s. Give a seller id.');
      return;
    }

    this.exporting.set(true);
    this.documents
      .vendorStatementCsv(vendorId, this.isoOrUndefined(this.from()), this.isoOrUndefined(this.to()))
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          this.documents.download(blob, `statement-${vendorId}.csv`);
        },
        error: (error: unknown) => {
          this.exporting.set(false);
          this.statementError.set(describeError(error, 'The CSV could not be produced.'));
        },
      });
  }

  // ---- The platform's own side -------------------------------------------------------------------

  protected loadRevenue(): void {
    this.loadingRevenue.set(true);
    this.settlements
      .platformRevenue({ from: this.isoOrUndefined(this.from()), to: this.isoOrUndefined(this.to()) })
      .subscribe({
        next: (revenue) => {
          this.loadingRevenue.set(false);
          this.revenue.set(revenue);
        },
        error: (error: unknown) => {
          this.loadingRevenue.set(false);
          this.actionError.set(describeError(error, 'Revenue could not be worked out.'));
        },
      });
  }

  protected loadStatutory(): void {
    this.loadingStatutory.set(true);
    this.settlements
      .statutoryExtract(
        { from: this.isoOrUndefined(this.from()), to: this.isoOrUndefined(this.to()) },
        this.vendorId().trim() || undefined,
      )
      .subscribe({
        next: (extract) => {
          this.loadingStatutory.set(false);
          this.statutory.set(extract);
        },
        error: (error: unknown) => {
          this.loadingStatutory.set(false);
          this.actionError.set(describeError(error, 'The extract could not be worked out.'));
        },
      });
  }

  protected exportStatutory(): void {
    this.exporting.set(true);
    this.documents
      .statutoryExtractCsv(
        this.isoOrUndefined(this.from()),
        this.isoOrUndefined(this.to()),
        this.vendorId().trim() || undefined,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          this.documents.download(blob, 'tcs-tds.csv');
        },
        error: (error: unknown) => {
          this.exporting.set(false);
          this.actionError.set(describeError(error, 'The CSV could not be produced.'));
        },
      });
  }

  // ---- The one write ------------------------------------------------------------------------------

  protected postAdjustment(): void {
    const vendorId = this.adjustVendorId().trim();
    const reason = this.adjustReason().trim();

    if (!vendorId || !reason) {
      this.adjustError.set('An adjustment needs a seller and a reason.');
      return;
    }

    const body: AdjustmentBody = {
      vendorId,
      direction: this.adjustDirection(),
      amount: Number(this.adjustAmount()) || 0,
      reason,
    };

    this.busy.set(true);
    this.adjustError.set(null);

    this.settlements.postAdjustment(body).subscribe({
      next: () => {
        this.busy.set(false);
        this.adjusting.set(false);
        this.adjustAmount.set('');
        this.adjustReason.set('');
        this.toasts.success('Adjustment posted.');
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.adjustError.set(describeError(error, 'It could not be posted.'));
      },
    });
  }

  /** A `date` input's value as an instant, or undefined when it is blank. */
  private isoOrUndefined(value: string): string | undefined {
    return value ? new Date(value).toISOString() : undefined;
  }
}
