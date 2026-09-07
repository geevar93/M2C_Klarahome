import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  CreatePriceListBody,
  PriceListFilters,
  PriceListResponse,
  PricingAdminService,
  UpdatePriceListBody,
} from '@klarahome/data-access-admin';
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
import { Alert, Badge, Button, Control, Field, Icon } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';
import { PRICE_LIST_TYPES } from './promotion-vocabulary';

/**
 * Price lists — the standing prices, and the ones that override them.
 *
 * **Overlap is the design, not a mistake.** A listing can sit in a base list, a sale list and a
 * seller's own list at once; `priority` decides which wins, and the window decides whether a list
 * is in play at all. So the two fields this screen makes prominent are priority and the window,
 * and the drawer says which way priority runs — because "higher priority" is ambiguous and getting
 * it backwards means a sale that never happens.
 *
 * **Deactivating and deleting are different answers.** Deactivating takes a list out of the
 * resolution without losing the nine thousand rows in it; deleting is for a list that was a
 * mistake. The API refuses a delete that would strand prices, so the destructive case is already
 * closed on the server — the typed confirmation here is for the other one.
 *
 * The rows themselves are one route down: a list of prices is a different screen from a list of
 * lists, and putting nine thousand of them behind an accordion here would be a table nobody can
 * filter.
 */
