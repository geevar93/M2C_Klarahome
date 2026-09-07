import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import {
  ReportColumn,
  ReportDefinition,
  ReportRow,
  ReportTable,
  ReportingAdminService,
} from '@klarahome/data-access-admin';
import { ChartPoint, DataTable, DataTableColumn, PageHeader, ReportChart } from '@klarahome/ui-admin';
import { Alert, Button, Control, Field, Skeleton } from '@klarahome/ui-primitives';

import { describeError } from '../../core/describe-error';
import { tableDate, tableMoney } from '../../core/format';

/** How many rows a chart is worth drawing for. Beyond this the table is the honest rendering. */
const CHARTABLE_ROWS = 60;

/**
 * Runs one report and renders it.
 *
 * A component rather than a page, because two screens need it: the report screen proper, and a
 * seller's performance page, which is the same runner over the reports a seller may see.
 *
 * **Every column is rendered by the kind the definition declares.** `Money`, `Count`, `Percent`,
 * `Date` and `Text` are formatted here from `ReportColumn.kind` rather than guessed from the
 * runtime type — a rupee amount and a count are both numbers, and a rate that arrived as `0.0834`
 * has to be shown as `8.34%` by somebody. That somebody is this component, once, for all thirteen
 * reports.
 *
 * **The chart is drawn only when there is something a chart says better than a table.** A report
 * grouped by day gets a line, one grouped by anything else gets bars, one with a single measure
 * and sixty-plus rows gets neither — a chart of two hundred bars is decoration. The chart's series
 * is the first non-text measure, and the table beside it carries every column, so nothing is only
 * available as a picture.
 *
 * **`truncated` is surfaced.** A report that hit its row ceiling is a partial answer, and reading
 * a partial table as a complete one is how somebody concludes that sales stopped on the 14th.
 */
