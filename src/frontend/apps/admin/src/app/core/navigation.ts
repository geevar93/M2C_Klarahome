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
import { AdminNavSection, unsavedChangesGuard } from '@klarahome/ui-admin';

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
 * `visibleSections()` builds the sidebar from it, and neither can describe a destination the other
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
 */
export interface AdminDestination {
  /** The router path, without a leading slash. */
  readonly path: string;
  readonly label: string;
  /** The sidebar heading this is filed under. */
  readonly section: string;
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

/** The sidebar's headings, in the order they appear. */
export const NAV_SECTIONS = [
  'Overview',
  'Catalogue',
  'Inventory',
  'Orders',
  'Pricing',
  'Content',
  'Sellers',
  'Finance',
  'Insight',
  'Your seller',
  'Settings',
] as const;

export const DESTINATIONS: readonly AdminDestination[] = [
  // ---- Overview -------------------------------------------------------------------------------
  {
    path: 'dashboard',
    label: 'Dashboard',
    section: 'Overview',
    icon: 'home',
    load: () => import('../pages/dashboard.page').then((m) => m.DashboardPage),
  },

  // ---- Catalogue ------------------------------------------------------------------------------
  {
    path: 'catalog/products',
    label: 'Products',
    section: 'Catalogue',
    icon: 'package',
    permissions: ['catalog.product.read'],
    load: () => import('../pages/catalog/products.page').then((m) => m.ProductsPage),
  },
  {
    path: 'catalog/products/:id',
    label: 'Product',
    section: 'Catalogue',
    permissions: ['catalog.product.read'],
    hidden: true,
    // The one screen in the back office long enough that a stray click on the sidebar loses real
    // work: ninety fields, a media list and a variant table.
    guardUnsavedChanges: true,
    load: () => import('../pages/catalog/product-detail.page').then((m) => m.ProductDetailPage),
  },
  {
    path: 'catalog/categories',
    label: 'Categories',
    section: 'Catalogue',
    icon: 'filter',
    permissions: ['catalog.taxonomy.read'],
    load: () => import('../pages/catalog/categories.page').then((m) => m.CategoriesPage),
  },
  {
    path: 'catalog/brands',
    label: 'Brands',
    section: 'Catalogue',
    icon: 'info',
    permissions: ['catalog.taxonomy.read'],
    load: () => import('../pages/catalog/brands.page').then((m) => m.BrandsPage),
  },
  {
    path: 'catalog/attributes',
    label: 'Attributes',
    section: 'Catalogue',
    icon: 'sort',
    permissions: ['catalog.taxonomy.read'],
    load: () => import('../pages/catalog/attributes.page').then((m) => m.AttributesPage),
  },
  {
    path: 'catalog/moderation',
    label: 'Moderation',
    section: 'Catalogue',
    icon: 'check',
    permissions: ['catalog.product.moderate'],
    platformOnly: true,
    load: () => import('../pages/catalog/moderation.page').then((m) => m.ModerationPage),
  },

  // ---- Inventory ------------------------------------------------------------------------------
  {
    path: 'inventory/stock',
    label: 'Stock',
    section: 'Inventory',
    icon: 'package',
    permissions: ['inventory.stock.read'],
    load: () => import('../pages/inventory/stock.page').then((m) => m.StockPage),
  },
  {
    path: 'inventory/adjustments',
    label: 'Adjustments',
    section: 'Inventory',
    icon: 'edit',
    permissions: ['inventory.stock.adjust'],
    load: () => import('../pages/inventory/adjustments.page').then((m) => m.AdjustmentsPage),
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
    path: 'inventory/stock-takes',
    label: 'Stock takes',
    section: 'Inventory',
    icon: 'check',
    permissions: ['inventory.stock-take.manage'],
    load: () => import('../pages/inventory/stock-takes.page').then((m) => m.StockTakesPage),
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

  // ---- Orders ---------------------------------------------------------------------------------
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
    path: 'fulfilment',
    label: 'Fulfilment',
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
    path: 'ndr',
    label: 'Failed deliveries',
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

  // ---- Pricing --------------------------------------------------------------------------------
  {
    path: 'promotions',
    label: 'Promotions',
    section: 'Pricing',
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
    section: 'Pricing',
    permissions: ['pricing.promotion.read'],
    platformOnly: true,
    hidden: true,
    load: () => import('../pages/pricing/promotion-detail.page').then((m) => m.PromotionDetailPage),
  },
  {
    path: 'price-lists',
    label: 'Price lists',
    section: 'Pricing',
    icon: 'card',
    permissions: ['pricing.price-list.read'],
    load: () => import('../pages/pricing/price-lists.page').then((m) => m.PriceListsPage),
  },
  {
    path: 'price-lists/:id',
    label: 'Price list',
    section: 'Pricing',
    permissions: ['pricing.price-list.read'],
    hidden: true,
    load: () => import('../pages/pricing/price-list-detail.page').then((m) => m.PriceListDetailPage),
  },
  {
    path: 'tax-rates',
    label: 'Tax rates',
    section: 'Pricing',
    icon: 'card',
    permissions: ['pricing.tax-rate.read'],
    platformOnly: true,
    load: () => import('../pages/pricing/tax-rates.page').then((m) => m.TaxRatesPage),
  },

  // ---- Content --------------------------------------------------------------------------------
  {
    path: 'content/pages',
    label: 'Pages',
    section: 'Content',
    icon: 'edit',
    permissions: ['content.content.manage'],
    platformOnly: true,
    load: () => import('../pages/content/pages.page').then((m) => m.ContentPagesPage),
  },
  {
    path: 'content/pages/:id',
    label: 'Page',
    section: 'Content',
    permissions: ['content.content.manage'],
    platformOnly: true,
    hidden: true,
    // The second screen in the back office long enough to lose real work to a stray click: a block
    // list, a schema-driven form per block and the SEO panel are all unsaved until Save draft.
    guardUnsavedChanges: true,
    load: () => import('../pages/content/page-composer.page').then((m) => m.PageComposerPage),
  },
  {
    path: 'content/banners',
    label: 'Banners',
    section: 'Content',
    icon: 'info',
    permissions: ['content.content.manage'],
    platformOnly: true,
    load: () => import('../pages/content/banners.page').then((m) => m.BannersPage),
  },
  {
    path: 'content/menus',
    label: 'Menus',
    section: 'Content',
    icon: 'menu',
    permissions: ['content.content.manage'],
    platformOnly: true,
    load: () => import('../pages/content/menus.page').then((m) => m.MenusPage),
  },
  {
    path: 'content/menus/:id',
    label: 'Menu',
    section: 'Content',
    permissions: ['content.content.manage'],
    platformOnly: true,
    hidden: true,
    // The whole item tree is held locally until it is saved in one write; see the editor.
    guardUnsavedChanges: true,
    load: () => import('../pages/content/menu-editor.page').then((m) => m.MenuEditorPage),
  },
  {
    path: 'content/collections',
    label: 'Collections',
    section: 'Content',
    icon: 'filter',
    permissions: ['content.content.manage'],
    platformOnly: true,
    load: () => import('../pages/content/collections.page').then((m) => m.CollectionsPage),
  },
  {
    path: 'content/collections/:id',
    label: 'Collection',
    section: 'Content',
    permissions: ['content.content.manage'],
    platformOnly: true,
    hidden: true,
    load: () => import('../pages/content/collection-detail.page').then((m) => m.CollectionDetailPage),
  },
  {
    path: 'content/redirects',
    label: 'Redirects',
    section: 'Content',
    icon: 'refresh',
    permissions: ['content.redirect.manage'],
    platformOnly: true,
    load: () => import('../pages/content/redirects.page').then((m) => m.RedirectsPage),
  },

  // ---- Sellers (platform only; a seller manages itself under "Your seller") --------------------
  {
    path: 'vendors',
    label: 'Sellers',
    section: 'Sellers',
    icon: 'user',
    permissions: ['vendors.vendor.read'],
    platformOnly: true,
    load: () => import('../pages/vendors/vendors.page').then((m) => m.VendorsPage),
  },
  {
    path: 'vendors/:id',
    label: 'Seller',
    section: 'Sellers',
    permissions: ['vendors.vendor.read'],
    platformOnly: true,
    hidden: true,
    load: () => import('../pages/vendors/vendor-detail.page').then((m) => m.VendorDetailPage),
  },
  {
    path: 'commission-plans',
    label: 'Commission plans',
    section: 'Sellers',
    icon: 'card',
    permissions: ['vendors.commission.manage'],
    platformOnly: true,
    load: () => import('../pages/vendors/commission-plans.page').then((m) => m.CommissionPlansPage),
  },

  // ---- Finance --------------------------------------------------------------------------------
  {
    path: 'settlements/cycles',
    label: 'Settlement cycles',
    section: 'Finance',
    icon: 'clock',
    permissions: ['settlements.settlement.read'],
    load: () => import('../pages/finance/settlement-cycles.page').then((m) => m.SettlementCyclesPage),
  },
  {
    path: 'payouts',
    label: 'Payouts',
    section: 'Finance',
    icon: 'wallet',
    permissions: ['settlements.payout.manage', 'settlements.settlement.read'],
    load: () => import('../pages/finance/payouts.page').then((m) => m.PayoutsPage),
  },
  {
    path: 'payouts/:id',
    label: 'Payout run',
    section: 'Finance',
    permissions: ['settlements.payout.manage', 'settlements.settlement.read'],
    hidden: true,
    load: () => import('../pages/finance/payout-detail.page').then((m) => m.PayoutDetailPage),
  },
  {
    path: 'ledger',
    label: 'Ledger',
    section: 'Finance',
    icon: 'card',
    permissions: ['settlements.settlement.read'],
    load: () => import('../pages/finance/ledger.page').then((m) => m.LedgerPage),
  },

  // ---- Insight --------------------------------------------------------------------------------
  {
    path: 'reports',
    label: 'Reports',
    section: 'Insight',
    icon: 'sort',
    permissions: ['reporting.report.read'],
    load: () => import('../pages/reports/reports.page').then((m) => m.ReportsPage),
  },
  {
    path: 'reports/:key',
    label: 'Report',
    section: 'Insight',
    permissions: ['reporting.report.read'],
    hidden: true,
    load: () => import('../pages/reports/report-detail.page').then((m) => m.ReportDetailPage),
  },
  {
    path: 'notifications',
    label: 'Notifications',
    section: 'Insight',
    icon: 'bell',
    permissions: ['notifications.log.read'],
    load: () => import('../pages/notifications.page').then((m) => m.NotificationsPage),
  },

  // ---- A seller's own screens -----------------------------------------------------------------
  {
    path: 'vendor/onboarding',
    label: 'Getting set up',
    section: 'Your seller',
    icon: 'check',
    vendorOnly: true,
    load: () => import('../pages/vendor/onboarding.page').then((m) => m.VendorOnboardingPage),
  },
  {
    path: 'vendor/profile',
    label: 'Seller profile',
    section: 'Your seller',
    icon: 'user',
    vendorOnly: true,
    load: () => import('../pages/vendor/profile.page').then((m) => m.VendorProfilePage),
  },
  {
    path: 'vendor/performance',
    label: 'How you are doing',
    section: 'Your seller',
    icon: 'sort',
    vendorOnly: true,
    load: () => import('../pages/vendor/performance.page').then((m) => m.VendorPerformancePage),
  },

  // ---- Settings -------------------------------------------------------------------------------
  {
    path: 'settings/store',
    label: 'Store settings',
    section: 'Settings',
    icon: 'home',
    permissions: ['platform.settings.manage'],
    platformOnly: true,
    load: () => import('../pages/settings/store-settings.page').then((m) => m.StoreSettingsPage),
  },
  {
    path: 'settings/shipping-zones',
    label: 'Shipping zones',
    section: 'Settings',
    icon: 'truck',
    permissions: ['shipping.rate.manage', 'shipping.shipment.read'],
    platformOnly: true,
    load: () => import('../pages/settings/shipping-zones.page').then((m) => m.ShippingZonesPage),
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
    path: 'settings/feature-flags',
    label: 'Feature flags',
    section: 'Settings',
    icon: 'filter',
    permissions: ['platform.settings.manage'],
    platformOnly: true,
    load: () => import('../pages/settings/feature-flags.page').then((m) => m.FeatureFlagsPage),
  },
  {
    path: 'settings/audit-log',
    label: 'Audit log',
    section: 'Settings',
    icon: 'clock',
    permissions: ['platform.audit.read'],
    load: () => import('../pages/audit-log.page').then((m) => m.AuditLogPage),
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
 * The sidebar this session should see.
 *
 * Sections with nothing in them are dropped, so a seller does not get an empty "Content" heading
 * where the platform's five items would be.
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
