import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@klarahome/data-access-auth';
import { Alert, Button, Control, Field } from '@klarahome/ui-primitives';
import { email, formField, formGroup, required } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { SignInFlow } from '../../core/sign-in.flow';
import { SocialSignIn } from './social-sign-in';

/**
 * Sign in — `/auth/login`.
 *
 * **The email address is the account.** That is a change from the original Step 7 design, which put
 * a mobile number and a one-time code first and the password behind a link. Mobile-OTP sign-in has
 * been withdrawn: it needs a DLT-registered SMS route this deployment does not have, and the
 * fallback that made it usable in development wrote one-time codes to the API log, which
 * `docs/07-security-compliance.md` §3 forbids outright in a deployed environment. The endpoints
 * behind it are switched off by the `identity.mobile-otp-login` flag rather than deleted, so the
 * day an SMS provider is paid for the capability returns without a deploy (ADR-014 decision 4).
 *
 * What replaces it is the two routes above and below the divider: an identity provider, which
 * verifies the address on our behalf and asks the shopper for no new secret, and an email and a
 * password. Which providers appear is the server's decision, and on an unconfigured deployment it
 * is none of them — see {@link SocialSignIn}.
 *
 * `/auth/otp` is still reachable from here, but only for a **second factor**: a password sign-in
 * that answers with a challenge instead of a session sends the shopper there to enter the code from
 * their authenticator app. It is no longer a way to sign in on its own.
 *
 * The `returnUrl` is carried through untouched and handed to `SignInFlow`, which is what validates
 * it. A shopper sent here from a checkout comes back to the checkout with their basket merged.
 */
@Component({
  selector: 'kh-login-page',
  imports: [Alert, Button, Control, Field, RouterLink, SocialSignIn],
  template: `
    <div class="panel">
      <h1>Sign in</h1>
      <p class="lead">Welcome back. Sign in to see your orders, addresses and wishlist.</p>

      @if (failure(); as message) {
        <kh-alert tone="danger">{{ message }}</kh-alert>
      }

      <kh-social-sign-in [returnUrl]="returnUrl()" />

      <form (submit)="signIn($event)" novalidate>
        <kh-field label="Email address" for="login-email" [error]="form.fields.email.error()">
          <input
            khControl
            id="login-email"
            type="email"
            autocomplete="email"
            autofocus
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

        <button khButton variant="primary" [block]="true" type="submit" [disabled]="busy()">
          {{ busy() ? 'Signing in…' : 'Sign in' }}
        </button>
      </form>

      <p class="links">
        <a routerLink="/auth/forgot-password" [queryParams]="{ returnUrl: returnUrl() }"
          >Forgot your password?</a
        >
      </p>

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
      font-size: var(--text-display-sm);
    }

    .lead {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
      margin-block-end: var(--space-6);
    }

    form {
      display: grid;
      gap: var(--space-4);
    }

    .links {
      margin-block: var(--space-4) 0;
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

  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected readonly returnUrl = computed(
    () => this.route.snapshot.queryParamMap.get('returnUrl') ?? undefined,
  );

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    email: formField('', [required('Email address'), email()], this.submitted),
    password: formField('', [required('Password')], this.submitted),
  });

  protected signIn(event: Event): void {
    event.preventDefault();
    if (!this.form.submit() || this.busy()) return;

    const { email: address, password } = this.form.values();
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
