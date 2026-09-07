import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  CollectionFilters,
  CollectionSummaryResponse,
  ContentAdminService,
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
import { Alert, Badge, Button, Control, Field, Icon } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';
import { COLLECTION_KINDS } from './content-vocabulary';

/**
 * Collections — the groupings a merchandiser makes that the catalogue does not.
 *
 * **A rule-driven collection holds rows, not a query.** Step 20 materialises the rule into items
 * because every condition it can express is about a schema the content module may not query, so
 * "refreshed at" is a real column with a real meaning: it is when the rows last agreed with the
 * rule. A collection whose rule was edited three days ago and refreshed never is showing what it
 * showed three days ago, and this list is where that becomes visible.
 *
 * Creating asks for three fields, because a collection has nothing to be a rule about until it
 * exists. The rule, the pinning and the SEO are one route down.
 */
@Component({
  selector: 'kh-collections-page',
  imports: [
    Alert,
    Badge,
    Button,
    CellTemplate,
    Control,
    DataTable,
    Field,
    FilterBar,
    Icon,
    Modal,
    PageHeader,
    RouterLink,
  ],
  template: `
    <kh-page-header heading="Collections" description="Groupings the storefront can browse and link to.">
      <button khButton type="button" variant="primary" (click)="startCreate()">
        <kh-icon name="plus" size="sm" />
        New collection
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Collections could not be loaded">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Collections"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="content-collections"
      exportMode="page"
      emptyMessage="No collection matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        searchLabel="Search collections"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="name" let-row>
        <a class="link" [routerLink]="['/content/collections', row.id]">{{ row.name }}</a>
        <span class="note"
          >/{{ row.slug }} · {{ row.itemCount }} product{{ row.itemCount === 1 ? '' : 's' }}</span
        >
      </ng-template>

      <ng-template khCell="state" let-row>
        <kh-badge [tone]="row.isActive ? 'success' : 'neutral'">
          {{ row.isActive ? 'Live' : 'Hidden' }}
        </kh-badge>
        @if (!row.isListed) {
          <span class="note">Not listed — reachable by link only</span>
        }
      </ng-template>

      <ng-template khCell="refreshedAt" let-row>
        @if (row.kind !== 'Rule') {
          <span class="note">Chosen by hand</span>
        } @else if (row.refreshedAt) {
          <span>{{ dateTime(row.refreshedAt) }}</span>
        } @else {
          <span class="note warn">Never refreshed</span>
        }
      </ng-template>
    </kh-data-table>

    <kh-modal [open]="creating()" heading="New collection" (closed)="creating.set(false)">
      @if (createError(); as message) {
        <kh-alert tone="danger" heading="It could not be created">{{ message }}</kh-alert>
      }

      <kh-field label="Name" for="collection-name" [error]="form.fields.name.error()">
        <input
          khControl
          id="collection-name"
          type="text"
          maxlength="160"
          [value]="form.fields.name.value()"
          (input)="form.fields.name.set($any($event.target).value)"
          (touched)="form.fields.name.markTouched()"
        />
      </kh-field>

      <kh-field
        label="URL slug"
        for="collection-slug"
        [optional]="true"
        hint="Left blank, it is made from the name."
      >
        <input
          khControl
          id="collection-slug"
          type="text"
          [value]="form.fields.slug.value()"
          (input)="form.fields.slug.set($any($event.target).value)"
        />
      </kh-field>

      <kh-field label="Description" for="collection-description" [optional]="true">
        <textarea
          khControl
          id="collection-description"
          rows="3"
          [value]="form.fields.description.value()"
          (input)="form.fields.description.set($any($event.target).value)"
        ></textarea>
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

    .note.warn {
      color: var(--color-warning);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CollectionsPage {
  private readonly content = inject(ContentAdminService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly dateTime = tableDateTime;

  protected readonly list = this.content.collections();
  protected readonly values = signal<FilterValues>({});
  protected readonly creating = signal(false);
  protected readonly saving = signal(false);
  protected readonly createError = signal<string | null>(null);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('A name')], this.submitted),
    slug: formField('', [], this.submitted),
    description: formField('', [], this.submitted),
  });

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: CollectionSummaryResponse) => row.id;
  protected readonly rowLabel = (row: CollectionSummaryResponse) => row.name;

  protected readonly columns: readonly DataTableColumn<CollectionSummaryResponse>[] = [
    { key: 'name', label: 'Collection', kind: 'custom' },
    {
      key: 'kind',
      label: 'Filled',
      value: (row) => (row.kind === 'Rule' ? 'By a rule' : 'By hand'),
      width: '9rem',
    },
    { key: 'state', label: 'State', kind: 'custom', width: '16rem' },
    { key: 'refreshedAt', label: 'Rows agree with the rule', kind: 'custom', width: '14rem' },
    {
      key: 'updatedAt',
      label: 'Edited',
      kind: 'date',
      value: (row) => tableDateTime(row.updatedAt),
      hiddenByDefault: true,
    },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'kind',
      label: 'Filled',
      kind: 'select',
      options: COLLECTION_KINDS.map((entry) => ({ value: entry.value, label: entry.label })),
    },
    { key: 'activeOnly', label: 'State', kind: 'select', options: [{ value: 'true', label: 'Live only' }] },
  ];

  constructor() {
    this.list.load();
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: CollectionFilters = {
      search: values['q'],
      kind: values['kind'],
      activeOnly: values['activeOnly'] === 'true' ? true : undefined,
    };
    this.list.setFilters(filters);
  }

  protected startCreate(): void {
    this.createError.set(null);
    this.form.reset({ name: '', slug: '', description: '' });
    this.creating.set(true);
  }

  protected create(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    this.saving.set(true);
    this.createError.set(null);

    this.content
      .createCollection({
        slug: values.slug || null,
        name: values.name,
        description: values.description || null,
      })
      .subscribe({
        next: (created) => {
          this.saving.set(false);
          this.creating.set(false);
          this.toasts.success('Collection created.');
          void this.router.navigate(['/content/collections', created.id]);
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
