import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { Icon } from './icon';

export interface StepperStep {
  /** Stable identifier — what `stepSelected` emits and what a URL carries. */
  readonly id: string;
  readonly label: string;
}

/**
 * Where the customer is in a multi-step flow, and how far they have left.
 *
 * Checkout on a phone is four screens, and an abandoned checkout is usually one where the shopper
 * could not tell whether they were nearly finished. So it is a list with a count, not a decorative
 * rail: `<ol>` with the current step carrying `aria-current="step"`, and a "Step 2 of 4" line that
 * says the same thing in words for anyone who cannot see the marks.
 *
 * **A completed step is a button; a future one is not.** Going back to change an address is
 * ordinary; skipping forward past the address to the payment is not a thing the flow can honour,
 * so it is not offered — a disabled control that looks tappable is worse than no control.
 */
@Component({
  selector: 'kh-stepper',
  imports: [Icon],
  template: `
    <p class="counter">Step {{ activeIndex() + 1 }} of {{ steps().length }}</p>

    <ol>
      @for (step of steps(); track step.id; let index = $index) {
        <li
          [class.done]="index < activeIndex()"
          [class.current]="index === activeIndex()"
          [attr.aria-current]="index === activeIndex() ? 'step' : null"
        >
          @if (index < activeIndex()) {
            <button type="button" (click)="stepSelected.emit(step.id)">
              <span class="marker"><kh-icon name="check" size="sm" /></span>
              <span class="label">{{ step.label }}</span>
            </button>
          } @else {
            <span class="static">
              <span class="marker">{{ index + 1 }}</span>
              <span class="label">{{ step.label }}</span>
            </span>
          }
        </li>
      }
    </ol>
  `,
  styles: `
    :host {
      display: block;
      margin-block-end: var(--space-4);
    }

    .counter {
      margin: 0 0 var(--space-2);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    ol {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      list-style: none;
      margin: 0;
      padding: 0;
      overflow-x: auto;
    }

    li {
      flex: 1;
      min-inline-size: 0;
    }

    button,
    .static {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      inline-size: 100%;
      padding: var(--space-2) 0;
      border: 0;
      border-block-start: var(--space-1) solid var(--color-border);
      background: none;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
      text-align: start;
    }

    li.done button {
      border-block-start-color: var(--color-primary);
      color: var(--color-text);
      cursor: pointer;
    }

    li.current .static {
      border-block-start-color: var(--color-primary);
      color: var(--color-text);
      font-weight: var(--weight-medium);
    }

    .marker {
      display: grid;
      place-items: center;
      flex: none;
      inline-size: var(--space-6);
      block-size: var(--space-6);
      border-radius: var(--radius-full);
      background: var(--color-surface);
      font-variant-numeric: tabular-nums;
    }

    li.done .marker {
      background: var(--color-primary-subtle);
      color: var(--color-primary);
    }

    li.current .marker {
      background: var(--color-primary);
      color: var(--color-on-primary);
    }

    /* Below 'sm' the labels are dropped and the markers carry the flow: four words across a 360px
       screen wrap into four lines and push the form below the fold. The "Step 2 of 4" line above
       still says where the shopper is, and it is what a screen reader was reading anyway. */
    @media (max-width: 479px) {
      .label {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip: rect(0, 0, 0, 0);
        white-space: nowrap;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Stepper {
  readonly steps = input.required<readonly StepperStep[]>();
  readonly active = input.required<string>();
  /** A completed step was tapped. Going back only — the future steps are not controls. */
  readonly stepSelected = output<string>();

  protected readonly activeIndex = computed(() => {
    const index = this.steps().findIndex((step) => step.id === this.active());
    return index === -1 ? 0 : index;
  });
}
