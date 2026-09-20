import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@klarahome/data-access-auth';
import { Alert, Button, Control, Field } from '@klarahome/ui-primitives';
import { formField, formGroup, numeric, required } from '@klarahome/util';
import { map } from 'rxjs';

import { describeError } from '../../core/describe-error';
import { SignInFlow } from '../../core/sign-in.flow';
import { AuthLayout } from './auth.layout';

/** How many digits an authenticator app produces. TOTP is six, and it is not deployment-specific. */
const CODE_LENGTH = 6;

/**
 * The second-factor screen — `/auth/otp`.
 *
 * **It is only ever a second factor now.** This screen used to serve two flows: the one-time code
 * texted to a mobile number, and the second factor of a password sign-in. Mobile-OTP sign-in has
 * been withdrawn (see `LoginPage`), so the `mobile` query parameter, the resend countdown and the
 * SMS branch have gone with it — they were unreachable, and unreachable code on an authentication
 * screen is the kind that rots without anybody noticing.
 *
 * What is left is the TOTP challenge. A password sign-in that answers with a `challenge` instead of
 * a session sends the caller here with that token in the query string, because it has to survive a
 * refresh and it is not a secret — the code is what proves anything, and the challenge alone buys
 * an attacker nothing. Reached directly — a bookmark, a stale tab, a refresh after the challenge has
 * already been spent — it sends the shopper back to sign in with `reason=expired`, which `LoginPage`
 * could show as a banner of its own; today it is shown here, immediately before the redirect, as the
 * one plain sentence a screen this short has room for.
 *
 * `autocomplete="one-time-code"` stays, and still earns its place: on both iOS and Android it makes
 * the keyboard offer the code straight from the authenticator app, turning six taps into one.
 */
@Component({
  selector: 'kh-otp-page',
  imports: [Alert, AuthLayout, Button, Control, Field, RouterLink],
  template: `
    <kh-auth-layout title="Two-step verification" [lead]="'Enter the ' + codeLength + '-digit code from your authenticator app.'">
      @if (failure(); as message) {
        <kh-alert tone="danger" #errorAlert tabindex="-1">{{ message }}</kh-alert>
      }

      <form (submit)="verify($event)" novalidate>
        <kh-field label="Code" for="otp-code" [error]="form.fields.code.error()">
          <input
            khControl
            khNumeric
            id="otp-code"
            type="text"
            inputmode="numeric"
            autocomplete="one-time-code"
            [attr.maxlength]="codeLength"
            autofocus
            [khInvalid]="!!form.fields.code.error()"
            [value]="form.fields.code.value()"
            (input)="onCode($any($event.target).value)"
            (touched)="form.fields.code.markTouched()"
          />
        </kh-field>

        <button khButton variant="primary" [block]="true" type="submit" [disabled]="busy()">
          {{ busy() ? 'Checking…' : 'Verify and continue' }}
        </button>
      </form>

      <p class="foot">
        <a routerLink="/auth/login" [queryParams]="{ returnUrl: returnUrl() }">Back to sign in</a>
      </p>
      <p class="support">
        Lost access to your authenticator?
        <a routerLink="/pages/contact">Contact support</a>.
      </p>
    </kh-auth-layout>
  `,
  styles: `
    .foot {
      margin-block-start: var(--space-6);
      font-size: var(--text-sm);
    }

    .support {
      margin-block-start: var(--space-2);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OtpPage {
  private readonly auth = inject(AuthService);
  private readonly flow = inject(SignInFlow);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly codeLength = CODE_LENGTH;
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected readonly challenge = signal(this.route.snapshot.queryParamMap.get('challenge'));

  /** Live, not a snapshot: `returnUrl` can change under this component without a full reload — a
   *  "Back to sign in" round trip that lands here again with a fresh challenge, for instance. */
  protected readonly returnUrl = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get('returnUrl') ?? undefined)),
    { initialValue: this.route.snapshot.queryParamMap.get('returnUrl') ?? undefined },
  );

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    code: formField('', [required('Code'), numeric(CODE_LENGTH)], this.submitted),
  });

  private readonly errorAlert = viewChild('errorAlert', { read: ElementRef<HTMLElement> });

  constructor() {
    // No challenge means the screen was reached directly — a bookmark, a stale tab, a refresh
    // after the token was already spent. There is nothing to verify, so it sends the customer back
    // to sign in with a reason the login screen can show, rather than a code box that cannot work.
    if (!this.challenge()) {
      void this.router.navigate(['/auth/login'], {
        queryParams: { returnUrl: this.returnUrl(), reason: 'expired' },
      });
      return;
    }

    effect(() => {
      if (this.failure()) this.errorAlert()?.nativeElement.focus();
    });
  }

  protected onCode(raw: string): void {
    const digits = raw.replace(/\D/g, '').slice(0, CODE_LENGTH);
    this.form.fields.code.set(digits);
    // Submitted on the last digit. A code the platform's own autofill just pasted should not then
    // need a button press, and the customer can still press one if autofill got it wrong.
    if (digits.length === CODE_LENGTH && !this.busy()) this.submit(digits);
  }

  protected verify(event: Event): void {
    event.preventDefault();
    if (!this.form.submit() || this.busy()) return;
    this.submit(this.form.values().code);
  }

  private submit(code: string): void {
    this.busy.set(true);
    this.failure.set(null);

    this.auth.verifyTwoFactor(this.challenge() ?? '', code).subscribe({
      next: (response) => {
        this.busy.set(false);
        if (!this.flow.complete(response, this.returnUrl() ?? null, 'password')) {
          this.failure.set('That code was accepted but the sign-in did not complete. Please try again.');
        }
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.form.reset();
        this.failure.set(
          describeError(error, 'That code is not right, or it has expired. Try the next one your app shows.'),
        );
      },
    });
  }
}
