import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** One point on a chart: its category, its magnitude, and how the caller writes the magnitude. */
export interface ChartPoint {
  /** The category or the period — what goes on the horizontal axis. */
  readonly label: string;
  /** The magnitude. Charts here start at zero, so a negative value is not drawable. */
  readonly value: number;
  /**
   * The value as the caller formats it — `₹1,24,500`, `8.3%`, `412`.
   *
   * Formatting is the caller's because only the report definition knows a column's kind, and a
   * chart that guessed from the runtime type would render a rupee amount and a count identically.
   */
  readonly display?: string;
}

/** How a series is drawn. */
export type ChartKind = 'bar' | 'line';

interface Bar {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
  readonly point: ChartPoint;
}

interface Tick {
  readonly y: number;
  readonly label: string;
}

interface AxisLabel {
  readonly x: number;
  readonly text: string;
}

const WIDTH = 640;
const HEIGHT = 240;
const PAD_LEFT = 56;
const PAD_RIGHT = 12;
const PAD_TOP = 12;
const PAD_BOTTOM = 32;
const MAX_AXIS_LABELS = 12;

/**
 * One series, drawn.
 *
 * **Placeholder-styled, deliberately** — colour, type and motion are Step 30's, and this draws in
 * the neutral tokens like everything else built before it. What is decided here is *form*, which
 * is not a matter of taste:
 *
 *  - **Bars for categories, a line for a period.** A report grouped by seller or by SKU is a
 *    comparison of separate things and reads as bars; one grouped by day is a shape over time and
 *    reads as a line. The report's own `groupBy` is what picks between them, so the choice is made
 *    from the data rather than from a preference.
 *  - **The value axis starts at zero, always.** A truncated axis makes a 3% movement look like a
 *    collapse, and the figures on these screens are the ones people make decisions with.
 *  - **The chart is not the data.** Every chart carries the same numbers as a table beside it —
 *    visually hidden when the caller already renders one — because a canvas of rectangles is
 *    unreadable to a screen reader and unusable to anybody who needs the exact figure. The SVG is
 *    `aria-hidden` for the same reason: it would otherwise be read out as a list of coordinates.
 *
 * Axis labels thin themselves rather than overlapping: thirty days of data draws thirty bars and
 * twelve dates, which is legible, where thirty overlapping dates are not.
 */
