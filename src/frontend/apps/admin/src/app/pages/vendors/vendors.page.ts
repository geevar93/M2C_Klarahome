import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  VendorBusinessType,
  VendorFilters,
  VendorListItem,
  VendorsAdminService,
} from '@klarahome/data-access-admin';
import {
  CellTemplate,
  DataTable,
  DataTableColumn,
  FilterBar,
  FilterDefinition,
  FilterValues,
  Modal,
  PageHeader,
} from '@klarahome/ui-admin';
import { Alert, Button, Control, Field, Icon, Rating } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, gstin, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { tableDate } from '../../core/format';
import { BUSINESS_TYPES, VENDOR_STATUSES } from './vendor-vocabulary';

/**
 * The seller directory.
 *
 * It opens on **applications awaiting a decision**, because every other status is waiting on the
 * seller rather than on the platform — an approved seller is uploading products, an active one is
 * trading, and neither needs anybody here to do anything. `Applied` and `UnderReview` are the two
 * where somebody is waiting on us.
 *
 * **Creating a seller here is the staff-initiated path**, and it captures the legal identity only:
 * a name, a constitution, a PAN and a GSTIN. Everything that makes a seller able to trade — the
 * documents, the bank account, the pickup address, the serviceable regions — is the onboarding
 * machine's, one route down, and asking for it in a create dialogue would be asking for a bank
 * account before anybody has agreed to work together.
 *
 * The screen is platform-only (`navigation.ts` marks it so) because a seller genuinely holds
 * `vendors.vendor.read` for their own record. That is the distinction `platformOnly` exists for.
 */
