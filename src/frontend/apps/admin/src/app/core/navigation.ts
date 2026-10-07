import { Type } from '@angular/core';
import { Route } from '@angular/router';
import {
  Session,
  authenticatedGuard,
  permissionGuard,
  platformOnlyGuard,
  platformPermissionGuard,
  vendorOnlyGuard,
} from '@klarahome/data-access-auth';
import { AdminNavCategory, AdminNavItem, AdminNavSection, unsavedChangesGuard } from '@klarahome/ui-admin';

/**
 * Every destination in the back office, declared once.
 *
 * **This is the step's central idea.** The full acceptance criterion for Step 26 is that two users
 * with different roles see correctly different navigation *and* are blocked from the routes behind
 * it — and those are two statements about the same fact. Written separately, as a nav array and a
 * route table, they agree on the day they are written and diverge on the day somebody adds a
 * screen: a menu item with no guard is a 403 the user was invited into, and a guard with no menu
 * item is a screen nobody can find.
 *
 * So there is one list. `adminRoutes()` builds the router configuration from it and
 * `navCategories()` builds the navigation from it, and neither can describe a destination the other
 * does not.
 *
 * Two kinds of gate, and the difference matters:
 *
 *  - **`permissions`** — any one of them will do. A screen is usually reachable by more than one
 *    role, and the finer distinctions inside it are made by hiding individual controls.
 *  - **`platformOnly`** — a scope, not a permission. A vendor owner *does* hold
 *    `vendors.vendor.read`; what they may not have is the platform's seller directory. No
 *    permission separates those two, so the token's `vendorId` does.
 *
 * A destination whose screen is not yet written carries `step` instead of `load`, and renders the
 * honest placeholder rather than a stub that pretends. The route, its guard and its menu item are
 * real from the day the destination is declared, which is what makes the RBAC navigation testable
 * before the screen exists — and what makes landing a screen a one-line diff here. Step 27 filled
 * in the eighteen operations destinations that way and Step 28 the twenty-four merchandising,
 * seller, finance, reporting and settings destinations, so **no `step` marker remains**. The field
 * stays because the arrangement is the point: the next screen this back office grows is declared
 * here first, with its guard and its menu item, and filled in afterwards.
 *
 * **Step 30 regrouped the sidebar around what a merchant is doing, not around the module that
 * happens to own the screen.** Eleven engineering-shaped sections (Catalogue, Inventory, Pricing,
 * Content, Finance, Insight, …) read fine to whoever built them and badly to the person who has to
 * find "the thing where I mark a parcel packed" among them. The regrouping below only ever touches
 * `section`, `label` and order — every `path` is byte-for-byte what it was, so a bookmarked or
 * shared link keeps working, and only the heading and wording around it changed. Where a label was
 * renamed, the reason is a merchant word for what platform staff called it internally (`Fulfilment`
 * → "To pack", `Ledger` → "Seller ledger", and so on) — the comment above each moved block says why.
 *
 * **The navigation is two levels, and only the first is in the sidebar.** Eleven sections and
 * thirty-odd items in one sidebar was a directory, and it read as clutter. The sidebar now lists
 * the six categories in `NAV_CATEGORIES` (Home, Sell, Catalogue, Grow, Marketplace, Business) and
 * nothing else; opening one shows that category's screens as a row of tabs above the page, with a
 * row of sections ahead of it when the category has more than one (Catalogue's Products, Organise
 * and Inventory). A section is still the unit of grouping - the categories only say which sections
 * share a sidebar entry - and `navCategories()` builds both levels from `DESTINATIONS`, the same
 * way `visibleSections()` always did. Every `path` is, again, byte-for-byte what it was.
 */