@Component({
  selector: 'kh-report-runner',
  imports: [Alert, Button, Control, DataTable, Field, PageHeader, ReportChart, Skeleton],
  template: `
    @if (definition(); as report) {
      <kh-page-header [heading]="report.name" [description]="report.description" [crumbs]="crumbs()" />
    }

    <section class="controls">
      <div class="row">
        <kh-field label="From" for="report-from" [optional]="true">
          <input
            khControl
            id="report-from"
            type="date"
            [value]="from()"
            (input)="from.set($any($event.target).value)"
          />
        </kh-field>
        <kh-field label="To" for="report-to" [optional]="true">
          <input
            khControl
            id="report-to"
            type="date"
            [value]="to()"
            (input)="to.set($any($event.target).value)"
          />
        </kh-field>

        @if (groupings().length > 1) {
          <kh-field label="Grouped by" for="report-group">
            <select
              khControl
              id="report-group"
              [value]="groupBy()"
              (change)="groupBy.set($any($event.target).value)"
            >
              @for (grouping of groupings(); track grouping) {
                <option [value]="grouping">{{ grouping }}</option>
              }
            </select>
          </kh-field>
        }

        @if (showVendorFilter()) {
          <kh-field label="Seller id" for="report-vendor" [optional]="true" hint="Blank means every seller.">
            <input
              khControl
              id="report-vendor"
              type="text"
              [value]="vendorId()"
              (input)="vendorId.set($any($event.target).value)"
            />
          </kh-field>
        }

        <button khButton type="button" variant="primary" [disabled]="running()" (click)="run()">
          {{ running() ? 'Running…' : 'Run it' }}
        </button>
        <button khButton type="button" variant="tertiary" [disabled]="exporting()" (click)="exportCsv()">
          {{ exporting() ? 'Producing…' : 'Produce a CSV' }}
        </button>
      </div>
    </section>

    @if (error(); as message) {
      <kh-alert tone="danger" heading="The report could not be run">{{ message }}</kh-alert>
    }

    @if (exportMessage(); as message) {
      <kh-alert tone="success" heading="It is being produced">{{ message }}</kh-alert>
    }

    @if (running()) {
      <kh-skeleton height="18rem" />
    } @else if (table(); as result) {
      @if (result.truncated) {
        <kh-alert tone="warning" heading="This is not the whole answer">
          The report hit its row ceiling, so what is below is a partial table. Narrow the period, or produce a
          CSV, which is not capped the same way.
        </kh-alert>
      }

      @if (chartPoints().length > 0) {
        <section class="chart">
          <kh-report-chart
            [points]="chartPoints()"
            [kind]="chartKind()"
            [label]="result.name"
            [caption]="chartCaption()"
            [categoryLabel]="categoryColumn()?.label ?? 'Category'"
            [valueLabel]="measureColumn()?.label ?? 'Value'"
          />
        </section>
      }

      <kh-data-table
        [label]="result.name"
        [columns]="tableColumns()"
        [rows]="rows()"
        [rowKey]="rowKey"
        [configurable]="true"
        [storageKey]="'report-' + result.key"
        exportMode="page"
        emptyMessage="Nothing was found for this period."
      />

      @if (result.totals; as totals) {
        <section class="totals">
          <h2>Totals</h2>
          <dl>
            @for (column of measureColumns(); track column.key) {
              <dt>{{ column.label }}</dt>
              <dd>{{ format(totals[column.key], column, result.currencyCode) }}</dd>
            }
          </dl>
        </section>
      }
    }
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .controls {
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

    .chart {
      margin-block-end: var(--space-4);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .totals {
      margin-block-start: var(--space-4);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-base);
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
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportRunner {
  private readonly reporting = inject(ReportingAdminService);

  /** Which report. Required; the runner does not choose one. */
  readonly reportKey = input.required<string>();
  /** Its declaration, when the caller already has it. Absent, the runner fetches the catalogue. */
  readonly definition = input<ReportDefinition | null>(null);
  /** Whether the "one seller" filter is offered. False on a seller's own screens. */
  readonly showVendorFilter = input(true);
  readonly crumbs = input<readonly { label: string; path: string }[]>([]);
  /** Runs as soon as the component appears, rather than waiting for the button. */
  readonly runOnInit = input(true);

  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly groupBy = signal('');
  protected readonly vendorId = signal('');

  protected readonly table = signal<ReportTable | null>(null);
  protected readonly running = signal(false);
  protected readonly exporting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly exportMessage = signal<string | null>(null);

  protected readonly groupings = computed(() => this.definition()?.groupBy ?? []);
  protected readonly rows = computed(() => this.table()?.rows ?? []);

  /**
   * The column a chart's categories come from.
   *
   * The first `Date` or `Text` column, which is what every report in the catalogue puts first —
   * the day, the SKU, the seller. A report with no such column is one this component does not
   * chart, which is the right answer for a single-row summary.
   */
  protected readonly categoryColumn = computed<ReportColumn | null>(() => {
    const columns = this.table()?.columns ?? [];
    return columns.find((column) => column.kind === 'Date' || column.kind === 'Text') ?? null;
  });

  protected readonly measureColumns = computed<readonly ReportColumn[]>(() =>
    (this.table()?.columns ?? []).filter(
      (column) => column.kind === 'Money' || column.kind === 'Count' || column.kind === 'Percent',
    ),
  );

  protected readonly measureColumn = computed<ReportColumn | null>(() => this.measureColumns()[0] ?? null);

  /** A line for a period, bars for anything else. Decided from the data, not from a preference. */
  protected readonly chartKind = computed<'bar' | 'line'>(() =>
    this.categoryColumn()?.kind === 'Date' ? 'line' : 'bar',
  );

  protected readonly chartPoints = computed<readonly ChartPoint[]>(() => {
    const result = this.table();
    const category = this.categoryColumn();
    const measure = this.measureColumn();
    if (!result || !category || !measure) return [];
    if (result.rows.length === 0 || result.rows.length > CHARTABLE_ROWS) return [];

    return result.rows.map((row) => ({
      label: this.format(row[category.key], category, result.currencyCode),
      value: numeric(row[measure.key]),
      display: this.format(row[measure.key], measure, result.currencyCode),
    }));
  });

  protected readonly chartCaption = computed(() => {
    const result = this.table();
    const measure = this.measureColumn();
    if (!result || !measure) return null;
    const grouping = result.groupBy ? ` by ${result.groupBy}` : '';
    return `${measure.label}${grouping}, ${tableDate(result.from)} → ${tableDate(result.to)}`;
  });

  protected readonly tableColumns = computed<readonly DataTableColumn<ReportRow>[]>(() => {
    const result = this.table();
    if (!result) return [];

    return result.columns.map((column) => ({
      key: column.key,
      label: column.label,
      kind:
        column.kind === 'Money' || column.kind === 'Count' || column.kind === 'Percent' ? 'number' : 'text',
      value: (row: ReportRow) => this.format(row[column.key], column, result.currencyCode),
    }));
  });

  /** Rows have no identifier of their own; a report is an aggregation, so position is the key. */
  protected readonly rowKey = (row: ReportRow) => JSON.stringify(row);

  constructor() {
    if (this.runOnInit()) queueMicrotask(() => this.run());
  }

  /** One value, rendered as the column's declared kind says it should be. */
  protected format(value: unknown, column: ReportColumn, currency: string): string {
    if (value === null || value === undefined) return '—';

    switch (column.kind) {
      case 'Money':
        return tableMoney(numeric(value), currency);
      case 'Count':
        return String(Math.round(numeric(value)));
      case 'Percent':
        // A ratio between nought and one on the wire, a percentage on the screen.
        return `${(numeric(value) * 100).toFixed(2)}%`;
      case 'Date':
        return tableDate(String(value));
      default:
        return String(value);
    }
  }

  protected run(): void {
    if (this.running()) return;

    this.running.set(true);
    this.error.set(null);
    this.exportMessage.set(null);

    this.reporting.table(this.reportKey(), this.request()).subscribe({
      next: (table) => {
        this.running.set(false);
        this.table.set(table);
        // Adopted from the answer, so the control agrees with what was actually run.
        if (table.groupBy) this.groupBy.set(table.groupBy);
      },
      error: (error: unknown) => {
        this.running.set(false);
        this.table.set(null);
        this.error.set(describeError(error, 'It could not be run.'));
      },
    });
  }

  /**
   * Produces the same report as a file.
   *
   * The run is recorded and the file lands in the private bucket; the link is fetched from the
   * runs list rather than here, because it is short-lived and a link held on this screen would
   * expire while somebody read the table.
   */
  protected exportCsv(): void {
    if (this.exporting()) return;

    this.exporting.set(true);
    this.error.set(null);

    this.reporting.export(this.reportKey(), this.request()).subscribe({
      next: () => {
        this.exporting.set(false);
        this.exportMessage.set(
          'The file is being produced. It appears under "What has been produced" on the Reports screen, with a link.',
        );
      },
      error: (error: unknown) => {
        this.exporting.set(false);
        this.error.set(describeError(error, 'The file could not be produced.'));
      },
    });
  }

  private request() {
    return {
      from: this.from() ? new Date(this.from()).toISOString() : undefined,
      to: this.to() ? new Date(this.to()).toISOString() : undefined,
      groupBy: this.groupBy() || undefined,
      vendorId: this.vendorId().trim() || undefined,
    };
  }
}

function numeric(value: unknown): number {
  const parsed = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}
