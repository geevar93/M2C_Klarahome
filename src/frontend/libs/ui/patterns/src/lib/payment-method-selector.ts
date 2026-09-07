import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MoneyPipe } from '@klarahome/i18n';

import { PaymentMethodView } from './commerce.model';

/**
 * How the customer wants to pay.
 *
 * Native radios in a `fieldset` with a `legend`, so the group is announced as a group, arrow keys
 * move within it, and the browser's own semantics do the work. A list of tappable cards with a
 * border colour looks the same and is unusable without sight.
 *
 * **An unavailable method is shown, disabled, with the reason.** Cash on delivery is refused for a
 * specific and knowable cause — the PIN code is prepaid-only, the basket is over the COD ceiling,
 * a seller does not accept it — and a customer who came expecting to pay cash needs to be told
 * which, not to find the option missing. The sentence is the API's own (Step 15, Step 16A), shown
 * verbatim: it distinguishes cases this component has no way to tell apart.
 *
 * The fee is a fee, not a surcharge invented here: the pricing engine put it on the quote, and
 * showing it beside the method is what stops the total moving unexplained on the next screen.
 */
@Component({
  selector: 'kh-payment-method-selector',
  imports: [MoneyPipe],
  template: `
    <fieldset>
      <legend>{{ legend() }}</legend>

      @for (method of methods(); track method.method) {
        <label class="kh-choice" [class.unavailable]="!method.isAvailable">
          <input
            type="radio"
            name="payment-method"
            [value]="method.method"
            [checked]="method.method === selected()"
            [disabled]="!method.isAvailable"
            (change)="chosen.emit(method.method)"
          />
          <span class="body">
            <span class="head">
              <span class="name">{{ method.name }}</span>
              @if (method.fee; as fee) {
                <span class="fee">+{{ fee | khMoney }} handling</span>
              }
            </span>
            @if (!method.isAvailable && method.reason) {
              <span class="reason">{{ method.reason }}</span>
            }
          </span>
        </label>
      }

      @if (methods().length === 0) {
        <p class="none">
          No payment method is available for this order yet. Check your delivery address, or try again in a
          moment.
        </p>
      }
    </fieldset>
  `,
  styles: `
    :host {
      display: block;
    }

    fieldset {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      border: 0;
    }

    legend {
      padding: 0;
      margin-block-end: var(--space-2);
      font-size: var(--text-base);
      font-weight: var(--weight-bold);
    }

    .body {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      min-width: 0;
    }

    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: var(--space-2);
    }

    .name {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .fee,
    .reason {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .none {
      margin: 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaymentMethodSelector {
  readonly methods = input.required<readonly PaymentMethodView[]>();
  readonly selected = input<string | null>(null);
  readonly legend = input('How would you like to pay?');

  readonly chosen = output<string>();
}
