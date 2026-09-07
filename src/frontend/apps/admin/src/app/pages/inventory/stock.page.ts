import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  CatalogAdminService,
  InventoryAdminService,
  StockFilters,
  StockItemResponse,
  StockLedgerEntryResponse,
  StockMovementReason,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  CellTemplate,
  DataTable,
  DataTableColumn,
  EntityDrawer,
  EntityOption,
  EntityPicker,
  FilterBar,
  FilterDefinition,
  FilterValues,
  Modal,
  PageHeader,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';
import { Observable, map } from 'rxjs';

import { describeError } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';

/**
 * Stock, by location.
 *
 * The row is a **stock item** — one listing at one warehouse — because that is the grain the
 * platform actually holds and the grain a reservation takes from. A screen that showed "stock by
 * SKU" would be summing across warehouses and would then have to explain that the 40 it shows
 * cannot all be sold to one address.
 *
 * Three columns and one filter carry the whole meaning:
 *
 *  - **On hand, reserved, available.** Available is on-hand minus reserved, and it is the only one
 *    a checkout can sell. Showing all three is what stops "we have 20, why did it say out of
 *    stock" — the answer is visible in the row.
 *  - **The low-stock queue is a filter, not a screen.** Low is `onHand <= reorderLevel`, a
 *    property of the row; a second page would be a second definition of it.
 *
 * Adjusting is a signed movement with a reason from a closed list, never a new balance — see
 * `InventoryAdminService`. The ledger drawer beside it is what that produces, and it is the only
 * account of why a number is what it is.
 *
 * **Tracking is how a row starts existing.** A listing nobody has purchased in has no stock item at
 * all, and an untracked offer is one the checkout refuses — which reads to an operator exactly like
 * "out of stock" and is not. `POST /admin/stock/open` existed from Step 11 and nothing called it;
 * this is the button (Step 28B, deliverable 17).
 */
