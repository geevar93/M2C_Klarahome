import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CompactNumberPipe } from '@klarahome/i18n';

/**
 * A star rating, shown as stars and stated as text.
 *
 * The stars are decorative — `aria-hidden` — and the accessible name is the sentence beside them:
 * "4.3 out of 5, 128 ratings". Five separate star glyphs read out individually are noise, and a
 * partially filled star has no reading at all.
 *
 * A product with no ratings renders nothing rather than an empty five-star row. Zero stars and
 * "no ratings yet" look identical at a glance, and the first is a judgement the shop has not made.
 */
@Component({
  selector: 'kh-rating',
  imports: [CompactNumberPipe],
  template: `
    @if (average(); as value) {
      <span class="stars" aria-hidden="true">
        <span class="track">★★★★★</span>
        <span class="fill" [style.inline-size.%]="fillPercent()">★★★★★</span>
      </span>
      <span class="value">{{ rounded() }}</span>
      @if (count() > 0) {
        <span class="count">({{ count() | khCompactNumber }})</span>
      }
      <span class="kh-visually-hidden">{{ description() }}</span>
    } @else if (showEmpty()) {
      <span class="empty">No ratings yet</span>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
      line-height: 1;
    }

    .stars {
      position: relative;
      display: inline-block;
      /* A fixed measure so the label beside it does not move between a 3.0 and a 4.5. */
      inline-size: 5.5em;
      font-size: var(--kh-rating-size, var(--text-base));
      letter-spacing: 0.08em;
      white-space: nowrap;
    }

    .track {
      color: var(--color-border-strong);
    }

    .fill {
      position: absolute;
      inset-block-start: 0;
      inset-inline-start: 0;
      overflow: hidden;
      color: var(--color-warning);
    }

    .value {
      font-weight: var(--weight-medium);
      color: var(--color-text);
    }

    .empty {
      font-size: var(--text-xs);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[style.--kh-rating-size]': 'size() === "sm" ? "var(--text-sm)" : "var(--text-base)"' },
})
export class Rating {
  /** The average out of five, or null when nothing has been rated. */
  readonly average = input<number | null>(null);
  readonly count = input(0);
  readonly size = input<'sm' | 'md'>('md');
  /** Whether "No ratings yet" is worth the line. True on a PDP, false on a card. */
  readonly showEmpty = input(false);

  protected readonly rounded = computed(() => (this.average() ?? 0).toFixed(1));

  /** Clamped, because a bad aggregate must not paint six stars. */
  protected readonly fillPercent = computed(() => Math.max(0, Math.min(5, this.average() ?? 0)) * 20);

  protected readonly description = computed(() => {
    const count = this.count();
    const ratings = count === 1 ? '1 rating' : `${count} ratings`;
    return `${this.rounded()} out of 5${count > 0 ? `, ${ratings}` : ''}`;
  });
}
