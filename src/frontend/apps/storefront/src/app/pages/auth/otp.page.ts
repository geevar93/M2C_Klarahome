import { ChangeDetectionStrategy, Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@klarahome/data-access-auth';
import { Alert, Button, Control, Field } from '@klarahome/ui-primitives';
import { formField, formGroup, numeric, required } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { SignInFlow } from '../../core/sign-in.flow';

/** How long before the code can be asked for again. Long enough that a slow SMS is not re-sent. */
const RESEND_SECONDS = 30;

/**
 * The code screen — `/auth/otp`.
 *
 * It serves two flows that look identical to the customer and are not the same to the server: the
 * one-time code sent to a mobile number, and the second factor of a password sign-in. Which one is
 * running is decided by the query string — a `mobile` or a `challenge` — because both have to
 * survive a refresh, and neither is a secret: the code itself is what proves anything.
 *
 * **How long the code is comes from the API**, not from a constant here. A deployment that shortens
 * it must not need a front-end release, so the box's `maxlength`, its validation and its hint all
 * read `length` off the request that sent the code.
 *
 * The resend countdown exists to stop the second tap that happens when an SMS takes eight seconds.
 * It is a courtesy on top of the API's own rate limit, not a replacement for it.
 *
 * `autocomplete="one-time-code"` is the single most valuable attribute on this screen: it is what
 * makes iOS and Android offer the code from the notification, turning six taps into one.
 */
@Component({
  selector: 'kh-otp-page',
  imports: [Alert, Button, Control, Field, RouterLink],
  template: `
    <div class="panel">
      <h1>Enter your code</h1>

      @if (mobile()) {
        <p class="lead">
          We sent a {{ codeLength() }}-digit code to {{ maskedMobile() }}.
          <a routerLink="/auth/login" [queryParams]="{ returnUrl: returnUrl() }">Use a different number</a>
        </p>
      } @else {
        <p class="lead">Enter the code from your authenticator app.</p>
      }

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
            [attr.maxlength]="codeLength()"
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

      @if (mobile()) {
        <div class="resend">
          @if (secondsLeft() > 0) {
            <span>You can ask for another code in {{ secondsLeft() }}s.</span>
          } @else {
            <button khButton variant="tertiary" type="button" [disabled]="busy()" (click)="resend()">
              Send another code
            </button>
          }
        </div>
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

    .resend {
      margin-block-start: var(--space-3);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OtpPage implements OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly flow = inject(SignInFlow);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly secondsLeft = signal(RESEND_SECONDS);

  private readonly params = this.route.snapshot.queryParamMap;
  private timer: ReturnType<typeof setInterval> | null = null;

  protected readonly mobile = signal(this.params.get('mobile'));
  protected readonly challenge = signal(this.params.get('challenge'));
  protected readonly returnUrl = computed(() => this.params.get('returnUrl') ?? undefined);

  /** What the API said it sent; six when it did not say, which is every deployment's default. */
  protected readonly codeLength = signal(Number.parseInt(this.params.get('length') ?? '', 10) || 6);

  protected readonly maskedMobile = computed(() => {
    const number = this.mobile();
    // The last two digits only. Enough to confirm the right number was typed, not enough to be a
    // phone number on a screen somebody else can see.
    return number ? `•••••• ${number.slice(-2)}` : '';
  });

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    code: formField('', [required('Code'), numeric(this.codeLength())], this.submitted),
  });

  constructor() {
    // Neither a number nor a challenge means the screen was reached directly. There is nothing to
    // verify, so it sends the customer to the start rather than showing a box that cannot work.
    if (!this.mobile() && !this.challenge()) {
      void this.router.navigate(['/auth/login'], { queryParams: { returnUrl: this.returnUrl() } });
      return;
    }

    if (this.mobile()) this.startCountdown();
  }

  ngOnDestroy(): void {
    this.stopCountdown();
  }

  protected onCode(raw: string): void {
    const digits = raw.replace(/\D/g, '').slice(0, this.codeLength());
    this.form.fields.code.set(digits);
    // Submitted on the last digit. A code the platform's own SMS autofill just pasted should not
    // then need a button press, and the customer can still press one if autofill got it wrong.
    if (digits.length === this.codeLength() && !this.busy()) this.submit(digits);
  }

  protected verify(event: Event): void {
    event.preventDefault();
    if (!this.form.submit() || this.busy()) return;
    this.submit(this.form.values().code);
  }

  protected resend(): void {
    const number = this.mobile();
    if (!number || this.busy()) return;

    this.busy.set(true);
    this.failure.set(null);

    this.auth.requestOtp(number).subscribe({
      next: (response) => {
        this.busy.set(false);
        this.codeLength.set(response.codeLength || 6);
        this.form.reset();
        this.startCountdown();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.failure.set(
          describeError(error, 'We could not send another code just yet. Please wait a moment.'),
        );
      },
    });
  }

  private submit(code: string): void {
    this.busy.set(true);
    this.failure.set(null);

    const number = this.mobile();
    const challenge = this.challenge();

    const request = number
      ? this.auth.verifyOtp(number, code)
      : this.auth.verifyTwoFactor(challenge ?? '', code);

    request.subscribe({
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
          describeError(error, 'That code is not right, or it has expired. Ask for a new one.'),
        );
      },
    });
  }

  private startCountdown(): void {
    this.stopCountdown();
    this.secondsLeft.set(RESEND_SECONDS);

    this.timer = setInterval(() => {
      this.secondsLeft.update((seconds) => Math.max(0, seconds - 1));
      if (this.secondsLeft() === 0) this.stopCountdown();
    }, 1000);
  }

  private stopCountdown(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}
