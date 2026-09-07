import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { Badge, Button, Icon } from '@klarahome/ui-primitives';

/** Who is signed in, as the bar needs to describe them. */
export interface AdminIdentityView {
  readonly name: string;
  /** The seller's name for a vendor user; null for platform staff. */
  readonly vendorName?: string | null;
  readonly roles: readonly string[];
}

/**
 * The bar across the top of the back office.
 *
 * It carries the three things that belong to the whole application rather than to a page: the way
 * into global search, the notifications centre, and who you are signed in as.
 *
 * **The scope is stated, not implied.** A vendor user sees their seller's name next to their own,
 * permanently, because the single most dangerous confusion in a marketplace back office is not
 * knowing whose data you are looking at. The same slot is where an impersonation banner belongs
 * when the API grows one.
 *
 * The user menu is a plain details/summary disclosure rather than a custom popup: it is a menu of
 * two links, and a hand-rolled one would need roving focus, Escape handling and outside-click
 * dismissal to be no better than what the platform already gives.
 */
@Component({
  selector: 'kh-admin-top-bar',
  imports: [Badge, Button, Icon, RouterLink],
  template: `
    <button
      khButton
      type="button"
      variant="tertiary"
      size="sm"
      [iconOnly]="true"
      class="menu"
      [attr.aria-label]="navOpen() ? 'Hide navigation' : 'Show navigation'"
      [attr.aria-expanded]="navOpen()"
      (click)="navToggled.emit()"
    >
      <kh-icon name="menu" />
    </button>

    <a class="brand" routerLink="/">{{ title() }}</a>

    <!-- A button rather than an input: the search itself is a dialog with its own keyboard model
         (see the app's global search), and two search boxes on one screen is one too many. -->
    <button khButton type="button" size="sm" class="search" (click)="searchOpened.emit()">
      <kh-icon name="search" size="sm" />
      <span class="search-label">Search orders, products, sellers…</span>
      <kbd>/</kbd>
    </button>

    <div class="spacer"></div>

    @if (showNotifications()) {
      <a
        khButton
        variant="tertiary"
        size="sm"
        routerLink="/notifications"
        class="bell"
        [attr.aria-label]="notificationsLabel()"
      >
        <kh-icon name="bell" size="sm" />
        @if (notificationCount(); as count) {
          <kh-badge tone="danger">{{ count > 99 ? '99+' : count }}</kh-badge>
        }
      </a>
    }

    <details class="account">
      <summary>
        <kh-icon name="user" size="sm" />
        <span class="who">
          <span class="name">{{ identity().name }}</span>
          @if (identity().vendorName; as vendor) {
            <span class="scope">{{ vendor }}</span>
          } @else {
            <span class="scope">Platform</span>
          }
        </span>
        <kh-icon name="chevron-down" size="sm" />
      </summary>

      <div class="menu-panel">
        <p class="roles">{{ roleLine() }}</p>
        <a routerLink="/profile" (click)="closeAccount()">Your profile and security</a>
        <button type="button" class="sign-out" (click)="signedOut.emit()">Sign out</button>
      </div>
    </details>
  `,
  styles: `
    :host {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      height: 100%;
      padding-inline: var(--space-3);
      border-block-end: 1px solid var(--color-border);
      background: var(--color-bg);
    }

    .brand {
      font-weight: var(--weight-bold);
      color: inherit;
      text-decoration: none;
      white-space: nowrap;
    }

    .search {
      flex: 0 1 24rem;
      justify-content: flex-start;
      min-width: 0;
      margin-inline-start: var(--space-3);
      color: var(--color-text-muted);
    }

    .search-label {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    kbd {
      margin-inline-start: auto;
      padding: 0 var(--space-1);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-sm);
      font-family: var(--font-mono);
      font-size: var(--text-xs);
    }

    .spacer {
      flex: 1;
    }

    .bell {
      position: relative;
    }

    .account {
      position: relative;
    }

    .account summary {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      min-height: var(--touch-target-min);
      padding-inline: var(--space-2);
      border-radius: var(--radius-md);
      cursor: pointer;
      list-style: none;
    }

    .account summary::-webkit-details-marker {
      display: none;
    }

    .who {
      display: flex;
      flex-direction: column;
      line-height: var(--leading-tight);
    }

    .name {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .scope {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .menu-panel {
      position: absolute;
      inset-inline-end: 0;
      inset-block-start: calc(100% + var(--space-1));
      z-index: var(--z-header);
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      min-width: 16rem;
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-md);
    }

    .roles {
      margin: 0;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .menu-panel a,
    .sign-out {
      display: block;
      padding: var(--space-2);
      border: 0;
      border-radius: var(--radius-sm);
      background: none;
      font: inherit;
      font-size: var(--text-sm);
      color: inherit;
      text-align: start;
      text-decoration: none;
      cursor: pointer;
    }

    .menu-panel a:hover,
    .sign-out:hover {
      background: var(--color-surface);
    }

    /* Below the sidebar's breakpoint the search label and the identity collapse to their icons;
       a tablet in portrait has room for the controls but not for the words. */
    @media (max-width: 60rem) {
      .search-label,
      kbd,
      .who {
        display: none;
      }

      .search {
        flex: 0 0 auto;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminTopBar {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly title = input('Klara Home');
  readonly identity = input.required<AdminIdentityView>();
  readonly navOpen = input(false);
  readonly showNotifications = input(false);
  /** Messages needing attention. Zero renders no badge at all rather than a "0". */
  readonly notificationCount = input<number | null>(null);

  readonly navToggled = output<void>();
  readonly searchOpened = output<void>();
  readonly signedOut = output<void>();

  protected readonly roleLine = computed(() => {
    const roles = this.identity().roles;
    return roles.length > 0 ? `Signed in as ${roles.join(', ')}` : 'No roles assigned';
  });

  protected readonly notificationsLabel = computed(() => {
    const count = this.notificationCount();
    return count ? `Notifications, ${count} needing attention` : 'Notifications';
  });

  /** Closes the account disclosure after a link inside it is followed. */
  protected closeAccount(): void {
    this.host.nativeElement.querySelector('details.account')?.removeAttribute('open');
  }
}
