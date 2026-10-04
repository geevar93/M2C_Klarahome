import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { Icon } from '@klarahome/ui-primitives';
import { filter } from 'rxjs';

import { AdminAttentionItem, AdminCreateAction, AdminNavGroup } from './admin.model';
import { AdminSidebar } from './admin-sidebar';
import { AdminIdentityView, AdminTopBar } from './admin-top-bar';
import { ImpersonationBanner, ImpersonationView } from './impersonation-banner';
import { AdminCrumb } from './page-header';

/**
 * The frame the whole back office sits in: top bar, sidebar, breadcrumbs, then the page.
 *
 * **One layout break, and it is a real one.** From 1024px (`lg`) the sidebar is a fixed column in
 * the page grid, always visible. Below it the same sidebar is a drawer: a menu button in the top
 * bar opens it over the page with a backdrop, and it closes on Esc, on a backdrop press, on a
 * link press and on any navigation. While closed it is `visibility: hidden`, so nothing in it can
 * be tabbed to. Focus moves into the drawer when it opens and back to the menu button when it
 * closes.
 *
 * The shell owns no data. The groups, the trail, the identity and the impersonation are inputs,
 * and every control emits; the app holds the state, so a sign-out or a route change can move it.
 * The one thing it does hold is whether the drawer is open, which is nobody else's business.
 *
 * The impersonation banner sits **above** the header rather than inside it, deliberately: the
 * header is sticky and the banner has to be, so putting it inside would make the one thing that
 * must never scroll away depend on the one thing that already does not.
 */
