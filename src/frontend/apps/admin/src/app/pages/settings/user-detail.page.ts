import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import {
  AdminUserResponse,
  IdentityAdminService,
  ImpersonationStore,
  RoleResponse,
  UserStatus,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import { ConfirmDialog, Modal, PageHeader, StatusBadge } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';

const STATUSES: readonly { value: UserStatus; label: string; hint: string }[] = [
  { value: 'Active', label: 'Active', hint: 'They can sign in.' },
  { value: 'Suspended', label: 'Suspended', hint: 'Refused at sign-in; the account is kept.' },
  { value: 'Disabled', label: 'Disabled', hint: 'The end of the account.' },
];

/**
 * One user: their roles, their status, and a way back in.
 *
 * **Roles are the whole of authorisation.** Changing them changes what this person can do at their
 * next request, not at their next sign-in, because the API evaluates permissions per request. The
 * form therefore says so — somebody removing a role from a colleague who is mid-task should know
 * it takes effect immediately.
 *
 * **A temporary password is not a password reset.** It is Step 7A's answer for a deployment with
 * no mail delivery: an administrator sets a one-time password, hands it over out of band, and the
 * holder must change it at their next sign-in. `mustChangePassword` on the response is what says
 * it took. Nothing here can read a password, and nothing here sets a permanent one on somebody
 * else's behalf.
 *
 * **The lockout is shown and not clearable**, because it expires on its own and because clearing
 * it is exactly what an attacker who has just been locked out would want.
 */
@Component({
  selector: 'kh-user-detail-page',
  imports: [
    Alert,
    Badge,
    Button,
    Checkbox,
    ConfirmDialog,
    Control,
    Field,
    HasPermission,
    Modal,
    PageHeader,
    Skeleton,
    StatusBadge,
  ],
  template: `
    <kh-page-header
      [heading]="user()?.email ?? user()?.mobile ?? 'User'"
      [description]="subtitle()"
      [crumbs]="[{ label: 'Users', path: '/settings/users' }]"
    >
      @if (user(); as current) {
        <kh-status-badge [status]="current.status" />
        <kh-badge [tone]="current.twoFactorEnabled ? 'success' : 'neutral'">
          {{ current.twoFactorEnabled ? '2FA on' : '2FA off' }}
        </kh-badge>

        @if (current.userType === 'Customer' && current.status === 'Active') {
          <button
            *khHasPermission="'identity.user.impersonate'"
            khButton
            type="button"
            (click)="startImpersonation()"
          >
            Act as this customer
          </button>
        }
      }
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="This user could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="16rem" />
    } @else if (user(); as current) {
      @if (actionError(); as message) {
        <kh-alert tone="danger" heading="That did not take">{{ message }}</kh-alert>
      }

      @if (isLockedOut(current)) {
        <kh-alert tone="warning" heading="Locked out">
          Too many failed sign-ins. The lock lifts by itself at {{ dateTime(current.lockedUntil) }}; it is not
          cleared by hand, because clearing it is what somebody who has just been locked out would ask for.
        </kh-alert>
      }

      @if (current.passwordSetupPending) {
        <kh-alert tone="info" heading="They have never signed in">
          Set a temporary password below and hand it over. They must change it when they use it.
        </kh-alert>
      }

      <div class="layout">
        <section class="panel">
          <h2>Roles</h2>
          <p class="hint">
            A user's permissions come from their roles and from nowhere else. A change takes effect on their
            next request, not at their next sign-in.
          </p>

          @if (assignableRoles().length === 0) {
            <p class="note">No role of the right scope exists yet.</p>
          } @else {
            @for (role of assignableRoles(); track role.id) {
              <kh-checkbox
                [label]="role.name"
                [description]="role.description"
                [inputId]="'role-' + role.id"
                [checked]="roleCodes().includes(role.code)"
                (checkedChange)="toggleRole(role.code, $event)"
              />
            }

            <button
              khButton
              type="button"
              variant="primary"
              *khHasPermission="'identity.user.manage'"
              [disabled]="busy()"
              (click)="saveRoles()"
            >
              {{ busy() ? 'Saving…' : 'Save roles' }}
            </button>
          }
        </section>

        <div class="column">
          <section class="panel">
            <h2>Status</h2>

            <kh-field label="Account status" for="user-status" [hint]="statusHint()">
              <select
                khControl
                id="user-status"
                [value]="status()"
                (change)="status.set($any($event.target).value)"
              >
                @for (choice of statuses; track choice.value) {
                  <option [value]="choice.value">{{ choice.label }}</option>
                }
              </select>
            </kh-field>

            <button
              khButton
              type="button"
              *khHasPermission="'identity.user.manage'"
              [variant]="status() === 'Disabled' ? 'danger' : 'primary'"
              [disabled]="busy() || status() === current.status"
              (click)="confirmingStatus.set(true)"
            >
              Change status
            </button>
          </section>

          <section class="panel" *khHasPermission="'identity.user.manage'">
            <h2>Temporary password</h2>
            <p class="hint">
              A one-time password to hand over out of band. They must change it when they use it, and nothing
              here can read what they choose.
            </p>

            <kh-field label="Temporary password" for="user-temp">
              <input
                khControl
                id="user-temp"
                type="text"
                autocomplete="off"
                [value]="temporaryPassword()"
                (input)="temporaryPassword.set($any($event.target).value)"
              />
            </kh-field>

            <button
              khButton
              type="button"
              [disabled]="busy() || temporaryPassword().length < 8"
              (click)="setPassword()"
            >
              Set it
            </button>
          </section>

          <section class="panel">
            <h2>The account</h2>
            <dl>
              <dt>Email</dt>
              <dd>
                {{ current.email ?? '—' }}
                @if (current.email && !current.emailVerified) {
                  <span class="note">unverified</span>
                }
              </dd>
              <dt>Mobile</dt>
              <dd>
                {{ current.mobile ?? '—' }}
                @if (current.mobile && !current.mobileVerified) {
                  <span class="note">unverified</span>
                }
              </dd>
              <dt>Kind</dt>
              <dd>{{ current.userType }}</dd>
              <dt>Seller</dt>
              <dd>{{ current.vendorId ?? 'Not a seller account' }}</dd>
              <dt>Created</dt>
              <dd>{{ dateTime(current.createdAt) }}</dd>
              <dt>Last signed in</dt>
              <dd>{{ dateTime(current.lastLoginAt) || 'Never' }}</dd>
            </dl>
          </section>
        </div>
      </div>
    }

    <kh-confirm-dialog
      [open]="confirmingStatus()"
      heading="Change this account's status"
      [message]="statusMessage()"
      confirmLabel="Change it"
      [tone]="status() === 'Disabled' ? 'danger' : 'warning'"
      [busy]="busy()"
      (confirmed)="saveStatus()"
      (cancelled)="confirmingStatus.set(false)"
    />

    <kh-modal
      [open]="impersonating()"
      heading="Act as this customer"
      width="30rem"
      [dismissible]="!busy()"
      (closed)="impersonating.set(false)"
    >
      <p class="hint">
        A support session is opened in this customer's name for a fixed window. It cannot be extended, and
        both the start and the stop are written to the audit trail against you.
      </p>

      @if (impersonationError(); as message) {
        <kh-alert tone="danger" heading="It could not be started">{{ message }}</kh-alert>
      }

      <kh-field
        label="Why"
        for="impersonation-reason"
        hint="What you are trying to reproduce. Recorded verbatim, and read by whoever reviews the trail."
      >
        <textarea
          khControl
          id="impersonation-reason"
          rows="3"
          maxlength="500"
          [value]="impersonationReason()"
          (input)="impersonationReason.set($any($event.target).value)"
        ></textarea>
      </kh-field>

      <div slot="footer">
        <button
          khButton
          type="button"
          variant="tertiary"
          [disabled]="busy()"
          (click)="impersonating.set(false)"
        >
          Cancel
        </button>
        <button
          khButton
          type="button"
          variant="primary"
          [disabled]="busy() || impersonationReason().trim().length < 10"
          (click)="impersonate()"
        >
          Start
        </button>
      </div>
    </kh-modal>
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .layout {
      display: grid;
      gap: var(--space-4);
      grid-template-columns: minmax(0, 1fr);
    }

    @media (min-width: 60rem) {
      .layout {
        grid-template-columns: repeat(2, minmax(0, 1fr));
        align-items: start;
      }
    }

    .column {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    .panel {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
    }

    .hint {
      margin: 0 0 var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .note {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    dl {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: var(--space-2) var(--space-4);
      margin: 0;
    }

    dt {
      color: var(--color-text-muted);
    }

    dd {
      margin: 0;
    }

    button {
      margin-block-start: var(--space-3);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserDetailPage {
  private readonly identity = inject(IdentityAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly toasts = inject(ToastService);

  protected readonly statuses = STATUSES;
  protected readonly dateTime = tableDateTime;
  private readonly id = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly user = signal<AdminUserResponse | null>(null);
  protected readonly roles = signal<readonly RoleResponse[]>([]);
  protected readonly roleCodes = signal<readonly string[]>([]);
  protected readonly status = signal<UserStatus>('Active');
  protected readonly temporaryPassword = signal('');

  protected readonly loading = signal(false);
  protected readonly busy = signal(false);

  // ---- Support impersonation (Step 28B, deliverable 1) -------------------------------------------

  private readonly impersonations = inject(ImpersonationStore);

  protected readonly impersonating = signal(false);
  protected readonly impersonationReason = signal('');
  protected readonly impersonationError = signal<string | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly confirmingStatus = signal(false);

  protected readonly subtitle = computed(() => {
    const current = this.user();
    if (!current) return null;
    return `${current.userType} · created ${tableDateTime(current.createdAt)}`;
  });

  protected readonly statusHint = computed(
    () => STATUSES.find((choice) => choice.value === this.status())?.hint ?? '',
  );

  protected readonly statusMessage = computed(() => {
    switch (this.status()) {
      case 'Suspended':
        return 'They are refused at sign-in from now on. Their sessions are not ended by this; revoke those separately if it is urgent.';
      case 'Disabled':
        return 'The account is finished. Their history stays, and they cannot sign in again.';
      default:
        return 'They can sign in again.';
    }
  });

  /** Only the roles whose scope matches this kind of account. */
  protected readonly assignableRoles = computed(() => {
    const type = this.user()?.userType;
    const wanted = type === 'Vendor' ? 'Vendor' : type === 'Customer' ? 'Customer' : 'Platform';
    return this.roles().filter((role) => role.scope === wanted);
  });

  constructor() {
    this.load();
    this.identity.roles().subscribe({
      next: (roles) => this.roles.set(roles),
      error: () => this.roles.set([]),
    });
  }

  protected isLockedOut(user: AdminUserResponse): boolean {
    return user.lockedUntil !== null && new Date(user.lockedUntil).getTime() > Date.now();
  }

  protected toggleRole(code: string, on: boolean): void {
    this.roleCodes.update((current) =>
      on ? [...new Set([...current, code])] : current.filter((entry) => entry !== code),
    );
  }

  protected saveRoles(): void {
    if (this.busy()) return;

    this.busy.set(true);
    this.actionError.set(null);

    this.identity.setUserRoles(this.id, this.roleCodes()).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.fill(saved);
        this.toasts.success('Roles saved.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'The roles could not be saved.'));
      },
    });
  }

  protected saveStatus(): void {
    if (this.busy()) return;

    this.busy.set(true);
    this.actionError.set(null);

    this.identity.setUserStatus(this.id, this.status()).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.confirmingStatus.set(false);
        this.fill(saved);
        this.toasts.success('Status changed.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.confirmingStatus.set(false);
        this.actionError.set(describeError(error, 'The status could not be changed.'));
      },
    });
  }

  protected setPassword(): void {
    if (this.busy()) return;

    this.busy.set(true);
    this.actionError.set(null);

    this.identity.setTemporaryPassword(this.id, this.temporaryPassword()).subscribe({
      next: (saved) => {
        this.busy.set(false);
        // Cleared straight away: a one-time password must not linger in a form on a shared screen.
        this.temporaryPassword.set('');
        this.fill(saved);
        this.toasts.success('Temporary password set. Hand it over out of band.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'It could not be set.'));
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.identity.user(this.id).subscribe({
      next: (user) => {
        this.loading.set(false);
        this.fill(user);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'That user could not be loaded.'));
      },
    });
  }

  private fill(user: AdminUserResponse): void {
    this.user.set(user);
    this.roleCodes.set(user.roles);
    this.status.set(user.status);
  }

  protected startImpersonation(): void {
    this.impersonationReason.set('');
    this.impersonationError.set(null);
    this.impersonating.set(true);
  }

  /**
   * Opens the support session.
   *
   * The reason is checked here for length only so the button can be disabled rather than the
   * request refused; the API is the authority on it, and its refusal is what is shown.
   */
  protected impersonate(): void {
    const reason = this.impersonationReason().trim();
    if (this.busy() || reason.length < 10) return;

    this.busy.set(true);
    this.impersonationError.set(null);

    this.identity.impersonate(this.id, reason).subscribe({
      next: (started) => {
        this.busy.set(false);
        this.impersonating.set(false);
        this.impersonations.start(started);
        this.toasts.success('You are now acting as this customer. The banner ends it.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.impersonationError.set(describeError(error, 'That session could not be started.'));
      },
    });
  }
}
