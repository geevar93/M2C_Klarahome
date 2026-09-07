import { ChangeDetectionStrategy, Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  CatalogAdminService,
  CatalogJobResponse,
  DocumentPrintService,
  ProductFilters,
  ProductListItem,
} from '@klarahome/data-access-admin';
import {
  BulkAction,
  CellTemplate,
  DataTable,
  DataTableColumn,
  FilterBar,
  FilterDefinition,
  FilterValues,
  Modal,
  PageHeader,
  toneFor,
} from '@klarahome/ui-admin';
import { Alert, Button, Icon } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';
import { Subscription, catchError, forkJoin, map, of } from 'rxjs';

import { describeError } from '../../core/describe-error';
import { tableDate, tableDateTime } from '../../core/format';

/**
 * The product catalogue.
 *
 * Three things here are decisions rather than layout.
 *
 * **Bulk actions are sequential requests, not a bulk endpoint.** There is no
 * `POST /admin/products/publish` taking forty ids, and inventing a client-side one by firing forty
 * requests and reporting "done" would hide that six of them were refused. So the page fires them,
 * waits for all of them, and reports the count that succeeded *and* the first reason one did not.
 * A bulk action in a back office is only useful if its failures are visible.
 *
 * **Import is a job, and the job's errors are the product of the screen.** The upload answers a
 * job id and the rows are processed afterwards, so the modal polls and then shows
 * `ImportErrorResponse` rows — line number, column, SKU, message. "412 rows failed" is a status;
 * this is something somebody can fix and re-upload.
 *
 * **Export is `exportMode="server"`.** The table holds 25 rows of a catalogue that has thousands,
 * and a button labelled Export that silently wrote 25 of them would be worse than no button. It
 * asks the API for the whole set, as another job.
 */
