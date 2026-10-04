import { ChangeDetectionStrategy, Component, input, computed } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ICON_NAMES, Icon, IconName, Skeleton } from '@klarahome/ui-primitives';

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
  imports: [Icon, NgTemplateOutlet, RouterLink, Skeleton],
  template: `
    @if (path(); as target) {
      <a [routerLink]="target" class="card" [attr.data-tone]="activeTone()" [class.zero]="isZero()">
        <ng-container *ngTemplateOutlet="body" />
      </a>
    } @else {
      <div class="card" [attr.data-tone]="activeTone()" [class.zero]="isZero()">
        <ng-container *ngTemplateOutlet="body" />
      </div>
    }

    <ng-template #body>
      <span class="top">
        <span class="label">{{ label() }}</span>
        @if (iconName(); as glyph) {
          <span class="glyph" aria-hidden="true"><kh-icon [name]="glyph" size="sm" /></span>
        }
      </span>
      @if (loading()) {
        <kh-skeleton width="3rem" height="1.875rem" />
      } @else if (value() === null || value() === undefined) {
        <span class="value muted" title="The server does not count this">—</span>
        <span class="kh-visually-hidden">Not counted</span>
      } @else {
        <span class="value">{{ shown() }}</span>
      }
      @if (hint(); as text) {
        <span class="hint">{{ text }}</span>
      }
      @if (path() && !loading()) {
        <span class="go" aria-hidden="true">Open <kh-icon name="chevron-right" size="sm" /></span>
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
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-sm);
      color: inherit;
      text-decoration: none;
    }

    a.card {
      transition:
        border-color var(--duration-fast) var(--ease-standard),
        box-shadow var(--duration-fast) var(--ease-standard);
    }

    a.card:hover {
      border-color: var(--color-primary);
      box-shadow: var(--shadow-md);
    }

    a.card:focus-visible {
      outline: 2px solid var(--color-focus-ring);
      outline-offset: 2px;
    }

    .top {
      display: flex;
      gap: var(--space-2);
      align-items: flex-start;
      justify-content: space-between;
    }

    .glyph {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      flex: none;
      width: 2rem;
      height: 2rem;
      border-radius: var(--radius-md);
      background: var(--color-primary-subtle);
      color: var(--color-primary);
    }

    /* A queue with something in it takes its colour from how urgent it is: orange for work waiting,
       red for something that has gone wrong. A queue at zero stays quiet. */
    [data-tone='warning'] .glyph {
      background: var(--color-warning-subtle);
      color: var(--color-warning-text);
    }

    [data-tone='danger'] .glyph {
      background: var(--color-danger-subtle);
      color: var(--color-danger-text);
    }

    [data-tone='warning'] .value {
      color: var(--color-warning-text);
    }

    [data-tone='danger'] .value {
      color: var(--color-danger-text);
    }

    /* A queue at zero has no work: flat and quiet, so the tile with something in it is the one seen. */
    .card.zero {
      background: var(--color-surface);
      box-shadow: none;
    }

    .card.zero .value,
    .card.zero .label {
      color: var(--color-text-muted);
    }

    .card.zero .glyph {
      background: var(--color-surface-raised);
      color: var(--color-text-muted);
    }

    /* The "Open ›" cue is for a pointer; the whole tile is the target on a phone. */
    .go {
      display: none;
      align-items: center;
      margin-block-start: var(--space-1);
      color: var(--color-primary);
      font-size: var(--text-xs);
      font-weight: var(--weight-medium);
    }

    .label {
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .value {
      font-size: var(--text-2xl);
      font-weight: var(--weight-bold);
      letter-spacing: var(--tracking-display);
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

    @media (min-width: 768px) {
      .card {
        padding: var(--space-4);
      }

      .value {
        font-size: var(--text-3xl);
      }

      .go {
        display: inline-flex;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KpiCard {
  readonly label = input.required<string>();
  /** The figure. `null` means the server did not count, which is not the same as zero. */
  readonly value = input<number | string | null | undefined>(null);
  /** A count with its thousands separators; a string as given. */
  protected readonly shown = computed(() => {
    const value = this.value();
    return typeof value === 'number' ? value.toLocaleString('en-IN') : value;
  });
  readonly hint = input<string | null>(null);
  readonly loading = input(false);
  /** Where the figure is explained. Absent makes the card a plain tile rather than a link. */
  readonly path = input<string | null>(null);
  /** A glyph from the icon registry; an unknown name draws none rather than throwing. */
  readonly icon = input<string | null>(null);
  /**
   * How urgent a non-zero count is. Applied only while the value is above zero, so a clear queue
   * is never drawn as a warning.
   */
  readonly tone = input<'neutral' | 'warning' | 'danger'>('neutral');

  protected readonly iconName = computed<IconName | null>(() => {
    const name = this.icon();
    return name && (ICON_NAMES as readonly string[]).includes(name) ? (name as IconName) : null;
  });

  protected readonly isZero = computed(() => this.value() === 0);

  protected readonly activeTone = computed(() => {
    const value = this.value();
    const hasWork = typeof value === 'string' ? value.length > 0 : typeof value === 'number' && value > 0;
    return hasWork ? this.tone() : 'neutral';
  });
}
