import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  AdminUserResponse,
  IdentityAdminService,
  RoleResponse,
  UserFilters,
  UserType,
  VendorsAdminService,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  CellTemplate,
  DataTable,
  DataTableColumn,
  EntityOption,
  EntityPicker,
  FilterBar,
  FilterDefinition,
  FilterValues,
  Modal,
  PageHeader,
  StatusBadge,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon } from '@klarahome/ui-primitives';
import { ToastService, email as emailRule, formField, formGroup, required } from '@klarahome/util';
import { Observable, map } from 'rxjs';

import { describeError, fieldErrors } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';

const USER_TYPES: readonly { value: UserType; label: string; hint: string }[] = [
  { value: 'Staff', label: 'Store staff', hint: 'Works for the platform.' },
  { value: 'Vendor', label: "A seller's user", hint: 'Needs the seller id they belong to.' },
  { value: 'Customer', label: 'A customer', hint: 'Shops on the storefront; has no back office.' },
];

/**
 * Who may sign in.
 *
 * **A user is created with roles, not with permissions.** The permission catalogue belongs to the
 * platform and is attached to roles; this screen therefore offers the roles that exist and never a
 * free-text permission — a misspelt permission is a role that grants nothing and looks like it
 * grants something.
 *
 * **A seller's user needs their seller.** `vendorId` is what makes the account vendor-scoped, and
 * it is what the back office reads to decide that the platform-only screens are not theirs (Step
 * 26's `platformOnly`). Creating a `Vendor` user without one produces an account that holds seller
 * permissions and has no seller to use them on, so the form asks for it and says why.
 *
 * The list's own columns carry the two facts that matter for support: whether two-factor is on,
 * and whether the account is locked out.
 */
