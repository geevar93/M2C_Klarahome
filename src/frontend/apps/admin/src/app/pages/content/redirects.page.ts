import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ContentAdminService, RedirectFilters, RedirectResponse } from '@klarahome/data-access-admin';
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
import { tableDateTime } from '../../core/format';

/** The two status codes a store actually needs, and what each of them tells a search engine. */
const STATUS_CODES = [
  { value: '301', label: '301 — moved for good', hint: 'Search engines transfer the ranking.' },
  { value: '302', label: '302 — moved for now', hint: 'Search engines keep the old URL indexed.' },
  { value: '410', label: '410 — gone', hint: 'No destination. Tells search engines to drop it.' },
] as const;

/**
 * Redirects — where a URL that used to work goes now.
 *
 * **The hit count is the point of this screen.** A redirect nobody follows is a row somebody added
 * defensively; one with four thousand hits is a link that is still being shared, and the two want
 * different decisions. `lastHitAt` is what says which is which, and it is why this table sorts
 * itself in the endpoint's own order rather than offering a sort the API does not accept.
 *
 * **301 and 302 are not interchangeable**, and the form says so rather than presenting a number.
 * A permanent redirect transfers a page's ranking and is remembered by browsers — including when
 * it was a mistake — so it is the one that is expensive to undo. A 410 is the third answer: the
 * page is gone and nothing replaces it, which is a better thing to tell a search engine than a
 * redirect to the home page.
 */
