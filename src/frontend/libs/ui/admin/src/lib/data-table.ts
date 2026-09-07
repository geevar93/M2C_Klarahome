import {
  ChangeDetectionStrategy,
  Component,
  Directive,
  TemplateRef,
  computed,
  contentChildren,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Badge, Button, Checkbox, Icon, Skeleton } from '@klarahome/ui-primitives';
import { BrowserStorage } from '@klarahome/util';

import { BulkAction, DataTableColumn, TablePage, TableSort } from './admin.model';

/**
 * Projects the cell for one column.
 *
 * ```html
 * <ng-template khCell="status" let-row>
 *   <kh-status-badge [status]="row.status" />
 * </ng-template>
 * ```
 *
 * A column definition is *data* — it is built in a `.ts` file, kept in a constant and sometimes
 * comes off the API — so it cannot carry a `TemplateRef`. This directive is how a `custom` column
 * gets one without the definition stopping being data.
 */
@Directive({ selector: 'ng-template[khCell]' })
export class CellTemplate {
  /** The `key` of the column this template draws. */
  readonly khCell = input.required<string>();
  readonly template = inject<TemplateRef<{ $implicit: unknown; index: number }>>(TemplateRef);
}

/**
 * The admin data table.
 *
 * One component behind every list screen in the back office, and the reason it is one component is
 * that all of them get the same six things wrong when written by hand: the header that scrolls
 * away, the checkbox that selects rows the filter has since removed, the sort that asks the API
 * for an order it does not support, the column chooser that forgets, the bulk bar that hides the
 * last row, and the pager that promises "page 7 of 42" over an API that pages by keyset.
 *
 * **It fetches nothing.** Rows, sort, page and loading state are inputs; every control emits what
 * the caller should ask the API for next. That is what lets one table serve an endpoint that
 * filters server-side and a screen that already has its rows, and what lets it be rendered in a
 * test without a server.
 *
 * Three decisions are worth naming:
 *
 *  - **Paging is Previous/Next over cursors**, never page numbers. See `TablePage`.
 *  - **Selection is cleared whenever the rows change** — a bulk action carried across a filter
 *    change would act on rows the user can no longer see, which is how "delete 12 selected" ends
 *    up deleting something else.
 *  - **Sorting is offered only where the API accepts it** (`DataTableColumn.sortKey`), because a
 *    header that produces a 400 is worse than a header that does not move.
 */
