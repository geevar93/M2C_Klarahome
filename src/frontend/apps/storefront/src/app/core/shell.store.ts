import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { CartStore, CartSummaryStore } from '@klarahome/data-access-cart';
import { ProductSearchService } from '@klarahome/data-access-catalog';
import { StoreConfigService, StoreContentService } from '@klarahome/data-access-content';
import { AuthService, SessionStore } from '@klarahome/data-access-auth';
import { BannerView, MiniCartLine, NavItem, SuggestionView } from '@klarahome/ui-patterns';
import { BrowserStorage, SeoService, ThemeService } from '@klarahome/util';
import { money } from '@klarahome/domain';
import { Subject, debounceTime, distinctUntilChanged, switchMap } from 'rxjs';

import { toBannerViews } from './banner.mapper';
import { CatalogMapper } from './catalog.mapper';
import { MENU_CODES, toNavItems } from './menu.mapper';
import { RecentSearchesStore } from './recent-searches.store';

/**
 * How long the box waits before asking.
 *
 * Long enough that typing "coffee table" is one request rather than eleven, short enough that the
 * panel feels like it is keeping up. The debounce is here, in the store, rather than in
 * `SearchEntry`: how much network a keystroke is worth is a deployment decision, not a property of
 * a text box.
 */
const SUGGEST_DEBOUNCE_MS = 200;

/** Where the dismissed cart dock remembers the item count it was dismissed at. */
const DOCK_HIDDEN_KEY = 'kh.cart.dock.hidden';

/** Below this, the server has nothing better to offer than the shopper's own history. */
const MIN_SUGGEST_LENGTH = 2;

/** The bundled square mark, `apps/storefront/public/brand/icon-512.png`, for a store with no logo set. */
const FALLBACK_LOGO_PATH = '/brand/icon-512.png';

/**
 * Everything the shell renders, in one place.
 *
 * The header, the drawers and the footer are `ui` components that know nothing about the API;
 * this is where the storefront's data meets them. It is a store rather than logic in the App
 * component because two things need it that are not the same component — the header and the
 * mobile drawer both render the menu, the header badge and the mini-cart both read the basket —
 * and because SSR calls `initialise` exactly once for the whole render.
 */
@Injectable({ providedIn: 'root' })
export class ShellStore {
  private readonly content = inject(StoreContentService);
  private readonly config = inject(StoreConfigService);
  private readonly cart = inject(CartSummaryStore);
  private readonly auth = inject(AuthService);
  private readonly storage = inject(BrowserStorage);
  /** The mutating half of the basket — only used here for the mini-cart's per-line remove. */
  private readonly cartActions = inject(CartStore);
  private readonly session = inject(SessionStore);
  private readonly seo = inject(SeoService);
  private readonly theme = inject(ThemeService);
  private readonly search = inject(ProductSearchService);
  private readonly mapper = inject(CatalogMapper);
  private readonly recentSearches = inject(RecentSearchesStore);

  private readonly announcements = signal<readonly BannerView[]>([]);
  private readonly header = signal<readonly NavItem[]>([]);
  private readonly footer = signal<readonly NavItem[]>([]);
  private readonly socialMenu = signal<readonly NavItem[]>([]);
  private readonly navDrawerOpen = signal(false);
  private readonly cartDrawerOpen = signal(false);
  private readonly typed = signal('');
  private readonly served = signal<readonly SuggestionView[]>([]);
  private readonly queries = new Subject<string>();

  /** The API's `Organization` and `WebSite` nodes, once they have arrived. */
  private siteGraph: readonly unknown[] | null = null;
  private started = false;

  readonly storeName = computed(() => this.config.branding().storeName);
  readonly tagline = computed(() => this.config.branding().tagline);
  /**
   * The announcement bar's banners.
   *
   * On the shell rather than on a page, because the strip is above the header and therefore on
   * every page. Fetched once with the menus (Step 28B, deliverable 19).
   */
  readonly announcementBanners: Signal<readonly BannerView[]> = this.announcements.asReadonly();

  readonly headerMenu: Signal<readonly NavItem[]> = this.header.asReadonly();
  readonly footerMenu: Signal<readonly NavItem[]> = this.footer.asReadonly();
  /** The `social` menu: the profile links the footer draws as icons. Empty where none is built. */
  readonly socialLinks: Signal<readonly NavItem[]> = this.socialMenu.asReadonly();

