import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
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
import {
  ORDER_STATUS_VOCAB,
  PAYMENT_STATUS_VOCAB,
  SUB_ORDER_STATUS_VOCAB,
  paymentMethodLabel,
  statusFilterOptions,
  statusLabel,
} from './order-vocabulary';

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
/** The filter keys that are mirrored into the address bar. */
const FILTER_KEYS = ['status', 'paymentStatus', 'q', 'from', 'to'] as const;

@Component({
  selector: 'kh-orders-page',
  imports: [Alert, Badge, CellTemplate, DataTable, FilterBar, PageHeader, RouterLink],
  template: `
    <kh-page-header
      heading="Orders"
      description="One row per order. When an order has items from more than one seller, each seller's share ships and is tracked separately."
    />

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Orders could not be loaded">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Orders"
      [filtered]="hasFilters()"
      (filtersCleared)="applyFilters({})"
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
        <!-- Items and sellers used to be two columns of their own; at 1366 they pushed "Placed" off
             the edge, and neither is a figure anybody sorts by. -->
        <span class="who"
          >{{ row.itemCount }} {{ row.itemCount === 1 ? 'item' : 'items' }} · {{ row.vendorCount }}
          {{ row.vendorCount === 1 ? 'seller' : 'sellers' }}</span
        >
      </ng-template>

      <ng-template khCell="subOrderStatuses" let-row>
        <div class="parts">
          @for (status of row.subOrderStatuses; track status) {
            <kh-badge [tone]="tone(status)">{{ label(status) }}</kh-badge>
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
  protected readonly hasFilters = computed(() => Object.keys(this.values()).length > 0);
  private readonly orders = inject(OrdersAdminService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

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
      value: (row) => statusLabel(ORDER_STATUS_VOCAB, row.status),
      tone: (row) => toneFor(row.status),
      width: '10rem',
      card: true,
    },
    { key: 'subOrderStatuses', label: 'Sellers’ shares', kind: 'custom' },
    {
      key: 'paymentStatus',
      label: 'Payment',
      kind: 'badge',
      value: (row) =>
        `${paymentMethodLabel(row.paymentMethod)} · ${statusLabel(PAYMENT_STATUS_VOCAB, row.paymentStatus)}`,
      tone: (row) => toneFor(row.paymentStatus),
      card: true,
    },
    {
      key: 'netTotal',
      label: 'Net total',
      kind: 'number',
      value: (row) => tableMoney(row.netTotal, row.currencyCode),
      card: true,
    },
    {
      // The same figure as the net total until something is cancelled or returned, which is why it
      // is off by default and named for what it is rather than a second "Total".
      key: 'grandTotal',
      label: 'Originally ordered',
      kind: 'number',
      value: (row) => tableMoney(row.grandTotal, row.currencyCode),
      hiddenByDefault: true,
    },
    {
      key: 'placedAt',
      label: 'Placed',
      kind: 'date',
      value: (row) => tableDateTime(row.placedAt),
      card: true,
    },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'status',
      label: 'Status',
      kind: 'select',
      options: statusFilterOptions(ORDER_STATUS_VOCAB),
    },
    {
      key: 'paymentStatus',
      label: 'Payment',
      kind: 'select',
      options: statusFilterOptions(PAYMENT_STATUS_VOCAB),
    },
    { key: 'from', label: 'Placed from', kind: 'date' },
    { key: 'to', label: 'Placed to', kind: 'date' },
  ];

  constructor() {
    // The filters live in the URL (`?status=Delivered&q=KH-1042`), so a link from the dashboard or
    // the notifications panel lands on the right slice, and Back restores the one you left.
    const query = this.route.snapshot.queryParamMap;
    const initial: Record<string, string> = {};
    for (const key of FILTER_KEYS) {
      const value = query.get(key);
      if (value) initial[key] = value;
    }
    // `setFilters` loads the first page, so there is no separate `load()`.
    this.applyFilters(initial, false);
  }

  protected tone(status: string) {
    return toneFor(status);
  }

  protected label(status: string) {
    return statusLabel(SUB_ORDER_STATUS_VOCAB, status);
  }

  protected applyFilters(values: FilterValues, syncUrl = true): void {
    this.values.set(values);
    if (syncUrl) {
      // Replace, not push: each chip press is a refinement of one view, not a page of history.
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: Object.fromEntries(FILTER_KEYS.map((key) => [key, values[key] || null])),
        replaceUrl: true,
      });
    }
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
