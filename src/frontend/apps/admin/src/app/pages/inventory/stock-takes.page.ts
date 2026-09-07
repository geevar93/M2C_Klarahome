import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { InventoryAdminService, StockTakeFilters, StockTakeResponse } from '@klarahome/data-access-admin';
import {
  CellTemplate,
  ConfirmDialog,
  DataTable,
  DataTableColumn,
  EntityDrawer,
  FilterBar,
  FilterDefinition,
  FilterValues,
  PageHeader,
  toneFor,
} from '@klarahome/ui-admin';
import { Alert, Button, Control, Field, Icon } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDate } from '../../core/format';

/** A line being counted. The count is a string because a half-typed number is not a number. */
interface CountLine {
  readonly stockItemId: string;
  readonly sku: string;
  readonly expected: number;
  counted: string;
  note: string;
}

/**
 * Counting what is actually on the shelves.
 *
 * A stock take is the one process on this platform where the *system* is presumed wrong, so the
 * screen is built around not prejudicing the count:
 *
 *  - **The expected quantity is shown, and the count starts blank.** Pre-filling with the expected
 *    number produces a count sheet somebody tabs through — which is not a count. Blank means every
 *    number on the sheet was typed by somebody who looked at a shelf.
 *  - **The variance appears as you type**, because a −40 that should have been −4 is caught here,
 *    at the shelf, and not by an accountant in three weeks.
 *  - **Counts save as often as you like; submitting is the irreversible step.** Only submitting
 *    writes the variances to the ledger, as `Correction` movements. Until then a stock take is a
 *    piece of paper.
 *
 * The count sheet is not paged. A stock take covers a chosen set of items and the endpoint returns
 * all of them, which is right: a counter with a Next button loses their place.
 */
