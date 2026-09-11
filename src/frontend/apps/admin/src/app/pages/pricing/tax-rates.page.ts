import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  PricingAdminService,
  TaxRateFilters,
  TaxRateResolutionResponse,
  TaxRateResponse,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  CellTemplate,
  ConfirmDialog,
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
import { tableDate } from '../../core/format';

/**
 * GST rates, by HSN code.
 *
 * **A rate has a window, and windows are how a rate change is staged.** The row that applies to an
 * invoice is the one whose window contains the invoice's date — not the newest row, and not the
 * active one. So this screen never asks "what is the rate for 6302"; it asks "what is the rate for
 * 6302 on this date", which is what the resolver beside the table does, and what the effective-from
 * column exists to make visible.
 *
 * That is also why editing a rate does not change history: the rows are the record an invoice was
 * raised against. Ending one row's window and adding another is the way to change a rate, and the
 * drawer's own wording says so — an operator who edits the live row instead has quietly changed
 * what last quarter's invoices would recompute to.
 *
 * The cess column is separate from the rate because it is a separate levy with its own base, and
 * adding the two together — which a single "total tax" column invites — is wrong on every product
 * that carries one.
 */
@Component({
  selector: 'kh-tax-rates-page',
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
    HasPermission,
    Icon,
    PageHeader,
  ],
  template: `
    <kh-page-header heading="Tax rates" description="The GST rate each HSN code carries, and from when.">
      <button
        khButton
        type="button"
        variant="primary"
        *khHasPermission="'pricing.tax-rate.manage'"
        (click)="startCreate()"
      >
        <kh-icon name="plus" size="sm" />
        New rate
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Tax rates could not be loaded">{{ message }}</kh-alert>
    }

    <div class="layout">
      <kh-data-table
        label="Tax rates"
        [columns]="columns"
        [rows]="list.rows()"
        [rowKey]="rowKey"
        [rowLabel]="rowLabel"
        [loading]="list.loading()"
        [page]="page()"
        exportMode="page"
        emptyMessage="No tax rate matches these filters."
        (nextPage)="list.next()"
        (previousPage)="list.previous()"
      >
        <kh-filter-bar
          slot="filters"
          [filters]="filters"
          [values]="values()"
          [searchable]="false"
          (changed)="applyFilters($event)"
        />

        <ng-template khCell="hsnCode" let-row>
          <button type="button" class="link" (click)="startEdit(row)">{{ row.hsnCode }}</button>
          @if (row.description) {
            <span class="note">{{ row.description }}</span>
          }
        </ng-template>

        <ng-template khCell="window" let-row>
          <kh-badge [tone]="row.isActive ? 'success' : 'neutral'">
            {{ row.isActive ? 'Active' : 'Retired' }}
          </kh-badge>
          <span class="note">
            {{ tableDate(row.effectiveFrom) }} →
            {{ row.effectiveTo ? tableDate(row.effectiveTo) : 'no end' }}
          </span>
        </ng-template>
      </kh-data-table>

      <aside class="panel">
        <h2>What rate applies?</h2>
        <p class="hint">
          Asks the engine for one HSN code on one date, which is the only question with a single answer —
          several rows can overlap while a change is being staged.
        </p>

        <kh-field label="HSN code" for="resolve-hsn">
          <input
            khControl
            id="resolve-hsn"
            type="text"
            [value]="resolveHsn()"
            (input)="resolveHsn.set($any($event.target).value)"
          />
        </kh-field>

        <kh-field label="On this date" for="resolve-date" [optional]="true" hint="Blank means today.">
          <input
            khControl
            id="resolve-date"
            type="date"
            [value]="resolveDate()"
            (input)="resolveDate.set($any($event.target).value)"
          />
        </kh-field>

        <button khButton type="button" [disabled]="resolving()" (click)="resolve()">
          {{ resolving() ? 'Asking…' : 'Resolve' }}
        </button>

        @if (resolveError(); as message) {
          <kh-alert tone="danger" heading="It could not be resolved">{{ message }}</kh-alert>
        }

        @if (resolved(); as answer) {
          <dl class="answer">
            <dt>GST</dt>
            <dd>{{ answer.rate }}%</dd>
            <dt>Cess</dt>
            <dd>{{ answer.cessRate }}%</dd>
            <dt>As of</dt>
            <dd>{{ tableDate(answer.asOf) }}</dd>
          </dl>
          @if (!answer.taxRateId) {
            <kh-alert tone="warning" heading="No row matched">
              Nothing is declared for {{ answer.hsnCode }} on that date, so the platform's default was used.
              An invoice raised today would carry it.
            </kh-alert>
          }
        }
      </aside>
    </div>

    @if (drawerOpen()) {
      <kh-entity-drawer
        [heading]="editing() ? 'Edit tax rate' : 'New tax rate'"
        [subtitle]="editing()?.hsnCode ?? null"
        (closed)="drawerOpen.set(false)"
      >
        <kh-form-shell
          heading="Tax rate"
          description="Changing a live row changes what past invoices would recompute to. To change a rate, end this row's window and add another."
          [summary]="summary()"
          [saving]="saving()"
          [dirty]="true"
          [submitLabel]="editing() ? 'Save' : 'Create'"
          (submitted)="save()"
          (cancelled)="drawerOpen.set(false)"
        >
          @if (!editing()) {
            <kh-field label="HSN code" for="tax-hsn" [error]="form.fields.hsnCode.error()">
              <input
                khControl
                id="tax-hsn"
                type="text"
                maxlength="12"
                [value]="form.fields.hsnCode.value()"
                (input)="form.fields.hsnCode.set($any($event.target).value)"
                (touched)="form.fields.hsnCode.markTouched()"
              />
            </kh-field>
          }

          <kh-field label="Description" for="tax-description" [optional]="true">
            <input
              khControl
              id="tax-description"
              type="text"
              maxlength="200"
              [value]="form.fields.description.value()"
              (input)="form.fields.description.set($any($event.target).value)"
            />
          </kh-field>

          <div class="row">
            <kh-field label="GST rate (%)" for="tax-rate" [error]="form.fields.rate.error()">
              <input
                khControl
                id="tax-rate"
                type="number"
                min="0"
                max="100"
                step="0.01"
                [value]="form.fields.rate.value()"
                (input)="form.fields.rate.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Cess (%)" for="tax-cess" hint="Zero for most goods.">
              <input
                khControl
                id="tax-cess"
                type="number"
                min="0"
                max="100"
                step="0.01"
                [value]="form.fields.cessRate.value()"
                (input)="form.fields.cessRate.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <div class="row">
            <kh-field label="Effective from" for="tax-from" [error]="form.fields.effectiveFrom.error()">
              <input
                khControl
                id="tax-from"
                type="date"
                [value]="form.fields.effectiveFrom.value()"
                (input)="form.fields.effectiveFrom.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Effective to" for="tax-to" [optional]="true" hint="Blank means open-ended.">
              <input
                khControl
                id="tax-to"
                type="date"
                [value]="effectiveTo()"
                (input)="effectiveTo.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          @if (editing()) {
            <kh-checkbox
              label="Active"
              inputId="tax-active"
              [checked]="isActive()"
              (checkedChange)="isActive.set($event)"
            />
          }
        </kh-form-shell>

        <div slot="footer">
          @if (editing()) {
            <button khButton type="button" size="sm" variant="danger" (click)="deleting.set(true)">
              Delete
            </button>
          }
        </div>
      </kh-entity-drawer>
    }

    <kh-confirm-dialog
      [open]="deleting()"
      heading="Delete this tax rate"
      message="Invoices already raised keep the rate they were raised with. Deleting removes the row future invoices would resolve against."
      confirmLabel="Delete"
      [confirmPhrase]="editing()?.hsnCode ?? null"
      [busy]="saving()"
      (confirmed)="remove()"
      (cancelled)="deleting.set(false)"
    />
  `,
  styles: `
    kh-alert {
      margin-block: var(--space-4);
    }

    .layout {
      display: grid;
      gap: var(--space-6);
      grid-template-columns: minmax(0, 1fr);
    }

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: minmax(0, 3fr) minmax(18rem, 1fr);
        align-items: start;
      }
    }

    .panel {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .panel h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
    }

    .hint {
      margin: 0 0 var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
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

    .row {
      display: flex;
      gap: var(--space-3);
    }

    .row > kh-field {
      flex: 1;
    }

    .answer {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: var(--space-1) var(--space-4);
      margin-block-start: var(--space-4);
    }

    .answer dt {
      color: var(--color-text-muted);
    }

    .answer dd {
      margin: 0;
      font-variant-numeric: tabular-nums;
      text-align: end;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TaxRatesPage {
  private readonly pricing = inject(PricingAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly tableDate = tableDate;

  protected readonly list = this.pricing.taxRates();
  protected readonly values = signal<FilterValues>({});
  protected readonly saving = signal(false);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly drawerOpen = signal(false);
  protected readonly deleting = signal(false);
  protected readonly editing = signal<TaxRateResponse | null>(null);
  protected readonly effectiveTo = signal('');
  protected readonly isActive = signal(true);

  protected readonly resolveHsn = signal('');
  protected readonly resolveDate = signal('');
  protected readonly resolving = signal(false);
  protected readonly resolveError = signal<string | null>(null);
  protected readonly resolved = signal<TaxRateResolutionResponse | null>(null);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    hsnCode: formField('', [required('An HSN code')], this.submitted),
    description: formField('', [], this.submitted),
    rate: formField('0', [required('A rate')], this.submitted),
    cessRate: formField('0', [], this.submitted),
    effectiveFrom: formField('', [required('A start date')], this.submitted),
  });

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: TaxRateResponse) => row.id;
  protected readonly rowLabel = (row: TaxRateResponse) => row.hsnCode;

  protected readonly columns: readonly DataTableColumn<TaxRateResponse>[] = [
    { key: 'hsnCode', label: 'HSN', kind: 'custom', width: '14rem' },
    { key: 'rate', label: 'GST', kind: 'number', value: (row) => `${row.rate}%`, width: '7rem' },
    { key: 'cessRate', label: 'Cess', kind: 'number', value: (row) => `${row.cessRate}%`, width: '7rem' },
    { key: 'window', label: 'Applies', kind: 'custom', width: '16rem' },
    {
      key: 'createdAt',
      label: 'Added',
      kind: 'date',
      value: (row) => tableDate(row.createdAt),
      hiddenByDefault: true,
    },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    { key: 'activeOnly', label: 'State', kind: 'select', options: [{ value: 'true', label: 'Active only' }] },
  ];

  constructor() {
    this.list.load();
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: TaxRateFilters = {
      hsnCode: values['hsnCode'],
      activeOnly: values['activeOnly'] === 'true' ? true : undefined,
    };
    this.list.setFilters(filters);
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.effectiveTo.set('');
    this.isActive.set(true);
    this.summary.set([]);
    this.form.reset({
      hsnCode: '',
      description: '',
      rate: '0',
      cessRate: '0',
      effectiveFrom: new Date().toISOString().slice(0, 10),
    });
    this.drawerOpen.set(true);
  }

  protected startEdit(row: TaxRateResponse): void {
    this.editing.set(row);
    this.effectiveTo.set(row.effectiveTo ? row.effectiveTo.slice(0, 10) : '');
    this.isActive.set(row.isActive);
    this.summary.set([]);
    this.form.reset({
      hsnCode: row.hsnCode,
      description: row.description ?? '',
      rate: String(row.rate),
      cessRate: String(row.cessRate),
      effectiveFrom: row.effectiveFrom.slice(0, 10),
    });
    this.drawerOpen.set(true);
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    const from = new Date(values.effectiveFrom).toISOString();
    const to = this.effectiveTo() ? new Date(this.effectiveTo()).toISOString() : null;

    this.saving.set(true);
    this.summary.set([]);

    const existing = this.editing();
    const request = existing
      ? this.pricing.updateTaxRate(existing.id, {
          description: values.description || null,
          rate: Number(values.rate) || 0,
          cessRate: Number(values.cessRate) || 0,
          effectiveFrom: from,
          effectiveTo: to,
          isActive: this.isActive(),
        })
      : this.pricing.createTaxRate({
          hsnCode: values.hsnCode,
          description: values.description || null,
          rate: Number(values.rate) || 0,
          cessRate: Number(values.cessRate) || 0,
          effectiveFrom: from,
          effectiveTo: to,
        });

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.drawerOpen.set(false);
        this.toasts.success(existing ? 'Tax rate saved.' : 'Tax rate created.');
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
    const existing = this.editing();
    if (!existing) return;

    this.saving.set(true);
    this.pricing.deleteTaxRate(existing.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleting.set(false);
        this.drawerOpen.set(false);
        this.toasts.success('Tax rate deleted.');
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.deleting.set(false);
        this.summary.set([describeError(error, 'It could not be deleted.')]);
      },
    });
  }

  protected resolve(): void {
    const hsn = this.resolveHsn().trim();
    if (!hsn) {
      this.resolveError.set('Give an HSN code to resolve.');
      return;
    }

    this.resolving.set(true);
    this.resolveError.set(null);

    const asOf = this.resolveDate() ? new Date(this.resolveDate()).toISOString() : undefined;
    this.pricing.resolveTaxRate(hsn, asOf).subscribe({
      next: (answer) => {
        this.resolving.set(false);
        this.resolved.set(answer);
      },
      error: (error: unknown) => {
        this.resolving.set(false);
        this.resolved.set(null);
        this.resolveError.set(describeError(error, 'No rate could be resolved.'));
      },
    });
  }
}