@Component({
  selector: 'kh-report-chart',
  template: `
    <figure>
      @if (caption(); as text) {
        <figcaption>{{ text }}</figcaption>
      }

      @if (points().length === 0) {
        <p class="empty">{{ emptyMessage() }}</p>
      } @else {
        <svg
          [attr.viewBox]="viewBox"
          role="img"
          aria-hidden="true"
          focusable="false"
          preserveAspectRatio="xMidYMid meet"
        >
          @for (tick of ticks(); track tick.y) {
            <line class="grid" [attr.x1]="padLeft" [attr.x2]="right" [attr.y1]="tick.y" [attr.y2]="tick.y" />
            <text class="tick" [attr.x]="padLeft - 8" [attr.y]="tick.y + 4" text-anchor="end">
              {{ tick.label }}
            </text>
          }

          @if (kind() === 'bar') {
            @for (bar of bars(); track bar.point.label) {
              <rect
                class="bar"
                [attr.x]="bar.x"
                [attr.y]="bar.y"
                [attr.width]="bar.width"
                [attr.height]="bar.height"
                rx="1"
              />
            }
          } @else {
            <polyline class="line" [attr.points]="linePoints()" />
            @for (bar of bars(); track bar.point.label) {
              <circle class="dot" [attr.cx]="bar.x + bar.width / 2" [attr.cy]="bar.y" r="2.5" />
            }
          }

          <line
            class="axis"
            [attr.x1]="padLeft"
            [attr.x2]="right"
            [attr.y1]="baseline"
            [attr.y2]="baseline"
          />

          @for (label of axisLabels(); track label.x) {
            <text class="tick" [attr.x]="label.x" [attr.y]="baseline + 18" text-anchor="middle">
              {{ label.text }}
            </text>
          }
        </svg>
      }

      <table [class.kh-visually-hidden]="!showTable()">
        <caption>
          {{
            label()
          }}
        </caption>
        <thead>
          <tr>
            <th scope="col">{{ categoryLabel() }}</th>
            <th scope="col">{{ valueLabel() }}</th>
          </tr>
        </thead>
        <tbody>
          @for (point of points(); track point.label) {
            <tr>
              <th scope="row">{{ point.label }}</th>
              <td>{{ point.display ?? point.value }}</td>
            </tr>
          }
        </tbody>
      </table>
    </figure>
  `,
  styles: `
    :host {
      display: block;
    }

    figure {
      margin: 0;
    }

    figcaption {
      margin-block-end: var(--space-2);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    svg {
      display: block;
      inline-size: 100%;
      block-size: auto;
    }

    .grid {
      stroke: var(--color-border);
      stroke-width: 1;
    }

    .axis {
      stroke: var(--color-border-strong);
      stroke-width: 1;
    }

    .tick {
      fill: var(--color-text-muted);
      font-family: var(--font-sans);
      font-size: 11px;
    }

    .bar {
      fill: var(--color-primary);
    }

    .line {
      fill: none;
      stroke: var(--color-primary);
      stroke-width: 2;
      stroke-linejoin: round;
      stroke-linecap: round;
    }

    .dot {
      fill: var(--color-primary);
    }

    .empty {
      margin: 0;
      padding: var(--space-6) 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
      text-align: center;
    }

    table {
      inline-size: 100%;
      margin-block-start: var(--space-4);
      border-collapse: collapse;
      font-size: var(--text-sm);
    }

    caption {
      margin-block-end: var(--space-2);
      color: var(--color-text-muted);
      font-size: var(--text-xs);
      text-align: start;
    }

    th,
    td {
      padding: var(--space-1) var(--space-2);
      border-block-end: 1px solid var(--color-border);
      text-align: start;
    }

    td {
      font-variant-numeric: tabular-nums;
      text-align: end;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportChart {
  readonly points = input<readonly ChartPoint[]>([]);
  readonly kind = input<ChartKind>('bar');
  /** The accessible name of the series. Read by the table's caption. */
  readonly label = input.required<string>();
  readonly caption = input<string | null>(null);
  readonly categoryLabel = input('Category');
  readonly valueLabel = input('Value');
  /** Shows the data table rather than hiding it. False where the caller renders its own. */
  readonly showTable = input(false);
  readonly emptyMessage = input('Nothing to chart for this period.');

  protected readonly viewBox = `0 0 ${WIDTH} ${HEIGHT}`;
  protected readonly padLeft = PAD_LEFT;
  protected readonly right = WIDTH - PAD_RIGHT;
  protected readonly baseline = HEIGHT - PAD_BOTTOM;

  /** The top of the value axis: the largest value, rounded up to something a person would say. */
  private readonly ceiling = computed(() => {
    const largest = this.points().reduce((max, point) => Math.max(max, point.value), 0);
    return largest <= 0 ? 1 : niceCeiling(largest);
  });

  protected readonly ticks = computed<readonly Tick[]>(() => {
    const top = this.ceiling();
    const plotHeight = this.baseline - PAD_TOP;
    return [0, 0.25, 0.5, 0.75, 1].map((fraction) => ({
      y: this.baseline - fraction * plotHeight,
      label: shortNumber(top * fraction),
    }));
  });

  protected readonly bars = computed<readonly Bar[]>(() => {
    const points = this.points();
    if (points.length === 0) return [];

    const plotWidth = this.right - PAD_LEFT;
    const plotHeight = this.baseline - PAD_TOP;
    const band = plotWidth / points.length;
    // A line's markers sit on the band's centre, so its "bar" is the whole band and its y is the
    // point rather than the top of a rectangle. One geometry serves both, which keeps the axis
    // labels — computed from the same bands — aligned in either mode.
    const barWidth = this.kind() === 'bar' ? Math.max(band * 0.62, 1) : band;
    const top = this.ceiling();

    return points.map((point, index) => {
      const height = Math.max((Math.max(point.value, 0) / top) * plotHeight, 0);
      const x = PAD_LEFT + index * band + (band - barWidth) / 2;
      return { x, y: this.baseline - height, width: barWidth, height, point };
    });
  });

  protected readonly linePoints = computed(() =>
    this.bars()
      .map((bar) => `${(bar.x + bar.width / 2).toFixed(1)},${bar.y.toFixed(1)}`)
      .join(' '),
  );

  protected readonly axisLabels = computed<readonly AxisLabel[]>(() => {
    const bars = this.bars();
    const step = Math.max(1, Math.ceil(bars.length / MAX_AXIS_LABELS));
    return bars
      .filter((_bar, index) => index % step === 0)
      .map((bar) => ({ x: bar.x + bar.width / 2, text: bar.point.label }));
  });
}

/** Rounds a maximum up to 1, 2 or 5 times a power of ten — the numbers axes are labelled with. */
function niceCeiling(value: number): number {
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const normalised = value / magnitude;
  const step = normalised <= 1 ? 1 : normalised <= 2 ? 2 : normalised <= 5 ? 5 : 10;
  return step * magnitude;
}

/** An axis label: `1.2k`, `3.4M`. The exact figure is in the table, which is the point of it. */
function shortNumber(value: number): string {
  const absolute = Math.abs(value);
  if (absolute >= 1_000_000) return `${trim(value / 1_000_000)}M`;
  if (absolute >= 1_000) return `${trim(value / 1_000)}k`;
  return trim(value);
}

function trim(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(1);
}
