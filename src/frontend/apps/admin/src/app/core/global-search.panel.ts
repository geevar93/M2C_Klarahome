import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { Router } from '@angular/router';
import { GlobalSearchService, SearchGroup } from '@klarahome/data-access-admin';
import { Modal } from '@klarahome/ui-admin';
import { Control, Skeleton } from '@klarahome/ui-primitives';
import { Subscription } from 'rxjs';

import { describeError } from './describe-error';

/**
 * Global search, as a dialog over whatever you were doing.
 *
 * A dialog and not a page because a search is an interruption: somebody is on an order and a
 * customer says a number. Leaving the screen to look it up and coming back is the wrong shape, and
 * a results *page* would also lose the filters of the list underneath it.
 *
 * The keyboard model is the whole point of building it rather than using a select, and it is the
 * `combobox` pattern the storefront's suggestions already use (WAI-ARIA APG):
 *
 *  - the input keeps focus throughout, so typing never stops working;
 *  - Down and Up move an **active option** that is pointed at by `aria-activedescendant` rather
 *    than actually focused, which is what lets both those things be true at once;
 *  - Enter opens the active result, Escape closes the dialog;
 *  - the listbox is flat across groups, because a reader arrowing through results does not care
 *    that hit four is the first seller rather than the last product.
 *
 * Searching is debounced and the previous request is cancelled, so typing "ORD-104" is one search
 * rather than seven — and a slow early answer can never overwrite a fast later one.
 */
@Component({
  selector: 'kh-global-search',
  imports: [Control, Modal, Skeleton],
  template: `
    <kh-modal [open]="open()" heading="Search" width="36rem" (closed)="close()">
      <input
        khControl
        khDescribed="false"
        #box
        type="search"
        role="combobox"
        aria-autocomplete="list"
        aria-label="Search orders, products, sellers and users"
        placeholder="Order number, product, seller, email…"
        autocomplete="off"
        [attr.aria-expanded]="hits().length > 0"
        [attr.aria-controls]="listId"
        [attr.aria-activedescendant]="activeId()"
        [value]="term()"
        (input)="onType($any($event.target).value)"
        (keydown)="onKeydown($event)"
      />

      @if (loading()) {
        <div class="state"><kh-skeleton [lines]="3" height="1rem" /></div>
      } @else if (failure(); as message) {
        <p class="state error">{{ message }}</p>
      } @else if (term().trim().length < 2) {
        <p class="state">Type at least two characters. Orders are matched by their number.</p>
      } @else if (hits().length === 0) {
        <p class="state">Nothing found for “{{ term() }}”.</p>
      } @else {
        <ul role="listbox" [id]="listId" [attr.aria-label]="'Search results'">
          @for (group of groups(); track group.key) {
            <li role="presentation" class="group">
              <p class="group-label" [id]="listId + '-' + group.key">{{ group.label }}</p>
              <ul role="group" [attr.aria-labelledby]="listId + '-' + group.key">
                @for (hit of group.hits; track hit.path) {
                  <li
                    role="option"
                    [id]="listId + '-opt-' + indexOf(hit.path)"
                    [attr.aria-selected]="activeIndex() === indexOf(hit.path)"
                    [class.active]="activeIndex() === indexOf(hit.path)"
                    (click)="go(hit.path)"
                    (mouseenter)="activeIndex.set(indexOf(hit.path))"
                  >
                    <span class="title">{{ hit.title }}</span>
                    <span class="subtitle">{{ hit.subtitle }}</span>
                  </li>
                }
              </ul>
            </li>
          }
        </ul>
      }
    </kh-modal>
  `,
  styles: `
    input {
      margin-block-end: var(--space-3);
    }

    .state {
      margin: 0;
      padding: var(--space-4) 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .state.error {
      color: var(--color-danger);
    }

    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .group + .group {
      margin-block-start: var(--space-3);
    }

    .group-label {
      margin: 0 0 var(--space-1);
      font-size: var(--text-xs);
      font-weight: var(--weight-medium);
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--color-text-muted);
    }

    [role='option'] {
      display: flex;
      flex-direction: column;
      padding: var(--space-2);
      border-radius: var(--radius-md);
      cursor: pointer;
    }

    [role='option'].active {
      background: var(--color-primary-subtle);
    }

    .title {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .subtitle {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GlobalSearchPanel {
  private readonly search = inject(GlobalSearchService);
  private readonly router = inject(Router);

  readonly open = input(false);
  readonly closed = output<void>();

  protected readonly listId = 'global-search-results';
  protected readonly term = signal('');
  protected readonly groups = signal<readonly SearchGroup[]>([]);
  protected readonly loading = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly activeIndex = signal(0);

  private timer: ReturnType<typeof setTimeout> | null = null;
  private inFlight: Subscription | null = null;

  constructor() {
    // Reopening starts clean. A dialog still showing the last search's results for a moment reads
    // as those results being for what you are about to type.
    effect(() => {
      if (this.open()) return;
      this.term.set('');
      this.groups.set([]);
      this.failure.set(null);
      this.activeIndex.set(0);
      this.cancel();
    });
  }

  /** Every hit, flattened, so the keyboard can move through them regardless of grouping. */
  protected hits(): readonly { path: string }[] {
    return this.groups().flatMap((group) => group.hits);
  }

  protected indexOf(path: string): number {
    return this.hits().findIndex((hit) => hit.path === path);
  }

  protected activeId(): string | null {
    return this.hits().length > 0 ? `${this.listId}-opt-${this.activeIndex()}` : null;
  }

  protected onType(value: string): void {
    this.term.set(value);
    this.activeIndex.set(0);
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.run(value), 250);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.hits().length;

    switch (event.key) {
      case 'ArrowDown':
        if (count === 0) return;
        event.preventDefault();
        // Wrapping, because a list of five with no wrap makes the last item a dead end.
        this.activeIndex.update((index) => (index + 1) % count);
        return;
      case 'ArrowUp':
        if (count === 0) return;
        event.preventDefault();
        this.activeIndex.update((index) => (index - 1 + count) % count);
        return;
      case 'Enter': {
        const hit = this.hits()[this.activeIndex()];
        if (!hit) return;
        event.preventDefault();
        this.go(hit.path);
        return;
      }
      default:
        return;
    }
  }

  protected go(path: string): void {
    this.close();
    void this.router.navigateByUrl(path);
  }

  protected close(): void {
    this.cancel();
    this.closed.emit();
  }

  private run(value: string): void {
    this.cancel();

    if (value.trim().length < 2) {
      this.groups.set([]);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.failure.set(null);
    this.inFlight = this.search.search(value).subscribe({
      next: (groups) => {
        this.groups.set(groups);
        this.activeIndex.set(0);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.failure.set(describeError(error, 'That search could not be run. Try again.'));
        this.loading.set(false);
      },
    });
  }

  private cancel(): void {
    if (this.timer) {
      clearTimeout(this.timer);
      this.timer = null;
    }
    this.inFlight?.unsubscribe();
    this.inFlight = null;
  }
}
