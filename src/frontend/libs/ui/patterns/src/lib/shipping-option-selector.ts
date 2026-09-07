import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MoneyPipe } from '@klarahome/i18n';

import { ShippingGroupView } from './commerce.model';

/** Which service was chosen for which seller. */
export interface ShippingChoice {
  readonly vendorId: string;
  readonly optionCode: string;
}

/**
 * How each seller's parcel travels — one group of choices per sub-order.
 *
 * **Per seller, not per order**, because that is what the basket actually is on this marketplace: a
 * cart with three sellers becomes three sub-orders, three parcels, three couriers and three
 * promises (Step 14, Step 16). A single "standard or express" control across the whole basket would
 * be a promise the platform cannot keep, and the shopper would discover that from three different
 * tracking pages.
 *
 * The delivery promise is a **range of days**, not a date. The API gives a minimum and a maximum
 * because that is what a courier's service level actually guarantees; turning it into "arrives
 * Thursday" is a specificity nobody sold us.
 *
 * A group with one option still renders it, as a selected radio the customer cannot change. Hiding
 * it would leave a shipping charge on the summary with nothing on the page to explain it.
 */
@Component({
  selector: 'kh-shipping-option-selector',
  imports: [MoneyPipe],
  template: `
    @for (group of groups(); track group.vendorId) {
      <fieldset>
        <legend>{{ group.sellerName }}</legend>

        @for (option of group.options; track option.code) {
          <label class="kh-choice">
            <input
              type="radio"
              [name]="'shipping-' + group.vendorId"
              [value]="option.code"
              [checked]="option.code === group.selectedCode"
              (change)="chosen.emit({ vendorId: group.vendorId, optionCode: option.code })"
            />
            <span class="body">
              <span class="head">
                <span class="name">{{ option.name }}</span>
                <span class="amount">{{ option.amount | khMoney }}</span>
              </span>
              <span class="promise">
                {{ promise(option.promisedMinDays, option.promisedMaxDays) }}
                @if (option.carrier) {
                  · {{ option.carrier }}
                }
              </span>
              @if (!option.isCodAvailable) {
                <span class="promise">Prepaid only on this service.</span>
              }
            </span>
          </label>
        }

        @if (group.options.length === 0) {
          <p class="none">No delivery service is available for this seller to your address.</p>
        }
      </fieldset>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--space-5);
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
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
      color: var(--color-text-muted);
    }

    .body {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      min-width: 0;
      flex: 1;
    }

    .head {
      display: flex;
      justify-content: space-between;
      gap: var(--space-3);
      font-size: var(--text-sm);
    }

    .name {
      font-weight: var(--weight-medium);
    }

    .amount {
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }

    .promise,
    .none {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .none {
      margin: 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShippingOptionSelector {
  readonly groups = input.required<readonly ShippingGroupView[]>();
  readonly chosen = output<ShippingChoice>();

  /** "in 3–5 days", or "in 2 days" when the courier's range is a single number. */
  protected promise(min: number, max: number): string {
    if (max <= 0) return 'Delivery date confirmed after dispatch';
    if (min === max) return `Arrives in ${min} ${min === 1 ? 'day' : 'days'}`;
    return `Arrives in ${min}–${max} days`;
  }
}
