import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { DestroyRef } from '@angular/core';
import { Button, Control, Field, Icon } from '@klarahome/ui-primitives';
import { Observable, Subject, Subscription, debounceTime, distinctUntilChanged, switchMap } from 'rxjs';

/** One thing the picker can offer, whatever kind of thing it is. */
export interface EntityOption {
  /** The identifier the caller wanted. This is what the picker yields.  */
  readonly id: string;
  /** What the operator reads. */
  readonly label: string;
  /** A second line: a code, a SKU, a price — whatever tells two similar rows apart. */
  readonly hint?: string | null;
}

/** How the picker finds candidates. Returns at most a screenful; the picker does not page. */
export type EntitySearch = (term: string) => Observable<readonly EntityOption[]>;

/**
 * One typeahead that yields an identifier.
 *
 * **Seven places in this back office asked an operator to type a raw UUID**: a promotion's seller
 * and listing scope, a price-list row, a collection's pinned product, a seller's staff user, a
 * ledger adjustment's seller, and the report and ledger seller filters. Every one of those
 * endpoints already searches; what was missing was a control that turned a search into an id
 * (Step 28B, deliverable 15).
 *
 * **It takes a search function rather than a service.** A component in `ui-admin` may not reach
 * into `data-access-admin` — that is the boundary that keeps this library renderable without an
 * API — so the caller supplies the one call it needs. That is also what lets one control find
 * products, sellers, users and listings without knowing what any of them are.
 *
 * **A chosen value is shown as a chip, not as text in the input.** The alternative — leaving the
 * label in the box — reads as a draft an operator can edit, and editing it does not change the id
 * underneath. A chip with a clear button says what actually happened: something was chosen, and it
 * can be un-chosen.
 *
 * **The raw id stays reachable.** An operator who has an id from a support conversation can paste
 * it, and the picker takes it as the value without a search; it is the paste path the seven screens
 * were built around, kept because it is genuinely the fastest route when you already know the id.
 */
