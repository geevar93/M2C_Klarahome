import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  IdentityAdminService,
  PermissionGroupResponse,
  RoleResponse,
  RoleScope,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  CellTemplate,
  DataTable,
  DataTableColumn,
  EntityDrawer,
  FormShell,
  PageHeader,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';

const SCOPES: readonly { value: RoleScope; label: string; hint: string }[] = [
  {
    value: 'Platform',
    label: 'Platform',
    hint: 'The whole store. Held by staff.',
  },
  {
    value: 'Vendor',
    label: 'Seller',
    hint: "The same permissions, evaluated against the holder's own seller only.",
  },
  { value: 'Customer', label: 'Customer', hint: 'The storefront. No back office at all.' },
];

/**
 * Roles, and the permissions in them.
 *
 * **The permission catalogue is the API's.** `GET /admin/permissions` answers every code the
 * platform knows about, grouped for display, and the editor is a set of ticks over that list —
 * there is no free-text field, because a misspelt permission is a role that grants nothing and
 * reads as though it grants something.
 *
 * **Scope is not a permission, and this is where the difference is created.** A `Vendor`-scoped
 * role holding `orders.order.read` means "their own orders"; a `Platform`-scoped one holding the
 * same code means every order in the store. That single field is what the back office's
 * `platformOnly` navigation rule exists to respect (Step 26), and it is why the scope is fixed at
 * creation: changing it would silently rewrite what every holder of the role can see.
 *
 * **A system role cannot be edited.** Those are the ones the platform seeds and depends on; the
 * API refuses the write and the editor says so rather than offering a form that will be rejected.
 */
