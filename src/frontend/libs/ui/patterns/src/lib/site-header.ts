import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Badge, Button, Icon } from '@klarahome/ui-primitives';

import { NavItem, isInternalHref } from './navigation.model';

/**
 * The site header.
 *
 * Mobile first, and the order of the markup is the order of the tab stops: menu, wordmark,
 * search, account, cart. On a phone the search box moves below the row rather than being
 * collapsed behind an icon — search is how a storefront is actually used, and hiding it behind a
 * tap costs more than the 44px of height it saves.
 *
 * Every piece of data arrives as an input. The header does not know what a menu is fetched from,
 * which is what lets the shell decide, and what lets this be rendered in a test with three lines
 * of setup.
 */
@Component({
  selector: 'kh-site-header',
  imports: [Badge, Button, Icon, RouterLink, RouterLinkActive],
  template: `
    <header class="bar">
      <button
        khButton
        variant="tertiary"
        [iconOnly]="true"
        class="menu-button"
        type="button"
        aria-label="Open menu"
        aria-haspopup="dialog"
        [attr.aria-expanded]="menuOpen()"
        (click)="menuToggled.emit()"
      >
        <kh-icon name="menu" />
      </button>

      <!-- The logo slot is a text wordmark read from store settings; no brand assets before
           Step 30 (docs/10-design-system-placeholder.md §1.3). -->
      <a class="wordmark" routerLink="/">{{ storeName() }}</a>

      <nav class="primary" [attr.aria-label]="'Primary'">
        @for (item of menu(); track item.label) {
          @if (isInternal(item.href)) {
            <a
              class="nav-link"
              [routerLink]="item.href"
              routerLinkActive="is-active"
              [routerLinkActiveOptions]="{ exact: false }"
              >{{ item.label }}</a
            >
          } @else if (item.href) {
            <a
              class="nav-link"
              [href]="item.href"
              [attr.target]="item.opensInNewTab ? '_blank' : null"
              rel="noopener"
              >{{ item.label }}</a
            >
          }
        }
      </nav>

      <div class="actions">
        <a
          khButton
          variant="tertiary"
          [iconOnly]="true"
          class="account"
          [routerLink]="accountLink()"
          [attr.aria-label]="isAuthenticated() ? 'Your account' : 'Sign in'"
        >
          <kh-icon name="user" />
        </a>

        <button
          khButton
          variant="tertiary"
          [iconOnly]="true"
          type="button"
          class="cart"
          aria-haspopup="dialog"
          [attr.aria-expanded]="cartOpen()"
          [attr.aria-label]="cartLabel()"
          (click)="cartOpened.emit()"
        >
          <kh-icon name="cart" />
          @if (cartCount() > 0) {
            <!-- 'aria-hidden': the count is already in the button's accessible name, and a badge
                 read separately turns "Cart, 3 items" into "Cart, 3 items, 3". -->
            <kh-badge tone="primary" class="count" aria-hidden="true">{{ cartCount() }}</kh-badge>
          }
        </button>
      </div>

      <div class="search">
        <ng-content select="[khHeaderSearch]" />
      </div>
    </header>
  `,
  styles: `
    :host {
      position: sticky;
      inset-block-start: 0;
      z-index: var(--z-header);
      display: block;
      background: var(--color-bg);
      border-block-end: 1px solid var(--color-border);
    }

    .bar {
      display: grid;
      grid-template-columns: auto 1fr auto;
      grid-template-areas:
        'menu wordmark actions'
        'search search search';
      align-items: center;
      gap: var(--space-2);
      width: 100%;
      max-width: var(--container-max);
      margin-inline: auto;
      padding: var(--space-2) var(--space-4);
    }

    .menu-button {
      grid-area: menu;
    }

    .wordmark {
      grid-area: wordmark;
      font-size: var(--text-xl);
      font-weight: var(--weight-bold);
      color: var(--color-text);
      text-decoration: none;
    }

    .actions {
      grid-area: actions;
      display: flex;
      align-items: center;
      gap: var(--space-1);
    }

    .search {
      grid-area: search;
    }

    .primary {
      display: none;
    }

    .cart {
      position: relative;
    }

    .count {
      position: absolute;
      inset-block-start: var(--space-1);
      inset-inline-end: 0;
    }

    .nav-link {
      padding: var(--space-2);
      color: var(--color-text);
      text-decoration: none;
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
      white-space: nowrap;
    }

    .nav-link.is-active {
      /* Not colour alone: an underline survives a monochrome screen and colour blindness. */
      text-decoration: underline;
      text-underline-offset: var(--space-1);
    }

    /* From the 'lg' breakpoint the layout is one row: the burger goes away, the menu is inline, and search keeps
       the middle. 1024px is the 'lg' breakpoint from _breakpoints.scss. */
    @media (min-width: 1024px) {
      .bar {
        grid-template-columns: auto auto 1fr auto;
        grid-template-areas: 'wordmark primary search actions';
        gap: var(--space-4);
        padding-block: var(--space-3);
      }

      .menu-button {
        display: none;
      }

      .primary {
        grid-area: primary;
        display: flex;
        align-items: center;
        gap: var(--space-1);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SiteHeader {
  readonly storeName = input('Klara Home');
  readonly menu = input<readonly NavItem[]>([]);
  readonly cartCount = input(0);
  readonly isAuthenticated = input(false);
  /** Reflected into `aria-expanded` so the burger says what it did. */
  readonly menuOpen = input(false);
  readonly cartOpen = input(false);

  readonly menuToggled = output<void>();
  readonly cartOpened = output<void>();

  protected readonly isInternal = isInternalHref;

  protected accountLink(): string {
    return this.isAuthenticated() ? '/account' : '/auth/login';
  }

  protected cartLabel(): string {
    const count = this.cartCount();
    if (count === 0) return 'Cart, empty';
    return count === 1 ? 'Cart, 1 item' : `Cart, ${count} items`;
  }
}
