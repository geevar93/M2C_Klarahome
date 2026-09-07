import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@klarahome/data-access-auth';
import { Alert, Button, Control, Field } from '@klarahome/ui-primitives';
import { email, formField, formGroup, mobile, normaliseMobile, required } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { SignInFlow } from '../../core/sign-in.flow';

/**
 * Sign in — `/auth/login`.
 *
 * **A mobile number first, a password second**, and that order is the whole design. Most customers
 * of an Indian storefront have a mobile number and no password; the API creates the account on the
 * first successful code (Step 7), so for them there is no sign-up at all — there is only this
 * screen. The password form is there for the customers who set one, behind a link rather than
 * beside it, because two forms side by side is a decision nobody asked to make.
 *
 * The `returnUrl` is carried through untouched and handed to `SignInFlow`, which is what validates
 * it. A shopper sent here from a checkout comes back to the checkout with their basket merged.
 *
 * **The screen never says whether a number is registered.** Requesting a code succeeds either way,
 * because an endpoint that answered differently would tell anyone who asked which numbers hold
 * accounts (docs/07-security-compliance.md).
 */
@Component({
  selector: 'kh-login-page',
  imports: [Alert, Button, Control, Field, RouterLink],
  template: `
    <div class="panel">
      <h1>Sign in</h1>

      @if (failure(); as message) {
        <kh-alert tone="danger">{{ message }}</kh-alert>
      }

      @if (mode() === 'otp') {
        <p class="lead">We will text you a code. No password needed.</p>

        <form (submit)="requestCode($event)" novalidate>
          <kh-field
            label="Mobile number"
            for="login-mobile"
            hint="The 10-digit number on your account."
            [error]="otpForm.fields.mobile.error()"
          >
            <input
              khControl
              khNumeric
              id="login-mobile"
              type="tel"
              inputmode="numeric"
              maxlength="13"
              autocomplete="tel-national"
              autofocus
              [khInvalid]="!!otpForm.fields.mobile.error()"
              [value]="otpForm.fields.mobile.value()"
              (input)="otpForm.fields.mobile.set($any($event.target).value)"
              (touched)="otpForm.fields.mobile.markTouched()"
            />
          </kh-field>

          <button khButton variant="primary" [block]="true" type="submit" [disabled]="busy()">
            {{ busy() ? 'Sending…' : 'Send me a code' }}
          </button>
        </form>

        <button khButton variant="tertiary" type="button" (click)="mode.set('password')">
          Sign in with a password instead
        </button>
      } @else {
        <form (submit)="signIn($event)" novalidate>
          <kh-field label="Email address" for="login-email" [error]="passwordForm.fields.email.error()">
            <input
              khControl
              id="login-email"
              type="email"
              autocomplete="email"
              [khInvalid]="!!passwordForm.fields.email.error()"
              [value]="passwordForm.fields.email.value()"
              (input)="passwordForm.fields.email.set($any($event.target).value)"
              (touched)="passwordForm.fields.email.markTouched()"
            />
          </kh-field>

          <kh-field label="Password" for="login-password" [error]="passwordForm.fields.password.error()">
            <input
              khControl
              id="login-password"
              type="password"
              autocomplete="current-password"
              [khInvalid]="!!passwordForm.fields.password.error()"
              [value]="passwordForm.fields.password.value()"
              (input)="passwordForm.fields.password.set($any($event.target).value)"
              (touched)="passwordForm.fields.password.markTouched()"
            />
          </kh-field>

          <button khButton variant="primary" [block]="true" type="submit" [disabled]="busy()">
            {{ busy() ? 'Signing in…' : 'Sign in' }}
          </button>
        </form>

        <div class="links">
          <a routerLink="/auth/forgot-password" [queryParams]="{ returnUrl: returnUrl() }"
            >Forgot your password?</a
          >
          <button khButton variant="tertiary" type="button" (click)="mode.set('otp')">
            Use a code instead
          </button>
        </div>
      }

      <p class="foot">
        New here?
        <a routerLink="/auth/register" [queryParams]="{ returnUrl: returnUrl() }">Create an account</a>
      </p>
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

    .links {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
      margin-block-start: var(--space-3);
      font-size: var(--text-sm);
    }

    .foot {
      margin-block-start: var(--space-6);
      padding-block-start: var(--space-4);
      border-block-start: 1px solid var(--color-border);
      font-size: var(--text-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly flow = inject(SignInFlow);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly mode = signal<'otp' | 'password'>('otp');
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected readonly returnUrl = computed(
    () => this.route.snapshot.queryParamMap.get('returnUrl') ?? undefined,
  );

  private readonly otpSubmitted = signal(false);
  private readonly passwordSubmitted = signal(false);

  protected readonly otpForm = formGroup(this.otpSubmitted, {
    mobile: formField('', [required('Mobile number'), mobile], this.otpSubmitted),
  });

  protected readonly passwordForm = formGroup(this.passwordSubmitted, {
    email: formField('', [required('Email address'), email()], this.passwordSubmitted),
    password: formField('', [required('Password')], this.passwordSubmitted),
  });

  protected requestCode(event: Event): void {
    event.preventDefault();
    if (!this.otpForm.submit() || this.busy()) return;

    const number = normaliseMobile(this.otpForm.values().mobile);
    this.busy.set(true);
    this.failure.set(null);

    this.auth.requestOtp(number).subscribe({
      next: (response) => {
        this.busy.set(false);
        // The number and what the API said about the code travel in the URL, so the code screen is
        // reachable on its own and a refresh there does not lose which number is being verified.
        void this.router.navigate(['/auth/otp'], {
          queryParams: {
            mobile: number,
            length: response.codeLength,
            expires: response.expiresInSeconds,
            returnUrl: this.returnUrl(),
          },
        });
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.failure.set(
          describeError(error, 'We could not send a code just now. Try again, or sign in with a password.'),
        );
      },
    });
  }

  protected signIn(event: Event): void {
    event.preventDefault();
    if (!this.passwordForm.submit() || this.busy()) return;

    const { email: address, password } = this.passwordForm.values();
    this.busy.set(true);
    this.failure.set(null);

    this.auth.signIn(address, password).subscribe({
      next: (response) => {
        this.busy.set(false);
        if (this.flow.complete(response, this.returnUrl() ?? null)) return;

        // A challenge instead of a session: one factor proved, the second still to go.
        void this.router.navigate(['/auth/otp'], {
          queryParams: { challenge: response.challenge?.challengeToken, returnUrl: this.returnUrl() },
        });
      },
      error: (error: unknown) => {
        this.busy.set(false);
        // Deliberately the same message whichever half was wrong. Saying "no account with that
        // email" is an account-enumeration oracle.
        this.failure.set(describeError(error, 'That email address and password do not match an account.'));
      },
    });
  }
}