@Component({
  selector: 'kh-admin-shell',
  imports: [AdminSidebar, AdminTopBar, Icon, ImpersonationBanner, RouterLink],
  template: `
    <a class="kh-skip-link" href="#main-content">Skip to main content</a>

    @if (impersonation(); as acting) {
      <kh-impersonation-banner
        [view]="acting"
        [busy]="endingImpersonation()"
        (exited)="impersonationExited.emit()"
      />
    }

    <header>
      <kh-admin-top-bar
        [identity]="identity()"
        [showNotifications]="showNotifications()"
        [attention]="attention()"
        [messageLogPath]="messageLogPath()"
        [createActions]="createActions()"
        [scheme]="scheme()"
        [menuOpen]="menuOpen()"
        (menuToggled)="toggleMenu()"
        (createChosen)="createChosen.emit($event)"
        (searchOpened)="searchOpened.emit()"
        (schemeToggled)="schemeToggled.emit()"
        (signedOut)="signedOut.emit()"
      />
    </header>

    @if (menuOpen()) {
      <div class="backdrop" aria-hidden="true" (click)="closeMenu(false)"></div>
    }

    <div class="layout">
      <aside id="admin-sidebar" [class.open]="menuOpen()" (keydown.escape)="closeMenu(true)">
        <kh-admin-sidebar [groups]="groups()" (navigated)="closeMenu(false)" />
      </aside>

      <main id="main-content" tabindex="-1">
        <div class="content">
          @if (crumbs().length > 1) {
            <nav aria-label="Breadcrumb">
              <ol>
                @for (crumb of crumbs(); track $index; let last = $last) {
                  <li>
                    @if (crumb.path && !last) {
                      <a [routerLink]="crumb.path">{{ crumb.label }}</a>
                    } @else {
                      <span [attr.aria-current]="last ? 'page' : null">{{ crumb.label }}</span>
                    }
                    @if (!last) {
                      <kh-icon name="chevron-right" size="sm" />
                    }
                  </li>
                }
              </ol>
            </nav>
          }
          <ng-content />
        </div>
      </main>
    </div>
  `,
  styles: `
    :host {
      display: block;
      min-height: 100vh;
      background: var(--color-bg);
    }

    header {
      position: sticky;
      inset-block-start: 0;
      /* Above the drawer's backdrop, so the menu button that opened the drawer can close it. */
      z-index: calc(var(--z-drawer) + 1);
      height: var(--header-height);
    }

    .layout {
      display: block;
    }

    /* Below 1024px: the drawer, hung from the bottom edge of the header. */
    aside {
      position: fixed;
      inset-block: var(--header-height) 0;
      inset-inline-start: 0;
      z-index: var(--z-drawer);
      width: min(var(--sidebar-width), 85vw);
      border-inline-end: 1px solid var(--color-border);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-lg);
      transform: translateX(-100%);
      visibility: hidden;
      transition:
        transform var(--duration-base) var(--ease-standard),
        visibility 0s linear var(--duration-base);
    }

    aside.open {
      transform: none;
      visibility: visible;
      transition-delay: 0s;
    }

    .backdrop {
      position: fixed;
      inset: 0;
      z-index: calc(var(--z-drawer) - 1);
      background: var(--color-overlay);
    }

    main {
      min-width: 0;
    }

    .content {
      max-width: 87.5rem;
      margin-inline: auto;
      padding: var(--space-4) var(--space-4) var(--space-12);
    }

    nav ol {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
      align-items: center;
      margin: 0 0 var(--space-4);
      padding: 0;
      list-style: none;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    nav li {
      display: flex;
      gap: var(--space-1);
      align-items: center;
    }

    nav a {
      color: inherit;
      text-decoration: none;
    }

    nav a:hover {
      color: var(--color-text);
      text-decoration: underline;
    }

    nav [aria-current='page'] {
      color: var(--color-text);
      font-weight: var(--weight-medium);
    }

    @media (min-width: 1024px) {
      .layout {
        display: grid;
        grid-template-columns: var(--sidebar-width) minmax(0, 1fr);
        align-items: start;
      }

      aside {
        position: sticky;
        inset-block: var(--header-height) auto;
        z-index: auto;
        width: auto;
        height: calc(100vh - var(--header-height));
        box-shadow: none;
        transform: none;
        visibility: visible;
        transition: none;
      }

      .backdrop {
        display: none;
      }

      .content {
        padding: var(--space-6) var(--space-8) var(--space-12);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminShell {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly groups = input.required<readonly AdminNavGroup[]>();
  readonly crumbs = input<readonly AdminCrumb[]>([]);
  readonly identity = input.required<AdminIdentityView>();
  readonly showNotifications = input(false);
  /** The work queues with something in them, for the notifications panel. */
  readonly attention = input<readonly AdminAttentionItem[]>([]);
  readonly messageLogPath = input<string | null>(null);
  /** What "Create" offers; empty hides it. The app decides, from what the session may create. */
  readonly createActions = input<readonly AdminCreateAction[]>([]);
  readonly scheme = input<'light' | 'dark'>('light');

  /** The support impersonation in progress, or null. Non-null shows the banner. */
  readonly impersonation = input<ImpersonationView | null>(null);

  /** Whether the stop is in flight, so the banner's button can say so. */
  readonly endingImpersonation = input(false);

  readonly createChosen = output<string>();
  readonly searchOpened = output<void>();
  readonly schemeToggled = output<void>();
  readonly signedOut = output<void>();

  /** The banner's Stop was pressed. The app ends the session and clears the input. */
  readonly impersonationExited = output<void>();

  protected readonly menuOpen = signal(false);

  constructor() {
    // Any navigation closes the drawer: it is a way to get somewhere, and the page has changed.
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.menuOpen.set(false));

    // Focus follows the drawer: into its first link when it opens.
    effect(() => {
      if (!this.menuOpen()) return;
      setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>('aside a')?.focus());
    });
  }

  protected toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  /** Closes the drawer; `restoreFocus` returns focus to the menu button (Esc), not on a link press. */
  protected closeMenu(restoreFocus: boolean): void {
    if (!this.menuOpen()) return;
    this.menuOpen.set(false);
    if (restoreFocus) this.host.nativeElement.querySelector<HTMLElement>('.menu-btn')?.focus();
  }
}
