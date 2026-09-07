import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  GoodsReceiptLinePayload,
  InventoryAdminService,
  PurchaseOrderFilters,
  PurchaseOrderLinePayload,
  PurchaseOrderResponse,
  StockItemResponse,
} from '@klarahome/data-access-admin';
import {
  CellTemplate,
  ConfirmDialog,
  DataTable,
  DataTableColumn,
  EntityDrawer,
  FilterBar,
  FilterDefinition,
  FilterValues,
  Modal,
  PageHeader,
  toneFor,
} from '@klarahome/ui-admin';
import { Alert, Button, Control, Field, Icon } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDate, tableMoney } from '../../core/format';

/** A line being drafted. Held as strings because that is what the inputs hold. */
interface DraftLine {
  listingId: string;
  sku: string;
  description: string;
  quantityOrdered: string;
  unitCost: string;
  taxRate: string;
}

/** What is being received against one ordered line. */
interface ReceiptLine {
  readonly purchaseOrderLineId: string;
  readonly sku: string;
  readonly outstanding: number;
  accepted: string;
  rejected: string;
  rejectionReason: string;
  batchCode: string;
}

/**
 * Buying stock in, and booking it when it arrives.
 *
 * The two halves of this screen are the two halves of purchasing, and the second one is where the
 * care goes.
 *
 * **Receiving counts accepted and rejected separately.** An order of 40 against which 30 arrive is
 * ambiguous — ten short-shipped is a conversation with the supplier and ten damaged is an
 * insurance claim, and a single "received" number cannot tell them apart. **Only the accepted
 * quantity moves the ledger**, which is what stops a damaged pallet becoming saleable stock.
 *
 * **A purchase order is a draft until it is submitted.** Lines can be changed freely before that
 * and not after, so the editor is offered on a draft and the receipt on everything past it. The
 * status is the API's, not a client-side mode.
 *
 * Partial receipt is the normal case rather than the exception: a second delivery against the same
 * order produces a second goods receipt, and the order stays `PartiallyReceived` until the
 * outstanding quantity is nil.
 */
