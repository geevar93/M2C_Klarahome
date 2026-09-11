import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  InventoryAdminService,
  StockItemResponse,
  StockLedgerEntryResponse,
  StockMovementReason,
} from '@klarahome/data-access-admin';
import { FilterBar, FilterValues, PageHeader } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Control, Field, Skeleton } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';

/**
 * Correcting a quantity, and moving stock between warehouses.
 *
 * The stock screen can adjust a row it is already showing; this is the other direction — somebody
 * is holding a physical thing and needs to find its row. So it opens on a search, and everything
 * else appears once one row is chosen.
 *
 * **A transfer is one request, not two adjustments.** `POST /admin/stock/transfer` writes a
 * `TransferOut` and a `TransferIn` together and answers both rows. Doing it as two adjustments
 * from a browser would leave the stock nowhere at all if the second one failed — and it fails
 * exactly when the network does, which is when somebody is standing in a warehouse.
 *
 * **The ledger under it is not decoration.** Every movement made here appears in it immediately,
 * which is how the operator confirms they typed −3 and not −30 before walking away.
 */
@Component({
  selector: 'kh-adjustments-page',
  imports: [Alert, Badge, Button, Control, Field, FilterBar, PageHeader, Skeleton],
  template: `
    <kh-page-header
      heading="Adjustments and transfers"
      description="Correct a count, or move stock from one warehouse to another. Both are recorded as movements with a reason."
    />

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not work" [dismissible]="true">{{ message }}</kh-alert>
    }

    <section class="panel">
      <h2>Find the stock</h2>
      <kh-filter-bar
        [filters]="[]"
        [values]="values()"
        searchLabel="Search by SKU"
        (changed)="search($event)"
      />

      @if (list.loading()) {
        <kh-skeleton height="8rem" />
      } @else if (list.error(); as message) {
        <kh-alert tone="danger" heading="The search failed">{{ message }}</kh-alert>
      } @else if (list.rows().length > 0) {
        <ul class="results">
          @for (item of list.rows(); track item.id) {
            <li>
              <button
                type="button"
                class="result"
                [class.chosen]="chosen()?.id === item.id"
                [attr.aria-pressed]="chosen()?.id === item.id"
                (click)="choose(item)"
              >
                <span class="sku">{{ item.sku }}</span>
                <span class="where">{{ item.warehouseCode }}</span>
                <span class="counts">
                  {{ item.quantityOnHand }} on hand · {{ item.quantityAvailable }} available
                </span>
                @if (item.isLow) {
                  <kh-badge tone="warning">Low</kh-badge>
                }
              </button>
            </li>
          }
        </ul>
      } @else if (searched()) {
        <p class="hint">Nothing matches that SKU. Check the warehouse is tracking it.</p>
      }
    </section>

    @if (chosen(); as item) {
      <div class="columns">
        <section class="panel">
          <h2>Adjust</h2>
          <p class="hint">
            {{ item.sku }} at {{ item.warehouseCode }} — {{ item.quantityOnHand }} on hand. Positive adds,
            negative removes.
          </p>

          <kh-field label="Change" for="adjustment-change" [error]="adjustForm.fields.change.error()">
            <input
              khControl
              khNumeric
              id="adjustment-change"
              type="number"
              step="1"
              [value]="adjustForm.fields.change.value()"
              (input)="adjustForm.fields.change.set($any($event.target).value)"
              (touched)="adjustForm.fields.change.markTouched()"
            />
          </kh-field>

          <kh-field label="Reason" for="adjustment-reason">
            <select
              khControl
              id="adjustment-reason"
              [value]="reason()"
              (change)="reason.set($any($event.target).value)"
            >
              @for (option of reasons; track option) {
                <option [value]="option">{{ option }}</option>
              }
            </select>
          </kh-field>

          <kh-field label="Note" for="adjustment-note" hint="Goes on the ledger row, and stays there.">
            <input
              khControl
              id="adjustment-note"
              type="text"
              maxlength="200"
              [value]="adjustForm.fields.note.value()"
              (input)="adjustForm.fields.note.set($any($event.target).value)"
            />
          </kh-field>

          <button khButton type="button" variant="primary" [disabled]="busy()" (click)="adjust()">
            Record the movement
          </button>
        </section>

        <section class="panel">
          <h2>Transfer</h2>
          <p class="hint">
            Moves {{ item.sku }} out of {{ item.warehouseCode }} and into another warehouse, in one step — so
            it is never in neither.
          </p>

          <kh-field
            label="To warehouse"
            for="transfer-to"
            [error]="transferForm.fields.toWarehouseId.error()"
          >
            <select
              khControl
              id="transfer-to"
              [value]="transferForm.fields.toWarehouseId.value()"
              (change)="transferForm.fields.toWarehouseId.set($any($event.target).value)"
              (touched)="transferForm.fields.toWarehouseId.markTouched()"
            >
              <option value="">Choose a warehouse</option>
              @for (warehouse of destinations(); track warehouse.id) {
                <option [value]="warehouse.id">{{ warehouse.code }} — {{ warehouse.name }}</option>
              }
            </select>
          </kh-field>

          <kh-field
            label="Quantity"
            for="transfer-quantity"
            [error]="transferForm.fields.quantity.error()"
            [hint]="'At most ' + item.quantityAvailable + ' — reserved units cannot be moved.'"
          >
            <input
              khControl
              khNumeric
              id="transfer-quantity"
              type="number"
              min="1"
              [attr.max]="item.quantityAvailable"
              [value]="transferForm.fields.quantity.value()"
              (input)="transferForm.fields.quantity.set($any($event.target).value)"
              (touched)="transferForm.fields.quantity.markTouched()"
            />
          </kh-field>

          <kh-field label="Note" for="transfer-note" [optional]="true">
            <input
              khControl
              id="transfer-note"
              type="text"
              maxlength="200"
              [value]="transferForm.fields.note.value()"
              (input)="transferForm.fields.note.set($any($event.target).value)"
            />
          </kh-field>

          <button khButton type="button" variant="primary" [disabled]="busy()" (click)="transfer()">
            Move the stock
          </button>
        </section>
      </div>

      <section class="panel">
        <h2>What has happened to it</h2>

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
              <th scope="col">Who</th>
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
                <td class="note">{{ entry.actorId ?? 'the platform' }}</td>
              </tr>
            } @empty {
              <tr>
                <td colspan="5" class="hint">Nothing has moved yet.</td>
              </tr>
            }
          </tbody>
        </table>

        <div class="pager">
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
      </section>
    }
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .panel {
      margin-block-end: var(--space-5);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .panel h2 {
      margin: 0 0 var(--space-3);
      font-size: var(--text-lg);
    }

    .columns {
      display: grid;
      gap: var(--space-4);
    }

    @media (min-width: 1024px) {
      .columns {
        grid-template-columns: 1fr 1fr;
      }
    }

    .hint {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .results {
      display: grid;
      gap: var(--space-2);
      margin: var(--space-3) 0 0;
      padding: 0;
      list-style: none;
    }

    .result {
      display: flex;
      gap: var(--space-3);
      align-items: baseline;
      inline-size: 100%;
      padding: var(--space-2) var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: none;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .result.chosen {
      border-color: var(--color-primary);
      box-shadow: 0 0 0 1px var(--color-primary);
    }

    .sku {
      font-weight: var(--weight-medium);
    }

    .where,
    .counts,
    .note {
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

    .negative {
      color: var(--color-danger);
    }

    .pager {
      display: flex;
      gap: var(--space-2);
      justify-content: flex-end;
      margin-block-start: var(--space-3);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdjustmentsPage {
  private readonly inventory = inject(InventoryAdminService);
  private readonly toasts = inject(ToastService);

  /** As on the stock screen: only the reasons a person may author. See `StockPage` for why. */
  protected readonly reasons: readonly StockMovementReason[] = [
    'Adjustment',
    'Damage',
    'Correction',
    'Return',
  ];

  protected readonly list = this.inventory.stock({}, 10);
  private readonly warehouseList = this.inventory.warehouses({ activeOnly: true }, 200);

  protected readonly values = signal<FilterValues>({});
  protected readonly searched = signal(false);
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected readonly chosen = signal<StockItemResponse | null>(null);
  protected readonly ledger = signal<ReturnType<InventoryAdminService['ledger']> | null>(null);
  protected readonly reason = signal<StockMovementReason>('Adjustment');

  private readonly adjustSubmitted = signal(false);
  private readonly transferSubmitted = signal(false);

  protected readonly adjustForm = formGroup(this.adjustSubmitted, {
    change: formField('', [required('A change')], this.adjustSubmitted),
    note: formField('', [], this.adjustSubmitted),
  });

  protected readonly transferForm = formGroup(this.transferSubmitted, {
    toWarehouseId: formField('', [required('A destination')], this.transferSubmitted),
    quantity: formField('', [required('A quantity')], this.transferSubmitted),
    note: formField('', [], this.transferSubmitted),
  });

  protected readonly ledgerRows = computed<readonly StockLedgerEntryResponse[]>(
    () => this.ledger()?.rows() ?? [],
  );

  /** Anywhere but here — a transfer to the warehouse the stock is already in is not a movement. */
  protected readonly destinations = computed(() =>
    this.warehouseList.rows().filter((warehouse) => warehouse.id !== this.chosen()?.warehouseId),
  );

  constructor() {
    this.warehouseList.load();
  }

  protected when(entry: StockLedgerEntryResponse): string {
    return tableDateTime(entry.occurredAt);
  }

  protected search(values: FilterValues): void {
    this.values.set(values);
    const term = values['q'];
    if (!term) {
      this.searched.set(false);
      this.list.clear();
      return;
    }
    this.searched.set(true);
    this.list.setFilters({ search: term });
  }

  protected choose(item: StockItemResponse): void {
    this.chosen.set(item);
    this.adjustForm.reset({ change: '', note: '' });
    this.transferForm.reset({ toWarehouseId: '', quantity: '', note: '' });
    this.reason.set('Adjustment');

    const ledger = this.inventory.ledger(item.id, {}, 15);
    this.ledger.set(ledger);
    ledger.load();
  }

  protected adjust(): void {
    const item = this.chosen();
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
      .adjust({ stockItemId: item.id, change, reason: this.reason(), note: values.note || null })
      .subscribe({
        next: (updated) => {
          this.busy.set(false);
          this.chosen.set(updated);
          this.adjustForm.reset({ change: '', note: '' });
          this.toasts.success('Movement recorded.');
          this.ledger()?.load();
          this.list.refresh();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(describeError(error, 'That movement was refused.'));
        },
      });
  }

  protected transfer(): void {
    const item = this.chosen();
    if (!item || !this.transferForm.submit() || this.busy()) return;

    const values = this.transferForm.values();
    const quantity = Number(values.quantity);
    if (!Number.isFinite(quantity) || quantity <= 0) {
      this.actionError.set('Enter how many units to move.');
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);

    this.inventory
      .transfer({
        listingId: item.listingId,
        fromWarehouseId: item.warehouseId,
        toWarehouseId: values.toWarehouseId,
        quantity,
        note: values.note || null,
      })
      .subscribe({
        next: (moved) => {
          this.busy.set(false);
          // The response carries both sides; the source row is the one this screen is showing.
          const source = moved.find((row) => row.warehouseId === item.warehouseId);
          if (source) this.chosen.set(source);
          this.transferForm.reset({ toWarehouseId: '', quantity: '', note: '' });
          this.toasts.success(`${quantity} moved.`);
          this.ledger()?.load();
          this.list.refresh();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(describeError(error, 'That transfer was refused.'));
        },
      });
  }
}
