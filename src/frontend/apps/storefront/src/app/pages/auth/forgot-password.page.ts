import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@klarahome/data-access-auth';
import { Alert, Button, Control, Field } from '@klarahome/ui-primitives';
import { ToastService, email, formField, formGroup, matches, minLength, required } from '@klarahome/util';
import { map } from 'rxjs';

import { describeError } from '../../core/describe-error';
import { PASSWORD_HINT, PASSWORD_MIN_LENGTH } from '../../core/password-policy';
import { AuthLayout } from './auth.layout';

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
  imports: [Alert, AuthLayout, Button, Control, Field, RouterLink],
  template: `
    @if (token()) {
      <kh-auth-layout title="Choose a new password">
        @if (failure(); as message) {
          <kh-alert tone="danger" #errorAlert tabindex="-1">{{ message }}</kh-alert>
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
            [hint]="passwordHint"
            [error]="resetForm.fields.password.error()"
          >
            <div class="password-wrap">
              <input
                khControl
                id="reset-password"
                [type]="passwordVisible() ? 'text' : 'password'"
                autocomplete="new-password"
                [khInvalid]="!!resetForm.fields.password.error()"
                [value]="resetForm.fields.password.value()"
                (input)="resetForm.fields.password.set($any($event.target).value)"
                (touched)="resetForm.fields.password.markTouched()"
              />
              <button
                type="button"
                class="reveal"
                [attr.aria-pressed]="passwordVisible()"
                aria-controls="reset-password"
                (click)="passwordVisible.set(!passwordVisible())"
              >
                {{ passwordVisible() ? 'Hide' : 'Show' }}
              </button>
            </div>
          </kh-field>

          <kh-field
            label="Confirm new password"
            for="reset-confirm"
            [error]="resetForm.fields.confirm.error()"
          >
            <div class="password-wrap">
              <input
                khControl
                id="reset-confirm"
                [type]="confirmVisible() ? 'text' : 'password'"
                autocomplete="new-password"
                [khInvalid]="!!resetForm.fields.confirm.error()"
                [value]="resetForm.fields.confirm.value()"
                (input)="resetForm.fields.confirm.set($any($event.target).value)"
                (touched)="resetForm.fields.confirm.markTouched()"
              />
              <button
                type="button"
                class="reveal"
                [attr.aria-pressed]="confirmVisible()"
                aria-controls="reset-confirm"
                (click)="confirmVisible.set(!confirmVisible())"
              >
                {{ confirmVisible() ? 'Hide' : 'Show' }}
              </button>
            </div>
          </kh-field>

          <button khButton variant="primary" [block]="true" type="submit" [disabled]="busy()">
            {{ busy() ? 'Saving…' : 'Save new password' }}
          </button>
        </form>
      </kh-auth-layout>
    } @else if (sent()) {
      <kh-auth-layout title="Check your email">
        <kh-alert tone="info">
          If that address is registered, we have sent a link to reset the password. It expires in an hour.
        </kh-alert>
        <p class="foot">
          <a routerLink="/auth/login" [queryParams]="{ returnUrl: returnUrl() }">Back to sign in</a>
        </p>
      </kh-auth-layout>
    } @else {
      <kh-auth-layout
        title="Reset your password"
        lead="Tell us the email address on the account and we will send a link."
      >
        @if (failure(); as message) {
          <kh-alert tone="danger" #errorAlert tabindex="-1">{{ message }}</kh-alert>
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
          Remembered it?
          <a routerLink="/auth/login" [queryParams]="{ returnUrl: returnUrl() }">Back to sign in</a>
        </p>
      </kh-auth-layout>
    }
  `,
  styles: `
    .foot {
      margin-block-start: var(--space-4);
      font-size: var(--text-sm);
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
export class ForgotPasswordPage {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly passwordVisible = signal(false);
  protected readonly confirmVisible = signal(false);
  protected readonly passwordHint = PASSWORD_HINT;

  /** Present when the customer followed the emailed link. Its absence is what shows the request form. */
  protected readonly token = signal(this.route.snapshot.queryParamMap.get('token'));

  /** Live, not a snapshot — the "Back to sign in" link must carry whatever `returnUrl` is on the
   *  URL right now, including one that arrived after this component was already created. */
  protected readonly returnUrl = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get('returnUrl') ?? undefined)),
    { initialValue: this.route.snapshot.queryParamMap.get('returnUrl') ?? undefined },
  );

  private readonly requestSubmitted = signal(false);
  private readonly resetSubmitted = signal(false);
  private readonly newPassword = formField(
    '',
    [required('Password'), minLength(PASSWORD_MIN_LENGTH, 'Password')],
    this.resetSubmitted,
  );

  protected readonly requestForm = formGroup(this.requestSubmitted, {
    email: formField('', [required('Email address'), email()], this.requestSubmitted),
  });

  protected readonly resetForm = formGroup(this.resetSubmitted, {
    // Prefilled from the link when the API put it there, and still editable — the customer knows
    // which address they used and the token is what actually authorises the change.
    email: formField(
      this.route.snapshot.queryParamMap.get('email') ?? '',
      [required('Email address'), email()],
      this.resetSubmitted,
    ),
    password: this.newPassword,
    confirm: formField(
      '',
      [required('Confirm new password'), matches(this.newPassword.value, 'The passwords')],
      this.resetSubmitted,
    ),
  });

  private readonly errorAlert = viewChild('errorAlert', { read: ElementRef<HTMLElement> });

  constructor() {
    effect(() => {
      if (this.failure()) this.errorAlert()?.nativeElement.focus();
    });
  }

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
        this.toasts.success('Your password has been changed. Sign in with the new one.');
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
