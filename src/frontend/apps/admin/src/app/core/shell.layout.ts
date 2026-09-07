import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NotificationCentreService, VendorsAdminService } from '@klarahome/data-access-admin';
import { SessionStore } from '@klarahome/data-access-auth';
import { AdminIdentityView, AdminShell } from '@klarahome/ui-admin';
import { ToastHost } from '@klarahome/ui-primitives';
import { BrowserStorage } from '@klarahome/util';

import { visibleSections } from './navigation';
import { GlobalSearchPanel } from './global-search.panel';
import { SignInFlow } from './sign-in.flow';

/** Where the collapsed/expanded choice is remembered, per browser. */
const RAIL_KEY = 'kh.admin.rail-collapsed';

/**
 * Everything behind the sign-in screen.
 *
 * A layout route rather than the application component, because `/login` must not be inside it:
 * a shell that rendered its own navigation around a sign-in form would be a menu of links to
 * screens the visitor cannot open, and the identity in its top bar would have nobody to name.
 *
 * It owns the three pieces of state that belong to the whole back office and to no page — whether
 * the tablet drawer is open, whether the desktop rail is collapsed, and whether global search is
 * up — and nothing else. Everything a page needs, a page fetches.
 *
 * The **sections come off the session**, through the same declaration the route guards are built
 * from (`navigation.ts`), so the sidebar cannot offer a screen the guard will refuse.
 */
@Component({
  selector: 'kh-admin-layout',
  imports: [AdminShell, GlobalSearchPanel, RouterOutlet, ToastHost],
  template: `
    <kh-admin-shell
      [sections]="sections()"
      [identity]="identity()"
      [navOpen]="navOpen()"
      [collapsed]="collapsed()"
      [showNotifications]="canReadNotifications()"
      [notificationCount]="notificationCount()"
      (navToggled)="toggleNav()"
      (navClosed)="navOpen.set(false)"
      (searchOpened)="searchOpen.set(true)"
      (signedOut)="signOut()"
    >
      <router-outlet />
    </kh-admin-shell>

    <kh-global-search [open]="searchOpen()" (closed)="searchOpen.set(false)" />

    <kh-toast-host />
  `,
  host: {
    // `/` opens search from anywhere, as it does in every tool the people using this already use.
    // Guarded so it does not steal the slash out of somebody's typing.
    '(document:keydown)': 'onKeydown($event)',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShellLayout {
  private readonly session = inject(SessionStore);
  private readonly notifications = inject(NotificationCentreService);
  private readonly vendors = inject(VendorsAdminService);
  private readonly signInFlow = inject(SignInFlow);
  private readonly storage = inject(BrowserStorage);

  protected readonly navOpen = signal(false);
  protected readonly searchOpen = signal(false);
  protected readonly collapsed = signal(this.storage.getJson<boolean>(RAIL_KEY, false));

  protected readonly sections = computed(() => visibleSections(this.session.session()));
  protected readonly notificationCount = this.notifications.attentionCount;

  protected readonly canReadNotifications = computed(() =>
    this.session.hasPermission('notifications.log.read'),
  );

  /**
   * The seller's own name, once it has been fetched.
   *
   * The token carries a vendor **id** and not a name, which at Step 26 left a seller's top bar
   * reading `Seller 3f2a91cc` — scope information a vendor user must never be in doubt about,
   * presented in a form nobody recognises. One request to `GET /admin/vendors/me` fixes it, and it
   * is made here rather than on each seller screen because the top bar is what has to say it.
   *
   * Failure is silent and falls back to the shortened id: a name that could not be fetched is not
   * a reason to interrupt somebody, and the id is still true.
   */
  private readonly vendorName = signal<string | null>(null);

  protected readonly identity = computed<AdminIdentityView>(() => {
    const session = this.session.session();
    return {
      name: session?.displayName ?? 'Signed out',
      vendorName: session?.vendorId ? (this.vendorName() ?? `Seller ${session.vendorId.slice(0, 8)}`) : null,
      roles: session?.roles ?? [],
    };
  });

  constructor() {
    // Counted once on entry rather than polled. See `NotificationCentreService` for why.
    if (this.session.hasPermission('notifications.log.read')) {
      this.notifications.refreshAttentionCount().subscribe({ error: () => undefined });
    }

    if (this.session.session()?.vendorId) {
      this.vendors.mine().subscribe({
        next: (vendor) => this.vendorName.set(vendor.displayName),
        error: () => undefined,
      });
    }
  }

  /**
   * One button, two behaviours — and they are not a compromise.
   *
   * Below the sidebar's breakpoint there is no rail to collapse, so the control opens the drawer;
   * above it there is no drawer, so it narrows the rail. The width is read at the moment of the
   * press rather than tracked, because nothing else depends on it.
   */
  protected toggleNav(): void {
    const isWide = globalThis.matchMedia?.('(min-width: 60.0625rem)').matches ?? true;
    if (!isWide) {
      this.navOpen.update((open) => !open);
      return;
    }

    const next = !this.collapsed();
    this.collapsed.set(next);
    this.storage.setJson(RAIL_KEY, next);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key !== '/' || event.ctrlKey || event.metaKey || event.altKey) return;

    // Not while somebody is typing. `isContentEditable` covers the rich-text editor Step 28 adds.
    const target = event.target as HTMLElement | null;
    const tag = target?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target?.isContentEditable) return;

    event.preventDefault();
    this.searchOpen.set(true);
  }

  protected async signOut(): Promise<void> {
    await this.signInFlow.signOut();
  }
}
