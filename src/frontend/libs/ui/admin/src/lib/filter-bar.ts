import { ChangeDetectionStrategy, Component, computed, effect, input, output, signal } from '@angular/core';
import { Badge, Button, Chip, Control, Icon } from '@klarahome/ui-primitives';

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
  /**
   * Drawn as a row of chips under the search box rather than as a select behind the Filters
   * button. For the one question every visit to the list asks — usually "what state". Left unset,
   * the first select with few enough options to fit on a row is the quick one.
   */
  readonly quick?: boolean;
}

/** The filters that are on, keyed as the API names them. An absent key means "not filtered". */
export type FilterValues = Readonly<Record<string, string>>;

/** A select with more options than this does not fit on a row of chips, so it stays a select. */
const QUICK_CHIP_LIMIT = 8;

/**
 * The search box and filters above a list.
 *
 * **One pattern for every list (admin UX phase 4).** The row is the search box, then a row of
 * chips for the one filter every visit asks about — usually status — and then a single Filters
 * button for the rest: payment state, dates, channel. A list used to open on five controls before
 * its first row, which at phone width was the whole screen; now it opens on a search box and a
 * row of tappable states, and the rarer questions are one press away with a count on the button
 * saying how many of them are on.
 *
 * Two behaviours make it worth a component rather than a row of inputs on each screen:
 *
 *  - **The search box is debounced; the filters are not.** Typing is a stream of intentions and
 *    firing a request per keystroke is a request per keystroke; choosing "Cancelled" from a select
 *    is a single decision and waiting 300 ms to act on it only feels broken.
 *  - **What is on shows as removable chips.** The filters themselves are folded away behind the
 *    button, and a list quietly filtered by something the user cannot see is the single most
 *    common "the data is missing" support ticket a back office generates. The quick filter is
 *    already visible as a pressed chip, so it is not repeated here.
 *
 * The values are inputs, not state: the page owns them, usually in the URL, so a filtered list is
 * a link somebody can send to a colleague.
 */
@Component({
  selector: 'kh-filter-bar',
  imports: [Badge, Button, Chip, Control, Icon],
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

      @if (folded().length > 0) {
        <button
          khButton
          type="button"
          size="sm"
          class="toggle"
          [attr.aria-expanded]="panelOpen()"
          [attr.aria-controls]="panelId"
          (click)="panelOpen.set(!panelOpen())"
        >
          <kh-icon name="filter" size="sm" />
          Filters
          @if (foldedOnCount(); as count) {
            <kh-badge tone="primary">{{ count }}</kh-badge>
          }
        </button>
      }
    </div>

    @if (quick(); as filter) {
      <!-- The workflow tabs: one underlined tab per status, "All" first. Buttons that set a filter,
           not a tablist, because nothing here swaps a panel; the page below is the same table. -->
      <div class="quick" role="group" [attr.aria-label]="filter.label">
        <button
          type="button"
          class="tab"
          [attr.aria-pressed]="valueOf(filter.key) === ''"
          (click)="apply(filter.key, '')"
        >
          All
        </button>
        @for (option of filter.options ?? []; track option.value) {
          <button
            type="button"
            class="tab"
            [attr.aria-pressed]="valueOf(filter.key) === option.value"
            (click)="apply(filter.key, option.value)"
          >
            {{ option.label }}
          </button>
        }
      </div>
    }

    @if (panelOpen() && folded().length > 0) {
      <div class="panel" [id]="panelId">
        @for (filter of folded(); track filter.key) {
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
    }

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
      gap: var(--space-2);
      align-items: center;
    }

    .search {
      position: relative;
      flex: 1 1 auto;
      min-width: 0;
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

    .toggle {
      flex: none;
    }

    /* Underline tabs, as the prototype draws its status tabs. They scroll sideways on a phone
       rather than wrapping: a second row of tabs above the first row of data is exactly the height
       this pattern exists to win back. */
    .quick {
      display: flex;
      gap: var(--space-1);
      margin-block-start: var(--space-1);
      overflow-x: auto;
      scrollbar-width: none;
    }

    .quick::-webkit-scrollbar {
      display: none;
    }

    .tab {
      flex: none;
      padding: var(--space-2) var(--space-3);
      border: 0;
      border-block-end: 2px solid transparent;
      background: none;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
      white-space: nowrap;
      cursor: pointer;
    }

    .tab:hover {
      border-block-end-color: var(--color-border-strong);
      color: var(--color-text);
    }

    .tab:focus-visible {
      outline: 2px solid var(--color-focus-ring);
      outline-offset: -2px;
      border-radius: var(--radius-sm);
    }

    .tab[aria-pressed='true'] {
      border-block-end-color: var(--color-primary);
      color: var(--color-primary);
    }

    .panel {
      display: grid;
      gap: var(--space-3);
      margin-block-start: var(--space-3);
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface);
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

    @media (min-width: 768px) {
      .search {
        flex: 1 1 16rem;
        max-width: 28rem;
      }

      .quick {
        flex-wrap: wrap;
        overflow-x: visible;
      }

      .panel {
        grid-template-columns: repeat(auto-fill, minmax(12rem, 1fr));
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FilterBar {
  private static sequence = 0;
  protected readonly panelId = `kh-filters-${(FilterBar.sequence += 1)}`;

  readonly filters = input<readonly FilterDefinition[]>([]);
  readonly values = input<FilterValues>({});
  readonly searchable = input(true);
  readonly searchLabel = input('Search');
  /** The debounce on the search box, in milliseconds. */
  readonly searchDelayMs = input(300);

  /** The whole filter set, every time any part of it changes. The page writes it to the URL. */
  readonly changed = output<FilterValues>();

  protected readonly draft = signal('');
  protected readonly panelOpen = signal(false);
  private timer: ReturnType<typeof setTimeout> | null = null;

  /** The filter drawn as chips: the one marked `quick`, else the first select that fits. */
  protected readonly quick = computed<FilterDefinition | null>(() => {
    const filters = this.filters();
    const marked = filters.find((filter) => filter.quick);
    if (marked) return marked;
    return (
      filters.find(
        (filter) =>
          filter.kind === 'select' &&
          filter.quick !== false &&
          (filter.options?.length ?? 0) > 0 &&
          (filter.options?.length ?? 0) <= QUICK_CHIP_LIMIT,
      ) ?? null
    );
  });

  /** Everything else, behind the button. */
  protected readonly folded = computed(() => {
    const quick = this.quick();
    return this.filters().filter((filter) => filter !== quick);
  });

  /** How many of the folded filters are on — the number on the button. */
  protected readonly foldedOnCount = computed(() => {
    const values = this.values();
    return this.folded().filter((filter) => (values[filter.key] ?? '') !== '').length;
  });

  /** Whether any search or filter is on, quick one included. */
  readonly isFiltered = computed(() => Object.values(this.values()).some((value) => value !== ''));

  protected readonly chips = computed(() => {
    const values = this.values();
    const quickKey = this.quick()?.key;
    const labels = new Map(this.filters().map((filter) => [filter.key, filter]));

    return Object.entries(values)
      .filter(([key, value]) => key !== 'q' && key !== quickKey && value !== '')
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

    // A folded filter arriving already on — from the URL — opens the panel, so what is narrowing
    // the list is in view on the first paint and not only as a chip.
    effect(() => {
      if (this.foldedOnCount() > 0) this.panelOpen.set(true);
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

  /** Switches every search and filter off. Public: an empty table beside the bar offers it. */
  clearAll(): void {
    this.draft.set('');
    this.changed.emit({});
  }
}
