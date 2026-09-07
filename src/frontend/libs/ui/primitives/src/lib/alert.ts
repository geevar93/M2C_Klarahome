import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { Button } from './button';
import { Icon, IconName } from './icon';

export type AlertTone = 'info' | 'success' | 'warning' | 'danger';

/**
 * An inline message about the thing it sits next to.
 *
 * `role="alert"` only for a failure: the role interrupts whatever a screen reader is saying, and
 * interrupting somebody mid-sentence to tell them a coupon was applied is worse than waiting.
 * Everything else is a `status`, which is announced when the user reaches a natural break.
 */
@Component({
  selector: 'kh-alert',
  imports: [Button, Icon],
  template: `
    <kh-icon [name]="icon()" size="sm" />
    <div class="body">
      @if (heading()) {
        <p class="heading">{{ heading() }}</p>
      }
      <ng-content />
    </div>
    @if (dismissible()) {
      <button
        khButton
        variant="tertiary"
        size="sm"
        [iconOnly]="true"
        aria-label="Dismiss"
        (click)="dismissed.emit()"
      >
        <kh-icon name="close" size="sm" />
      </button>
    }
  `,
  styles: `
    :host {
      display: flex;
      align-items: flex-start;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
      border: 1px solid var(--color-border);
      border-left-width: var(--space-1);
      border-radius: var(--radius-md);
      background: var(--color-surface);
      color: var(--color-text);
      font-size: var(--text-sm);
    }

    :host([data-tone='info']) {
      border-left-color: var(--color-info);
    }

    :host([data-tone='success']) {
      border-left-color: var(--color-success);
    }

    :host([data-tone='warning']) {
      border-left-color: var(--color-warning);
    }

    :host([data-tone='danger']) {
      border-left-color: var(--color-danger);
    }

    .body {
      flex: 1;
      min-width: 0;
    }

    .heading {
      margin: 0 0 var(--space-1);
      font-weight: var(--weight-medium);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-tone]': 'tone()',
    '[attr.role]': 'role()',
  },
})
export class Alert {
  readonly tone = input<AlertTone>('info');
  readonly heading = input<string | null>(null);
  readonly dismissible = input(false);
  readonly dismissed = output<void>();

  protected readonly role = computed(() => (this.tone() === 'danger' ? 'alert' : 'status'));

  protected readonly icon = computed<IconName>(() => {
    switch (this.tone()) {
      case 'success':
        return 'check';
      case 'warning':
      case 'danger':
        return 'alert';
      default:
        return 'info';
    }
  });
}