  readonly isNavOpen: Signal<boolean> = this.navDrawerOpen.asReadonly();
  readonly isCartOpen: Signal<boolean> = this.cartDrawerOpen.asReadonly();

  readonly isAuthenticated = this.session.isAuthenticated;

  /**
   * What the search box offers.
   *
   * Recent searches while the box is empty, the server's suggestions once there is something to
   * suggest against. Never both: on a 360px screen a list mixing the two is a list nobody scans,
   * and once a shopper is typing the server knows more than their history does.
   */
  readonly suggestions: Signal<readonly SuggestionView[]> = computed(() =>
    this.typed().trim().length < MIN_SUGGEST_LENGTH ? this.recentSearches.suggestions() : this.served(),
  );

  readonly suggestionsHeading = computed(() =>
    this.typed().trim().length < MIN_SUGGEST_LENGTH ? 'Recent searches' : '',
  );
  readonly cartCount = this.cart.itemCount;
  readonly cartLoading = this.cart.isLoading;
  readonly cartSubtotal = this.cart.subtotal;

  /**
   * The item count at which the shopper last hid the cart dock, or null.
   *
   * Session-scoped: a dismissed dock stays away for this visit, but a basket that has changed
   * since — one more item, one fewer — is news, and the dock comes back to report it.
   */
  private readonly dockHiddenAt = signal<number | null>(this.readDockHiddenAt());

  /** Whether the floating cart panel should show: something in the basket, and not dismissed at this count. */
  readonly isCartDockOpen = computed(() => {
    const count = this.cart.itemCount();
    return count > 0 && this.dockHiddenAt() !== count && !this.cartDrawerOpen();
  });

  /** The basket, in the shape the mini-cart renders. Capped: a drawer is a summary, not the cart. */
  readonly miniCartLines: Signal<readonly MiniCartLine[]> = computed(() => {
    const currency = this.cart.current()?.currencyCode ?? 'INR';
    return this.cart
      .lines()
      .slice(0, 6)
      .map<MiniCartLine>((line) => ({
        id: line.id,
        name: line.name,
        quantity: line.quantity,
        lineTotal: money(line.lineTotal, currency),
        reference: line.sku,
      }));
  });

  /**
   * Loads what the shell needs, once per application.
   *
   * All four reads happen during server rendering, so the header, the footer and the page's
   * canonical tag are in the HTML a crawler receives; the transfer cache then stops the browser
   * asking for any of them again on hydration.
   */
  initialise(): void {
    if (this.started) return;
    this.started = true;

    this.config.load().subscribe(() => {
      // The `{store}` token in the title template is the branding settings' store name, which
      // arrives here rather than with the SEO configuration. `configure` merges, so whichever of
      // the two answers second completes the pair instead of overwriting it.
      this.seo.configure({ storeName: this.storeName(), storeTagline: this.tagline() });
      this.publishSiteStructuredData();
      // The white-label mechanism (docs/10-design-system.md §6): whatever token overrides this
      // tenant's branding section carries are applied to `:root` now, during the same SSR pass
      // that renders the header with this tenant's name.
      this.theme.apply(this.config.branding().themeTokens);
    });
    this.content
      .banners('AnnouncementBar')
      .subscribe((banners) => this.announcements.set(toBannerViews(banners)));
    this.content.menu(MENU_CODES.header).subscribe((menu) => this.header.set(toNavItems(menu.items)));
    this.content.menu(MENU_CODES.footer).subscribe((menu) => this.footer.set(toNavItems(menu.items)));
    this.content.menu(MENU_CODES.social).subscribe((menu) => this.socialMenu.set(toNavItems(menu.items)));
    this.content.seoConfig().subscribe((seoConfig) => {
      this.seo.configure(seoConfig);
      this.publishSiteStructuredData();
    });
    this.content.siteStructuredData().subscribe((nodes) => {
      this.siteGraph = nodes;
      this.publishSiteStructuredData();
    });

    // The session is restored before the basket is read. A returning customer's anonymous basket
    // was merged into their own at sign-in, so a cart read before `/auth/refresh` answers shows the
    // empty anonymous one — a "0" badge over a basket with three things in it. The refresh is
    // single-flight and silent: it answers false, never throws, when there is nobody to restore.
    if (this.session.isResolved()) {
      this.cart.loadOnce();
    } else {
      const load = () => this.cart.loadOnce();
      this.auth.refresh().subscribe({ next: load, error: load });
    }

    // `switchMap`, so a slow answer for "cof" can never arrive after and overwrite the answer for
    // "coffee" — which is the bug that makes an autocomplete look haunted.
    this.queries
      .pipe(
        debounceTime(SUGGEST_DEBOUNCE_MS),
        distinctUntilChanged(),
        switchMap((query) => this.search.suggest(query)),
      )
      .subscribe((response) =>
        this.served.set(response.suggestions.map((item) => this.mapper.suggestion(item))),
      );
  }