export interface AdminDestination {
  /** The router path, without a leading slash. */
  readonly path: string;
  readonly label: string;
  /** The section this is filed under; `NAV_CATEGORIES` says which sidebar category that is in. */
  readonly section: NavSection;
  /** A name from the `kh-icon` registry. An unknown one falls back rather than throwing. */
  readonly icon?: string;
  /** Any one of these grants it. Absent means "any signed-in user". */
  readonly permissions?: readonly string[];
  /** Platform staff only — a seller may not see it whatever permissions they hold. */
  readonly platformOnly?: boolean;
  /** A seller's own screens. Platform staff have no seller to view them through. */
  readonly vendorOnly?: boolean;
  /** A detail route, or one reached from elsewhere. It has a guard but no menu item. */
  readonly hidden?: boolean;
  /** The real screen. Absent means the placeholder, and `step` says who builds it. */
  readonly load?: () => Promise<Type<unknown>>;
  /**
   * Whether leaving this screen mid-edit should be challenged.
   *
   * Declared here rather than on the component, because a `canDeactivate` guard is part of the
   * route and this is the file that owns routes. It is opt-in: most admin screens either save
   * immediately or hold three fields, and a confirmation on those is noise that trains people to
   * dismiss it — which is what makes it useless on the screen that has ninety.
   */
  readonly guardUnsavedChanges?: boolean;
  /** Which step builds this screen, for the placeholder's own text. */
  readonly step?: string;
}

/**
 * The sections, in the order they appear within their category.
 *
 * `System` is deliberately last: feature flags, the audit log and the notification message log are
 * tools for diagnosing the platform, not for running the shop, so they are the last section of the
 * last category.
 */
export const NAV_SECTIONS = [
  'Home',
  'Orders',
  'Products',
  'Organise',
  'Inventory',
  'Promotions',
  'Storefront',
  'Marketplace',
  'Your business',
  'Settings',
  'System',
] as const;

export type NavSection = (typeof NAV_SECTIONS)[number];

/**
 * The sidebar's entries, and which sections are filed under each.
 *
 * The same eleven sections as ever, grouped for a sidebar of six rather than a list of thirty-five.
 * Every `NavSection` appears in exactly one category (the spec checks), so nothing declared in
 * `DESTINATIONS` can be missing from the navigation. `icon` is a name from the `kh-icon` registry.
 * The category's own link opens on the first screen in it that the session can reach.
 */
export const NAV_CATEGORIES: readonly {
  readonly key: string;
  readonly label: string;
  readonly icon: string;
  readonly sections: readonly NavSection[];
}[] = [
  { key: 'home', label: 'Home', icon: 'home', sections: ['Home'] },
  { key: 'sell', label: 'Sell', icon: 'cart', sections: ['Orders'] },
  { key: 'catalogue', label: 'Catalogue', icon: 'package', sections: ['Products', 'Organise', 'Inventory'] },
  { key: 'grow', label: 'Grow', icon: 'megaphone', sections: ['Promotions', 'Storefront'] },
  { key: 'marketplace', label: 'Marketplace', icon: 'wallet', sections: ['Marketplace'] },
  { key: 'business', label: 'Business', icon: 'settings', sections: ['Your business', 'Settings', 'System'] },
];