@Component({
  selector: 'kh-entity-picker',
  imports: [Button, Control, Field, Icon],
  template: `
    <kh-field [label]="label()" [for]="inputId()" [hint]="hint()" [error]="error()" [optional]="optional()">
      @if (chosen(); as current) {
        <div class="chosen">
          <span class="chip">
            <span class="chip-label">{{ current.label }}</span>
            @if (current.hint) {
              <span class="chip-hint">{{ current.hint }}</span>
            }
          </span>
          <button
            khButton
            type="button"
            size="sm"
            variant="tertiary"
            [attr.aria-label]="'Clear ' + label()"
            (click)="clear()"
          >
            <kh-icon name="close" size="sm" />
          </button>
        </div>
      } @else {
        <input
          khControl
          [id]="inputId()"
          type="search"
          role="combobox"
          autocomplete="off"
          [attr.aria-expanded]="options().length > 0"
          [attr.aria-controls]="inputId() + '-options'"
          [attr.aria-busy]="searching()"
          [placeholder]="placeholder()"
          [value]="term()"
          (input)="type($any($event.target).value)"
        />
      }
    </kh-field>

    @if (!chosen()) {
      <div class="results" [id]="inputId() + '-options'" role="listbox" [attr.aria-label]="label()">
        @if (searching()) {
          <p class="note">Searching…</p>
        } @else if (failed()) {
          <p class="note danger">That search could not be run. Try again, or paste the id.</p>
        } @else if (term().length >= minimumLength() && options().length === 0) {
          <p class="note">Nothing matched “{{ term() }}”.</p>
        } @else {
          @for (option of options(); track option.id) {
            <button
              type="button"
              class="option"
              role="option"
              [attr.aria-selected]="false"
              (click)="choose(option)"
            >
              <span class="option-label">{{ option.label }}</span>
              @if (option.hint) {
                <span class="option-hint">{{ option.hint }}</span>
              }
            </button>
          }
        }
      </div>
    }
  `,
  /* eslint-disable local/no-hardcoded-spacing-in-styles -- '.option's gap: 2px is a deliberate
     micro-gap between a result's title and subtitle line. The smallest spacing token, --space-1,
     is 4px; doubling this tight a gap is a visual change of its own and one call site does not
     justify a new token below --space-1 (Step 30 frontend-visual-consistency-review.md, finding
     #10). Disabled for the whole block rather than a line: the violation sits deep inside a
     template literal, where a directive comment cannot be attached to just that line. */
  styles: `
    :host {
      display: block;
    }

    .chosen {
      display: flex;
      gap: var(--space-2);
      align-items: center;
    }

    .chip {
      display: flex;
      flex-direction: column;
      flex: 1;
      min-inline-size: 0;
      padding: var(--space-2) var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-sm);
      background: var(--color-surface-muted);
    }

    .chip-label,
    .option-label {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .chip-hint,
    .option-hint,
    .note {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .note.danger {
      color: var(--color-danger-text);
    }

    .results {
      display: flex;
      flex-direction: column;
      margin-block-start: var(--space-1);
    }

    .results:empty {
      display: none;
    }

    .note {
      margin: 0;
      padding: var(--space-2) var(--space-3);
    }

    .option {
      display: flex;
      flex-direction: column;
      gap: 2px;
      padding: var(--space-2) var(--space-3);
      border: none;
      border-radius: var(--radius-sm);
      background: none;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .option:hover,
    .option:focus-visible {
      background: var(--color-surface-muted);
    }
  `,
  /* eslint-enable local/no-hardcoded-spacing-in-styles */
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntityPicker {
  private readonly destroyRef = inject(DestroyRef);

  /** The field label, and the accessible name of both the input and the result list. */
  readonly label = input.required<string>();

  /** A unique id for the input, so the label points at it. */
  readonly inputId = input.required<string>();

  readonly hint = input<string | null>(null);
  readonly error = input<string | null>(null);
  readonly optional = input(false);
  readonly placeholder = input('Search, or paste an id');

  /**
   * How many characters before a search runs.
   *
   * Two, not one: a single character matches most of a catalogue, and a request per keystroke on a
   * field somebody is still thinking about is the most expensive thing a typeahead can do.
   */
  readonly minimumLength = input(2);

  /** The search. Called on a debounce, and its previous call is cancelled rather than raced. */
  readonly search = input.required<EntitySearch>();

  /** The value, as the caller holds it. Setting it externally shows the chip. */
  readonly value = input<EntityOption | null>(null);

  /** The chosen entity, or null when it was cleared. */
  readonly chose = output<EntityOption | null>();

  protected readonly term = signal('');
  protected readonly options = signal<readonly EntityOption[]>([]);
  protected readonly searching = signal(false);
  protected readonly failed = signal(false);

  private readonly picked = signal<EntityOption | null>(null);
  private readonly terms = new Subject<string>();
  private subscription: Subscription | null = null;

  /** The chip's contents: what the caller set, or what was picked here. */
  protected readonly chosen = computed(() => this.value() ?? this.picked());

  constructor() {
    // Subscribed in the constructor rather than on first keystroke, so the debounce and the
    // switchMap exist before there is anything to debounce. `switchMap` is what makes a slow
    // search for "ta" unable to overwrite the results for "table".
    effect(() => {
      const search = this.search();

      this.subscription?.unsubscribe();
      this.subscription = this.terms
        .pipe(
          debounceTime(250),
          distinctUntilChanged(),
          switchMap((term) => search(term)),
        )
        .subscribe({
          next: (options) => {
            this.searching.set(false);
            this.failed.set(false);
            this.options.set(options);
          },
          error: () => {
            this.searching.set(false);
            this.failed.set(true);
            this.options.set([]);
          },
        });
    });

    this.destroyRef.onDestroy(() => this.subscription?.unsubscribe());
  }

  protected type(term: string): void {
    this.term.set(term);
    this.failed.set(false);

    // A pasted identifier is taken as the value rather than searched for. Somebody who already has
    // the id is not looking for it, and most of these endpoints cannot find a row by its own id
    // anyway — they search names.
    if (UUID.test(term.trim())) {
      this.choose({ id: term.trim(), label: term.trim(), hint: 'Pasted identifier' });
      return;
    }

    if (term.trim().length < this.minimumLength()) {
      this.options.set([]);
      this.searching.set(false);
      return;
    }

    this.searching.set(true);
    this.terms.next(term.trim());
  }

  protected choose(option: EntityOption): void {
    this.picked.set(option);
    this.term.set('');
    this.options.set([]);
    this.searching.set(false);
    this.chose.emit(option);
  }

  protected clear(): void {
    this.picked.set(null);
    this.term.set('');
    this.options.set([]);
    this.chose.emit(null);
  }
}

/** A UUID in the shape this platform's ids take. Used only to recognise a paste. */
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
