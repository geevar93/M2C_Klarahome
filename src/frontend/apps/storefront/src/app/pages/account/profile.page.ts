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
import { ProfileStore } from '@klarahome/data-access-account';
import { isApiError } from '@klarahome/data-access-auth';
import { Alert, Badge, Button, Checkbox, Control, ErrorState, Field, PageHeader, Skeleton } from '@klarahome/ui-primitives';
import { FeatureFlags, formField, formGroup, gstin, maxLength } from '@klarahome/util';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';

/**
 * Your profile — `/account/profile`.
 *
 * What the customer told us about themselves, and it is deliberately short: a name, a date of birth,
 * a GSTIN for business invoices, and whether they want marketing. A storefront that asks for more
 * than it uses is collecting personal data it has no purpose for, which the DPDP Act treats as a
 * question to answer rather than a form field to add.
 *
 * **Identity is shown but not edited here.** The mobile number and the email address are what the
 * account is proved by, and changing one is a verification rather than a save — so they are
 * displayed with their verified state and their own "verify" action, and the form below them is the
 * ordinary PATCH.
 *
 * The verification code is entered on the same screen rather than on a routed one: this is a
 * ten-second act in the middle of a form, and taking somebody to another URL for it means coming
 * back to a form that has forgotten what they were typing.
 */
