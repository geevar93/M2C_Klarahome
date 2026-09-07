import { ChangeDetectionStrategy, Component, computed, effect, input, output, signal } from '@angular/core';
import { Button, Chip, Control, Icon } from '@klarahome/ui-primitives';

/** One value a select-shaped filter offers. */
export interface FilterOption {
  readonly value: string;
  readonly label: string;
}

/** One filter control. Only the two shapes the admin's list endpoints actually accept. */
export interface FilterDefinition {
  /** The query parameter this writes — `status`, `channel`, `from`. */
  readonly key: string;
  readonly label: string;
  readonly kind: 'select' | 'date';
  /** For `select`. The blank option is added automatically and means "no filter". */
  readonly options?: readonly FilterOption[];
}

/** The filters that are on, keyed as the API names them. An absent key means "not filtered". */
export type FilterValues = Readonly<Record<string, string>>;

/**
 * The search box and filters above a list.
 *
 * Two behaviours make it worth a component rather than a row of inputs on each screen:
 *
 *  - **The search box is debounced; the filters are not.** Typing is a stream of intentions and
 *    firing a request per keystroke is a request per keystroke; choosing "Cancelled" from a select
 *    is a single decision and waiting 300 ms to act on it only feels broken.
 *  - **What is on shows as removable chips.** The filters themselves may be scrolled away or
 *    folded on a tablet, and a list quietly filtered by something the user cannot see is the
 *    single most common "the data is missing" support ticket a back office generates.
 *
 * The values are inputs, not state: the page owns them, usually in the URL, so a filtered list is
 * a link somebody can send to a colleague.
 */
@Component({
  selector: 'kh-filter-bar',
  imports: [Button, Chip, Control, Icon],
  template: `
    <div class="row">
      @if (searchable()) {
        <div class="search">
          <kh-icon name="search" size="sm" />
          <input
            khControl
            khDescribed="false"
            type="search"
            [attr.aria-label]="searchLabel()"
            [attr.placeholder]="searchLabel()"
            [value]="draft()"
            (input)="onType($any($event.target).value)"
          />
        </div>
      }

      @for (filter of filters(); track filter.key) {
        <label class="filter">
          <span class="filter-label">{{ filter.label }}</span>

          @if (filter.kind === 'select') {
            <select
              khControl
              khDescribed="false"
              [value]="valueOf(filter.key)"
              (change)="apply(filter.key, $any($event.target).value)"
            >
              <option value="">Any</option>
              @for (option of filter.options ?? []; track option.value) {
                <option [value]="option.value">{{ option.label }}</option>
              }
            </select>
          } @else {
            <input
              khControl
              khDescribed="false"
              type="date"
              [value]="valueOf(filter.key)"
              (change)="apply(filter.key, $any($event.target).value)"
            />
          }
        </label>
      }
    </div>

    @if (chips().length > 0) {
      <div class="chips">
        @for (chip of chips(); track chip.key) {
          <kh-chip
            [label]="chip.label"
            [removable]="true"
            [selected]="true"
            (toggled)="apply(chip.key, '')"
          />
        }
        <button khButton type="button" variant="tertiary" size="sm" (click)="clearAll()">Clear all</button>
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .row {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      align-items: flex-end;
    }

    .search {
      position: relative;
      flex: 1 1 16rem;
      min-width: 12rem;
    }

    .search kh-icon {
      position: absolute;
      inset-block-start: 50%;
      inset-inline-start: var(--space-2);
      transform: translateY(-50%);
      color: var(--color-text-muted);
      pointer-events: none;
    }

    .search input {
      padding-inline-start: var(--space-8);
    }

    .filter {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
    }

    .filter-label {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: center;
      margin-block-start: var(--space-3);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FilterBar {
  readonly filters = input<readonly FilterDefinition[]>([]);
  readonly values = input<FilterValues>({});
  readonly searchable = input(true);
  readonly searchLabel = input('Search');
  /** The debounce on the search box, in milliseconds. */
  readonly searchDelayMs = input(300);

  /** The whole filter set, every time any part of it changes. The page writes it to the URL. */
  readonly changed = output<FilterValues>();

  protected readonly draft = signal('');
  private timer: ReturnType<typeof setTimeout> | null = null;

  protected readonly chips = computed(() => {
    const values = this.values();
    const labels = new Map(this.filters().map((filter) => [filter.key, filter]));

    return Object.entries(values)
      .filter(([key, value]) => key !== 'q' && value !== '')
      .map(([key, value]) => {
        const definition = labels.get(key);
        const option = definition?.options?.find((entry) => entry.value === value);
        return { key, label: `${definition?.label ?? key}: ${option?.label ?? value}` };
      });
  });

  constructor() {
    // The box follows the URL when the page navigates — a back gesture that restored the list but
    // not the words in the search box would be a box that lies about what is on screen. It must
    // not fight the user's typing, which is why it reads the input rather than the draft.
    effect(() => {
      const q = this.values()['q'] ?? '';
      this.draft.set(q);
    });
  }

  protected valueOf(key: string): string {
    return this.values()[key] ?? '';
  }

  protected onType(value: string): void {
    this.draft.set(value);
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.apply('q', value), this.searchDelayMs());
  }

  protected apply(key: string, value: string): void {
    const next: Record<string, string> = { ...this.values() };
    if (value === '') delete next[key];
    else next[key] = value;
    this.changed.emit(next);
  }

  protected clearAll(): void {
    this.draft.set('');
    this.changed.emit({});
  }
}
