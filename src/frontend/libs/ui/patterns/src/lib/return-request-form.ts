import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { MoneyPipe } from '@klarahome/i18n';
import { Alert, Button, Control, Field, ProductImage, QuantityStepper } from '@klarahome/ui-primitives';

import { ReturnReasonView, ReturnableLineView } from './commerce.model';

/** What the customer is asking for. The exact shape `POST /store/returns` wants. */
export interface ReturnRequestValue {
  readonly type: 'Refund' | 'Replacement';
  readonly reasonCode: string;
  readonly reasonNote: string | null;
  readonly lines: readonly { readonly orderLineId: string; readonly quantity: number }[];
  readonly refundMode: 'Original' | 'StoreCredit';
}

/**
 * Asking to send something back.
 *
 * The form is built out of what the API already decided, and that is the point of it. Which lines
 * may be returned, how many of each, whether the window is still open and what the refund would
 * come to are all answered by `GET /store/orders/{id}/return-eligibility` (Step 17) — so this form
 * offers exactly those and no more. A form that let a customer request a return the policy refuses
 * produces a rejection email, which is a worse experience than not offering it.
 *
 * The **reason drives the rest of the form**, because in this domain it genuinely does: a reason
 * carries its own policy (Step 17), so choosing "damaged in transit" is what makes photographs
 * required, and choosing a reason that does not allow one is what removes the replacement option.
 * Those rules arrive as data on the reason rather than being written out here, which is what keeps
 * the form and the server's decision from drifting.
 *
 * **Refund destination is asked, not assumed.** Store credit is instant and the original instrument
 * takes days; both are legitimate preferences and guessing wrong is the complaint that follows.
 */
