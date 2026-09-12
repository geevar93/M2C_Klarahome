import { ChangeDetectionStrategy, Component, effect, input, output, signal } from '@angular/core';
import { Button, Checkbox, Control, Field } from '@klarahome/ui-primitives';
import {
  FormField,
  Validator,
  formField,
  formGroup,
  gstin,
  maxLength,
  mobile,
  normaliseMobile,
  pincode,
  required,
} from '@klarahome/util';

import { AddressView } from './commerce.model';

/** A state, as the form's `select` lists them. `id` is what the API stores. */
export interface StateOptionView {
  readonly id: string;
  readonly name: string;
}

/** What the PIN code lookup answered, so the form can fill the city and state in. */
export interface PincodePlaceView {
  readonly pincode: string;
  readonly city: string;
  readonly stateId: string | null;
  readonly stateName: string;
}

/** The address the form produces — the exact shape the API's `AddressInput` wants. */
export interface AddressFormValue {
  readonly label: string | null;
  readonly recipientName: string;
  readonly mobile: string;
  readonly line1: string;
  readonly line2: string | null;
  readonly landmark: string | null;
  readonly city: string;
  readonly stateId: string;
  readonly pincode: string;
  readonly gstin: string | null;
  readonly type: 'Home' | 'Office';
  readonly isDefaultShipping: boolean;
  readonly isDefaultBilling: boolean;
}

/**
 * The address form — the longest thing a customer types on this storefront, and therefore the one
 * place worth spending care on.
 *
 * Four decisions carry it:
 *
 *  - **The PIN code comes before the city.** Six digits determine the city and the state in India,
 *    so the form asks for them first and fills the other two in (`pincodeEntered` then `place`).
 *    Two fewer fields to type on a phone, and — more importantly — a city spelt the way the postal
 *    service spells it, which is what the courier's serviceability check is keyed on.
 *  - **The city and state stay editable.** The lookup is a convenience, and a PIN code does span
 *    more than one locality. A form that locks a field because a service filled it is a form
 *    somebody eventually cannot complete.
 *  - **Every input carries its real `autocomplete` token** — `postal-code`, `address-line1`,
 *    `tel-national`. This is what lets a browser fill the whole form from a saved profile, and it
 *    is the largest single thing that can be done for checkout completion on a phone.
 *  - **Messages appear on blur, never on the first keystroke** (see `forms.ts` in
 *    `@klarahome/util`). A mobile number turning red at its second digit is nagging, not
 *    validation.
 *
 * GSTIN is offered because a business buyer needs the invoice in the company's name — that is what
 * makes the order a B2B supply for GST — and asking for it after the invoice has been issued is
 * too late.
 */
