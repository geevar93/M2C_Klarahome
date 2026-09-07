import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '@klarahome/data-access-auth';
import { Alert, Button, Control, Field } from '@klarahome/ui-primitives';
import { email, formField, formGroup, matches, minLength, required } from '@klarahome/util';
import { firstValueFrom } from 'rxjs';

import { describeError } from '../../core/describe-error';

/**
 * Both halves of a password reset, on one route.
 *
 * The request and the emailed link that comes back are one screen because they are one flow with
 * two entry points, and the `token` in the query string is what says which. Two routes would mean
 * two screens that had to agree about the same five rules, and a link landing on the wrong one.
 *
 * **The confirmation is deliberately non-committal**: "if that address has an account, we have
 * sent a link". The API answers 204 whether or not the address is registered, because an endpoint
 * that distinguished the two would tell anybody who asked which of their guesses are staff email
 * addresses. Saying "we have sent you an email" would give that away in the interface instead.
 */
@Component({
  selector: 'kh-forgot-password-page',
  imports: [Alert, Button, Control, Field, RouterLink],
  template: `
    <main class="pane">
      <h1>{{ hasToken() ? 'Choose a new password' : 'Reset your password' }}</h1>

      @if (done()) {
        <kh-alert tone="success" heading="Done">
          @if (hasToken()) {
            Your password has been changed. <a routerLink="/login">Sign in</a> with it.
          } @else {
            If that address has an account, a reset link is on its way. The link is valid for a short time.
          }
        </kh-alert>
      } @else if (hasToken()) {
        <form (submit)="submitReset($event)" novalidate>
          @if (failure(); as message) {
            <kh-alert tone="danger">{{ message }}</kh-alert>
          }

          <kh-field label="Email" for="reset-email" [error]="reset.fields.email.error()">
            <input
              khControl
              id="reset-email"
              type="email"
              autocomplete="username"
              autocapitalize="none"
              [khInvalid]="!!reset.fields.email.error()"
              [value]="reset.fields.email.value()"
              (input)="reset.fields.email.set($any($event.target).value)"
              (touched)="reset.fields.email.markTouched()"
            />
          </kh-field>

          <kh-field
            label="New password"
            for="reset-password"
            hint="At least 12 characters. A phrase you can remember beats a short scramble."
            [error]="reset.fields.password.error()"
          >
            <input
              khControl
              id="reset-password"
              type="password"
              autocomplete="new-password"
              [khInvalid]="!!reset.fields.password.error()"
              [value]="reset.fields.password.value()"
              (input)="reset.fields.password.set($any($event.target).value)"
              (touched)="reset.fields.password.markTouched()"
            />
          </kh-field>

          <kh-field label="Confirm new password" for="reset-confirm" [error]="reset.fields.confirm.error()">
            <input
              khControl
              id="reset-confirm"
              type="password"
              autocomplete="new-password"
              [khInvalid]="!!reset.fields.confirm.error()"
              [value]="reset.fields.confirm.value()"
              (input)="reset.fields.confirm.set($any($event.target).value)"
              (touched)="reset.fields.confirm.markTouched()"
            />
          </kh-field>

          <button khButton type="submit" variant="primary" [block]="true" [disabled]="busy()">
            {{ busy() ? 'Saving…' : 'Set new password' }}
          </button>
        </form>
      } @else {
        <p class="lead">We will email you a link to set a new one.</p>

        <form (submit)="submitRequest($event)" novalidate>
          @if (failure(); as message) {
            <kh-alert tone="danger">{{ message }}</kh-alert>
          }

          <kh-field label="Email" for="forgot-email" [error]="request.fields.email.error()">
            <input
              khControl
              id="forgot-email"
              type="email"
              autocomplete="username"
              autocapitalize="none"
              [khInvalid]="!!request.fields.email.error()"
              [value]="request.fields.email.value()"
              (input)="request.fields.email.set($any($event.target).value)"
              (touched)="request.fields.email.markTouched()"
            />
          </kh-field>

          <button khButton type="submit" variant="primary" [block]="true" [disabled]="busy()">
            {{ busy() ? 'Sending…' : 'Send reset link' }}
          </button>
        </form>
      }

      <p class="foot"><a routerLink="/login">Back to sign in</a></p>
    </main>
  `,
  styles: `
    :host {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      padding: var(--space-4);
      background: var(--color-surface);
    }

    .pane {
      width: 100%;
      max-width: 24rem;
      padding: var(--space-6);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-bg);
    }

    h1 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-xl);
    }

    .lead {
      margin: 0 0 var(--space-5);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    kh-alert {
      margin-block-end: var(--space-4);
    }

    .foot {
      margin: var(--space-4) 0 0;
      font-size: var(--text-sm);
      text-align: center;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForgotPasswordPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  private readonly token = computed(
    () => this.router.parseUrl(this.router.url).queryParams['token'] as string | undefined,
  );

  protected readonly hasToken = computed(() => !!this.token());
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly failure = signal<string | null>(null);

  private readonly requestSubmitted = signal(false);
  protected readonly request = formGroup(this.requestSubmitted, {
    email: formField('', [required('Email'), email()], this.requestSubmitted),
  });

  private readonly resetSubmitted = signal(false);
  private readonly newPassword = formField(
    '',
    [required('Password'), minLength(12, 'Password')],
    this.resetSubmitted,
  );
  protected readonly reset = formGroup(this.resetSubmitted, {
    email: formField('', [required('Email'), email()], this.resetSubmitted),
    password: this.newPassword,
    confirm: formField(
      '',
      [required('Confirmation'), matches(this.newPassword.value, 'The passwords')],
      this.resetSubmitted,
    ),
  });

  protected async submitRequest(event: Event): Promise<void> {
    event.preventDefault();
    if (!this.request.submit() || this.busy()) return;

    this.busy.set(true);
    this.failure.set(null);

    try {
      await firstValueFrom(this.auth.forgotPassword(this.request.values().email));
      this.done.set(true);
    } catch (error) {
      // A transport failure is worth showing; a refusal is not, because the endpoint does not
      // refuse — it answers 204 for an address it has never seen.
      this.failure.set(describeError(error, 'That request could not be sent. Try again.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async submitReset(event: Event): Promise<void> {
    event.preventDefault();
    if (!this.reset.submit() || this.busy()) return;

    this.busy.set(true);
    this.failure.set(null);
    const values = this.reset.values();

    try {
      await firstValueFrom(this.auth.resetPassword(values.email, this.token() ?? '', values.password));
      this.done.set(true);
    } catch (error) {
      this.failure.set(
        describeError(error, 'That link is no longer valid. Ask for a new one and try again.'),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
