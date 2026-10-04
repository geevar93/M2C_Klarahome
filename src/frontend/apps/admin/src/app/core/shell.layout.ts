import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { IdentityAdminService, ImpersonationStore, VendorsAdminService } from '@klarahome/data-access-admin';
import { SessionStore } from '@klarahome/data-access-auth';
import { AdminAttentionItem, AdminCreateAction, AdminIdentityView, AdminShell } from '@klarahome/ui-admin';
import { ToastHost } from '@klarahome/ui-primitives';
import { filter, map } from 'rxjs';

import { breadcrumbsFor } from './breadcrumbs';
import { ColourSchemeService } from './colour-scheme';
import { sidebarGroups } from './navigation';
import { GlobalSearchPanel } from './global-search.panel';
import { ProductQuickAdd } from './product-quick-add';
import { QueueCountsStore } from './queue-counts.store';
import { QuickAction, quickActionsFor } from './quick-actions';
import { SignInFlow } from './sign-in.flow';

/** The glyph each work queue wears in the notifications panel. */
const ATTENTION_ICONS: Readonly<Record<string, string>> = {
  'to-pack': 'package',
  overdue: 'clock',
  returns: 'refresh',
  moderation: 'check',
  vendors: 'user',
  notifications: 'alert',
};

/**
 * Everything behind the sign-in screen.
 *
 * A layout route rather than the application component, because `/login` must not be inside it:
 * a shell that rendered its own navigation around a sign-in form would be a menu of links to
 * screens the visitor cannot open, and the identity in its top bar would have nobody to name.
 *
 * It owns the state that belongs to the whole back office and to no page — whether the command
 * palette is up, whether the product quick-add sheet is, and the colour scheme — and nothing else.
 * Everything a page needs, a page fetches.
 *
 * The **sidebar comes off the session**, through the same declaration the route guards are built
 * from (`navigation.ts`), so the shell cannot offer a screen the guard will refuse. The counts on
 * it, and in the notifications panel, come off `QueueCountsStore`, the same numbers the dashboard
 * shows, refreshed on navigation at the store's own pace. The breadcrumb trail is derived from
 * the same declaration and the current URL (`breadcrumbs.ts`).
 */