@Component({
  selector: 'kh-account-profile-page',
  imports: [Alert, Badge, Button, Checkbox, Control, ErrorState, Field, PageHeader, Skeleton],
  template: `
    <kh-page-header title="Profile" />

    @if (!store.hasLoaded()) {
      <kh-skeleton height="12rem" />
    } @else if (error()) {
      <kh-error-state (retry)="load()" />
    } @else {
      <section class="identity">
        <h2>How you sign in</h2>

        @if (store.user(); as user) {
          <dl class="identity-list">
            <div class="row">
              <dt>Mobile number</dt>
              <dd>
                <span class="value">{{ user.mobile || 'Not added' }}</span>
                @if (user.mobile) {
                  @if (user.mobileVerified) {
                    <kh-badge tone="success">Verified</kh-badge>
                  } @else if (smsVerifyAvailable()) {
                    <button khButton variant="tertiary" size="sm" type="button" (click)="startVerify('Sms')">
                      Verify
                    </button>
                  } @else {
                    <span class="note">Verification by SMS is coming soon.</span>
                  }
                }
              </dd>
            </div>

            <div class="row">
              <dt>Email address</dt>
              <dd>
                <span class="value">{{ user.email || 'Not added' }}</span>
                @if (user.email) {
                  @if (user.emailVerified) {
                    <kh-badge tone="success">Verified</kh-badge>
                  } @else if (emailVerifyAvailable()) {
                    <button khButton variant="tertiary" size="sm" type="button" (click)="startVerify('Email')">
                      Verify
                    </button>
                  } @else {
                    <span class="note">Verification by email is coming soon.</span>
                  }
                }
              </dd>
            </div>
          </dl>
        }

        @if (verifying(); as channel) {
          <form class="verify" (submit)="confirmVerify($event)" novalidate>
            <kh-field
              label="Verification code"
              for="verify-code"
              [hint]="
                'We sent a code to your ' + (channel === 'Email' ? 'email address' : 'mobile number') + '.'
              "
            >
              <input
                #verifyCodeInput
                khControl
                khNumeric
                id="verify-code"
                type="text"
                inputmode="numeric"
                autocomplete="one-time-code"
                maxlength="8"
                [value]="verifyCode()"
                (input)="verifyCode.set($any($event.target).value.replace(nonDigits, ''))"
              />
            </kh-field>
            <div class="actions">
              <button khButton variant="primary" type="submit" [disabled]="busy()">
                {{ busy() ? 'Confirming…' : 'Confirm' }}
              </button>
              <button khButton variant="tertiary" type="button" (click)="verifying.set(null)">Cancel</button>
            </div>
          </form>
        }
      </section>

      <section>
        <h2>About you</h2>

        <form (submit)="save($event)" novalidate>
          <div class="pair">
            <kh-field label="First name" for="profile-first" [error]="form.fields.firstName.error()">
              <input
                khControl
                id="profile-first"
                type="text"
                autocomplete="given-name"
                [value]="form.fields.firstName.value()"
                (input)="form.fields.firstName.set($any($event.target).value)"
                (touched)="form.fields.firstName.markTouched()"
              />
            </kh-field>

            <kh-field label="Last name" for="profile-last" [error]="form.fields.lastName.error()">
              <input
                khControl
                id="profile-last"
                type="text"
                autocomplete="family-name"
                [value]="form.fields.lastName.value()"
                (input)="form.fields.lastName.set($any($event.target).value)"
                (touched)="form.fields.lastName.markTouched()"
              />
            </kh-field>
          </div>

          <kh-field
            label="Date of birth"
            for="profile-dob"
            [optional]="true"
            hint="So we can wish you well, and nothing else."
            [error]="form.fields.dateOfBirth.error()"
          >
            <input
              khControl
              id="profile-dob"
              type="date"
              autocomplete="bday"
              [value]="form.fields.dateOfBirth.value()"
              (input)="form.fields.dateOfBirth.set($any($event.target).value)"
              (touched)="form.fields.dateOfBirth.markTouched()"
            />
          </kh-field>

          <kh-field
            label="GSTIN"
            for="profile-gstin"
            [optional]="true"
            hint="Used by default on business invoices."
            [error]="form.fields.gstin.error()"
          >
            <input
              khControl
              id="profile-gstin"
              type="text"
              maxlength="15"
              [khInvalid]="!!form.fields.gstin.error()"
              [value]="form.fields.gstin.value()"
              (input)="form.fields.gstin.set($any($event.target).value.toUpperCase())"
              (touched)="form.fields.gstin.markTouched()"
            />
          </kh-field>

          <kh-checkbox
            [bare]="true"
            label="Send me offers and new arrivals"
            description="Order updates are sent regardless — they are part of buying something."
            inputId="profile-consent"
            [checked]="consent()"
            (checkedChange)="consent.set($event)"
          />

          <button khButton variant="primary" type="submit" [disabled]="store.isSaving()">
            {{ store.isSaving() ? 'Saving…' : 'Save changes' }}
          </button>
        </form>
      </section>

      @if (referralCode(); as code) {
        <kh-alert tone="info" heading="Your referral code">{{ code }}</kh-alert>
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }

    h2 {
      font-size: var(--text-lg);
    }

    section {
      margin-block-end: var(--space-8);
    }

    .identity-list {
      margin: 0;
    }

    .row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      padding-block: var(--space-3);
      border-block-end: 1px solid var(--color-border);
    }

    dt {
      margin: 0;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    dd {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      margin: 0;
    }

    .value {
      font-size: var(--text-sm);
    }

    /* In place of the "Verify" button when the channel that OTP would go out on is not switched on
       yet — muted, not an error state: nothing here is wrong, the capability just is not live. */
    .note {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .verify {
      margin-block-start: var(--space-4);
      max-inline-size: 20rem;
    }

    .pair {
      display: grid;
      gap: var(--space-4);
    }

    @media (min-width: 480px) {
      .pair {
        grid-template-columns: 1fr 1fr;
      }
    }

    .actions {
      display: flex;
      gap: var(--space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountProfilePage {
  protected readonly store = inject(ProfileStore);
  private readonly flags = inject(FeatureFlags);
  private readonly toasts = inject(ToastService);

  /** Used by the template to strip anything but digits out of the code box. */
  protected readonly nonDigits = /\D/g;

  // The "Verify" action sends an OTP over a channel this deployment may not have switched on yet
  // (Step 7A's flags, which `availableChannels` ignores — see notifications.page.ts). Mobile needs
  // only SMS; email verification needs both the SMS-equivalent email flag and the separate
  // identity.email-verification flag, because a deployment can run email notifications without
  // having turned on email as a proof of identity.
  protected readonly smsVerifyAvailable = this.flags.flag('notifications.sms');
  private readonly emailNotificationsOn = this.flags.flag('notifications.email');
  private readonly emailVerificationOn = this.flags.flag('identity.email-verification');
  protected readonly emailVerifyAvailable = computed(
    () => this.emailNotificationsOn() && this.emailVerificationOn(),
  );

  protected readonly consent = signal(false);
  protected readonly verifying = signal<'Sms' | 'Email' | null>(null);
  protected readonly verifyCode = signal('');
  protected readonly busy = signal(false);
  protected readonly referralCode = signal<string | null>(null);
  protected readonly error = signal(false);

  private readonly verifyCodeInput = viewChild<ElementRef<HTMLInputElement>>('verifyCodeInput');

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    firstName: formField('', [maxLength(60, 'First name')], this.submitted),
    lastName: formField('', [maxLength(60, 'Last name')], this.submitted),
    dateOfBirth: formField('', [], this.submitted),
    gstin: formField('', [gstin], this.submitted),
  });

  constructor() {
    this.load();

    // The document arrives after the form is constructed, so seeding is an effect. It is guarded on
    // the profile changing identity rather than running on every emission, so a save that answers
    // with the same document does not overwrite what somebody has since typed.
    effect(() => {
      const profile = this.store.profile();
      if (!profile) return;
      this.form.reset({
        firstName: profile.firstName ?? '',
        lastName: profile.lastName ?? '',
        // The API sends a full timestamp; `<input type="date">` wants the date part alone.
        dateOfBirth: (profile.dateOfBirth ?? '').slice(0, 10),
        gstin: profile.gstin ?? '',
      });
      this.consent.set(profile.marketingConsent);
      this.referralCode.set(profile.referralCode || null);
    });

    // The verification code box appears mid-form; sending focus to it is what lets somebody who
    // asked for a code start typing it without reaching for the mouse.
    effect(() => {
      const isVerifying = this.verifying();
      const input = this.verifyCodeInput();
      if (isVerifying && input) input.nativeElement.focus();
    });
  }

  protected load(): void {
    this.error.set(false);
    this.store.load().subscribe({ error: () => this.error.set(true) });
  }

  protected save(event: Event): void {
    event.preventDefault();
    if (!this.form.submit()) return;

    const values = this.form.values();
    this.store
      .update({
        firstName: values.firstName || null,
        lastName: values.lastName || null,
        dateOfBirth: values.dateOfBirth || null,
        // Gender is on the contract and is not asked for: nothing on this storefront uses it, and a
        // field with no purpose is personal data collected for no reason.
        gender: null,
        gstin: values.gstin || null,
        marketingConsent: this.consent(),
      })
      .subscribe({
        next: () => this.toasts.success('Your profile is saved.'),
        error: (error: unknown) => {
          if (isApiError(error) && error.status === 422) {
            const unmatched = this.form.applyServerErrors(error.fieldErrors);
            if (unmatched.length > 0) this.toasts.danger(unmatched.join(' '));
            return;
          }
          this.toasts.danger(describeError(error, 'We could not save your profile.'));
        },
      });
  }

  protected startVerify(channel: 'Sms' | 'Email'): void {
    this.verifyCode.set('');
    this.store.requestVerification(channel).subscribe({
      next: () => this.verifying.set(channel),
      error: (error: unknown) =>
        // Both channels can be switched off by an operator (Step 7A), in which case this is the
        // API saying so rather than a failure to hide.
        this.toasts.warning(
          describeError(error, 'We could not send a code just now. Please try again later.'),
        ),
    });
  }

  protected confirmVerify(event: Event): void {
    event.preventDefault();
    const channel = this.verifying();
    if (!channel || this.busy() || !this.verifyCode()) return;

    this.busy.set(true);
    this.store.confirmVerification(channel, this.verifyCode()).subscribe({
      next: () => {
        this.busy.set(false);
        this.verifying.set(null);
        this.toasts.success('Verified. Thank you.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.toasts.warning(describeError(error, 'That code is not right, or it has expired.'));
      },
    });
  }
}
