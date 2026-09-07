import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { InventoryAdminService, WarehouseFilters, WarehouseResponse } from '@klarahome/data-access-admin';
import {
  CellTemplate,
  ConfirmDialog,
  DataTable,
  DataTableColumn,
  EntityDrawer,
  FilterBar,
  FilterValues,
  FormShell,
  PageHeader,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, pincode, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';

/**
 * Where stock is kept, and which one ships first.
 *
 * **Priority is the field that does something.** When an order can be met from more than one
 * warehouse, the lowest priority number wins (Step 11) — so this is not a label, it is the
 * allocation rule, and it is why the column is sorted by it rather than by name.
 *
 * **The PIN code is not the address's PIN code by accident.** It is what serviceability and the
 * rate card are computed from: the zone a parcel travels through is decided by this six-digit
 * number and the destination's (Step 16). Getting it wrong prices every shipment out of this
 * warehouse wrongly, which is why it is validated here as well as by the API.
 *
 * Deleting is offered only for a warehouse with nothing in it. `stockItemCount` is on the row for
 * that reason — the API refuses the rest, and this is the number that says which case you are in.
 */
@Component({
  selector: 'kh-warehouses-page',
  imports: [
    Alert,
    Badge,
    Button,
    CellTemplate,
    Checkbox,
    ConfirmDialog,
    Control,
    DataTable,
    EntityDrawer,
    Field,
    FilterBar,
    FormShell,
    Icon,
    PageHeader,
  ],
  template: `
    <kh-page-header
      heading="Warehouses"
      description="Where stock sits. The lowest priority number ships first when both could."
    >
      <button khButton type="button" variant="primary" (click)="startCreate()">
        <kh-icon name="plus" size="sm" />
        New warehouse
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Warehouses could not be loaded">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Warehouses"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      exportMode="page"
      emptyMessage="No warehouse matches this search."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="[]"
        [values]="values()"
        searchLabel="Search warehouses"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="name" let-row>
        <button type="button" class="link" (click)="startEdit(row)">{{ row.name }}</button>
        <span class="code">{{ row.code }}</span>
      </ng-template>

      <ng-template khCell="isActive" let-row>
        <kh-badge [tone]="row.isActive ? 'success' : 'warning'">
          {{ row.isActive ? 'Active' : 'Closed' }}
        </kh-badge>
      </ng-template>
    </kh-data-table>

    @if (drawerOpen()) {
      <kh-entity-drawer
        [heading]="editing() ? 'Edit warehouse' : 'New warehouse'"
        [subtitle]="editing()?.code ?? null"
        (closed)="drawerOpen.set(false)"
      >
        <kh-form-shell
          heading="Warehouse"
          [summary]="summary()"
          [saving]="saving()"
          [dirty]="true"
          [submitLabel]="editing() ? 'Save' : 'Create'"
          (submitted)="save()"
          (cancelled)="drawerOpen.set(false)"
        >
          <kh-field label="Name" for="warehouse-name" [error]="form.fields.name.error()">
            <input
              khControl
              id="warehouse-name"
              type="text"
              maxlength="120"
              [value]="form.fields.name.value()"
              (input)="form.fields.name.set($any($event.target).value)"
              (touched)="form.fields.name.markTouched()"
            />
          </kh-field>

          <kh-field
            label="Code"
            for="warehouse-code"
            hint="Short, and printed on pick lists. It cannot be changed later."
            [error]="form.fields.code.error()"
          >
            <input
              khControl
              id="warehouse-code"
              type="text"
              maxlength="20"
              [disabled]="!!editing()"
              [value]="form.fields.code.value()"
              (input)="form.fields.code.set($any($event.target).value)"
              (touched)="form.fields.code.markTouched()"
            />
          </kh-field>

          <kh-field
            label="PIN code"
            for="warehouse-pincode"
            hint="Every shipping rate out of this warehouse is worked out from it."
            [error]="form.fields.pincode.error()"
          >
            <input
              khControl
              khNumeric
              id="warehouse-pincode"
              type="text"
              inputmode="numeric"
              maxlength="6"
              [value]="form.fields.pincode.value()"
              (input)="form.fields.pincode.set($any($event.target).value)"
              (touched)="form.fields.pincode.markTouched()"
            />
          </kh-field>

          <kh-field label="Address line 1" for="warehouse-line1" [error]="form.fields.line1.error()">
            <input
              khControl
              id="warehouse-line1"
              type="text"
              [value]="form.fields.line1.value()"
              (input)="form.fields.line1.set($any($event.target).value)"
              (touched)="form.fields.line1.markTouched()"
            />
          </kh-field>

          <kh-field label="Address line 2" for="warehouse-line2" [optional]="true">
            <input
              khControl
              id="warehouse-line2"
              type="text"
              [value]="form.fields.line2.value()"
              (input)="form.fields.line2.set($any($event.target).value)"
            />
          </kh-field>

          <div class="pair">
            <kh-field label="City" for="warehouse-city" [error]="form.fields.city.error()">
              <input
                khControl
                id="warehouse-city"
                type="text"
                [value]="form.fields.city.value()"
                (input)="form.fields.city.set($any($event.target).value)"
                (touched)="form.fields.city.markTouched()"
              />
            </kh-field>

            <kh-field
              label="State"
              for="warehouse-state"
              hint="The state's own identifier, from the platform's reference data."
              [error]="form.fields.stateId.error()"
            >
              <input
                khControl
                id="warehouse-state"
                type="text"
                [value]="form.fields.stateId.value()"
                (input)="form.fields.stateId.set($any($event.target).value)"
                (touched)="form.fields.stateId.markTouched()"
              />
            </kh-field>
          </div>

          <kh-field
            label="Priority"
            for="warehouse-priority"
            hint="Lower ships first when an order could come from either."
          >
            <input
              khControl
              khNumeric
              id="warehouse-priority"
              type="number"
              min="0"
              [value]="form.fields.priority.value()"
              (input)="form.fields.priority.set($any($event.target).value)"
            />
          </kh-field>

          @if (editing()) {
            <kh-checkbox
              label="Open for picking"
              description="A closed warehouse keeps its stock but is not allocated from."
              inputId="warehouse-active"
              [checked]="isActive()"
              (checkedChange)="isActive.set($event)"
            />
          }
        </kh-form-shell>

        <div slot="footer">
          @if (editing(); as current) {
            <p class="counts">{{ current.stockItemCount }} stock items held here.</p>
            <button
              khButton
              type="button"
              size="sm"
              variant="danger"
              [disabled]="current.stockItemCount > 0"
              [attr.title]="current.stockItemCount > 0 ? 'Move its stock somewhere else first.' : null"
              (click)="deleting.set(true)"
            >
              Delete
            </button>
          }
        </div>
      </kh-entity-drawer>
    }

    <kh-confirm-dialog
      [open]="deleting()"
      heading="Delete this warehouse"
      message="It holds no stock, so nothing is lost — but its code cannot be reused on the pick lists already printed."
      confirmLabel="Delete"
      [confirmPhrase]="editing()?.code ?? null"
      [busy]="saving()"
      (confirmed)="remove()"
      (cancelled)="deleting.set(false)"
    />
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

    .code,
    .counts {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .code {
      display: block;
    }

    .pair {
      display: grid;
      gap: var(--space-3);
    }

    @media (min-width: 40rem) {
      .pair {
        grid-template-columns: 1fr 1fr;
      }
    }

    [slot='footer'] {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      justify-content: space-between;
      inline-size: 100%;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WarehousesPage {
  private readonly inventory = inject(InventoryAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly list = this.inventory.warehouses();
  protected readonly values = signal<FilterValues>({});
  protected readonly saving = signal(false);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly drawerOpen = signal(false);
  protected readonly deleting = signal(false);
  protected readonly editing = signal<WarehouseResponse | null>(null);
  protected readonly isActive = signal(true);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('The name')], this.submitted),
    code: formField('', [required('A code')], this.submitted),
    pincode: formField('', [required('The PIN code'), pincode], this.submitted),
    line1: formField('', [required('The address')], this.submitted),
    line2: formField('', [], this.submitted),
    city: formField('', [required('The city')], this.submitted),
    stateId: formField('', [required('The state')], this.submitted),
    priority: formField('0', [], this.submitted),
  });

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: WarehouseResponse) => row.id;
  protected readonly rowLabel = (row: WarehouseResponse) => row.name;

  protected readonly columns: readonly DataTableColumn<WarehouseResponse>[] = [
    { key: 'name', label: 'Warehouse', kind: 'custom' },
    { key: 'pincode', label: 'PIN code', value: (row) => row.pincode, width: '8rem' },
    { key: 'priority', label: 'Priority', kind: 'number', value: (row) => row.priority },
    { key: 'stockItemCount', label: 'Stock items', kind: 'number', value: (row) => row.stockItemCount },
    { key: 'isActive', label: 'Status', kind: 'custom', width: '8rem' },
  ];

  constructor() {
    this.list.load();
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: WarehouseFilters = { search: values['q'] };
    this.list.setFilters(filters);
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.isActive.set(true);
    this.summary.set([]);
    this.form.reset({
      name: '',
      code: '',
      pincode: '',
      line1: '',
      line2: '',
      city: '',
      stateId: '',
      priority: '0',
    });
    this.drawerOpen.set(true);
  }

  protected startEdit(warehouse: WarehouseResponse): void {
    this.editing.set(warehouse);
    this.isActive.set(warehouse.isActive);
    this.summary.set([]);
    this.form.reset({
      name: warehouse.name,
      code: warehouse.code,
      pincode: warehouse.pincode,
      line1: warehouse.address?.line1 ?? '',
      line2: warehouse.address?.line2 ?? '',
      city: warehouse.address?.city ?? '',
      stateId: warehouse.address?.stateId ?? '',
      priority: String(warehouse.priority),
    });
    this.drawerOpen.set(true);
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    const address = {
      line1: values.line1,
      line2: values.line2 || null,
      city: values.city,
      stateId: values.stateId,
      pincode: values.pincode,
    };

    this.saving.set(true);
    this.summary.set([]);

    const warehouse = this.editing();
    const request = warehouse
      ? this.inventory.updateWarehouse(warehouse.id, {
          name: values.name,
          pincode: values.pincode,
          address,
          priority: Number(values.priority || 0),
          isActive: this.isActive(),
        })
      : this.inventory.createWarehouse({
          // The seller a warehouse belongs to is the token's; a platform user's is the store's own.
          vendorId: null,
          code: values.code,
          name: values.name,
          pincode: values.pincode,
          address,
          priority: Number(values.priority || 0),
        });

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.drawerOpen.set(false);
        this.toasts.success(warehouse ? 'Warehouse saved.' : 'Warehouse created.');
        this.list.refresh();
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

  protected remove(): void {
    const warehouse = this.editing();
    if (!warehouse) return;

    this.saving.set(true);
    this.inventory.deleteWarehouse(warehouse.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleting.set(false);
        this.drawerOpen.set(false);
        this.toasts.success('Warehouse deleted.');
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.deleting.set(false);
        this.summary.set([describeError(error, 'That warehouse could not be deleted.')]);
      },
    });
  }
}