@Component({
  selector: 'kh-price-lists-page',
  imports: [
    Alert,
    Badge,
    Button,
    CellTemplate,
    ConfirmDialog,
    Control,
    DataTable,
    EntityDrawer,
    Field,
    FilterBar,
    FormShell,
    Icon,
    PageHeader,
    RouterLink,
  ],
  template: `
    <kh-page-header
      heading="Price lists"
      description="What a listing sells for, and which list wins when several claim it."
    >
      <button khButton type="button" variant="primary" (click)="startCreate()">
        <kh-icon name="plus" size="sm" />
        New price list
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Price lists could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not take">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Price lists"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="price-lists"
      exportMode="page"
      emptyMessage="No price list matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        searchLabel="Search price lists"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="name" let-row>
        <a class="link" [routerLink]="['/price-lists', row.id]">{{ row.name }}</a>
        <span class="note"
          >{{ row.code }} · {{ row.itemCount }} price{{ row.itemCount === 1 ? '' : 's' }}</span
        >
      </ng-template>

      <ng-template khCell="window" let-row>
        <kh-badge [tone]="row.isActive ? 'success' : 'neutral'">
          {{ row.isActive ? 'On' : 'Off' }}
        </kh-badge>
        <span class="note">{{ windowLabel(row) }}</span>
      </ng-template>

      <ng-template khCell="actions" let-row>
        <button khButton type="button" size="sm" variant="tertiary" (click)="startEdit(row)">Edit</button>
        <button
          khButton
          type="button"
          size="sm"
          variant="tertiary"
          [disabled]="busyId() === row.id"
          (click)="toggle(row)"
        >
          {{ row.isActive ? 'Switch off' : 'Switch on' }}
        </button>
      </ng-template>
    </kh-data-table>

    @if (drawerOpen()) {
      <kh-entity-drawer
        [heading]="editing() ? 'Edit price list' : 'New price list'"
        [subtitle]="editing()?.code ?? null"
        (closed)="drawerOpen.set(false)"
      >
        <kh-form-shell
          heading="Price list"
          [summary]="summary()"
          [saving]="saving()"
          [dirty]="true"
          [submitLabel]="editing() ? 'Save' : 'Create'"
          (submitted)="save()"
          (cancelled)="drawerOpen.set(false)"
        >
          <kh-field label="Name" for="pl-name" [error]="form.fields.name.error()">
            <input
              khControl
              id="pl-name"
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
              for="pl-code"
              hint="Stable, and not editable afterwards — reports and imports refer to it."
              [error]="form.fields.code.error()"
            >
              <input
                khControl
                id="pl-code"
                type="text"
                maxlength="40"
                [value]="form.fields.code.value()"
                (input)="form.fields.code.set($any($event.target).value)"
              />
            </kh-field>

            <kh-field
              label="Seller"
              for="pl-vendor"
              [optional]="true"
              hint="A seller id makes this their own list. Blank makes it the store's."
            >
              <input
                khControl
                id="pl-vendor"
                type="text"
                [value]="vendorId()"
                (input)="vendorId.set($any($event.target).value)"
              />
            </kh-field>
          }

          <kh-field label="Kind" for="pl-type" [hint]="typeHint()">
            <select khControl id="pl-type" [value]="type()" (change)="type.set($any($event.target).value)">
              @for (choice of types; track choice.value) {
                <option [value]="choice.value">{{ choice.label }}</option>
              }
            </select>
          </kh-field>

          <kh-field
            label="Priority"
            for="pl-priority"
            hint="Lower wins. A sale list should sit below the base list it overrides."
          >
            <input
              khControl
              id="pl-priority"
              type="number"
              min="0"
              [value]="form.fields.priority.value()"
              (input)="form.fields.priority.set($any($event.target).value)"
            />
          </kh-field>

          <div class="row">
            <kh-field label="Starts" for="pl-starts" [optional]="true">
              <input
                khControl
                id="pl-starts"
                type="datetime-local"
                [value]="startsAt()"
                (input)="startsAt.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Ends" for="pl-ends" [optional]="true">
              <input
                khControl
                id="pl-ends"
                type="datetime-local"
                [value]="endsAt()"
                (input)="endsAt.set($any($event.target).value)"
              />
            </kh-field>
          </div>
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
      heading="Delete this price list"
      message="Every price in it goes with it. Switching it off keeps the rows and stops it applying."
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
      font-weight: var(--weight-medium);
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
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PriceListsPage {
  private readonly pricing = inject(PricingAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly types = PRICE_LIST_TYPES;

  protected readonly list = this.pricing.priceLists();
  protected readonly values = signal<FilterValues>({});
  protected readonly saving = signal(false);
  protected readonly busyId = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly drawerOpen = signal(false);
  protected readonly deleting = signal(false);
  protected readonly editing = signal<PriceListResponse | null>(null);
  protected readonly type = signal('Base');
  protected readonly vendorId = signal('');
  protected readonly startsAt = signal('');
  protected readonly endsAt = signal('');

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('The name')], this.submitted),
    code: formField('', [required('A code')], this.submitted),
    priority: formField('100', [], this.submitted),
  });

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly typeHint = computed(
    () => this.types.find((choice) => choice.value === this.type())?.hint ?? '',
  );

  protected readonly rowKey = (row: PriceListResponse) => row.id;
  protected readonly rowLabel = (row: PriceListResponse) => row.name;

  protected readonly columns: readonly DataTableColumn<PriceListResponse>[] = [
    { key: 'name', label: 'Price list', kind: 'custom' },
    { key: 'type', label: 'Kind', value: (row) => row.type, width: '8rem' },
    { key: 'priority', label: 'Priority', kind: 'number', value: (row) => row.priority, width: '6rem' },
    { key: 'window', label: 'State', kind: 'custom', width: '14rem' },
    {
      key: 'vendorId',
      label: 'Seller',
      value: (row) => row.vendorId ?? 'The store',
      hiddenByDefault: true,
    },
    {
      key: 'createdAt',
      label: 'Created',
      kind: 'date',
      value: (row) => tableDateTime(row.createdAt),
      hiddenByDefault: true,
    },
    { key: 'actions', label: '', kind: 'custom', width: '11rem' },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'type',
      label: 'Kind',
      kind: 'select',
      options: PRICE_LIST_TYPES.map((entry) => ({ value: entry.value, label: entry.label })),
    },
    {
      key: 'activeOnly',
      label: 'State',
      kind: 'select',
      options: [{ value: 'true', label: 'Switched on only' }],
    },
  ];

  constructor() {
    this.list.load();
  }

  protected windowLabel(row: PriceListResponse): string {
    if (!row.startsAt && !row.endsAt) return 'No window — always in play';
    const from = row.startsAt ? tableDateTime(row.startsAt) : 'always';
    const to = row.endsAt ? tableDateTime(row.endsAt) : 'no end';
    return `${from} → ${to}`;
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: PriceListFilters = {
      type: values['type'],
      search: values['q'],
      activeOnly: values['activeOnly'] === 'true' ? true : undefined,
    };
    this.list.setFilters(filters);
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.type.set('Base');
    this.vendorId.set('');
    this.startsAt.set('');
    this.endsAt.set('');
    this.summary.set([]);
    this.form.reset({ name: '', code: '', priority: '100' });
    this.drawerOpen.set(true);
  }

  protected startEdit(row: PriceListResponse): void {
    this.editing.set(row);
    this.type.set(row.type);
    this.vendorId.set(row.vendorId ?? '');
    this.startsAt.set(localInput(row.startsAt));
    this.endsAt.set(localInput(row.endsAt));
    this.summary.set([]);
    this.form.reset({ name: row.name, code: row.code, priority: String(row.priority) });
    this.drawerOpen.set(true);
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    const starts = this.startsAt() ? new Date(this.startsAt()).toISOString() : null;
    const ends = this.endsAt() ? new Date(this.endsAt()).toISOString() : null;

    this.saving.set(true);
    this.summary.set([]);

    const existing = this.editing();
    const request = existing
      ? this.pricing.updatePriceList(existing.id, {
          name: values.name,
          type: this.type(),
          priority: Number(values.priority) || 0,
          startsAt: starts,
          endsAt: ends,
        } satisfies UpdatePriceListBody)
      : this.pricing.createPriceList({
          vendorId: this.vendorId().trim() || null,
          code: values.code,
          name: values.name,
          type: this.type(),
          priority: Number(values.priority) || 0,
          startsAt: starts,
          endsAt: ends,
        } satisfies CreatePriceListBody);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.drawerOpen.set(false);
        this.toasts.success(existing ? 'Price list saved.' : 'Price list created.');
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

  protected toggle(row: PriceListResponse): void {
    this.busyId.set(row.id);
    this.actionError.set(null);

    const request = row.isActive
      ? this.pricing.deactivatePriceList(row.id)
      : this.pricing.activatePriceList(row.id);

    request.subscribe({
      next: () => {
        this.busyId.set(null);
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.actionError.set(describeError(error, 'That price list could not be changed.'));
      },
    });
  }

  protected remove(): void {
    const existing = this.editing();
    if (!existing) return;

    this.saving.set(true);
    this.pricing.deletePriceList(existing.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleting.set(false);
        this.drawerOpen.set(false);
        this.toasts.success('Price list deleted.');
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.deleting.set(false);
        this.summary.set([describeError(error, 'It could not be deleted.')]);
      },
    });
  }
}

/** An ISO instant as a `datetime-local` input wants it: the local wall clock, no zone. */
function localInput(value: string | null): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}
