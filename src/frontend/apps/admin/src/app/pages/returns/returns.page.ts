import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ReturnFilters, ReturnSummaryResponse, ReturnsAdminService } from '@klarahome/data-access-admin';
import {
  CellTemplate,
  DataTable,
  DataTableColumn,
  FilterBar,
  FilterDefinition,
  FilterValues,
  PageHeader,
  toneFor,
} from '@klarahome/ui-admin';
import { Alert } from '@klarahome/ui-primitives';

import { tableDateTime, tableMoney } from '../../core/format';

/**
 * The returns queue.
 *
 * It opens on **Requested** because that is the only status where somebody is waiting on a
 * decision — every later one is waiting on a parcel, a courier or a bank. The list is otherwise
 * ordinary; the work is on the detail page.
 *
 * The `Age` column is the one worth having. A return's cost is mostly the customer's patience, and
 * a queue sorted by date without saying how old anything is makes a week-old request look like
 * yesterday's.
 */
@Component({
  selector: 'kh-returns-page',
  imports: [Alert, CellTemplate, DataTable, FilterBar, PageHeader, RouterLink],
  template: `
    <kh-page-header
      heading="Returns"
      description="What customers have asked to send back, and how far each one has got."
    />

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Returns could not be loaded">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Returns"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="returns-list"
      exportMode="page"
      emptyMessage="No return matches these filters."
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

      <ng-template khCell="returnNumber" let-row>
        <a class="link" [routerLink]="['/returns', row.id]">{{ row.returnNumber }}</a>
        <span class="note">{{ row.orderNumber }} · {{ row.subOrderNumber }}</span>
      </ng-template>

      <ng-template khCell="age" let-row>
        <span [class.old]="ageInDays(row) >= 3">{{ ageLabel(row) }}</span>
      </ng-template>
    </kh-data-table>
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

    .old {
      font-weight: var(--weight-medium);
      color: var(--color-danger);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReturnsPage {
  private readonly returns = inject(ReturnsAdminService);

  protected readonly list = this.returns.returns();
  protected readonly values = signal<FilterValues>({ status: 'Requested' });

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: ReturnSummaryResponse) => row.id;
  protected readonly rowLabel = (row: ReturnSummaryResponse) => row.returnNumber;

  protected readonly columns: readonly DataTableColumn<ReturnSummaryResponse>[] = [
    { key: 'returnNumber', label: 'Return', kind: 'custom', width: '14rem' },
    {
      key: 'status',
      label: 'Status',
      kind: 'badge',
      value: (row) => row.status,
      tone: (row) => toneFor(row.status),
      width: '10rem',
    },
    { key: 'type', label: 'Kind', value: (row) => row.type, width: '8rem' },
    { key: 'reasonCode', label: 'Reason', value: (row) => row.reasonCode },
    { key: 'units', label: 'Units', kind: 'number', value: (row) => row.units },
    {
      key: 'estimatedRefund',
      label: 'Estimated',
      kind: 'number',
      value: (row) => tableMoney(row.estimatedRefund, row.currencyCode),
    },
    {
      key: 'refundAmount',
      label: 'Refunded',
      kind: 'number',
      value: (row) => (row.refundAmount > 0 ? tableMoney(row.refundAmount, row.currencyCode) : '—'),
    },
    { key: 'age', label: 'Waiting', kind: 'custom', width: '8rem' },
    {
      key: 'requestedAt',
      label: 'Requested',
      kind: 'date',
      value: (row) => tableDateTime(row.requestedAt),
      hiddenByDefault: true,
    },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'status',
      label: 'Status',
      kind: 'select',
      options: [
        { value: 'Requested', label: 'Awaiting a decision' },
        { value: 'Approved', label: 'Approved' },
        { value: 'PickupScheduled', label: 'Pickup arranged' },
        { value: 'InTransit', label: 'On its way back' },
        { value: 'Received', label: 'Received' },
        { value: 'Inspected', label: 'Inspected' },
        { value: 'Refunded', label: 'Refunded' },
        { value: 'Rejected', label: 'Rejected' },
        { value: 'Closed', label: 'Closed' },
      ],
    },
    { key: 'from', label: 'Requested from', kind: 'date' },
    { key: 'to', label: 'Requested to', kind: 'date' },
  ];

  constructor() {
    this.list.setFilters({ status: 'Requested' });
  }

  protected ageInDays(row: ReturnSummaryResponse): number {
    const requested = new Date(row.requestedAt).getTime();
    if (Number.isNaN(requested)) return 0;
    return Math.floor((Date.now() - requested) / 86_400_000);
  }

  protected ageLabel(row: ReturnSummaryResponse): string {
    const days = this.ageInDays(row);
    if (days <= 0) return 'Today';
    return days === 1 ? '1 day' : `${days} days`;
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: ReturnFilters = {
      status: values['status'],
      from: values['from'],
      to: values['to'],
    };
    this.list.setFilters(filters);
  }
}
