import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Session } from '@klarahome/data-access-auth';
import { AdminNavCategory, AdminSidebar, AdminSubNav, categoryFor } from '@klarahome/ui-admin';

import { DESTINATIONS, navCategories } from './navigation';

@Component({ selector: 'kh-blank', template: '' })
class Blank {}

/**
 * The two halves of the navigation as the shell draws them: the sidebar (categories only) and the
 * tab rows above the page, built from the real declaration for a platform admin who may open
 * everything. Routes are stubs - what is under test is what the navigation shows for a URL, not
 * the screens - and every real path (hidden detail routes included) resolves, so `/orders/123`
 * is a navigation like any other.
 */
describe('Shell navigation', () => {
  const session: Session = {
    userId: 'u1',
    displayName: 'Admin',
    permissions: DESTINATIONS.flatMap((entry) => entry.permissions ?? []),
    roles: [],
    expiresAt: Date.now() + 60_000,
  };
  const counts = new Map<string, number | string>([
    ['/fulfilment', 8],
    ['/returns', 3],
  ]);
  const categories = navCategories(session, counts);

  @Component({
    selector: 'kh-test-host',
    imports: [AdminSidebar, AdminSubNav],
    template: `
      <kh-admin-sidebar [categories]="categories" />
      <kh-admin-sub-nav [category]="active()" />
    `,
  })
  class Host {
    readonly categories = categories;
    /** What the shell computes from the URL; set by `go` because this host is zoneless and OnPush-like. */
    readonly active = signal<AdminNavCategory | null>(null);
  }

  let fixture: ComponentFixture<Host>;
  let router: Router;

  const go = async (url: string) => {
    await router.navigateByUrl(url);
    fixture.componentInstance.active.set(categoryFor(categories, url));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const root = () => fixture.nativeElement as HTMLElement;
  const sidebarLinks = () => Array.from(root().querySelectorAll<HTMLAnchorElement>('kh-admin-sidebar nav a'));
  const tabs = () => Array.from(root().querySelectorAll<HTMLAnchorElement>('kh-admin-sub-nav nav.screens a'));
  const sections = () => Array.from(root().querySelectorAll<HTMLAnchorElement>('kh-admin-sub-nav nav.sections a'));
  const text = (a: HTMLElement) => a.textContent?.replace(/\s+/g, ' ').trim();
  const current = (links: HTMLAnchorElement[]) => links.filter((a) => a.getAttribute('aria-current') === 'page').map(text);

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Host],
      providers: [provideRouter(DESTINATIONS.map((destination) => ({ path: destination.path, component: Blank })))],
    }).compileComponents();
    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
  });

  it('lists only the six categories in the sidebar, with the queue total on the one that holds it', async () => {
    await go('/dashboard');
    expect(sidebarLinks().map((a) => text(a)!.replace(/\d+$/, '').trim())).toEqual([
      'Home',
      'Sell',
      'Catalogue',
      'Grow',
      'Marketplace',
      'Business',
    ]);
    expect(text(sidebarLinks()[1])).toContain('11');
  });

  it('draws no tab row on Home, which has one screen', async () => {
    await go('/dashboard');
    expect(current(sidebarLinks())).toEqual(['Home']);
    expect(tabs()).toHaveLength(0);
    expect(sections()).toHaveLength(0);
  });

  it('shows a single-section category as tabs only, with counts on the tabs', async () => {
    await go('/fulfilment');
    expect(current(sidebarLinks())).toEqual(['Sell11']);
    expect(sections()).toHaveLength(0);
    expect(tabs().map((a) => text(a))).toEqual(['Orders', 'To pack 8', 'Shipments', 'Delivery problems', 'Returns 3']);
    expect(current(tabs())).toEqual(['To pack 8']);
  });

  it('keeps the parent tab and category active on a detail route', async () => {
    await go('/orders/123');
    expect(current(sidebarLinks())).toEqual(['Sell11']);
    expect(current(tabs())).toEqual(['Orders']);
  });

  it('shows a multi-section category as a section row and the tabs of the current section only', async () => {
    await go('/inventory/warehouses');
    expect(sections().map((a) => text(a))).toEqual(['Products', 'Organise', 'Inventory']);
    expect(current(sections())).toEqual(['Inventory']);
    expect(tabs().map((a) => text(a))).toEqual([
      'Adjustments',
      'Stock takes',
      'Purchase orders',
      'Warehouses',
      'Suppliers',
    ]);
    expect(current(tabs())).toEqual(['Warehouses']);
  });

  it('opens a section on its first screen', async () => {
    await go('/catalog/products');
    const organise = sections().find((a) => text(a) === 'Organise');
    expect(organise?.getAttribute('href')).toBe('/catalog/categories');
  });

  it('marks nothing on a page no category lists', async () => {
    await go('/dashboard');
    expect(categoryFor(categories, '/more')).toBeNull();
  });
});
