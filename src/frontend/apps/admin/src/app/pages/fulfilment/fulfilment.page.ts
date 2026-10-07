import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { of, switchMap } from 'rxjs';
import {
  DocumentPrintService,
  FulfilmentService,
  InventoryAdminService,
  OrdersAdminService,
  PickListLineResponse,
  ShipmentResponse,
  SubOrderResponse,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import { Modal, PageHeader, StatusBadge, toneFor } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime, tableMoney } from '../../core/format';
import { SUB_ORDER_STATUS_VOCAB, statusLabel, statusTooltip } from '../orders/order-vocabulary';

/** One line being packed, and how many of it actually went in the box. */
interface PackLine {
  readonly orderLineId: string;
  readonly sku: string;
  readonly name: string;
  readonly ordered: number;
  quantity: string;
}

/** Parcel statuses that mean it is packed or booked but has not left the building. */
const OPEN_PARCEL_STATUSES: ReadonlySet<string> = new Set([
  'Draft',
  'Created',
  'LabelGenerated',
  'PickupScheduled',
]);

interface ParcelSummary {
  readonly id: string;
  readonly status: string;
  readonly createdAt: string;
  readonly awb: string | null;
  readonly weightGrams: number;
}

/** The newest parcel that has not left and is not cancelled, or null. Exported for the spec. */
export function pickOpenParcel(parcels: readonly ParcelSummary[]): ParcelSummary | null {
  const open = parcels
    .filter((parcel) => OPEN_PARCEL_STATUSES.has(parcel.status))
    .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
  return open[0] ?? null;
}

/**
 * The parcel to pick the modal up with, or null to start at the pack step.
 *
 * A Confirmed part normally already has an empty Draft parcel (weight 0, no waybill) opened for it
 * when the order was confirmed, so a parcel existing is not proof anything was done. Work counts as
 * done when the parcel has a weight or a waybill, or when the part has moved past Confirmed. The
 * part's status is only a hint — the queue row it came from can be stale — so the parcel decides.
 */
export function pickResumableParcel(
  parcels: readonly ParcelSummary[],
  partStatus: string,
): ParcelSummary | null {
  const open = pickOpenParcel(parcels);
  if (!open) return null;
  return open.awb || open.weightGrams > 0 || partStatus !== 'Confirmed' ? open : null;
}

/**
 * Getting parcels out of the door.
 *
 * This is the warehouse's screen, and it is arranged as the work happens rather than as the data
 * is shaped: **the queue of parts to pack**, then **the pick list to print**, then one part at a
 * time through pack → weigh → book → dispatch.
 *
 * The order of those four is not a convention, it is the constraint:
 *
 *  - **Pack before weigh**, because what went in the box decides what the box weighs, and a
 *    short-shipped line changes both.
 *  - **Weigh before book**, because the courier's price is quoted against the weight and the
 *    dimensions — the greater of actual and volumetric — and booking first means booking a price
 *    that will be corrected by the courier's own scales, at their rate, three weeks later.
 *  - **Book before dispatch**, because dispatch is the handover and the waybill is what is handed
 *    over.
 *
 * The screen offers exactly the next step for the shipment in hand, and nothing else. A row of
 * five buttons where four are wrong is how a warehouse dispatches an unweighed parcel.
 *
 * **Booking can fail for reasons nobody here can fix** — no aggregator account, an unserviceable
 * PIN code, an API that is down. The manual waybill beside it is the Step 16A answer: a
 * deployment with no logistics integration still dispatches, with the number from the courier's
 * own book typed in.
 */
