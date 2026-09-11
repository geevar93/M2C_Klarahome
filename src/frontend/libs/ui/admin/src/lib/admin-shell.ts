import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Drawer } from '@klarahome/ui-primitives';

import { AdminNavSection } from './admin.model';
import { AdminIdentityView, AdminTopBar } from './admin-top-bar';
import { AdminSidebar } from './admin-sidebar';
import { ImpersonationBanner, ImpersonationView } from './impersonation-banner';

/**
 * The frame the whole back office sits in.
 *
 * **Desktop-first, and genuinely usable on a tablet** (`docs/05-frontend-architecture.md` §4.1) —
 * which is not the storefront's rule inverted, but a different rule. Vendors do dispatch on
 * tablets in a warehouse, standing up, so the layout has one break and it is a real one:
 *
 *  - **Above 1024px (`lg`, `libs/ui/primitives/src/styles/_breakpoints.scss`)** the sidebar is a
 *    column of the page grid. Collapsing it narrows the column to a rail of icons; the content
 *    reflows into the space, and the choice is remembered by the app.
 *  - **Below 1024px** the sidebar is not in the grid at all — it becomes a `kh-drawer`, with
 *    the focus trapping, Escape handling and scroll locking that a panel over content needs and a
 *    column does not. It closes on navigation, because a drawer still covering the page you have
 *    just navigated to is the most irritating thing a responsive shell does.
 *
 * The same `AdminSidebar` renders in both, from the same sections. Two sidebars would be two
 * navigations, and one of them would be the one nobody updated.
 *
 * The shell owns no state: `navOpen`, `collapsed`, the identity and the impersonation are inputs,
 * and every control emits. The app holds them, so a sign-out or a route change can move them.
 *
 * The impersonation banner sits **above** the header rather than inside it, and that is deliberate:
 * the header is sticky and the banner has to be, so putting it inside would make the one thing that
 * must never scroll away depend on the one thing that already does not
 * (docs/07-security-compliance.md §2, Step 28B deliverable 1).
 */
@Component({
  selector: 'kh-admin-shell',
  imports: [AdminSidebar, AdminTopBar, Drawer, ImpersonationBanner],
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
        [navOpen]="navOpen()"
        [showNotifications]="showNotifications()"
        [notificationCount]="notificationCount()"
        (navToggled)="navToggled.emit()"
        (searchOpened)="searchOpened.emit()"
        (signedOut)="signedOut.emit()"
      />
    </header>

    <div class="layout" [class.collapsed]="collapsed()">
      <!-- The column. Hidden below the breakpoint, where the drawer below takes over. -->
      <aside class="rail">
        <kh-admin-sidebar [sections]="sections()" [collapsed]="collapsed()" />
      </aside>

      <main id="main-content" tabindex="-1">
        <ng-content />
      </main>
    </div>

    <kh-drawer
      class="sheet"
      [open]="navOpen()"
      side="start"
      label="Back office navigation"
      (closed)="navClosed.emit()"
    >
      <kh-admin-sidebar [sections]="sections()" (navigated)="navClosed.emit()" />
    </kh-drawer>
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
      z-index: var(--z-header);
      height: var(--header-height);
    }

    .layout {
      display: grid;
      grid-template-columns: 1fr;
    }

    main {
      min-width: 0;
      padding: var(--space-6) var(--space-4);
    }

    /* The drawer is the tablet form. Above the breakpoint it is not rendered at all — the
       component removes itself from the DOM when closed, and the app never opens it there. */
    .sheet {
      display: block;
    }

    .rail {
      display: none;
    }

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: 16rem 1fr;
        align-items: start;
      }

      .layout.collapsed {
        grid-template-columns: 4rem 1fr;
      }

      .rail {
        display: block;
        position: sticky;
        /* Below the sticky header, so the two never overlap. */
        inset-block-start: var(--header-height);
        height: calc(100vh - var(--header-height));
        border-inline-end: 1px solid var(--color-border);
      }

      main {
        padding: var(--space-6);
      }

      .sheet {
        display: none;
      }
    }

    @media (min-width: 1536px) {
      main {
        /* A line of text 200 characters wide is unreadable; a data table is not text. The cap is
           generous and applies to the column, not to a table inside it, which scrolls. */
        max-width: 110rem;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminShell {
  readonly sections = input.required<readonly AdminNavSection[]>();
  readonly identity = input.required<AdminIdentityView>();
  /** Whether the tablet drawer is showing. Meaningless above the breakpoint. */
  readonly navOpen = input(false);
  /** Whether the desktop column is a rail of icons. */
  readonly collapsed = input(false);
  readonly showNotifications = input(false);
  readonly notificationCount = input<number | null>(null);

  /** The support impersonation in progress, or null. Non-null shows the banner. */
  readonly impersonation = input<ImpersonationView | null>(null);

  /** Whether the stop is in flight, so the banner's button can say so. */
  readonly endingImpersonation = input(false);

  /** The menu button was pressed: open the drawer on a tablet, collapse the rail on a desktop. */
  readonly navToggled = output<void>();
  readonly navClosed = output<void>();
  readonly searchOpened = output<void>();
  readonly signedOut = output<void>();

  /** The banner's Stop was pressed. The app ends the session and clears the input. */
  readonly impersonationExited = output<void>();
}
