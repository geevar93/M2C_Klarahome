import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { OrderFilters, OrderSummaryResponse, OrdersAdminService } from '@klarahome/data-access-admin';
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
import { Alert, Badge } from '@klarahome/ui-primitives';

import { tableDateTime, tableMoney } from '../../core/format';

/**
 * Every order.
 *
 * The list a support call starts from, so the filters are the questions actually asked: **which
 * order** (the number, matched exactly), **what state**, **paid or not**, and **when**. There is
 * no free-text search because the endpoint offers none, and a box that quietly matched only the
 * order number would be a search that lies about its reach.
 *
 * The `Sellers` and `Parts` columns are what make an order legible on this platform: a basket
 * split across three sellers is one order and three sub-orders, and it is normal for one of them
 * to be delivered while another has not shipped. The status column shows the order's own derived
 * status; the parts column shows the distinct statuses underneath it, which is the honest
 * summary — "Partially shipped" as a single word would be a status the domain does not have.
 */
@Component({
  selector: 'kh-orders-page',
  imports: [Alert, Badge, CellTemplate, DataTable, FilterBar, PageHeader, RouterLink],
  template: `
    <kh-page-header
      heading="Orders"
      description="One row per order. An order split across sellers has a part per seller, each moving at its own pace."
    />

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Orders could not be loaded">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Orders"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="orders-list"
      exportMode="page"
      emptyMessage="No order matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        searchLabel="Order number"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="orderNumber" let-row>
        <a class="link" [routerLink]="['/orders', row.id]">{{ row.orderNumber }}</a>
        <span class="who">{{ row.customerName }}</span>
      </ng-template>

      <ng-template khCell="subOrderStatuses" let-row>
        <div class="parts">
          @for (status of row.subOrderStatuses; track status) {
            <kh-badge [tone]="tone(status)">{{ status }}</kh-badge>
          } @empty {
            <span class="who">—</span>
          }
        </div>
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

    .who {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .parts {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrdersPage {
  private readonly orders = inject(OrdersAdminService);

  protected readonly list = this.orders.orders();
  protected readonly values = signal<FilterValues>({});

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: OrderSummaryResponse) => row.id;
  protected readonly rowLabel = (row: OrderSummaryResponse) => row.orderNumber;

  protected readonly columns: readonly DataTableColumn<OrderSummaryResponse>[] = [
    { key: 'orderNumber', label: 'Order', kind: 'custom', width: '14rem' },
    {
      key: 'status',
      label: 'Status',
      kind: 'badge',
      value: (row) => row.status,
      tone: (row) => toneFor(row.status),
      width: '10rem',
    },
    { key: 'subOrderStatuses', label: 'Parts', kind: 'custom' },
    {
      key: 'paymentStatus',
      label: 'Payment',
      kind: 'badge',
      value: (row) => `${row.paymentMethod} · ${row.paymentStatus}`,
      tone: (row) => toneFor(row.paymentStatus),
    },
    { key: 'itemCount', label: 'Items', kind: 'number', value: (row) => row.itemCount },
    {
      key: 'vendorCount',
      label: 'Sellers',
      kind: 'number',
      value: (row) => row.vendorCount,
      hiddenByDefault: true,
    },
    {
      key: 'netTotal',
      label: 'Net total',
      kind: 'number',
      value: (row) => tableMoney(row.netTotal, row.currencyCode),
    },
    {
      key: 'grandTotal',
      label: 'Ordered',
      kind: 'number',
      value: (row) => tableMoney(row.grandTotal, row.currencyCode),
      hiddenByDefault: true,
    },
    { key: 'placedAt', label: 'Placed', kind: 'date', value: (row) => tableDateTime(row.placedAt) },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'status',
      label: 'Status',
      kind: 'select',
      options: [
        { value: 'Pending', label: 'Awaiting payment' },
        { value: 'Confirmed', label: 'Confirmed' },
        { value: 'Processing', label: 'Being prepared' },
        { value: 'Shipped', label: 'Shipped' },
        { value: 'Delivered', label: 'Delivered' },
        { value: 'Cancelled', label: 'Cancelled' },
        { value: 'Returned', label: 'Returned' },
      ],
    },
    {
      key: 'paymentStatus',
      label: 'Payment',
      kind: 'select',
      options: [
        { value: 'Pending', label: 'Not paid' },
        { value: 'Authorized', label: 'Authorised' },
        { value: 'Paid', label: 'Paid' },
        { value: 'Failed', label: 'Failed' },
        { value: 'Refunded', label: 'Refunded' },
      ],
    },
    { key: 'from', label: 'Placed from', kind: 'date' },
    { key: 'to', label: 'Placed to', kind: 'date' },
  ];

  constructor() {
    this.list.load();
  }

  protected tone(status: string) {
    return toneFor(status);
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: OrderFilters = {
      status: values['status'],
      paymentStatus: values['paymentStatus'],
      // The search box is the order number, matched exactly — the only lookup the endpoint has.
      number: values['q'],
      from: values['from'],
      to: values['to'],
    };
    this.list.setFilters(filters);
  }
}
