import { breadcrumbsFor } from './breadcrumbs';

describe('breadcrumbsFor', () => {
  it('has no trail on the dashboard, and none for a URL it cannot place', () => {
    expect(breadcrumbsFor('/dashboard')).toEqual([]);
    expect(breadcrumbsFor('/')).toEqual([]);
    expect(breadcrumbsFor('/nowhere/at/all')).toEqual([]);
  });

  it('shows Home, then the page, when the page is its own section heading', () => {
    expect(breadcrumbsFor('/orders').map((crumb) => crumb.label)).toEqual(['Home', 'Orders']);
  });

  it('files a page under its section as plain text, not a link', () => {
    const trail = breadcrumbsFor('/inventory/warehouses');
    expect(trail.map((crumb) => crumb.label)).toEqual(['Home', 'Inventory', 'Warehouses']);
    expect(trail[1].path).toBeUndefined();
  });

  it('links a detail page back to its list, matching the :id by pattern and ignoring the query', () => {
    const trail = breadcrumbsFor('/orders/4f1c?tab=notes');
    expect(trail.map((crumb) => crumb.label)).toEqual(['Home', 'Orders', 'Order']);
    expect(trail[1].path).toBe('/orders');
  });

  it('knows the two routes that are not declared destinations', () => {
    expect(breadcrumbsFor('/more').map((crumb) => crumb.label)).toEqual(['Home', 'All screens']);
    expect(breadcrumbsFor('/profile').map((crumb) => crumb.label)).toEqual(['Home', 'Your profile']);
  });
});