@Component({
  selector: 'kh-vendors-page',
  imports: [
    Alert,
    Button,
    CellTemplate,
    Control,
    DataTable,
    Field,
    FilterBar,
    Icon,
    Modal,
    PageHeader,
    Rating,
    RouterLink,
  ],
  template: `
    <kh-page-header
      heading="Sellers"
      description="Who sells on this store, and how far each application has got."
    >
      <button khButton type="button" variant="primary" (click)="startCreate()">
        <kh-icon name="plus" size="sm" />
        New seller
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Sellers could not be loaded">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Sellers"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="vendors-list"
      exportMode="page"
      emptyMessage="No seller matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        searchLabel="Search sellers"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="displayName" let-row>
        <a class="link" [routerLink]="['/vendors', row.id]">{{ row.displayName }}</a>
        <span class="note">{{ row.code }} · {{ row.legalName }}</span>
      </ng-template>

      <ng-template khCell="rating" let-row>
        @if (row.rating !== null) {
          <kh-rating [average]="row.rating" size="sm" />
        } @else {
          <span class="note">No reviews yet</span>
        }
      </ng-template>
    </kh-data-table>

    <kh-modal [open]="creating()" heading="New seller" (closed)="creating.set(false)">
      @if (createError(); as message) {
        <kh-alert tone="danger" heading="It could not be created">{{ message }}</kh-alert>
      }

      <p class="hint">
        The legal identity only. Documents, bank details and pickup addresses come later, through the seller's
        own onboarding.
      </p>

      <kh-field label="Legal name" for="vendor-legal" [error]="form.fields.legalName.error()">
        <input
          khControl
          id="vendor-legal"
          type="text"
          maxlength="200"
          [value]="form.fields.legalName.value()"
          (input)="form.fields.legalName.set($any($event.target).value)"
          (touched)="form.fields.legalName.markTouched()"
        />
      </kh-field>

      <kh-field
        label="Trading name"
        for="vendor-display"
        [optional]="true"
        hint="What shoppers see. Left blank, the legal name is used."
      >
        <input
          khControl
          id="vendor-display"
          type="text"
          maxlength="200"
          [value]="form.fields.displayName.value()"
          (input)="form.fields.displayName.set($any($event.target).value)"
        />
      </kh-field>

      <kh-field label="Constitution" for="vendor-type">
        <select
          khControl
          id="vendor-type"
          [value]="businessType()"
          (change)="businessType.set($any($event.target).value)"
        >
          @for (choice of businessTypes; track choice.value) {
            <option [value]="choice.value">{{ choice.label }}</option>
          }
        </select>
      </kh-field>

      <div class="row">
        <kh-field label="PAN" for="vendor-pan" [optional]="true">
          <input
            khControl
            id="vendor-pan"
            type="text"
            maxlength="10"
            [value]="form.fields.pan.value()"
            (input)="form.fields.pan.set($any($event.target).value)"
          />
        </kh-field>
        <kh-field label="GSTIN" for="vendor-gstin" [optional]="true" [error]="form.fields.gstin.error()">
          <input
            khControl
            id="vendor-gstin"
            type="text"
            maxlength="15"
            [value]="form.fields.gstin.value()"
            (input)="form.fields.gstin.set($any($event.target).value)"
          />
        </kh-field>
      </div>

      <div class="row">
        <kh-field label="Support email" for="vendor-email" [optional]="true">
          <input
            khControl
            id="vendor-email"
            type="email"
            [value]="form.fields.supportEmail.value()"
            (input)="form.fields.supportEmail.set($any($event.target).value)"
          />
        </kh-field>
        <kh-field label="Support phone" for="vendor-phone" [optional]="true">
          <input
            khControl
            id="vendor-phone"
            type="tel"
            [value]="form.fields.supportPhone.value()"
            (input)="form.fields.supportPhone.set($any($event.target).value)"
          />
        </kh-field>
      </div>

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

    .hint {
      margin: 0 0 var(--space-4);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .row {
      display: flex;
      gap: var(--space-3);
    }

    .row > kh-field {
      flex: 1;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VendorsPage {
  private readonly vendors = inject(VendorsAdminService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly businessTypes = BUSINESS_TYPES;

  protected readonly list = this.vendors.vendors();
  protected readonly values = signal<FilterValues>({ status: 'Applied' });
  protected readonly creating = signal(false);
  protected readonly saving = signal(false);
  protected readonly createError = signal<string | null>(null);
  protected readonly businessType = signal<VendorBusinessType>('SoleProprietorship');

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    legalName: formField('', [required('The legal name')], this.submitted),
    displayName: formField('', [], this.submitted),
    pan: formField('', [], this.submitted),
    gstin: formField('', [gstin], this.submitted),
    supportEmail: formField('', [], this.submitted),
    supportPhone: formField('', [], this.submitted),
  });

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: VendorListItem) => row.id;
  protected readonly rowLabel = (row: VendorListItem) => row.displayName;

  protected readonly columns: readonly DataTableColumn<VendorListItem>[] = [
    { key: 'displayName', label: 'Seller', kind: 'custom' },
    {
      key: 'status',
      label: 'Status',
      kind: 'badge',
      value: (row) => row.status,
      width: '10rem',
    },
    { key: 'rating', label: 'Rating', kind: 'custom', width: '10rem' },
    {
      key: 'onboardedAt',
      label: 'Trading since',
      kind: 'date',
      value: (row) => tableDate(row.onboardedAt) || 'Not yet',
      width: '10rem',
    },
    {
      key: 'commissionPlanId',
      label: 'Commission plan',
      value: (row) => row.commissionPlanId ?? 'The default',
      hiddenByDefault: true,
    },
    {
      key: 'createdAt',
      label: 'Applied',
      kind: 'date',
      value: (row) => tableDate(row.createdAt),
      hiddenByDefault: true,
    },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'status',
      label: 'Status',
      kind: 'select',
      options: VENDOR_STATUSES.map((entry) => ({ value: entry.value, label: entry.label })),
    },
  ];

  constructor() {
    this.list.setFilters({ status: 'Applied' });
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: VendorFilters = { status: values['status'], search: values['q'] };
    this.list.setFilters(filters);
  }

  protected startCreate(): void {
    this.createError.set(null);
    this.businessType.set('SoleProprietorship');
    this.form.reset({
      legalName: '',
      displayName: '',
      pan: '',
      gstin: '',
      supportEmail: '',
      supportPhone: '',
    });
    this.creating.set(true);
  }

  protected create(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    this.saving.set(true);
    this.createError.set(null);

    this.vendors
      .createVendor({
        legalName: values.legalName,
        displayName: values.displayName || null,
        businessType: this.businessType(),
        code: null,
        slug: null,
        pan: values.pan || null,
        gstin: values.gstin || null,
        registeredAddress: null,
        supportEmail: values.supportEmail || null,
        supportPhone: values.supportPhone || null,
      })
      .subscribe({
        next: (created) => {
          this.saving.set(false);
          this.creating.set(false);
          this.toasts.success('Seller created.');
          void this.router.navigate(['/vendors', created.id]);
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
