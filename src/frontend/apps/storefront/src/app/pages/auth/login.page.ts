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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@klarahome/data-access-auth';
import { Alert, Button, Control, Field } from '@klarahome/ui-primitives';
import { email, formField, formGroup, required } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { SignInFlow } from '../../core/sign-in.flow';
import { AuthLayout } from './auth.layout';
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
 * their authenticator app — and only when the response actually carries one; a session that failed
 * to establish for some other reason is a failure, not a half-finished challenge, and stays on this
 * screen with the same anti-enumeration message every other failure gets.
 *
 * The `returnUrl` is carried through untouched and handed to `SignInFlow`, which is what validates
 * it. A shopper sent here from a checkout comes back to the checkout with their cart merged.
 */
@Component({
  selector: 'kh-login-page',
  imports: [Alert, AuthLayout, Button, Control, Field, RouterLink, SocialSignIn],
  template: `
    <kh-auth-layout title="Sign in" lead="Welcome back. Sign in to see your orders, addresses and wishlist.">
      @if (sessionExpired()) {
        <kh-alert tone="info">Your sign-in session expired. Please sign in again.</kh-alert>
      }

      @if (failure(); as message) {
        <kh-alert tone="danger" #errorAlert tabindex="-1">{{ message }}</kh-alert>
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
          <div class="password-wrap">
            <input
              khControl
              id="login-password"
              [type]="passwordVisible() ? 'text' : 'password'"
              autocomplete="current-password"
              [khInvalid]="!!form.fields.password.error()"
              [value]="form.fields.password.value()"
              (input)="form.fields.password.set($any($event.target).value)"
              (touched)="form.fields.password.markTouched()"
            />
            <button
              type="button"
              class="reveal"
              [attr.aria-pressed]="passwordVisible()"
              aria-controls="login-password"
              (click)="passwordVisible.set(!passwordVisible())"
            >
              {{ passwordVisible() ? 'Hide' : 'Show' }}
            </button>
          </div>
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
    </kh-auth-layout>
  `,
  styles: `
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
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly flow = inject(SignInFlow);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly passwordVisible = signal(false);

  protected readonly returnUrl = computed(
    () => this.route.snapshot.queryParamMap.get('returnUrl') ?? undefined,
  );
  /** Set by `OtpPage` when it is reached without a live challenge — see its own doc comment. */
  protected readonly sessionExpired = computed(
    () => this.route.snapshot.queryParamMap.get('reason') === 'expired',
  );

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    email: formField('', [required('Email address'), email()], this.submitted),
    password: formField('', [required('Password')], this.submitted),
  });

  private readonly errorAlert = viewChild('errorAlert', { read: ElementRef<HTMLElement> });

  constructor() {
    // Moves focus to the failure so a screen reader announces it — a message that only appears
    // visually is a message half the people who need it never learn happened.
    effect(() => {
      if (this.failure()) this.errorAlert()?.nativeElement.focus();
    });
  }

  protected signIn(event: Event): void {
    event.preventDefault();
    if (!this.form.submit() || this.busy()) return;

    const { email: address, password } = this.form.values();
    this.busy.set(true);
    this.failure.set(null);

    this.auth.signIn(address, password).subscribe({
      next: (response) => {
        this.busy.set(false);
        if (this.flow.complete(response, this.returnUrl() ?? null, 'password')) return;

        // A challenge instead of a session: one factor proved, the second still to go. Only
        // routed there when the response actually carries a token to challenge against — a
        // session that failed to establish for any other reason is a plain failure, not a
        // half-finished second factor, and gets the same anti-enumeration message below.
        const challengeToken = response.challenge?.challengeToken;
        if (challengeToken) {
          void this.router.navigate(['/auth/otp'], {
            queryParams: { challenge: challengeToken, returnUrl: this.returnUrl() },
          });
          return;
        }

        this.failure.set('That email address and password do not match an account.');
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