@Component({
  selector: 'kh-roles-page',
  imports: [
    Alert,
    Badge,
    Button,
    CellTemplate,
    Checkbox,
    Control,
    DataTable,
    EntityDrawer,
    Field,
    FormShell,
    HasPermission,
    Icon,
    PageHeader,
    Skeleton,
  ],
  template: `
    <kh-page-header heading="Roles" description="What a role may do, and whose data it may do it to.">
      <button
        khButton
        type="button"
        variant="primary"
        *khHasPermission="'identity.role.manage'"
        (click)="startCreate()"
      >
        <kh-icon name="plus" size="sm" />
        New role
      </button>
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="Roles could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="14rem" />
    } @else {
      <kh-data-table
        label="Roles"
        [columns]="columns"
        [rows]="roles()"
        [rowKey]="rowKey"
        [rowLabel]="rowLabel"
        exportMode="page"
        emptyMessage="No role has been created yet."
      >
        <ng-template khCell="name" let-row>
          <button type="button" class="link" (click)="startEdit(row)">{{ row.name }}</button>
          <span class="note"
            ><code>{{ row.code }}</code> · {{ row.description }}</span
          >
        </ng-template>

        <ng-template khCell="scope" let-row>
          <kh-badge [tone]="row.scope === 'Platform' ? 'primary' : 'neutral'">
            {{ scopeLabel(row.scope) }}
          </kh-badge>
          @if (row.isSystem) {
            <span class="note">Built in — not editable</span>
          }
        </ng-template>
      </kh-data-table>
    }

    @if (drawerOpen()) {
      <kh-entity-drawer
        [heading]="editing() ? 'Edit role' : 'New role'"
        [subtitle]="editing()?.code ?? null"
        (closed)="drawerOpen.set(false)"
      >
        <kh-form-shell
          heading="Role"
          [summary]="summary()"
          [saving]="saving()"
          [dirty]="true"
          [submitLabel]="editing() ? 'Save' : 'Create'"
          (submitted)="save()"
          (cancelled)="drawerOpen.set(false)"
        >
          @if (editing()?.isSystem) {
            <kh-alert tone="warning" heading="This is a built-in role">
              The platform depends on it and the API will refuse a change. It is shown so you can see what it
              grants.
            </kh-alert>
          }

          <kh-field label="Name" for="role-name" [error]="form.fields.name.error()">
            <input
              khControl
              id="role-name"
              type="text"
              maxlength="120"
              [value]="form.fields.name.value()"
              (input)="form.fields.name.set($any($event.target).value)"
              (touched)="form.fields.name.markTouched()"
            />
          </kh-field>

          @if (!editing()) {
            <kh-field
              label="Code"
              for="role-code"
              hint="Stable. Referred to when a user is created."
              [error]="form.fields.code.error()"
            >
              <input
                khControl
                id="role-code"
                type="text"
                maxlength="60"
                [value]="form.fields.code.value()"
                (input)="form.fields.code.set($any($event.target).value)"
              />
            </kh-field>

            <kh-field label="Scope" for="role-scope" [hint]="scopeHint()">
              <select
                khControl
                id="role-scope"
                [value]="scope()"
                (change)="scope.set($any($event.target).value)"
              >
                @for (choice of scopes; track choice.value) {
                  <option [value]="choice.value">{{ choice.label }}</option>
                }
              </select>
            </kh-field>
          }

          <kh-field label="What it is for" for="role-description" [error]="form.fields.description.error()">
            <input
              khControl
              id="role-description"
              type="text"
              maxlength="200"
              [value]="form.fields.description.value()"
              (input)="form.fields.description.set($any($event.target).value)"
            />
          </kh-field>

          <p class="count">
            {{ selected().length }} permission{{ selected().length === 1 ? '' : 's' }} ticked
          </p>

          @for (group of groups(); track group.group) {
            <fieldset>
              <legend>{{ humanise(group.group) }}</legend>
              @for (permission of group.permissions; track permission.code) {
                <kh-checkbox
                  [label]="permission.code"
                  [description]="permission.description"
                  [inputId]="'permission-' + permission.code"
                  [disabled]="editing()?.isSystem ?? false"
                  [checked]="selected().includes(permission.code)"
                  (checkedChange)="toggle(permission.code, $event)"
                />
              }
            </fieldset>
          }
        </kh-form-shell>
      </kh-entity-drawer>
    }
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .link {
      display: block;
      padding: 0;
      border: none;
      background: none;
      color: var(--color-link);
      font: inherit;
      font-weight: var(--weight-medium);
      text-align: start;
      cursor: pointer;
    }

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    code {
      font-family: var(--font-mono);
    }

    .count {
      margin: var(--space-4) 0 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    fieldset {
      margin-block: var(--space-3);
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    legend {
      padding-inline: var(--space-2);
      font-weight: var(--weight-medium);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RolesPage {
  private readonly identity = inject(IdentityAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly scopes = SCOPES;

  protected readonly roles = signal<readonly RoleResponse[]>([]);
  protected readonly groups = signal<readonly PermissionGroupResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly drawerOpen = signal(false);
  protected readonly editing = signal<RoleResponse | null>(null);
  protected readonly scope = signal<RoleScope>('Platform');
  protected readonly selected = signal<readonly string[]>([]);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('A name')], this.submitted),
    code: formField('', [required('A code')], this.submitted),
    description: formField('', [required('A description')], this.submitted),
  });

  protected readonly scopeHint = computed(
    () => SCOPES.find((choice) => choice.value === this.scope())?.hint ?? '',
  );

  protected readonly rowKey = (row: RoleResponse) => row.id;
  protected readonly rowLabel = (row: RoleResponse) => row.name;

  protected readonly columns: readonly DataTableColumn<RoleResponse>[] = [
    { key: 'name', label: 'Role', kind: 'custom' },
    { key: 'scope', label: 'Scope', kind: 'custom', width: '14rem' },
    {
      key: 'permissions',
      label: 'Permissions',
      kind: 'number',
      value: (row) => row.permissions.length,
      width: '9rem',
    },
  ];

  constructor() {
    this.load();
  }

  protected scopeLabel(scope: RoleScope): string {
    return SCOPES.find((choice) => choice.value === scope)?.label ?? scope;
  }

  protected humanise(group: string): string {
    const spaced = group.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[._-]+/g, ' ');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
  }

  protected toggle(code: string, on: boolean): void {
    this.selected.update((current) =>
      on ? [...new Set([...current, code])] : current.filter((entry) => entry !== code),
    );
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.scope.set('Platform');
    this.selected.set([]);
    this.summary.set([]);
    this.form.reset({ name: '', code: '', description: '' });
    this.drawerOpen.set(true);
  }

  protected startEdit(role: RoleResponse): void {
    this.editing.set(role);
    this.scope.set(role.scope);
    this.selected.set(role.permissions);
    this.summary.set([]);
    this.form.reset({ name: role.name, code: role.code, description: role.description });
    this.drawerOpen.set(true);
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    this.saving.set(true);
    this.summary.set([]);

    const existing = this.editing();
    const request = existing
      ? this.identity.updateRole(existing.id, {
          name: values.name,
          description: values.description,
          permissions: [...this.selected()],
        })
      : this.identity.createRole({
          code: values.code,
          name: values.name,
          scope: this.scope(),
          description: values.description,
          permissions: [...this.selected()],
        });

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.drawerOpen.set(false);
        this.toasts.success(existing ? 'Role saved.' : 'Role created.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const errors = fieldErrors(error);
        this.summary.set(
          errors ? this.form.applyServerErrors(errors) : [describeError(error, 'It could not be saved.')],
        );
      },
    });
  }

  private load(): void {
    this.loading.set(true);

    this.identity.roles().subscribe({
      next: (roles) => {
        this.loading.set(false);
        this.roles.set(roles);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'They could not be loaded.'));
      },
    });

    this.identity.permissions().subscribe({
      next: (groups) => this.groups.set(groups),
      // Without the catalogue the editor has nothing to tick, and that is what it will show.
      error: () => this.groups.set([]),
    });
  }
}