  /** The shopper typed. Called on every keystroke; the debounce above decides what reaches the API. */
  suggest(query: string): void {
    this.typed.set(query);
    if (query.trim().length >= MIN_SUGGEST_LENGTH) this.queries.next(query.trim());
    else this.served.set([]);
  }

  /** A search was submitted or a suggestion taken; the panel's state is no longer wanted. */
  clearSuggestions(): void {
    this.typed.set('');
    this.served.set([]);
  }

  openNav(): void {
    this.navDrawerOpen.set(true);
  }

  closeNav(): void {
    this.navDrawerOpen.set(false);
  }

  toggleNav(): void {
    this.navDrawerOpen.update((open) => !open);
  }

  hideCartDock(): void {
    const count = this.cart.itemCount();
    this.dockHiddenAt.set(count);
    this.storage.set(DOCK_HIDDEN_KEY, String(count), 'session');
  }

  private readDockHiddenAt(): number | null {
    const raw = this.storage.get(DOCK_HIDDEN_KEY, 'session');
    const parsed = raw === null ? NaN : Number(raw);
    return Number.isFinite(parsed) ? parsed : null;
  }

  openCart(): void {
    // Opened, so it is read now rather than from whenever the page last loaded — a basket changed
    // in another tab is the ordinary case on a desktop.
    this.cart.load();
    this.cartDrawerOpen.set(true);
  }

  closeCart(): void {
    this.cartDrawerOpen.set(false);
  }

  /**
   * Removes one line from the mini-cart.
   *
   * Delegated to `CartStore` — the mutating half of the basket, normally reserved for the cart
   * page — rather than duplicating a remove call here: it already carries the optimistic update
   * and rollback (`docs/25` cart work). `CartSummaryStore` is read-only by design (see its own doc
   * comment), so its copy is refreshed from the same response once the write lands, which is what
   * keeps the header badge and the drawer themselves in step with the cart page.
   */
  removeCartLine(lineId: string): void {
    this.cartActions.remove(lineId).subscribe({
      next: (cart) => this.cart.replace(cart),
      error: () => undefined,
    });
  }

  /**
   * `Organization` and `WebSite`, which belong to the site rather than to any page.
   *
   * `SearchAction` is what lets a search box appear under the store's result in Google, and it is
   * the reason the site-wide block is written here and not on the home page: it has to be on
   * every page a crawler might enter through (docs/05-frontend-architecture.md §3.5).
   */
  private publishSiteStructuredData(): void {
    // The API's graph when it has answered: it carries the logo, `sameAs` and the contact point,
    // which the two nodes below cannot, and one `@graph` block links them by `@id`.
    if (this.siteGraph) {
      const graph = this.siteGraph.map((node) => this.withFallbackLogo(node));
      this.seo.setJsonLd('organization', { '@context': 'https://schema.org', '@graph': graph });
      this.seo.clearJsonLd('website');
      return;
    }

    const name = this.storeName();
    const url = this.seo.canonicalBaseUrl;
    if (!url) return;

    this.seo.setJsonLd('organization', {
      '@context': 'https://schema.org',
      '@type': 'Organization',
      name,
      url,
      logo: this.seo.absolute(FALLBACK_LOGO_PATH),
    });

    this.seo.setJsonLd('website', {
      '@context': 'https://schema.org',
      '@type': 'WebSite',
      name,
      url,
      potentialAction: {
        '@type': 'SearchAction',
        target: { '@type': 'EntryPoint', urlTemplate: `${url}/search?q={search_term_string}` },
        'query-input': 'required name=search_term_string',
      },
    });
  }

  /**
   * The `Organization` node with a logo, supplying the bundled mark when the store has uploaded
   * none. Google shows an organisation's logo only when the markup names one, and an unset logo
   * setting is the ordinary state of a new store, not a decision to have no logo.
   */
  private withFallbackLogo(node: unknown): unknown {
    const record = node as Record<string, unknown>;
    if (record['@type'] !== 'Organization' || record['logo']) return node;
    return { ...record, logo: this.seo.absolute(FALLBACK_LOGO_PATH) };
  }
}