@Component({
  selector: 'kh-products-page',
  imports: [Alert, Button, CellTemplate, DataTable, FilterBar, Icon, Modal, PageHeader, RouterLink],
  template: `
    <kh-page-header heading="Products" description="Everything the catalogue holds, whoever created it.">
      <a khButton variant="primary" routerLink="/catalog/products/new">
        <kh-icon name="plus" size="sm" />
        New product
      </a>
      <button khButton type="button" (click)="importOpen.set(true)">
        <kh-icon name="download" size="sm" />
        Import CSV
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="The catalogue could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="Not everything worked" [dismissible]="true">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Products"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [selectable]="true"
      [bulkActions]="bulkActions()"
      [configurable]="true"
      storageKey="catalog-products"
      exportMode="server"
      emptyMessage="No product matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
      (bulkAction)="runBulk($event)"
      (exportRequested)="exportAll()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        searchLabel="Search by name or SKU"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="name" let-row>
        <a class="link" [routerLink]="['/catalog/products', row.id]">{{ row.name }}</a>
        <span class="slug">{{ row.slug }}</span>
      </ng-template>
    </kh-data-table>

    <kh-modal
      [open]="importOpen()"
      heading="Import products"
      width="44rem"
      [dismissible]="!importing()"
      (closed)="closeImport()"
    >
      <p class="hint">
        A CSV in the platform's own column order. Download the template if you have not imported before — a
        file with the wrong headings fails every row.
      </p>

      <div class="import-actions">
        <label class="upload">
          <input type="file" accept=".csv,text/csv" [disabled]="importing()" (change)="startImport($event)" />
          <kh-icon name="plus" size="sm" />
          {{ importing() ? 'Importing…' : 'Choose a CSV' }}
        </label>
        <button khButton type="button" size="sm" (click)="downloadTemplate()">Download the template</button>
      </div>

      @if (importError(); as message) {
        <kh-alert tone="danger" heading="The import could not start">{{ message }}</kh-alert>
      }

      @if (job(); as current) {
        <section class="job">
          <h3>{{ current.fileName }} — {{ current.status }}</h3>
          <!-- A native progress element: this is a determinate bar with a real value, which
               kh-progress-bar deliberately is not — that one is the indeterminate loading strip. -->
          <progress [value]="current.processedRows" [max]="current.totalRows || 1">
            {{ current.processedRows }} of {{ current.totalRows }}
          </progress>
          <p class="counts">
            {{ current.processedRows }} of {{ current.totalRows }} processed ·
            {{ current.succeededRows }} written · {{ current.failedRows }} refused
          </p>

          @if (current.failureReason; as reason) {
            <kh-alert tone="danger" heading="The job stopped">{{ reason }}</kh-alert>
          }

          @if (current.errors.length > 0) {
            <table class="errors">
              <caption>
                Rows that were refused. Fix these in the file and import it again — the rows that were written
                are not written twice.
              </caption>
              <thead>
                <tr>
                  <th scope="col">Row</th>
                  <th scope="col">Column</th>
                  <th scope="col">SKU</th>
                  <th scope="col">Problem</th>
                </tr>
              </thead>
              <tbody>
                @for (problem of current.errors; track problem.rowNumber + (problem.column ?? '')) {
                  <tr>
                    <td>{{ problem.rowNumber }}</td>
                    <td>{{ problem.column ?? '—' }}</td>
                    <td>{{ problem.sku ?? '—' }}</td>
                    <td>{{ problem.message }}</td>
                  </tr>
                }
              </tbody>
            </table>
          }

          @if (current.downloadUrl; as url) {
            <a khButton size="sm" [href]="url" target="_blank" rel="noopener">Download the report</a>
          }
        </section>
      }

      <div slot="footer">
        <button khButton type="button" variant="tertiary" [disabled]="importing()" (click)="closeImport()">
          Close
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

    .slug {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .hint {
      margin-block-start: 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .import-actions {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      margin-block-end: var(--space-4);
    }

    .upload {
      display: inline-flex;
      gap: var(--space-1);
      align-items: center;
      padding: var(--space-2) var(--space-3);
      border: 1px dashed var(--color-border-strong);
      border-radius: var(--radius-md);
      font-size: var(--text-sm);
      cursor: pointer;
    }

    .upload input {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }

    .job h3 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-base);
    }

    .job progress {
      inline-size: 100%;
    }

    .counts {
      margin: var(--space-2) 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .errors {
      inline-size: 100%;
      border-collapse: collapse;
      font-size: var(--text-sm);
    }

    .errors caption {
      margin-block-end: var(--space-2);
      color: var(--color-text-muted);
      font-size: var(--text-xs);
      text-align: start;
    }

    .errors th,
    .errors td {
      padding: var(--space-1) var(--space-2);
      border-block-end: 1px solid var(--color-border);
      text-align: start;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductsPage implements OnDestroy {
  private readonly catalog = inject(CatalogAdminService);
  private readonly documents = inject(DocumentPrintService);
  private readonly toasts = inject(ToastService);

  protected readonly list = this.catalog.products();
  protected readonly values = signal<FilterValues>({});
  protected readonly actionError = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly importOpen = signal(false);
  protected readonly importing = signal(false);
  protected readonly importError = signal<string | null>(null);
  protected readonly job = signal<CatalogJobResponse | null>(null);

  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private pollSubscription: Subscription | null = null;

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: ProductListItem) => row.id;
  protected readonly rowLabel = (row: ProductListItem) => row.name;

  protected readonly columns: readonly DataTableColumn<ProductListItem>[] = [
    { key: 'name', label: 'Product', kind: 'custom' },
    {
      key: 'status',
      label: 'Status',
      kind: 'badge',
      value: (row) => row.status,
      tone: (row) => toneFor(row.status),
    },
    { key: 'variantCount', label: 'Variants', kind: 'number', value: (row) => row.variantCount },
    { key: 'listingCount', label: 'Offers', kind: 'number', value: (row) => row.listingCount },
    {
      key: 'ratingAverage',
      label: 'Rating',
      kind: 'number',
      value: (row) => (row.ratingAverage === null ? '—' : row.ratingAverage.toFixed(1)),
      hiddenByDefault: true,
    },
    { key: 'publishedAt', label: 'Published', kind: 'date', value: (row) => tableDate(row.publishedAt) },
    {
      key: 'createdAt',
      label: 'Created',
      kind: 'date',
      value: (row) => tableDateTime(row.createdAt),
      hiddenByDefault: true,
    },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'status',
      label: 'Status',
      kind: 'select',
      options: [
        { value: 'Draft', label: 'Draft' },
        { value: 'PendingApproval', label: 'Awaiting approval' },
        { value: 'Active', label: 'Active' },
        { value: 'Inactive', label: 'Inactive' },
        { value: 'Archived', label: 'Archived' },
      ],
    },
  ];

  /**
   * The bulk actions, disabled with a reason while one is running.
   *
   * Disabled rather than hidden: a control that disappears mid-task teaches nobody why, and the
   * reason string is what the table shows on hover.
   */
  protected readonly bulkActions = computed<readonly BulkAction[]>(() => {
    const reason = this.busy() ? 'Another bulk action is still running.' : null;
    return [
      { key: 'publish', label: 'Publish', disabledReason: reason },
      { key: 'unpublish', label: 'Unpublish', disabledReason: reason },
      { key: 'archive', label: 'Archive', destructive: true, disabledReason: reason },
    ];
  });

  constructor() {
    this.list.load();
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: ProductFilters = { status: values['status'], search: values['q'] };
    this.list.setFilters(filters);
  }

  protected runBulk(action: { key: string; ids: readonly string[] }): void {
    if (action.ids.length === 0 || this.busy()) return;

    this.busy.set(true);
    this.actionError.set(null);

    const call = (id: string) => {
      if (action.key === 'publish') return this.catalog.publishProduct(id);
      if (action.key === 'unpublish') return this.catalog.unpublishProduct(id);
      return this.catalog.archiveProduct(id);
    };

    // `forkJoin` abandons the whole set on the first error, which is the wrong report for a bulk
    // action: the other thirty-nine may well have worked. Each call therefore catches its own
    // failure into a value, so every one of them completes and the summary can count both sides.
    forkJoin(
      action.ids.map((id) =>
        call(id).pipe(
          map(() => null),
          catchError((error: unknown) => of(describeError(error, 'That product was refused.'))),
        ),
      ),
    ).subscribe((outcomes) => {
      this.busy.set(false);
      const failures = outcomes.filter((outcome): outcome is string => outcome !== null);
      const succeeded = outcomes.length - failures.length;

      if (succeeded > 0) this.toasts.success(`${succeeded} of ${outcomes.length} updated.`);
      if (failures.length > 0) {
        this.actionError.set(
          `${failures.length} of ${outcomes.length} were refused. The first said: ${failures[0]}`,
        );
      }
      this.list.refresh();
    });
  }

  /**
   * The import template.
   *
   * Fetched rather than linked, for the reason `DocumentPrintService` exists: the access token is
   * held in memory only, and a plain `<a download>` is a browser navigation that carries no
   * `Authorization` header. It would download a 401 page named products.csv.
   */
  protected downloadTemplate(): void {
    this.documents.productImportTemplate().subscribe({
      next: (blob) => this.documents.download(blob, 'product-import-template.csv'),
      error: (error: unknown) =>
        this.importError.set(describeError(error, 'The template could not be downloaded.')),
    });
  }

  protected exportAll(): void {
    this.catalog.exportProducts().subscribe({
      next: (created) => {
        this.importOpen.set(true);
        this.watch(created);
      },
      error: (error: unknown) =>
        this.actionError.set(describeError(error, 'The export could not be started.')),
    });
  }

  protected startImport(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.importing.set(true);
    this.importError.set(null);
    this.job.set(null);

    this.catalog.importProducts(file).subscribe({
      next: (created) => this.watch(created),
      error: (error: unknown) => {
        this.importing.set(false);
        this.importError.set(describeError(error, 'That file could not be accepted.'));
      },
    });
  }

  protected closeImport(): void {
    if (this.importing()) return;
    this.stopPolling();
    this.importOpen.set(false);
    this.job.set(null);
    this.importError.set(null);
  }

  /**
   * Watches a job to completion.
   *
   * Polled rather than pushed: there is no socket, the job is short, and two seconds is fast
   * enough for somebody watching a progress bar and slow enough not to be a load test. The
   * timer is cleared when the job settles and when the component goes away — a poll that outlives
   * its screen is a request per two seconds for the rest of the session.
   */
  private watch(current: CatalogJobResponse): void {
    this.job.set(current);

    if (current.status === 'Queued' || current.status === 'Running') {
      this.importing.set(true);
      this.pollTimer = setTimeout(() => {
        this.pollSubscription = this.catalog.job(current.id).subscribe({
          next: (next) => this.watch(next),
          // A single failed poll is not a failed job; the next tick tries again.
          error: () => this.watch(current),
        });
      }, 2000);
      return;
    }

    this.importing.set(false);
    this.stopPolling();
    if (current.status !== 'Failed') this.list.refresh();
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) clearTimeout(this.pollTimer);
    this.pollTimer = null;
    this.pollSubscription?.unsubscribe();
    this.pollSubscription = null;
  }
}