@Component({
  selector: 'kh-purchase-orders-page',
  imports: [
    Alert,
    Button,
    CellTemplate,
    ConfirmDialog,
    Control,
    DataTable,
    EntityDrawer,
    Field,
    FilterBar,
    Icon,
    Modal,
    PageHeader,
  ],
  template: `
    <kh-page-header
      heading="Purchase orders"
      description="What has been ordered from suppliers, and what has actually turned up."
    >
      <button khButton type="button" variant="primary" (click)="startDraft()">
        <kh-icon name="plus" size="sm" />
        New purchase order
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Purchase orders could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not work" [dismissible]="true">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Purchase orders"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      exportMode="page"
      emptyMessage="No purchase order matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters()"
        [values]="values()"
        [searchable]="false"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="number" let-row>
        <button type="button" class="link" (click)="open(row)">{{ row.number }}</button>
      </ng-template>

      <ng-template khCell="actions" let-row>
        <div class="row-actions">
          @if (row.status === 'Submitted' || row.status === 'PartiallyReceived') {
            <button khButton type="button" size="sm" [disabled]="busy()" (click)="startReceipt(row)">
              Receive
            </button>
          }
        </div>
      </ng-template>
    </kh-data-table>

    @if (viewing(); as order) {
      <kh-entity-drawer
        [heading]="order.number"
        [subtitle]="order.status + ' · ' + money(order.total)"
        (closed)="viewing.set(null)"
      >
        <table>
          <thead>
            <tr>
              <th scope="col">SKU</th>
              <th scope="col" class="numeric">Ordered</th>
              <th scope="col" class="numeric">Received</th>
              <th scope="col" class="numeric">Unit cost</th>
              <th scope="col" class="numeric">Line total</th>
            </tr>
          </thead>
          <tbody>
            @for (line of order.lines; track line.id) {
              <tr>
                <td>
                  {{ line.sku }}
                  @if (line.description; as text) {
                    <span class="note">{{ text }}</span>
                  }
                </td>
                <td class="numeric">{{ line.quantityOrdered }}</td>
                <td class="numeric">{{ line.quantityReceived }}</td>
                <td class="numeric">{{ money(line.unitCost) }}</td>
                <td class="numeric">{{ money(line.lineTotal) }}</td>
              </tr>
            }
          </tbody>
          <tfoot>
            <tr>
              <td colspan="4">Subtotal</td>
              <td class="numeric">{{ money(order.subtotal) }}</td>
            </tr>
            <tr>
              <td colspan="4">Tax</td>
              <td class="numeric">{{ money(order.taxTotal) }}</td>
            </tr>
            <tr>
              <td colspan="4"><strong>Total</strong></td>
              <td class="numeric">
                <strong>{{ money(order.total) }}</strong>
              </td>
            </tr>
          </tfoot>
        </table>

        @if (order.notes; as notes) {
          <p class="hint">{{ notes }}</p>
        }

        <div slot="footer">
          @if (order.status === 'Draft') {
            <button
              khButton
              type="button"
              size="sm"
              variant="primary"
              [disabled]="busy()"
              (click)="submit(order)"
            >
              Submit to the supplier
            </button>
          }
          @if (order.status !== 'Cancelled' && order.status !== 'Received') {
            <button
              khButton
              type="button"
              size="sm"
              variant="danger"
              [disabled]="busy()"
              (click)="cancelling.set(order)"
            >
              Cancel
            </button>
          }
        </div>
      </kh-entity-drawer>
    }

    @if (drafting()) {
      <kh-entity-drawer
        heading="New purchase order"
        subtitle="Saved as a draft. Nothing is ordered until it is submitted."
        (closed)="drafting.set(false)"
      >
        <kh-field label="Supplier" for="po-supplier">
          <select
            khControl
            id="po-supplier"
            [value]="supplierId()"
            (change)="supplierId.set($any($event.target).value)"
          >
            <option value="">Choose a supplier</option>
            @for (supplier of suppliers.rows(); track supplier.id) {
              <option [value]="supplier.id">{{ supplier.code }} — {{ supplier.name }}</option>
            }
          </select>
        </kh-field>

        <kh-field label="Deliver to" for="po-warehouse">
          <select
            khControl
            id="po-warehouse"
            [value]="warehouseId()"
            (change)="onWarehouseChosen($any($event.target).value)"
          >
            <option value="">Choose a warehouse</option>
            @for (warehouse of warehouses.rows(); track warehouse.id) {
              <option [value]="warehouse.id">{{ warehouse.code }} — {{ warehouse.name }}</option>
            }
          </select>
        </kh-field>

        <kh-field label="Expected on" for="po-expected" [optional]="true">
          <input
            khControl
            id="po-expected"
            type="date"
            [value]="expectedAt()"
            (input)="expectedAt.set($any($event.target).value)"
          />
        </kh-field>

        <fieldset>
          <legend>Lines</legend>
          <p class="hint">
            Search the stock held at that warehouse and add what you are ordering. Costs are what the supplier
            charges, not what you sell for.
          </p>

          <kh-filter-bar
            [filters]="[]"
            [values]="lineSearch()"
            searchLabel="Search stock by SKU"
            (changed)="searchStock($event)"
          />

          @if (stock.rows().length > 0) {
            <ul class="results">
              @for (item of stock.rows(); track item.id) {
                <li>
                  <button type="button" class="result" (click)="addLine(item)">
                    <span class="sku">{{ item.sku }}</span>
                    <span class="note">{{ item.quantityOnHand }} on hand</span>
                  </button>
                </li>
              }
            </ul>
          }

          @for (line of lines(); track $index; let index = $index) {
            <div class="line">
              <span class="sku">{{ line.sku }}</span>
              <kh-field label="Quantity" [for]="'po-line-qty-' + index">
                <input
                  khControl
                  khNumeric
                  [id]="'po-line-qty-' + index"
                  type="number"
                  min="1"
                  [value]="line.quantityOrdered"
                  (input)="setLine(index, 'quantityOrdered', $any($event.target).value)"
                />
              </kh-field>
              <kh-field label="Unit cost" [for]="'po-line-cost-' + index">
                <input
                  khControl
                  khNumeric
                  [id]="'po-line-cost-' + index"
                  type="number"
                  min="0"
                  step="0.01"
                  [value]="line.unitCost"
                  (input)="setLine(index, 'unitCost', $any($event.target).value)"
                />
              </kh-field>
              <kh-field label="Tax %" [for]="'po-line-tax-' + index">
                <input
                  khControl
                  khNumeric
                  [id]="'po-line-tax-' + index"
                  type="number"
                  min="0"
                  max="28"
                  step="0.01"
                  [value]="line.taxRate"
                  (input)="setLine(index, 'taxRate', $any($event.target).value)"
                />
              </kh-field>
              <button
                khButton
                type="button"
                size="sm"
                variant="danger"
                aria-label="Remove line"
                (click)="removeLine(index)"
              >
                <kh-icon name="trash" size="sm" />
              </button>
            </div>
          }

          @if (lines().length === 0) {
            <p class="hint">No lines yet. A purchase order with no lines cannot be created.</p>
          }
        </fieldset>

        <kh-field label="Notes" for="po-notes" [optional]="true">
          <textarea
            khControl
            id="po-notes"
            rows="2"
            [value]="notes()"
            (input)="notes.set($any($event.target).value)"
          ></textarea>
        </kh-field>

        <div slot="footer">
          <button khButton type="button" variant="tertiary" (click)="drafting.set(false)">Cancel</button>
          <button
            khButton
            type="button"
            variant="primary"
            [disabled]="busy() || lines().length === 0 || !supplierId() || !warehouseId()"
            (click)="createDraft()"
          >
            Save as draft
          </button>
        </div>
      </kh-entity-drawer>
    }

    <kh-modal
      [open]="receiving() !== null"
      heading="Receive this delivery"
      width="48rem"
      [dismissible]="!busy()"
      (closed)="receiving.set(null)"
    >
      @if (receiving(); as order) {
        <p class="hint">
          {{ order.number }} — count what arrived. Only the accepted quantity goes into stock; what is
          rejected is recorded against the supplier and does not.
        </p>

        <table>
          <thead>
            <tr>
              <th scope="col">SKU</th>
              <th scope="col" class="numeric">Outstanding</th>
              <th scope="col">Accepted</th>
              <th scope="col">Rejected</th>
              <th scope="col">Why rejected</th>
              <th scope="col">Batch</th>
            </tr>
          </thead>
          <tbody>
            @for (line of receiptLines(); track line.purchaseOrderLineId; let index = $index) {
              <tr>
                <td>{{ line.sku }}</td>
                <td class="numeric">{{ line.outstanding }}</td>
                <td>
                  <input
                    khControl
                    khNumeric
                    [id]="'receipt-accepted-' + index"
                    [attr.aria-label]="'Accepted for ' + line.sku"
                    type="number"
                    min="0"
                    [attr.max]="line.outstanding"
                    [value]="line.accepted"
                    (input)="setReceipt(index, 'accepted', $any($event.target).value)"
                  />
                </td>
                <td>
                  <input
                    khControl
                    khNumeric
                    [id]="'receipt-rejected-' + index"
                    [attr.aria-label]="'Rejected for ' + line.sku"
                    type="number"
                    min="0"
                    [value]="line.rejected"
                    (input)="setReceipt(index, 'rejected', $any($event.target).value)"
                  />
                </td>
                <td>
                  <input
                    khControl
                    [id]="'receipt-reason-' + index"
                    [attr.aria-label]="'Rejection reason for ' + line.sku"
                    type="text"
                    [value]="line.rejectionReason"
                    (input)="setReceipt(index, 'rejectionReason', $any($event.target).value)"
                  />
                </td>
                <td>
                  <input
                    khControl
                    [id]="'receipt-batch-' + index"
                    [attr.aria-label]="'Batch code for ' + line.sku"
                    type="text"
                    [value]="line.batchCode"
                    (input)="setReceipt(index, 'batchCode', $any($event.target).value)"
                  />
                </td>
              </tr>
            }
          </tbody>
        </table>
      }

      <div slot="footer">
        <button khButton type="button" variant="tertiary" [disabled]="busy()" (click)="receiving.set(null)">
          Cancel
        </button>
        <button khButton type="button" variant="primary" [disabled]="busy()" (click)="receive()">
          Book it in
        </button>
      </div>
    </kh-modal>

    <kh-confirm-dialog
      [open]="cancelling() !== null"
      heading="Cancel this purchase order"
      message="The supplier is not told by the platform. Anything already received stays received."
      confirmLabel="Cancel the order"
      [busy]="busy()"
      (confirmed)="cancel()"
      (cancelled)="cancelling.set(null)"
    />
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .link {
      padding: 0;
      border: none;
      background: none;
      color: var(--color-link);
      font: inherit;
      font-weight: var(--weight-medium);
      cursor: pointer;
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
    }

    .hint {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    table {
      inline-size: 100%;
      border-collapse: collapse;
      font-size: var(--text-sm);
    }

    th,
    td {
      padding: var(--space-2);
      border-block-end: 1px solid var(--color-border);
      text-align: start;
    }

    .numeric {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }

    fieldset {
      margin: 0 0 var(--space-4);
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    legend {
      padding-inline: var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .results {
      display: grid;
      gap: var(--space-1);
      margin: var(--space-2) 0;
      padding: 0;
      list-style: none;
    }

    .result {
      display: flex;
      gap: var(--space-2);
      align-items: baseline;
      inline-size: 100%;
      padding: var(--space-1) var(--space-2);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-sm);
      background: none;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .line {
      display: grid;
      grid-template-columns: 1fr 6rem 8rem 6rem auto;
      gap: var(--space-2);
      align-items: end;
      padding-block: var(--space-2);
      border-block-start: 1px solid var(--color-border);
    }

    .sku {
      font-weight: var(--weight-medium);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PurchaseOrdersPage {
  private readonly inventory = inject(InventoryAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly list = this.inventory.purchaseOrders();
  protected readonly suppliers = this.inventory.suppliers({ activeOnly: true }, 200);
  protected readonly warehouses = this.inventory.warehouses({ activeOnly: true }, 200);
  protected readonly stock = this.inventory.stock({}, 8);

  protected readonly values = signal<FilterValues>({});
  protected readonly lineSearch = signal<FilterValues>({});
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected readonly viewing = signal<PurchaseOrderResponse | null>(null);
  protected readonly drafting = signal(false);
  protected readonly receiving = signal<PurchaseOrderResponse | null>(null);
  protected readonly cancelling = signal<PurchaseOrderResponse | null>(null);

  protected readonly supplierId = signal('');
  protected readonly warehouseId = signal('');
  protected readonly expectedAt = signal('');
  protected readonly notes = signal('');
  protected readonly lines = signal<readonly DraftLine[]>([]);
  protected readonly receiptLines = signal<readonly ReceiptLine[]>([]);

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: PurchaseOrderResponse) => row.id;
  protected readonly rowLabel = (row: PurchaseOrderResponse) => row.number;

  protected readonly columns: readonly DataTableColumn<PurchaseOrderResponse>[] = [
    { key: 'number', label: 'Number', kind: 'custom', width: '10rem' },
    {
      key: 'status',
      label: 'Status',
      kind: 'badge',
      value: (row) => row.status,
      tone: (row) => toneFor(row.status),
      width: '11rem',
    },
    { key: 'expectedAt', label: 'Expected', kind: 'date', value: (row) => tableDate(row.expectedAt) },
    { key: 'lines', label: 'Lines', kind: 'number', value: (row) => row.lines.length },
    { key: 'total', label: 'Total', kind: 'number', value: (row) => tableMoney(row.total) },
    { key: 'actions', label: 'Actions', kind: 'custom', width: '8rem' },
  ];

  protected readonly filters = computed<readonly FilterDefinition[]>(() => [
    {
      key: 'status',
      label: 'Status',
      kind: 'select',
      options: [
        { value: 'Draft', label: 'Draft' },
        { value: 'Submitted', label: 'Ordered' },
        { value: 'PartiallyReceived', label: 'Part received' },
        { value: 'Received', label: 'Received' },
        { value: 'Cancelled', label: 'Cancelled' },
      ],
    },
    {
      key: 'warehouseId',
      label: 'Warehouse',
      kind: 'select',
      options: this.warehouses.rows().map((warehouse) => ({
        value: warehouse.id,
        label: `${warehouse.code} — ${warehouse.name}`,
      })),
    },
  ]);

  constructor() {
    this.list.load();
    this.suppliers.load();
    this.warehouses.load();
  }

  protected money(amount: number): string {
    return tableMoney(amount);
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: PurchaseOrderFilters = {
      status: values['status'],
      warehouseId: values['warehouseId'],
    };
    this.list.setFilters(filters);
  }

  protected open(order: PurchaseOrderResponse): void {
    this.viewing.set(order);
  }

  // ---- Drafting ---------------------------------------------------------------------------------

  protected startDraft(): void {
    this.supplierId.set('');
    this.warehouseId.set('');
    this.expectedAt.set('');
    this.notes.set('');
    this.lines.set([]);
    this.lineSearch.set({});
    this.stock.clear();
    this.drafting.set(true);
  }

  protected onWarehouseChosen(id: string): void {
    this.warehouseId.set(id);
    // The stock search is scoped to the destination, because a line has to name a listing that is
    // tracked *there* — ordering into a warehouse that does not stock the SKU is the commonest
    // way to produce a purchase order that cannot be received.
    this.stock.setFilters({ warehouseId: id || undefined });
    this.lines.set([]);
  }

  protected searchStock(values: FilterValues): void {
    this.lineSearch.set(values);
    this.stock.setFilters({ warehouseId: this.warehouseId() || undefined, search: values['q'] });
  }

  protected addLine(item: StockItemResponse): void {
    if (this.lines().some((line) => line.listingId === item.listingId)) return;
    this.lines.update((current) => [
      ...current,
      {
        listingId: item.listingId,
        sku: item.sku,
        description: '',
        quantityOrdered: '1',
        unitCost: '0',
        taxRate: '0',
      },
    ]);
  }

  protected setLine(index: number, field: keyof DraftLine, value: string): void {
    this.lines.update((current) =>
      current.map((line, at) => (at === index ? { ...line, [field]: value } : line)),
    );
  }

  protected removeLine(index: number): void {
    this.lines.update((current) => current.filter((_line, at) => at !== index));
  }

  protected createDraft(): void {
    if (this.busy()) return;

    const lines: PurchaseOrderLinePayload[] = this.lines().map((line) => ({
      listingId: line.listingId,
      description: line.description || null,
      quantityOrdered: Number(line.quantityOrdered || 0),
      unitCost: Number(line.unitCost || 0),
      taxRate: Number(line.taxRate || 0),
    }));

    if (lines.some((line) => line.quantityOrdered <= 0)) {
      this.actionError.set('Every line needs a quantity of at least one.');
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);

    this.inventory
      .createPurchaseOrder({
        supplierId: this.supplierId(),
        warehouseId: this.warehouseId(),
        expectedAt: this.expectedAt() || null,
        notes: this.notes() || null,
        lines,
      })
      .subscribe({
        next: (created) => {
          this.busy.set(false);
          this.drafting.set(false);
          this.viewing.set(created);
          this.toasts.success(`${created.number} saved as a draft.`);
          this.list.refresh();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(describeError(error, 'That purchase order could not be created.'));
        },
      });
  }

  protected submit(order: PurchaseOrderResponse): void {
    this.act(this.inventory.submitPurchaseOrder(order.id), `${order.number} submitted.`);
  }

  protected cancel(): void {
    const order = this.cancelling();
    if (!order) return;
    this.cancelling.set(null);
    this.act(this.inventory.cancelPurchaseOrder(order.id), `${order.number} cancelled.`);
  }

  // ---- Receiving --------------------------------------------------------------------------------

  protected startReceipt(order: PurchaseOrderResponse): void {
    this.receiving.set(order);
    this.receiptLines.set(
      order.lines
        .map((line) => ({
          purchaseOrderLineId: line.id,
          sku: line.sku,
          outstanding: line.quantityOrdered - line.quantityReceived,
          // Pre-filled with what is outstanding, because the full delivery is the common case and
          // the operator's job is then to correct the exceptions rather than type every number.
          accepted: String(Math.max(0, line.quantityOrdered - line.quantityReceived)),
          rejected: '0',
          rejectionReason: '',
          batchCode: '',
        }))
        .filter((line) => line.outstanding > 0),
    );
  }

  protected setReceipt(index: number, field: keyof ReceiptLine, value: string): void {
    this.receiptLines.update((current) =>
      current.map((line, at) => (at === index ? { ...line, [field]: value } : line)),
    );
  }

  protected receive(): void {
    const order = this.receiving();
    if (!order || this.busy()) return;

    const lines: GoodsReceiptLinePayload[] = this.receiptLines()
      .map((line) => ({
        purchaseOrderLineId: line.purchaseOrderLineId,
        accepted: Number(line.accepted || 0),
        rejected: Number(line.rejected || 0),
        rejectionReason: line.rejectionReason || null,
        batchCode: line.batchCode || null,
        expiresOn: null,
      }))
      .filter((line) => line.accepted > 0 || line.rejected > 0);

    if (lines.length === 0) {
      this.actionError.set('Nothing was counted. Enter what arrived on at least one line.');
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);

    this.inventory.receivePurchaseOrder(order.id, { notes: null, lines }).subscribe({
      next: (receipt) => {
        this.busy.set(false);
        this.receiving.set(null);
        this.toasts.success(`Goods receipt ${receipt.number} posted.`);
        this.list.refresh();
        this.reopen(order.id);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That delivery could not be booked in.'));
      },
    });
  }

  private act(request: ReturnType<InventoryAdminService['submitPurchaseOrder']>, message: string): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);

    request.subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.viewing.set(updated);
        this.toasts.success(message);
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That could not be done.'));
      },
    });
  }

  /** Refetches the order behind an open drawer, so the received quantities update in place. */
  private reopen(id: string): void {
    if (this.viewing()?.id !== id) return;
    this.inventory.purchaseOrder(id).subscribe({
      next: (order) => this.viewing.set(order),
      error: () => this.viewing.set(null),
    });
  }
}
