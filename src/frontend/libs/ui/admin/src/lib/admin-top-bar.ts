import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { ICON_NAMES, Icon, IconName } from '@klarahome/ui-primitives';

import { AdminAttentionItem, AdminCreateAction } from './admin.model';
import { AdminNotifications } from './admin-notifications';

/** Who is signed in, as the bar needs to describe them. */
export interface AdminIdentityView {
  readonly name: string;
  /** The seller's name for a vendor user; null for platform staff. */
  readonly vendorName?: string | null;
  readonly roles: readonly string[];
}

type Popover = 'create' | 'account';

/**
 * The bar across the top of the back office.
 *
 * Left to right: the menu button (below 1024px, where the sidebar is a drawer), the brand, the
 * search trigger, then "Create", the colour-scheme toggle, the notifications panel and the
 * account menu. It carries what belongs to the whole application rather than to a page.
 *
 * **The scope is stated, not implied.** A seller sees their own name next to the avatar, and the
 * account menu says "Seller" or "Platform" again, because the most dangerous confusion in a
 * marketplace back office is not knowing whose data you are looking at.
 *
 * The two menus here are real `role="menu"` popovers with arrow-key movement, Esc to close (focus
 * returns to the button that opened them) and outside-click dismissal; only one is open at a time.
 * Search is a button rather than an input because the search itself is a dialog with its own
 * keyboard model (the command palette), and two search boxes on one screen is one too many.
 */
