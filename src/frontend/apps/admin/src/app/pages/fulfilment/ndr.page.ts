import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FulfilmentService, NdrAction, NdrFilters, NdrResponse } from '@klarahome/data-access-admin';
import {
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
import { Alert, Badge, Button, Control, Field } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';

/**
 * The four things that can be done with a parcel the courier could not deliver.
 *
 * Typed against `NdrAction` since Step 28B, and typing it found two of these four were wrong: this
 * screen sent `Reschedule` and `UpdateAddress`, and the enum's members are `Rescheduled` and
 * `AddressUpdated`. Both buttons were refused by the server for as long as they existed, which is
 * precisely the failure an untyped vocabulary produces — a value renamed or misremembered costs a
 * request rather than a build (deliverable 11).
 */
const ACTIONS: readonly { readonly value: NdrAction; readonly label: string; readonly hint: string }[] = [
  {
    value: 'Reattempt',
    label: 'Try again',
    hint: 'The courier makes another attempt, at the same address.',
  },
  {
    value: 'Rescheduled',
    label: 'Try again on a date',
    hint: 'The customer has said when they will be in. Pick that day.',
  },
  {
    value: 'AddressUpdated',
    label: 'Correct the address',
    hint: 'Only for a wrong or incomplete address the customer has corrected.',
  },
  {
    value: 'ReturnToOrigin',
    label: 'Send it back',
    hint: 'Ends the delivery. The parcel comes back and the order is settled as a return.',
  },
];

/**
 * Parcels the courier could not deliver.
 *
 * A non-delivery report is a **clock**, not a list: couriers hold an undelivered parcel for a
 * fixed window — usually three attempts across a few days — and after that it goes back whatever
 * anybody here wanted. So the queue opens on the ones with no action recorded yet, and the age of
 * the report is a column rather than a detail.
 *
 * **Send it back is destructive and is drawn as such.** The other three keep the parcel in play;
 * that one ends the delivery, starts a return leg and, for a cash order, means nothing is
 * collected. It reads differently in the dialog because it *is* different.
 *
 * The reason code is the courier's own — "customer not available", "premises closed", "address
 * incomplete" — and it is shown unedited, because it decides which of the four actions has any
 * chance of working. An incomplete address will fail the second attempt too.
 */
@Component({
  selector: 'kh-ndr-page',
  imports: [Alert, Badge, Button, CellTemplate, Control, DataTable, Field, FilterBar, Modal, PageHeader],
  template: `
    <kh-page-header
      heading="Failed deliveries"
      description="Parcels the courier could not hand over. They will be sent back on their own if nobody decides otherwise."
    />

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="The queue could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not work" [dismissible]="true">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Failed deliveries"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      exportMode="page"
      emptyMessage="Nothing has failed to deliver. "
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

      <ng-template khCell="reason" let-row>
        <span class="reason">{{ row.reason || row.reasonCode }}</span>
        <span class="note">attempt {{ row.attemptNumber }} · waybill {{ row.awb ?? 'unknown' }}</span>
      </ng-template>

      <ng-template khCell="actions" let-row>
        @if (row.resolvedAt) {
          <kh-badge tone="success">Settled</kh-badge>
        } @else {
          <button khButton type="button" size="sm" [disabled]="busy()" (click)="start(row)">Decide</button>
        }
      </ng-template>
    </kh-data-table>

    <kh-modal
      [open]="deciding() !== null"
      heading="What should happen to this parcel"
      width="34rem"
      [dismissible]="!busy()"
      (closed)="deciding.set(null)"
    >
      @if (deciding(); as report) {
        <p class="hint">
          {{ report.orderNumber }} · waybill {{ report.awb ?? 'unknown' }} · attempt
          {{ report.attemptNumber }}. The courier said: {{ report.reason || report.reasonCode }}.
        </p>

        <kh-field label="What to do" for="ndr-action">
          <select
            khControl
            id="ndr-action"
            [value]="action()"
            (change)="action.set($any($event.target).value)"
          >
            @for (option of actions; track option.value) {
              <option [value]="option.value">{{ option.label }}</option>
            }
          </select>
        </kh-field>

        <p class="hint">{{ hint() }}</p>

        @if (action() === 'Rescheduled') {
          <kh-field label="Deliver on" for="ndr-date">
            <input
              khControl
              id="ndr-date"
              type="date"
              [value]="rescheduledFor()"
              (input)="rescheduledFor.set($any($event.target).value)"
            />
          </kh-field>
        }

        <kh-field
          label="Note"
          for="ndr-remark"
          hint="What the customer said. It goes to the courier and onto the order's timeline."
        >
          <input
            khControl
            id="ndr-remark"
            type="text"
            maxlength="200"
            [value]="remark()"
            (input)="remark.set($any($event.target).value)"
          />
        </kh-field>

        @if (action() === 'ReturnToOrigin') {
          <kh-alert tone="warning" heading="This ends the delivery">
            The parcel comes back to the warehouse and the order is settled as a return. For a cash order,
            nothing is collected.
          </kh-alert>
        }
      }

      <div slot="footer">
        <button khButton type="button" variant="tertiary" [disabled]="busy()" (click)="deciding.set(null)">
          Not now
        </button>
        <button
          khButton
          type="button"
          [variant]="action() === 'ReturnToOrigin' ? 'danger' : 'primary'"
          [disabled]="busy() || (action() === 'Rescheduled' && !rescheduledFor())"
          (click)="decide()"
        >
          {{ action() === 'ReturnToOrigin' ? 'Send it back' : 'Tell the courier' }}
        </button>
      </div>
    </kh-modal>
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .reason {
      display: block;
      font-weight: var(--weight-medium);
    }

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .hint {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NdrPage {
  private readonly fulfilment = inject(FulfilmentService);
  private readonly toasts = inject(ToastService);

  protected readonly actions = ACTIONS;

  protected readonly list = this.fulfilment.ndr();
  protected readonly values = signal<FilterValues>({});
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected readonly deciding = signal<NdrResponse | null>(null);
  protected readonly action = signal<NdrAction>('Reattempt');
  protected readonly rescheduledFor = signal('');
  protected readonly remark = signal('');

  protected readonly hint = computed(
    () => ACTIONS.find((option) => option.value === this.action())?.hint ?? '',
  );

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: NdrResponse) => row.id;
  protected readonly rowLabel = (row: NdrResponse) => row.orderNumber;

  protected readonly columns: readonly DataTableColumn<NdrResponse>[] = [
    { key: 'orderNumber', label: 'Order', value: (row) => row.orderNumber, width: '13rem' },
    { key: 'reason', label: 'Why it failed', kind: 'custom' },
    {
      key: 'action',
      label: 'Decision',
      kind: 'badge',
      value: (row) => row.action || 'Not decided',
      tone: (row) => toneFor(row.action || 'pending'),
      width: '10rem',
    },
    { key: 'raisedAt', label: 'Raised', kind: 'date', value: (row) => tableDateTime(row.raisedAt) },
    {
      key: 'rescheduledFor',
      label: 'Rescheduled for',
      kind: 'date',
      value: (row) => tableDateTime(row.rescheduledFor),
      hiddenByDefault: true,
    },
    { key: 'actions', label: '', kind: 'custom', width: '8rem' },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'action',
      label: 'Decision',
      kind: 'select',
      options: ACTIONS.map((option) => ({ value: option.value, label: option.label })),
    },
  ];

  constructor() {
    this.list.load();
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: NdrFilters = { action: (values['action'] as NdrAction) || undefined };
    this.list.setFilters(filters);
  }

  protected start(report: NdrResponse): void {
    this.deciding.set(report);
    this.action.set('Reattempt');
    this.rescheduledFor.set('');
    this.remark.set('');
  }

  protected decide(): void {
    const report = this.deciding();
    if (!report || this.busy()) return;

    this.busy.set(true);
    this.actionError.set(null);

    this.fulfilment
      .actionNdr(report.id, {
        action: this.action(),
        remark: this.remark() || null,
        // Only meaningful for a reschedule; sent as null otherwise so the courier is not given a
        // date it did not ask for.
        rescheduledFor: this.action() === 'Rescheduled' ? this.rescheduledFor() || null : null,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.deciding.set(null);
          this.toasts.success('The courier has been told.');
          this.list.refresh();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(describeError(error, 'That decision could not be recorded.'));
        },
      });
  }
}
