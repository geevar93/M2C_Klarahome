import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  DocumentPrintService,
  FulfilmentService,
  InventoryAdminService,
  OrdersAdminService,
  PickListLineResponse,
  ShipmentResponse,
  SubOrderResponse,
} from '@klarahome/data-access-admin';
import { Modal, PageHeader, StatusBadge } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime, tableMoney } from '../../core/format';

/** One line being packed, and how many of it actually went in the box. */
interface PackLine {
  readonly orderLineId: string;
  readonly sku: string;
  readonly name: string;
  readonly ordered: number;
  quantity: string;
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
  imports: [Alert, Badge, Button, Control, Field, Icon, Modal, PageHeader, Skeleton, StatusBadge],
  template: `
    <kh-page-header
      heading="Fulfilment"
      description="What has to leave the building today, and the steps that get it there."
    >
      <button khButton type="button" [disabled]="pickLoading()" (click)="loadPickList()">
        <kh-icon name="refresh" size="sm" />
        Refresh the pick list
      </button>
    </kh-page-header>

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not work" [dismissible]="true">{{ message }}</kh-alert>
    }

    <section class="panel">
      <div class="panel-head">
        <h2>Pick list</h2>
        <p class="hint">{{ pickList().length }} lines to take off the shelves.</p>

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
        <table>
          <thead>
            <tr>
              <th scope="col">SKU</th>
              <th scope="col">Item</th>
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
                <td>{{ line.sku }}</td>
                <td>{{ line.name }}</td>
                <td class="numeric">{{ line.quantity }}</td>
                <td>{{ line.subOrderNumber }}</td>
                <td>{{ line.warehouseName ?? 'Not allocated' }}</td>
                <td>{{ line.destinationPincode }}</td>
                <td>{{ when(line.dispatchDueAt) }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>

    <section class="panel">
      <div class="panel-head">
        <h2>Waiting to be packed</h2>
        <p class="hint">
          Confirmed parts with no parcel yet. Overdue ones are past the seller's dispatch cut-off.
        </p>
      </div>

      @if (queue.error(); as message) {
        <kh-alert tone="danger" heading="The queue could not be loaded">{{ message }}</kh-alert>
      }

      @if (queue.loading() && queue.rows().length === 0) {
        <kh-skeleton height="10rem" />
      } @else {
        <table>
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
                <td>
                  {{ part.subOrderNumber }}
                  <span class="note">{{ money(part.netTotal, part.currencyCode) }}</span>
                </td>
                <td>{{ part.vendorName ?? '—' }}</td>
                <td><kh-status-badge [status]="part.status" /></td>
                <td class="numeric">{{ part.lines.length }}</td>
                <td>
                  {{ when(part.dispatchDueAt) }}
                  @if (part.dispatchDueAt && isPast(part.dispatchDueAt)) {
                    <kh-badge tone="danger">Overdue</kh-badge>
                  }
                </td>
                <td>
                  <button khButton type="button" size="sm" [disabled]="busy()" (click)="startPack(part)">
                    Pack
                  </button>
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="6" class="hint">Nothing is waiting to be packed.</td>
              </tr>
            }
          </tbody>
        </table>

        <div class="pager">
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
          <li [class.done]="booked()">3. Waybill</li>
          <li>4. Hand over</li>
        </ol>

        @if (!shipment()) {
          <p class="hint">
            {{ part.subOrderNumber }} — reduce a quantity if it is not all going in this box. What is left
            behind stays on the part and can be shipped separately.
          </p>

          <table>
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
              · waybill {{ awb }} with {{ parcel.courier }}
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
              <legend>Waybill</legend>
              <p class="hint">
                Ask the courier for one, or type in a number from their own book if this deployment has no
                logistics account.
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

              <kh-field label="Waybill typed by hand" for="pack-awb" [optional]="true">
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
          <button khButton type="button" variant="primary" [disabled]="busy()" (click)="createAndPack()">
            Pack it
          </button>
        } @else if (!weighed()) {
          <button khButton type="button" variant="primary" [disabled]="busy()" (click)="weigh()">
            Record the weight
          </button>
        } @else if (!booked()) {
          <button khButton type="button" variant="primary" [disabled]="busy()" (click)="book()">
            Get a waybill
          </button>
        } @else {
          <button khButton type="button" [disabled]="busy()" (click)="printLabel()">Print the label</button>
          <button khButton type="button" variant="primary" [disabled]="busy()" (click)="dispatch()">
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
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .panel-head {
      display: flex;
      gap: var(--space-3);
      align-items: baseline;
      justify-content: space-between;
      margin-block-end: var(--space-3);
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

    tr.overdue td {
      background: var(--color-surface);
      font-weight: var(--weight-medium);
    }

    .pager {
      display: flex;
      gap: var(--space-2);
      justify-content: flex-end;
      margin-block-start: var(--space-3);
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

  /** Confirmed parts are the ones with stock held and nothing packed. */
  protected readonly queue = this.orders.subOrders({ status: 'Confirmed' });

  protected readonly pickList = signal<readonly PickListLineResponse[]>([]);
  protected readonly pickLoading = signal(false);

  /** The stock location both halves of this screen are about, or blank for all of them. */
  protected readonly warehouseFilter = signal('');

  /** Every warehouse, for the filter. There are few enough that paging one is a control nobody uses. */
  protected readonly warehouses = inject(InventoryAdminService).warehouses({}, 100);
  protected readonly busy = signal(false);
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
    this.packing.set(part);
    this.shipment.set(null);
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
  }

  protected closePack(): void {
    if (this.busy()) return;
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
          this.toasts.success(updated.awb ? `Waybill ${updated.awb}.` : 'Booked.');
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(
            describeError(
              error,
              'No waybill could be got. Type one in from the courier’s own book to carry on.',
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