@Component({
  selector: 'kh-redirects-page',
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
    <kh-page-header heading="Redirects" description="Where a URL that used to work goes now.">
      <button khButton type="button" variant="primary" (click)="startCreate()">
        <kh-icon name="plus" size="sm" />
        New redirect
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Redirects could not be loaded">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Redirects"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="content-redirects"
      exportMode="page"
      emptyMessage="No redirect matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        searchLabel="Search paths"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="fromPath" let-row>
        <button type="button" class="link" (click)="startEdit(row)">{{ row.fromPath }}</button>
        <span class="note">→ {{ row.toPath ?? 'nothing — gone' }}</span>
      </ng-template>

      <ng-template khCell="statusCode" let-row>
        <kh-badge [tone]="row.statusCode === 301 ? 'info' : 'neutral'">{{ row.statusCode }}</kh-badge>
        @if (!row.isActive) {
          <span class="note">Switched off</span>
        }
      </ng-template>

      <ng-template khCell="hitCount" let-row>
        <span>{{ row.hitCount }}</span>
        @if (row.lastHitAt) {
          <span class="note">last {{ dateTime(row.lastHitAt) }}</span>
        } @else {
          <span class="note">never followed</span>
        }
      </ng-template>
    </kh-data-table>

    @if (drawerOpen()) {
      <kh-entity-drawer
        [heading]="editing() ? 'Edit redirect' : 'New redirect'"
        [subtitle]="editing()?.fromPath ?? null"
        (closed)="drawerOpen.set(false)"
      >
        <kh-form-shell
          heading="Redirect"
          [summary]="summary()"
          [saving]="saving()"
          [dirty]="true"
          [submitLabel]="editing() ? 'Save' : 'Create'"
          (submitted)="save()"
          (cancelled)="drawerOpen.set(false)"
        >
          @if (!editing()) {
            <kh-field
              label="From"
              for="redirect-from"
              hint="The path that used to work, beginning with a slash. Not editable afterwards."
              [error]="form.fields.fromPath.error()"
            >
              <input
                khControl
                id="redirect-from"
                type="text"
                [value]="form.fields.fromPath.value()"
                (input)="form.fields.fromPath.set($any($event.target).value)"
                (touched)="form.fields.fromPath.markTouched()"
              />
            </kh-field>
          }

          <kh-field label="What to answer" for="redirect-status" [hint]="statusHint()">
            <select
              khControl
              id="redirect-status"
              [value]="statusCode()"
              (change)="statusCode.set($any($event.target).value)"
            >
              @for (choice of statusCodes; track choice.value) {
                <option [value]="choice.value">{{ choice.label }}</option>
              }
            </select>
          </kh-field>

          @if (statusCode() !== '410') {
            <kh-field label="To" for="redirect-to" [error]="form.fields.toPath.error()">
              <input
                khControl
                id="redirect-to"
                type="text"
                [value]="form.fields.toPath.value()"
                (input)="form.fields.toPath.set($any($event.target).value)"
              />
            </kh-field>
          }

          <kh-field
            label="Note"
            for="redirect-note"
            [optional]="true"
            hint="Why this exists, for whoever finds it later."
          >
            <input
              khControl
              id="redirect-note"
              type="text"
              maxlength="200"
              [value]="form.fields.note.value()"
              (input)="form.fields.note.set($any($event.target).value)"
            />
          </kh-field>

          @if (editing()) {
            <kh-checkbox
              label="Active"
              inputId="redirect-active"
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
      heading="Delete this redirect"
      message="Anybody following the old link gets a 404 again. Switching it off does the same thing and keeps the record."
      confirmLabel="Delete"
      [confirmPhrase]="editing()?.fromPath ?? null"
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
      font-family: var(--font-mono);
      font-weight: var(--weight-medium);
      text-align: start;
      cursor: pointer;
    }

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RedirectsPage {
  private readonly content = inject(ContentAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly statusCodes = STATUS_CODES;
  protected readonly dateTime = tableDateTime;

  protected readonly list = this.content.redirects();
  protected readonly values = signal<FilterValues>({});
  protected readonly saving = signal(false);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly drawerOpen = signal(false);
  protected readonly deleting = signal(false);
  protected readonly editing = signal<RedirectResponse | null>(null);
  protected readonly statusCode = signal('301');
  protected readonly isActive = signal(true);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    fromPath: formField('', [required('A path')], this.submitted),
    toPath: formField('', [], this.submitted),
    note: formField('', [], this.submitted),
  });

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly statusHint = computed(
    () => STATUS_CODES.find((choice) => choice.value === this.statusCode())?.hint ?? '',
  );

  protected readonly rowKey = (row: RedirectResponse) => row.id;
  protected readonly rowLabel = (row: RedirectResponse) => row.fromPath;

  protected readonly columns: readonly DataTableColumn<RedirectResponse>[] = [
    { key: 'fromPath', label: 'Path', kind: 'custom' },
    { key: 'statusCode', label: 'Answers', kind: 'custom', width: '9rem' },
    { key: 'hitCount', label: 'Followed', kind: 'custom', width: '12rem' },
    { key: 'note', label: 'Note', value: (row) => row.note ?? '', hiddenByDefault: true },
    {
      key: 'createdAt',
      label: 'Added',
      kind: 'date',
      value: (row) => tableDateTime(row.createdAt),
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
    const filters: RedirectFilters = {
      search: values['q'],
      activeOnly: values['activeOnly'] === 'true' ? true : undefined,
    };
    this.list.setFilters(filters);
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.statusCode.set('301');
    this.isActive.set(true);
    this.summary.set([]);
    this.form.reset({ fromPath: '', toPath: '', note: '' });
    this.drawerOpen.set(true);
  }

  protected startEdit(row: RedirectResponse): void {
    this.editing.set(row);
    this.statusCode.set(String(row.statusCode));
    this.isActive.set(row.isActive);
    this.summary.set([]);
    this.form.reset({ fromPath: row.fromPath, toPath: row.toPath ?? '', note: row.note ?? '' });
    this.drawerOpen.set(true);
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    const status = Number(this.statusCode()) || 301;
    // A 410 says "gone", which is precisely a redirect with nowhere to go. Sending a destination
    // with it would be two contradictory answers in one row.
    const to = status === 410 ? null : values.toPath || null;

    this.saving.set(true);
    this.summary.set([]);

    const existing = this.editing();
    const request = existing
      ? this.content.updateRedirect(existing.id, {
          toPath: to,
          statusCode: status,
          isActive: this.isActive(),
          note: values.note || null,
        })
      : this.content.createRedirect({
          fromPath: values.fromPath,
          toPath: to,
          statusCode: status,
          note: values.note || null,
        });

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.drawerOpen.set(false);
        this.toasts.success(existing ? 'Redirect saved.' : 'Redirect created.');
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
    this.content.deleteRedirect(existing.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleting.set(false);
        this.drawerOpen.set(false);
        this.toasts.success('Redirect deleted.');
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