@Component({
  selector: 'kh-admin-top-bar',
  imports: [AdminNotifications, Icon, RouterLink],
  template: `
    <button
      type="button"
      class="icon-btn menu-btn"
      aria-label="Open navigation"
      aria-controls="admin-sidebar"
      [attr.aria-expanded]="menuOpen()"
      (click)="menuToggled.emit()"
    >
      <kh-icon name="menu" />
    </button>

    <a class="brand" routerLink="/dashboard" aria-label="Klara Home, dashboard">
      <span class="mark" aria-hidden="true"><kh-icon name="home" size="sm" /></span>
      <span class="wordmark">{{ title() }}</span>
    </a>

    <button type="button" class="search" aria-keyshortcuts="Control+K Meta+K" (click)="searchOpened.emit()">
      <kh-icon name="search" size="sm" />
      <span class="search-label">Search orders, products, sellers…</span>
      <kbd aria-hidden="true">Ctrl K</kbd>
    </button>

    <div class="spacer"></div>

    <button type="button" class="icon-btn search-icon" aria-label="Search" (click)="searchOpened.emit()">
      <kh-icon name="search" />
    </button>

    @if (createActions().length > 0) {
      <div class="pop">
        <button
          type="button"
          class="create"
          aria-haspopup="menu"
          [attr.aria-expanded]="open() === 'create'"
          (click)="toggle('create', $event)"
        >
          <kh-icon name="plus" size="sm" />
          <span class="create-label">Create</span>
          <kh-icon name="chevron-down" size="sm" class="create-chevron" />
        </button>

        @if (open() === 'create') {
          <div class="menu" role="menu" aria-label="Create" (keydown)="onMenuKeydown($event)">
            @for (action of createActions(); track action.key) {
              <button type="button" role="menuitem" (click)="choose(action)">
                <kh-icon [name]="iconFor(action.icon)" size="sm" />
                <span class="item-text">
                  <span>{{ action.label }}</span>
                  @if (action.hint) {
                    <span class="item-hint">{{ action.hint }}</span>
                  }
                </span>
              </button>
            }
          </div>
        }
      </div>
    }

    <button
      type="button"
      class="icon-btn scheme-btn"
      [attr.aria-label]="scheme() === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'"
      (click)="schemeToggled.emit()"
    >
      <kh-icon [name]="scheme() === 'dark' ? 'sun' : 'moon'" />
    </button>

    @if (showNotifications()) {
      <kh-admin-notifications [items]="attention()" [messageLogPath]="messageLogPath()" />
    }

    <div class="pop">
      <button
        type="button"
        class="account"
        aria-haspopup="menu"
        [attr.aria-expanded]="open() === 'account'"
        [attr.aria-label]="'Account menu, ' + identity().name"
        (click)="toggle('account', $event)"
      >
        <span class="avatar" aria-hidden="true">{{ initials() }}</span>
        <span class="who">
          <span class="name">{{ identity().name }}</span>
          <span class="scope">{{ identity().vendorName ?? 'Platform' }}</span>
        </span>
      </button>

      @if (open() === 'account') {
        <div class="menu account-menu" role="menu" aria-label="Account" (keydown)="onMenuKeydown($event)">
          <div class="who-card">
            <span class="name">{{ identity().name }}</span>
            <span class="scope">{{ roleLine() }}</span>
          </div>
          <a role="menuitem" routerLink="/profile" (click)="close()">
            <kh-icon name="user" size="sm" /><span>Your profile and security</span>
          </a>
          <a role="menuitem" routerLink="/more" (click)="close()">
            <kh-icon name="grid" size="sm" /><span>All screens</span>
          </a>
          <!-- The two controls the phone bar gives up (seven was too many): home and the colour scheme. -->
          <a role="menuitem" class="phone-only" routerLink="/dashboard" (click)="close()">
            <kh-icon name="home" size="sm" /><span>Dashboard</span>
          </a>
          <button type="button" role="menuitem" class="phone-only" (click)="close(); schemeToggled.emit()">
            <kh-icon [name]="scheme() === 'dark' ? 'sun' : 'moon'" size="sm" />
            <span>{{ scheme() === 'dark' ? 'Switch to light mode' : 'Switch to dark mode' }}</span>
          </button>
          <button type="button" role="menuitem" class="sign-out" (click)="close(); signedOut.emit()">
            <span>Sign out</span>
          </button>
        </div>
      }
    </div>
  `,
  styles: `
    :host {
      display: flex;
      gap: var(--space-1);
      align-items: center;
      height: 100%;
      padding-inline: var(--space-3);
      border-block-end: 1px solid var(--header-border, var(--color-border));
      background: var(--header-bg, var(--color-surface-raised));
      color: var(--header-text, var(--color-text));
    }

    .icon-btn,
    .account,
    .create,
    .search {
      border: 0;
      background: none;
      color: inherit;
      cursor: pointer;
    }

    .icon-btn {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2.5rem;
      height: 2.5rem;
      border-radius: var(--radius-md);
      color: var(--header-text-muted, var(--color-text-muted));
    }

    .icon-btn:hover,
    .icon-btn[aria-expanded='true'] {
      background: color-mix(in srgb, var(--header-text, var(--color-text)) 8%, transparent);
      color: var(--header-text, var(--color-text));
    }

    /* The home pill and the colour-scheme toggle live in the account menu on a phone. */
    .brand {
      display: none;
      gap: var(--space-2);
      align-items: center;
      color: var(--header-text, var(--color-text));
      font-weight: var(--weight-bold);
      text-decoration: none;
      white-space: nowrap;
    }

    @media (pointer: coarse) {
      .brand {
        min-block-size: var(--touch-target-min);
        min-inline-size: var(--touch-target-min);
      }
    }

    .scheme-btn {
      display: none;
    }

    .mark {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2rem;
      height: 2rem;
      border-radius: var(--radius-md);
      background: var(--header-button-bg, var(--color-primary));
      color: var(--header-button-text, var(--color-on-primary));
    }

    .wordmark {
      display: none;
      font-size: var(--text-lg);
      letter-spacing: var(--tracking-display);
    }

    .search {
      display: none;
      align-items: center;
      gap: var(--space-2);
      flex: 1 1 auto;
      max-width: 28rem;
      height: 2.5rem;
      margin-inline-start: var(--space-2);
      padding-inline: var(--space-3);
      border: 1px solid var(--header-control-border, var(--color-border-strong));
      border-radius: var(--radius-md);
      background: var(--header-control-bg, var(--color-surface));
      color: var(--header-control-text, var(--header-text-muted, var(--color-text-muted)));
      font-size: var(--text-sm);
      text-align: start;
    }

    .search:hover {
      border-color: var(--header-control-text, var(--header-text-muted, var(--color-text-muted)));
    }

    .search-label {
      flex: 1;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    kbd {
      padding: 0 var(--space-1);
      border: 1px solid var(--header-control-border, var(--color-border-strong));
      border-radius: var(--radius-sm);
      background: var(--header-control-bg, var(--color-surface-raised));
      font-family: var(--font-sans);
      font-size: var(--text-xs);
      font-weight: var(--weight-medium);
    }

    .spacer {
      flex: 1;
    }

    .pop {
      position: relative;
    }

    .create {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      height: 2.5rem;
      padding-inline: var(--space-3);
      border-radius: var(--radius-md);
      background: var(--header-button-bg, var(--color-primary));
      color: var(--header-button-text, var(--color-on-primary));
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .create:hover {
      background: var(--header-button-hover, var(--color-primary-hover));
    }

    /* Controls sitting directly on the header band take the header's own ring; the popover
       panels float on cream and keep the page ring. */
    .brand:focus-visible,
    .icon-btn:focus-visible,
    .search:focus-visible,
    .create:focus-visible,
    .account:focus-visible {
      outline-color: var(--header-focus-ring, var(--color-focus-ring));
    }

    .create-label,
    .create-chevron {
      display: none;
    }

    .account {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      height: 2.5rem;
      padding: 0 var(--space-1);
      border-radius: var(--radius-full);
    }

    .avatar {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2rem;
      height: 2rem;
      border-radius: var(--radius-full);
      background: var(--header-button-bg, var(--color-primary));
      color: var(--header-button-text, var(--color-on-primary));
      font-size: var(--text-xs);
      font-weight: var(--weight-semibold);
    }

    .who {
      display: none;
      flex-direction: column;
      padding-inline-end: var(--space-2);
      line-height: var(--leading-tight);
      text-align: start;
    }

    .name {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .scope {
      font-size: var(--text-xs);
      color: var(--header-text-muted, var(--color-text-muted));
    }

    .menu {
      position: absolute;
      inset-block-start: calc(100% + var(--space-2));
      inset-inline-end: 0;
      z-index: var(--z-header);
      display: flex;
      flex-direction: column;
      min-width: 14rem;
      padding: var(--space-1);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-lg);
    }

    .menu button,
    .menu a {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      padding: var(--space-2) var(--space-3);
      border: 0;
      border-radius: var(--radius-md);
      background: none;
      color: var(--color-text);
      font-size: var(--text-sm);
      text-align: start;
      text-decoration: none;
      cursor: pointer;
    }

    .menu button:hover,
    .menu a:hover,
    .menu [role='menuitem']:focus-visible {
      background: var(--color-surface);
    }

    .item-text {
      display: flex;
      flex-direction: column;
    }

    .item-hint {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .who-card {
      display: flex;
      flex-direction: column;
      padding: var(--space-2) var(--space-3) var(--space-3);
      margin-block-end: var(--space-1);
      border-block-end: 1px solid var(--color-border);
    }

    .sign-out {
      margin-block-start: var(--space-1);
      border-block-start: 1px solid var(--color-border);
      border-radius: 0 0 var(--radius-md) var(--radius-md);
    }

    @media (min-width: 480px) {
      .wordmark,
      .create-label,
      .create-chevron {
        display: inline-flex;
      }

      .create {
        padding-inline: var(--space-3) var(--space-2);
      }
    }

    @media (min-width: 768px) {
      :host {
        gap: var(--space-2);
      }

      .brand {
        display: flex;
      }

      .scheme-btn {
        display: inline-flex;
      }

      .menu .phone-only {
        display: none;
      }

      .search {
        display: flex;
      }

      .search-icon {
        display: none;
      }
    }

    @media (min-width: 1024px) {
      :host {
        padding-inline: var(--space-6);
      }

      .menu-btn {
        display: none;
      }

      .brand {
        width: calc(var(--sidebar-width) - var(--space-6) - var(--space-2));
      }

      .who {
        display: flex;
      }
    }
  `,
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'onEscape()',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminTopBar {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly title = input('Klara Home');
  readonly identity = input.required<AdminIdentityView>();
  readonly showNotifications = input(false);
  /** The queues with something waiting, for the notifications panel. */
  readonly attention = input<readonly AdminAttentionItem[]>([]);
  readonly messageLogPath = input<string | null>(null);
  /** What "Create" offers. Empty hides the control: this session may not make anything. */
  readonly createActions = input<readonly AdminCreateAction[]>([]);
  readonly scheme = input<'light' | 'dark'>('light');
  /** Whether the drawer is open, for the menu button's `aria-expanded`. */
  readonly menuOpen = input(false);

  readonly menuToggled = output<void>();
  readonly searchOpened = output<void>();
  readonly createChosen = output<string>();
  readonly schemeToggled = output<void>();
  readonly signedOut = output<void>();

  protected readonly open = signal<Popover | null>(null);

  protected readonly initials = computed(
    () =>
      this.identity()
        .name.split(/\s+/)
        .filter(Boolean)
        .slice(0, 2)
        .map((word) => word[0]?.toUpperCase() ?? '')
        .join('') || '?',
  );

  protected readonly roleLine = computed(() => {
    const roles = this.identity().roles;
    return roles.length > 0 ? roles.join(', ') : 'No roles assigned';
  });

  protected toggle(which: Popover, event: Event): void {
    const opening = this.open() !== which;
    this.open.set(opening ? which : null);
    if (opening) {
      // Focus lands on the first item once the menu has rendered, so a keyboard user is in it.
      const trigger = event.currentTarget as HTMLElement;
      queueMicrotask(() =>
        setTimeout(() => trigger.parentElement?.querySelector<HTMLElement>('[role="menuitem"]')?.focus()),
      );
    }
  }

  protected choose(action: AdminCreateAction): void {
    this.close();
    this.createChosen.emit(action.key);
  }

  protected close(): void {
    this.open.set(null);
  }

  protected iconFor(name: string | undefined): IconName {
    return name && (ICON_NAMES as readonly string[]).includes(name) ? (name as IconName) : 'plus';
  }

  protected onMenuKeydown(event: KeyboardEvent): void {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp' && event.key !== 'Home' && event.key !== 'End')
      return;
    const items = Array.from(
      (event.currentTarget as HTMLElement).querySelectorAll<HTMLElement>('[role="menuitem"]'),
    );
    if (items.length === 0) return;
    event.preventDefault();
    const current = items.indexOf(document.activeElement as HTMLElement);
    const next =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? items.length - 1
          : (current + (event.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
    items[next].focus();
  }

  protected onDocumentClick(event: Event): void {
    if (this.open() && !(event.target as Element).closest('.pop')) this.close();
  }

  protected onEscape(): void {
    const which = this.open();
    if (!which) return;
    this.close();
    const trigger = this.host.nativeElement.querySelector<HTMLElement>(
      which === 'create' ? '.create' : '.account',
    );
    trigger?.focus();
  }
}
