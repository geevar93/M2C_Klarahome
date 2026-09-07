import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@klarahome/data-access-auth';
import { Alert, Button, Control, Field } from '@klarahome/ui-primitives';
import { formField, formGroup, numeric, required } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { SignInFlow } from '../../core/sign-in.flow';

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
 * an attacker nothing.
 *
 * `autocomplete="one-time-code"` stays, and still earns its place: on both iOS and Android it makes
 * the keyboard offer the code straight from the authenticator app, turning six taps into one.
 */
@Component({
  selector: 'kh-otp-page',
  imports: [Alert, Button, Control, Field],
  template: `
    <div class="panel">
      <h1>Two-step verification</h1>
      <p class="lead">Enter the {{ codeLength }}-digit code from your authenticator app.</p>

      @if (failure(); as message) {
        <kh-alert tone="danger">{{ message }}</kh-alert>
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

  private readonly params = this.route.snapshot.queryParamMap;

  protected readonly challenge = signal(this.params.get('challenge'));
  protected readonly returnUrl = computed(() => this.params.get('returnUrl') ?? undefined);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    code: formField('', [required('Code'), numeric(CODE_LENGTH)], this.submitted),
  });

  constructor() {
    // No challenge means the screen was reached directly. There is nothing to verify, so it sends
    // the customer to the start rather than showing a box that cannot work.
    if (!this.challenge()) {
      void this.router.navigate(['/auth/login'], { queryParams: { returnUrl: this.returnUrl() } });
    }
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
        if (!this.flow.complete(response, this.returnUrl() ?? null)) {
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
