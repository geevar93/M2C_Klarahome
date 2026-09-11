import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import {
  CreditNoteResponse,
  OrdersAdminService,
  QcLineRequest,
  ReturnDisposition,
  ReturnReasonResponse,
  ReturnResponse,
  ReturnsAdminService,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  ConfirmDialog,
  EntityOption,
  EntityPicker,
  Modal,
  PageHeader,
  StatusBadge,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Control, Field, Skeleton } from '@klarahome/ui-primitives';
import { Observable, map, of } from 'rxjs';

import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime, tableMoney } from '../../core/format';

/** One line being inspected: what came back, how much of it is good, and where it goes. */
interface QcDraft {
  readonly returnLineId: string;
  readonly sku: string;
  readonly name: string;
  readonly quantity: number;
  accepted: string;
  disposition: ReturnDisposition;
}

/**
 * Where an accepted unit goes. Rejected units are recorded but do not return to stock.
 *
 * Typed against `ReturnDisposition` since Step 28B, and typing it found a value that never existed:
 * this screen offered `Damaged`, and the enum's third member is `Quarantine`. Every inspection that
 * chose it was refused (deliverable 11).
 *
 * `Pending` is deliberately not offered: it is what a line says before anybody has graded it, and
 * the API refuses it as a verdict.
 */
const DISPOSITIONS: readonly { readonly value: ReturnDisposition; readonly label: string }[] = [
  { value: 'Restock', label: 'Back on the shelf — saleable' },
  { value: 'Quarantine', label: 'Held for a decision — moves no stock' },
  { value: 'Scrap', label: 'Scrapped — written off supply' },
];

/**
 * One return, from the decision to the money going back.
 *
 * **Every button on this page is an entry in `nextStatuses`.** The API computes the edges from the
 * transition table that will judge the request (Step 17, where the table is data precisely so a
 * back office cannot invent an edge), so this screen never decides that a received return may be
 * refunded — it reads that it may. An edge removed on the server stops being a button here with
 * no change to this file.
 *
 * **QC comes before the stock moves, and it is per line.** A two-item return is routinely one
 * saleable unit and one not, and a single pass/fail would restock both or neither. The disposition
 * on each line is what decides where the unit goes; only `Restock` puts it back on sale.
 *
 * **The refund and the credit note are two records.** Money going back to the customer is the
 * refund; the credit note reduces the seller's output tax and exists whether or not any money
 * moved — a store-credit refund still needs one. So it is fetched separately and shown as its own
 * fact rather than as a field on the return.
 *
 * The reason code's own policy is displayed alongside, because it is what explains the defaults:
 * whether the goods have to come back at all, whether QC is required, and who pays the return
 * freight are properties of the reason, not of this screen.
 */
