import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Button, Chip, Disclosure } from '@klarahome/ui-primitives';

import { AppliedFilterView, FacetGroupView } from './catalog.model';

/** A facet value being turned on or off. The panel never mutates state; the page owns the URL. */
export interface FacetToggle {
  readonly key: string;
  readonly value: string;
  readonly selected: boolean;
}

/**
 * The facets, in one component rendered in two places.
 *
 * On a phone this is the content of a bottom sheet; from `lg` it is a sidebar
 * (docs/05-frontend-architecture.md §3.3). Those are two hosts, not two components — a sheet and a
 * sidebar that were separate implementations would drift on the day someone adds a facet type, and
 * the drift would only show on one screen size.
 *
 * A group opens automatically when it already has a selection, which is what a shopper arriving on
 * a shared filter URL needs to see: the filters that are on, without hunting through twelve
 * collapsed groups for the one that is narrowing their results.
 *
 * Each value is a `kh-chip` with `aria-pressed`, so the whole panel is a set of toggles a screen
 * reader can read the state of — rather than a set of checkboxes whose labels carry counts that
 * change under them.
 */
@Component({
  selector: 'kh-filter-panel',
  imports: [Button, Chip, Disclosure],
  template: `
    <div class="head">
      <h2 class="title">Filters</h2>
      @if (applied().length > 0) {
        <button khButton variant="tertiary" size="sm" type="button" (click)="cleared.emit()">
          Clear all
        </button>
      }
    </div>

    @for (group of groups(); track group.key) {
      <kh-disclosure
        [heading]="group.label"
        [hint]="group.selectedCount > 0 ? group.selectedCount + ' selected' : null"
        [open]="group.selectedCount > 0"
      >
        <div class="values">
          @for (value of group.values; track value.value) {
            <kh-chip
              [label]="value.label"
              [count]="value.count"
              [selected]="value.selected"
              (toggled)="toggled.emit({ key: group.key, value: value.value, selected: !value.selected })"
            />
          }
        </div>
      </kh-disclosure>
    } @empty {
      <p class="none">No filters are available for these results.</p>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
    }

    .title {
      margin: 0;
      font-size: var(--text-lg);
    }

    .values {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }

    .none {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FilterPanel {
  readonly groups = input.required<readonly FacetGroupView[]>();
  /** Only used to decide whether "Clear all" is worth showing. */
  readonly applied = input<readonly AppliedFilterView[]>([]);

  readonly toggled = output<FacetToggle>();
  readonly cleared = output<void>();
}
