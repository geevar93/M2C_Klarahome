import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { Alert, Button, Control, Icon } from '@klarahome/ui-primitives';
import { Subject, Subscription, catchError, debounceTime, map, of, startWith, switchMap } from 'rxjs';

import { EntityOption, EntitySearch } from './entity-picker';
import { Modal } from './modal';

/**
 * A dialog that finds several things at once and hands back all of them.
 *
 * `kh-entity-picker` turns a search into one id. The page composer needed the plural: a product
 * carousel of twelve products was twelve UUIDs copied out of the catalogue one tab at a time. This
 * is that search in a modal, with a checkbox per row and a tray of what has been ticked so far.
 *
 * **The selection outlives the search.** Ticking two lamps, searching "rug" and ticking a third is
 * three chosen things, not one — the tray is the selection, and the result list is only where rows
 * are found. That is also why a chosen row that no longer matches the term stays in the tray.
 *
 * **What is already there is shown as already there.** `existingIds` rows are ticked and disabled,
 * so the dialog cannot add a duplicate and the operator can see what the block holds without
 * closing it.
 *
 * Like its single-value sibling it takes a search function, not a service — `ui-admin` does not
 * reach into the API client — and it runs that search with an empty term on open, so there is
 * something to pick from before anybody types.
 */
@Component({
  selector: 'kh-entity-multi-picker',
  imports: [Alert, Button, Control, Icon, Modal],
  template: `
    <kh-modal [open]="open()" [heading]="heading()" width="44rem" (closed)="closed.emit()">
      <input
        khControl
        type="search"
        autocomplete="off"
        [attr.aria-label]="'Search ' + noun()"
        [attr.aria-controls]="listId"
        [placeholder]="placeholder()"
        [value]="term()"
        (input)="type($any($event.target).value)"
      />

      @if (limit() !== null) {
        <p class="note">{{ chosen().length }} selected · room for {{ remaining() }} more</p>
      }

      @if (chosen().length > 0) {
        <ul class="tray" aria-label="Selected">
          @for (option of chosen(); track option.id) {
            <li class="chip">
              @if (option.imageUrl) {
                <img class="chip-thumb" [src]="option.imageUrl" alt="" width="24" height="24" />
              }
              <span>{{ option.label }}</span>
              <button
                khButton
                type="button"
                size="sm"
                variant="tertiary"
                [iconOnly]="true"
                [attr.aria-label]="'Remove ' + option.label"
                (click)="toggle(option)"
              >
                <kh-icon name="close" size="sm" />
              </button>
            </li>
          }
        </ul>
      }

      @if (failed()) {
        <kh-alert tone="danger" heading="That search could not be run">Try again in a moment.</kh-alert>
      }

      <ul
        class="results"
        [id]="listId"
        role="listbox"
        aria-multiselectable="true"
        [attr.aria-label]="heading()"
      >
        @if (searching() && options().length === 0) {
          <li class="note">Searching…</li>
        } @else if (!failed() && options().length === 0) {
          <li class="note">
            {{ term() ? 'Nothing matched “' + term() + '”.' : 'Nothing to choose from yet.' }}
          </li>
        }

        @for (option of options(); track option.id) {
          <li>
            <label
              class="row"
              role="option"
              [class.existing]="isExisting(option.id)"
              [attr.aria-selected]="isExisting(option.id) || isChosen(option.id)"
            >
              <input
                type="checkbox"
                [checked]="isExisting(option.id) || isChosen(option.id)"
                [disabled]="isExisting(option.id) || (!isChosen(option.id) && remaining() === 0)"
                (change)="toggle(option)"
              />
              @if (showImages()) {
                @if (option.imageUrl) {
                  <img class="thumb" [src]="option.imageUrl" alt="" loading="lazy" width="48" height="48" />
                } @else {
                  <span class="thumb" aria-hidden="true"></span>
                }
              }
              <span class="text">
                <span class="label">{{ option.label }}</span>
                @if (isExisting(option.id)) {
                  <span class="hint">Already added</span>
                } @else if (option.hint) {
                  <span class="hint">{{ option.hint }}</span>
                }
              </span>
            </label>
          </li>
        }
      </ul>

      <div slot="footer">
        <button khButton type="button" variant="tertiary" (click)="closed.emit()">Cancel</button>
        <button
          khButton
          type="button"
          variant="primary"
          [disabled]="chosen().length === 0"
          (click)="confirm()"
        >
          {{ chosen().length === 0 ? 'Add' : 'Add ' + chosen().length }}
        </button>
      </div>
    </kh-modal>
  `,
  styles: `
    .note {
      margin: var(--space-2) 0 0;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .tray {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin: var(--space-3) 0 0;
      padding: 0;
      list-style: none;
    }

    .chip {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      max-inline-size: 100%;
      padding-inline-start: var(--space-2);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-full);
      background: var(--color-surface-muted);
      font-size: var(--text-sm);
    }

    .chip span {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .results {
      display: flex;
      flex-direction: column;
      margin: var(--space-3) 0 0;
      padding: 0;
      list-style: none;
      border-block-start: 1px solid var(--color-border);
    }

    .results .note {
      padding: var(--space-3);
    }

    .row {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      min-block-size: var(--touch-target-min);
      padding: var(--space-2) var(--space-3);
      border-block-end: 1px solid var(--color-border);
      cursor: pointer;
    }

    .row:hover {
      background: var(--color-surface-muted);
    }

    .row.existing {
      cursor: default;
      color: var(--color-text-muted);
    }

    .row input {
      flex-shrink: 0;
      inline-size: 1.125rem;
      block-size: 1.125rem;
    }

    /* A fixed square whether or not the row has a picture, so the names line up down the list. */
    .thumb {
      flex-shrink: 0;
      inline-size: 3rem;
      block-size: 3rem;
      border-radius: var(--radius-sm);
      object-fit: cover;
      background: var(--color-surface-muted);
    }

    .chip-thumb {
      flex-shrink: 0;
      inline-size: 1.5rem;
      block-size: 1.5rem;
      border-radius: var(--radius-full);
      object-fit: cover;
    }

    .text {
      display: flex;
      flex-direction: column;
      min-inline-size: 0;
    }

    .label {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .hint {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntityMultiPicker {
  private static sequence = 0;

  readonly open = input(false);
  readonly heading = input.required<string>();
  /** What is being chosen, in the plural: "products". Used in the search box's name. */
  readonly noun = input('items');
  readonly placeholder = input('Search by name, or paste an id');
  readonly search = input.required<EntitySearch>();
  /** Ids the caller already holds: shown ticked and disabled, never returned again. */
  readonly existingIds = input<readonly string[]>([]);
  /** How many more may be added, or null for no limit. */
  readonly limit = input<number | null>(null);

  /** Everything ticked, in the order it was ticked. Emitted once, on "Add". */
  readonly confirmed = output<readonly EntityOption[]>();
  readonly closed = output<void>();

  protected readonly listId = `kh-multi-picker-${++EntityMultiPicker.sequence}`;
  protected readonly term = signal('');
  protected readonly options = signal<readonly EntityOption[]>([]);
  protected readonly chosen = signal<readonly EntityOption[]>([]);
  protected readonly searching = signal(false);
  protected readonly failed = signal(false);

  /** Thumbnails only when some result has one: a list of collections needs no empty squares. */
  protected readonly showImages = computed(() => this.options().some((option) => option.imageUrl));

  protected readonly remaining = computed(() => {
    const limit = this.limit();
    return limit === null ? Number.POSITIVE_INFINITY : Math.max(0, limit - this.chosen().length);
  });

  private readonly existing = computed(() => new Set(this.existingIds()));
  private readonly terms = new Subject<string>();
  private subscription: Subscription | null = null;

  constructor() {
    // Every opening is a fresh dialog: last time's ticks were either added or abandoned.
    effect(() => {
      if (!this.open()) return;
      untracked(() => {
        this.chosen.set([]);
        this.term.set('');
        this.run();
      });
    });

    inject(DestroyRef).onDestroy(() => this.subscription?.unsubscribe());
  }

  protected type(term: string): void {
    this.term.set(term);
    this.searching.set(true);
    this.terms.next(term.trim());
  }

  protected isChosen(id: string): boolean {
    return this.chosen().some((option) => option.id === id);
  }

  protected isExisting(id: string): boolean {
    return this.existing().has(id);
  }

  protected toggle(option: EntityOption): void {
    if (this.isExisting(option.id)) return;

    if (this.isChosen(option.id)) {
      this.chosen.update((current) => current.filter((entry) => entry.id !== option.id));
    } else if (this.remaining() > 0) {
      this.chosen.update((current) => [...current, option]);
    }
  }

  protected confirm(): void {
    this.confirmed.emit(this.chosen());
  }

  /**
   * One pipeline per opening, seeded with the empty term so the list is populated on open.
   * `switchMap` cancels a slow search for "ta" rather than let it overwrite "table".
   */
  private run(): void {
    const search = this.search();

    this.subscription?.unsubscribe();
    this.searching.set(true);
    this.subscription = this.terms
      .pipe(
        debounceTime(250),
        startWith(''),
        switchMap((term) =>
          search(term).pipe(
            map((options) => ({ options, failed: false })),
            catchError(() => of({ options: [] as readonly EntityOption[], failed: true })),
          ),
        ),
      )
      .subscribe(({ options, failed }) => {
        this.searching.set(false);
        this.failed.set(failed);
        this.options.set(options);
      });
  }
}
