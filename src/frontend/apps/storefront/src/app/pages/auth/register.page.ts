import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
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
import { PASSWORD_HINT, PASSWORD_MIN_LENGTH } from '../../core/password-policy';
import { SignInFlow } from '../../core/sign-in.flow';
import { AuthLayout } from './auth.layout';
import { SocialSignIn } from './social-sign-in';

/**
 * Create an account — `/auth/register`.
 *
 * Four fields, and each one earns its place: an email address because that is what the account is
 * keyed on, a password twice because there is no way back from a typo in one that is masked, and a
 * mobile number because a delivery partner needs to ring somebody. The number is optional here — it
 * is asked for again, and required, on the address the order is delivered to, and demanding it twice
 * on a signup form is how people leave. It is **only** a contact number now: mobile-OTP sign-in has
 * been withdrawn (see `LoginPage`), so nothing about the account is keyed on it.
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
  imports: [Alert, AuthLayout, Button, Checkbox, Control, Field, RouterLink, SocialSignIn],
  template: `
    <kh-auth-layout title="Create an account">
      <p class="lead">
        Already have one?
        <a routerLink="/auth/login" [queryParams]="{ returnUrl: returnUrl() }">Sign in</a>.
        Forgot your password?
        <a routerLink="/auth/forgot-password" [queryParams]="{ returnUrl: returnUrl() }">Reset it</a>.
      </p>

      @if (failure(); as message) {
        <kh-alert tone="danger" #errorAlert tabindex="-1">{{ message }}</kh-alert>
      }

      <!--
        Above the form, because it is both the faster route and the one that arrives with the
        address already verified — a stronger assertion than our own verification link would have
        been (ADR-014). It renders nothing when no provider is configured.
      -->
      <kh-social-sign-in [returnUrl]="returnUrl()" />

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
          [hint]="passwordHint"
          [error]="form.fields.password.error()"
        >
          <div class="password-wrap">
            <input
              khControl
              id="reg-password"
              [type]="passwordVisible() ? 'text' : 'password'"
              autocomplete="new-password"
              [khInvalid]="!!form.fields.password.error()"
              [value]="form.fields.password.value()"
              (input)="form.fields.password.set($any($event.target).value)"
              (touched)="form.fields.password.markTouched()"
            />
            <button
              type="button"
              class="reveal"
              [attr.aria-pressed]="passwordVisible()"
              aria-controls="reg-password"
              (click)="passwordVisible.set(!passwordVisible())"
            >
              {{ passwordVisible() ? 'Hide' : 'Show' }}
            </button>
          </div>
        </kh-field>

        <kh-field label="Confirm password" for="reg-confirm" [error]="form.fields.confirm.error()">
          <div class="password-wrap">
            <input
              khControl
              id="reg-confirm"
              [type]="confirmVisible() ? 'text' : 'password'"
              autocomplete="new-password"
              [khInvalid]="!!form.fields.confirm.error()"
              [value]="form.fields.confirm.value()"
              (input)="form.fields.confirm.set($any($event.target).value)"
              (touched)="form.fields.confirm.markTouched()"
            />
            <button
              type="button"
              class="reveal"
              [attr.aria-pressed]="confirmVisible()"
              aria-controls="reg-confirm"
              (click)="confirmVisible.set(!confirmVisible())"
            >
              {{ confirmVisible() ? 'Hide' : 'Show' }}
            </button>
          </div>
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
    </kh-auth-layout>
  `,
  styles: `
    kh-checkbox {
      margin-block-end: var(--space-4);
    }

    .password-wrap {
      position: relative;
    }

    .password-wrap .kh-control {
      padding-inline-end: var(--space-12);
    }

    .reveal {
      position: absolute;
      inset-block-start: 50%;
      inset-inline-end: var(--space-2);
      transform: translateY(-50%);
      border: none;
      background: none;
      padding: var(--space-1) var(--space-2);
      color: var(--color-primary);
      font-size: var(--text-xs);
      font-weight: var(--weight-medium);
      cursor: pointer;
    }

    .reveal:hover,
    .reveal:focus-visible {
      text-decoration: underline;
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
  protected readonly passwordVisible = signal(false);
  protected readonly confirmVisible = signal(false);
  protected readonly passwordHint = PASSWORD_HINT;

  protected readonly returnUrl = computed(
    () => this.route.snapshot.queryParamMap.get('returnUrl') ?? undefined,
  );

  private readonly submitted = signal(false);
  private readonly password = formField(
    '',
    [required('Password'), minLength(PASSWORD_MIN_LENGTH, 'Password')],
    this.submitted,
  );

  protected readonly form = formGroup(this.submitted, {
    email: formField('', [required('Email address'), email()], this.submitted),
    mobile: formField('', [mobile], this.submitted),
    password: this.password,
    // Compared against the password's own signal, so the message appears the moment the two stop
    // agreeing rather than on submit. The label matches the visible "Confirm password" label above
    // it, so the field's own error reads as an answer to the field it is under.
    confirm: formField(
      '',
      [required('Confirm password'), matches(this.password.value, 'The passwords')],
      this.submitted,
    ),
  });

  private readonly errorAlert = viewChild('errorAlert', { read: ElementRef<HTMLElement> });

  constructor() {
    effect(() => {
      if (this.failure()) this.errorAlert()?.nativeElement.focus();
    });
  }

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
          this.flow.complete(response, this.returnUrl() ?? null, 'password');
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