export const DESTINATIONS: readonly AdminDestination[] = [
  // ---- Home -----------------------------------------------------------------------------------
  {
    path: 'dashboard',
    label: 'Dashboard',
    section: 'Home',
    icon: 'home',
    load: () => import('../pages/dashboard.page').then((m) => m.DashboardPage),
  },

  // ---- Orders -----------------------------------------------------------------------------------
  {
    path: 'orders',
    label: 'Orders',
    section: 'Orders',
    icon: 'cart',
    permissions: ['orders.order.read'],
    load: () => import('../pages/orders/orders.page').then((m) => m.OrdersPage),
  },
  {
    path: 'orders/:id',
    label: 'Order',
    section: 'Orders',
    permissions: ['orders.order.read'],
    hidden: true,
    load: () => import('../pages/orders/order-detail.page').then((m) => m.OrderDetailPage),
  },
  {
    // "Fulfilment" is the module's own name for this; "To pack" is what the person doing it calls
    // it. Same screen, same guard, same route.
    path: 'fulfilment',
    label: 'To pack',
    section: 'Orders',
    icon: 'package',
    permissions: ['orders.order.transition', 'shipping.shipment.manage'],
    load: () => import('../pages/fulfilment/fulfilment.page').then((m) => m.FulfilmentPage),
  },
  {
    path: 'shipments',
    label: 'Shipments',
    section: 'Orders',
    icon: 'truck',
    permissions: ['shipping.shipment.read'],
    load: () => import('../pages/fulfilment/shipments.page').then((m) => m.ShipmentsPage),
  },
  {
    // "NDR" (non-delivery report) is the courier's term. Nobody outside logistics reads that
    // abbreviation as "a parcel a customer refused, or a driver could not deliver".
    path: 'ndr',
    label: 'Delivery problems',
    section: 'Orders',
    icon: 'alert',
    permissions: ['shipping.ndr.manage'],
    load: () => import('../pages/fulfilment/ndr.page').then((m) => m.NdrPage),
  },
  {
    path: 'returns',
    label: 'Returns',
    section: 'Orders',
    icon: 'refresh',
    permissions: ['returns.return.read'],
    load: () => import('../pages/returns/returns.page').then((m) => m.ReturnsPage),
  },
  {
    path: 'returns/:id',
    label: 'Return',
    section: 'Orders',
    permissions: ['returns.return.read'],
    hidden: true,
    load: () => import('../pages/returns/return-detail.page').then((m) => m.ReturnDetailPage),
  },

  // ---- Products ---------------------------------------------------------------------------------
  // Catalogue and inventory used to be two sections; a merchant thinks of both as "the product
  // side of the business" and does not draw the platform's internal boundary between them.
  {
    path: 'catalog/products',
    label: 'Products',
    section: 'Products',
    icon: 'package',
    permissions: ['catalog.product.read'],
    load: () => import('../pages/catalog/products.page').then((m) => m.ProductsPage),
  },
  {
    path: 'catalog/products/:id',
    label: 'Product',
    section: 'Products',
    permissions: ['catalog.product.read'],
    hidden: true,
    // The one screen in the back office long enough that a stray click on the sidebar loses real
    // work: ninety fields, a media list and a variant table.
    guardUnsavedChanges: true,
    load: () => import('../pages/catalog/product-detail.page').then((m) => m.ProductDetailPage),
  },
  {
    path: 'inventory/stock',
    label: 'Stock',
    section: 'Products',
    icon: 'layers',
    permissions: ['inventory.stock.read'],
    load: () => import('../pages/inventory/stock.page').then((m) => m.StockPage),
  },
  {
    // "Moderation" describes the platform's action on the listing; "Products to review" describes
    // what the person opening the screen is about to do.
    path: 'catalog/moderation',
    label: 'Products to review',
    section: 'Products',
    icon: 'check',
    permissions: ['catalog.product.moderate'],
    platformOnly: true,
    load: () => import('../pages/catalog/moderation.page').then((m) => m.ModerationPage),
  },

  // "Organise" is the taxonomy: the things a product is filed under. Touched when the range
  // changes shape, not when a product is added, so it is a second row of tabs rather than the first.
  {
    path: 'catalog/categories',
    label: 'Categories',
    section: 'Organise',
    icon: 'filter',
    permissions: ['catalog.taxonomy.read'],
    load: () => import('../pages/catalog/categories.page').then((m) => m.CategoriesPage),
  },
  {
    path: 'catalog/brands',
    label: 'Brands',
    section: 'Organise',
    icon: 'tag',
    permissions: ['catalog.taxonomy.read'],
    load: () => import('../pages/catalog/brands.page').then((m) => m.BrandsPage),
  },
  {
    path: 'catalog/attributes',
    label: 'Attributes',
    section: 'Organise',
    icon: 'sort',
    permissions: ['catalog.taxonomy.read'],
    load: () => import('../pages/catalog/attributes.page').then((m) => m.AttributesPage),
  },

  // "Inventory" is the warehouse's side of the product: what moved, what was counted, what was
  // bought, and where from. Warehouses and suppliers used to sit under Settings; they are set up
  // once, but the person who sets them up is the person raising purchase orders, not the one
  // managing users and tax rates.
  {
    path: 'inventory/adjustments',
    // Short, because the sidebar truncated "Adjustments & transfers"; the screen's own heading still
    // says both, since a transfer between warehouses is half of what it does.
    label: 'Adjustments',
    section: 'Inventory',
    icon: 'edit',
    permissions: ['inventory.stock.adjust'],
    load: () => import('../pages/inventory/adjustments.page').then((m) => m.AdjustmentsPage),
  },
  {
    path: 'inventory/stock-takes',
    label: 'Stock takes',
    section: 'Inventory',
    icon: 'clipboard',
    permissions: ['inventory.stock-take.manage'],
    load: () => import('../pages/inventory/stock-takes.page').then((m) => m.StockTakesPage),
  },
  {
    path: 'inventory/purchase-orders',
    label: 'Purchase orders',
    section: 'Inventory',
    icon: 'download',
    permissions: ['inventory.purchasing.manage'],
    load: () => import('../pages/inventory/purchase-orders.page').then((m) => m.PurchaseOrdersPage),
  },
  {
    path: 'inventory/warehouses',
    label: 'Warehouses',
    section: 'Inventory',
    icon: 'home',
    permissions: ['inventory.warehouse.manage'],
    load: () => import('../pages/inventory/warehouses.page').then((m) => m.WarehousesPage),
  },
  {
    path: 'inventory/suppliers',
    label: 'Suppliers',
    section: 'Inventory',
    icon: 'truck',
    permissions: ['inventory.purchasing.manage'],
    load: () => import('../pages/inventory/suppliers.page').then((m) => m.SuppliersPage),
  },

  // ---- Grow: Storefront -------------------------------------------------------------------------
  // What shoppers see. The CMS screens, filed under the category named for what the merchant is doing
  // with them rather than for the module that owns them.
  {
    path: 'content/pages',
    label: 'Pages',
    section: 'Storefront',
    icon: 'edit',
    permissions: ['content.content.manage'],
    platformOnly: true,
    load: () => import('../pages/content/pages.page').then((m) => m.ContentPagesPage),
  },
  {
    path: 'content/pages/:id',
    label: 'Page',
    section: 'Storefront',
    permissions: ['content.content.manage'],
    platformOnly: true,
    hidden: true,
    // The second screen in the back office long enough to lose real work to a stray click: a block
    // list, a schema-driven form per block and the SEO panel are all unsaved until Save draft.
    guardUnsavedChanges: true,
    load: () => import('../pages/content/page-composer.page').then((m) => m.PageComposerPage),
  },
  {
    path: 'content/menus',
    label: 'Menus',
    section: 'Storefront',
    icon: 'menu',
    permissions: ['content.content.manage'],
    platformOnly: true,
    load: () => import('../pages/content/menus.page').then((m) => m.MenusPage),
  },
  {
    path: 'content/menus/:id',
    label: 'Menu',
    section: 'Storefront',
    permissions: ['content.content.manage'],
    platformOnly: true,
    hidden: true,
    // The whole item tree is held locally until it is saved in one write; see the editor.
    guardUnsavedChanges: true,
    load: () => import('../pages/content/menu-editor.page').then((m) => m.MenuEditorPage),
  },
  {
    path: 'content/banners',
    label: 'Banners',
    section: 'Storefront',
    icon: 'info',
    permissions: ['content.content.manage'],
    platformOnly: true,
    load: () => import('../pages/content/banners.page').then((m) => m.BannersPage),
  },
  {
    path: 'content/collections',
    label: 'Collections',
    section: 'Storefront',
    icon: 'filter',
    permissions: ['content.content.manage'],
    platformOnly: true,
    load: () => import('../pages/content/collections.page').then((m) => m.CollectionsPage),
  },
  {
    path: 'content/collections/:id',
    label: 'Collection',
    section: 'Storefront',
    permissions: ['content.content.manage'],
    platformOnly: true,
    hidden: true,
    guardUnsavedChanges: true,
    load: () => import('../pages/content/collection-detail.page').then((m) => m.CollectionDetailPage),
  },
  // ---- Grow: Promotions -------------------------------------------------------------------------
  // What makes shoppers buy. The first row of the Grow category, because a promotion is the thing a
  // merchant opens this category to run.
  {
    path: 'promotions',
    label: 'Promotions',
    section: 'Promotions',
    icon: 'wallet',
    permissions: ['pricing.promotion.read'],
    platformOnly: true,
    load: () => import('../pages/pricing/promotions.page').then((m) => m.PromotionsPage),
  },
  {
    // `new` matches this route, and the builder treats it as "not saved yet". A promotion's id is
    // a GUID, so the literal can never collide with a real one - and a create screen that was not
    // the edit screen would be two forms with one set of rules between them.
    path: 'promotions/:id',
    label: 'Promotion',
    section: 'Promotions',
    permissions: ['pricing.promotion.read'],
    platformOnly: true,
    hidden: true,
    // The largest form in the back office: a rule, its scope and its tiers, all unsaved until Save.
    guardUnsavedChanges: true,
    load: () => import('../pages/pricing/promotion-detail.page').then((m) => m.PromotionDetailPage),
  },
  {
    path: 'price-lists',
    label: 'Price lists',
    section: 'Promotions',
    icon: 'card',
    permissions: ['pricing.price-list.read'],
    load: () => import('../pages/pricing/price-lists.page').then((m) => m.PriceListsPage),
  },
  {
    path: 'price-lists/:id',
    label: 'Price list',
    section: 'Promotions',
    permissions: ['pricing.price-list.read'],
    hidden: true,
    load: () => import('../pages/pricing/price-list-detail.page').then((m) => m.PriceListDetailPage),
  },

  // ---- More: Marketplace ------------------------------------------------------------------------
  // The marketplace's own commercial side: who is selling, what they are owed, and what the
  // business is making from it. Platform-only, other than a seller's own read of `vendors.vendor`,
  // which `platformOnly` still refuses — see `canReach`.
  {
    path: 'vendors',
    label: 'Sellers',
    section: 'Marketplace',
    icon: 'user',
    permissions: ['vendors.vendor.read'],
    platformOnly: true,
    load: () => import('../pages/vendors/vendors.page').then((m) => m.VendorsPage),
  },
  {
    path: 'vendors/:id',
    label: 'Seller',
    section: 'Marketplace',
    permissions: ['vendors.vendor.read'],
    platformOnly: true,
    hidden: true,
    load: () => import('../pages/vendors/vendor-detail.page').then((m) => m.VendorDetailPage),
  },
  {
    path: 'commission-plans',
    label: 'Commission plans',
    section: 'Marketplace',
    icon: 'card',
    permissions: ['vendors.commission.manage'],
    platformOnly: true,
    load: () => import('../pages/vendors/commission-plans.page').then((m) => m.CommissionPlansPage),
  },
  {
    path: 'settlements/cycles',
    label: 'Settlement cycles',
    section: 'Marketplace',
    icon: 'clock',
    permissions: ['settlements.settlement.read'],
    load: () => import('../pages/finance/settlement-cycles.page').then((m) => m.SettlementCyclesPage),
  },
  {
    path: 'payouts',
    label: 'Payouts',
    section: 'Marketplace',
    icon: 'wallet',
    permissions: ['settlements.payout.manage', 'settlements.settlement.read'],
    load: () => import('../pages/finance/payouts.page').then((m) => m.PayoutsPage),
  },
  {
    path: 'payouts/:id',
    label: 'Payout run',
    section: 'Marketplace',
    permissions: ['settlements.payout.manage', 'settlements.settlement.read'],
    hidden: true,
    load: () => import('../pages/finance/payout-detail.page').then((m) => m.PayoutDetailPage),
  },
  {
    // "Ledger" alone reads as a bookkeeping term; this is specifically each seller's running
    // balance with the platform.
    path: 'ledger',
    label: 'Seller ledger',
    section: 'Marketplace',
    // Not `card`: Commission plans already wears it in this group, and two identical glyphs on
    // one row of tabs say nothing.
    icon: 'clipboard',
    permissions: ['settlements.settlement.read'],
    load: () => import('../pages/finance/ledger.page').then((m) => m.LedgerPage),
  },
  {
    path: 'reports',
    label: 'Reports',
    section: 'Marketplace',
    icon: 'sort',
    permissions: ['reporting.report.read'],
    load: () => import('../pages/reports/reports.page').then((m) => m.ReportsPage),
  },
  {
    path: 'reports/:key',
    label: 'Report',
    section: 'Marketplace',
    permissions: ['reporting.report.read'],
    hidden: true,
    load: () => import('../pages/reports/report-detail.page').then((m) => m.ReportDetailPage),
  },

  // ---- Your business (a seller's own screens) ----------------------------------------------------
  {
    path: 'vendor/onboarding',
    label: 'Getting set up',
    section: 'Your business',
    icon: 'check',
    vendorOnly: true,
    load: () => import('../pages/vendor/onboarding.page').then((m) => m.VendorOnboardingPage),
  },
  {
    path: 'vendor/profile',
    label: 'Seller profile',
    section: 'Your business',
    icon: 'user',
    vendorOnly: true,
    load: () => import('../pages/vendor/profile.page').then((m) => m.VendorProfilePage),
  },
  {
    // "How you are doing" was already merchant language; "Performance" is the one-word version the
    // rest of the sidebar uses for other single-screen sections.
    path: 'vendor/performance',
    label: 'Performance',
    section: 'Your business',
    icon: 'sort',
    vendorOnly: true,
    load: () => import('../pages/vendor/performance.page').then((m) => m.VendorPerformancePage),
  },

  // ---- Settings -----------------------------------------------------------------------------------
  {
    path: 'settings/store',
    label: 'Store settings',
    section: 'Settings',
    icon: 'home',
    permissions: ['platform.settings.manage'],
    platformOnly: true,
    guardUnsavedChanges: true,
    load: () => import('../pages/settings/store-settings.page').then((m) => m.StoreSettingsPage),
  },
  {
    // "Shipping zones" is the module's name; the screen is where both the delivery areas and their
    // rates are set, which is what someone configuring delivery actually searches for.
    path: 'settings/shipping-zones',
    label: 'Delivery zones & rates',
    section: 'Settings',
    icon: 'truck',
    permissions: ['shipping.rate.manage', 'shipping.shipment.read'],
    platformOnly: true,
    load: () => import('../pages/settings/shipping-zones.page').then((m) => m.ShippingZonesPage),
  },
  {
    path: 'tax-rates',
    label: 'Tax rates',
    section: 'Settings',
    icon: 'card',
    permissions: ['pricing.tax-rate.read'],
    platformOnly: true,
    load: () => import('../pages/pricing/tax-rates.page').then((m) => m.TaxRatesPage),
  },
  {
    path: 'settings/users',
    label: 'Users',
    section: 'Settings',
    icon: 'user',
    permissions: ['identity.user.read'],
    load: () => import('../pages/settings/users.page').then((m) => m.UsersPage),
  },
  {
    path: 'settings/users/:id',
    label: 'User',
    section: 'Settings',
    permissions: ['identity.user.read'],
    hidden: true,
    load: () => import('../pages/settings/user-detail.page').then((m) => m.UserDetailPage),
  },
  {
    path: 'settings/roles',
    label: 'Roles',
    section: 'Settings',
    icon: 'check',
    permissions: ['identity.role.read'],
    platformOnly: true,
    load: () => import('../pages/settings/roles.page').then((m) => m.RolesPage),
  },
  {
    path: 'content/redirects',
    label: 'Redirects',
    section: 'Settings',
    icon: 'refresh',
    permissions: ['content.redirect.manage'],
    platformOnly: true,
    load: () => import('../pages/content/redirects.page').then((m) => m.RedirectsPage),
  },

  // ---- System (platform diagnostics, not merchant tools; kept last) -------------------------------
  {
    path: 'settings/feature-flags',
    label: 'Feature flags',
    section: 'System',
    icon: 'filter',
    permissions: ['platform.settings.manage'],
    platformOnly: true,
    load: () => import('../pages/settings/feature-flags.page').then((m) => m.FeatureFlagsPage),
  },
  {
    path: 'settings/audit-log',
    label: 'Audit log',
    section: 'System',
    icon: 'clock',
    permissions: ['platform.audit.read'],
    load: () => import('../pages/audit-log.page').then((m) => m.AuditLogPage),
  },
  {
    // "Notifications" is what the module is called; this screen is specifically the delivery log
    // for messages already sent, not a place to read your own notifications.
    path: 'notifications',
    label: 'Message log',
    section: 'System',
    icon: 'bell',
    permissions: ['notifications.log.read'],
    load: () => import('../pages/notifications.page').then((m) => m.NotificationsPage),
  },
];

