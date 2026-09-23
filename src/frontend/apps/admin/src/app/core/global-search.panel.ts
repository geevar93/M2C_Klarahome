import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { Router } from '@angular/router';
import { GlobalSearchService, SearchGroup } from '@klarahome/data-access-admin';
import { SessionStore } from '@klarahome/data-access-auth';
import { Modal } from '@klarahome/ui-admin';
import { Control, Skeleton } from '@klarahome/ui-primitives';
import { Subscription } from 'rxjs';

import { describeError } from './describe-error';
import { visibleSections } from './navigation';

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
 *
 * **A "Go to" group sits above the record results (Step 30).** `GlobalSearchService` only ever
 * finds *records* — an order, a product, a seller — because that is what the four list endpoints
 * it fans out to return. It never finds a *screen*, so a person typing "shipping" hoping to land
 * on delivery zones gets nothing, and has to already know that screen lives under Settings. The
 * "Go to" group is computed locally, from the same `visibleSections()` the sidebar renders — so it
 * can never offer a screen this session's sidebar does not also show — matched against the term by
 * label and by a short list of synonyms for the handful of screens whose sidebar label is not the
 * word most people type (`SCREEN_SYNONYMS` below). It costs no request and no debounce: unlike
 * the record groups, it is recomputed synchronously on every keystroke.
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
        <p class="state">Nothing found for “{{ term() }}”. Try a screen name, like “promotions”.</p>
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
  private readonly session = inject(SessionStore);

  readonly open = input(false);
  readonly closed = output<void>();

  protected readonly listId = 'global-search-results';
  protected readonly term = signal('');
  /** What the four record endpoints found. Populated asynchronously by `run()`. */
  private readonly recordGroups = signal<readonly SearchGroup[]>([]);
  protected readonly loading = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly activeIndex = signal(0);

  private timer: ReturnType<typeof setTimeout> | null = null;
  private inFlight: Subscription | null = null;

  /**
   * The sidebar screens this session's own labels rather than the module's — synonyms for the few
   * whose sidebar wording is not the word a person searching for it types. Keyed by the word,
   * valued by the exact `AdminNavItem.label` it should surface (see `navigation.ts`).
   */
  private static readonly SCREEN_SYNONYMS: Record<string, string> = {
    shipping: 'Delivery zones & rates',
    delivery: 'Delivery zones & rates',
    zones: 'Delivery zones & rates',
    rates: 'Delivery zones & rates',
    payout: 'Payouts',
    payouts: 'Payouts',
    coupon: 'Promotions',
    coupons: 'Promotions',
    discount: 'Promotions',
    discounts: 'Promotions',
    sale: 'Promotions',
    sales: 'Promotions',
    customer: 'Users',
    customers: 'Users',
    homepage: 'Pages',
  };

  /** The screens this session's sidebar shows, flattened once per search rather than per group. */
  private readonly screens = computed(() => visibleSections(this.session.session()).flatMap((section) => section.items));

  /**
   * The "Go to" group, matched locally against the sidebar's own labels — see the class doc for
   * why this exists as well as the record groups the service fetches.
   */
  protected readonly screenGroup = computed<SearchGroup | null>(() => {
    const query = this.term().trim().toLowerCase();
    if (query.length < 2) return null;

    const matched = new Map<string, { label: string; path: string }>();
    for (const item of this.screens()) {
      if (item.label.toLowerCase().includes(query)) matched.set(item.path, item);
    }
    for (const [word, label] of Object.entries(GlobalSearchPanel.SCREEN_SYNONYMS)) {
      if (!word.includes(query) && !query.includes(word)) continue;
      const item = this.screens().find((entry) => entry.label === label);
      if (item) matched.set(item.path, item);
    }

    if (matched.size === 0) return null;
    return {
      key: 'screens',
      label: 'Go to',
      hits: [...matched.values()]
        .slice(0, 5)
        .map((item) => ({ id: item.path, title: item.label, subtitle: 'Screen', path: item.path })),
    };
  });

  /** The "Go to" group first, then whatever records the search found — see the class doc. */
  protected readonly groups = computed<readonly SearchGroup[]>(() => {
    const screens = this.screenGroup();
    return screens ? [screens, ...this.recordGroups()] : this.recordGroups();
  });

  constructor() {
    // Reopening starts clean. A dialog still showing the last search's results for a moment reads
    // as those results being for what you are about to type.
    effect(() => {
      if (this.open()) return;
      this.term.set('');
      this.recordGroups.set([]);
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
      this.recordGroups.set([]);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.failure.set(null);
    this.inFlight = this.search.search(value).subscribe({
      next: (groups) => {
        this.recordGroups.set(groups);
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