@Component({
  selector: 'kh-data-table',
  imports: [NgTemplateOutlet, Badge, Button, Checkbox, Icon, Skeleton],
  template: `
    <div class="toolbar">
      <div class="toolbar-lead">
        <ng-content select="[slot=filters]" />
      </div>

      <div class="toolbar-actions">
        @if (exportMode() !== 'none') {
          <button khButton type="button" size="sm" (click)="requestExport()">
            <kh-icon name="download" size="sm" />
            Export CSV
          </button>
        }

        @if (configurable()) {
          <div class="chooser">
            <button
              khButton
              type="button"
              size="sm"
              [attr.aria-expanded]="chooserOpen()"
              aria-haspopup="true"
              (click)="chooserOpen.set(!chooserOpen())"
            >
              <kh-icon name="filter" size="sm" />
              Columns
            </button>

            @if (chooserOpen()) {
              <!-- A popover rather than a dialog: it changes what is on screen behind it, and
                   trapping focus away from the table while choosing that table's columns is the
                   wrong model. Escape closes it. -->
              <div
                class="chooser-panel"
                role="group"
                aria-label="Choose columns"
                (keydown)="onChooserKeydown($event)"
              >
                @for (column of columns(); track column.key) {
                  <kh-checkbox
                    [label]="column.label"
                    [checked]="isVisible(column.key)"
                    [inputId]="'col-' + instanceId + '-' + column.key"
                    (checkedChange)="toggleColumn(column.key, $event)"
                  />
                }
              </div>
            }
          </div>
        }

        <ng-content select="[slot=actions]" />
      </div>
    </div>

    @if (selectable() && selectedCount() > 0) {
      <!-- Announced, because the number of [selected]="true" rows changes without anything moving on
           screen for somebody selecting with the keyboard. -->
      <div class="bulk-bar" role="status">
        <span class="bulk-count">{{ selectedCount() }} selected</span>

        @for (action of bulkActions(); track action.key) {
          <button
            khButton
            type="button"
            size="sm"
            [variant]="action.destructive ? 'danger' : 'secondary'"
            [disabled]="!!action.disabledReason"
            [attr.title]="action.disabledReason"
            (click)="bulkAction.emit({ key: action.key, ids: selectedIds() })"
          >
            {{ action.label }}
          </button>
        }

        <button khButton type="button" size="sm" variant="tertiary" (click)="clearSelection()">Clear</button>
      </div>
    }

    <div class="scroll">
      <table [attr.aria-label]="label()" [attr.aria-busy]="loading()">
        <thead>
          <tr>
            @if (selectable()) {
              <th scope="col" class="select-cell">
                <!-- A [bare]="true" input with an aria-label rather than kh-checkbox: a table cell has
                     no room for a visible label, and this is the one control on the platform that
                     needs the third, indeterminate state. -->
                <input
                  type="checkbox"
                  aria-label="Select all rows on this page"
                  [checked]="allOnPageSelected()"
                  [indeterminate]="someOnPageSelected()"
                  (change)="toggleAllOnPage(isChecked($event))"
                />
              </th>
            }

            @for (column of visibleColumns(); track column.key) {
              <th
                scope="col"
                [style.width]="column.width"
                [class.numeric]="isNumeric(column)"
                [attr.aria-sort]="ariaSort(column)"
              >
                @if (column.sortKey; as sortKey) {
                  <button type="button" class="sort" (click)="toggleSort(sortKey)">
                    {{ column.label }}
                    <kh-icon [name]="sortIcon(sortKey)" size="sm" />
                  </button>
                } @else {
                  {{ column.label }}
                }
              </th>
            }
          </tr>
        </thead>

        <tbody>
          @if (loading() && rows().length === 0) {
            <!-- Skeleton rows rather than a spinner: the table keeps its shape, so the page does
                 not jump when the rows arrive. -->
            @for (placeholder of skeletonRows(); track placeholder) {
              <tr>
                @if (selectable()) {
                  <td class="select-cell"><kh-skeleton width="1rem" height="1rem" /></td>
                }
                @for (column of visibleColumns(); track column.key) {
                  <td><kh-skeleton height="0.875rem" /></td>
                }
              </tr>
            }
          } @else {
            @for (row of rows(); track rowKey()(row); let index = $index) {
              <tr [class.selected]="isSelected(rowKey()(row))">
                @if (selectable()) {
                  <td class="select-cell">
                    <input
                      type="checkbox"
                      [attr.aria-label]="'Select ' + rowLabel()(row)"
                      [checked]="isSelected(rowKey()(row))"
                      (change)="toggleRow(rowKey()(row), isChecked($event))"
                    />
                  </td>
                }

                @for (column of visibleColumns(); track column.key) {
                  <td [class.numeric]="isNumeric(column)">
                    @if (cellTemplate(column.key); as template) {
                      <ng-container
                        [ngTemplateOutlet]="template"
                        [ngTemplateOutletContext]="{ $implicit: row, index: index }"
                      />
                    } @else if (column.kind === 'badge') {
                      <kh-badge [tone]="column.tone ? column.tone(row) : 'neutral'">{{
                        text(column, row)
                      }}</kh-badge>
                    } @else {
                      {{ text(column, row) }}
                    }
                  </td>
                }
              </tr>
            } @empty {
              <tr>
                <td class="empty" [attr.colspan]="columnCount()">{{ emptyMessage() }}</td>
              </tr>
            }
          }
        </tbody>
      </table>
    </div>

    <div class="pager">
      <p class="count">
        {{ rows().length }} shown
        @if (total(); as rowCount) {
          <span class="muted">· about {{ rowCount }} in total</span>
        }
      </p>

      <div class="pager-controls">
        <button
          khButton
          type="button"
          size="sm"
          [disabled]="!hasPrevious() || loading()"
          (click)="previousPage.emit()"
        >
          <kh-icon name="chevron-left" size="sm" />
          Previous
        </button>
        <button
          khButton
          type="button"
          size="sm"
          [disabled]="!hasNext() || loading()"
          (click)="nextPage.emit()"
        >
          Next
          <kh-icon name="chevron-right" size="sm" />
        </button>
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      align-items: center;
      justify-content: space-between;
      padding: var(--space-3);
      border-block-end: 1px solid var(--color-border);
    }

    .toolbar-lead {
      flex: 1 1 18rem;
      min-width: 0;
    }

    .toolbar-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: center;
    }

    .chooser {
      position: relative;
    }

    .chooser-panel {
      position: absolute;
      inset-inline-end: 0;
      inset-block-start: calc(100% + var(--space-1));
      z-index: var(--z-header);
      min-width: 14rem;
      max-height: 20rem;
      overflow-y: auto;
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-md);
    }

    .bulk-bar {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: center;
      padding: var(--space-2) var(--space-3);
      background: var(--color-primary-subtle);
      border-block-end: 1px solid var(--color-border);
    }

    .bulk-count {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    /* The table is the one element in the admin allowed to scroll sideways: forty columns of
       stock do not fold onto a tablet, and a squeezed table is unreadable long before it is
       unusable. */
    .scroll {
      overflow: auto;
      max-height: 70vh;
    }

    table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--text-sm);
    }

    th,
    td {
      padding: var(--space-2) var(--space-3);
      text-align: start;
      white-space: nowrap;
      border-block-end: 1px solid var(--color-border);
    }

    thead th {
      position: sticky;
      inset-block-start: 0;
      z-index: 1;
      background: var(--color-surface);
      font-weight: var(--weight-medium);
      color: var(--color-text-muted);
    }

    tbody tr.selected {
      background: var(--color-primary-subtle);
    }

    .numeric {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }

    .select-cell {
      width: var(--touch-target-min);
    }

    .select-cell input {
      width: 1.125rem;
      height: 1.125rem;
      accent-color: var(--color-primary);
      cursor: pointer;
    }

    .sort {
      display: inline-flex;
      gap: var(--space-1);
      align-items: center;
      padding: 0;
      border: 0;
      background: none;
      font: inherit;
      color: inherit;
      cursor: pointer;
    }

    .empty {
      padding: var(--space-10) var(--space-3);
      text-align: center;
      color: var(--color-text-muted);
      white-space: normal;
    }

    .pager {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      align-items: center;
      justify-content: space-between;
      padding: var(--space-3);
    }

    .count {
      margin: 0;
      font-size: var(--text-sm);
    }

    .muted {
      color: var(--color-text-muted);
    }

    .pager-controls {
      display: flex;
      gap: var(--space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DataTable<TRow> {
  private readonly storage = inject(BrowserStorage);

  private static sequence = 0;
  /** Distinguishes this table's control ids from another table's on the same screen. */
  protected readonly instanceId = `dt${(DataTable.sequence += 1)}`;

  readonly columns = input.required<readonly DataTableColumn<TRow>[]>();
  readonly rows = input.required<readonly TRow[]>();
  /** The stable identity of a row. Selection, tracking and bulk actions are all keyed on it. */
  readonly rowKey = input.required<(row: TRow) => string>();
  /** How a row is named in its checkbox label — a screen reader reads "Select ORD-1042". */
  readonly rowLabel = input<(row: TRow) => string>((row) => this.rowKey()(row));

  /** The accessible name of the table. Required: "table" alone tells a screen reader nothing. */
  readonly label = input.required<string>();
  readonly page = input<TablePage | null>(null);
  readonly sort = input<TableSort | null>(null);
  readonly loading = input(false);
  readonly emptyMessage = input('Nothing matches these filters.');

  readonly selectable = input(false);
  readonly bulkActions = input<readonly BulkAction[]>([]);

  /**
   * Whether the column chooser is offered, and where its choices are remembered.
   *
   * A `storageKey` makes the choice survive a reload, per browser. It is a convenience and never a
   * dependency: an absent or unreadable value simply means every column is shown.
   */
  readonly configurable = input(false);
  readonly storageKey = input<string | null>(null);

  /**
   * `page` builds a CSV from the rows on screen; `server` asks the caller to fetch the whole set
   * from its own export endpoint. A table showing 50 of 12,000 rows must not offer a button
   * labelled "Export" that silently exports 50.
   */
  readonly exportMode = input<'none' | 'page' | 'server'>('none');

  readonly sortChanged = output<TableSort | null>();
  readonly nextPage = output<void>();
  readonly previousPage = output<void>();
  readonly selectionChanged = output<readonly string[]>();
  readonly bulkAction = output<{ key: string; ids: readonly string[] }>();
  /** Emitted for `exportMode="server"`; the caller calls its own export endpoint. */
  readonly exportRequested = output<void>();

  private readonly cellTemplates = contentChildren(CellTemplate);
  private readonly selection = signal<ReadonlySet<string>>(new Set());
  private readonly hidden = signal<ReadonlySet<string>>(new Set());
  protected readonly chooserOpen = signal(false);

  protected readonly visibleColumns = computed(() =>
    this.columns().filter((column) => !this.hidden().has(column.key)),
  );
  protected readonly columnCount = computed(() => this.visibleColumns().length + (this.selectable() ? 1 : 0));
  protected readonly selectedCount = computed(() => this.selection().size);
  protected readonly selectedIds = computed(() => [...this.selection()]);
  protected readonly skeletonRows = computed(() => Array.from({ length: 6 }, (_, index) => index));
  protected readonly hasNext = computed(() => !!this.page()?.nextCursor);
  protected readonly hasPrevious = computed(() => this.page()?.hasPrevious === true);
  protected readonly total = computed(() => this.page()?.total ?? null);

  protected readonly allOnPageSelected = computed(() => {
    const rows = this.rows();
    const selected = this.selection();
    return rows.length > 0 && rows.every((row) => selected.has(this.rowKey()(row)));
  });

  /** Some but not all — the header tick's third state. */
  protected readonly someOnPageSelected = computed(
    () => this.selectedCount() > 0 && !this.allOnPageSelected(),
  );

  constructor() {
    // The remembered column choice, read per storage key. Read in an effect rather than in the
    // constructor, which is what lets the key be an input at all.
    effect(() => {
      const key = this.storageKey();
      if (!key) return;
      this.hidden.set(new Set(this.storage.getJson<string[]>(`kh.columns.${key}`, [])));
    });

    // A column marked `hiddenByDefault` starts hidden — but only where nothing is remembered,
    // otherwise the remembered choice above would be overwritten on every render.
    effect(() => {
      const defaults = this.columns()
        .filter((column) => column.hiddenByDefault)
        .map((column) => column.key);
      if (defaults.length === 0 || this.storageKey()) return;
      this.hidden.set(new Set(defaults));
    });

    // Rows changing means a new filter, a new page or a refresh. Anything still selected refers
    // to rows that may no longer be on screen, and a bulk action over those is the defect this
    // component exists to prevent.
    effect(() => {
      this.rows();
      this.selection.set(new Set());
    });
  }

  protected isVisible(key: string): boolean {
    return !this.hidden().has(key);
  }

  protected toggleColumn(key: string, visible: boolean): void {
    const next = new Set(this.hidden());
    if (visible) next.delete(key);
    else next.add(key);
    this.hidden.set(next);

    const storageKey = this.storageKey();
    if (storageKey) this.storage.setJson(`kh.columns.${storageKey}`, [...next]);
  }

  protected onChooserKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.stopPropagation();
      this.chooserOpen.set(false);
    }
  }

  protected isSelected(key: string): boolean {
    return this.selection().has(key);
  }

  /** Reads a native checkbox's new state off its change event. */
  protected isChecked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  protected toggleRow(key: string, selected: boolean): void {
    const next = new Set(this.selection());
    if (selected) next.add(key);
    else next.delete(key);
    this.selection.set(next);
    this.selectionChanged.emit([...next]);
  }

  protected toggleAllOnPage(selected: boolean): void {
    const next = selected ? new Set(this.rows().map((row) => this.rowKey()(row))) : new Set<string>();
    this.selection.set(next);
    this.selectionChanged.emit([...next]);
  }

  protected clearSelection(): void {
    this.selection.set(new Set());
    this.selectionChanged.emit([]);
  }

  /**
   * Cycles ascending → descending → unsorted.
   *
   * The third state matters: a list whose natural order is "newest first" cannot be got back to
   * once a column has been sorted, unless the header can also let go.
   */
  protected toggleSort(key: string): void {
    const current = this.sort();
    if (current?.key !== key) {
      this.sortChanged.emit({ key, direction: 'asc' });
      return;
    }
    this.sortChanged.emit(current.direction === 'asc' ? { key, direction: 'desc' } : null);
  }

  protected ariaSort(column: DataTableColumn<TRow>): 'ascending' | 'descending' | 'none' | null {
    if (!column.sortKey) return null;
    const current = this.sort();
    if (current?.key !== column.sortKey) return 'none';
    return current.direction === 'asc' ? 'ascending' : 'descending';
  }

  protected sortIcon(key: string): 'sort' | 'chevron-up' | 'chevron-down' {
    const current = this.sort();
    if (current?.key !== key) return 'sort';
    return current.direction === 'asc' ? 'chevron-up' : 'chevron-down';
  }

  protected isNumeric(column: DataTableColumn<TRow>): boolean {
    return column.numeric ?? column.kind === 'number';
  }

  protected text(column: DataTableColumn<TRow>, row: TRow): string {
    const value = column.value?.(row);
    // An em dash rather than an empty cell: a blank says nothing about whether the value is
    // missing or the column is broken.
    return value === null || value === undefined || value === '' ? '—' : `${value}`;
  }

  protected cellTemplate(key: string): TemplateRef<{ $implicit: unknown; index: number }> | null {
    return this.cellTemplates().find((cell) => cell.khCell() === key)?.template ?? null;
  }

  protected requestExport(): void {
    if (this.exportMode() === 'server') {
      this.exportRequested.emit();
      return;
    }
    this.downloadCsv();
  }

  /**
   * Writes the rows on screen to a CSV file.
   *
   * The visible columns only, and in the order they are shown: an export that silently included
   * the eleven columns somebody switched off is not the table they were looking at. Values are
   * quoted and internal quotes doubled — the whole of RFC 4180 that matters here — and a cell
   * beginning `=`, `+`, `-` or `@` is prefixed with a tab so a spreadsheet reads it as text
   * rather than as a formula.
   */
  private downloadCsv(): void {
    const columns = this.visibleColumns();
    const header = columns.map((column) => quoteCsv(column.label)).join(',');
    const body = this.rows().map((row) =>
      columns.map((column) => quoteCsv(this.text(column, row))).join(','),
    );

    // The byte-order mark is what makes Excel read the file as UTF-8 rather than as the machine's
    // code page — the difference between a rupee sign and mojibake in every finance export.
    const blob = new Blob(['﻿', [header, ...body].join('\r\n')], {
      type: 'text/csv;charset=utf-8',
    });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${this.label()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }
}

function quoteCsv(value: string): string {
  const guarded = /^[=+\-@]/.test(value) ? `\t${value}` : value;
  return `"${guarded.replace(/"/g, '""')}"`;
}
