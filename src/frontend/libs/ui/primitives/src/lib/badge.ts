import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type BadgeTone = 'neutral' | 'primary' | 'accent' | 'success' | 'warning' | 'danger' | 'info';

/**
 * A small count or status marker.
 *
 * Colour is never the only carrier of the meaning: the badge always contains text, because a
 * green dot and a red dot are the same dot to eight per cent of men in India
 * (docs/10-design-system-placeholder.md §4, WCAG 1.4.1).
 */
@Component({
  selector: 'kh-badge',
  template: '<ng-content />',
  styles: `
    /* A label, not a pill. Bordered and lightly raised like every other control on the page,
       with the corner radius the inputs and buttons use — a solid rounded blob reads as a button
       that does nothing, and a row of them fights the row of real controls beside it. The tone is
       carried by the border and a tint of the surface, with the text left dark for contrast. */
    :host {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: var(--space-1);
      padding: 0 var(--space-2);
      min-width: var(--space-6);
      min-block-size: var(--space-6);
      border-radius: var(--radius-sm);
      font-size: var(--text-xs);
      font-weight: var(--weight-medium);
      line-height: 1;
      white-space: nowrap;
      background: var(--color-surface-raised);
      color: var(--color-text);
      border: 1px solid var(--color-border);
      box-shadow: var(--shadow-sm);
    }

    :host([data-tone='primary']) {
      background: var(--color-primary-subtle);
      border-color: var(--color-primary);
    }

    /* Merchandising, not status: a sale, a new arrival, a bestseller. The one tone whose text is
       coloured rather than left at ink, because the accent strip is what carries the theme's
       second hue onto a listing page — the accent-text role is the strength each theme checked
       against its accent-surface for exactly this pairing. */
    :host([data-tone='accent']) {
      background: var(--color-accent-surface);
      border-color: var(--color-accent);
      color: var(--color-accent-text);
    }

    :host([data-tone='success']) {
      background: var(--color-success-subtle);
      border-color: var(--color-success);
    }

    :host([data-tone='warning']) {
      background: var(--color-warning-subtle);
      border-color: var(--color-warning);
    }

    :host([data-tone='danger']) {
      background: var(--color-danger-subtle);
      border-color: var(--color-danger);
    }

    :host([data-tone='info']) {
      background: var(--color-info-subtle);
      border-color: var(--color-info);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-tone]': 'tone()' },
})
export class Badge {
  readonly tone = input<BadgeTone>('neutral');
}
