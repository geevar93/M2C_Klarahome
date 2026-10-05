import { Session } from '@klarahome/data-access-auth';

import {
  DESTINATIONS,
  NAV_CATEGORIES,
  NAV_SECTIONS,
  adminRoutes,
  canReach,
  navCategories,
  visibleSections,
} from './navigation';

/**
 * The one piece of Step 26 that is cheaper to test than to reason about twice.
 *
 * The sprint rules (`IMPLEMENTATION_PLAN.md` §3.2) defer tests, with one exception: a unit test is
 * written where it is the cheapest way to get an algorithm right while writing it — a state
 * machine's transition table, or a rule set like this one. `canReach` **is** the RBAC rule table,
 * two roles' worth of navigation come out of it, and the failure mode if it is wrong is a menu
 * item that leads to a 403 or a screen a seller can see another seller's version of.
 *
 * What it does not test is authorisation. The API enforces that; these are the rules for what is
 * *offered*, and the end-to-end proof that the two agree is Step 29's (`TEST_DEBT.md`).
 */
describe('Admin navigation rules', () => {
  const session = (overrides: Partial<Session> = {}): Session => ({
    userId: 'u1',
    displayName: 'Test user',
    permissions: [],
    roles: [],
    expiresAt: Date.now() + 60_000,
    ...overrides,
  });

  it('offers nothing to a signed-out visitor', () => {
    expect(visibleSections(null)).toEqual([]);
  });

  it('grants a destination when the session holds any one of its permissions', () => {
    const fulfilment = DESTINATIONS.find((entry) => entry.path === 'fulfilment');
    expect(fulfilment?.permissions?.length).toBeGreaterThan(1);

    const withOne = session({ permissions: ['shipping.shipment.manage'] });
    expect(canReach(withOne, fulfilment!)).toBe(true);
  });

  it('refuses a platform-only destination to a seller who holds its permission', () => {
    // The case the `platformOnly` flag exists for: a vendor owner genuinely holds
    // `vendors.vendor.read` — for their own record — and must still not see the seller directory.
    const directory = DESTINATIONS.find((entry) => entry.path === 'vendors');
    const seller = session({ vendorId: 'v1', permissions: ['vendors.vendor.read'] });
    const staff = session({ permissions: ['vendors.vendor.read'] });

    expect(canReach(seller, directory!)).toBe(false);
    expect(canReach(staff, directory!)).toBe(true);
  });

  it("refuses a seller's own screens to platform staff", () => {
    const onboarding = DESTINATIONS.find((entry) => entry.path === 'vendor/onboarding');
    expect(canReach(session({ permissions: [] }), onboarding!)).toBe(false);
    expect(canReach(session({ vendorId: 'v1' }), onboarding!)).toBe(true);
  });

  it('drops a section entirely when none of its items is reachable', () => {
    const sections = visibleSections(session({ permissions: ['orders.order.read'] }));
    expect(sections.map((entry) => entry.label)).toEqual(['Home', 'Orders']);
    expect(sections.find((entry) => entry.label === 'Orders')?.items.map((item) => item.path)).toEqual([
      '/orders',
    ]);
  });

  it('shows two roles two different navigations from one declaration', () => {
    const support = visibleSections(
      session({ permissions: ['identity.user.read', 'orders.order.read', 'notifications.log.read'] }),
    );
    const seller = visibleSections(
      session({ vendorId: 'v1', permissions: ['catalog.product.read', 'orders.order.read'] }),
    );

    const paths = (sections: ReturnType<typeof visibleSections>) =>
      sections.flatMap((section) => section.items.map((item) => item.path));

    expect(paths(support)).toContain('/settings/users');
    expect(paths(support)).not.toContain('/catalog/products');
    expect(paths(seller)).toContain('/catalog/products');
    expect(paths(seller)).not.toContain('/settings/users');
  });

  it('never lists a hidden detail route in the navigation', () => {
    const everything = session({
      permissions: DESTINATIONS.flatMap((entry) => entry.permissions ?? []),
    });
    const listed = visibleSections(everything).flatMap((section) => section.items.map((item) => item.path));

    for (const hidden of DESTINATIONS.filter((entry) => entry.hidden)) {
      expect(listed).not.toContain(`/${hidden.path}`);
    }
  });

  it('declares every destination in a known section', () => {
    for (const destination of DESTINATIONS) {
      expect(NAV_SECTIONS).toContain(destination.section);
    }
  });

  it('files every section under exactly one category, so nothing declared can go missing', () => {
    const filed = NAV_CATEGORIES.flatMap((category) => category.sections);
    expect([...filed].sort()).toEqual([...NAV_SECTIONS].sort());
    expect(new Set(filed).size).toBe(filed.length);
  });

  it('keeps the six categories and their labels', () => {
    expect(NAV_CATEGORIES.map((category) => category.label)).toEqual([
      'Home',
      'Sell',
      'Catalogue',
      'Grow',
      'Marketplace',
      'Business',
    ]);
  });

  it('builds the categories from the same reachable list, nothing added and nothing lost', () => {
    const everything = session({ permissions: DESTINATIONS.flatMap((entry) => entry.permissions ?? []) });
    const categories = navCategories(everything, new Map());

    const paths = categories.flatMap((category) =>
      category.sections.flatMap((section) => section.items.map((item) => item.path)),
    );
    const reachable = visibleSections(everything).flatMap((section) => section.items.map((item) => item.path));
    expect(paths.sort()).toEqual(reachable.sort());
  });

  it('drops a category with nothing reachable in it', () => {
    const support = navCategories(session({ permissions: ['identity.user.read', 'notifications.log.read'] }), new Map());
    expect(support.map((category) => category.key)).toEqual(['home', 'business']);
  });

  it('opens each category on the first screen this session can reach', () => {
    // A seller with no promotions permission: Grow must open on Price lists, not on a 403.
    const seller = navCategories(session({ vendorId: 'v1', permissions: ['pricing.price-list.read'] }), new Map());
    expect(seller.find((category) => category.key === 'grow')?.path).toBe('/price-lists');

    // A platform admin with everything: each opens on its own first screen.
    const everything = navCategories(
      session({ permissions: DESTINATIONS.flatMap((entry) => entry.permissions ?? []) }),
      new Map(),
    );
    expect(everything.find((category) => category.key === 'home')?.path).toBe('/dashboard');
    expect(everything.find((category) => category.key === 'sell')?.path).toBe('/orders');
    expect(everything.find((category) => category.key === 'catalogue')?.path).toBe('/catalog/products');
    expect(everything.find((category) => category.key === 'business')?.path).toBe('/settings/store');
  });

  it("gives a platform admin Catalogue's and Business's sections, and a seller only their own", () => {
    const everything = navCategories(
      session({ permissions: DESTINATIONS.flatMap((entry) => entry.permissions ?? []) }),
      new Map(),
    );
    const labels = (key: string) =>
      everything.find((category) => category.key === key)?.sections.map((section) => section.label);
    expect(labels('catalogue')).toEqual(['Products', 'Organise', 'Inventory']);
    expect(labels('business')).toEqual(['Settings', 'System']);
    expect(labels('marketplace')).toEqual(['Marketplace']);

    const seller = navCategories(session({ vendorId: 'v1', permissions: ['orders.order.read'] }), new Map());
    expect(seller.find((category) => category.key === 'business')?.sections.map((section) => section.label)).toEqual([
      'Your business',
    ]);
    expect(seller.find((category) => category.key === 'marketplace')).toBeUndefined();
  });

  it('counts the queues onto screens and adds them up on the section and the category', () => {
    const everything = session({ permissions: DESTINATIONS.flatMap((entry) => entry.permissions ?? []) });
    const categories = navCategories(
      everything,
      new Map<string, number | string>([
        ['/fulfilment', 8],
        ['/returns', 3],
        ['/catalog/moderation', '50+'],
      ]),
    );

    const sell = categories.find((category) => category.key === 'sell');
    const items = sell?.sections.flatMap((section) => section.items) ?? [];
    expect(items.find((item) => item.path === '/fulfilment')?.badge).toBe(8);
    expect(items.find((item) => item.path === '/orders')?.badge).toBeUndefined();
    expect(sell?.sections[0].badge).toBe(11);
    expect(sell?.badge).toBe(11);

    // A capped screen caps its section and category: nobody counted past fifty, so neither may claim to have.
    const catalogue = categories.find((category) => category.key === 'catalogue');
    expect(catalogue?.sections[0].badge).toBe('50+');
    expect(catalogue?.sections[1].badge).toBe(0);
    expect(catalogue?.badge).toBe('50+');
    expect(categories.find((category) => category.key === 'home')?.badge).toBe(0);
  });

  it('builds one guarded route per destination, and every route lazily', () => {
    const routes = adminRoutes();

    expect(routes).toHaveLength(DESTINATIONS.length);
    for (const route of routes) {
      expect(route.canActivate).toHaveLength(1);
      expect(typeof route.loadComponent).toBe('function');
    }
  });
});
