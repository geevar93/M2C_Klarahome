import { ChangeDetectionStrategy, Component, input, linkedSignal, output, signal } from '@angular/core';
import { Button, Icon } from '@klarahome/ui-primitives';

import { DeliveryEstimateView } from './catalog.model';

/**
 * "Deliver to 500081" — the control an Indian shopper looks for before the price.
 *
 * PIN-code-first is a stated behaviour of this storefront
 * (docs/05-frontend-architecture.md §3.6): the code is asked for on the PDP, remembered, and
 * pre-applied at checkout. This component asks and reports; remembering is the page's job, because
 * storage is not something a presentational component should reach for.
 *
 * The result is announced. A shopper who taps "Check" and cannot see the line that appears below
 * is otherwise told nothing at all, and whether something can be delivered is the single most
 * consequential fact on the page — hence `role="status"` on the outcome rather than a toast.
 *
 * Six digits, and the first may not be zero: that is the format of every Indian PIN code, and
 * checking it here saves a round trip and gives a better message than the API's would be.
 */
@Component({
  selector: 'kh-delivery-estimator',
  imports: [Button, Icon],
  template: `
    <form (submit)="submit($event)">
      <label [attr.for]="inputId()">Check delivery</label>
      <div class="row">
        <input
          [id]="inputId()"
          type="text"
          inputmode="numeric"
          autocomplete="postal-code"
          maxlength="6"
          placeholder="PIN code"
          [value]="pincode()"
          [attr.aria-invalid]="showFormatError() ? 'true' : null"
          [attr.aria-describedby]="inputId() + '-status'"
          (input)="onInput($any($event.target).value)"
        />
        <button khButton variant="secondary" type="submit" [disabled]="checking() || !isValid()">
          {{ checking() ? 'Checking…' : 'Check' }}
        </button>
      </div>
    </form>

    <p class="status" role="status" [id]="inputId() + '-status'">
      @if (showFormatError()) {
        Enter a six-digit PIN code.
      } @else if (estimate(); as result) {
        @if (result.deliverable) {
          <kh-icon name="truck" size="sm" />
          <span>
            Delivered to {{ result.place || result.pincode }}
            @if (result.etaDays !== null) {
              in {{ result.etaDays }} {{ result.etaDays === 1 ? 'day' : 'days' }}
            }
            · {{ result.codAvailable ? 'Cash on delivery available' : 'Prepaid only' }}
          </span>
        } @else {
          <!-- The API's own sentence, verbatim: it distinguishes "we do not deliver to this area
               yet" from "no courier serves this PIN code", and rewording it here would lose that. -->
          <span>{{ result.message || 'We cannot deliver to this PIN code yet.' }}</span>
        }
      }
    </p>
  `,
  styles: `
    :host {
      display: block;
    }

    label {
      display: block;
      margin-block-end: var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .row {
      display: flex;
      gap: var(--space-2);
      max-inline-size: 22rem;
    }

    input {
      flex: 1;
      min-inline-size: 0;
      min-block-size: var(--touch-target-min);
      padding-inline: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      font-variant-numeric: tabular-nums;
    }

    input[aria-invalid='true'] {
      border-color: var(--color-danger);
    }

    .status {
      display: flex;
      align-items: flex-start;
      gap: var(--space-2);
      margin: var(--space-2) 0 0;
      min-block-size: var(--space-5);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeliveryEstimator {
  /** The remembered PIN code. Seeds the box; the shopper may type over it. */
  readonly initialPincode = input('');
  readonly estimate = input<DeliveryEstimateView | null>(null);
  readonly checking = input(false);
  readonly inputId = input('kh-pincode');

  readonly checked = output<string>();

  protected readonly pincode = linkedSignal(() => this.initialPincode());
  /** Set on submit only — complaining at the second digit is nagging, not validation. */
  protected readonly showFormatError = signal(false);

  protected isValid(): boolean {
    return /^[1-9][0-9]{5}$/.test(this.pincode());
  }

  protected onInput(raw: string): void {
    // Digits only. A shopper pasting "500 081" from a bill should not be told off for the space.
    this.pincode.set(raw.replace(/\D/g, '').slice(0, 6));
    if (this.showFormatError() && this.isValid()) this.showFormatError.set(false);
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (!this.isValid()) {
      this.showFormatError.set(true);
      return;
    }
    this.showFormatError.set(false);
    this.checked.emit(this.pincode());
  }
}