/**
 * Whether this session may reach a destination.
 *
 * The same predicate the guards enforce, evaluated against the session the client holds — which is
 * why the sidebar and the router cannot disagree about a screen. It is **not** the authorisation
 * decision: the API makes that, against the token, on every request.
 */
export function canReach(session: Session | null, destination: AdminDestination): boolean {
  if (!session) return false;

  const isVendor = session.vendorId !== undefined && session.vendorId !== null;
  if (destination.platformOnly && isVendor) return false;
  if (destination.vendorOnly && !isVendor) return false;

  const required = destination.permissions;
  if (!required || required.length === 0) return true;
  return required.some((permission) => session.permissions.includes(permission));
}

/**
 * The sections this session should see, in order, regardless of category.
 *
 * Sections with nothing in them are dropped, so a seller does not get an empty "Storefront" section
 * where the platform's four items would be. Global search's "Go to" group and the dashboard's
 * "do you have any screen at all" check read this flat list; the shell reads `navCategories()`.
 */
export function visibleSections(session: Session | null): readonly AdminNavSection[] {
  return NAV_SECTIONS.map((section) => ({
    label: section,
    items: DESTINATIONS.filter(
      (destination) =>
        destination.section === section && !destination.hidden && canReach(session, destination),
    ).map((destination) => ({
      label: destination.label,
      path: `/${destination.path}`,
      icon: destination.icon,
    })),
  })).filter((section) => section.items.length > 0);
}

