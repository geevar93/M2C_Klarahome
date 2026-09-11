import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  DocumentPrintService,
  FulfilmentService,
  MediaLibraryService,
  ShipmentFilters,
  ShipmentResponse,
  ShipmentSummaryResponse,
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
  PageHeader,
  StatusBadge,
  toneFor,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Control, Field, Icon } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime, tableMoney } from '../../core/format';

/**
 * Every parcel, and the courier's account of what happened to it.
 *
 * Three things live here rather than on the fulfilment screen, because they are all about a parcel
 * that has *already left*:
 *
 * **The manifest.** A handover sheet closes a set of shipments into one collection the courier
 * signs for. It is built from the table's selection, which is why this list is selectable — a
 * manifest is a decision about which parcels are going in today's van, and that is a
 * multi-selection, not a filter.
 *
 * **The tracking a webhook never brought.** `Sync` asks the courier directly, and `Record` writes a
 * scan by hand — which is not a workaround but the manual adapter's only path (Step 16A): a
 * deployment with no aggregator account has no webhook to miss.
 *
 * **The courier's failed messages.** An event that could not be applied is kept rather than
 * dropped, and replaying it is what an operator does once the reason is fixed. It is on this
 * screen because this is where somebody notices a parcel whose status has not moved since Tuesday.
 */
