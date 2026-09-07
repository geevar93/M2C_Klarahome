import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AuditLogFilters, AuditLogService } from '@klarahome/data-access-admin';
import { AuditTrail, FilterBar, FilterDefinition, FilterValues, PageHeader } from '@klarahome/ui-admin';
import { Alert, Button, Icon } from '@klarahome/ui-primitives';

import { toAuditEntry } from '../core/audit.mapper';

/**
 * The audit log.
 *
 * The same component the entity pages embed (`kh-audit-trail`), over the whole table rather than
 * one record. That is the point of building the viewer as a component: an investigation that
 * starts on an order and widens to "what else did this person do that afternoon" is the same
 * reading surface with a different filter, not a second screen.
 *
 * Filtering is by **actor, action, entity and date**, which are the four questions actually asked
 * of an audit trail. There is deliberately no free-text search: the endpoint does not offer one,
 * and a box that quietly matched only the action name would be a search that lies about its
 * scope.
 *
 * The rows are read-only and there is no way to make them otherwise. `platform.audit_logs` is
 * append-only and partitioned by month (Step 6); an audit trail somebody can edit is not one.
 */
@Component({
  selector: 'kh-audit-log-page',
  imports: [Alert, AuditTrail, Button, FilterBar, Icon, PageHeader],
  template: `
    <kh-page-header
      heading="Audit log"
      description="Every change the platform recorded, newest first. Entries cannot be edited or removed."
    />

    <div class="controls">
      <kh-filter-bar
        [filters]="filters"
        [values]="values()"
        [searchable]="false"
        (changed)="applyFilters($event)"
      />

      <button khButton type="button" size="sm" [disabled]="list.loading()" (click)="list.refresh()">
        <kh-icon name="refresh" size="sm" />
        Refresh
      </button>
    </div>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="The log could not be loaded">{{ message }}</kh-alert>
    }

    <kh-audit-trail [entries]="entries()" [loading]="list.loading()" [showTechnical]="true" />

    <div class="pager">
      <button
        khButton
        type="button"
        size="sm"
        [disabled]="!list.hasPrevious() || list.loading()"
        (click)="list.previous()"
      >
        <kh-icon name="chevron-left" size="sm" />
        Newer
      </button>
      <button
        khButton
        type="button"
        size="sm"
        [disabled]="!list.nextCursor() || list.loading()"
        (click)="list.next()"
      >
        Older
        <kh-icon name="chevron-right" size="sm" />
      </button>
    </div>
  `,
  styles: `
    .controls {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      align-items: flex-end;
      justify-content: space-between;
      margin-block-end: var(--space-4);
    }

    .controls kh-filter-bar {
      flex: 1;
    }

    kh-alert {
      margin-block-end: var(--space-4);
    }

    .pager {
      display: flex;
      gap: var(--space-2);
      justify-content: flex-end;
      margin-block-start: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditLogPage {
  private readonly audit = inject(AuditLogService);

  protected readonly list = this.audit.list();
  protected readonly values = signal<FilterValues>({});

  protected readonly entries = computed(() => this.list.rows().map(toAuditEntry));

  /**
   * The entity types worth offering.
   *
   * A closed list rather than a free-text box, because `entityType` is matched exactly and a typo
   * silently returns nothing — which reads as "there is no record of that", the one answer an
   * audit log must never give wrongly.
   */
  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'entityType',
      label: 'Record type',
      kind: 'select',
      options: [
        { value: 'Order', label: 'Order' },
        { value: 'SubOrder', label: 'Sub-order' },
        { value: 'Product', label: 'Product' },
        { value: 'Listing', label: 'Listing' },
        { value: 'Vendor', label: 'Seller' },
        { value: 'User', label: 'User' },
        { value: 'Role', label: 'Role' },
        { value: 'Return', label: 'Return' },
        { value: 'Payment', label: 'Payment' },
        { value: 'Payout', label: 'Payout' },
        { value: 'Settings', label: 'Settings' },
        { value: 'FeatureFlag', label: 'Feature flag' },
      ],
    },
    { key: 'from', label: 'From', kind: 'date' },
    { key: 'to', label: 'To', kind: 'date' },
  ];

  constructor() {
    this.list.load();
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: AuditLogFilters = {
      entityType: values['entityType'],
      from: values['from'],
      to: values['to'],
    };
    this.list.setFilters(filters);
  }
}
