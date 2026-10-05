import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ICON_NAMES, Icon, IconName } from '@klarahome/ui-primitives';

import { AdminNavCategory } from './admin.model';
import { categoryFor, currentPath } from './nav-location';

/**
 * The sidebar: one link per category, and nothing else to scan.
 *
 * It lists the categories and no screens. Thirty-odd links in a column read as a directory; the
 * screens of the open category are the sub-nav's job (`kh-admin-sub-nav`, drawn above the page by
 * the shell), so this stays at most six rows however much the back office grows. A category's link
 * opens on the first screen in it the session can reach, and it wears the sum of that category's
 * queue counts so "something is waiting in Sell" is visible from anywhere.
 *
 * It filters nothing. The categories it is given are the categories it draws: the app has already
 * dropped every destination this session may not open, from the same declaration the route guards
 * are built from (`navigation.ts`).
 *
 * The category holding the current page is marked with `aria-current`, a heavier weight and a
 * leading rule as well as a tint (WCAG 1.4.1: never colour alone). It is "holding", not "equal
 * to": a detail route such as `/orders/123` keeps Sell marked. `navigated` fires on any link press
 * so the shell can close the drawer it sits in on a phone; the "All screens" link below is the way
 * to the whole list on a page of its own.
 */
@Component({
  selector: 'kh-admin-sidebar',
  imports: [Icon, RouterLink],
  template: `
    <nav aria-label="Main navigation">
      <ul>
        @for (category of categories(); track category.key) {
          <li>
            <a
              [routerLink]="category.path"
              [class.active]="active()?.key === category.key"
              [attr.aria-current]="active()?.key === category.key ? 'page' : null"
              (click)="navigated.emit()"
            >
              <kh-icon [name]="iconFor(category.icon)" size="sm" />
              <span class="label">{{ category.label }}</span>
              @if (category.badge) {
                <span class="count">{{ category.badge }}</span>
              }
            </a>
          </li>
        }
      </ul>
    </nav>

    <a class="all" routerLink="/more" (click)="navigated.emit()">
      <kh-icon name="grid" size="sm" />
      <span>All screens</span>
    </a>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
      min-height: 0;
    }

    /* The list takes what the footer leaves and scrolls inside it (a short phone in landscape), so
       nothing can sit underneath the pinned "All screens" link: min-height 0 lets a flex child
       shrink below its content. */
    nav {
      flex: 1 1 0;
      min-height: 0;
      padding: var(--space-3) var(--space-3) var(--space-4);
      overflow-y: auto;
      scrollbar-width: thin;
    }

    ul {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    a {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      width: 100%;
      /* 2.75rem is the 44px touch floor, which a link does not get from _base.scss. */
      min-height: 2.75rem;
      padding: var(--space-2) var(--space-3);
      border: 0;
      border-radius: var(--radius-md);
      background: none;
      color: var(--color-text-subtle);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
      text-align: start;
      text-decoration: none;
      cursor: pointer;
    }

    a:hover {
      background: var(--color-surface);
      color: var(--color-text);
    }

    a:focus-visible {
      outline: 2px solid var(--color-focus-ring);
      outline-offset: -2px;
    }

    a.active {
      background: var(--color-primary-subtle);
      color: var(--color-primary);
      font-weight: var(--weight-semibold);
      box-shadow: inset 3px 0 0 var(--color-primary);
    }

    .label {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .count {
      padding: 0 var(--space-2);
      border-radius: var(--radius-full);
      background: var(--color-warning-subtle);
      color: var(--color-warning-text);
      font-size: var(--text-xs);
      font-weight: var(--weight-semibold);
      line-height: 1.25rem;
    }

    .all {
      flex: none;
      width: auto;
      margin: 0 var(--space-3) var(--space-3);
      background: var(--color-surface-raised);
      border-block-start: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminSidebar {
  readonly categories = input.required<readonly AdminNavCategory[]>();
  readonly navigated = output<void>();

  private readonly path = currentPath();

  /** The category the current page is in, or null on a page no category lists (`/more`). */
  protected readonly active = computed(() => categoryFor(this.categories(), this.path()));

  protected iconFor(name: string | undefined): IconName {
    return name && (ICON_NAMES as readonly string[]).includes(name) ? (name as IconName) : 'grid';
  }
}
