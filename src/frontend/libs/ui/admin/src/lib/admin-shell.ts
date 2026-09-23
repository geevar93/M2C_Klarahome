import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { AdminNavHub } from './admin.model';
import { AdminHubBar } from './admin-hub-bar';
import { AdminSubNav } from './admin-sub-nav';
import { AdminIdentityView, AdminTopBar } from './admin-top-bar';
import { ImpersonationBanner, ImpersonationView } from './impersonation-banner';

/**
 * The frame the whole back office sits in.
 *
 * **Five doors, and a row of tabs behind the open one.** The navigation used to be a sidebar of
 * eight sections and thirty-odd items, drawn as a column on a desktop and a drawer on a tablet.
 * Both were a directory. The shell now has one primary navigation — the five hubs — and one
 * secondary — the groups behind the current hub — and the same two components draw them at
 * every width. The layout has one break and it is a real one:
 *
 *  - **Below 1024px** the hub bar is fixed along the bottom edge, where a thumb reaches it, and
 *    the sub-nav is a row that scrolls sideways under the page title. There is no drawer and no
 *    menu button: nothing is hidden behind a control, so nothing needs opening.
 *  - **From 1024px (`lg`, `libs/ui/primitives/src/styles/_breakpoints.scss`)** the hub bar is a
 *    narrow rail in the page grid and the sub-nav wraps instead of scrolling. There is nothing to
 *    collapse, because the rail is already the collapsed form.
 *
 * The shell owns no state: the hubs, the identity and the impersonation are inputs, and every
 * control emits. The app holds them, so a sign-out or a route change can move them.
 *
 * The impersonation banner sits **above** the header rather than inside it, and that is deliberate:
 * the header is sticky and the banner has to be, so putting it inside would make the one thing that
 * must never scroll away depend on the one thing that already does not
 * (docs/07-security-compliance.md §2, Step 28B deliverable 1).
 */
@Component({
  selector: 'kh-admin-shell',
  imports: [AdminHubBar, AdminSubNav, AdminTopBar, ImpersonationBanner],
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
        [notificationCount]="notificationCount()"
        [showCreate]="showCreate()"
        (createOpened)="createOpened.emit()"
        (searchOpened)="searchOpened.emit()"
        (signedOut)="signedOut.emit()"
      />
    </header>

    <div class="layout">
      <kh-admin-hub-bar class="doors" [hubs]="hubs()" />

      <main id="main-content" tabindex="-1">
        <kh-admin-sub-nav class="tabs" [hubs]="hubs()" />
        <ng-content />
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
      z-index: var(--z-header);
      height: var(--header-height);
    }

    .layout {
      display: grid;
      grid-template-columns: 1fr;
    }

    main {
      min-width: 0;
      padding: var(--space-4) var(--space-4) var(--space-6);
      /* Room for the bar along the bottom, plus the phone's own home indicator beneath it. */
      padding-block-end: calc(var(--bottom-bar-height) + env(safe-area-inset-bottom, 0px) + var(--space-6));
    }

    .tabs {
      margin-block-end: var(--space-4);
    }

    /* The bar. Fixed rather than sticky, because the page scrolls under it and it must not. */
    .doors {
      position: fixed;
      inset-inline: 0;
      inset-block-end: 0;
      z-index: var(--z-header);
      height: calc(var(--bottom-bar-height) + env(safe-area-inset-bottom, 0px));
      padding-block-end: env(safe-area-inset-bottom, 0px);
      border-block-start: 1px solid var(--color-border);
    }

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: 5.5rem 1fr;
        align-items: start;
      }

      .doors {
        position: sticky;
        inset: auto;
        /* Below the sticky header, so the two never overlap. */
        inset-block-start: var(--header-height);
        height: calc(100vh - var(--header-height));
        padding-block-end: 0;
        border-block-start: 0;
        border-inline-end: 1px solid var(--color-border);
      }

      main {
        padding: var(--space-5) var(--space-6) var(--space-6);
      }

      .tabs {
        margin-block-end: var(--space-5);
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
  readonly hubs = input.required<readonly AdminNavHub[]>();
  readonly identity = input.required<AdminIdentityView>();
  readonly showNotifications = input(false);
  readonly notificationCount = input<number | null>(null);
  /** Whether the top bar offers "+ New". The app decides, from what the session may create. */
  readonly showCreate = input(false);

  /** The support impersonation in progress, or null. Non-null shows the banner. */
  readonly impersonation = input<ImpersonationView | null>(null);

  /** Whether the stop is in flight, so the banner's button can say so. */
  readonly endingImpersonation = input(false);

  readonly createOpened = output<void>();
  readonly searchOpened = output<void>();
  readonly signedOut = output<void>();

  /** The banner's Stop was pressed. The app ends the session and clears the input. */
  readonly impersonationExited = output<void>();
}
