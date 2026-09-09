import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService, TwoFactorSetupResponse } from '@klarahome/data-access-auth';
import { Alert, Button, Control, Field } from '@klarahome/ui-primitives';
import { email, formField, formGroup, minLength, numeric, required } from '@klarahome/util';
import { firstValueFrom } from 'rxjs';

import { describeError, fieldErrors } from '../../core/describe-error';
import { SignInFlow } from '../../core/sign-in.flow';

/**
 * The challenge types a password can be answered with (`AuthContracts.cs`).
 *
 * Named rather than inlined because the set is open: a challenge added to the API after this
 * screen was written must fail loudly here, and that is only possible if the ones it does handle
 * are enumerated.
 */
const CHALLENGE_CODE = 'two-factor';
const CHALLENGE_ENROLMENT = 'two-factor-enrolment';
const CHALLENGE_PASSWORD_CHANGE = 'password-change-required';

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
 * **The second step is either a code or an enrolment.** An account whose role makes a second
 * factor mandatory and which has not enrolled one — the first administrator of every new
 * deployment — is answered with `two-factor-enrolment`, and the only thing that satisfies it is a
 * secret this screen stages and displays. Treating it as an ordinary code challenge asks for a
 * code from an app that was never set up: an administrator locked out of a working deployment,
 * with nothing on screen explaining why.
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
        @if (setup(); as details) {
          <p class="lead">
            This account needs an authenticator app before it can sign in. Add it below, then enter the code
            it shows.
          </p>

          <ol class="steps">
            <li>Open your authenticator app and add an account.</li>
            <li>
              Enter this key, or scan the URI below if your app can:
              <code class="secret">{{ details.secret }}</code>
              <code class="uri">{{ details.otpAuthUri }}</code>
            </li>
            <li>Type the six-digit code it shows.</li>
          </ol>
        } @else {
          <p class="lead">
            Enter the six-digit code from your authenticator app. It changes every thirty seconds.
          </p>
        }

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

    .steps {
      margin: 0 0 var(--space-4);
      padding-inline-start: var(--space-5);
      font-size: var(--text-sm);
    }

    code {
      display: block;
      margin-block: var(--space-2);
      padding: var(--space-2);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-sm);
      background: var(--color-surface);
      font-family: var(--font-mono);
      overflow-wrap: anywhere;
    }

    .secret {
      font-size: var(--text-base);
      letter-spacing: 0.08em;
    }

    .uri {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
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

  /**
   * The staged authenticator secret, set only when the second step is an enrolment rather than a
   * code. One step serves both because the field, the submit and the endpoint are identical; only
   * the instructions above them differ.
   */
  protected readonly setup = signal<TwoFactorSetupResponse | null>(null);
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

      const challenge = response.challenge;

      if (challenge) {
        this.challengeToken = challenge.challengeToken;
        this.codeField.reset();
        this.codeSubmitted.set(false);

        if (challenge.type === CHALLENGE_ENROLMENT) {
          await this.beginEnrolment(challenge.challengeToken);
          return;
        }

        if (challenge.type === CHALLENGE_CODE) {
          this.setup.set(null);
          this.step.set('code');
          return;
        }

        // A challenge this screen cannot answer. Saying so is the whole point: falling through to
        // the code box would ask for six digits that cannot satisfy it, and read as a broken login.
        this.challengeToken = null;
        this.failure.set(
          challenge.type === CHALLENGE_PASSWORD_CHANGE
            ? 'This password was issued by an administrator and has to be replaced before you can sign in. Use "Forgotten your password?" below to set your own.'
            : 'This account needs a step this screen does not support yet. Ask an administrator for help.',
        );
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
      this.setup.set(null);
      await this.flow.completeSignIn(this.returnUrl());
    } catch (error) {
      this.failure.set(
        describeError(error, 'That code was not accepted. Check the clock on your phone and try again.'),
      );
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Stages a secret for a sign-in that stopped at enrolment, and shows it.
   *
   * A failure here returns to the password step rather than leaving an unanswerable code box on
   * screen: without a staged secret there is no code the user could produce.
   */
  private async beginEnrolment(token: string): Promise<void> {
    try {
      this.setup.set(await firstValueFrom(this.auth.enrolTwoFactor(token)));
      this.step.set('code');
    } catch (error) {
      this.challengeToken = null;
      this.setup.set(null);
      this.failure.set(
        describeError(error, 'Two-factor setup could not be started. Sign in again to retry.'),
      );
    }
  }

  protected backToPassword(): void {
    this.challengeToken = null;
    // The staged secret is dropped rather than kept for a second attempt: an abandoned enrolment
    // leaving a live secret in a browser tab is a credential nobody is looking after.
    this.setup.set(null);
    this.failure.set(null);
    this.form.fields.password.reset();
    this.submitted.set(false);
    this.step.set('password');
  }
}
