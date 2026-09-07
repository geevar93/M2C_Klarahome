import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ContentAdminService, PageFilters, PageSummaryResponse } from '@klarahome/data-access-admin';
import {
  CellTemplate,
  DataTable,
  DataTableColumn,
  FilterBar,
  FilterDefinition,
  FilterValues,
  Modal,
  PageHeader,
  StatusBadge,
} from '@klarahome/ui-admin';
import { Alert, Button, Control, Field, Icon } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';
import { PAGE_STATUSES, PAGE_TYPES } from './content-vocabulary';

/**
 * Every page the storefront can render.
 *
 * The list is deliberately thin, because a page's substance is its blocks and those belong to the
 * composer. What it does carry is the three facts somebody scanning for work needs: **what state
 * it is in, whether it is scheduled, and whether the draft has moved since it was published** —
 * that last one from `contentChangedAt` against `publishedAt`, which is how an editor finds the
 * six pages somebody edited and forgot to publish.
 *
 * Creating asks for four fields and nothing else. A page's type cannot be changed afterwards, so
 * it is asked for here; everything else is the composer's, and a create dialogue that collected
 * SEO fields would be collecting them before there was a page to write them about.
 */
@Component({
  selector: 'kh-content-pages-page',
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
    RouterLink,
    StatusBadge,
  ],
  template: `
    <kh-page-header heading="Pages" description="What the storefront says, and whether it is live yet.">
      <button khButton type="button" variant="primary" (click)="startCreate()">
        <kh-icon name="plus" size="sm" />
        New page
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Pages could not be loaded">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Pages"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="content-pages"
      exportMode="page"
      emptyMessage="No page matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        searchLabel="Search pages"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="title" let-row>
        <a class="link" [routerLink]="['/content/pages', row.id]">{{ row.title }}</a>
        <span class="note"
          >/{{ row.slug }} · {{ row.blockCount }} block{{ row.blockCount === 1 ? '' : 's' }}</span
        >
      </ng-template>

      <ng-template khCell="status" let-row>
        <kh-status-badge [status]="row.status" />
        @if (row.scheduledAt) {
          <span class="note">for {{ dateTime(row.scheduledAt) }}</span>
        } @else if (hasUnpublishedChanges(row)) {
          <span class="note warn">Draft is ahead of what is live</span>
        }
      </ng-template>
    </kh-data-table>

    <kh-modal [open]="creating()" heading="New page" (closed)="creating.set(false)">
      @if (createError(); as message) {
        <kh-alert tone="danger" heading="It could not be created">{{ message }}</kh-alert>
      }

      <kh-field label="Title" for="new-page-title" [error]="form.fields.title.error()">
        <input
          khControl
          id="new-page-title"
          type="text"
          maxlength="200"
          [value]="form.fields.title.value()"
          (input)="form.fields.title.set($any($event.target).value)"
          (touched)="form.fields.title.markTouched()"
        />
      </kh-field>

      <kh-field
        label="URL slug"
        for="new-page-slug"
        [optional]="true"
        hint="Left blank, it is made from the title."
      >
        <input
          khControl
          id="new-page-slug"
          type="text"
          [value]="form.fields.slug.value()"
          (input)="form.fields.slug.set($any($event.target).value)"
        />
      </kh-field>

      <kh-field label="Kind" for="new-page-type" [hint]="typeHint()">
        <select khControl id="new-page-type" [value]="type()" (change)="type.set($any($event.target).value)">
          @for (choice of types; track choice.value) {
            <option [value]="choice.value">{{ choice.label }}</option>
          }
        </select>
      </kh-field>

      <kh-field label="Summary" for="new-page-summary" [optional]="true">
        <textarea
          khControl
          id="new-page-summary"
          rows="2"
          [value]="form.fields.summary.value()"
          (input)="form.fields.summary.set($any($event.target).value)"
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
export class ContentPagesPage {
  private readonly content = inject(ContentAdminService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly types = PAGE_TYPES;
  protected readonly dateTime = tableDateTime;

  protected readonly list = this.content.pages();
  protected readonly values = signal<FilterValues>({});
  protected readonly creating = signal(false);
  protected readonly saving = signal(false);
  protected readonly createError = signal<string | null>(null);
  protected readonly type = signal('Landing');

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    title: formField('', [required('A title')], this.submitted),
    slug: formField('', [], this.submitted),
    summary: formField('', [], this.submitted),
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

  protected readonly rowKey = (row: PageSummaryResponse) => row.id;
  protected readonly rowLabel = (row: PageSummaryResponse) => row.title;

  protected readonly columns: readonly DataTableColumn<PageSummaryResponse>[] = [
    { key: 'title', label: 'Page', kind: 'custom' },
    { key: 'type', label: 'Kind', value: (row) => row.type, width: '8rem' },
    { key: 'status', label: 'State', kind: 'custom', width: '14rem' },
    { key: 'version', label: 'Version', kind: 'number', value: (row) => row.version, width: '6rem' },
    {
      key: 'publishedAt',
      label: 'Published',
      kind: 'date',
      value: (row) => tableDateTime(row.publishedAt),
    },
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
      key: 'status',
      label: 'State',
      kind: 'select',
      options: PAGE_STATUSES.map((entry) => ({ value: entry.value, label: entry.label })),
    },
    {
      key: 'type',
      label: 'Kind',
      kind: 'select',
      options: PAGE_TYPES.map((entry) => ({ value: entry.value, label: entry.label })),
    },
  ];

  constructor() {
    this.list.load();
  }

  /** The draft has moved since the last publish — the commonest way a change never goes live. */
  protected hasUnpublishedChanges(row: PageSummaryResponse): boolean {
    if (!row.publishedAt || !row.contentChangedAt) return false;
    return new Date(row.contentChangedAt).getTime() > new Date(row.publishedAt).getTime();
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: PageFilters = {
      search: values['q'],
      status: values['status'],
      type: values['type'],
    };
    this.list.setFilters(filters);
  }

  protected startCreate(): void {
    this.createError.set(null);
    this.type.set('Landing');
    this.form.reset({ title: '', slug: '', summary: '' });
    this.creating.set(true);
  }

  protected create(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    this.saving.set(true);
    this.createError.set(null);

    this.content
      .createPage({
        slug: values.slug || null,
        type: this.type(),
        title: values.title,
        summary: values.summary || null,
      })
      .subscribe({
        next: (created) => {
          this.saving.set(false);
          this.creating.set(false);
          this.toasts.success('Page created.');
          void this.router.navigate(['/content/pages', created.id]);
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
