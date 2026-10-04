import {
  ChangeDetectionStrategy,
  Component,
  Directive,
  ElementRef,
  TemplateRef,
  computed,
  contentChild,
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

import { FilterBar } from './filter-bar';
import { humanise } from './status-badge';
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
 *  - **Below 768px the rows are cards**, drawn from the same cells (see the styles). The first
 *    column is the card's heading and the rest are captioned by their column label, so every
 *    list in the back office reads on a phone without a card template per screen.
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
        <ng-content select="[slot=actions]" />

        @if (exportMode() !== 'none' || configurable()) {
          <!-- Export and the column chooser used to be two worded buttons beside the filters,
               which on a phone was a third row of chrome above the first row of data. They are
               one overflow menu now: tools, not tasks. A popover rather than a dialog, because it
               changes what is on screen behind it, and trapping focus away from the table while
               choosing that table's columns is the wrong model. Escape closes it. -->
          <div class="tools">
            <button
              khButton
              type="button"
              size="sm"
              [iconOnly]="true"
              aria-label="Table tools"
              [attr.aria-expanded]="toolsOpen()"
              aria-haspopup="true"
              (click)="toolsOpen.set(!toolsOpen())"
            >
              <kh-icon name="more" size="sm" />
            </button>

            @if (toolsOpen()) {
              <div
                class="tools-panel"
                role="group"
                aria-label="Table tools"
                (keydown)="onToolsKeydown($event)"
              >
                @if (exportMode() !== 'none') {
                  <button
                    khButton
                    type="button"
                    size="sm"
                    variant="tertiary"
                    class="tool"
                    (click)="requestExport()"
                  >
                    <kh-icon name="download" size="sm" />
                    Export CSV
                  </button>
                }

                @if (configurable()) {
                  <p class="tools-heading">Columns</p>
                  @for (column of columns(); track column.key) {
                    <kh-checkbox
                      [label]="column.label"
                      [checked]="isVisible(column.key)"
                      [inputId]="'col-' + instanceId + '-' + column.key"
                      (checkedChange)="toggleColumn(column.key, $event)"
                    />
                  }
                }
              </div>
            }
          </div>
        }
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
                [class.sticky-end]="isSticky(column)"
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

                @for (column of visibleColumns(); track column.key; let first = $first) {
                  <td
                    [class.numeric]="isNumeric(column)"
                    [class.sticky-end]="isSticky(column)"
                    [class.title]="first"
                    [class.card-hidden]="!first && hasCardMap() && !column.card"
                    [attr.data-label]="first ? null : column.label"
                  >
                    @if (cellTemplate(column.key); as template) {
                      <ng-container
                        [ngTemplateOutlet]="template"
                        [ngTemplateOutletContext]="{ $implicit: row, index: index }"
                      />
                    } @else if (column.kind === 'badge') {
                      <kh-badge [tone]="column.tone ? column.tone(row) : 'neutral'"
                        ><span class="dot" aria-hidden="true"></span>{{ badgeText(column, row) }}</kh-badge
                      >
                    } @else {
                      {{ text(column, row) }}
                    }
                  </td>
                }
              </tr>
            } @empty {
              <tr>
                <td class="empty" [attr.colspan]="columnCount()">
                  <span class="empty-glyph" aria-hidden="true"><kh-icon name="search" /></span>
                  <span class="empty-message">{{ emptyText() }}</span>
                  @if (canClear()) {
                    <button khButton type="button" size="sm" variant="secondary" (click)="clearFilters()">
                      {{ clearLabel() }}
                    </button>
                  }
                </td>
              </tr>
            }
          }
        </tbody>
      </table>
    </div>

    @if (rows().length > 0 || hasPrevious() || hasNext()) {
      <div class="pager">
        <p class="count">
          {{ rows().length }} shown
          @if (total(); as rowCount) {
            <span class="muted">· about {{ formatCount(rowCount) }} in total</span>
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
    }
  `,
  styles: `
    :host {
      display: block;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-sm);
    }

    /* Bottom-aligned, like the filter bar inside it: its fields carry a label above the control,
       so centring would float Export and Columns half a label higher than the inputs beside them. */
    .toolbar {
      display: flex;
      gap: var(--space-2);
      /* Top-aligned on a phone so the tools button shares the search box's line rather than
         dropping under the chips; bottom-aligned from \`md\`, where the fields beside it carry a
         label above the control and centring would float the button half a label too high. */
      align-items: flex-start;
      justify-content: space-between;
      padding: var(--space-3);
      border-block-end: 1px solid var(--color-border);
    }

    .toolbar-lead {
      flex: 1 1 auto;
      min-width: 0;
    }

    .toolbar-actions {
      display: flex;
      flex: none;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: center;
      justify-content: flex-end;
    }

    @media (min-width: 768px) {
      .toolbar {
        flex-wrap: wrap;
        gap: var(--space-3);
        align-items: flex-end;
      }

      .toolbar-lead {
        flex: 1 1 18rem;
      }
    }

    .tools {
      position: relative;
    }

    .tools-panel {
      position: absolute;
      inset-inline-end: 0;
      inset-block-start: calc(100% + var(--space-1));
      z-index: var(--z-header);
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      min-width: 14rem;
      max-height: 20rem;
      overflow-y: auto;
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-md);
    }

    .tool {
      justify-content: flex-start;
    }

    .tools-heading {
      margin: var(--space-2) 0 var(--space-1);
      font-size: var(--text-xs);
      font-weight: var(--weight-medium);
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--color-text-muted);
    }

    .bulk-bar {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: center;
      padding: var(--space-2) var(--space-3);
      background: var(--color-primary-subtle);
      border-block-end: 1px solid var(--color-primary);
      color: var(--color-text);
    }

    .bulk-count {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    /* ---- Rows as cards (the base state), a table from 768px (admin UX phase 4) ----------------
       A row of nine columns does not fold onto a phone; it scrolled sideways, and a swipe that
       meant "next row" caught the table instead. Below \`md\` each row is a card: the first
       column is its heading, and every other cell is a caption/value pair, the caption read from
       the column's label. Nothing about the rows changes — same cells, same templates — only how
       they are laid out, so a page needs no card template of its own. Sorting lives in the header
       and the header is hidden here; a phone user sorts on the desktop, or filters. */
    .scroll {
      display: block;
      padding: var(--space-3);
    }

    table {
      display: block;
      width: 100%;
      border-collapse: collapse;
      font-size: var(--text-sm);
    }

    thead {
      display: none;
    }

    tbody,
    tr {
      display: block;
    }

    tbody tr {
      padding: var(--space-2) var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
    }

    tbody tr + tr {
      margin-block-start: var(--space-2);
    }

    tbody tr.selected {
      background: var(--color-primary-subtle);
    }

    td {
      display: flex;
      gap: var(--space-3);
      align-items: baseline;
      justify-content: space-between;
      padding: var(--space-1) 0;
      text-align: end;
      white-space: normal;
    }

    td[data-label]::before {
      content: attr(data-label);
      flex: none;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    /* Left off the phone card (\`DataTableColumn.card\`); a cell again from \`md\`, below. */
    td.card-hidden {
      display: none;
    }

    td.title {
      display: block;
      padding-block-start: var(--space-2);
      font-weight: var(--weight-medium);
      text-align: start;
    }

    .numeric {
      font-variant-numeric: tabular-nums;
    }

    .select-cell {
      justify-content: flex-start;
    }

    .select-cell input {
      width: 1.125rem;
      height: 1.125rem;
      accent-color: var(--color-primary);
      cursor: pointer;
    }

    .select-cell::after {
      content: 'Select';
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    /* The table is the one element in the admin allowed to scroll sideways: forty columns of
       stock do not fold onto a tablet, and a squeezed table is unreadable long before it is
       unusable. Sideways only. A table capped at a viewport height scrolls inside a page that
       also scrolls, and two scrollbars stacked on each other is the most common "the page feels
       broken" report there is. The page is already paged, so letting the rows run the page's own
       length costs nothing. */
    @media (min-width: 768px) {
      .scroll {
        padding: 0;
        overflow-x: auto;
      }

      table {
        display: table;
      }

      thead {
        display: table-header-group;
      }

      tbody {
        display: table-row-group;
      }

      tr,
      tbody tr {
        display: table-row;
        padding: 0;
        border: 0;
        border-radius: 0;
        background: none;
      }

      tbody tr + tr {
        margin-block-start: 0;
      }

      tbody tr.selected {
        background: var(--color-primary-subtle);
      }

      th,
      td,
      td.title,
      td.card-hidden {
        display: table-cell;
        padding: var(--space-2) var(--space-3);
        text-align: start;
        white-space: nowrap;
        font-weight: inherit;
        border-block-end: 1px solid var(--color-border);
      }

      td[data-label]::before,
      .select-cell::after {
        content: none;
      }

      thead th {
        position: sticky;
        inset-block-start: 0;
        z-index: 1;
        background: var(--color-surface);
        font-size: var(--text-xs);
        font-weight: var(--weight-semibold);
        letter-spacing: 0.04em;
        text-transform: uppercase;
        color: var(--color-text-muted);
      }

      tbody tr:last-child td {
        border-block-end: 0;
      }

      tbody tr:hover:not(.selected) {
        background: var(--color-surface);
      }

      /* The pinned end column (row actions). Opaque so the cells scrolling under it do not show
         through, and it follows the row's own hover and selected tints. */
      .sticky-end {
        position: sticky;
        inset-inline-end: 0;
        background: var(--color-surface-raised);
        box-shadow: inset 1px 0 0 var(--color-border);
      }

      thead th.sticky-end {
        z-index: 2;
        background: var(--color-surface);
      }

      tbody tr:hover:not(.selected) .sticky-end {
        background: var(--color-surface);
      }

      tbody tr.selected .sticky-end {
        background: var(--color-primary-subtle);
      }

      .numeric {
        text-align: end;
      }

      .select-cell {
        width: var(--touch-target-min);
      }
    }

    .dot {
      flex: none;
      width: 0.375rem;
      height: 0.375rem;
      border-radius: var(--radius-full);
      background: currentColor;
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

    td.empty {
      display: block;
      padding: var(--space-10) var(--space-3);
      text-align: center;
      color: var(--color-text-muted);
      white-space: normal;
    }

    /* A designed empty state, not a sentence: a glyph, the message, and the way out. */
    .empty-glyph {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 3rem;
      height: 3rem;
      margin-block-end: var(--space-3);
      border-radius: var(--radius-full);
      background: var(--color-surface);
      color: var(--color-text-muted);
    }

    .empty-message {
      display: block;
      margin-block-end: var(--space-2);
      color: var(--color-text);
      font-weight: var(--weight-medium);
    }

    /* The empty row is a card with no border: a bordered box saying "nothing" is a box. */
    tbody tr:has(> td.empty) {
      border: 0;
      background: none;
    }

    @media (min-width: 768px) {
      td.empty {
        display: table-cell;
      }
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
  host: {
    '(document:click)': 'onDocumentClick($event)',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DataTable<TRow> {
  protected formatCount(count: number): string {
    return count.toLocaleString('en-IN');
  }

  private readonly storage = inject(BrowserStorage);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** A click anywhere outside the tools menu closes it — a popover that only Escape closes is a trap for a mouse. */
  protected onDocumentClick(event: Event): void {
    if (!this.toolsOpen()) return;
    const tools = this.host.nativeElement.querySelector('.tools');
    if (tools && !tools.contains(event.target as Node)) this.toolsOpen.set(false);
  }

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
  /** What an empty table says when nothing is filtering it; derived from `emptyMessage` when unset. */
  /** The label of the reset offered by an empty, filtered table — "Show all" where a default filter is on. */
  readonly clearLabel = input('Clear filters');
  readonly emptyUnfilteredMessage = input<string | null>(null);
  /**
   * Whether a filter is narrowing the list. With it, an empty table offers to clear the filters,
   * because "nothing matches" and "there is nothing" call for different next moves and the table
   * cannot tell them apart on its own.
   */
  readonly filtered = input(false);
  readonly filtersCleared = output<void>();

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
  /** The filter bar projected into the `filters` slot, if any: it knows whether the list is narrowed, and how to widen it. */
  private readonly filterBar = contentChild(FilterBar);
  /**
   * Whether an empty table can offer a way out: a page said so with `filtered`, or the projected
   * filter bar has a search or filter on. Every list gets the reset without wiring it.
   */
  protected readonly canClear = computed(() => this.filtered() || this.filterBar()?.isFiltered() === true);
  private readonly selection = signal<ReadonlySet<string>>(new Set());
  private readonly hidden = signal<ReadonlySet<string>>(new Set());
  protected readonly toolsOpen = signal(false);

  protected isSticky(column: DataTableColumn<TRow>): boolean {
    return column.sticky === 'end' || column.key === 'actions';
  }

  /** Resets the projected filter bar (which tells the page), else asks the page to clear. */
  protected clearFilters(): void {
    const bar = this.filterBar();
    if (bar) bar.clearAll();
    else this.filtersCleared.emit();
  }

  protected readonly visibleColumns = computed(() =>
    this.columns().filter((column) => !this.hidden().has(column.key)),
  );
  /** Whether this table chose which columns its phone cards carry — see `DataTableColumn.card`. */
  protected readonly hasCardMap = computed(() => this.columns().some((column) => column.card));
  /**
   * The empty-state sentence. A page words it for the filtered case ("No banner matches these
   * filters."), which is a lie about a list nobody has filtered: with nothing narrowing it, the
   * honest message is that there are none yet. A page can say it itself with
   * `emptyUnfilteredMessage`; otherwise the "No X matches…" wording is turned into "No Xs yet.".
   */
  protected readonly emptyText = computed(() => {
    const message = this.emptyMessage();
    if (this.canClear()) return message;
    const explicit = this.emptyUnfilteredMessage();
    if (explicit) return explicit;
    const match = /^No (.+?) matche?s? (?:these filters|this search)\.$/.exec(message);
    return match ? `No ${pluralise(match[1])} yet.` : message;
  });
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
    // The columns that start hidden: remembered per storage key where the user has chosen, else the
    // `hiddenByDefault` ones. Read in an effect rather than in the constructor, which is what lets
    // the key be an input at all. (The defaults used to apply only to a table with no storage key,
    // so every persisted list showed the columns that were meant to start off.)
    effect(() => {
      const defaults = this.columns()
        .filter((column) => column.hiddenByDefault)
        .map((column) => column.key);
      const key = this.storageKey();
      const remembered = key ? this.storage.getJson<string[] | null>(`kh.columns.${key}`, null) : null;
      this.hidden.set(new Set(remembered ?? defaults));
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

  protected onToolsKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.stopPropagation();
      this.toolsOpen.set(false);
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

  /** A raw enum name (`UnderReview`) reads as words; text a page already worded is left alone. */
  protected badgeText(column: DataTableColumn<TRow>, row: TRow): string {
    const value = this.text(column, row);
    return /^[A-Z][a-z]+(?:[A-Z][a-z]+)+$/.test(value) ? humanise(value) : value;
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
    this.toolsOpen.set(false);
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

/** Good enough for the nouns the back office lists; "stock" and "messages" do not take an s. */
function pluralise(noun: string): string {
  if (/^stock$/i.test(noun)) return noun;
  if (/[^aeiou]y$/i.test(noun)) return `${noun.slice(0, -1)}ies`;
  if (/(s|x|ch|sh)$/i.test(noun)) return `${noun}es`;
  return `${noun}s`;
}

function quoteCsv(value: string): string {
  const guarded = /^[=+\-@]/.test(value) ? `\t${value}` : value;
  return `"${guarded.replace(/"/g, '""')}"`;
}