/**
 * The navigation: the categories this session should see, each with its sections and screens.
 *
 * The sidebar draws only the categories; the shell draws the open one's sections and screens above
 * the page. Built from `visibleSections()`, the same list the route guards, global search and the
 * "All screens" page read, so the navigation cannot offer a screen the guard would refuse. A
 * category with nothing reachable in it is dropped, which is what keeps a support user's sidebar
 * to Home and Business rather than six entries four of which lead nowhere. `Home` is never empty
 * because the dashboard needs no permission.
 *
 * A category's `path` is the first reachable screen in it, so its sidebar link opens on a real
 * page for *this* session: a seller with no promotions permission gets Grow opening on Price
 * lists, not on a 403.
 *
 * `counts` is keyed by path (`/fulfilment` -> 8) and each screen whose path has a count wears it as
 * a badge. A section wears the sum of its screens and the category the sum of everything behind it,
 * so a person on Home can see that eleven things are waiting behind Sell before opening it. A
 * capped count (`50+`) makes every sum above it a cap too: adding an unknown to anything gives an
 * unknown, and `58` would be a number nobody counted.
 */
export function navCategories(
  session: Session | null,
  counts: ReadonlyMap<string, number | string>,
): readonly AdminNavCategory[] {
  const sections = visibleSections(session);

  return NAV_CATEGORIES.map((category): AdminNavCategory => {
    const own = sections
      .filter((section) => category.sections.includes(section.label as NavSection))
      .map((section) => {
        const tally = tallyQueues(section.items, counts);
        return { ...section, items: tally.items, badge: tally.badge };
      });
    const all = tallyQueues(own.flatMap((section) => section.items), counts);

    return {
      key: category.key,
      label: category.label,
      icon: category.icon,
      path: own[0]?.items[0]?.path ?? '/dashboard',
      sections: own,
      badge: all.badge,
    };
  }).filter((category) => category.sections.length > 0);
}

