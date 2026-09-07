import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '@klarahome/data-access-auth';
import { Alert, Button, Control, Field } from '@klarahome/ui-primitives';
import { email, formField, formGroup, minLength, numeric, required } from '@klarahome/util';
import { firstValueFrom } from 'rxjs';

import { describeError, fieldErrors } from '../../core/describe-error';
import { SignInFlow } from '../../core/sign-in.flow';

/**
 * The way into the back office.
 *
 * **One route with two steps, not two routes.** The second factor is only reachable while holding
 * a `challengeToken` the password earned, and that token must never be in a URL — a URL is
 * bookmarked, pasted into chat and written to server logs. Keeping the step in a signal means the
 * code screen simply cannot be reached without the credential that makes it meaningful, and a
 * reload sends the user back to the password step, which is correct: the challenge is short-lived
 * and re-entering a password is not the hard part.
 *
 * There is **no OTP sign-in and no self-registration** here, which is the substantive difference
 * from the storefront's login. A back-office account is created by an administrator
 * (`POST /admin/users`); an account that could be created by whoever holds a phone number would be
 * an account that grants itself access to other people's orders.
 *
 * Sign-in failures are shown as one message and never say which half was wrong. "That email is not
 * registered" is an account-enumeration oracle, and the API answers accordingly
 * (`docs/07-security-compliance.md`); this screen does not undo that by being helpful.
 */
@Component({
  selector: 'kh-login-page',
  imports: [Alert, Button, Control, Field, RouterLink],
  template: `
    <main class="pane">
      <h1>Klara Home back office</h1>

      @if (step() === 'password') {
        <p class="lead">Sign in with the account your administrator created for you.</p>

        <form (submit)="submitPassword($event)" novalidate>
          @if (failure(); as message) {
            <kh-alert tone="danger">{{ message }}</kh-alert>
          }

          <kh-field label="Email" for="login-email" [error]="form.fields.email.error()">
            <input
              khControl
              id="login-email"
              type="email"
              autocomplete="username"
              autocapitalize="none"
              spellcheck="false"
              [khInvalid]="!!form.fields.email.error()"
              [value]="form.fields.email.value()"
              (input)="form.fields.email.set($any($event.target).value)"
              (touched)="form.fields.email.markTouched()"
            />
          </kh-field>

          <kh-field label="Password" for="login-password" [error]="form.fields.password.error()">
            <input
              khControl
              id="login-password"
              type="password"
              autocomplete="current-password"
              [khInvalid]="!!form.fields.password.error()"
              [value]="form.fields.password.value()"
              (input)="form.fields.password.set($any($event.target).value)"
              (touched)="form.fields.password.markTouched()"
            />
          </kh-field>

          <button khButton type="submit" variant="primary" [block]="true" [disabled]="busy()">
            {{ busy() ? 'Signing in…' : 'Sign in' }}
          </button>
        </form>

        <p class="foot">
          <a routerLink="/forgot-password">Forgotten your password?</a>
        </p>
      } @else {
        <p class="lead">
          Enter the six-digit code from your authenticator app. It changes every thirty seconds.
        </p>

        <form (submit)="submitCode($event)" novalidate>
          @if (failure(); as message) {
            <kh-alert tone="danger">{{ message }}</kh-alert>
          }

          <kh-field
            label="Authentication code"
            for="login-code"
            hint="Six digits, no spaces."
            [error]="codeField.error()"
          >
            <input
              khControl
              khNumeric
              id="login-code"
              type="text"
              inputmode="numeric"
              maxlength="6"
              autocomplete="one-time-code"
              [khInvalid]="!!codeField.error()"
              [value]="codeField.value()"
              (input)="codeField.set($any($event.target).value)"
              (touched)="codeField.markTouched()"
            />
          </kh-field>

          <button khButton type="submit" variant="primary" [block]="true" [disabled]="busy()">
            {{ busy() ? 'Checking…' : 'Verify' }}
          </button>
        </form>

        <p class="foot">
          <button type="button" class="linkish" (click)="backToPassword()">Start again</button>
        </p>
      }
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

    .linkish {
      padding: 0;
      border: 0;
      background: none;
      font: inherit;
      color: var(--color-primary);
      text-decoration: underline;
      cursor: pointer;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly flow = inject(SignInFlow);

  private readonly submitted = signal(false);
  protected readonly form = formGroup(this.submitted, {
    email: formField('', [required('Email'), email()], this.submitted),
    password: formField('', [required('Password'), minLength(8, 'Password')], this.submitted),
  });

  private readonly codeSubmitted = signal(false);
  protected readonly codeField = formField('', [required('Code'), numeric(6)], this.codeSubmitted);

  protected readonly step = signal<'password' | 'code'>('password');
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);

  /** Held in memory for exactly as long as the code step is on screen. Never in a URL. */
  private challengeToken: string | null = null;

  /** Read off the URL rather than injected as an input, so a guard's redirect is honoured. */
  protected readonly returnUrl = computed(() =>
    new URLSearchParams(globalThis.location?.search ?? '').get('returnUrl'),
  );

  protected async submitPassword(event: Event): Promise<void> {
    event.preventDefault();
    if (!this.form.submit() || this.busy()) return;

    this.busy.set(true);
    this.failure.set(null);
    const { email: address, password } = this.form.values();

    try {
      const response = await firstValueFrom(this.auth.signIn(address, password));

      if (response.challenge) {
        this.challengeToken = response.challenge.challengeToken;
        this.codeField.reset();
        this.codeSubmitted.set(false);
        this.step.set('code');
        return;
      }

      await this.flow.completeSignIn(this.returnUrl());
    } catch (error) {
      // Field errors are applied where the API named a field — an expired temporary password
      // names one — and the rest becomes the single message above the form.
      const unmatched = this.form.applyServerErrors(fieldErrors(error));
      this.failure.set(unmatched[0] ?? describeError(error, 'That email and password were not accepted.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async submitCode(event: Event): Promise<void> {
    event.preventDefault();
    this.codeSubmitted.set(true);
    this.codeField.markTouched();
    if (this.codeField.problem() !== null || this.busy()) return;

    const token = this.challengeToken;
    if (!token) {
      // The challenge is gone — a reload, or the component was re-created. Back to the password,
      // which is the only way to earn another one.
      this.backToPassword();
      return;
    }

    this.busy.set(true);
    this.failure.set(null);

    try {
      await firstValueFrom(this.auth.verifyTwoFactor(token, this.codeField.value().trim()));
      await this.flow.completeSignIn(this.returnUrl());
    } catch (error) {
      this.failure.set(
        describeError(error, 'That code was not accepted. Check the clock on your phone and try again.'),
      );
    } finally {
      this.busy.set(false);
    }
  }

  protected backToPassword(): void {
    this.challengeToken = null;
    this.failure.set(null);
    this.form.fields.password.reset();
    this.submitted.set(false);
    this.step.set('password');
  }
}
