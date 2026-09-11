import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import {
  CancelLineBody,
  DocumentPrintService,
  InvoiceResponse,
  OrderEventResponse,
  OrderResponse,
  OrdersAdminService,
  SubOrderResponse,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import { ConfirmDialog, Modal, PageHeader, StatusBadge, toneFor } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime, tableMoney } from '../../core/format';

/** A line being cancelled, and how much of it. */
interface CancelDraft {
  readonly orderLineId: string;
  readonly sku: string;
  readonly name: string;
  readonly cancellable: number;
  quantity: string;
}

/**
 * One order, and everything that can be done to it.
 *
 * The screen is organised around the fact that **the sub-order is the unit of work**: the order
 * carries the customer, the addresses and the money, and each sub-order carries a seller, a
 * status, an invoice and its own cancellation. There is deliberately no control that moves "the
 * order" — its status is derived from its parts and the API has no endpoint that takes one.
 *
 * Three things are worth pointing out.
 *
 * **The transition buttons are `nextStatuses`, rendered.** The API computes them from the same
 * transition table it will judge the request against (Step 14), so a screen that hard-coded
 * "Confirmed can go to Packed" would be a second copy of that table — and the copy is the one
 * that goes stale when an edge is added. Here, an edge that no longer exists simply stops being a
 * button.
 *
 * **Cancelling is one request, whole or partial.** Naming lines and quantities cancels those; not
 * naming any cancels the lot. The stock release and the refund are the API's, in the same
 * transaction — a client that ran them as three calls could stop after the second and leave stock
 * held against an order nobody is going to pay for.
 *
 * **A note's visibility is a deliberate choice each time.** The checkbox defaults to off, because
 * an internal note about a fraud check and a message to the buyer are one tick apart and the
 * mistake is not recoverable.
 */