@Component({
  selector: 'kh-return-detail-page',
  imports: [
    Alert,
    Badge,
    Button,
    ConfirmDialog,
    Control,
    EntityPicker,
    Field,
    HasPermission,
    Modal,
    PageHeader,
    Skeleton,
    StatusBadge,
  ],
  template: `
    <kh-page-header
      [heading]="rma()?.returnNumber || 'Return'"
      [crumbs]="[{ label: 'Returns', path: '/returns' }]"
      [description]="subtitle()"
    >
      @if (rma(); as current) {
        <kh-status-badge [status]="current.status" />
      }
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="This return could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not work" [dismissible]="true">{{ message }}</kh-alert>
    }

    @if (loading()) {
      <kh-skeleton height="22rem" />
    } @else if (rma(); as current) {
      <div class="layout">
        <div class="main">
          <section class="panel">
            <h2>What is coming back</h2>
            <table>
              <thead>
                <tr>
                  <th scope="col">Item</th>
                  <th scope="col" class="numeric">Asked</th>
                  <th scope="col" class="numeric">Accepted</th>
                  <th scope="col">Where it went</th>
                  <th scope="col" class="numeric">Refund</th>
                </tr>
              </thead>
              <tbody>
                @for (line of current.lines; track line.id) {
                  <tr>
                    <td>
                      {{ line.name }}<span class="note">{{ line.sku }}</span>
                    </td>
                    <td class="numeric">{{ line.quantity }}</td>
                    <td class="numeric">{{ line.quantityAccepted }}</td>
                    <td>
                      {{ line.disposition || '—' }}
                      @if (line.qcNote; as note) {
                        <span class="note">{{ note }}</span>
                      }
                    </td>
                    <td class="numeric">
                      {{ money(line.acceptedRefund || line.refundAmount, current.currencyCode) }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </section>

          <section class="panel">
            <h2>What can be done now</h2>
            <p class="hint">
              These come from the API's own transition table, so what is offered here is exactly what it will
              accept.
            </p>

            <div class="actions">
              @if (can('Approved')) {
                <button
                  *khHasPermission="'returns.return.decide'"
                  khButton
                  type="button"
                  variant="primary"
                  [disabled]="busy()"
                  (click)="approving.set(true)"
                >
                  Approve
                </button>
              }
              @if (can('Rejected')) {
                <button
                  *khHasPermission="'returns.return.decide'"
                  khButton
                  type="button"
                  variant="danger"
                  [disabled]="busy()"
                  (click)="rejecting.set(true)"
                >
                  Reject
                </button>
              }
              @if (can('PickupScheduled')) {
                <button
                  *khHasPermission="'returns.return.decide'"
                  khButton
                  type="button"
                  [disabled]="busy()"
                  (click)="schedulingPickup.set(true)"
                >
                  Arrange the pickup
                </button>
              }
              @if (can('Received')) {
                <button
                  *khHasPermission="'returns.qc.manage'"
                  khButton
                  type="button"
                  [disabled]="busy()"
                  (click)="receive()"
                >
                  Mark it received
                </button>
              }
              @if (can('Inspected')) {
                <button
                  *khHasPermission="'returns.qc.manage'"
                  khButton
                  type="button"
                  [disabled]="busy()"
                  (click)="startQc(current)"
                >
                  Inspect it
                </button>
              }
              @if (can('Refunded')) {
                <button
                  *khHasPermission="'returns.refund.manage'"
                  khButton
                  type="button"
                  variant="primary"
                  [disabled]="busy()"
                  (click)="refunding.set(true)"
                >
                  Refund
                </button>
              }
              @if (can('Replaced')) {
                <button
                  *khHasPermission="'returns.refund.manage'"
                  khButton
                  type="button"
                  [disabled]="busy()"
                  (click)="startReplace()"
                >
                  Send a replacement
                </button>
              }
              @if (can('Closed')) {
                <button khButton type="button" [disabled]="busy()" (click)="close()">Close it</button>
              }
              @if (current.nextStatuses.length === 0) {
                <p class="hint">Nothing further. This return is finished.</p>
              }
            </div>
          </section>

          @if (creditNote(); as note) {
            <section class="panel">
              <h2>Credit note {{ note.creditNoteNumber }}</h2>
              <p class="hint">
                Reduces the seller's output tax for {{ note.financialYear }}. It exists whether or not money
                moved — a store-credit refund needs one too.
              </p>
              <dl class="facts">
                <div>
                  <dt>Taxable value</dt>
                  <dd>{{ money(note.taxableValue, note.currencyCode) }}</dd>
                </div>
                @if (note.cgst > 0) {
                  <div>
                    <dt>CGST</dt>
                    <dd>{{ money(note.cgst, note.currencyCode) }}</dd>
                  </div>
                  <div>
                    <dt>SGST</dt>
                    <dd>{{ money(note.sgst, note.currencyCode) }}</dd>
                  </div>
                }
                @if (note.igst > 0) {
                  <div>
                    <dt>IGST</dt>
                    <dd>{{ money(note.igst, note.currencyCode) }}</dd>
                  </div>
                }
                <div class="total">
                  <dt>Total</dt>
                  <dd>{{ money(note.total, note.currencyCode) }}</dd>
                </div>
              </dl>
            </section>
          }
        </div>

        <aside class="side">
          <section class="panel">
            <h2>Why</h2>
            <p>
              <strong>{{ reason()?.label ?? current.reasonCode }}</strong>
            </p>
            @if (current.reasonNote; as note) {
              <p class="note">“{{ note }}”</p>
            }
            @if (reason(); as policy) {
              <ul class="policy">
                <li>
                  {{ policy.isPickupRequired ? 'The goods come back' : 'The goods stay with the customer' }}
                </li>
                <li>{{ policy.requiresQc ? 'Inspection required' : 'No inspection needed' }}</li>
                <li>Return freight paid by {{ policy.shippingPayer }}</li>
                @if (policy.isVendorFault) {
                  <li>Counted against the seller</li>
                }
              </ul>
            }
          </section>

          <section class="panel">
            <h2>Money</h2>
            <dl class="facts">
              <div>
                <dt>Estimated</dt>
                <dd>{{ money(current.estimatedRefund, current.currencyCode) }}</dd>
              </div>
              @if (current.approvedAmount > 0) {
                <div>
                  <dt>Approved</dt>
                  <dd>{{ money(current.approvedAmount, current.currencyCode) }}</dd>
                </div>
              }
              @if (current.returnShippingFee > 0) {
                <div>
                  <dt>Return freight</dt>
                  <dd>−{{ money(current.returnShippingFee, current.currencyCode) }}</dd>
                </div>
              }
              @if (current.refundAmount > 0) {
                <div class="total">
                  <dt>Refunded</dt>
                  <dd>{{ money(current.refundAmount, current.currencyCode) }}</dd>
                </div>
                <div>
                  <dt>How</dt>
                  <dd>{{ current.refundMode ?? '—' }}</dd>
                </div>
              }
            </dl>
          </section>

          <section class="panel">
            <h2>Where it is</h2>
            <dl class="facts">
              <div>
                <dt>Requested</dt>
                <dd>{{ when(current.requestedAt) }}</dd>
              </div>
              @if (current.approvedAt; as at) {
                <div>
                  <dt>Approved</dt>
                  <dd>{{ when(at) }}</dd>
                </div>
              }
              @if (current.pickupAwb; as awb) {
                <div>
                  <dt>Pickup waybill</dt>
                  <dd>{{ awb }}</dd>
                </div>
              }
              @if (current.pickupScheduledFor; as at) {
                <div>
                  <dt>Pickup on</dt>
                  <dd>{{ when(at) }}</dd>
                </div>
              }
              @if (current.receivedAt; as at) {
                <div>
                  <dt>Received</dt>
                  <dd>{{ when(at) }}</dd>
                </div>
              }
              @if (current.qcPassed !== null) {
                <div>
                  <dt>Inspection</dt>
                  <dd>
                    <kh-badge [tone]="current.qcPassed ? 'success' : 'danger'">
                      {{ current.qcPassed ? 'Passed' : 'Failed' }}
                    </kh-badge>
                  </dd>
                </div>
              }
              @if (current.refundedAt; as at) {
                <div>
                  <dt>Refunded</dt>
                  <dd>{{ when(at) }}</dd>
                </div>
              }
            </dl>
            @if (current.rejectedReason; as reason) {
              <p class="note">Rejected: {{ reason }}</p>
            }
            @if (current.qcNotes; as notes) {
              <p class="note">Inspection: {{ notes }}</p>
            }
          </section>
        </aside>
      </div>
    }

    <kh-modal
      [open]="approving()"
      heading="Approve this return"
      width="32rem"
      [dismissible]="!busy()"
      (closed)="approving.set(false)"
    >
      <p class="hint">
        Leave the amount blank to use what the reason code worked out. Only change it if you have agreed
        something different with the customer.
      </p>

      <kh-field label="Refund amount" for="approve-amount" [optional]="true">
        <input
          khControl
          khNumeric
          id="approve-amount"
          type="number"
          min="0"
          step="0.01"
          [value]="approveAmount()"
          (input)="approveAmount.set($any($event.target).value)"
        />
      </kh-field>

      <kh-field label="Note" for="approve-note" [optional]="true">
        <input
          khControl
          id="approve-note"
          type="text"
          maxlength="200"
          [value]="approveNote()"
          (input)="approveNote.set($any($event.target).value)"
        />
      </kh-field>

      <div slot="footer">
        <button khButton type="button" variant="tertiary" [disabled]="busy()" (click)="approving.set(false)">
          Cancel
        </button>
        <button khButton type="button" variant="primary" [disabled]="busy()" (click)="approve()">
          Approve
        </button>
      </div>
    </kh-modal>

    <kh-modal
      [open]="schedulingPickup()"
      heading="Arrange the pickup"
      width="30rem"
      [dismissible]="!busy()"
      (closed)="schedulingPickup.set(false)"
    >
      <p class="hint">
        A reverse shipment, which is an ordinary one with its addresses inverted. Leave the date blank to let
        the courier choose the next available slot.
      </p>

      <kh-field label="Collect on" for="pickup-date" [optional]="true">
        <input
          khControl
          id="pickup-date"
          type="date"
          [value]="pickupAt()"
          (input)="pickupAt.set($any($event.target).value)"
        />
      </kh-field>

      <div slot="footer">
        <button
          khButton
          type="button"
          variant="tertiary"
          [disabled]="busy()"
          (click)="schedulingPickup.set(false)"
        >
          Cancel
        </button>
        <button khButton type="button" variant="primary" [disabled]="busy()" (click)="schedulePickup()">
          Arrange it
        </button>
      </div>
    </kh-modal>

    <kh-modal
      [open]="inspecting()"
      heading="Inspect what came back"
      width="46rem"
      [dismissible]="!busy()"
      (closed)="inspecting.set(false)"
    >
      <p class="hint">
        Per line, because a two-item return is routinely one saleable unit and one not. Only
        <strong>Back on the shelf</strong> puts stock back on sale — the rest are recorded and do not.
      </p>

      <table>
        <thead>
          <tr>
            <th scope="col">Item</th>
            <th scope="col" class="numeric">Came back</th>
            <th scope="col">Accepted</th>
            <th scope="col">Where it goes</th>
          </tr>
        </thead>
        <tbody>
          @for (line of qcDraft(); track line.returnLineId; let index = $index) {
            <tr>
              <td>
                {{ line.name }}<span class="note">{{ line.sku }}</span>
              </td>
              <td class="numeric">{{ line.quantity }}</td>
              <td>
                <input
                  khControl
                  khNumeric
                  [id]="'qc-accepted-' + index"
                  [attr.aria-label]="'Accepted quantity for ' + line.name"
                  type="number"
                  min="0"
                  [attr.max]="line.quantity"
                  [value]="line.accepted"
                  (input)="setQcAccepted(index, $any($event.target).value)"
                />
              </td>
              <td>
                <select
                  khControl
                  [id]="'qc-disposition-' + index"
                  [attr.aria-label]="'Disposition for ' + line.name"
                  [value]="line.disposition"
                  (change)="setQcDisposition(index, $any($event.target).value)"
                >
                  @for (option of dispositions; track option.value) {
                    <option [value]="option.value">{{ option.label }}</option>
                  }
                </select>
              </td>
            </tr>
          }
        </tbody>
      </table>

      <kh-field label="Inspection notes" for="qc-notes" [optional]="true">
        <textarea
          khControl
          id="qc-notes"
          rows="2"
          [value]="qcNotes()"
          (input)="qcNotes.set($any($event.target).value)"
        ></textarea>
      </kh-field>

      <div slot="footer">
        <button khButton type="button" variant="tertiary" [disabled]="busy()" (click)="inspecting.set(false)">
          Cancel
        </button>
        <button khButton type="button" variant="primary" [disabled]="busy()" (click)="inspect()">
          Record the inspection
        </button>
      </div>
    </kh-modal>

    <kh-modal
      [open]="refunding()"
      heading="Refund this return"
      width="32rem"
      [dismissible]="!busy()"
      (closed)="refunding.set(false)"
    >
      <p class="hint">
        Leave both blank to refund what the inspection accepted, to the instrument the customer paid with.
      </p>

      <kh-field label="How" for="refund-mode">
        <select
          khControl
          id="refund-mode"
          [value]="refundMode()"
          (change)="refundMode.set($any($event.target).value)"
        >
          <option value="">The original payment method</option>
          <option value="StoreCredit">Store credit</option>
        </select>
      </kh-field>

      <kh-field label="Amount" for="refund-amount" [optional]="true">
        <input
          khControl
          khNumeric
          id="refund-amount"
          type="number"
          min="0"
          step="0.01"
          [value]="refundAmount()"
          (input)="refundAmount.set($any($event.target).value)"
        />
      </kh-field>

      <div slot="footer">
        <button khButton type="button" variant="tertiary" [disabled]="busy()" (click)="refunding.set(false)">
          Cancel
        </button>
        <button
          khButton
          type="button"
          variant="primary"
          [disabled]="busy()"
          (click)="confirmRefund.set(true)"
        >
          Refund
        </button>
      </div>
    </kh-modal>

    <kh-confirm-dialog
      [open]="confirmRefund()"
      heading="Send the money back"
      message="The refund is raised with the payment provider and a credit note is issued. It cannot be recalled."
      confirmLabel="Refund"
      [busy]="busy()"
      (confirmed)="refund()"
      (cancelled)="confirmRefund.set(false)"
    />

    <kh-confirm-dialog
      [open]="rejecting()"
      heading="Reject this return"
      message="The customer is shown exactly what you write here."
      confirmLabel="Reject"
      [requireReason]="true"
      [busy]="busy()"
      (confirmed)="reject($event.reason)"
      (cancelled)="rejecting.set(false)"
    />

    <kh-modal
      [open]="replacing()"
      heading="Send a replacement"
      width="30rem"
      [dismissible]="!busy()"
      (closed)="replacing.set(false)"
    >
      <p class="hint">
        Naming the order the replacement went out on is what makes it traceable afterwards. Leave it blank if
        the goods were sent outside this platform, and say so in the timeline.
      </p>

      <kh-entity-picker
        label="Replacement order"
        inputId="replacement-order"
        [optional]="true"
        hint="This shopper's orders, by number."
        [search]="orderSearch"
        (chose)="replacementOrder.set($event?.id ?? null)"
      />

      <div slot="footer">
        <button khButton type="button" variant="tertiary" [disabled]="busy()" (click)="replacing.set(false)">
          Cancel
        </button>
        <button khButton type="button" variant="primary" [disabled]="busy()" (click)="replace()">
          Record the replacement
        </button>
      </div>
    </kh-modal>
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

    .hint {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .note {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    td .note {
      display: block;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
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
    }

    .facts dt,
    .facts dd {
      margin: 0;
    }

    .facts dt {
      color: var(--color-text-muted);
    }

    .facts .total {
      border-block-start: 1px solid var(--color-border);
      font-weight: var(--weight-medium);
    }

    .policy {
      margin: var(--space-2) 0 0;
      padding-inline-start: var(--space-4);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReturnDetailPage {
  private readonly returns = inject(ReturnsAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly toasts = inject(ToastService);

  // ---- Attaching the replacement order (Step 28B, deliverable 18) --------------------------------

  private readonly orders = inject(OrdersAdminService);

  protected readonly replacing = signal(false);
  protected readonly replacementOrder = signal<string | null>(null);

  /**
   * Finds this shopper's orders for the picker.
   *
   * Scoped to the customer on the return, so an operator cannot attach somebody else's order by
   * mistyping a number.
   */
  protected readonly orderSearch = (term: string): Observable<readonly EntityOption[]> => {
    const customerId = this.rma()?.customerId;
    if (!customerId) return of([]);

    return this.orders.searchCustomerOrders(customerId, term).pipe(
      map((orders) =>
        orders.map((order) => ({
          id: order.id,
          label: order.orderNumber,
          hint: `${order.status} · ${tableDateTime(order.placedAt)}`,
        })),
      ),
    );
  };

  protected readonly dispositions = DISPOSITIONS;

  private readonly id = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly rma = signal<ReturnResponse | null>(null);
  protected readonly creditNote = signal<CreditNoteResponse | null>(null);
  private readonly reasons = signal<readonly ReturnReasonResponse[]>([]);

  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);

  protected readonly approving = signal(false);
  protected readonly rejecting = signal(false);
  protected readonly schedulingPickup = signal(false);
  protected readonly inspecting = signal(false);
  protected readonly refunding = signal(false);
  protected readonly confirmRefund = signal(false);

  protected readonly approveAmount = signal('');
  protected readonly approveNote = signal('');
  protected readonly pickupAt = signal('');
  protected readonly qcDraft = signal<readonly QcDraft[]>([]);
  protected readonly qcNotes = signal('');
  protected readonly refundMode = signal('');
  protected readonly refundAmount = signal('');

  protected readonly subtitle = computed(() => {
    const current = this.rma();
    if (!current) return null;
    return `${current.orderNumber} · ${current.type} · requested ${tableDateTime(current.requestedAt)}`;
  });

  /** The policy behind the reason code, which is what explains every default on this screen. */
  protected readonly reason = computed(() =>
    this.reasons().find((entry) => entry.code === this.rma()?.reasonCode),
  );

  constructor() {
    this.load();
    this.returns.reasons(true).subscribe({
      next: (reasons) => this.reasons.set(reasons),
      // Not fatal: without them the reason code is shown bare, which is still the reason.
      error: () => this.reasons.set([]),
    });
  }

  protected money(amount: number, currency: string): string {
    return tableMoney(amount, currency);
  }

  protected when(value: string | null): string {
    return tableDateTime(value) || '—';
  }

  /** Whether the API's own transition table allows this edge from where the return is now. */
  protected can(status: string): boolean {
    return this.rma()?.nextStatuses.includes(status) ?? false;
  }

  protected approve(): void {
    this.approving.set(false);
    const amount = this.approveAmount();
    this.act(
      this.returns.approve(this.id, {
        amount: amount ? Number(amount) : null,
        // Null leaves the reason code's own answer alone, which is right unless somebody has
        // spoken to the customer and knows better.
        pickupRequired: null,
        note: this.approveNote() || null,
      }),
      'Approved.',
    );
  }

  protected reject(reason: string): void {
    this.rejecting.set(false);
    this.act(this.returns.reject(this.id, reason || null), 'Rejected, and the customer told.');
  }

  protected schedulePickup(): void {
    this.schedulingPickup.set(false);
    this.act(this.returns.schedulePickup(this.id, this.pickupAt() || null), 'Pickup arranged.');
  }

  protected receive(): void {
    this.act(this.returns.receive(this.id, null), 'Marked as received.');
  }

  protected startQc(current: ReturnResponse): void {
    this.qcNotes.set('');
    this.qcDraft.set(
      current.lines.map((line) => ({
        returnLineId: line.id,
        sku: line.sku,
        name: line.name,
        quantity: line.quantity,
        // Everything accepted and restocked is the common case; the exceptions are what somebody
        // holding the goods is here to record.
        accepted: String(line.quantity),
        disposition: 'Restock',
      })),
    );
    this.inspecting.set(true);
  }

  protected setQcAccepted(index: number, value: string): void {
    this.qcDraft.update((current) =>
      current.map((line, at) => (at === index ? { ...line, accepted: value } : line)),
    );
  }

  protected setQcDisposition(index: number, value: string): void {
    this.qcDraft.update((current) =>
      current.map((line, at) => (at === index ? { ...line, disposition: value as ReturnDisposition } : line)),
    );
  }

  protected inspect(): void {
    const draft = this.qcDraft();
    const lines: QcLineRequest[] = draft.map((line) => ({
      returnLineId: line.returnLineId,
      quantityAccepted: Number(line.accepted || 0),
      disposition: line.disposition,
      // Per-line notes are not offered: one note for the whole inspection is what an inspector
      // actually writes, and it goes on `QcBody.notes` below.
      note: null,
    }));

    // The overall result follows from the lines rather than being a separate choice: a return where
    // nothing was accepted failed, and one where anything was passed.
    const anyAccepted = lines.some((line) => line.quantityAccepted > 0);

    this.inspecting.set(false);
    this.act(
      this.returns.inspect(this.id, {
        result: anyAccepted ? 'Pass' : 'Fail',
        disposition: null,
        notes: this.qcNotes() || null,
        lines,
      }),
      'Inspection recorded.',
    );
  }

  protected refund(): void {
    this.confirmRefund.set(false);
    this.refunding.set(false);
    const amount = this.refundAmount();
    this.act(
      this.returns.refund(this.id, {
        mode: this.refundMode() || null,
        amount: amount ? Number(amount) : null,
      }),
      'Refund raised.',
    );
  }

  protected startReplace(): void {
    this.replacementOrder.set(null);
    this.replacing.set(true);
  }

  /**
   * Records the replacement, naming the order it went out on where there is one.
   *
   * Until Step 28B this always sent null, because nothing on this screen could find an order —
   * so a replacement was closed with no trace of what was actually sent (deliverable 18). The
   * order is still *placed* elsewhere: creating one from a return is an order type the domain
   * model does not yet describe, and inventing its tax and settlement treatment here would be
   * inventing a policy rather than building a screen.
   */
  protected replace(): void {
    this.replacing.set(false);
    this.act(
      this.returns.replace(this.id, this.replacementOrder(), 'Replacement agreed with the customer.'),
      'Replacement recorded.',
    );
  }

  protected close(): void {
    this.act(this.returns.close(this.id, null), 'Closed.');
  }

  private act(request: ReturnType<ReturnsAdminService['receive']>, message: string): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);

    request.subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.rma.set(updated);
        this.toasts.success(message);
        if (updated.creditNoteId) this.loadCreditNote();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That could not be done.'));
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.returns.return_(this.id).subscribe({
      next: (loaded) => {
        this.loading.set(false);
        this.rma.set(loaded);
        if (loaded.creditNoteId) this.loadCreditNote();
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'That return could not be found.'));
      },
    });
  }

  private loadCreditNote(): void {
    this.returns.creditNoteForReturn(this.id).subscribe({
      next: (note) => this.creditNote.set(note),
      // A missing credit note is not an error worth a banner: the panel simply does not appear.
      error: () => this.creditNote.set(null),
    });
  }
}
