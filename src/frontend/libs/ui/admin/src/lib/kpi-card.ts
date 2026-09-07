import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Skeleton } from '@klarahome/ui-primitives';

/**
 * One number on the dashboard, and what it means.
 *
 * Three states, and the third is the one usually skipped: loading, a value, and **no value the
 * server would give**. Several of this platform's list endpoints answer `PageInfo.total` as null
 * because counting on every page is too expensive (`docs/04-api-specification.md` §1.1), and a
 * card that rendered `0` for "we did not count" would be a dashboard that lies when it is busiest.
 * An unknown count renders an em dash and says so to a screen reader.
 *
 * The card is a link when it has a `path`, and a plain figure when it has not — a KPI that cannot
 * be drilled into is a number without a next step, but half of them genuinely have nowhere to go
 * until the screen behind them is built.
 */
@Component({
  selector: 'kh-kpi-card',
  imports: [NgTemplateOutlet, RouterLink, Skeleton],
  template: `
    @if (path(); as target) {
      <a [routerLink]="target" class="card">
        <ng-container *ngTemplateOutlet="body" />
      </a>
    } @else {
      <div class="card">
        <ng-container *ngTemplateOutlet="body" />
      </div>
    }

    <ng-template #body>
      <span class="label">{{ label() }}</span>
      @if (loading()) {
        <kh-skeleton width="3rem" height="1.875rem" />
      } @else if (value() === null || value() === undefined) {
        <span class="value muted" title="The server does not count this">—</span>
        <span class="kh-visually-hidden">Not counted</span>
      } @else {
        <span class="value">{{ value() }}</span>
      }
      @if (hint(); as text) {
        <span class="hint">{{ text }}</span>
      }
    </ng-template>
  `,
  styles: `
    :host {
      display: block;
    }

    .card {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      height: 100%;
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      color: inherit;
      text-decoration: none;
    }

    a.card:hover {
      border-color: var(--color-border-strong);
    }

    .label {
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .value {
      font-size: var(--text-3xl);
      font-weight: var(--weight-bold);
      font-variant-numeric: tabular-nums;
      line-height: var(--leading-tight);
    }

    .value.muted {
      color: var(--color-text-muted);
    }

    .hint {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KpiCard {
  readonly label = input.required<string>();
  /** The figure. `null` means the server did not count, which is not the same as zero. */
  readonly value = input<number | string | null | undefined>(null);
  readonly hint = input<string | null>(null);
  readonly loading = input(false);
  /** Where the figure is explained. Absent makes the card a plain tile rather than a link. */
  readonly path = input<string | null>(null);
}
