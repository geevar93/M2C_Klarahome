import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  InventoryAdminService,
  ReferenceDataService,
  SupplierFilters,
  SupplierResponse,
} from '@klarahome/data-access-admin';
import {
  CellTemplate,
  DataTable,
  DataTableColumn,
  EntityDrawer,
  FilterBar,
  FilterDefinition,
  FilterValues,
  FormShell,
  PageHeader,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';

/**
 * Who this store buys from.
 *
 * **A purchase order cannot be raised until a supplier exists**, and until Step 28B there was no
 * way to add one: `InventoryAdminService` covered create, update and list, the purchase-order
 * drawer read the list, and nothing wrote to it. This screen is the missing half (deliverable 13).
 *
 * **A supplier is never deleted, only closed.** Purchase orders and goods receipts point at it, and
 * a purchase order whose supplier vanished is a receipt nobody can reconcile. `isActive` is what
 * takes one out of the picker; the history it is named in stays intact.
 *
 * **The GSTIN is optional and validated when present.** A small unregistered supplier is ordinary,
 * and refusing to record one would push the operator into a spreadsheet. What is not ordinary is a
 * malformed GSTIN reaching a purchase order, so the server checks the shape of one that is given.
 *
 * **Payment terms are in days because that is what an invoice says.** "Net 30" is a number the
 * finance team reads off the supplier's own paperwork, and storing it as a date rule rather than a
 * count would be a model of something nobody has yet asked for.
 */
@Component({
  selector: 'kh-suppliers-page',
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
    FilterBar,
    FormShell,
    Icon,
    PageHeader,
  ],
  template: `
    <kh-page-header
      heading="Suppliers"
      description="Who stock is bought from. A purchase order names one of these."
    >
      <button khButton type="button" variant="primary" (click)="startCreate()">
        <kh-icon name="plus" size="sm" />
        New supplier
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Suppliers could not be loaded">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Suppliers"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      exportMode="page"
      emptyMessage="No supplier matches this search."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        searchLabel="Search suppliers"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="name" let-row>
        <button type="button" class="link" (click)="startEdit(row)">{{ row.name }}</button>
        <span class="muted">{{ row.code }}</span>
      </ng-template>

      <ng-template khCell="contact" let-row>
        <span>{{ row.contactName ?? '—' }}</span>
        <span class="muted">{{ row.email ?? row.phone ?? 'no contact recorded' }}</span>
      </ng-template>

      <ng-template khCell="isActive" let-row>
        <kh-badge [tone]="row.isActive ? 'success' : 'warning'">
          {{ row.isActive ? 'Active' : 'Closed' }}
        </kh-badge>
      </ng-template>
    </kh-data-table>

    @if (drawerOpen()) {
      <kh-entity-drawer
        [heading]="editing() ? 'Edit supplier' : 'New supplier'"
        [subtitle]="editing()?.code ?? null"
        (closed)="drawerOpen.set(false)"
      >
        <kh-form-shell
          heading="Supplier"
          [summary]="summary()"
          [saving]="saving()"
          [dirty]="true"
          [submitLabel]="editing() ? 'Save' : 'Create'"
          (submitted)="save()"
          (cancelled)="drawerOpen.set(false)"
        >
          <kh-field label="Name" for="supplier-name" [error]="form.fields.name.error()">
            <input
              khControl
              id="supplier-name"
              type="text"
              maxlength="200"
              [value]="form.fields.name.value()"
              (input)="form.fields.name.set($any($event.target).value)"
              (touched)="form.fields.name.markTouched()"
            />
          </kh-field>

          <kh-field
            label="Code"
            for="supplier-code"
            hint="Short, and printed on purchase orders. It cannot be changed later."
            [error]="form.fields.code.error()"
          >
            <input
              khControl
              id="supplier-code"
              type="text"
              maxlength="32"
              [disabled]="!!editing()"
              [value]="form.fields.code.value()"
              (input)="form.fields.code.set($any($event.target).value)"
              (touched)="form.fields.code.markTouched()"
            />
          </kh-field>

          <div class="pair">
            <kh-field label="Contact" for="supplier-contact" [optional]="true">
              <input
                khControl
                id="supplier-contact"
                type="text"
                maxlength="120"
                [value]="form.fields.contactName.value()"
                (input)="form.fields.contactName.set($any($event.target).value)"
              />
            </kh-field>

            <kh-field label="Payment terms (days)" for="supplier-terms">
              <input
                khControl
                khNumeric
                id="supplier-terms"
                type="number"
                min="0"
                max="365"
                [value]="form.fields.paymentTermsDays.value()"
                (input)="form.fields.paymentTermsDays.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <div class="pair">
            <kh-field label="Email" for="supplier-email" [optional]="true">
              <input
                khControl
                id="supplier-email"
                type="email"
                [value]="form.fields.email.value()"
                (input)="form.fields.email.set($any($event.target).value)"
              />
            </kh-field>

            <kh-field label="Phone" for="supplier-phone" [optional]="true">
              <input
                khControl
                id="supplier-phone"
                type="tel"
                [value]="form.fields.phone.value()"
                (input)="form.fields.phone.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <kh-field
            label="GSTIN"
            for="supplier-gstin"
            hint="Optional. A small unregistered supplier is ordinary; a malformed GSTIN is not."
            [optional]="true"
          >
            <input
              khControl
              id="supplier-gstin"
              type="text"
              maxlength="15"
              [value]="form.fields.gstin.value()"
              (input)="form.fields.gstin.set($any($event.target).value.toUpperCase())"
            />
          </kh-field>

          <h3>Address</h3>
          <p class="hint">
            Optional, and worth filling in: it is what a purchase order is sent to and what a delivery is
            chased against.
          </p>

          <kh-field label="Address line 1" for="supplier-line1" [optional]="true">
            <input
              khControl
              id="supplier-line1"
              type="text"
              [value]="form.fields.line1.value()"
              (input)="form.fields.line1.set($any($event.target).value)"
            />
          </kh-field>

          <div class="pair">
            <kh-field label="City" for="supplier-city" [optional]="true">
              <input
                khControl
                id="supplier-city"
                type="text"
                [value]="form.fields.city.value()"
                (input)="form.fields.city.set($any($event.target).value)"
              />
            </kh-field>

            <kh-field label="State" for="supplier-state" [optional]="true">
              <select
                khControl
                id="supplier-state"
                [value]="form.fields.stateId.value()"
                (change)="form.fields.stateId.set($any($event.target).value)"
              >
                <option value="">Not recorded</option>
                @for (state of states(); track state.id) {
                  <option [value]="state.id">{{ state.name }} ({{ state.code }})</option>
                }
              </select>
            </kh-field>
          </div>

          <kh-field label="PIN code" for="supplier-pincode" [optional]="true">
            <input
              khControl
              khNumeric
              id="supplier-pincode"
              type="text"
              inputmode="numeric"
              maxlength="6"
              [value]="form.fields.pincode.value()"
              (input)="form.fields.pincode.set($any($event.target).value)"
            />
          </kh-field>

          @if (editing()) {
            <kh-checkbox
              label="Open for ordering"
              description="A closed supplier keeps its history and stops appearing on new purchase orders."
              inputId="supplier-active"
              [checked]="isActive()"
              (checkedChange)="isActive.set($event)"
            />
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

    .muted,
    .hint {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    h3 {
      margin-block: var(--space-4) 0;
      font-size: var(--text-sm);
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
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SuppliersPage {
  private readonly inventory = inject(InventoryAdminService);
  private readonly toasts = inject(ToastService);

  /** The platform's states, so an address names one rather than an identifier. */
  protected readonly states = toSignal(inject(ReferenceDataService).states, { initialValue: [] });

  protected readonly list = this.inventory.suppliers();
  protected readonly values = signal<FilterValues>({});
  protected readonly saving = signal(false);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly drawerOpen = signal(false);
  protected readonly editing = signal<SupplierResponse | null>(null);
  protected readonly isActive = signal(true);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('The name')], this.submitted),
    code: formField('', [required('A code')], this.submitted),
    contactName: formField('', [], this.submitted),
    email: formField('', [], this.submitted),
    phone: formField('', [], this.submitted),
    gstin: formField('', [], this.submitted),
    line1: formField('', [], this.submitted),
    city: formField('', [], this.submitted),
    stateId: formField('', [], this.submitted),
    pincode: formField('', [], this.submitted),
    paymentTermsDays: formField('30', [], this.submitted),
  });

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'activeOnly',
      label: 'Status',
      kind: 'select',
      options: [{ value: 'true', label: 'Open for ordering' }],
    },
  ];

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: SupplierResponse) => row.id;
  protected readonly rowLabel = (row: SupplierResponse) => row.name;

  protected readonly columns: readonly DataTableColumn<SupplierResponse>[] = [
    { key: 'name', label: 'Supplier', kind: 'custom' },
    { key: 'contact', label: 'Contact', kind: 'custom' },
    { key: 'gstin', label: 'GSTIN', value: (row) => row.gstin ?? '—', width: '11rem' },
    {
      key: 'paymentTermsDays',
      label: 'Terms',
      kind: 'number',
      value: (row) => row.paymentTermsDays,
      width: '6rem',
    },
    { key: 'isActive', label: 'Status', kind: 'custom', width: '8rem' },
  ];

  constructor() {
    this.list.load();
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: SupplierFilters = {
      search: values['q'],
      activeOnly: values['activeOnly'] === 'true' ? true : undefined,
    };
    this.list.setFilters(filters);
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.isActive.set(true);
    this.summary.set([]);
    this.form.reset({
      name: '',
      code: '',
      contactName: '',
      email: '',
      phone: '',
      gstin: '',
      line1: '',
      city: '',
      stateId: '',
      pincode: '',
      paymentTermsDays: '30',
    });
    this.drawerOpen.set(true);
  }

  protected startEdit(supplier: SupplierResponse): void {
    this.editing.set(supplier);
    this.isActive.set(supplier.isActive);
    this.summary.set([]);
    this.form.reset({
      name: supplier.name,
      code: supplier.code,
      contactName: supplier.contactName ?? '',
      email: supplier.email ?? '',
      phone: supplier.phone ?? '',
      gstin: supplier.gstin ?? '',
      line1: supplier.address?.line1 ?? '',
      city: supplier.address?.city ?? '',
      stateId: supplier.address?.stateId ?? '',
      pincode: supplier.address?.pincode ?? '',
      paymentTermsDays: String(supplier.paymentTermsDays),
    });
    this.drawerOpen.set(true);
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();

    // An address is sent whole or not at all. A partial one — a city with no state — is worse than
    // none on a document somebody has to post something to.
    const address =
      values.line1 && values.city && values.stateId && values.pincode
        ? {
            line1: values.line1,
            line2: null,
            city: values.city,
            stateId: values.stateId,
            pincode: values.pincode,
          }
        : null;

    this.saving.set(true);
    this.summary.set([]);

    const supplier = this.editing();
    const request = supplier
      ? this.inventory.updateSupplier(supplier.id, {
          name: values.name,
          contactName: values.contactName || null,
          email: values.email || null,
          phone: values.phone || null,
          gstin: values.gstin || null,
          address,
          paymentTermsDays: Number(values.paymentTermsDays || 0),
          isActive: this.isActive(),
        })
      : this.inventory.createSupplier({
          // The seller a supplier belongs to is the token's; a platform user's is the store's own.
          vendorId: null,
          code: values.code,
          name: values.name,
          contactName: values.contactName || null,
          email: values.email || null,
          phone: values.phone || null,
          gstin: values.gstin || null,
          address,
          paymentTermsDays: Number(values.paymentTermsDays || 0),
        });

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.drawerOpen.set(false);
        this.toasts.success(supplier ? 'Supplier saved.' : 'Supplier created.');
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
}
