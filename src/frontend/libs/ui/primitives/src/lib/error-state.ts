import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { Alert } from './alert';
import { Button } from './button';

/**
 * What a page shows when it asked for something and did not get it.
 *
 * Distinct from `kh-empty-state` on purpose. "You have not ordered anything yet" is a statement
 * about the customer; a failed read is a statement about us, and the two must never share a
 * screen — a 500 that renders as an empty order history is a customer who thinks their orders are
 * gone. So a failed load says what happened and offers the one thing that helps: trying again.
 *
 * The retry is the component's whole reason to exist. A page that wants to show a failure without
 * offering a retry should use `kh-alert` directly.
 */
@Component({
  selector: 'kh-error-state',
  imports: [Alert, Button],
  template: `
    <kh-alert tone="danger" [heading]="heading()">
      <p class="message">{{ message() }}</p>
      <button khButton variant="secondary" size="sm" type="button" [disabled]="retrying()" (click)="retry.emit()">
        {{ retrying() ? 'Trying again…' : 'Try again' }}
      </button>
    </kh-alert>
  `,
  styles: `
    :host {
      display: block;
    }

    .message {
      margin: 0 0 var(--space-3);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ErrorState {
  readonly heading = input('We could not load this');
  readonly message = input('Something went wrong on our side. Your data is safe — please try again.');
  readonly retrying = input(false);
  readonly retry = output<void>();
}