@Component({
  selector: 'kh-order-detail-page',
  imports: [
    Alert,
    Badge,
    Button,
    Checkbox,
    ConfirmDialog,
    Control,
    Field,
    HasPermission,
    Modal,
    PageHeader,
    Skeleton,
    StatusBadge,
  ],
  template: `
    <kh-page-header
      [heading]="order()?.orderNumber || 'Order'"
      [crumbs]="[{ label: 'Orders', path: '/orders' }]"
      [description]="subtitle()"
    >
      @if (order(); as current) {
        <kh-status-badge [status]="current.status" />
        <kh-status-badge [status]="current.paymentStatus" />
      }
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="This order could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not work" [dismissible]="true">{{ message }}</kh-alert>
    }

    @if (loading()) {
      <kh-skeleton height="24rem" />
    } @else if (order(); as current) {
      <div class="layout">
        <div class="main">
          @for (part of current.subOrders; track part.id) {
            <section class="panel">
              <header class="part-head">
                <div>
                  <h2>{{ part.subOrderNumber }}</h2>
                  <p class="hint">
                    {{ part.vendorName ?? 'Seller' }} ·
                    {{ money(part.netTotal, part.currencyCode) }}
                    @if (part.dispatchDueAt; as due) {
                      · dispatch due {{ when(due) }}
                    }
                  </p>
                </div>
                <kh-status-badge [status]="part.status" />
              </header>

              <table>
                <thead>
                  <tr>
                    <th scope="col">Item</th>
                    <th scope="col" class="numeric">Qty</th>
                    <th scope="col" class="numeric">Unit</th>
                    <th scope="col" class="numeric">Tax</th>
                    <th scope="col" class="numeric">Total</th>
                  </tr>
                </thead>
                <tbody>
                  @for (line of part.lines; track line.id) {
                    <tr>
                      <td>
                        {{ line.name }}
                        <span class="note">{{ line.sku }}</span>
                        @if (line.quantityCancelled > 0) {
                          <kh-badge tone="danger">{{ line.quantityCancelled }} cancelled</kh-badge>
                        }
                        @if (line.quantityReturned > 0) {
                          <kh-badge tone="info">{{ line.quantityReturned }} returned</kh-badge>
                        }
                      </td>
                      <td class="numeric">{{ line.quantity }}</td>
                      <td class="numeric">{{ money(line.unitPrice, current.currencyCode) }}</td>
                      <td class="numeric">
                        {{ money(line.cgst + line.sgst + line.igst + line.cess, current.currencyCode) }}
                        <span class="note">{{ line.gstRate }}%</span>
                      </td>
                      <td class="numeric">{{ money(line.lineTotal, current.currencyCode) }}</td>
                    </tr>
                  }
                </tbody>
              </table>

              <div class="part-actions">
                @for (next of part.nextStatuses; track next) {
                  <button
                    *khHasPermission="'orders.order.transition'"
                    khButton
                    type="button"
                    size="sm"
                    [disabled]="busy()"
                    (click)="transition(part, next)"
                  >
                    Mark {{ next }}
                  </button>
                }

                @if (part.isCancellable) {
                  <button
                    *khHasPermission="'orders.suborder.cancel'"
                    khButton
                    type="button"
                    size="sm"
                    variant="danger"
                    [disabled]="busy()"
                    (click)="startCancel(part)"
                  >
                    Cancel
                  </button>
                }

                @if (part.invoice; as invoice) {
                  <button
                    khButton
                    type="button"
                    size="sm"
                    [disabled]="busy()"
                    (click)="downloadInvoice(invoice)"
                  >
                    Invoice {{ invoice.invoiceNumber }}
                  </button>
                } @else {
                  <button
                    *khHasPermission="'orders.invoice.manage'"
                    khButton
                    type="button"
                    size="sm"
                    [disabled]="busy()"
                    (click)="issueInvoice(part)"
                  >
                    Raise the invoice
                  </button>
                }
              </div>

              @if (part.cancellationReason; as reason) {
                <p class="hint">Cancelled: {{ reason }}</p>
              }
            </section>
          }

          <section class="panel">
            <h2>Timeline</h2>
            <ol class="timeline">
              @for (event of timeline(); track event.id) {
                <li>
                  <span class="when">{{ when(event.occurredAt) }}</span>
                  <span class="what">
                    {{ event.message || describe(event) }}
                    @if (event.isCustomerVisible) {
                      <kh-badge tone="info">Seen by the customer</kh-badge>
                    }
                  </span>
                  <span class="note">{{ event.actorType }}</span>
                </li>
              } @empty {
                <li class="hint">Nothing has happened yet.</li>
              }
            </ol>
          </section>

          <section class="panel" *khHasPermission="'orders.order.note'">
            <h2>Add a note</h2>
            <kh-field label="Note" for="order-note">
              <textarea
                khControl
                id="order-note"
                rows="3"
                maxlength="1000"
                [value]="note()"
                (input)="note.set($any($event.target).value)"
              ></textarea>
            </kh-field>

            <kh-checkbox
              label="Show this to the customer"
              description="Off means an internal note. This cannot be undone once saved."
              inputId="order-note-visible"
              [checked]="noteVisible()"
              (checkedChange)="noteVisible.set($event)"
            />

            <button
              khButton
              type="button"
              variant="primary"
              [disabled]="busy() || note().trim().length === 0"
              (click)="addNote()"
            >
              Add the note
            </button>
          </section>
        </div>

        <aside class="side">
          <section class="panel">
            <h2>Customer</h2>
            <p>{{ current.customerName }}</p>
            @if (current.customerEmail; as email) {
              <p class="note">{{ email }}</p>
            }
            @if (current.customerMobile; as mobile) {
              <p class="note">{{ mobile }}</p>
            }
          </section>

          <section class="panel">
            <h2>Delivering to</h2>
            <p>{{ current.shippingAddress.recipientName }}</p>
            <p class="note">
              {{ current.shippingAddress.line1 }}<br />
              @if (current.shippingAddress.line2; as line2) {
                {{ line2 }}<br />
              }
              {{ current.shippingAddress.city }},
              {{ current.shippingAddress.stateName ?? current.shippingAddress.stateCode }}
              {{ current.shippingAddress.pincode }}<br />
              {{ current.shippingAddress.mobile }}
            </p>
          </section>

          <section class="panel">
            <h2>Money</h2>
            <dl>
              <div>
                <dt>Items</dt>
                <dd>{{ money(current.itemsTotal, current.currencyCode) }}</dd>
              </div>
              @if (current.discountTotal > 0) {
                <div>
                  <dt>Discount</dt>
                  <dd>−{{ money(current.discountTotal, current.currencyCode) }}</dd>
                </div>
              }
              <div>
                <dt>Shipping</dt>
                <dd>{{ money(current.shippingTotal, current.currencyCode) }}</dd>
              </div>
              <div>
                <dt>Tax</dt>
                <dd>{{ money(current.taxTotal, current.currencyCode) }}</dd>
              </div>
              @if (current.codFee > 0) {
                <div>
                  <dt>Cash-on-delivery fee</dt>
                  <dd>{{ money(current.codFee, current.currencyCode) }}</dd>
                </div>
              }
              @if (current.walletApplied > 0) {
                <div>
                  <dt>Store credit</dt>
                  <dd>−{{ money(current.walletApplied, current.currencyCode) }}</dd>
                </div>
              }
              <div class="total">
                <dt>Ordered</dt>
                <dd>{{ money(current.grandTotal, current.currencyCode) }}</dd>
              </div>
              @if (current.cancelledTotal > 0) {
                <div>
                  <dt>Cancelled</dt>
                  <dd>−{{ money(current.cancelledTotal, current.currencyCode) }}</dd>
                </div>
                <div class="total">
                  <dt>Net</dt>
                  <dd>{{ money(current.netTotal, current.currencyCode) }}</dd>
                </div>
              }
            </dl>
            @if (current.couponCode; as coupon) {
              <p class="note">Coupon {{ coupon }}</p>
            }
          </section>
        </aside>
      </div>
    }

    <kh-modal
      [open]="cancelling() !== null"
      heading="Cancel part of this order"
      width="40rem"
      [dismissible]="!busy()"
      (closed)="cancelling.set(null)"
    >
      @if (cancelling(); as part) {
        <p class="hint">
          {{ part.subOrderNumber }} — leave the quantities as they are to cancel the whole part, or reduce
          them to cancel some of it. Stock is released and any payment refunded by the platform, in one step.
        </p>

        <table>
          <thead>
            <tr>
              <th scope="col">Item</th>
              <th scope="col" class="numeric">Can cancel</th>
              <th scope="col">Cancelling</th>
            </tr>
          </thead>
          <tbody>
            @for (line of cancelDraft(); track line.orderLineId; let index = $index) {
              <tr>
                <td>
                  {{ line.name }}<span class="note">{{ line.sku }}</span>
                </td>
                <td class="numeric">{{ line.cancellable }}</td>
                <td>
                  <input
                    khControl
                    khNumeric
                    [id]="'cancel-qty-' + index"
                    [attr.aria-label]="'Quantity to cancel for ' + line.name"
                    type="number"
                    min="0"
                    [attr.max]="line.cancellable"
                    [value]="line.quantity"
                    (input)="setCancelQuantity(index, $any($event.target).value)"
                  />
                </td>
              </tr>
            }
          </tbody>
        </table>

        <kh-field
          label="Reason"
          for="cancel-reason"
          hint="Recorded on the timeline and shown to the customer."
        >
          <input
            khControl
            id="cancel-reason"
            type="text"
            maxlength="200"
            [value]="cancelReason()"
            (input)="cancelReason.set($any($event.target).value)"
          />
        </kh-field>
      }

      <div slot="footer">
        <button khButton type="button" variant="tertiary" [disabled]="busy()" (click)="cancelling.set(null)">
          Keep it
        </button>
        <button khButton type="button" variant="danger" [disabled]="busy()" (click)="confirmCancel.set(true)">
          Cancel these items
        </button>
      </div>
    </kh-modal>

    <kh-confirm-dialog
      [open]="confirmCancel()"
      heading="Cancel these items"
      message="Stock goes back, any payment is refunded, and the customer is told. This cannot be undone."
      confirmLabel="Cancel them"
      [requireReason]="false"
      [busy]="busy()"
      (confirmed)="cancel()"
      (cancelled)="confirmCancel.set(false)"
    />
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .layout {
      display: grid;
      gap: var(--space-4);
    }

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: minmax(0, 1fr) 20rem;
        align-items: start;
      }
    }

    .panel {
      margin-block-end: var(--space-4);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .panel h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
    }

    .part-head {
      display: flex;
      gap: var(--space-3);
      align-items: flex-start;
      justify-content: space-between;
    }

    .part-head h2 {
      margin-block-end: var(--space-1);
    }

    .part-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin-block-start: var(--space-3);
    }

    .hint,
    .note {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .note {
      font-size: var(--text-xs);
    }

    td .note {
      display: block;
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

    .timeline {
      display: grid;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .timeline li {
      display: grid;
      grid-template-columns: 11rem 1fr auto;
      gap: var(--space-2);
      align-items: baseline;
      padding-block: var(--space-1);
      border-block-end: 1px solid var(--color-border);
      font-size: var(--text-sm);
    }

    .when {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    dl {
      margin: 0;
      font-size: var(--text-sm);
    }

    dl div {
      display: flex;
      justify-content: space-between;
      padding-block: var(--space-1);
    }

    dl dt,
    dl dd {
      margin: 0;
    }

    dl .total {
      border-block-start: 1px solid var(--color-border);
      font-weight: var(--weight-medium);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderDetailPage {
  private readonly orders = inject(OrdersAdminService);
  private readonly documents = inject(DocumentPrintService);
  private readonly route = inject(ActivatedRoute);
  private readonly toasts = inject(ToastService);

  private readonly id = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly order = signal<OrderResponse | null>(null);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);

  protected readonly note = signal('');
  protected readonly noteVisible = signal(false);

  protected readonly cancelling = signal<SubOrderResponse | null>(null);
  protected readonly confirmCancel = signal(false);
  protected readonly cancelDraft = signal<readonly CancelDraft[]>([]);
  protected readonly cancelReason = signal('');

  protected readonly subtitle = computed(() => {
    const current = this.order();
    if (!current) return null;
    return `${current.customerName} · placed ${tableDateTime(current.placedAt)}`;
  });

  /** Newest first: the question on an order detail page is always "what happened last". */
  protected readonly timeline = computed<readonly OrderEventResponse[]>(() =>
    [...(this.order()?.timeline ?? [])].sort((left, right) =>
      right.occurredAt.localeCompare(left.occurredAt),
    ),
  );

  constructor() {
    this.load();
  }

  protected tone(status: string) {
    return toneFor(status);
  }

  protected money(amount: number, currency: string): string {
    return tableMoney(amount, currency);
  }

  protected when(value: string | null): string {
    return tableDateTime(value);
  }

  /** A timeline row with no message still says something: the edge it recorded. */
  protected describe(event: OrderEventResponse): string {
    if (event.fromStatus && event.toStatus) return `${event.fromStatus} → ${event.toStatus}`;
    if (event.toStatus) return `Became ${event.toStatus}`;
    return event.type;
  }

  protected transition(part: SubOrderResponse, status: string): void {
    this.act(this.orders.transition(part.id, status, null), `${part.subOrderNumber} is now ${status}.`);
  }

  protected startCancel(part: SubOrderResponse): void {
    this.cancelling.set(part);
    this.cancelReason.set('');
    this.cancelDraft.set(
      part.lines
        .map((line) => {
          const cancellable = line.quantity - line.quantityCancelled - line.quantityReturned;
          return {
            orderLineId: line.id,
            sku: line.sku,
            name: line.name,
            cancellable,
            quantity: String(Math.max(0, cancellable)),
          };
        })
        .filter((line) => line.cancellable > 0),
    );
  }

  protected setCancelQuantity(index: number, value: string): void {
    this.cancelDraft.update((current) =>
      current.map((line, at) => (at === index ? { ...line, quantity: value } : line)),
    );
  }

  protected cancel(): void {
    const part = this.cancelling();
    if (!part || this.busy()) return;

    const draft = this.cancelDraft();
    const lines: CancelLineBody[] = draft
      .map((line) => ({ orderLineId: line.orderLineId, quantity: Number(line.quantity || 0) }))
      .filter((line) => line.quantity > 0);

    if (lines.length === 0) {
      this.actionError.set('Nothing was selected to cancel.');
      this.confirmCancel.set(false);
      return;
    }

    // Every cancellable unit selected means the whole part, and the API's own whole-part path —
    // which is what releases the shipping charge as well as the lines — is a null `lines`.
    const isWhole = draft.every((line) => Number(line.quantity || 0) === line.cancellable);

    this.confirmCancel.set(false);
    this.cancelling.set(null);

    this.act(
      this.orders.cancelSubOrder(part.id, {
        reason: this.cancelReason() || null,
        lines: isWhole ? null : lines,
      }),
      `${part.subOrderNumber} cancelled.`,
    );
  }

  protected addNote(): void {
    const current = this.order();
    if (!current || this.busy()) return;

    this.busy.set(true);
    this.actionError.set(null);

    this.orders.addNote(current.id, this.note().trim(), this.noteVisible()).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.order.set(updated);
        this.note.set('');
        this.noteVisible.set(false);
        this.toasts.success('Note added.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That note could not be added.'));
      },
    });
  }

  protected issueInvoice(part: SubOrderResponse): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);

    this.orders.issueInvoice(part.id).subscribe({
      next: (invoice) => {
        this.busy.set(false);
        this.toasts.success(`Invoice ${invoice.invoiceNumber} raised.`);
        this.load();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That invoice could not be raised.'));
      },
    });
  }

  /**
   * Opens an invoice PDF.
   *
   * The endpoint answers a short-lived signed URL rather than the bytes, so this one *can* be a
   * navigation — the signature is the authorisation and no header is needed. It is requested on
   * the click rather than held, because it expires.
   */
  protected downloadInvoice(invoice: InvoiceResponse): void {
    this.busy.set(true);
    this.orders.downloadInvoice(invoice.id).subscribe({
      next: (link) => {
        this.busy.set(false);
        const view = globalThis.window;
        if (!view?.open(link.url, '_blank')) {
          this.actionError.set(
            'The invoice opened in a tab that the browser blocked. Allow pop-ups and try again.',
          );
        }
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That invoice could not be opened.'));
      },
    });
  }

  private act(request: ReturnType<OrdersAdminService['transition']>, message: string): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);

    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.toasts.success(message);
        // Refetched whole rather than patched: a transition writes a timeline event and can move
        // the order's own derived status, and neither is in the sub-order the API answered.
        this.load();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That could not be done.'));
      },
    });
  }

  private load(): void {
    this.loading.set(this.order() === null);
    this.orders.order(this.id).subscribe({
      next: (loaded) => {
        this.loading.set(false);
        this.order.set(loaded);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'That order could not be found.'));
      },
    });
  }
}
