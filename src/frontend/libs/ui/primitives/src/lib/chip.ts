import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { Icon } from './icon';

/**
 * A selectable or removable token — a facet value, an applied filter, a variant option.
 *
 * It renders a real `<button>` with `aria-pressed` when it toggles, because that is what a filter
 * chip is: a control with two states that a screen reader has to be able to report. A removable
 * chip is a different control — its label says "Remove <x>" — so the two are one component with
 * one input rather than two components that drift apart.
 *
 * Selection is never carried by colour alone: the selected state also changes the border weight
 * and adds a tick (docs/10-design-system-placeholder.md §4).
 */
@Component({
  selector: 'kh-chip',
  imports: [Icon],
  template: `
    <button
      type="button"
      [disabled]="disabled()"
      [attr.aria-pressed]="removable() ? null : selected()"
      [attr.aria-label]="removable() ? 'Remove ' + label() : null"
      (click)="toggled.emit()"
    >
      @if (selected() && !removable()) {
        <kh-icon name="check" size="sm" />
      }
      <span class="label">{{ label() }}</span>
      @if (count() !== null) {
        <span class="count">{{ count() }}</span>
      }
      @if (removable()) {
        <kh-icon name="close" size="sm" />
      }
    </button>
  `,
  styles: `
    :host {
      display: inline-flex;
      max-inline-size: 100%;
    }

    button {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      max-inline-size: 100%;
      min-block-size: var(--touch-target-min);
      padding-inline: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-full);
      background: var(--color-surface-raised);
      color: var(--color-text);
      font-size: var(--text-sm);
    }

    button[aria-pressed='true'] {
      border-color: var(--color-primary);
      border-width: 2px;
      background: var(--color-primary-subtle);
      font-weight: var(--weight-medium);
    }

    button:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .label {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .count {
      color: var(--color-text-muted);
      font-variant-numeric: tabular-nums;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Chip {
  readonly label = input.required<string>();
  readonly selected = input(false);
  readonly disabled = input(false);
  /** The number of results behind a facet value. Null on a chip that is not a facet. */
  readonly count = input<number | null>(null);
  /** Renders as "remove this", not "toggle this" — the applied-filters row. */
  readonly removable = input(false);

  readonly toggled = output<void>();
}
