import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '@klarahome/data-access-auth';
import { Alert, Button, Checkbox, Control, Field } from '@klarahome/ui-primitives';
import {
  email,
  formField,
  formGroup,
  matches,
  minLength,
  mobile,
  normaliseMobile,
  required,
} from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { SignInFlow } from '../../core/sign-in.flow';

/**
 * Create an account — `/auth/register`.
 *
 * Four fields, and each one earns its place: an email address because that is what the account is
 * keyed on, a password twice because there is no way back from a typo in one that is masked, and a
 * mobile number because a delivery partner needs to ring somebody. The number is optional here — it
 * is asked for again, and required, on the address the order is delivered to, and demanding it twice
 * on a signup form is how people leave.
 *
 * **Marketing consent is opt-in and unticked.** A pre-ticked box is not consent under the DPDP Act,
 * and the API records when it was given (Step 7's `marketingConsentAt`).
 *
 * The API signs the new customer in, so the response is adopted like any other sign-in and the same
 * `SignInFlow` runs — cart merged, wishlist loaded, `returnUrl` honoured. Sending somebody back to a
 * sign-in screen straight after choosing a password is a step that exists only to lose them.
 */
@Component({
  selector: 'kh-register-page',
  imports: [Alert, Button, Checkbox, Control, Field, RouterLink],
  template: `
    <div class="panel">
      <h1>Create an account</h1>
      <p class="lead">
        Or <a routerLink="/auth/login" [queryParams]="{ returnUrl: returnUrl() }">sign in with a code</a> — no
        password needed.
      </p>

      @if (failure(); as message) {
        <kh-alert tone="danger">{{ message }}</kh-alert>
      }

      <form (submit)="submit($event)" novalidate>
        <kh-field label="Email address" for="reg-email" [error]="form.fields.email.error()">
          <input
            khControl
            id="reg-email"
            type="email"
            autocomplete="email"
            [khInvalid]="!!form.fields.email.error()"
            [value]="form.fields.email.value()"
            (input)="form.fields.email.set($any($event.target).value)"
            (touched)="form.fields.email.markTouched()"
          />
        </kh-field>

        <kh-field
          label="Mobile number"
          for="reg-mobile"
          [optional]="true"
          hint="So a delivery partner can reach you."
          [error]="form.fields.mobile.error()"
        >
          <input
            khControl
            khNumeric
            id="reg-mobile"
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

        <kh-field
          label="Password"
          for="reg-password"
          hint="At least 10 characters."
          [error]="form.fields.password.error()"
        >
          <input
            khControl
            id="reg-password"
            type="password"
            autocomplete="new-password"
            [khInvalid]="!!form.fields.password.error()"
            [value]="form.fields.password.value()"
            (input)="form.fields.password.set($any($event.target).value)"
            (touched)="form.fields.password.markTouched()"
          />
        </kh-field>

        <kh-field label="Confirm password" for="reg-confirm" [error]="form.fields.confirm.error()">
          <input
            khControl
            id="reg-confirm"
            type="password"
            autocomplete="new-password"
            [khInvalid]="!!form.fields.confirm.error()"
            [value]="form.fields.confirm.value()"
            (input)="form.fields.confirm.set($any($event.target).value)"
            (touched)="form.fields.confirm.markTouched()"
          />
        </kh-field>

        <kh-checkbox
          [bare]="true"
          label="Send me offers and new arrivals"
          description="You can change this at any time in your notification preferences."
          inputId="reg-consent"
          [checked]="consent()"
          (checkedChange)="consent.set($event)"
        />

        <button khButton variant="primary" [block]="true" type="submit" [disabled]="busy()">
          {{ busy() ? 'Creating…' : 'Create account' }}
        </button>
      </form>
    </div>
  `,
  styles: `
    :host {
      display: block;
      padding-block: var(--space-8) var(--space-10);
    }

    .panel {
      max-inline-size: 26rem;
      margin-inline: auto;
    }

    h1 {
      font-size: var(--text-2xl);
    }

    .lead {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    form {
      margin-block-start: var(--space-4);
    }

    kh-checkbox {
      margin-block-end: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly flow = inject(SignInFlow);
  private readonly route = inject(ActivatedRoute);

  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly consent = signal(false);

  protected readonly returnUrl = computed(
    () => this.route.snapshot.queryParamMap.get('returnUrl') ?? undefined,
  );

  private readonly submitted = signal(false);
  private readonly password = formField(
    '',
    [required('Password'), minLength(10, 'Password')],
    this.submitted,
  );

  protected readonly form = formGroup(this.submitted, {
    email: formField('', [required('Email address'), email()], this.submitted),
    mobile: formField('', [mobile], this.submitted),
    password: this.password,
    // Compared against the password's own signal, so the message appears the moment the two stop
    // agreeing rather than on submit.
    confirm: formField(
      '',
      [required('Confirmation'), matches(this.password.value, 'The passwords')],
      this.submitted,
    ),
  });

  protected submit(event: Event): void {
    event.preventDefault();
    if (!this.form.submit() || this.busy()) return;

    const values = this.form.values();
    this.busy.set(true);
    this.failure.set(null);

    this.auth
      .register({
        email: values.email,
        password: values.password,
        mobile: values.mobile ? normaliseMobile(values.mobile) : null,
        marketingConsent: this.consent(),
      })
      .subscribe({
        next: (response) => {
          this.busy.set(false);
          this.flow.complete(response, this.returnUrl() ?? null);
        },
        error: (error: unknown) => {
          this.busy.set(false);
          // The API's own words: it is the only thing that knows whether the address is taken or
          // the password failed a policy, and either is something the customer must act on.
          this.failure.set(
            describeError(error, 'We could not create that account. Please check your details.'),
          );
        },
      });
  }
}
