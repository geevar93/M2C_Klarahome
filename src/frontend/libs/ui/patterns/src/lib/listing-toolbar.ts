import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Button, Chip, Icon } from '@klarahome/ui-primitives';
import { KhNumberPipe } from '@klarahome/i18n';

import { AppliedFilterView, SortOptionView } from './catalog.model';

/**
 * What sits above a listing: the result count, the sort control, the filter button, and the
 * filters that are already on.
 *
 * The **count is a live region**. A shopper who taps a filter on a phone sees the grid change
 * below the fold; a screen-reader user is told nothing at all unless the number announces itself.
 * `aria-live="polite"` on the count is the cheapest correct answer, and it is why the count is
 * rendered here rather than inside the grid.
 *
 * Sort is a native `<select>`. A custom listbox would need a popup, a roving tabindex and its own
 * keyboard model to equal what the platform gives free — and on a phone the native control is a
 * full-height wheel, which is better than anything a storefront builds.
 *
 * The filter button is hidden from `lg`, where the panel is a permanent sidebar and a button that
 * opens a sheet nobody sees would be a keyboard trap in the tab order.
 */
@Component({
  selector: 'kh-listing-toolbar',
  imports: [Button, Chip, Icon, KhNumberPipe],
  template: `
    <div class="row">
      <p class="count" aria-live="polite">
        @if (loading()) {
          Searching…
        } @else {
          {{ total() | khNumber }} {{ total() === 1 ? 'product' : 'products' }}
        }
      </p>

      <div class="controls">
        <button
          khButton
          variant="secondary"
          size="sm"
          type="button"
          class="filter"
          (click)="filtersOpened.emit()"
        >
          <kh-icon name="filter" size="sm" />
          Filters
          @if (applied().length > 0) {
            <span class="badge">{{ applied().length }}</span>
          }
        </button>

        <label class="sort">
          <span class="kh-visually-hidden">Sort by</span>
          <kh-icon name="sort" size="sm" />
          <select [value]="sort()" (change)="sorted.emit($any($event.target).value)">
            @for (option of sortOptions(); track option.value) {
              <option [value]="option.value">{{ option.label }}</option>
            }
          </select>
        </label>
      </div>
    </div>

    @if (applied().length > 0) {
      <!-- A list, because it is one, and because "3 filters applied" is then announced without a
           count having to be written anywhere. -->
      <ul class="applied" aria-label="Applied filters">
        @for (filter of applied(); track filter.key + filter.value) {
          <li>
            <kh-chip [label]="filter.label" [removable]="true" (toggled)="removed.emit(filter)" />
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host {
      display: block;
      padding-block: var(--space-3);
    }

    .row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .count {
      margin: 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .controls {
      display: flex;
      align-items: center;
      gap: var(--space-2);
    }

    .badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-inline-size: var(--space-5);
      padding-inline: var(--space-1);
      border-radius: var(--radius-full);
      background: var(--color-primary);
      color: var(--color-on-primary);
      font-size: var(--text-xs);
    }

    .sort {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      min-block-size: var(--touch-target-min);
      padding-inline: var(--space-2);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      color: var(--color-text-muted);
    }

    select {
      border: 0;
      background: transparent;
      color: var(--color-text);
      font-size: var(--text-sm);
      max-inline-size: 10rem;
    }

    .applied {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin: var(--space-3) 0 0;
      padding: 0;
      list-style: none;
    }

    @media (min-width: 1024px) {
      .filter {
        display: none;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ListingToolbar {
  readonly total = input(0);
  readonly loading = input(false);
  readonly sort = input('relevance');
  readonly sortOptions = input.required<readonly SortOptionView[]>();
  readonly applied = input<readonly AppliedFilterView[]>([]);

  readonly filtersOpened = output<void>();
  readonly sorted = output<string>();
  readonly removed = output<AppliedFilterView>();
}