@Component({
  selector: 'kh-stock-page',
  imports: [
    Alert,
    Badge,
    Button,
    CellTemplate,
    Checkbox,
    Control,
    DataTable,
    EntityDrawer,
    EntityPicker,
    Field,
    FilterBar,
    HasPermission,
    Modal,
    PageHeader,
  ],
  template: `
    <kh-page-header
      heading="Stock"
      description="One row per listing per warehouse. Available is what a shopper can actually buy."
    >
      <button
        *khHasPermission="'inventory.stock.adjust'"
        khButton
        type="button"
        variant="primary"
        (click)="startTracking()"
      >
        Track a listing
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Stock could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not work" [dismissible]="true">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Stock"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="inventory-stock"
      exportMode="page"
      emptyMessage="No stock matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters()"
        [values]="values()"
        searchLabel="Search by SKU"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="sku" let-row>
        <span class="sku">{{ row.sku }}</span>
        <span class="where">{{ row.warehouseCode }}</span>
      </ng-template>

      <ng-template khCell="available" let-row>
        <strong [class.low]="row.isLow">{{ row.quantityAvailable }}</strong>
        @if (row.isLow) {
          <kh-badge tone="warning">Low</kh-badge>
        }
      </ng-template>

      <ng-template khCell="actions" let-row>
        <div class="row-actions">
          <button khButton type="button" size="sm" (click)="openLedger(row)">Ledger</button>
          <button
            *khHasPermission="'inventory.stock.adjust'"
            khButton
            type="button"
            size="sm"
            (click)="openAdjust(row)"
          >
            Adjust
          </button>
          <button
            *khHasPermission="'inventory.stock.configure'"
            khButton
            type="button"
            size="sm"
            (click)="openSettings(row)"
          >
            Settings
          </button>
        </div>
      </ng-template>
    </kh-data-table>

    @if (ledgerFor(); as item) {
      <kh-entity-drawer
        [heading]="'Ledger — ' + item.sku"
        [subtitle]="item.warehouseCode + ' · ' + item.quantityOnHand + ' on hand'"
        (closed)="closeLedger()"
      >
        <p class="hint">
          Every movement, newest first. The balance after each one is what the platform believed at that
          moment — nothing here can be edited, which is what makes it worth reading.
        </p>

        @if (ledger()?.error(); as message) {
          <kh-alert tone="danger" heading="The ledger could not be loaded">{{ message }}</kh-alert>
        }

        <table>
          <thead>
            <tr>
              <th scope="col">When</th>
              <th scope="col">Reason</th>
              <th scope="col" class="numeric">Change</th>
              <th scope="col" class="numeric">Balance</th>
              <th scope="col">Reference</th>
            </tr>
          </thead>
          <tbody>
            @for (entry of ledgerRows(); track entry.id) {
              <tr>
                <td>{{ when(entry) }}</td>
                <td>
                  {{ entry.reason }}
                  @if (entry.note; as note) {
                    <span class="note">{{ note }}</span>
                  }
                </td>
                <td class="numeric" [class.negative]="entry.change < 0">
                  {{ entry.change > 0 ? '+' : '' }}{{ entry.change }}
                </td>
                <td class="numeric">{{ entry.balanceAfter }}</td>
                <td class="reference">{{ reference(entry) }}</td>
              </tr>
            } @empty {
              <tr>
                <td colspan="5" class="hint">Nothing has moved yet.</td>
              </tr>
            }
          </tbody>
        </table>

        <div slot="footer">
          <button
            khButton
            type="button"
            size="sm"
            [disabled]="!ledger()?.hasPrevious() || ledger()?.loading()"
            (click)="ledger()?.previous()"
          >
            Newer
          </button>
          <button
            khButton
            type="button"
            size="sm"
            [disabled]="!ledger()?.nextCursor() || ledger()?.loading()"
            (click)="ledger()?.next()"
          >
            Older
          </button>
        </div>
      </kh-entity-drawer>
    }

    <kh-modal
      [open]="adjustFor() !== null"
      heading="Adjust stock"
      width="30rem"
      [dismissible]="!busy()"
      (closed)="adjustFor.set(null)"
    >
      @if (adjustFor(); as item) {
        <p class="hint">
          {{ item.sku }} at {{ item.warehouseCode }} — {{ item.quantityOnHand }} on hand,
          {{ item.quantityReserved }} reserved.
        </p>

        <kh-field
          label="Change"
          for="adjust-change"
          hint="Positive adds, negative removes. There is no way to set a balance directly, on purpose."
          [error]="adjustForm.fields.change.error()"
        >
          <input
            khControl
            khNumeric
            id="adjust-change"
            type="number"
            step="1"
            [value]="adjustForm.fields.change.value()"
            (input)="adjustForm.fields.change.set($any($event.target).value)"
            (touched)="adjustForm.fields.change.markTouched()"
          />
        </kh-field>

        <kh-field label="Reason" for="adjust-reason">
          <select
            khControl
            id="adjust-reason"
            [value]="adjustReason()"
            (change)="adjustReason.set($any($event.target).value)"
          >
            @for (reason of adjustReasons; track reason) {
              <option [value]="reason">{{ reason }}</option>
            }
          </select>
        </kh-field>

        <kh-field label="Note" for="adjust-note" hint="Why, in a few words. It goes on the ledger row.">
          <input
            khControl
            id="adjust-note"
            type="text"
            maxlength="200"
            [value]="adjustForm.fields.note.value()"
            (input)="adjustForm.fields.note.set($any($event.target).value)"
          />
        </kh-field>
      }

      <div slot="footer">
        <button khButton type="button" variant="tertiary" [disabled]="busy()" (click)="adjustFor.set(null)">
          Cancel
        </button>
        <button khButton type="button" variant="primary" [disabled]="busy()" (click)="adjust()">
          Record the movement
        </button>
      </div>
    </kh-modal>

    <kh-modal
      [open]="settingsFor() !== null"
      heading="Stock settings"
      width="30rem"
      [dismissible]="!busy()"
      (closed)="settingsFor.set(null)"
    >
      @if (settingsFor(); as item) {
        <p class="hint">{{ item.sku }} at {{ item.warehouseCode }}.</p>

        <kh-field label="Reorder level" for="settings-level" hint="At or below this, the row is flagged low.">
          <input
            khControl
            khNumeric
            id="settings-level"
            type="number"
            min="0"
            [value]="settingsForm.fields.reorderLevel.value()"
            (input)="settingsForm.fields.reorderLevel.set($any($event.target).value)"
          />
        </kh-field>

        <kh-field label="Reorder quantity" for="settings-quantity" hint="How many to buy when it is.">
          <input
            khControl
            khNumeric
            id="settings-quantity"
            type="number"
            min="0"
            [value]="settingsForm.fields.reorderQuantity.value()"
            (input)="settingsForm.fields.reorderQuantity.set($any($event.target).value)"
          />
        </kh-field>

        <kh-checkbox
          label="Allow backorders"
          description="Shoppers can buy it when there is none. Only turn this on if you can still ship."
          inputId="settings-backorder"
          [checked]="allowBackorder()"
          (checkedChange)="allowBackorder.set($event)"
        />

        <kh-checkbox
          label="Allow pre-orders"
          inputId="settings-preorder"
          [checked]="allowPreorder()"
          (checkedChange)="allowPreorder.set($event)"
        />
      }

      <div slot="footer">
        <button khButton type="button" variant="tertiary" [disabled]="busy()" (click)="settingsFor.set(null)">
          Cancel
        </button>
        <button khButton type="button" variant="primary" [disabled]="busy()" (click)="saveSettings()">
          Save
        </button>
      </div>
    </kh-modal>

    <kh-modal
      [open]="tracking()"
      heading="Track a listing"
      width="30rem"
      [dismissible]="!busy()"
      (closed)="tracking.set(false)"
    >
      <p class="hint">
        Opens a stock row at zero so the listing can be received into and sold. It moves no stock.
      </p>

      @if (trackError(); as message) {
        <kh-alert tone="danger" heading="It could not be opened">{{ message }}</kh-alert>
      }

      <kh-entity-picker
        label="Listing"
        inputId="track-listing"
        hint="Search by SKU or product name, or paste the listing id."
        [search]="listingSearch"
        (chose)="trackListing.set($event?.id ?? null)"
      />

      <kh-field label="Warehouse" for="track-warehouse">
        <select
          khControl
          id="track-warehouse"
          [value]="trackWarehouse()"
          (change)="trackWarehouse.set($any($event.target).value)"
        >
          <option value="">Choose a warehouse…</option>
          @for (warehouse of warehouses.rows(); track warehouse.id) {
            <option [value]="warehouse.id">{{ warehouse.name }} ({{ warehouse.code }})</option>
          }
        </select>
      </kh-field>

      <div slot="footer">
        <button khButton type="button" variant="tertiary" [disabled]="busy()" (click)="tracking.set(false)">
          Cancel
        </button>
        <button
          khButton
          type="button"
          variant="primary"
          [disabled]="busy() || !trackListing() || !trackWarehouse()"
          (click)="track()"
        >
          Start tracking
        </button>
      </div>
    </kh-modal>
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .sku {
      display: block;
      font-weight: var(--weight-medium);
    }

    .where,
    .note,
    .reference {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .where,
    .note {
      display: block;
    }

    .low {
      color: var(--color-warning-text, var(--color-text));
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
    }

    .hint {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
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

    .negative {
      color: var(--color-danger);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StockPage {
  private readonly inventory = inject(InventoryAdminService);
  private readonly toasts = inject(ToastService);

  // ---- Tracking a listing (Step 28B, deliverable 17) ---------------------------------------------

  private readonly catalog = inject(CatalogAdminService);

  protected readonly tracking = signal(false);
  protected readonly trackError = signal<string | null>(null);
  protected readonly trackListing = signal<string | null>(null);
  protected readonly trackWarehouse = signal('');

  /** Every warehouse, for the picker. There are few enough that paging one is a control nobody uses. */
  protected readonly warehouses = this.inventory.warehouses({}, 100);

  /**
   * Finds listings for the picker.
   *
   * An arrow property rather than a method, because it is passed into a component input and a bound
   * method would lose its `this`.
   */
  protected readonly listingSearch = (term: string): Observable<readonly EntityOption[]> =>
    this.catalog.searchListings(term).pipe(
      map((listings) =>
        listings.map((listing) => ({
          id: listing.id,
          label: listing.productName,
          hint: `${listing.sku} · ${listing.status}`,
        })),
      ),
    );

  /**
   * The reasons an operator may choose.
   *
   * A subset of `StockMovementReason`, not all of it: `Sale`, `Reservation`, `Release`, `TransferIn`
   * and `TransferOut` are written by the platform as a consequence of something happening, and
   * offering them here would let somebody hand-write a sale that no order explains.
   */
  protected readonly adjustReasons: readonly StockMovementReason[] = [
    'Adjustment',
    'Damage',
    'Correction',
    'Return',
  ];

  protected readonly list = this.inventory.stock();
  /** Reference data for the warehouse filter. Small, bounded, and read straight off the store. */
  private readonly warehouseList = this.inventory.warehouses({ activeOnly: true }, 200);
  protected readonly values = signal<FilterValues>({});
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected readonly ledgerFor = signal<StockItemResponse | null>(null);
  protected readonly ledger = signal<ReturnType<InventoryAdminService['ledger']> | null>(null);
  protected readonly adjustFor = signal<StockItemResponse | null>(null);
  protected readonly settingsFor = signal<StockItemResponse | null>(null);

  protected readonly adjustReason = signal<StockMovementReason>('Adjustment');
  protected readonly allowBackorder = signal(false);
  protected readonly allowPreorder = signal(false);

  private readonly adjustSubmitted = signal(false);
  private readonly settingsSubmitted = signal(false);

  protected readonly adjustForm = formGroup(this.adjustSubmitted, {
    change: formField('', [required('A change')], this.adjustSubmitted),
    note: formField('', [], this.adjustSubmitted),
  });

  protected readonly settingsForm = formGroup(this.settingsSubmitted, {
    reorderLevel: formField('0', [], this.settingsSubmitted),
    reorderQuantity: formField('0', [], this.settingsSubmitted),
  });

  protected readonly ledgerRows = computed<readonly StockLedgerEntryResponse[]>(
    () => this.ledger()?.rows() ?? [],
  );

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: StockItemResponse) => row.id;
  protected readonly rowLabel = (row: StockItemResponse) => `${row.sku} at ${row.warehouseCode}`;

  protected readonly columns: readonly DataTableColumn<StockItemResponse>[] = [
    { key: 'sku', label: 'SKU', kind: 'custom' },
    { key: 'quantityOnHand', label: 'On hand', kind: 'number', value: (row) => row.quantityOnHand },
    { key: 'quantityReserved', label: 'Reserved', kind: 'number', value: (row) => row.quantityReserved },
    { key: 'available', label: 'Available', kind: 'custom', numeric: true },
    {
      key: 'reorderLevel',
      label: 'Reorder at',
      kind: 'number',
      value: (row) => row.reorderLevel,
      hiddenByDefault: true,
    },
    { key: 'trackingMode', label: 'Tracking', value: (row) => row.trackingMode, hiddenByDefault: true },
    { key: 'updatedAt', label: 'Last moved', kind: 'date', value: (row) => tableDateTime(row.updatedAt) },
    { key: 'actions', label: 'Actions', kind: 'custom', width: '16rem' },
  ];

  /** The warehouse options come from the API, so the filter cannot offer one that is not there. */
  protected readonly filters = computed<readonly FilterDefinition[]>(() => [
    {
      key: 'warehouseId',
      label: 'Warehouse',
      kind: 'select',
      options: this.warehouseList.rows().map((warehouse) => ({
        value: warehouse.id,
        label: `${warehouse.code} — ${warehouse.name}`,
      })),
    },
    {
      key: 'level',
      label: 'Stock level',
      kind: 'select',
      options: [
        { value: 'low', label: 'Low — at or below reorder' },
        { value: 'out', label: 'Out of stock' },
      ],
    },
  ]);

  constructor() {
    this.list.load();
    this.warehouseList.load();
  }

  protected when(entry: StockLedgerEntryResponse): string {
    return tableDateTime(entry.occurredAt);
  }

  protected reference(entry: StockLedgerEntryResponse): string {
    if (!entry.referenceType) return '—';
    return entry.referenceId ? `${entry.referenceType} ${entry.referenceId}` : entry.referenceType;
  }

  protected startTracking(): void {
    this.trackError.set(null);
    this.trackListing.set(null);
    this.trackWarehouse.set('');
    this.warehouses.load();
    this.tracking.set(true);
  }

  protected track(): void {
    const listingId = this.trackListing();
    const warehouseId = this.trackWarehouse();
    if (!listingId || !warehouseId || this.busy()) return;

    this.busy.set(true);
    this.trackError.set(null);

    this.inventory.openStockItem({ listingId, warehouseId }).subscribe({
      next: () => {
        this.busy.set(false);
        this.tracking.set(false);
        this.toasts.success('That listing is now tracked, at zero.');
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.trackError.set(describeError(error, 'That listing could not be tracked.'));
      },
    });
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const level = values['level'];
    const filters: StockFilters = {
      warehouseId: values['warehouseId'],
      lowStock: level === 'low' ? true : undefined,
      outOfStock: level === 'out' ? true : undefined,
      search: values['q'],
    };
    this.list.setFilters(filters);
  }

  protected openLedger(item: StockItemResponse): void {
    this.ledgerFor.set(item);
    const ledger = this.inventory.ledger(item.id);
    this.ledger.set(ledger);
    ledger.load();
  }

  protected closeLedger(): void {
    this.ledger()?.clear();
    this.ledger.set(null);
    this.ledgerFor.set(null);
  }

  protected openAdjust(item: StockItemResponse): void {
    this.adjustFor.set(item);
    this.adjustReason.set('Adjustment');
    this.adjustForm.reset({ change: '', note: '' });
  }

  protected adjust(): void {
    const item = this.adjustFor();
    if (!item || !this.adjustForm.submit() || this.busy()) return;

    const values = this.adjustForm.values();
    const change = Number(values.change);
    if (!Number.isFinite(change) || change === 0) {
      this.actionError.set('A movement of zero is not a movement. Enter a positive or negative number.');
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);

    this.inventory
      .adjust({
        stockItemId: item.id,
        change,
        reason: this.adjustReason(),
        note: values.note || null,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.adjustFor.set(null);
          this.toasts.success('Movement recorded.');
          this.list.refresh();
          if (this.ledgerFor()?.id === item.id) this.ledger()?.load();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(describeError(error, 'That movement was refused.'));
        },
      });
  }

  protected openSettings(item: StockItemResponse): void {
    this.settingsFor.set(item);
    this.allowBackorder.set(item.allowBackorder);
    this.allowPreorder.set(item.allowPreorder);
    this.settingsForm.reset({
      reorderLevel: String(item.reorderLevel),
      reorderQuantity: String(item.reorderQuantity),
    });
  }

  protected saveSettings(): void {
    const item = this.settingsFor();
    if (!item || this.busy()) return;

    const values = this.settingsForm.values();
    this.busy.set(true);
    this.actionError.set(null);

    this.inventory
      .configure(item.id, {
        reorderLevel: Number(values.reorderLevel || 0),
        reorderQuantity: Number(values.reorderQuantity || 0),
        allowBackorder: this.allowBackorder(),
        allowPreorder: this.allowPreorder(),
        preorderAvailableAt: item.preorderAvailableAt,
        trackingMode: item.trackingMode,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.settingsFor.set(null);
          this.toasts.success('Settings saved.');
          this.list.refresh();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(describeError(error, 'Those settings could not be saved.'));
        },
      });
  }
}
