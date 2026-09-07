import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CatalogAdminService, ModerationFilters, ModerationResponse } from '@klarahome/data-access-admin';
import {
  CellTemplate,
  ConfirmDialog,
  DataTable,
  DataTableColumn,
  FilterBar,
  FilterDefinition,
  FilterValues,
  PageHeader,
  toneFor,
} from '@klarahome/ui-admin';
import { Alert, Button } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';
import { catchError, forkJoin, map, of } from 'rxjs';

import { describeError } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';

/**
 * Products a seller has submitted, waiting for someone to look at them.
 *
 * The queue is a work list rather than a browsing surface, so it opens on `Pending` and the filter
 * is there for the case somebody wants to re-read what they approved yesterday.
 *
 * **Rejecting demands a reason and approving does not**, which is the asymmetry that matters: the
 * reason is the whole message the seller receives, and a rejection with no reason is a product
 * that comes back unchanged and is rejected again. Approval needs no explanation because the
 * result explains itself.
 *
 * Bulk approve exists because the realistic case is forty near-identical variants from one seller.
 * Bulk reject does not — one reason cannot honestly describe forty different problems, and a
 * queue where the fast path is "reject all with 'does not meet standards'" is a queue that
 * teaches sellers nothing.
 */
@Component({
  selector: 'kh-moderation-page',
  imports: [Alert, Button, CellTemplate, ConfirmDialog, DataTable, FilterBar, PageHeader, RouterLink],
  template: `
    <kh-page-header
      heading="Moderation"
      description="Products sellers have submitted. Nothing here is on the storefront yet."
    />

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="The queue could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="Not everything worked" [dismissible]="true">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Moderation queue"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [selectable]="true"
      [bulkActions]="bulkActions()"
      emptyMessage="Nothing is waiting for review."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
      (bulkAction)="approveMany($event.ids)"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        [searchable]="false"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="productName" let-row>
        <a class="link" [routerLink]="['/catalog/products', row.productId]">{{ row.productName }}</a>
      </ng-template>

      <ng-template khCell="actions" let-row>
        <div class="row-actions">
          <button khButton type="button" size="sm" [disabled]="busy()" (click)="approve(row)">Approve</button>
          <button
            khButton
            type="button"
            size="sm"
            variant="danger"
            [disabled]="busy()"
            (click)="rejecting.set(row)"
          >
            Reject
          </button>
        </div>
      </ng-template>
    </kh-data-table>

    <kh-confirm-dialog
      [open]="rejecting() !== null"
      heading="Reject this product"
      message="The seller is shown exactly what you write here, and can correct it and submit again."
      confirmLabel="Reject"
      [requireReason]="true"
      [busy]="busy()"
      (confirmed)="reject($event.reason)"
      (cancelled)="rejecting.set(null)"
    />
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .link {
      font-weight: var(--weight-medium);
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ModerationPage {
  private readonly catalog = inject(CatalogAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly list = this.catalog.moderationQueue();
  protected readonly values = signal<FilterValues>({ status: 'Pending' });
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);
  protected readonly rejecting = signal<ModerationResponse | null>(null);

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: ModerationResponse) => row.id;
  protected readonly rowLabel = (row: ModerationResponse) => row.productName;

  protected readonly columns: readonly DataTableColumn<ModerationResponse>[] = [
    { key: 'productName', label: 'Product', kind: 'custom' },
    {
      key: 'status',
      label: 'Status',
      kind: 'badge',
      value: (row) => row.status,
      tone: (row) => toneFor(row.status),
      width: '9rem',
    },
    { key: 'submittedAt', label: 'Submitted', kind: 'date', value: (row) => tableDateTime(row.submittedAt) },
    { key: 'notes', label: 'Notes', value: (row) => row.notes, hiddenByDefault: true },
    {
      key: 'reviewedAt',
      label: 'Reviewed',
      kind: 'date',
      value: (row) => tableDateTime(row.reviewedAt),
      hiddenByDefault: true,
    },
    { key: 'actions', label: 'Decision', kind: 'custom', width: '13rem' },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'status',
      label: 'Status',
      kind: 'select',
      options: [
        { value: 'Pending', label: 'Waiting' },
        { value: 'Approved', label: 'Approved' },
        { value: 'Rejected', label: 'Rejected' },
      ],
    },
  ];

  /** Approve in bulk; reject one at a time. See the class remarks for why the pair is uneven. */
  protected readonly bulkActions = computed(() => [
    {
      key: 'approve',
      label: 'Approve selected',
      disabledReason: this.busy() ? 'A decision is still being recorded.' : null,
    },
  ]);

  constructor() {
    this.list.setFilters({ status: 'Pending' });
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: ModerationFilters = { status: values['status'] };
    this.list.setFilters(filters);
  }

  protected approve(row: ModerationResponse): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);

    this.catalog.approveProduct(row.productId, null).subscribe({
      next: () => {
        this.busy.set(false);
        this.toasts.success(`${row.productName} approved.`);
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That product could not be approved.'));
      },
    });
  }

  protected approveMany(ids: readonly string[]): void {
    if (this.busy() || ids.length === 0) return;

    // The selection is keyed on the moderation row's id; approving takes the product's, so the
    // rows on screen are looked up rather than the ids being passed straight through.
    const rows = this.list.rows().filter((row) => ids.includes(row.id));
    if (rows.length === 0) return;

    this.busy.set(true);
    this.actionError.set(null);

    forkJoin(
      rows.map((row) =>
        this.catalog.approveProduct(row.productId, null).pipe(
          map(() => null),
          catchError((error: unknown) => of(`${row.productName}: ${describeError(error, 'refused')}`)),
        ),
      ),
    ).subscribe((outcomes) => {
      this.busy.set(false);
      const failures = outcomes.filter((outcome): outcome is string => outcome !== null);
      const succeeded = outcomes.length - failures.length;

      if (succeeded > 0) this.toasts.success(`${succeeded} of ${outcomes.length} approved.`);
      if (failures.length > 0) {
        this.actionError.set(
          `${failures.length} of ${outcomes.length} were refused. The first said — ${failures[0]}`,
        );
      }
      this.list.refresh();
    });
  }

  protected reject(reason: string): void {
    const row = this.rejecting();
    if (!row) return;

    this.rejecting.set(null);
    this.busy.set(true);
    this.actionError.set(null);

    this.catalog.rejectProduct(row.productId, { notes: reason }).subscribe({
      next: () => {
        this.busy.set(false);
        this.toasts.success(`${row.productName} rejected, and the seller told why.`);
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That product could not be rejected.'));
      },
    });
  }
}
