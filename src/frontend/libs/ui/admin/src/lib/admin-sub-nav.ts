import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AdminNavCategory, AdminNavItem } from './admin.model';
import { currentPath, itemMatches, sectionFor } from './nav-location';

/**
 * The screens of the open category, as a row of tabs above the page.
 *
 * The sidebar names the category; this is the "inside" of it. Two tiers, and only as many as the
 * category needs:
 *
 *  - **Sections**, a compact first row, only when the category has more than one (Catalogue's
 *    Products, Organise and Inventory). Choosing one opens its first screen. A section you are not
 *    in wears the sum of its queue counts, so "something is waiting in Inventory" is visible from
 *    Products.
 *  - **Screens**, the tabs of the current section, each wearing its own queue count. A category
 *    with a single screen (Home) draws nothing at all, because a row of one tab is not navigation.
 *
 * They are route links in a `<nav>`, not an ARIA tablist: a tablist promises arrow-key roving and
 * panels in the same page, and these are ordinary links that go somewhere. The current screen is
 * `aria-current="page"` - and on a detail route (`/orders/123`) it is still the list's tab.
 *
 * On a phone each row scrolls sideways inside itself rather than wrapping onto four lines or
 * widening the page, and the active tab is scrolled into view after every navigation, so the
 * current screen is never off-screen. Links reach the 44px touch floor under `pointer: coarse`.
 */
@Component({
  selector: 'kh-admin-sub-nav',
  imports: [RouterLink],
  template: `
    @if (showSections()) {
      <nav class="sections" aria-label="Sections">
        <ul>
          @for (section of category()!.sections; track section.label) {
            <li>
              <a
                [routerLink]="section.items[0].path"
                [attr.aria-current]="section === current() ? 'page' : null"
              >
                {{ section.label }}
                @if (section.badge && section !== current()) {
                  <span class="count">{{ section.badge }}</span>
                }
              </a>
            </li>
          }
        </ul>
      </nav>
    }

    @if (showScreens()) {
      <nav class="screens" [attr.aria-label]="(category()!.label) + ' screens'">
        <ul>
          @for (item of current()!.items; track item.path) {
            <li>
              <a [routerLink]="item.path" [attr.aria-current]="isActive(item) ? 'page' : null">
                {{ item.label }}
                @if (item.badge) {
                  <span class="count">{{ item.badge }}</span>
                }
              </a>
            </li>
          }
        </ul>
      </nav>
    }
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }

    nav {
      min-width: 0;
    }

    /* Space is on the last row rather than the host, so a category that draws nothing leaves none. */
    nav:last-of-type {
      margin-block-end: var(--space-3);
    }

    /* Each row scrolls inside itself; the page never does. position: relative makes it the offset
       parent the scroll-into-view maths measures from. The negative margin and matching padding let
       the focus ring and the last tab's edge sit inside the scrollport instead of being clipped. */
    ul {
      position: relative;
      display: flex;
      gap: var(--space-2);
      align-items: center;
      margin: 0 calc(var(--space-1) * -1);
      padding: var(--space-1);
      overflow-x: auto;
      overscroll-behavior-x: contain;
      list-style: none;
      scrollbar-width: none;
    }

    ul::-webkit-scrollbar {
      display: none;
    }

    li {
      flex: none;
    }

    a {
      display: inline-flex;
      gap: var(--space-2);
      align-items: center;
      min-height: 2.25rem;
      padding: var(--space-1) var(--space-3);
      border: 1px solid transparent;
      border-radius: var(--radius-full);
      color: var(--color-text-subtle);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
      text-decoration: none;
      white-space: nowrap;
    }

    a:hover {
      background: var(--color-surface);
      color: var(--color-text);
    }

    a:focus-visible {
      outline: 2px solid var(--color-focus-ring);
      outline-offset: 1px;
    }

    /* Sections: quieter than the screens below them, so the row reads as a heading for the tabs. */
    .sections a {
      min-height: 2rem;
      padding-inline: var(--space-2);
      border-radius: var(--radius-md);
      color: var(--color-text-muted);
      font-size: var(--text-xs);
      font-weight: var(--weight-semibold);
      letter-spacing: 0.06em;
      text-transform: uppercase;
    }

    .sections a[aria-current='page'] {
      background: none;
      color: var(--color-text);
      box-shadow: inset 0 -2px 0 var(--color-primary);
      border-radius: var(--radius-sm);
    }

    /* Screens: the current one is filled and heavier, not just tinted (WCAG 1.4.1). */
    .screens a {
      border-color: var(--color-border);
      background: var(--color-surface-raised);
    }

    .screens a:hover {
      background: var(--color-surface);
    }

    .screens a[aria-current='page'] {
      border-color: var(--color-primary);
      background: var(--color-primary-subtle);
      color: var(--color-primary);
      font-weight: var(--weight-semibold);
    }

    .count {
      padding: 0 var(--space-2);
      border-radius: var(--radius-full);
      background: var(--color-warning-subtle);
      color: var(--color-warning-text);
      font-size: var(--text-xs);
      font-weight: var(--weight-semibold);
      letter-spacing: 0;
      line-height: 1.25rem;
    }

    /* A link does not get the 44px floor from _base.scss; a finger needs it. */
    @media (pointer: coarse) {
      a,
      .sections a {
        min-height: var(--touch-target-min);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminSubNav {
  /** The open category, or null on a page no category lists. Null draws nothing. */
  readonly category = input<AdminNavCategory | null>(null);

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly path = currentPath();

  /** The section holding the current page. */
  protected readonly current = computed(() => {
    const category = this.category();
    return category ? sectionFor(category.sections, this.path()) : null;
  });

  protected readonly showSections = computed(() => (this.category()?.sections.length ?? 0) > 1);

  /** A row of one tab is not navigation, so a category with a single screen shows none. */
  protected readonly showScreens = computed(() => (this.current()?.items.length ?? 0) > 1);

  constructor() {
    // After each navigation (and when the category changes) the active tab is brought into view
    // inside its own row, centred where it can be. Scrolling the row directly rather than calling
    // `scrollIntoView` keeps the page itself from jumping vertically.
    effect((onCleanup) => {
      this.path();
      this.category();
      const timer = setTimeout(() => {
        for (const row of this.host.nativeElement.querySelectorAll<HTMLElement>('ul')) {
          const tab = row.querySelector<HTMLElement>('[aria-current="page"]');
          if (!tab) continue;
          row.scrollLeft = tab.offsetLeft - (row.clientWidth - tab.offsetWidth) / 2;
        }
      });
      onCleanup(() => clearTimeout(timer));
    });
  }

  protected isActive(item: AdminNavItem): boolean {
    return itemMatches(item, this.path());
  }
}
