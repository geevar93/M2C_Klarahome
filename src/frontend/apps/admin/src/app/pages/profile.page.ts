import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AdminSessionService, SessionResponse, TwoFactorSetupResponse } from '@klarahome/data-access-admin';
import { AuthService, SessionStore } from '@klarahome/data-access-auth';
import { KhDatePipe } from '@klarahome/i18n';
import { ConfirmDialog, FormShell, Modal, PageHeader } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Control, Field } from '@klarahome/ui-primitives';
import { formField, formGroup, matches, minLength, numeric, required } from '@klarahome/util';
import { ToastService } from '@klarahome/util';
import { firstValueFrom } from 'rxjs';

import { describeError, fieldErrors } from '../core/describe-error';

/**
 * Your account: the password, the second factor, and every session it has open.
 *
 * These three belong on one screen because they are one question — "is this account still only
 * mine?" — and because the answer to "I think somebody has my password" needs all three within
 * reach: change it, turn on the second factor, and end the sessions that are not yours.
 *
 * **The sessions list is the only way a user discovers a session they did not start**
 * (`docs/07-security-compliance.md` §2). It shows the device and the address each was opened from,
 * marks the current one so it cannot be ended by accident, and offers ending all the others in
 * one action — because at the moment somebody needs this, going through a list one row at a time
 * is exactly what they do not have the composure for.
 *
 * Enrolling a second factor shows the secret **as text as well as in the `otpauth://` URI**: a
 * user setting this up on the same machine that is displaying it cannot scan their own screen, and
 * that is the ordinary case for staff on a desktop.
 */