/** Puts each item's queue count on it and adds them up; a capped count caps the sum. */
function tallyQueues(
  items: readonly AdminNavItem[],
  counts: ReadonlyMap<string, number | string>,
): { readonly items: readonly AdminNavItem[]; readonly badge: number | string } {
  let total = 0;
  let capped = false;
  const counted = items.map((item) => {
    const count = counts.get(item.path);
    if (typeof count === 'number') total += count;
    if (typeof count === 'string') capped = true;
    return count === undefined ? item : { ...item, badge: count };
  });
  return { items: counted, badge: capped ? `${Math.max(total, QUEUE_CAP)}+` : total };
}

/** The page the counts are read from; a capped queue is at least this long. Matches `DashboardService`. */
const QUEUE_CAP = 50;

/**
 * The router configuration these destinations describe.
 *
 * `canActivate` rather than `canMatch`, because a route the router cannot match falls through to
 * the wildcard and the user is told the page does not exist — which is a lie, and a confusing one
 * when a colleague has just sent them the link. A guard that activates and redirects to `/403`
 * says the true thing: this page exists and is not yours.
 */
export function adminRoutes(): Route[] {
  return DESTINATIONS.map((destination) => ({
    path: destination.path,
    canActivate: [guardFor(destination)],
    canDeactivate: destination.guardUnsavedChanges ? [unsavedChangesGuard] : undefined,
    loadComponent:
      destination.load ?? (() => import('../pages/placeholder.page').then((m) => m.PlaceholderPage)),
    data: {
      title: destination.label,
      section: destination.section,
      placeholder: destination.load
        ? undefined
        : { heading: destination.label, step: destination.step ?? 'a later step' },
    },
  }));
}

function guardFor(destination: AdminDestination) {
  const permissions = destination.permissions ?? [];

  if (destination.vendorOnly) return vendorOnlyGuard;
  if (permissions.length === 0) return destination.platformOnly ? platformOnlyGuard : authenticatedGuard;

  // One guard rather than two, so `requireSession` — and the silent refresh inside it — runs once.
  return destination.platformOnly ? platformPermissionGuard(...permissions) : permissionGuard(...permissions);
}
