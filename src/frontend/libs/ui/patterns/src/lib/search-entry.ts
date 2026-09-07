import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  input,
  linkedSignal,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Button, Icon } from '@klarahome/ui-primitives';

import { SuggestionView } from './catalog.model';
import { SearchSuggestions } from './search-suggestions';

/**
 * The search box in the header, and the suggestion popup under it.
 *
 * A real `<form role="search">` with a submit button, not a bare input with a keydown handler:
 * the form is what gives a phone keyboard its "Search" key, what makes Enter work without any
 * JavaScript at all, and what a screen reader announces as the search landmark. Everything the
 * autocomplete adds sits on top of that — with suggestions switched off, or JavaScript broken,
 * the box still searches.
 *
 * **The combobox pattern, done properly.** The input carries `role="combobox"`,
 * `aria-expanded`, `aria-controls` and `aria-activedescendant`; the arrow keys move the active
 * option **without moving focus**, which is the part naive implementations get wrong and which is
 * why the keyboard model lives here, in the component that owns the input, rather than in the
 * popup. Escape closes the list and leaves the query alone; a second Escape is the browser's to
 * handle.
 *
 * Suggestions are debounced by the page, not here: how long to wait before asking the API is a
 * network decision, and this component would be the wrong place to hard-code it.
 */
@Component({
  selector: 'kh-search-entry',
  imports: [Button, Icon, SearchSuggestions],
  template: `
    <form role="search" (submit)="submit($event)">
      <label class="kh-visually-hidden" [attr.for]="inputId()">{{ label() }}</label>
      <div class="field">
        <kh-icon name="search" size="sm" />
        <input
          #input
          [id]="inputId()"
          type="search"
          name="q"
          autocomplete="off"
          enterkeyhint="search"
          role="combobox"
          aria-autocomplete="list"
          [attr.aria-expanded]="isOpen()"
          [attr.aria-controls]="listboxId()"
          [attr.aria-activedescendant]="activeOptionId()"
          [attr.placeholder]="placeholder()"
          [value]="query()"
          (input)="onInput($any($event.target).value)"
          (keydown)="onKeydown($event)"
          (focus)="opened.set(true)"
          (blur)="opened.set(false)"
        />
      </div>
      <button khButton variant="primary" size="sm" type="submit">Search</button>

      @if (isOpen()) {
        <kh-search-suggestions
          [items]="suggestions()"
          [heading]="suggestionsHeading()"
          [activeIndex]="activeIndex()"
          [listboxId]="listboxId()"
          (chosen)="choose($event)"
        />
      }
    </form>
  `,
  styles: `
    form {
      position: relative;
      display: flex;
      gap: var(--space-2);
      align-items: center;
      width: 100%;
    }

    .field {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      flex: 1;
      min-width: 0;
      padding-inline: var(--space-3);
      min-height: var(--touch-target-min);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      color: var(--color-text-muted);
    }

    .field:focus-within {
      border-color: var(--color-border-strong);
    }

    input {
      flex: 1;
      min-width: 0;
      border: 0;
      background: transparent;
      color: var(--color-text);
      font-size: var(--text-base);
      outline: none;
    }

    /* The browser's own clear affordance is a 12px target in the corner of a 44px control. */
    input::-webkit-search-cancel-button {
      appearance: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SearchEntry {
  /** Prefills the box — the search page passes the query the results are for. */
  readonly initialQuery = input('');
  readonly placeholder = input('Search for products');
  readonly label = input('Search for products');
  /** Distinguishes the header's box from the drawer's when both are in the document. */
  readonly inputId = input('kh-search');
  /** What to offer. The page decides whether these are recent searches or server suggestions. */
  readonly suggestions = input<readonly SuggestionView[]>([]);
  readonly suggestionsHeading = input('');

  readonly submitted = output<string>();
  /** The query changed. The page debounces this and asks the API. */
  readonly queryChanged = output<string>();
  readonly suggestionChosen = output<SuggestionView>();

  /**
   * Writable, but seeded from the input.
   *
   * A `linkedSignal` rather than a copy in the constructor: typing must not be fought by the
   * parent on every keystroke, and yet navigating from one search to another — which reuses this
   * component — has to put the new query in the box. This is the one construct that does both.
   */
  protected readonly query = linkedSignal(() => this.initialQuery());

  protected readonly opened = signal(false);
  protected readonly activeIndex = signal(-1);
  protected readonly listboxId = computed(() => `${this.inputId()}-listbox`);

  /**
   * The popup shows only while the box has focus and there is something to show.
   *
   * `blur` closes it, and the options call `preventDefault` on `mousedown` so that clicking one
   * does not blur the input before the click lands — the single most common autocomplete bug.
   */
  protected readonly isOpen = computed(() => this.opened() && this.suggestions().length > 0);

  protected readonly activeOptionId = computed(() => {
    const index = this.activeIndex();
    return this.isOpen() && index >= 0 ? `${this.listboxId()}-option-${index}` : null;
  });

  private readonly input = viewChild<ElementRef<HTMLInputElement>>('input');

  /** Moves focus into the box — used when the mobile search drawer opens. */
  focus(): void {
    this.input()?.nativeElement.focus();
  }

  protected onInput(value: string): void {
    this.query.set(value);
    // A new query invalidates the highlighted row: the list under it is about to change.
    this.activeIndex.set(-1);
    this.opened.set(true);
    this.queryChanged.emit(value);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.suggestions().length;

    if (event.key === 'Escape') {
      this.opened.set(false);
      this.activeIndex.set(-1);
      return;
    }

    if (event.key === 'Enter') {
      const active = this.activeSuggestion();
      if (active) {
        event.preventDefault();
        this.choose(active);
      }
      return;
    }

    if (count === 0 || (event.key !== 'ArrowDown' && event.key !== 'ArrowUp')) return;

    // The caret would otherwise jump to the start or end of the text on every arrow press.
    event.preventDefault();
    this.opened.set(true);

    const step = event.key === 'ArrowDown' ? 1 : -1;
    // Wraps through −1, which is "back to what I typed" — the state a shopper needs to be able to
    // return to after walking past the bottom of the list.
    this.activeIndex.update((index) => {
      const next = index + step;
      if (next >= count) return -1;
      if (next < -1) return count - 1;
      return next;
    });
  }

  protected choose(item: SuggestionView): void {
    this.opened.set(false);
    this.activeIndex.set(-1);
    this.query.set(item.text);
    this.suggestionChosen.emit(item);
  }

  protected submit(event: Event): void {
    event.preventDefault();

    const active = this.activeSuggestion();
    if (active) {
      this.choose(active);
      return;
    }

    const value = this.query().trim();
    if (!value) return;
    this.opened.set(false);
    this.submitted.emit(value);
  }

  private activeSuggestion(): SuggestionView | null {
    const index = this.activeIndex();
    return this.isOpen() && index >= 0 ? (this.suggestions()[index] ?? null) : null;
  }
}