@Component({
  selector: 'kh-admin-layout',
  imports: [AdminShell, GlobalSearchPanel, ProductQuickAdd, RouterOutlet, ToastHost],
  template: `
    <kh-admin-shell
      [groups]="groups()"
      [crumbs]="crumbs()"
      [identity]="identity()"
      [showNotifications]="true"
      [attention]="attention()"
      [messageLogPath]="canReadNotifications() ? '/notifications' : null"
      [createActions]="createActions()"
      [scheme]="scheme.scheme()"
      [impersonation]="impersonation()"
      [endingImpersonation]="endingImpersonation()"
      (createChosen)="createFromMenu($event)"
      (searchOpened)="searchOpen.set(true)"
      (schemeToggled)="scheme.toggle()"
      (signedOut)="signOut()"
      (impersonationExited)="endImpersonation()"
    >
      <router-outlet />
    </kh-admin-shell>

    <kh-global-search
      [open]="searchOpen()"
      [actions]="quickActions()"
      (closed)="searchOpen.set(false)"
      (actionChosen)="openQuickAction($event)"
    />

    <kh-product-quick-add [open]="productQuickAddOpen()" (closed)="productQuickAddOpen.set(false)" />

    <kh-toast-host />
  `,
  host: {
    // Ctrl/Cmd+K opens the command palette from anywhere, and so does `/` (as it does in every
    // tool the people using this already use). `/` is guarded so it does not steal the slash out
    // of somebody's typing; Ctrl/Cmd+K is safe to take everywhere because no field uses it.
    '(document:keydown)': 'onKeydown($event)',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShellLayout {
  private readonly session = inject(SessionStore);
  private readonly vendors = inject(VendorsAdminService);
  private readonly signInFlow = inject(SignInFlow);
  private readonly router = inject(Router);

  private readonly queues = inject(QueueCountsStore);

  protected readonly scheme = inject(ColourSchemeService);

  protected readonly searchOpen = signal(false);
  protected readonly productQuickAddOpen = signal(false);

  /**
   * A quick action chosen from the palette or the Create menu. Most are a link; the product one
   * has a sheet of its own, which opens here rather than navigating to the full form.
   */
  protected openQuickAction(action: QuickAction): void {
    if (action.opens === 'product-quick-add') this.productQuickAddOpen.set(true);
    else void this.router.navigateByUrl(action.targetPath);
  }

  protected createFromMenu(key: string): void {
    const action = this.quickActions().find((candidate) => candidate.targetPath === key);
    if (action) this.openQuickAction(action);
  }

  protected readonly groups = computed(() =>
    sidebarGroups(this.session.session(), this.queues.countsByPath()),
  );
  protected readonly quickActions = computed(() => quickActionsFor(this.session.session()));

  protected readonly createActions = computed<readonly AdminCreateAction[]>(() =>
    this.quickActions().map((action) => ({
      key: action.targetPath,
      label: action.label,
      hint: action.hint,
      icon: action.icon,
    })),
  );

  /** The trail above the page, from the current URL and the same declaration the routes come from. */
  protected readonly crumbs = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map((event) => breadcrumbsFor((event as NavigationEnd).urlAfterRedirects)),
    ),
    { initialValue: breadcrumbsFor(this.router.url) },
  );

  /**
   * The notifications panel: the work queues that have something in them.
   *
   * The same numbers the dashboard shows (`QueueCountsStore`), not a separate feed — the API has
   * no event stream, and a second source would be a second number that disagrees with the first.
   */
  protected readonly attention = computed<readonly AdminAttentionItem[]>(() =>
    this.queues
      .tiles()
      .filter((tile) => tile.value !== null && tile.value !== 0)
      .map((tile) => ({
        key: tile.key,
        label: tile.label,
        hint: tile.hint,
        count: tile.value as number | string,
        path: tile.path,
        icon: ATTENTION_ICONS[tile.key],
      })),
  );

  protected readonly canReadNotifications = computed(() =>
    this.session.hasPermission('notifications.log.read'),
  );

  // ---- Support impersonation (Step 28B, deliverable 1) -------------------------------------------

  private readonly identityAdmin = inject(IdentityAdminService);
  private readonly impersonations = inject(ImpersonationStore);

  protected readonly endingImpersonation = signal(false);

  /**
   * What the banner shows, or null.
   *
   * The store's own shape minus the token: a component in `ui-admin` has no business holding an
   * access token, and the banner has no use for one.
   */
  protected readonly impersonation = computed(() => {
    const acting = this.impersonations.actingAs();

    return acting
      ? { displayName: acting.displayName, reason: acting.reason, expiresAt: acting.expiresAt }
      : null;
  });

  /**
   * Ends the impersonation, server first.
   *
   * The local state is cleared whatever the server says. A stop that failed leaves a session the
   * clock will end within the window anyway, and a banner an operator cannot dismiss is worse than
   * one that goes away a few seconds before the session does.
   */
  protected endImpersonation(): void {
    const acting = this.impersonations.actingAs();
    if (!acting || this.endingImpersonation()) return;

    this.endingImpersonation.set(true);

    this.identityAdmin.endImpersonation(acting.sessionId).subscribe({
      next: () => {
        this.endingImpersonation.set(false);
        this.impersonations.end();
      },
      error: () => {
        this.endingImpersonation.set(false);
        this.impersonations.end();
      },
    });
  }

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
    // The session ending underneath the shell. The interceptor has already tried the refresh
    // cookie and been refused, and `AuthService` has cleared the store; what is left is a user on
    // a screen every request from which now fails. A sign-out the user asked for also empties the
    // session, and `SignInFlow` says which of the two this is.
    let hadSession = this.session.isAuthenticated();
    effect(() => {
      const signedIn = this.session.isAuthenticated();
      if (signedIn) {
        hadSession = true;
        return;
      }
      if (!hadSession || this.signInFlow.signingOut()) return;
      hadSession = false;
      void this.signInFlow.sessionEnded(this.router.url);
    });

    // The queue counts on the tabs: on entry, and then on each navigation once they have gone
    // stale. The store decides what stale means; see `QueueCountsStore`.
    this.queues.refresh(true);
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.queues.refresh());

    if (this.session.session()?.vendorId) {
      this.vendors.mine().subscribe({
        next: (vendor) => this.vendorName.set(vendor.displayName),
        error: () => undefined,
      });
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      this.searchOpen.set(true);
      return;
    }

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