@Component({
  selector: 'kh-users-page',
  imports: [
    Alert,
    Badge,
    Button,
    CellTemplate,
    Checkbox,
    Control,
    DataTable,
    EntityPicker,
    Field,
    FilterBar,
    HasPermission,
    Icon,
    Modal,
    PageHeader,
    RouterLink,
    StatusBadge,
  ],
  template: `
    <kh-page-header heading="Users" description="Who may sign in, and what they may do.">
      <button
        khButton
        type="button"
        variant="primary"
        *khHasPermission="'identity.user.manage'"
        (click)="startCreate()"
      >
        <kh-icon name="plus" size="sm" />
        New user
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Users could not be loaded">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Users"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="users-list"
      exportMode="page"
      emptyMessage="No user matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        searchLabel="Search by email or mobile"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="identity" let-row>
        <a class="link" [routerLink]="['/settings/users', row.id]">{{ row.email ?? row.mobile ?? row.id }}</a>
        <span class="note">
          {{ row.userType }}
          @if (row.vendorId) {
            · seller {{ row.vendorId }}
          }
        </span>
      </ng-template>

      <ng-template khCell="status" let-row>
        <kh-status-badge [status]="row.status" />
        @if (isLockedOut(row)) {
          <span class="note warn">Locked until {{ dateTime(row.lockedUntil) }}</span>
        }
        @if (row.mustChangePassword) {
          <span class="note">Must change their password</span>
        }
      </ng-template>

      <ng-template khCell="security" let-row>
        <kh-badge [tone]="row.twoFactorEnabled ? 'success' : 'neutral'">
          {{ row.twoFactorEnabled ? '2FA on' : '2FA off' }}
        </kh-badge>
        @if (!row.emailVerified && row.email) {
          <span class="note">Email unverified</span>
        }
      </ng-template>
    </kh-data-table>

    <kh-modal [open]="creating()" heading="New user" (closed)="creating.set(false)">
      @if (createError(); as message) {
        <kh-alert tone="danger" heading="It could not be created">{{ message }}</kh-alert>
      }

      <kh-field label="Email" for="user-email" [error]="form.fields.email.error()">
        <input
          khControl
          id="user-email"
          type="email"
          [value]="form.fields.email.value()"
          (input)="form.fields.email.set($any($event.target).value)"
          (touched)="form.fields.email.markTouched()"
        />
      </kh-field>

      <kh-field label="Mobile" for="user-mobile" [optional]="true">
        <input
          khControl
          id="user-mobile"
          type="tel"
          [value]="form.fields.mobile.value()"
          (input)="form.fields.mobile.set($any($event.target).value)"
        />
      </kh-field>

      <kh-field label="Kind of account" for="user-type" [hint]="typeHint()">
        <select
          khControl
          id="user-type"
          [value]="userType()"
          (change)="userType.set($any($event.target).value)"
        >
          @for (choice of userTypes; track choice.value) {
            <option [value]="choice.value">{{ choice.label }}</option>
          }
        </select>
      </kh-field>

      @if (userType() === 'Vendor') {
        <kh-entity-picker
          label="Seller"
          inputId="user-vendor"
          hint="Without this the account holds a seller's permissions and has no seller to use them on."
          [search]="vendorSearch"
          [value]="vendorOption()"
          (chose)="form.fields.vendorId.set($event?.id ?? '')"
        />
      }

      <fieldset>
        <legend>Roles</legend>
        <p class="hint">A user's permissions come from their roles, and from nowhere else.</p>
        @for (role of assignableRoles(); track role.id) {
          <kh-checkbox
            [label]="role.name"
            [description]="role.description"
            [inputId]="'role-' + role.id"
            [checked]="roleCodes().includes(role.code)"
            (checkedChange)="toggleRole(role.code, $event)"
          />
        }
      </fieldset>

      <div slot="footer">
        <button khButton type="button" variant="tertiary" (click)="creating.set(false)">Cancel</button>
        <button khButton type="button" variant="primary" [disabled]="saving()" (click)="create()">
          {{ saving() ? 'Creating…' : 'Create and open' }}
        </button>
      </div>
    </kh-modal>
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .link {
      display: block;
      font-weight: var(--weight-medium);
    }

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .note.warn {
      color: var(--color-warning);
    }

    fieldset {
      margin-block-start: var(--space-4);
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    legend {
      padding-inline: var(--space-2);
      font-weight: var(--weight-medium);
    }

    .hint {
      margin: 0 0 var(--space-2);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersPage {
  private readonly identity = inject(IdentityAdminService);
  private readonly vendors = inject(VendorsAdminService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly userTypes = USER_TYPES;
  protected readonly dateTime = tableDateTime;

  protected readonly list = this.identity.users();
  protected readonly values = signal<FilterValues>({});
  protected readonly creating = signal(false);
  protected readonly saving = signal(false);
  protected readonly createError = signal<string | null>(null);
  protected readonly userType = signal<UserType>('Staff');
  protected readonly roleCodes = signal<readonly string[]>([]);
  protected readonly roles = signal<readonly RoleResponse[]>([]);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    email: formField('', [required('An email address'), emailRule()], this.submitted),
    mobile: formField('', [], this.submitted),
    vendorId: formField('', [], this.submitted),
  });

  /**
   * A raw-id text box asked for the one thing nobody has memorised. Every other "pick an existing
   * seller" screen in this app is a search (`vendor-detail.page.ts`, `promotion-detail.page.ts`);
   * this was the one place left typing a UUID by hand, which reads exactly like a blank field until
   * the server refuses it.
   */
  protected readonly vendorOption = computed<EntityOption | null>(() => {
    const id = this.form.fields.vendorId.value();
    return id ? { id, label: id } : null;
  });

  protected readonly vendorSearch = (term: string): Observable<readonly EntityOption[]> =>
    this.vendors
      .searchVendors(term)
      .pipe(
        map((sellers) =>
          sellers.map((seller) => ({ id: seller.id, label: seller.displayName, hint: seller.code })),
        ),
      );

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly typeHint = computed(
    () => USER_TYPES.find((choice) => choice.value === this.userType())?.hint ?? '',
  );

  /** Only the roles whose scope matches the kind of account being made. */
  protected readonly assignableRoles = computed(() => {
    const wanted =
      this.userType() === 'Vendor' ? 'Vendor' : this.userType() === 'Customer' ? 'Customer' : 'Platform';
    return this.roles().filter((role) => role.scope === wanted);
  });

  protected readonly rowKey = (row: AdminUserResponse) => row.id;
  protected readonly rowLabel = (row: AdminUserResponse) => row.email ?? row.mobile ?? row.id;

  protected readonly columns: readonly DataTableColumn<AdminUserResponse>[] = [
    { key: 'identity', label: 'User', kind: 'custom' },
    { key: 'roles', label: 'Roles', value: (row) => row.roles.join(', ') || 'None' },
    { key: 'status', label: 'Status', kind: 'custom', width: '15rem' },
    { key: 'security', label: 'Security', kind: 'custom', width: '11rem' },
    {
      key: 'lastLoginAt',
      label: 'Last signed in',
      kind: 'date',
      value: (row) => tableDateTime(row.lastLoginAt) || 'Never',
      width: '13rem',
    },
    {
      key: 'createdAt',
      label: 'Created',
      kind: 'date',
      value: (row) => tableDateTime(row.createdAt),
      hiddenByDefault: true,
    },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'userType',
      label: 'Kind',
      kind: 'select',
      options: USER_TYPES.map((entry) => ({ value: entry.value, label: entry.label })),
    },
  ];

  constructor() {
    this.list.load();
    this.identity.roles().subscribe({
      next: (roles) => this.roles.set(roles),
      // Not fatal: a user can be created with no role and have one attached afterwards.
      error: () => this.roles.set([]),
    });
  }

  protected isLockedOut(row: AdminUserResponse): boolean {
    return row.lockedUntil !== null && new Date(row.lockedUntil).getTime() > Date.now();
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: UserFilters = {
      search: values['q'],
      userType: (values['userType'] as UserType | undefined) || undefined,
    };
    this.list.setFilters(filters);
  }

  protected toggleRole(code: string, on: boolean): void {
    this.roleCodes.update((current) =>
      on ? [...new Set([...current, code])] : current.filter((entry) => entry !== code),
    );
  }

  protected startCreate(): void {
    this.createError.set(null);
    this.userType.set('Staff');
    this.roleCodes.set([]);
    this.form.reset({ email: '', mobile: '', vendorId: '' });
    this.creating.set(true);
  }

  protected create(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    this.saving.set(true);
    this.createError.set(null);

    this.identity
      .createUser({
        email: values.email,
        mobile: values.mobile || null,
        userType: this.userType(),
        roleCodes: [...this.roleCodes()],
        vendorId: this.userType() === 'Vendor' ? values.vendorId || null : null,
      })
      .subscribe({
        next: (created) => {
          this.saving.set(false);
          this.creating.set(false);
          this.toasts.success('User created. Set a temporary password to let them in.');
          void this.router.navigate(['/settings/users', created.id]);
        },
        error: (error: unknown) => {
          this.saving.set(false);
          const errors = fieldErrors(error);
          const unmatched = errors ? this.form.applyServerErrors(errors) : [];
          this.createError.set(unmatched[0] ?? describeError(error, 'It could not be created.'));
        },
      });
  }
}
