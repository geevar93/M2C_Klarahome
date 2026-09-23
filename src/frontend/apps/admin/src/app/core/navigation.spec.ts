import { Session } from '@klarahome/data-access-auth';

import {
  DESTINATIONS,
  HUBS,
  NAV_SECTIONS,
  SECTION_HUB,
  adminRoutes,
  canReach,
  visibleHubs,
  visibleSections,
  withQueueCounts,
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

  it('files every section behind exactly one of the five doors', () => {
    for (const section of NAV_SECTIONS) {
      expect(HUBS.map((hub) => hub.key)).toContain(SECTION_HUB[section]);
    }
    for (const destination of DESTINATIONS) {
      expect(NAV_SECTIONS).toContain(destination.section);
    }
  });

  it('drops a door with nothing reachable behind it', () => {
    const support = visibleHubs(session({ permissions: ['identity.user.read', 'notifications.log.read'] }));
    expect(support.map((hub) => hub.key)).toEqual(['home', 'more']);
  });

  it('opens each door on the first screen this session can reach', () => {
    // A seller with no promotions permission: the Grow door must open on Price lists, not on a 403.
    const seller = visibleHubs(session({ vendorId: 'v1', permissions: ['pricing.price-list.read'] }));
    expect(seller.find((hub) => hub.key === 'grow')?.path).toBe('/price-lists');

    // A platform admin with everything: the door opens on the hub's own first screen.
    const everything = visibleHubs(
      session({ permissions: DESTINATIONS.flatMap((entry) => entry.permissions ?? []) }),
    );
    expect(everything.find((hub) => hub.key === 'orders')?.path).toBe('/orders');
    expect(everything.find((hub) => hub.key === 'products')?.path).toBe('/catalog/products');
  });

  it('keeps More as a landing page however much is behind it', () => {
    const everything = visibleHubs(
      session({ permissions: DESTINATIONS.flatMap((entry) => entry.permissions ?? []) }),
    );
    const more = everything.find((hub) => hub.key === 'more');
    expect(more?.path).toBe('/more');
    expect(more?.sections.map((section) => section.label)).toEqual(['Marketplace', 'Settings', 'System']);
  });

  it("puts a seller's own screens behind More, and the marketplace nowhere", () => {
    const seller = visibleHubs(session({ vendorId: 'v1', permissions: ['orders.order.read'] }));
    const more = seller.find((hub) => hub.key === 'more');
    expect(more?.sections.map((section) => section.label)).toEqual(['Your business']);
  });

  it('counts the queues onto the tabs, and adds them up on the door', () => {
    const everything = visibleHubs(
      session({ permissions: DESTINATIONS.flatMap((entry) => entry.permissions ?? []) }),
    );
    const counted = withQueueCounts(
      everything,
      new Map<string, number | string>([
        ['/fulfilment', 8],
        ['/returns', 3],
        ['/catalog/moderation', '50+'],
      ]),
    );

    const orders = counted.find((hub) => hub.key === 'orders');
    const tabs = orders?.sections.flatMap((section) => section.items) ?? [];
    expect(tabs.find((item) => item.path === '/fulfilment')?.badge).toBe(8);
    expect(tabs.find((item) => item.path === '/orders')?.badge).toBeUndefined();
    expect(orders?.badge).toBe(11);

    // A capped tab caps the door: nobody counted past fifty, so the door must not claim to have.
    expect(counted.find((hub) => hub.key === 'products')?.badge).toBe('50+');
    expect(counted.find((hub) => hub.key === 'home')?.badge).toBe(0);
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
