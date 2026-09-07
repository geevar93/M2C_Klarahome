import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';

import { Button } from './button';
import { Icon } from './icon';

/**
 * Minus, a number, plus.
 *
 * The number is a real `<input type="number">` and not a label between two buttons: going from 1
 * to 12 with a plus button is eleven taps, and a keyboard or screen-reader user needs to be able
 * to state the quantity rather than step to it. The buttons are then a convenience on top of a
 * control that already works without them.
 *
 * The value is a `model()`, so the parent binds `[(quantity)]` and the component never holds a
 * quantity the parent disagrees with. Clamping happens here — a stepper that lets a shopper ask
 * for 40 of something with 3 in stock produces a server error the shopper cannot act on.
 */
@Component({
  selector: 'kh-quantity-stepper',
  imports: [Button, Icon],
  template: `
    <button
      khButton
      variant="secondary"
      [iconOnly]="true"
      type="button"
      [disabled]="disabled() || quantity() <= min()"
      [attr.aria-label]="'Decrease ' + label()"
      (click)="step(-1)"
    >
      <kh-icon name="minus" size="sm" />
    </button>

    <label class="kh-visually-hidden" [attr.for]="inputId()">{{ label() }}</label>
    <input
      [id]="inputId()"
      type="number"
      inputmode="numeric"
      [attr.min]="min()"
      [attr.max]="max()"
      [disabled]="disabled()"
      [value]="quantity()"
      (change)="onTyped($any($event.target).value)"
    />

    <button
      khButton
      variant="secondary"
      [iconOnly]="true"
      type="button"
      [disabled]="disabled() || quantity() >= max()"
      [attr.aria-label]="'Increase ' + label()"
      (click)="step(1)"
    >
      <kh-icon name="plus" size="sm" />
    </button>
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: var(--space-2);
    }

    input {
      inline-size: 3.5rem;
      min-block-size: var(--touch-target-min);
      padding-inline: var(--space-2);
      text-align: center;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      font-variant-numeric: tabular-nums;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuantityStepper {
  readonly quantity = model(1);
  readonly min = input(1);
  /** The purchasable ceiling — stock on hand, or the seller's per-order limit, whichever is lower. */
  readonly max = input(10);
  readonly disabled = input(false);
  readonly label = input('Quantity');
  readonly inputId = input('kh-quantity');

  protected readonly bounds = computed(() => ({ min: this.min(), max: Math.max(this.min(), this.max()) }));

  protected step(delta: number): void {
    this.quantity.set(this.clamp(this.quantity() + delta));
  }

  protected onTyped(raw: string): void {
    const parsed = Number.parseInt(raw, 10);
    // A cleared or unparseable box falls back to the floor rather than to NaN, which would
    // otherwise reach the API as `quantity=null` and be refused.
    this.quantity.set(this.clamp(Number.isFinite(parsed) ? parsed : this.min()));
  }

  private clamp(value: number): number {
    const { min, max } = this.bounds();
    return Math.min(max, Math.max(min, value));
  }
}