@Component({
  selector: 'kh-profile-page',
  imports: [Alert, Badge, Button, ConfirmDialog, Control, Field, FormShell, KhDatePipe, Modal, PageHeader],
  template: `
    <kh-page-header heading="Your profile and security" [description]="identityLine()" />

    <section class="card">
      <kh-form-shell
        heading="Change your password"
        description="Changing it ends every other session on this account."
        [summary]="passwordSummary()"
        [saving]="savingPassword()"
        submitLabel="Change password"
        [showDefaultActions]="true"
        (submitted)="changePassword()"
        (cancelled)="passwordForm.reset()"
      >
        <kh-field
          label="Current password"
          for="current-password"
          [error]="passwordForm.fields.current.error()"
        >
          <input
            khControl
            id="current-password"
            type="password"
            autocomplete="current-password"
            [khInvalid]="!!passwordForm.fields.current.error()"
            [value]="passwordForm.fields.current.value()"
            (input)="passwordForm.fields.current.set($any($event.target).value)"
            (touched)="passwordForm.fields.current.markTouched()"
          />
        </kh-field>

        <kh-field
          label="New password"
          for="new-password"
          hint="At least 12 characters."
          [error]="passwordForm.fields.next.error()"
        >
          <input
            khControl
            id="new-password"
            type="password"
            autocomplete="new-password"
            [khInvalid]="!!passwordForm.fields.next.error()"
            [value]="passwordForm.fields.next.value()"
            (input)="passwordForm.fields.next.set($any($event.target).value)"
            (touched)="passwordForm.fields.next.markTouched()"
          />
        </kh-field>

        <kh-field
          label="Confirm new password"
          for="confirm-password"
          [error]="passwordForm.fields.confirm.error()"
        >
          <input
            khControl
            id="confirm-password"
            type="password"
            autocomplete="new-password"
            [khInvalid]="!!passwordForm.fields.confirm.error()"
            [value]="passwordForm.fields.confirm.value()"
            (input)="passwordForm.fields.confirm.set($any($event.target).value)"
            (touched)="passwordForm.fields.confirm.markTouched()"
          />
        </kh-field>
      </kh-form-shell>
    </section>

    <section class="card">
      <header class="section-head">
        <h2>Two-factor authentication</h2>
        @if (twoFactorEnabled()) {
          <kh-badge tone="success">On</kh-badge>
        } @else {
          <kh-badge tone="warning">Off</kh-badge>
        }
      </header>

      @if (twoFactorEnabled()) {
        <p class="muted">Signing in asks for a code from your authenticator app as well as your password.</p>
        <button khButton type="button" variant="danger" (click)="disableOpen.set(true)">
          Turn off two-factor authentication
        </button>
      } @else {
        <p class="muted">
          A password on its own is one stolen credential away from your account. An authenticator app adds a
          code that changes every thirty seconds.
        </p>
        <button khButton type="button" variant="primary" [disabled]="startingSetup()" (click)="startSetup()">
          {{ startingSetup() ? 'Preparing…' : 'Set up two-factor authentication' }}
        </button>
      }
    </section>

    <section class="card">
      <header class="section-head">
        <h2>Where you are signed in</h2>
        <button khButton type="button" size="sm" [disabled]="loadingSessions()" (click)="loadSessions()">
          Refresh
        </button>
      </header>

      @if (sessionsError(); as message) {
        <kh-alert tone="danger">{{ message }}</kh-alert>
      }

      <ul class="sessions">
        @for (item of sessions(); track item.id) {
          <li>
            <div class="session-body">
              <p class="device">
                {{ item.device ?? 'Unknown device' }}
                @if (item.isCurrent) {
                  <kh-badge tone="primary">This one</kh-badge>
                }
              </p>
              <p class="muted">
                {{ item.ipAddress ?? 'Address unknown' }} · started {{ item.startedAt | khDate }} · last seen
                {{ item.lastSeenAt | khDate }}
              </p>
            </div>

            @if (!item.isCurrent) {
              <button khButton type="button" size="sm" variant="danger" (click)="revoke(item)">End</button>
            }
          </li>
        } @empty {
          @if (!loadingSessions()) {
            <li class="muted">No other sessions.</li>
          }
        }
      </ul>

      @if (otherSessionCount() > 0) {
        <button khButton type="button" variant="danger" (click)="revokeAllOpen.set(true)">
          End the other {{ otherSessionCount() }} session(s)
        </button>
      }
    </section>

    <!-- Enrolment -->
    <kh-modal
      [open]="setup() !== null"
      heading="Set up two-factor authentication"
      width="30rem"
      (closed)="cancelSetup()"
    >
      @if (setup(); as details) {
        <ol class="steps">
          <li>Open your authenticator app and add an account.</li>
          <li>
            Enter this key, or scan the URI below if your app can:
            <code class="secret">{{ details.secret }}</code>
            <code class="uri">{{ details.otpAuthUri }}</code>
          </li>
          <li>Type the six-digit code it shows.</li>
        </ol>

        <kh-field label="Code from your app" for="enrol-code" [error]="enrolCode.error()">
          <input
            khControl
            khNumeric
            id="enrol-code"
            type="text"
            inputmode="numeric"
            maxlength="6"
            autocomplete="one-time-code"
            [khInvalid]="!!enrolCode.error()"
            [value]="enrolCode.value()"
            (input)="enrolCode.set($any($event.target).value)"
            (touched)="enrolCode.markTouched()"
          />
        </kh-field>
      }

      <div slot="footer">
        <button khButton type="button" variant="tertiary" (click)="cancelSetup()">Cancel</button>
        <button khButton type="button" variant="primary" [disabled]="enabling()" (click)="confirmSetup()">
          {{ enabling() ? 'Checking…' : 'Turn it on' }}
        </button>
      </div>
    </kh-modal>

    <!-- Turning it off -->
    <kh-modal
      [open]="disableOpen()"
      heading="Turn off two-factor authentication"
      width="28rem"
      (closed)="disableOpen.set(false)"
    >
      <kh-alert tone="warning">
        Your account will be protected by its password alone. Both your password and a current code are needed
        to do this.
      </kh-alert>

      <kh-field label="Your password" for="disable-password" [error]="disableForm.fields.password.error()">
        <input
          khControl
          id="disable-password"
          type="password"
          autocomplete="current-password"
          [khInvalid]="!!disableForm.fields.password.error()"
          [value]="disableForm.fields.password.value()"
          (input)="disableForm.fields.password.set($any($event.target).value)"
          (touched)="disableForm.fields.password.markTouched()"
        />
      </kh-field>

      <kh-field label="Code from your app" for="disable-code" [error]="disableForm.fields.code.error()">
        <input
          khControl
          khNumeric
          id="disable-code"
          type="text"
          inputmode="numeric"
          maxlength="6"
          [khInvalid]="!!disableForm.fields.code.error()"
          [value]="disableForm.fields.code.value()"
          (input)="disableForm.fields.code.set($any($event.target).value)"
          (touched)="disableForm.fields.code.markTouched()"
        />
      </kh-field>

      <div slot="footer">
        <button khButton type="button" variant="tertiary" (click)="disableOpen.set(false)">Cancel</button>
        <button khButton type="button" variant="danger" [disabled]="disabling()" (click)="disableTwoFactor()">
          {{ disabling() ? 'Turning off…' : 'Turn it off' }}
        </button>
      </div>
    </kh-modal>

    <kh-confirm-dialog
      [open]="revokeAllOpen()"
      heading="End every other session?"
      message="Anyone signed in to this account on another device will be signed out immediately. This one stays open."
      confirmLabel="End the others"
      [busy]="revokingAll()"
      (confirmed)="revokeAll()"
      (cancelled)="revokeAllOpen.set(false)"
    />
  `,
  styles: `
    .card {
      max-width: 42rem;
      margin-block-end: var(--space-6);
      padding: var(--space-5);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .section-head {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      justify-content: space-between;
      margin-block-end: var(--space-3);
    }

    h2 {
      margin: 0;
      font-size: var(--text-lg);
    }

    .muted {
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .sessions {
      margin: 0 0 var(--space-4);
      padding: 0;
      list-style: none;
    }

    .sessions li {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      justify-content: space-between;
      padding-block: var(--space-3);
      border-block-end: 1px solid var(--color-border);
    }

    .session-body {
      min-width: 0;
    }

    .device {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      margin: 0 0 var(--space-1);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .sessions p {
      margin: 0;
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
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfilePage {
  private readonly accounts = inject(AdminSessionService);
  private readonly auth = inject(AuthService);
  private readonly store = inject(SessionStore);
  private readonly toasts = inject(ToastService);

  protected readonly sessions = signal<readonly SessionResponse[]>([]);
  protected readonly loadingSessions = signal(false);
  protected readonly sessionsError = signal<string | null>(null);

  protected readonly setup = signal<TwoFactorSetupResponse | null>(null);
  protected readonly startingSetup = signal(false);
  protected readonly enabling = signal(false);
  protected readonly twoFactorEnabled = signal(false);

  protected readonly disableOpen = signal(false);
  protected readonly disabling = signal(false);
  protected readonly revokeAllOpen = signal(false);
  protected readonly revokingAll = signal(false);
  protected readonly savingPassword = signal(false);
  protected readonly passwordSummary = signal<readonly string[]>([]);

  protected readonly identityLine = computed(() => {
    const session = this.store.session();
    if (!session) return null;
    const scope = session.vendorId ? 'Seller account' : 'Platform account';
    return `${session.displayName} · ${scope} · ${session.roles.join(', ') || 'no roles'}`;
  });

  protected readonly otherSessionCount = computed(
    () => this.sessions().filter((item) => !item.isCurrent).length,
  );

  private readonly passwordSubmitted = signal(false);
  private readonly nextPassword = formField(
    '',
    [required('Password'), minLength(12, 'Password')],
    this.passwordSubmitted,
  );
  protected readonly passwordForm = formGroup(this.passwordSubmitted, {
    current: formField('', [required('Password')], this.passwordSubmitted),
    next: this.nextPassword,
    confirm: formField(
      '',
      [required('Confirmation'), matches(this.nextPassword.value, 'The passwords')],
      this.passwordSubmitted,
    ),
  });

  private readonly enrolSubmitted = signal(false);
  protected readonly enrolCode = formField('', [required('Code'), numeric(6)], this.enrolSubmitted);

  private readonly disableSubmitted = signal(false);
  protected readonly disableForm = formGroup(this.disableSubmitted, {
    password: formField('', [required('Password')], this.disableSubmitted),
    code: formField('', [required('Code'), numeric(6)], this.disableSubmitted),
  });

  constructor() {
    this.loadSessions();
    this.accounts.me().subscribe({
      next: (me) => this.twoFactorEnabled.set(me.user.twoFactorEnabled),
      // The badge simply does not claim anything it could not confirm.
      error: () => undefined,
    });
  }

  protected loadSessions(): void {
    this.loadingSessions.set(true);
    this.sessionsError.set(null);
    this.accounts.sessions().subscribe({
      next: (items) => {
        this.sessions.set(items);
        this.loadingSessions.set(false);
      },
      error: (error: unknown) => {
        this.sessionsError.set(describeError(error, 'Your sessions could not be loaded.'));
        this.loadingSessions.set(false);
      },
    });
  }

  protected async changePassword(): Promise<void> {
    if (!this.passwordForm.submit() || this.savingPassword()) return;

    this.savingPassword.set(true);
    this.passwordSummary.set([]);
    const values = this.passwordForm.values();

    try {
      const response = await firstValueFrom(this.accounts.changePassword(values.current, values.next));
      // The API rotates the token family, so the fresh one has to be adopted or this session is
      // the next request away from being signed out by its own password change.
      this.auth.adopt(response);
      this.passwordForm.reset();
      this.toasts.success('Your password has been changed. Other sessions have been signed out.');
      this.loadSessions();
    } catch (error) {
      const unmatched = this.passwordForm.applyServerErrors(fieldErrors(error));
      this.passwordSummary.set(
        unmatched.length > 0 ? unmatched : [describeError(error, 'Your password could not be changed.')],
      );
    } finally {
      this.savingPassword.set(false);
    }
  }

  protected startSetup(): void {
    this.startingSetup.set(true);
    this.enrolCode.reset();
    this.enrolSubmitted.set(false);

    this.accounts.startTwoFactorSetup().subscribe({
      next: (details) => {
        this.setup.set(details);
        this.startingSetup.set(false);
      },
      error: (error: unknown) => {
        this.startingSetup.set(false);
        this.toasts.danger(describeError(error, 'Two-factor setup could not be started.'));
      },
    });
  }

  protected cancelSetup(): void {
    // The secret is dropped rather than kept for a second attempt: an abandoned enrolment leaving
    // a live secret in a browser tab is a credential nobody is looking after.
    this.setup.set(null);
    this.enrolCode.reset();
  }

  protected confirmSetup(): void {
    this.enrolSubmitted.set(true);
    this.enrolCode.markTouched();
    if (this.enrolCode.problem() !== null || this.enabling()) return;

    this.enabling.set(true);
    this.accounts.enableTwoFactor(this.enrolCode.value().trim()).subscribe({
      next: () => {
        this.enabling.set(false);
        this.setup.set(null);
        this.twoFactorEnabled.set(true);
        this.toasts.success('Two-factor authentication is on.');
      },
      error: (error: unknown) => {
        this.enabling.set(false);
        this.enrolCode.setServerError(
          describeError(error, 'That code was not accepted. Try the next one your app shows.'),
        );
      },
    });
  }

  protected disableTwoFactor(): void {
    if (!this.disableForm.submit() || this.disabling()) return;

    this.disabling.set(true);
    const values = this.disableForm.values();
    this.accounts.disableTwoFactor(values.password, values.code).subscribe({
      next: () => {
        this.disabling.set(false);
        this.disableOpen.set(false);
        this.disableForm.reset();
        this.twoFactorEnabled.set(false);
        this.toasts.warning('Two-factor authentication is off.');
      },
      error: (error: unknown) => {
        this.disabling.set(false);
        const unmatched = this.disableForm.applyServerErrors(fieldErrors(error));
        this.toasts.danger(unmatched[0] ?? describeError(error, 'That could not be turned off.'));
      },
    });
  }

  protected revoke(item: SessionResponse): void {
    this.accounts.revokeSession(item.id).subscribe({
      next: () => {
        this.toasts.success('That session has been ended.');
        this.loadSessions();
      },
      error: (error: unknown) => this.toasts.danger(describeError(error, 'That session could not be ended.')),
    });
  }

  protected revokeAll(): void {
    this.revokingAll.set(true);
    this.accounts.revokeAllSessions(true).subscribe({
      next: (result) => {
        this.revokingAll.set(false);
        this.revokeAllOpen.set(false);
        this.toasts.success(`${result.revoked} session(s) ended.`);
        this.loadSessions();
      },
      error: (error: unknown) => {
        this.revokingAll.set(false);
        this.toasts.danger(describeError(error, 'Those sessions could not be ended.'));
      },
    });
  }
}