@Component({
  selector: 'kh-fulfilment-page',
  imports: [
    HasPermission,
    Alert,
    Badge,
    Button,
    Control,
    Field,
    Icon,
    Modal,
    PageHeader,
    Skeleton,
    StatusBadge,
  ],
  template: `
    <kh-page-header
      heading="To pack"
      description="What has to leave the building today, and the steps that get it there."
    />

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="Something went wrong" [dismissible]="true">{{ message }}</kh-alert>
    }

    <section class="panel kh-panel">
      <div class="panel-head">
        <h2>Pick list</h2>
        <p class="hint">{{ pickList().length }} lines to take off the shelves.</p>
        <!-- Beside the list it refreshes, and a plain button: it was the page's only filled action. -->
        <button khButton type="button" size="sm" [disabled]="pickLoading()" (click)="loadPickList()">
          <kh-icon name="refresh" size="sm" />
          Refresh
        </button>

        <kh-field label="Warehouse" for="pick-warehouse">
          <select
            khControl
            id="pick-warehouse"
            [value]="warehouseFilter()"
            (change)="setWarehouse($any($event.target).value)"
          >
            <option value="">Every warehouse</option>
            @for (warehouse of warehouses.rows(); track warehouse.id) {
              <option [value]="warehouse.id">{{ warehouse.name }} ({{ warehouse.code }})</option>
            }
          </select>
        </kh-field>
      </div>

      @if (pickLoading()) {
        <kh-skeleton height="10rem" />
      } @else if (pickList().length === 0) {
        <p class="hint">Nothing is waiting to be picked.</p>
      } @else {
        <!-- Cards on a phone (kh-cards, _base.scss): a picker walks the shelves with the phone in
             one hand, and a seven-column table that scrolls sideways is not a pick list there.
             The item leads because it is what they are looking for; the SKU is what they check. -->
        <table class="kh-table kh-cards">
          <thead>
            <tr>
              <th scope="col">Item</th>
              <th scope="col">SKU</th>
              <th scope="col" class="numeric">Qty</th>
              <th scope="col">Order</th>
              <th scope="col">Shelf</th>
              <th scope="col">To</th>
              <th scope="col">Due</th>
            </tr>
          </thead>
          <tbody>
            @for (line of pickList(); track line.shipmentId + line.sku) {
              <tr [class.overdue]="isOverdue(line)">
                <td class="kh-cards-title">{{ line.name }}</td>
                <td data-label="SKU">{{ line.sku }}</td>
                <td class="numeric" data-label="Qty">{{ line.quantity }}</td>
                <td class="kh-cards-extra" data-label="Order">{{ line.subOrderNumber }}</td>
                <td data-label="Shelf">{{ line.warehouseName ?? 'Not allocated' }}</td>
                <td class="kh-cards-extra" data-label="To">{{ line.destinationPincode }}</td>
                <td data-label="Due">{{ when(line.dispatchDueAt) }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>

    <section class="panel kh-panel">
      <div class="panel-head">
        <h2>Waiting to leave</h2>
        <p class="hint">
          Parts not yet handed to the courier, at whatever step they have reached. Overdue ones are past the
          seller's dispatch cut-off.
        </p>
      </div>

      @if (queue.error(); as message) {
        <kh-alert tone="danger" heading="The queue could not be loaded">{{ message }}</kh-alert>
      }

      @if (queue.loading() && queue.rows().length === 0) {
        <kh-skeleton height="10rem" />
      } @else {
        <table class="kh-table kh-cards">
          <thead>
            <tr>
              <th scope="col">Part</th>
              <th scope="col">Seller</th>
              <th scope="col">Status</th>
              <th scope="col" class="numeric">Items</th>
              <th scope="col">Dispatch due</th>
              <th scope="col"><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (part of queue.rows(); track part.id) {
              <tr>
                <td class="kh-cards-title">
                  {{ part.subOrderNumber }}
                  <span class="note">{{ money(part.netTotal, part.currencyCode) }}</span>
                </td>
                <td class="kh-cards-extra" data-label="Seller">{{ part.vendorName ?? '—' }}</td>
                <td data-label="Status">
                  <span [title]="statusTooltip(part.status)">
                    <kh-status-badge [status]="part.status" [label]="statusLabelFor(part.status)" />
                  </span>
                </td>
                <td class="numeric kh-cards-extra" data-label="Items">{{ part.lines.length }}</td>
                <td data-label="Dispatch due">
                  {{ when(part.dispatchDueAt) }}
                  @if (part.dispatchDueAt && isPast(part.dispatchDueAt)) {
                    <kh-badge tone="danger">Overdue</kh-badge>
                  }
                </td>
                <td class="action">
                  <button
                    khButton
                    type="button"
                    size="sm"
                    *khHasPermission="'shipping.shipment.manage'"
                    [disabled]="busy()"
                    (click)="startPack(part)"
                  >
                    {{ part.status === 'Confirmed' ? 'Pack' : 'Continue' }}
                  </button>
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="6" class="hint">Nothing is waiting to leave the building.</td>
              </tr>
            }
          </tbody>
        </table>

        <div class="pager">
          <span class="hint">{{ queue.rows().length }} shown</span>
          <button
            khButton
            type="button"
            size="sm"
            [disabled]="!queue.hasPrevious() || queue.loading()"
            (click)="queue.previous()"
          >
            Previous
          </button>
          <button
            khButton
            type="button"
            size="sm"
            [disabled]="!queue.nextCursor() || queue.loading()"
            (click)="queue.next()"
          >
            Next
          </button>
        </div>
      }
    </section>

    <kh-modal
      [open]="packing() !== null"
      heading="Pack this part"
      width="44rem"
      [dismissible]="!busy()"
      (closed)="closePack()"
    >
      @if (packing(); as part) {
        <ol class="steps">
          <li [class.done]="shipment() !== null">1. What is in the box</li>
          <li [class.done]="weighed()">2. Weight and size</li>
          <li [class.done]="booked()">3. Tracking number</li>
          <li>4. Hand over</li>
        </ol>

        @if (resuming()) {
          <p class="hint">Looking up the parcel for {{ part.subOrderNumber }}…</p>
          <kh-skeleton height="6rem" />
        } @else if (!shipment()) {
          <p class="hint">
            {{ part.subOrderNumber }} — reduce a quantity if it is not all going in this box. What is left
            behind stays on the part and can be shipped separately.
          </p>

          <table class="kh-table">
            <thead>
              <tr>
                <th scope="col">Item</th>
                <th scope="col" class="numeric">Ordered</th>
                <th scope="col">Packing</th>
              </tr>
            </thead>
            <tbody>
              @for (line of packLines(); track line.orderLineId; let index = $index) {
                <tr>
                  <td>
                    {{ line.name }}<span class="note">{{ line.sku }}</span>
                  </td>
                  <td class="numeric">{{ line.ordered }}</td>
                  <td>
                    <input
                      khControl
                      khNumeric
                      [id]="'pack-qty-' + index"
                      [attr.aria-label]="'Quantity packed for ' + line.name"
                      type="number"
                      min="0"
                      [attr.max]="line.ordered"
                      [value]="line.quantity"
                      (input)="setPackQuantity(index, $any($event.target).value)"
                    />
                  </td>
                </tr>
              }
            </tbody>
          </table>
        } @else if (shipment(); as parcel) {
          <p class="hint">
            Parcel {{ parcel.id }} · {{ parcel.status }}
            @if (parcel.awb; as awb) {
              · tracking number {{ awb }} with {{ parcel.courier }}
            }
          </p>

          @if (!weighed()) {
            <fieldset>
              <legend>Weight and size</legend>
              <p class="hint">
                The courier charges the greater of the actual weight and length × width × height ÷ 5000.
                Measure the box, not the product.
              </p>

              <div class="grid">
                <kh-field label="Weight (grams)" for="pack-weight">
                  <input
                    khControl
                    khNumeric
                    id="pack-weight"
                    type="number"
                    min="1"
                    [value]="weight()"
                    (input)="weight.set($any($event.target).value)"
                  />
                </kh-field>
                <kh-field label="Length (cm)" for="pack-length">
                  <input
                    khControl
                    khNumeric
                    id="pack-length"
                    type="number"
                    min="1"
                    [value]="lengthCm()"
                    (input)="lengthCm.set($any($event.target).value)"
                  />
                </kh-field>
                <kh-field label="Width (cm)" for="pack-width">
                  <input
                    khControl
                    khNumeric
                    id="pack-width"
                    type="number"
                    min="1"
                    [value]="widthCm()"
                    (input)="widthCm.set($any($event.target).value)"
                  />
                </kh-field>
                <kh-field label="Height (cm)" for="pack-height">
                  <input
                    khControl
                    khNumeric
                    id="pack-height"
                    type="number"
                    min="1"
                    [value]="heightCm()"
                    (input)="heightCm.set($any($event.target).value)"
                  />
                </kh-field>
              </div>

              @if (volumetric() > 0) {
                <p class="hint">
                  Volumetric weight {{ volumetric() }} g — the courier will bill on {{ chargeable() }} g.
                </p>
              }
            </fieldset>
          } @else if (!booked()) {
            <fieldset>
              <legend>Tracking number</legend>
              <p class="hint">
                Get a tracking number from the courier, or type one in by hand if you booked it another way.
              </p>

              <kh-field
                label="Courier"
                for="pack-courier"
                [optional]="true"
                hint="Leave blank to let the platform choose."
              >
                <input
                  khControl
                  id="pack-courier"
                  type="text"
                  [value]="courier()"
                  (input)="courier.set($any($event.target).value)"
                />
              </kh-field>

              <kh-field label="Tracking number, typed by hand" for="pack-awb" [optional]="true">
                <input
                  khControl
                  id="pack-awb"
                  type="text"
                  [value]="manualAwb()"
                  (input)="manualAwb.set($any($event.target).value)"
                />
              </kh-field>
            </fieldset>
          } @else {
            <p class="hint">
              Print the label, put it on the box, and hand it over. Dispatching is the last step and tells the
              customer it is on its way.
            </p>
          }
        }
      }

      <div slot="footer">
        <button khButton type="button" variant="tertiary" [disabled]="busy()" (click)="closePack()">
          Close
        </button>

        @if (!shipment()) {
          <button
            khButton
            type="button"
            variant="primary"
            [disabled]="busy() || resuming()"
            (click)="createAndPack()"
          >
            Pack it
          </button>
        } @else if (!weighed()) {
          <button khButton type="button" variant="primary" [disabled]="busy()" (click)="weigh()">
            Record the weight
          </button>
        } @else if (!booked()) {
          <button khButton type="button" variant="primary" [disabled]="busy()" (click)="book()">
            Get a tracking number
          </button>
        } @else {
          <button khButton type="button" [disabled]="busy()" (click)="printLabel()">Print the label</button>
          <button
            khButton
            type="button"
            variant="primary"
            *khHasPermission="'orders.order.transition'"
            [disabled]="busy()"
            (click)="dispatch()"
          >
            Hand over to the courier
          </button>
        }
      </div>
    </kh-modal>
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .panel {
      margin-block-end: var(--space-5);
    }

    /* Wraps: on a phone the heading, its hint and the warehouse select stack rather than share a
       390px line three ways. */
    .panel-head {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2) var(--space-3);
      align-items: baseline;
      justify-content: space-between;
      margin-block-end: var(--space-3);
    }

    .panel-head h2 {
      flex: 1 1 100%;
    }

    @media (min-width: 768px) {
      .panel-head h2 {
        flex: none;
      }
    }

    .panel h2 {
      margin: 0;
      font-size: var(--text-lg);
    }

    .hint,
    .note {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .note {
      display: block;
      font-size: var(--text-xs);
    }

    /* On a card the Pack button is the whole width and the last thing on it: the one action,
       under the thumb. From \`md\` it is a cell again. */
    .action {
      display: block;
      padding-block-start: var(--space-2);
    }

    .action button[khButton] {
      inline-size: 100%;
    }

    @media (min-width: 768px) {
      .action {
        display: table-cell;
      }

      .action button[khButton] {
        inline-size: auto;
      }
    }

    @media (pointer: coarse) {
      button[khButton] {
        min-block-size: 44px;
      }
    }

    .numeric {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }

    tr.overdue td {
      background: var(--color-surface);
      font-weight: var(--weight-medium);
    }

    .pager {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      justify-content: flex-end;
      margin-block-start: var(--space-3);
    }

    .pager .hint {
      margin: 0;
      margin-inline-end: auto;
    }

    .steps {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      margin: 0 0 var(--space-4);
      padding: 0;
      list-style: none;
      font-size: var(--text-sm);
    }

    .steps li {
      color: var(--color-text-muted);
    }

    .steps li.done {
      color: var(--color-text);
      font-weight: var(--weight-medium);
    }

    fieldset {
      margin: 0 0 var(--space-3);
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    legend {
      padding-inline: var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
      gap: var(--space-3);
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
export class FulfilmentPage {
  private readonly fulfilment = inject(FulfilmentService);
  private readonly orders = inject(OrdersAdminService);
  private readonly documents = inject(DocumentPrintService);
  private readonly toasts = inject(ToastService);

  /**
   * Everything not yet handed over. Packing and booking move the part's own status on, so a queue
   * of `Confirmed` alone would lose a part the moment it was packed — and with it the way back to
   * the waybill and the handover.
   */
  protected readonly queue = this.orders.subOrders({ status: 'Confirmed,Processing,Packed' });

  protected readonly pickList = signal<readonly PickListLineResponse[]>([]);
  protected readonly pickLoading = signal(false);

  /** The stock location both halves of this screen are about, or blank for all of them. */
  protected readonly warehouseFilter = signal('');

  /** Every warehouse, for the filter. There are few enough that paging one is a control nobody uses. */
  protected readonly warehouses = inject(InventoryAdminService).warehouses({}, 100);
  protected readonly busy = signal(false);
  /** Looking up the parcel of a part that is already under way. */
  protected readonly resuming = signal(false);
  /** Bumped on every open and close, so a slow lookup cannot land in a modal that has moved on. */
  private openToken = 0;
  protected readonly actionError = signal<string | null>(null);

  protected readonly packing = signal<SubOrderResponse | null>(null);
  protected readonly shipment = signal<ShipmentResponse | null>(null);
  protected readonly packLines = signal<readonly PackLine[]>([]);

  protected readonly weight = signal('');
  protected readonly lengthCm = signal('');
  protected readonly widthCm = signal('');
  protected readonly heightCm = signal('');
  protected readonly courier = signal('');
  protected readonly manualAwb = signal('');

  /** A weight has been captured once the API says the shipment has one. */
  protected readonly weighed = computed(() => (this.shipment()?.weightGrams ?? 0) > 0);
  protected readonly booked = computed(() => !!this.shipment()?.awb);

  /** The courier's own formula: L × W × H ÷ 5000, in kilograms, rendered as grams. */
  protected readonly volumetric = computed(() => {
    const l = Number(this.lengthCm() || 0);
    const w = Number(this.widthCm() || 0);
    const h = Number(this.heightCm() || 0);
    if (l <= 0 || w <= 0 || h <= 0) return 0;
    return Math.round(((l * w * h) / 5000) * 1000);
  });

  protected readonly chargeable = computed(() => Math.max(this.volumetric(), Number(this.weight() || 0)));

  constructor() {
    this.queue.load();
    this.warehouses.load();
    this.loadPickList();
  }

  protected money(amount: number, currency: string): string {
    return tableMoney(amount, currency);
  }

  protected when(value: string | null): string {
    return tableDateTime(value) || '—';
  }

  protected tone(status: string) {
    return toneFor(status);
  }

  protected statusLabelFor(status: string): string {
    return statusLabel(SUB_ORDER_STATUS_VOCAB, status);
  }

  protected statusTooltip(status: string): string {
    return statusTooltip(SUB_ORDER_STATUS_VOCAB, status);
  }

  protected isPast(value: string): boolean {
    return new Date(value).getTime() < Date.now();
  }

  protected isOverdue(line: PickListLineResponse): boolean {
    return !!line.dispatchDueAt && this.isPast(line.dispatchDueAt);
  }

  /**
   * Narrows both halves of this screen to one stock location.
   *
   * The pick list and the queue below it are the same warehouse's work, so one control moves both.
   * With two warehouses and no filter, each picker is handed every parcel (Step 28B, deliverable 12).
   */
  protected setWarehouse(warehouseId: string): void {
    this.warehouseFilter.set(warehouseId);
    this.queue.setFilters({ ...this.queue.filters(), warehouseId: warehouseId || undefined });
    this.loadPickList();
  }

  protected loadPickList(): void {
    this.pickLoading.set(true);
    this.fulfilment.pickList(undefined, this.warehouseFilter() || undefined).subscribe({
      next: (lines) => {
        this.pickLoading.set(false);
        this.pickList.set(lines);
      },
      error: (error: unknown) => {
        this.pickLoading.set(false);
        this.actionError.set(describeError(error, 'The pick list could not be loaded.'));
      },
    });
  }

  // ---- The four steps ---------------------------------------------------------------------------

  protected startPack(part: SubOrderResponse): void {
    const token = ++this.openToken;
    this.packing.set(part);
    this.shipment.set(null);
    this.resuming.set(false);
    this.weight.set('');
    this.lengthCm.set('');
    this.widthCm.set('');
    this.heightCm.set('');
    this.courier.set('');
    this.manualAwb.set('');
    this.packLines.set(
      part.lines
        .map((line) => {
          const outstanding = line.quantity - line.quantityCancelled - line.quantityReturned;
          return {
            orderLineId: line.id,
            sku: line.sku,
            name: line.name,
            ordered: outstanding,
            quantity: String(Math.max(0, outstanding)),
          };
        })
        .filter((line) => line.ordered > 0),
    );

    // The row may be stale, so the status alone cannot say there is nothing to resume. Always look.
    this.resume(part, token);
  }

  /**
   * Finds the part's parcel that is still in the building — the newest of Draft, Created,
   * LabelGenerated or PickupScheduled — when it shows work already done (see `pickResumableParcel`), and loads it in full, so `weighed()` and `booked()` put the
   * modal on the right step. No such parcel is not an error: the pack step stays. A failed lookup
   * is, because "Pack it" would then risk a duplicate.
   */
  private resume(part: SubOrderResponse, token: number): void {
    this.resuming.set(true);
    this.actionError.set(null);

    this.fulfilment
      .parcelsFor(part.id)
      .pipe(
        switchMap((parcels) => {
          const open = pickResumableParcel(parcels, part.status);
          return open ? this.fulfilment.shipment(open.id) : of(null);
        }),
      )
      .subscribe({
        next: (parcel) => {
          if (token !== this.openToken) return;
          this.resuming.set(false);
          this.shipment.set(parcel);
        },
        error: (error: unknown) => {
          if (token !== this.openToken) return;
          this.resuming.set(false);
          this.actionError.set(describeError(error, 'The parcel for this part could not be looked up.'));
        },
      });
  }

  protected closePack(): void {
    if (this.busy()) return;
    this.openToken++;
    this.resuming.set(false);
    this.packing.set(null);
    this.shipment.set(null);
  }

  protected setPackQuantity(index: number, value: string): void {
    this.packLines.update((current) =>
      current.map((line, at) => (at === index ? { ...line, quantity: value } : line)),
    );
  }

  /**
   * Opens the parcel and records what is in it.
   *
   * One request: `CreateShipmentBody` carries the packed lines, so a shipment never exists in the
   * "created but nobody knows what is in it" state that two calls would produce.
   */
  protected createAndPack(): void {
    const part = this.packing();
    if (!part || this.busy()) return;

    const lines = this.packLines()
      .map((line) => ({ orderLineId: line.orderLineId, quantity: Number(line.quantity || 0) }))
      .filter((line) => line.quantity > 0);

    if (lines.length === 0) {
      this.actionError.set('Nothing was packed. Enter at least one quantity.');
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);

    this.fulfilment
      .create(part.id, {
        lines,
        // Zero until the box is on the scales. The weigh step is what fills these in, and the
        // screen will not offer a waybill until it has.
        weight: 0,
        dimensions: null,
        courier: null,
        pickupLocationId: null,
        manualAwb: null,
        manualCourier: null,
      })
      .subscribe({
        next: (parcel) => {
          this.busy.set(false);
          this.shipment.set(parcel);
          // The part is now Processing; the row behind the modal should say so.
          this.queue.refresh();
          this.toasts.success('Packed. Now weigh the box.');
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(describeError(error, 'That parcel could not be opened.'));
        },
      });
  }

  protected weigh(): void {
    const parcel = this.shipment();
    if (!parcel || this.busy()) return;

    const grams = Number(this.weight() || 0);
    if (grams <= 0) {
      this.actionError.set('Enter the weight of the packed box, in grams.');
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);

    this.fulfilment
      .weigh(parcel.id, {
        weight: grams,
        dimensions: {
          lengthCm: Number(this.lengthCm() || 0),
          widthCm: Number(this.widthCm() || 0),
          heightCm: Number(this.heightCm() || 0),
        },
      })
      .subscribe({
        next: (updated) => {
          this.busy.set(false);
          this.shipment.set(updated);
          this.toasts.success('Weight recorded.');
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(describeError(error, 'The weight could not be recorded.'));
        },
      });
  }

  protected book(): void {
    const parcel = this.shipment();
    if (!parcel || this.busy()) return;

    this.busy.set(true);
    this.actionError.set(null);

    const manual = this.manualAwb().trim();
    this.fulfilment
      .book(parcel.id, {
        courier: this.courier() || null,
        pickupLocationId: null,
        manualAwb: manual || null,
        // The courier that holds a hand-written waybill is the one typed beside it, and the API
        // needs both to route a later tracking update to the right adapter.
        manualCourier: manual ? this.courier() || 'Manual' : null,
      })
      .subscribe({
        next: (updated) => {
          this.busy.set(false);
          this.shipment.set(updated);
          // Booking moves the part to Packed.
          this.queue.refresh();
          this.toasts.success(updated.awb ? `Tracking number ${updated.awb}.` : 'Booked.');
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(
            describeError(
              error,
              'Couldn’t get a tracking number. You can type one in from the courier’s paperwork.',
            ),
          );
        },
      });
  }

  protected printLabel(): void {
    const parcel = this.shipment();
    if (!parcel) return;

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

  protected dispatch(): void {
    const parcel = this.shipment();
    if (!parcel || this.busy()) return;

    this.busy.set(true);
    this.actionError.set(null);

    this.fulfilment.dispatch(parcel.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.packing.set(null);
        this.shipment.set(null);
        this.toasts.success('Handed over. The customer has been told.');
        this.queue.refresh();
        this.loadPickList();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That parcel could not be dispatched.'));
      },
    });
  }
}