@Component({
  selector: 'kh-address-form',
  imports: [Button, Checkbox, Control, Field],
  template: `
    <form (submit)="submit($event)" novalidate>
      <div class="pair">
        <kh-field label="Full name" for="addr-name" [error]="form.fields.recipientName.error()">
          <input
            khControl
            id="addr-name"
            type="text"
            autocomplete="name"
            [khInvalid]="!!form.fields.recipientName.error()"
            [value]="form.fields.recipientName.value()"
            (input)="form.fields.recipientName.set($any($event.target).value)"
            (touched)="form.fields.recipientName.markTouched()"
          />
        </kh-field>

        <kh-field
          label="Mobile number"
          for="addr-mobile"
          hint="We send delivery updates to this number."
          [error]="form.fields.mobile.error()"
        >
          <input
            khControl
            khNumeric
            id="addr-mobile"
            type="tel"
            inputmode="numeric"
            maxlength="13"
            autocomplete="tel-national"
            [khInvalid]="!!form.fields.mobile.error()"
            [value]="form.fields.mobile.value()"
            (input)="form.fields.mobile.set($any($event.target).value)"
            (touched)="form.fields.mobile.markTouched()"
          />
        </kh-field>
      </div>

      <kh-field
        label="PIN code"
        for="addr-pincode"
        hint="Six digits. We will fill in the city and state."
        [error]="form.fields.pincode.error()"
      >
        <input
          khControl
          khNumeric
          id="addr-pincode"
          type="text"
          inputmode="numeric"
          maxlength="6"
          autocomplete="postal-code"
          [khInvalid]="!!form.fields.pincode.error()"
          [value]="form.fields.pincode.value()"
          (input)="onPincode($any($event.target).value)"
          (touched)="form.fields.pincode.markTouched()"
        />
      </kh-field>

      <div class="pair">
        <kh-field label="City" for="addr-city" [error]="form.fields.city.error()">
          <input
            khControl
            id="addr-city"
            type="text"
            autocomplete="address-level2"
            [khInvalid]="!!form.fields.city.error()"
            [value]="form.fields.city.value()"
            (input)="form.fields.city.set($any($event.target).value)"
            (touched)="form.fields.city.markTouched()"
          />
        </kh-field>

        <kh-field label="State" for="addr-state" [error]="form.fields.stateId.error()">
          <select
            khControl
            id="addr-state"
            autocomplete="address-level1"
            [khInvalid]="!!form.fields.stateId.error()"
            (change)="form.fields.stateId.set($any($event.target).value)"
            (touched)="form.fields.stateId.markTouched()"
          >
            <option value="" [selected]="form.fields.stateId.value() === ''">Select a state</option>
            @for (state of states(); track state.id) {
              <option [value]="state.id" [selected]="state.id === form.fields.stateId.value()">
                {{ state.name }}
              </option>
            }
          </select>
        </kh-field>
      </div>

      <kh-field label="Flat, house number, building" for="addr-line1" [error]="form.fields.line1.error()">
        <input
          khControl
          id="addr-line1"
          type="text"
          autocomplete="address-line1"
          [khInvalid]="!!form.fields.line1.error()"
          [value]="form.fields.line1.value()"
          (input)="form.fields.line1.set($any($event.target).value)"
          (touched)="form.fields.line1.markTouched()"
        />
      </kh-field>

      <kh-field
        label="Area, street, sector"
        for="addr-line2"
        [optional]="true"
        [error]="form.fields.line2.error()"
      >
        <input
          khControl
          id="addr-line2"
          type="text"
          autocomplete="address-line2"
          [value]="form.fields.line2.value()"
          (input)="form.fields.line2.set($any($event.target).value)"
          (touched)="form.fields.line2.markTouched()"
        />
      </kh-field>

      <kh-field
        label="Landmark"
        for="addr-landmark"
        [optional]="true"
        hint="Helps the delivery partner find you."
        [error]="form.fields.landmark.error()"
      >
        <input
          khControl
          id="addr-landmark"
          type="text"
          [value]="form.fields.landmark.value()"
          (input)="form.fields.landmark.set($any($event.target).value)"
          (touched)="form.fields.landmark.markTouched()"
        />
      </kh-field>

      <kh-field
        label="GSTIN"
        for="addr-gstin"
        [optional]="true"
        hint="For a business invoice in your company's name."
        [error]="form.fields.gstin.error()"
      >
        <input
          khControl
          id="addr-gstin"
          type="text"
          maxlength="15"
          [khInvalid]="!!form.fields.gstin.error()"
          [value]="form.fields.gstin.value()"
          (input)="form.fields.gstin.set($any($event.target).value.toUpperCase())"
          (touched)="form.fields.gstin.markTouched()"
        />
      </kh-field>

      <fieldset class="type">
        <legend>Address type</legend>
        <label class="kh-choice">
          <input
            type="radio"
            name="addr-type"
            value="Home"
            [checked]="type() === 'Home'"
            (change)="type.set('Home')"
          />
          <span>Home</span>
        </label>
        <label class="kh-choice">
          <input
            type="radio"
            name="addr-type"
            value="Office"
            [checked]="type() === 'Office'"
            (change)="type.set('Office')"
          />
          <span>Office</span>
        </label>
      </fieldset>

      <kh-checkbox
        [bare]="true"
        label="Make this my default address"
        inputId="addr-default"
        [checked]="isDefault()"
        (checkedChange)="isDefault.set($event)"
      />

      <div class="actions">
        <button khButton variant="primary" type="submit" [disabled]="saving()">
          {{ saving() ? 'Saving…' : submitLabel() }}
        </button>
        @if (cancellable()) {
          <button khButton variant="tertiary" type="button" (click)="cancelled.emit()">Cancel</button>
        }
      </div>
    </form>
  `,
  styles: `
    :host {
      display: block;
    }

    .pair {
      display: grid;
      gap: 0 var(--space-4);
    }

    @media (min-width: 480px) {
      .pair {
        grid-template-columns: 1fr 1fr;
        align-items: start;
      }
    }

    fieldset.type {
      display: flex;
      gap: var(--space-2);
      margin: 0 0 var(--space-4);
      padding: 0;
      border: 0;
    }

    legend {
      padding: 0;
      margin-block-end: var(--space-1);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin-block-start: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AddressForm {
  readonly states = input.required<readonly StateOptionView[]>();
  /** The address being edited, or null for a new one. */
  readonly address = input<AddressView | null>(null);
  /** What the PIN code lookup last answered. Fills the city and state when it arrives. */
  readonly place = input<PincodePlaceView | null>(null);
  readonly saving = input(false);
  readonly cancellable = input(true);
  readonly submitLabel = input('Save address');

  /** Six digits were typed. The page looks the place up; this component does not fetch. */
  readonly pincodeEntered = output<string>();
  readonly saved = output<AddressFormValue>();
  readonly cancelled = output<void>();

  protected readonly type = signal<'Home' | 'Office'>('Home');
  protected readonly isDefault = signal(false);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    recipientName: this.field('', [required('Name'), maxLength(120, 'Name')]),
    mobile: this.field('', [required('Mobile number'), mobile]),
    pincode: this.field('', [required('PIN code'), pincode]),
    city: this.field('', [required('City'), maxLength(80, 'City')]),
    stateId: this.field('', [required('State')]),
    line1: this.field('', [required('Address'), maxLength(200, 'Address')]),
    line2: this.field('', [maxLength(200, 'Address')]),
    landmark: this.field('', [maxLength(120, 'Landmark')]),
    gstin: this.field('', [gstin]),
  });

  constructor() {
    // The address arrives after the form is constructed — it is resolved, or picked from a list —
    // so seeding is an effect rather than a constructor read.
    effect(() => {
      const address = this.address();
      if (address) this.seed(address);
    });

    // The lookup's answer fills the two fields it determines. It is guarded on the PIN code still
    // being the one that was asked about: a slow answer for 500081 must not overwrite the city of
    // somebody who has since typed 110001.
    effect(() => {
      const place = this.place();
      if (!place || place.pincode !== this.form.fields.pincode.value()) return;
      this.form.fields.city.set(place.city);
      if (place.stateId) this.form.fields.stateId.set(place.stateId);
    });
  }

  protected onPincode(raw: string): void {
    const digits = raw.replace(/\D/g, '').slice(0, 6);
    this.form.fields.pincode.set(digits);
    if (digits.length === 6) this.pincodeEntered.emit(digits);
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (!this.form.submit()) return;

    const values = this.form.values();
    this.saved.emit({
      // A label is what an address book shows instead of the street — "Home", "Office". It is the
      // address type here rather than a field of its own: asking somebody to name their address
      // and then also to categorise it is one question too many on a phone.
      label: this.type(),
      recipientName: values.recipientName,
      // Normalised on the way out, not on the way in: somebody pasting "+91 98765 43210" should
      // see what they pasted while they check it, and the API should receive ten digits.
      mobile: normaliseMobile(values.mobile),
      line1: values.line1,
      line2: values.line2 || null,
      landmark: values.landmark || null,
      city: values.city,
      stateId: values.stateId,
      pincode: values.pincode,
      gstin: values.gstin ? values.gstin.toUpperCase() : null,
      type: this.type(),
      isDefaultShipping: this.isDefault(),
      // Billing follows shipping. A separate billing address is a Phase 2 B2B concern, and there is
      // nowhere on this storefront a customer can ask for one.
      isDefaultBilling: this.isDefault(),
    });
  }

  private field(initial: string, validators: readonly Validator[]): FormField {
    return formField(initial, validators, this.submitted);
  }

  /** Fills the form from an existing address. */
  private seed(address: AddressView): void {
    this.form.reset({
      recipientName: address.recipientName,
      mobile: address.mobile,
      pincode: address.pincode,
      city: address.city,
      stateId: address.stateId,
      line1: address.line1,
      line2: address.line2 ?? '',
      landmark: address.landmark ?? '',
      gstin: address.gstin ?? '',
    });
    this.type.set(address.type);
    this.isDefault.set(address.isDefaultShipping);
  }
}
