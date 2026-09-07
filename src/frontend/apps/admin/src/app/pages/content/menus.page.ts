import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ContentAdminService, MenuSummaryResponse } from '@klarahome/data-access-admin';
import { CellTemplate, DataTable, DataTableColumn, Modal, PageHeader } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';

/**
 * The storefront's menus.
 *
 * A short screen, because there are three or four menus in a store and the work is in their items.
 * What it does is the two things a list of menus is for: **finding the one you meant**, by the code
 * the storefront asks for it by, and **making a new one**.
 *
 * The code is asked for on creation and is not editable afterwards. The storefront fetches a menu
 * by code (`GET /store/menus/{code}`), so renaming one would be renaming an API path that a
 * deployed page is already calling — which is a deployment, not an edit.
 *
 * Menus are not paged: the endpoint answers the whole list, because a store with enough menus to
 * need a cursor has a navigation problem rather than a paging one.
 */
@Component({
  selector: 'kh-menus-page',
  imports: [
    Alert,
    Badge,
    Button,
    CellTemplate,
    Control,
    DataTable,
    Field,
    Icon,
    Modal,
    PageHeader,
    RouterLink,
    Skeleton,
  ],
  template: `
    <kh-page-header heading="Menus" description="The navigation the storefront draws, and what is in it.">
      <button khButton type="button" variant="primary" (click)="startCreate()">
        <kh-icon name="plus" size="sm" />
        New menu
      </button>
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="Menus could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="12rem" />
    } @else {
      <kh-data-table
        label="Menus"
        [columns]="columns"
        [rows]="menus()"
        [rowKey]="rowKey"
        [rowLabel]="rowLabel"
        exportMode="page"
        emptyMessage="No menu has been created yet."
      >
        <ng-template khCell="name" let-row>
          <a class="link" [routerLink]="['/content/menus', row.id]">{{ row.name }}</a>
          <span class="note">
            <code>{{ row.code }}</code> · {{ row.itemCount }} item{{ row.itemCount === 1 ? '' : 's' }}
          </span>
        </ng-template>

        <ng-template khCell="isActive" let-row>
          <kh-badge [tone]="row.isActive ? 'success' : 'neutral'">
            {{ row.isActive ? 'Shown' : 'Hidden' }}
          </kh-badge>
        </ng-template>
      </kh-data-table>
    }

    <kh-modal [open]="creating()" heading="New menu" (closed)="creating.set(false)">
      @if (createError(); as message) {
        <kh-alert tone="danger" heading="It could not be created">{{ message }}</kh-alert>
      }

      <kh-field label="Name" for="menu-name" [error]="form.fields.name.error()">
        <input
          khControl
          id="menu-name"
          type="text"
          maxlength="120"
          [value]="form.fields.name.value()"
          (input)="form.fields.name.set($any($event.target).value)"
          (touched)="form.fields.name.markTouched()"
        />
      </kh-field>

      <kh-field
        label="Code"
        for="menu-code"
        hint="How the storefront asks for it. Not editable afterwards — a deployed page is already calling it."
        [error]="form.fields.code.error()"
      >
        <input
          khControl
          id="menu-code"
          type="text"
          maxlength="60"
          [value]="form.fields.code.value()"
          (input)="form.fields.code.set($any($event.target).value)"
        />
      </kh-field>

      <kh-field label="Placement" for="menu-placement" [optional]="true" hint="Header, footer, drawer.">
        <input
          khControl
          id="menu-placement"
          type="text"
          maxlength="60"
          [value]="form.fields.placement.value()"
          (input)="form.fields.placement.set($any($event.target).value)"
        />
      </kh-field>

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

    code {
      font-family: var(--font-mono);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MenusPage {
  private readonly content = inject(ContentAdminService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly menus = signal<readonly MenuSummaryResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);

  protected readonly creating = signal(false);
  protected readonly saving = signal(false);
  protected readonly createError = signal<string | null>(null);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('A name')], this.submitted),
    code: formField('', [required('A code')], this.submitted),
    placement: formField('', [], this.submitted),
  });

  protected readonly rowKey = (row: MenuSummaryResponse) => row.id;
  protected readonly rowLabel = (row: MenuSummaryResponse) => row.name;

  protected readonly columns: readonly DataTableColumn<MenuSummaryResponse>[] = [
    { key: 'name', label: 'Menu', kind: 'custom' },
    { key: 'placement', label: 'Placement', value: (row) => row.placement ?? '—', width: '12rem' },
    { key: 'isActive', label: 'State', kind: 'custom', width: '8rem' },
    {
      key: 'updatedAt',
      label: 'Edited',
      kind: 'date',
      value: (row) => tableDateTime(row.updatedAt),
      width: '14rem',
    },
  ];

  constructor() {
    this.load();
  }

  protected startCreate(): void {
    this.createError.set(null);
    this.form.reset({ name: '', code: '', placement: '' });
    this.creating.set(true);
  }

  protected create(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    this.saving.set(true);
    this.createError.set(null);

    this.content
      .createMenu({ code: values.code, name: values.name, placement: values.placement || null })
      .subscribe({
        next: (created) => {
          this.saving.set(false);
          this.creating.set(false);
          this.toasts.success('Menu created.');
          void this.router.navigate(['/content/menus', created.id]);
        },
        error: (error: unknown) => {
          this.saving.set(false);
          const errors = fieldErrors(error);
          const unmatched = errors ? this.form.applyServerErrors(errors) : [];
          this.createError.set(unmatched[0] ?? describeError(error, 'It could not be created.'));
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.content.menus().subscribe({
      next: (menus) => {
        this.loading.set(false);
        this.menus.set(menus);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'Menus could not be loaded.'));
      },
    });
  }
}