@Component({
  selector: 'kh-return-request-form',
  imports: [Alert, Button, Control, Field, MoneyPipe, ProductImage, QuantityStepper],
  template: `
    <form (submit)="submit($event)" novalidate>
      <fieldset class="lines">
        <legend>What are you sending back?</legend>

        @for (line of lines(); track line.orderLineId) {
          <div class="line" [class.disabled]="!line.isReturnable">
            <label class="kh-choice">
              <input
                type="checkbox"
                [checked]="isChosen(line.orderLineId)"
                [disabled]="!line.isReturnable"
                (change)="toggle(line, $any($event.target).checked)"
              />
              <span class="body">
                <span class="name">{{ line.name }}</span>
                <span class="meta">{{ line.sku }} · refund up to {{ line.estimatedRefund | khMoney }}</span>
                @if (!line.isReturnable && line.reason) {
                  <span class="meta">{{ line.reason }}</span>
                }
              </span>
            </label>

            <kh-product-image [source]="line.image" [placeholder]="line.sku" sizes="4rem" />

            @if (isChosen(line.orderLineId) && line.quantityReturnable > 1) {
              <kh-quantity-stepper
                class="qty"
                [quantity]="quantityFor(line.orderLineId)"
                [max]="line.quantityReturnable"
                [inputId]="'ret-qty-' + line.orderLineId"
                [label]="'Quantity of ' + line.name + ' to return'"
                (quantityChange)="setQuantity(line.orderLineId, $event)"
              />
            }
          </div>
        }
      </fieldset>

      <kh-field label="Why are you sending it back?" for="ret-reason" [error]="reasonError()">
        <select
          khControl
          id="ret-reason"
          [khInvalid]="!!reasonError()"
          (change)="reasonCode.set($any($event.target).value)"
        >
          <option value="" [selected]="reasonCode() === ''">Select a reason</option>
          @for (reason of reasons(); track reason.code) {
            <option [value]="reason.code" [selected]="reason.code === reasonCode()">
              {{ reason.label }}
            </option>
          }
        </select>
      </kh-field>

      @if (chosenReason(); as reason) {
        @if (reason.description) {
          <p class="reason-note">{{ reason.description }}</p>
        }
        @if (reason.requiresEvidence) {
          <kh-alert tone="info">
            This reason needs photographs. Our team will ask for them by email once the request is raised —
            uploading them here is coming with the media picker.
          </kh-alert>
        }
      }

      <kh-field
        label="Anything else we should know?"
        for="ret-note"
        [optional]="true"
        hint="A sentence or two helps the seller decide faster."
      >
        <textarea
          khControl
          id="ret-note"
          rows="3"
          maxlength="500"
          [value]="note()"
          (input)="note.set($any($event.target).value)"
        ></textarea>
      </kh-field>

      @if (allowsReplacement()) {
        <fieldset class="choices">
          <legend>What would you like instead?</legend>
          <label class="kh-choice">
            <input
              type="radio"
              name="ret-type"
              value="Refund"
              [checked]="type() === 'Refund'"
              (change)="type.set('Refund')"
            />
            <span>A refund</span>
          </label>
          <label class="kh-choice">
            <input
              type="radio"
              name="ret-type"
              value="Replacement"
              [checked]="type() === 'Replacement'"
              (change)="type.set('Replacement')"
            />
            <span>A replacement</span>
          </label>
        </fieldset>
      }

      @if (type() === 'Refund') {
        <fieldset class="choices">
          <legend>Where should the refund go?</legend>
          <label class="kh-choice">
            <input
              type="radio"
              name="ret-refund"
              value="Original"
              [checked]="refundMode() === 'Original'"
              (change)="refundMode.set('Original')"
            />
            <span class="body">
              <span class="name">Back to how you paid</span>
              <span class="meta">Reaches your bank in 5–7 working days.</span>
            </span>
          </label>
          <label class="kh-choice">
            <input
              type="radio"
              name="ret-refund"
              value="StoreCredit"
              [checked]="refundMode() === 'StoreCredit'"
              (change)="refundMode.set('StoreCredit')"
            />
            <span class="body">
              <span class="name">As store credit</span>
              <span class="meta">Available to spend as soon as the return is approved.</span>
            </span>
          </label>
        </fieldset>
      }

      @if (linesError()) {
        <kh-alert tone="danger">{{ linesError() }}</kh-alert>
      }

      <div class="actions">
        <button khButton variant="primary" type="submit" [disabled]="saving()">
          {{ saving() ? 'Sending…' : 'Request return' }}
        </button>
        <button khButton variant="tertiary" type="button" (click)="cancelled.emit()">Cancel</button>
      </div>
    </form>
  `,
  styles: `
    :host {
      display: block;
    }

    fieldset {
      margin: 0 0 var(--space-5);
      padding: 0;
      border: 0;
    }

    legend {
      padding: 0;
      margin-block-end: var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .lines {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
    }

    .line {
      display: grid;
      grid-template-columns: 1fr 4rem;
      align-items: start;
      gap: var(--space-2);
    }

    .line.disabled {
      opacity: 0.6;
    }

    .qty {
      grid-column: 1 / -1;
      justify-self: start;
    }

    .choices {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
    }

    .body {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      min-width: 0;
    }

    .name {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .meta {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .reason-note {
      margin: calc(var(--space-4) * -1) 0 var(--space-4);
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReturnRequestForm {
  readonly lines = input.required<readonly ReturnableLineView[]>();
  readonly reasons = input.required<readonly ReturnReasonView[]>();
  readonly saving = input(false);

  readonly submitted = output<ReturnRequestValue>();
  readonly cancelled = output<void>();

  protected readonly reasonCode = signal('');
  protected readonly note = signal('');
  protected readonly type = signal<'Refund' | 'Replacement'>('Refund');
  protected readonly refundMode = signal<'Original' | 'StoreCredit'>('Original');

  /** Chosen line ids to the quantity asked for. A `Map` in a signal, replaced rather than mutated. */
  private readonly chosen = signal<ReadonlyMap<string, number>>(new Map());
  private readonly attempted = signal(false);

  protected readonly chosenReason = computed(
    () => this.reasons().find((reason) => reason.code === this.reasonCode()) ?? null,
  );

  protected readonly allowsReplacement = computed(() => this.chosenReason()?.allowsReplacement ?? false);

  protected readonly reasonError = computed(() =>
    this.attempted() && !this.reasonCode() ? 'Choose a reason.' : null,
  );

  protected readonly linesError = computed(() =>
    this.attempted() && this.chosen().size === 0 ? 'Choose at least one item to send back.' : null,
  );

  protected isChosen(orderLineId: string): boolean {
    return this.chosen().has(orderLineId);
  }

  protected quantityFor(orderLineId: string): number {
    return this.chosen().get(orderLineId) ?? 1;
  }

  protected toggle(line: ReturnableLineView, checked: boolean): void {
    const next = new Map(this.chosen());
    if (checked) next.set(line.orderLineId, Math.min(1, line.quantityReturnable) || 1);
    else next.delete(line.orderLineId);
    this.chosen.set(next);
  }

  protected setQuantity(orderLineId: string, quantity: number): void {
    if (!this.chosen().has(orderLineId)) return;
    const next = new Map(this.chosen());
    next.set(orderLineId, quantity);
    this.chosen.set(next);
  }

  protected submit(event: Event): void {
    event.preventDefault();
    this.attempted.set(true);
    if (!this.reasonCode() || this.chosen().size === 0) return;

    // A replacement is not a refund, so the destination it would go to is not sent — the API would
    // ignore it, and sending a field that means nothing is how a contract acquires dead parameters.
    const isReplacement = this.type() === 'Replacement' && this.allowsReplacement();

    this.submitted.emit({
      type: isReplacement ? 'Replacement' : 'Refund',
      reasonCode: this.reasonCode(),
      reasonNote: this.note().trim() || null,
      lines: [...this.chosen()].map(([orderLineId, quantity]) => ({ orderLineId, quantity })),
      refundMode: isReplacement ? 'Original' : this.refundMode(),
    });
  }
}
