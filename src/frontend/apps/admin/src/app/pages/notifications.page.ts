import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  NotificationCentreService,
  NotificationFilters,
  NotificationLogResponse,
} from '@klarahome/data-access-admin';
import { KhDatePipe } from '@klarahome/i18n';
import {
  CellTemplate,
  ConfirmDialog,
  DataTable,
  DataTableColumn,
  FilterBar,
  FilterDefinition,
  FilterValues,
  PageHeader,
  StatusBadge,
  TablePage,
} from '@klarahome/ui-admin';
import { Alert, Button } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../core/describe-error';

/**
 * The notifications centre.
 *
 * Every email and SMS the platform has tried to send, and what became of each. It answers the
 * second question of nearly every support conversation — "did they get it?" — and, when the answer
 * is no, it is the only place the message can be sent again without somebody opening a database.
 *
 * **A failed notification is otherwise invisible.** Nothing on this platform breaks when an SMS
 * provider rejects a number: the order is placed, the shipment moves, and the customer simply
 * never hears. This screen is the alarm for that class of silence, which is why its count sits in
 * the top bar of every page.
 *
 * Retry is destructive in the sense the confirmation pattern means — it sends a real message to a
 * real person — so it asks first, and it asks for the recipient rather than a bare "are you sure":
 * the whole risk being guarded against is retrying the wrong row.
 */
@Component({
  selector: 'kh-notifications-page',
  imports: [
    Alert,
    Button,
    CellTemplate,
    ConfirmDialog,
    DataTable,
    FilterBar,
    KhDatePipe,
    PageHeader,
    StatusBadge,
  ],
  template: `
    <kh-page-header
      heading="Notifications"
      description="Every message the platform has tried to send. Retry anything that failed."
    />

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="That list could not be loaded">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Notification log"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [page]="page()"
      [loading]="list.loading()"
      emptyMessage="No messages match these filters."
      exportMode="page"
      [configurable]="true"
      storageKey="notifications"
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

      <ng-template khCell="status" let-row>
        <kh-status-badge [status]="row.status" [tone]="row.suppression ? 'warning' : null" />
      </ng-template>

      <ng-template khCell="sentAt" let-row>
        {{ row.sentAt ? (row.sentAt | khDate) : '—' }}
      </ng-template>

      <ng-template khCell="actions" let-row>
        @if (canRetry(row)) {
          <button khButton type="button" size="sm" (click)="ask(row)">Retry</button>
        }
      </ng-template>
    </kh-data-table>

    <kh-confirm-dialog
      [open]="pending() !== null"
      heading="Send this message again?"
      [message]="confirmMessage()"
      confirmLabel="Send again"
      tone="warning"
      [busy]="retrying()"
      (confirmed)="retry()"
      (cancelled)="pending.set(null)"
    />
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationsPage {
  private readonly centre = inject(NotificationCentreService);
  private readonly toasts = inject(ToastService);

  protected readonly list = this.centre.list();
  protected readonly values = signal<FilterValues>({});
  protected readonly pending = signal<NotificationLogResponse | null>(null);
  protected readonly retrying = signal(false);

  protected readonly rowKey = (row: NotificationLogResponse): string => row.id;
  protected readonly rowLabel = (row: NotificationLogResponse): string =>
    `${row.eventKey} to ${row.recipient}`;

  protected readonly columns: readonly DataTableColumn<NotificationLogResponse>[] = [
    { key: 'createdAt', label: 'Queued', kind: 'date', value: (row) => row.createdAt },
    { key: 'eventKey', label: 'Event', value: (row) => row.eventKey },
    { key: 'channel', label: 'Channel', value: (row) => row.channel },
    { key: 'recipient', label: 'Recipient', value: (row) => row.recipient },
    { key: 'subject', label: 'Subject', value: (row) => row.subject, hiddenByDefault: true },
    { key: 'status', label: 'Status', kind: 'custom' },
    { key: 'attempts', label: 'Attempts', kind: 'number', value: (row) => row.attempts },
    { key: 'sentAt', label: 'Sent', kind: 'custom' },
    // The provider's own words. Hidden by default because it is long and only matters once
    // somebody is investigating a specific row.
    { key: 'error', label: 'Error', value: (row) => row.error, hiddenByDefault: true },
    { key: 'actions', label: '', kind: 'custom', width: '7rem' },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'status',
      label: 'Status',
      kind: 'select',
      options: [
        { value: 'Queued', label: 'Queued' },
        { value: 'Sending', label: 'Sending' },
        { value: 'Sent', label: 'Sent' },
        { value: 'Delivered', label: 'Delivered' },
        { value: 'Failed', label: 'Failed' },
        { value: 'Bounced', label: 'Bounced' },
        { value: 'Suppressed', label: 'Suppressed' },
      ],
    },
    {
      key: 'channel',
      label: 'Channel',
      kind: 'select',
      options: [
        { value: 'Email', label: 'Email' },
        { value: 'Sms', label: 'SMS' },
        { value: 'Whatsapp', label: 'WhatsApp' },
        { value: 'Push', label: 'Push' },
      ],
    },
    { key: 'from', label: 'From', kind: 'date' },
    { key: 'to', label: 'To', kind: 'date' },
  ];

  protected readonly page = computed<TablePage>(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly confirmMessage = computed(() => {
    const row = this.pending();
    if (!row) return '';
    return `This sends "${row.eventKey}" to ${row.recipient} again. They will receive a second copy if the first one did arrive.`;
  });

  constructor() {
    this.list.load();
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: NotificationFilters = {
      status: values['status'],
      channel: values['channel'],
      from: values['from'],
      to: values['to'],
    };
    this.list.setFilters(filters);
  }

  /**
   * Whether a row may be sent again.
   *
   * Only what actually failed. Retrying a delivered message sends a duplicate for no reason, and
   * a suppressed one is suppressed on purpose — the recipient opted out, or the channel is off —
   * so retrying it would be overriding a decision this screen has no business overriding.
   */
  protected canRetry(row: NotificationLogResponse): boolean {
    return row.status === 'Failed' || row.status === 'Bounced';
  }

  protected ask(row: NotificationLogResponse): void {
    this.pending.set(row);
  }

  protected retry(): void {
    const row = this.pending();
    if (!row) return;

    this.retrying.set(true);
    this.centre.retry(row.id).subscribe({
      next: () => {
        this.retrying.set(false);
        this.pending.set(null);
        this.toasts.success('The message has been queued again.');
        // The row's status and attempt count have both moved; refetch rather than patch, so what
        // is on screen is what the server has.
        this.list.refresh();
        this.centre.refreshAttentionCount().subscribe({ error: () => undefined });
      },
      error: (error: unknown) => {
        this.retrying.set(false);
        this.toasts.danger(describeError(error, 'That message could not be queued again.'));
      },
    });
  }
}
