import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * What a page shows when there is nothing to show.
 *
 * Plain text and a single action, no illustration — that is the placeholder rule
 * (docs/10-design-system-placeholder.md §3) and it is also the right shape: an empty state exists
 * to say what happened and what to do next, and a picture says neither.
 *
 * It is a heading and a paragraph rather than a `<p>` in a box, because a screen-reader user
 * navigating by heading needs to find the reason the region is empty.
 */
@Component({
  selector: 'kh-empty-state',
  template: `
    <h2 class="heading">{{ heading() }}</h2>
    @if (message()) {
      <p class="message">{{ message() }}</p>
    }
    <ng-content />
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--space-3);
      padding: var(--space-10) var(--space-4);
      text-align: center;
      color: var(--color-text);
    }

    .heading {
      margin: 0;
      font-size: var(--text-lg);
    }

    .message {
      margin: 0;
      max-width: 48ch;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmptyState {
  readonly heading = input.required<string>();
  readonly message = input<string | null>(null);
}