@Component({
  selector: 'kh-stock-takes-page',
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
    PageHeader,
  ],
  template: `
    <kh-page-header
      heading="Stock takes"
      description="Counting the shelves, and correcting the platform where the two disagree."
    >
      <button khButton type="button" variant="primary" (click)="startCreate()">
        <kh-icon name="plus" size="sm" />
        New stock take
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Stock takes could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not work" [dismissible]="true">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Stock takes"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      emptyMessage="No stock take matches these filters."
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
    </kh-data-table>

    @if (creating()) {
      <kh-entity-drawer
        heading="New stock take"
        subtitle="Every tracked item in the warehouse is counted unless you narrow it later."
        (closed)="creating.set(false)"
      >
        <kh-field label="Warehouse" for="take-warehouse">
          <select
            khControl
            id="take-warehouse"
            [value]="warehouseId()"
            (change)="warehouseId.set($any($event.target).value)"
          >
            <option value="">Choose a warehouse</option>
            @for (warehouse of warehouses.rows(); track warehouse.id) {
              <option [value]="warehouse.id">{{ warehouse.code }} — {{ warehouse.name }}</option>
            }
          </select>
        </kh-field>

        <kh-field label="Scheduled for" for="take-scheduled" [optional]="true">
          <input
            khControl
            id="take-scheduled"
            type="date"
            [value]="scheduledFor()"
            (input)="scheduledFor.set($any($event.target).value)"
          />
        </kh-field>

        <kh-field label="Notes" for="take-notes" [optional]="true">
          <textarea
            khControl
            id="take-notes"
            rows="2"
            [value]="notes()"
            (input)="notes.set($any($event.target).value)"
          ></textarea>
        </kh-field>

        <div slot="footer">
          <button khButton type="button" variant="tertiary" (click)="creating.set(false)">Cancel</button>
          <button
            khButton
            type="button"
            variant="primary"
            [disabled]="busy() || !warehouseId()"
            (click)="create()"
          >
            Start counting
          </button>
        </div>
      </kh-entity-drawer>
    }

    @if (viewing(); as take) {
      <kh-entity-drawer
        [heading]="'Stock take ' + take.number"
        [subtitle]="take.status + ' · ' + take.lines.length + ' items'"
        (closed)="viewing.set(null)"
      >
        @if (take.status === 'Submitted') {
          <kh-alert tone="info" heading="This count is closed">
            Its variances have been written to the stock ledger as corrections. It cannot be changed.
          </kh-alert>
        }

        <table>
          <thead>
            <tr>
              <th scope="col">SKU</th>
              <th scope="col" class="numeric">Expected</th>
              <th scope="col">Counted</th>
              <th scope="col" class="numeric">Variance</th>
              <th scope="col">Note</th>
            </tr>
          </thead>
          <tbody>
            @for (line of countLines(); track line.stockItemId; let index = $index) {
              <tr>
                <td>{{ line.sku }}</td>
                <td class="numeric">{{ line.expected }}</td>
                <td>
                  @if (editable()) {
                    <input
                      khControl
                      khNumeric
                      [id]="'count-' + index"
                      [attr.aria-label]="'Counted quantity for ' + line.sku"
                      type="number"
                      min="0"
                      [value]="line.counted"
                      (input)="setCount(index, 'counted', $any($event.target).value)"
                    />
                  } @else {
                    {{ line.counted || '—' }}
                  }
                </td>
                <td class="numeric" [class.negative]="varianceOf(line) < 0">
                  {{ line.counted === '' ? '—' : (varianceOf(line) > 0 ? '+' : '') + varianceOf(line) }}
                </td>
                <td>
                  @if (editable()) {
                    <input
                      khControl
                      [id]="'count-note-' + index"
                      [attr.aria-label]="'Note for ' + line.sku"
                      type="text"
                      [value]="line.note"
                      (input)="setCount(index, 'note', $any($event.target).value)"
                    />
                  } @else {
                    {{ line.note || '—' }}
                  }
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="5" class="hint">Nothing to count in this warehouse.</td>
              </tr>
            }
          </tbody>
        </table>

        @if (countedSoFar() > 0) {
          <p class="hint">
            {{ countedSoFar() }} of {{ countLines().length }} counted · net variance
            {{ netVariance() > 0 ? '+' : '' }}{{ netVariance() }} units.
          </p>
        }

        <div slot="footer">
          @if (editable()) {
            <button khButton type="button" size="sm" [disabled]="busy()" (click)="saveCounts()">
              Save the counts
            </button>
            <button
              khButton
              type="button"
              size="sm"
              variant="primary"
              [disabled]="busy() || countedSoFar() === 0"
              (click)="submitting.set(true)"
            >
              Submit and correct stock
            </button>
            <button
              khButton
              type="button"
              size="sm"
              variant="danger"
              [disabled]="busy()"
              (click)="cancelling.set(true)"
            >
              Abandon
            </button>
          }
        </div>
      </kh-entity-drawer>
    }

    <kh-confirm-dialog
      [open]="submitting()"
      heading="Submit this count"
      [message]="
        'Every variance becomes a correction on the stock ledger and the quantities change. There is no undo — a mistake has to be corrected by another movement. Net change: ' +
        (netVariance() > 0 ? '+' : '') +
        netVariance() +
        ' units.'
      "
      confirmLabel="Submit the count"
      tone="warning"
      [confirmPhrase]="viewing()?.number ?? null"
      [busy]="busy()"
      (confirmed)="submit()"
      (cancelled)="submitting.set(false)"
    />

    <kh-confirm-dialog
      [open]="cancelling()"
      heading="Abandon this stock take"
      message="The counts entered so far are discarded and no stock changes."
      confirmLabel="Abandon"
      [busy]="busy()"
      (confirmed)="abandon()"
      (cancelled)="cancelling.set(false)"
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
export class StockTakesPage {
  private readonly inventory = inject(InventoryAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly list = this.inventory.stockTakes();
  protected readonly warehouses = this.inventory.warehouses({ activeOnly: true }, 200);

  protected readonly values = signal<FilterValues>({});
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected readonly creating = signal(false);
  protected readonly viewing = signal<StockTakeResponse | null>(null);
  protected readonly submitting = signal(false);
  protected readonly cancelling = signal(false);

  protected readonly warehouseId = signal('');
  protected readonly scheduledFor = signal('');
  protected readonly notes = signal('');
  protected readonly countLines = signal<readonly CountLine[]>([]);

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: StockTakeResponse) => row.id;
  protected readonly rowLabel = (row: StockTakeResponse) => row.number;

  /** Draft and Counting accept counts; Submitted and Cancelled are closed. */
  protected readonly editable = computed(() => {
    const status = this.viewing()?.status;
    return status === 'Draft' || status === 'Counting';
  });

  protected readonly countedSoFar = computed(
    () => this.countLines().filter((line) => line.counted !== '').length,
  );

  protected readonly netVariance = computed(() =>
    this.countLines()
      .filter((line) => line.counted !== '')
      .reduce((total, line) => total + this.varianceOf(line), 0),
  );

  protected readonly columns: readonly DataTableColumn<StockTakeResponse>[] = [
    { key: 'number', label: 'Number', kind: 'custom', width: '10rem' },
    {
      key: 'status',
      label: 'Status',
      kind: 'badge',
      value: (row) => row.status,
      tone: (row) => toneFor(row.status),
      width: '9rem',
    },
    { key: 'lines', label: 'Items', kind: 'number', value: (row) => row.lines.length },
    { key: 'scheduledFor', label: 'Scheduled', kind: 'date', value: (row) => tableDate(row.scheduledFor) },
    { key: 'submittedAt', label: 'Submitted', kind: 'date', value: (row) => tableDate(row.submittedAt) },
  ];

  protected readonly filters = computed<readonly FilterDefinition[]>(() => [
    {
      key: 'status',
      label: 'Status',
      kind: 'select',
      options: [
        { value: 'Draft', label: 'Draft' },
        { value: 'Counting', label: 'Being counted' },
        { value: 'Submitted', label: 'Submitted' },
        { value: 'Cancelled', label: 'Abandoned' },
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
    this.warehouses.load();
  }

  protected varianceOf(line: CountLine): number {
    if (line.counted === '') return 0;
    return Number(line.counted) - line.expected;
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: StockTakeFilters = {
      status: values['status'],
      warehouseId: values['warehouseId'],
    };
    this.list.setFilters(filters);
  }

  protected startCreate(): void {
    this.warehouseId.set('');
    this.scheduledFor.set('');
    this.notes.set('');
    this.creating.set(true);
  }

  protected create(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);

    this.inventory
      .createStockTake({
        warehouseId: this.warehouseId(),
        scheduledFor: this.scheduledFor() || null,
        notes: this.notes() || null,
        // Null means every tracked item in the warehouse, which is what a full count is.
        stockItemIds: null,
      })
      .subscribe({
        next: (created) => {
          this.busy.set(false);
          this.creating.set(false);
          this.show(created);
          this.toasts.success(`Stock take ${created.number} started.`);
          this.list.refresh();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(describeError(error, 'That stock take could not be started.'));
        },
      });
  }

  protected open(take: StockTakeResponse): void {
    this.show(take);
  }

  protected setCount(index: number, field: 'counted' | 'note', value: string): void {
    this.countLines.update((current) =>
      current.map((line, at) => (at === index ? { ...line, [field]: value } : line)),
    );
  }

  protected saveCounts(): void {
    const take = this.viewing();
    if (!take || this.busy()) return;

    const lines = this.countLines()
      .filter((line) => line.counted !== '')
      .map((line) => ({
        stockItemId: line.stockItemId,
        countedQuantity: Number(line.counted),
        note: line.note || null,
      }));

    if (lines.length === 0) {
      this.actionError.set('Nothing has been counted yet.');
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);

    this.inventory.countStockTake(take.id, { lines }).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.show(updated);
        this.toasts.success('Counts saved.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'Those counts could not be saved.'));
      },
    });
  }

  protected submit(): void {
    const take = this.viewing();
    if (!take || this.busy()) return;

    this.submitting.set(false);
    this.busy.set(true);
    this.actionError.set(null);

    // Saved first, so what is submitted is what is on screen. Submitting a stock take whose last
    // three counts were still in the browser would correct the wrong quantities.
    const lines = this.countLines()
      .filter((line) => line.counted !== '')
      .map((line) => ({
        stockItemId: line.stockItemId,
        countedQuantity: Number(line.counted),
        note: line.note || null,
      }));

    this.inventory.countStockTake(take.id, { lines }).subscribe({
      next: () => {
        this.inventory.submitStockTake(take.id).subscribe({
          next: (updated) => {
            this.busy.set(false);
            this.show(updated);
            this.toasts.success('Counted, and the stock corrected.');
            this.list.refresh();
          },
          error: (error: unknown) => {
            this.busy.set(false);
            this.actionError.set(describeError(error, 'The count could not be submitted.'));
          },
        });
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(
          describeError(error, 'The counts could not be saved, so nothing was submitted.'),
        );
      },
    });
  }

  protected abandon(): void {
    const take = this.viewing();
    if (!take || this.busy()) return;

    this.cancelling.set(false);
    this.busy.set(true);

    this.inventory.cancelStockTake(take.id).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.show(updated);
        this.toasts.success('Stock take abandoned.');
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That stock take could not be abandoned.'));
      },
    });
  }

  /**
   * Puts a stock take on screen.
   *
   * A counted quantity that is already recorded is shown; one that is not stays **blank** rather
   * than falling back to the expected number — see the class remarks for why that is the whole
   * design of the sheet.
   */
  private show(take: StockTakeResponse): void {
    this.viewing.set(take);
    this.countLines.set(
      take.lines.map((line) => ({
        stockItemId: line.stockItemId,
        sku: line.sku,
        expected: line.expectedQuantity,
        counted: line.countedQuantity === null ? '' : String(line.countedQuantity),
        note: line.note ?? '',
      })),
    );
  }
}