@Component({
  selector: 'kh-shipments-page',
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
    HasPermission,
    Icon,
    PageHeader,
    StatusBadge,
  ],
  template: `
    <kh-page-header
      heading="Shipments"
      description="Parcels that have been booked, and where the courier says they are."
    />

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Shipments could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not work" [dismissible]="true">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Shipments"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [selectable]="true"
      [bulkActions]="bulkActions()"
      [configurable]="true"
      storageKey="shipments-list"
      exportMode="page"
      emptyMessage="No parcel matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
      (bulkAction)="createManifest($event.ids)"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        searchLabel="Order number, recipient or waybill"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="orderNumber" let-row>
        <button type="button" class="link" (click)="open(row)">{{ row.orderNumber }}</button>
        <span class="note">{{ row.subOrderNumber }}</span>
      </ng-template>

      <ng-template khCell="awb" let-row>
        @if (row.awb) {
          <span class="awb">{{ row.awb }}</span>
          <span class="note">{{ row.courier ?? 'courier unknown' }}</span>
        } @else {
          <span class="note">Not booked</span>
        }
      </ng-template>
    </kh-data-table>

    <section class="panel">
      <div class="panel-head">
        <h2>Courier messages that failed</h2>
        <button khButton type="button" size="sm" [disabled]="events.loading()" (click)="events.refresh()">
          <kh-icon name="refresh" size="sm" />
          Refresh
        </button>
      </div>
      <p class="hint">
        Updates the courier sent that the platform could not apply. Fix the cause, then replay — nothing is
        lost in the meantime.
      </p>

      <table>
        <thead>
          <tr>
            <th scope="col">Received</th>
            <th scope="col">Courier</th>
            <th scope="col">Waybill</th>
            <th scope="col">Event</th>
            <th scope="col">Why it failed</th>
            <th scope="col"><span class="sr-only">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          @for (event of events.rows(); track event.id) {
            <tr>
              <td>{{ when(event.receivedAt) }}</td>
              <td>{{ event.provider }}</td>
              <td>{{ event.awb ?? '—' }}</td>
              <td>
                {{ event.eventType }}
                @if (!event.signatureValid) {
                  <kh-badge tone="danger">Signature invalid</kh-badge>
                }
              </td>
              <td class="note">{{ event.processError ?? event.status }} · {{ event.attempts }} attempts</td>
              <td>
                <button
                  *khHasPermission="'shipping.shipment.manage'"
                  khButton
                  type="button"
                  size="sm"
                  [disabled]="busy()"
                  (click)="replay(event.id)"
                >
                  Replay
                </button>
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="6" class="hint">Nothing has failed. That is the answer you want.</td>
            </tr>
          }
        </tbody>
      </table>
    </section>

    @if (viewing(); as parcel) {
      <kh-entity-drawer
        [heading]="parcel.awb || parcel.subOrderNumber"
        [subtitle]="parcel.orderNumber + ' · ' + parcel.status"
        (closed)="viewing.set(null)"
      >
        <dl class="facts">
          <div>
            <dt>Status</dt>
            <dd><kh-status-badge [status]="parcel.status" /></dd>
          </div>
          <div>
            <dt>Courier</dt>
            <dd>{{ parcel.courier ?? '—' }} ({{ parcel.provider }})</dd>
          </div>
          <div>
            <dt>Service</dt>
            <dd>{{ parcel.serviceName ?? '—' }}</dd>
          </div>
          <div>
            <dt>Weight</dt>
            <dd>
              {{ parcel.weightGrams }} g
              @if (parcel.chargedWeightGrams) {
                · billed at {{ parcel.chargedWeightGrams }} g
              }
            </dd>
          </div>
          <div>
            <dt>Box</dt>
            <dd>{{ parcel.lengthCm }} × {{ parcel.widthCm }} × {{ parcel.heightCm }} cm</dd>
          </div>
          <div>
            <dt>To</dt>
            <dd>{{ parcel.destinationPincode }}</dd>
          </div>
          @if (parcel.codAmount) {
            <div>
              <dt>To collect</dt>
              <dd>{{ money(parcel.codAmount, parcel.currencyCode) }}</dd>
            </div>
          }
          <div>
            <dt>Freight charged</dt>
            <dd>{{ money(parcel.freightCharged, parcel.currencyCode) }}</dd>
          </div>
          @if (parcel.expectedDeliveryAt; as expected) {
            <div>
              <dt>Expected</dt>
              <dd>{{ when(expected) }}</dd>
            </div>
          }
          @if (parcel.statusReason; as reason) {
            <div>
              <dt>Note</dt>
              <dd>{{ reason }}</dd>
            </div>
          }
        </dl>

        <h3>What is in it</h3>
        <table>
          <thead>
            <tr>
              <th scope="col">Item</th>
              <th scope="col" class="numeric">Qty</th>
            </tr>
          </thead>
          <tbody>
            @for (line of parcel.lines; track line.orderLineId) {
              <tr>
                <td>
                  {{ line.name }}<span class="note">{{ line.sku }}</span>
                </td>
                <td class="numeric">{{ line.quantity }}</td>
              </tr>
            }
          </tbody>
        </table>

        <h3>Where it has been</h3>
        <ol class="tracking">
          @for (event of parcel.tracking; track event.occurredAt + event.status) {
            <li>
              <span class="when">{{ when(event.occurredAt) }}</span>
              <span>
                {{ event.status }}
                @if (event.location; as place) {
                  · {{ place }}
                }
                @if (event.remark; as remark) {
                  <span class="note">{{ remark }}</span>
                }
              </span>
              @if (!event.isApplied) {
                <kh-badge tone="warning">Not applied</kh-badge>
              }
            </li>
          } @empty {
            <li class="hint">The courier has not reported anything yet.</li>
          }
        </ol>

        <fieldset *khHasPermission="'shipping.shipment.manage'">
          <legend>Record a scan by hand</legend>
          <p class="hint">For a waybill this deployment tracks itself — there is no webhook to wait for.</p>
          <div class="pair">
            <kh-field label="Status" for="tracking-status">
              <input
                khControl
                id="tracking-status"
                type="text"
                [value]="trackingStatus()"
                (input)="trackingStatus.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Remark" for="tracking-remark" [optional]="true">
              <input
                khControl
                id="tracking-remark"
                type="text"
                [value]="trackingRemark()"
                (input)="trackingRemark.set($any($event.target).value)"
              />
            </kh-field>
          </div>
          <button
            khButton
            type="button"
            size="sm"
            [disabled]="busy() || trackingStatus().trim().length === 0"
            (click)="recordTracking()"
          >
            Record it
          </button>
        </fieldset>

        <div slot="footer">
          <button khButton type="button" size="sm" [disabled]="busy()" (click)="printLabel(parcel)">
            Print the label
          </button>
          <button khButton type="button" size="sm" [disabled]="busy()" (click)="sync(parcel)">
            Ask the courier
          </button>
          <button
            *khHasPermission="'shipping.shipment.manage'"
            khButton
            type="button"
            size="sm"
            variant="danger"
            [disabled]="busy()"
            (click)="cancelling.set(parcel)"
          >
            Cancel the parcel
          </button>
        </div>
      </kh-entity-drawer>
    }

    <kh-confirm-dialog
      [open]="cancelling() !== null"
      heading="Cancel this parcel"
      message="The booking is withdrawn with the courier. The order it belongs to is not cancelled — do that on the order itself."
      confirmLabel="Cancel the parcel"
      [requireReason]="true"
      [busy]="busy()"
      (confirmed)="cancel($event.reason)"
      (cancelled)="cancelling.set(null)"
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
      font-weight: var(--weight-medium);
      text-align: start;
      cursor: pointer;
    }

    .awb {
      display: block;
      font-variant-numeric: tabular-nums;
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

    .panel {
      margin-block-start: var(--space-5);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .panel-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .panel h2 {
      margin: 0;
      font-size: var(--text-lg);
    }

    h3 {
      margin: var(--space-4) 0 var(--space-2);
      font-size: var(--text-base);
    }

    table {
      inline-size: 100%;
      border-collapse: collapse;
      font-size: var(--text-sm);
    }

    th,
    td {
      padding: var(--space-2);
      border-block-end: 1px solid var(--color-border);
      text-align: start;
      vertical-align: top;
    }

    .numeric {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }

    .facts {
      margin: 0;
      font-size: var(--text-sm);
    }

    .facts div {
      display: flex;
      gap: var(--space-3);
      justify-content: space-between;
      padding-block: var(--space-1);
      border-block-end: 1px solid var(--color-border);
    }

    .facts dt,
    .facts dd {
      margin: 0;
    }

    .facts dt {
      color: var(--color-text-muted);
    }

    .tracking {
      display: grid;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;
      font-size: var(--text-sm);
    }

    .tracking li {
      display: grid;
      grid-template-columns: 11rem 1fr auto;
      gap: var(--space-2);
      align-items: baseline;
    }

    .when {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    fieldset {
      margin: var(--space-4) 0 0;
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    legend {
      padding-inline: var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .pair {
      display: grid;
      gap: var(--space-3);
    }

    @media (min-width: 768px) {
      .pair {
        grid-template-columns: 1fr 1fr;
      }
    }

    .sr-only {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShipmentsPage {
  private readonly fulfilment = inject(FulfilmentService);
  private readonly documents = inject(DocumentPrintService);
  private readonly media = inject(MediaLibraryService);
  private readonly toasts = inject(ToastService);

  protected readonly list = this.fulfilment.shipments();
  /** Only the ones that failed: a log of everything that worked is not a work queue. */
  protected readonly events = this.fulfilment.courierEvents({ status: 'Failed' }, 10);

  protected readonly values = signal<FilterValues>({});
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected readonly viewing = signal<ShipmentResponse | null>(null);
  protected readonly cancelling = signal<ShipmentResponse | null>(null);
  protected readonly trackingStatus = signal('');
  protected readonly trackingRemark = signal('');

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: ShipmentSummaryResponse) => row.id;
  protected readonly rowLabel = (row: ShipmentSummaryResponse) => row.awb ?? row.subOrderNumber;

  protected readonly columns: readonly DataTableColumn<ShipmentSummaryResponse>[] = [
    { key: 'orderNumber', label: 'Order', kind: 'custom', width: '13rem' },
    {
      key: 'status',
      label: 'Status',
      kind: 'badge',
      value: (row) => row.status,
      tone: (row) => toneFor(row.status),
      width: '10rem',
    },
    { key: 'awb', label: 'Waybill', kind: 'custom' },
    { key: 'destinationPincode', label: 'To', value: (row) => row.destinationPincode, width: '7rem' },
    { key: 'weightGrams', label: 'Weight', kind: 'number', value: (row) => `${row.weightGrams} g` },
    {
      key: 'codAmount',
      label: 'To collect',
      kind: 'number',
      value: (row) => (row.codAmount ? tableMoney(row.codAmount, row.currencyCode) : '—'),
    },
    {
      key: 'expectedDeliveryAt',
      label: 'Expected',
      kind: 'date',
      value: (row) => tableDateTime(row.expectedDeliveryAt),
    },
    {
      key: 'createdAt',
      label: 'Booked',
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
        { value: 'Created', label: 'Packed, not booked' },
        { value: 'Booked', label: 'Booked' },
        { value: 'PickedUp', label: 'Picked up' },
        { value: 'InTransit', label: 'In transit' },
        { value: 'OutForDelivery', label: 'Out for delivery' },
        { value: 'Delivered', label: 'Delivered' },
        { value: 'Failed', label: 'Delivery failed' },
        { value: 'Cancelled', label: 'Cancelled' },
      ],
    },
  ];

  protected readonly bulkActions = computed(() => [
    {
      key: 'manifest',
      label: 'Create a handover manifest',
      disabledReason: this.busy() ? 'Something else is still running.' : null,
    },
  ]);

  constructor() {
    this.list.load();
    this.events.load();
  }

  protected money(amount: number, currency: string): string {
    return tableMoney(amount, currency);
  }

  protected when(value: string | null): string {
    return tableDateTime(value) || '—';
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: ShipmentFilters = { status: values['status'], q: values['q'] };
    this.list.setFilters(filters);
  }

  protected open(row: ShipmentSummaryResponse): void {
    this.trackingStatus.set('');
    this.trackingRemark.set('');
    this.busy.set(true);

    // The summary row does not carry the lines or the tracking, so the drawer fetches the whole
    // shipment rather than drawing a detail view out of a list row.
    this.fulfilment.shipment(row.id).subscribe({
      next: (parcel) => {
        this.busy.set(false);
        this.viewing.set(parcel);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That parcel could not be opened.'));
      },
    });
  }

  protected createManifest(ids: readonly string[]): void {
    if (ids.length === 0 || this.busy()) return;

    this.busy.set(true);
    this.actionError.set(null);

    this.fulfilment
      .createManifest({ shipmentIds: [...ids], vendorId: null, pickupLocationId: null })
      .subscribe({
        next: (manifest) => {
          this.busy.set(false);
          this.toasts.success(`Manifest ${manifest.reference} — ${manifest.shipmentCount} parcels.`);
          this.list.refresh();
          if (manifest.fileId) this.openManifest(manifest.fileId);
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(describeError(error, 'That manifest could not be created.'));
        },
      });
  }

  protected printLabel(parcel: ShipmentResponse): void {
    this.busy.set(true);
    this.documents.shipmentLabel(parcel.id).subscribe({
      next: (label) => {
        this.busy.set(false);
        if (!this.documents.openUrl(label.url)) {
          this.actionError.set('The label opened in a tab the browser blocked. Allow pop-ups and try again.');
        }
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That label could not be fetched.'));
      },
    });
  }

  protected sync(parcel: ShipmentResponse): void {
    this.act(this.fulfilment.syncTracking(parcel.id), 'Asked the courier.');
  }

  protected recordTracking(): void {
    const parcel = this.viewing();
    if (!parcel || this.busy()) return;

    this.act(
      this.fulfilment.recordTracking(
        parcel.id,
        this.trackingStatus().trim(),
        this.trackingRemark() || null,
        null,
      ),
      'Scan recorded.',
    );
    this.trackingStatus.set('');
    this.trackingRemark.set('');
  }

  protected cancel(reason: string): void {
    const parcel = this.cancelling();
    if (!parcel) return;
    this.cancelling.set(null);
    this.act(this.fulfilment.cancel(parcel.id, reason || null), 'Parcel cancelled.');
  }

  protected replay(id: string): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);

    this.fulfilment.replayCourierEvent(id).subscribe({
      next: () => {
        this.busy.set(false);
        this.toasts.success('Replayed.');
        this.events.refresh();
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That message could not be replayed.'));
      },
    });
  }

  /**
   * The manifest PDF.
   *
   * Stored as a media file rather than streamed from the shipping module, so it is fetched the way
   * every private file is: a short-lived signed link, requested at the moment it is needed.
   */
  private openManifest(fileId: string): void {
    this.media.link(fileId).subscribe({
      next: (link) => {
        const view = globalThis.window;
        if (!view?.open(link.url, '_blank')) {
          this.actionError.set(
            'The manifest opened in a tab the browser blocked. Allow pop-ups and try again.',
          );
        }
      },
      error: () =>
        this.actionError.set(
          'The manifest was created but its file could not be opened. Try again from the manifest list.',
        ),
    });
  }

  private act(request: ReturnType<FulfilmentService['syncTracking']>, message: string): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);

    request.subscribe({
      next: (parcel) => {
        this.busy.set(false);
        this.viewing.set(parcel);
        this.toasts.success(message);
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That could not be done.'));
      },
    });
  }
}
