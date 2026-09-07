import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@klarahome/data-access-auth';
import { Alert, Button, Control, Field } from '@klarahome/ui-primitives';
import { email, formField, formGroup, matches, minLength, required } from '@klarahome/util';

import { describeError } from '../../core/describe-error';

/**
 * Password reset — `/auth/forgot-password`, in both of its halves.
 *
 * One route and two states, because they are two halves of one act: the form that asks for the
 * email address, and the form the emailed link comes back to with a `token` in the query string.
 * Splitting them across two routes would mean a second screen whose only job is to read a query
 * parameter, and a customer who lost the tab could not get back to either.
 *
 * **The request never says whether the address exists.** The API answers 204 either way, and this
 * screen says "if that address is registered, we have sent you a link" — because a screen that said
 * "we have sent you an email" would be lying half the time and telling anyone who asked which
 * addresses hold accounts the other half (docs/07-security-compliance.md).
 */
@Component({
  selector: 'kh-forgot-password-page',
  imports: [Alert, Button, Control, Field, RouterLink],
  template: `
    <div class="panel">
      @if (token()) {
        <h1>Choose a new password</h1>

        @if (failure(); as message) {
          <kh-alert tone="danger">{{ message }}</kh-alert>
        }

        <form (submit)="reset($event)" novalidate>
          <kh-field label="Email address" for="reset-email" [error]="resetForm.fields.email.error()">
            <input
              khControl
              id="reset-email"
              type="email"
              autocomplete="email"
              [khInvalid]="!!resetForm.fields.email.error()"
              [value]="resetForm.fields.email.value()"
              (input)="resetForm.fields.email.set($any($event.target).value)"
              (touched)="resetForm.fields.email.markTouched()"
            />
          </kh-field>

          <kh-field
            label="New password"
            for="reset-password"
            hint="At least 10 characters."
            [error]="resetForm.fields.password.error()"
          >
            <input
              khControl
              id="reset-password"
              type="password"
              autocomplete="new-password"
              [khInvalid]="!!resetForm.fields.password.error()"
              [value]="resetForm.fields.password.value()"
              (input)="resetForm.fields.password.set($any($event.target).value)"
              (touched)="resetForm.fields.password.markTouched()"
            />
          </kh-field>

          <kh-field
            label="Confirm new password"
            for="reset-confirm"
            [error]="resetForm.fields.confirm.error()"
          >
            <input
              khControl
              id="reset-confirm"
              type="password"
              autocomplete="new-password"
              [khInvalid]="!!resetForm.fields.confirm.error()"
              [value]="resetForm.fields.confirm.value()"
              (input)="resetForm.fields.confirm.set($any($event.target).value)"
              (touched)="resetForm.fields.confirm.markTouched()"
            />
          </kh-field>

          <button khButton variant="primary" [block]="true" type="submit" [disabled]="busy()">
            {{ busy() ? 'Saving…' : 'Save new password' }}
          </button>
        </form>
      } @else if (sent()) {
        <h1>Check your email</h1>
        <kh-alert tone="info">
          If that address is registered, we have sent a link to reset the password. It expires in an hour.
        </kh-alert>
        <p class="foot">
          <a routerLink="/auth/login" [queryParams]="{ returnUrl: returnUrl() }">Back to sign in</a>
        </p>
      } @else {
        <h1>Reset your password</h1>
        <p class="lead">Tell us the email address on the account and we will send a link.</p>

        @if (failure(); as message) {
          <kh-alert tone="danger">{{ message }}</kh-alert>
        }

        <form (submit)="request($event)" novalidate>
          <kh-field label="Email address" for="forgot-email" [error]="requestForm.fields.email.error()">
            <input
              khControl
              id="forgot-email"
              type="email"
              autocomplete="email"
              autofocus
              [khInvalid]="!!requestForm.fields.email.error()"
              [value]="requestForm.fields.email.value()"
              (input)="requestForm.fields.email.set($any($event.target).value)"
              (touched)="requestForm.fields.email.markTouched()"
            />
          </kh-field>

          <button khButton variant="primary" [block]="true" type="submit" [disabled]="busy()">
            {{ busy() ? 'Sending…' : 'Send the link' }}
          </button>
        </form>

        <p class="foot">
          Signing in with a code instead?
          <a routerLink="/auth/login" [queryParams]="{ returnUrl: returnUrl() }">Use your mobile number</a>
        </p>
      }
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

    .foot {
      margin-block-start: var(--space-4);
      font-size: var(--text-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForgotPasswordPage {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly failure = signal<string | null>(null);

  private readonly params = this.route.snapshot.queryParamMap;

  /** Present when the customer followed the emailed link. Its absence is what shows the request form. */
  protected readonly token = signal(this.params.get('token'));
  protected readonly returnUrl = computed(() => this.params.get('returnUrl') ?? undefined);

  private readonly requestSubmitted = signal(false);
  private readonly resetSubmitted = signal(false);
  private readonly newPassword = formField(
    '',
    [required('Password'), minLength(10, 'Password')],
    this.resetSubmitted,
  );

  protected readonly requestForm = formGroup(this.requestSubmitted, {
    email: formField('', [required('Email address'), email()], this.requestSubmitted),
  });

  protected readonly resetForm = formGroup(this.resetSubmitted, {
    // Prefilled from the link when the API put it there, and still editable — the customer knows
    // which address they used and the token is what actually authorises the change.
    email: formField(
      this.params.get('email') ?? '',
      [required('Email address'), email()],
      this.resetSubmitted,
    ),
    password: this.newPassword,
    confirm: formField(
      '',
      [required('Confirmation'), matches(this.newPassword.value, 'The passwords')],
      this.resetSubmitted,
    ),
  });

  protected request(event: Event): void {
    event.preventDefault();
    if (!this.requestForm.submit() || this.busy()) return;

    this.busy.set(true);
    this.failure.set(null);

    this.auth.forgotPassword(this.requestForm.values().email).subscribe({
      next: () => {
        this.busy.set(false);
        this.sent.set(true);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        // A failure here is a transport or a rate limit, never "no such account" — that answers 204
        // like everything else.
        this.failure.set(describeError(error, 'We could not send that just now. Please try again shortly.'));
      },
    });
  }

  protected reset(event: Event): void {
    event.preventDefault();
    if (!this.resetForm.submit() || this.busy()) return;

    const { email: address, password } = this.resetForm.values();
    this.busy.set(true);
    this.failure.set(null);

    this.auth.resetPassword(address, this.token() ?? '', password).subscribe({
      next: () => {
        this.busy.set(false);
        // Deliberately not signed in here. A reset invalidates every session (Step 7), and proving
        // the new password once is the point of having set it.
        void this.router.navigate(['/auth/login'], {
          queryParams: { returnUrl: this.returnUrl() },
        });
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.failure.set(
          describeError(error, 'That link is no longer valid. Ask for a new one and try again.'),
        );
      },
    });
  }
}
