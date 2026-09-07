import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type BadgeTone = 'neutral' | 'primary' | 'success' | 'warning' | 'danger' | 'info';

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
    :host {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: var(--space-1);
      padding: 0 var(--space-2);
      min-width: var(--space-5);
      border-radius: var(--radius-full);
      font-size: var(--text-xs);
      font-weight: var(--weight-medium);
      line-height: var(--space-5);
      background: var(--color-surface);
      color: var(--color-text);
      border: 1px solid var(--color-border);
    }

    :host([data-tone='primary']) {
      background: var(--color-primary);
      color: var(--color-on-primary);
      border-color: var(--color-primary);
    }

    :host([data-tone='success']) {
      background: var(--color-success);
      color: var(--color-text-inverse);
      border-color: var(--color-success);
    }

    :host([data-tone='warning']) {
      background: var(--color-warning);
      color: var(--color-text-inverse);
      border-color: var(--color-warning);
    }

    :host([data-tone='danger']) {
      background: var(--color-danger);
      color: var(--color-text-inverse);
      border-color: var(--color-danger);
    }

    :host([data-tone='info']) {
      background: var(--color-info);
      color: var(--color-text-inverse);
      border-color: var(--color-info);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-tone]': 'tone()' },
})
export class Badge {
  readonly tone = input<BadgeTone>('neutral');
}
