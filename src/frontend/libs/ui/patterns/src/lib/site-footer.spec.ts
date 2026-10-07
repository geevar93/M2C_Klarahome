import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { Icon } from '@klarahome/ui-primitives';

import { SiteFooter } from './site-footer';

/**
 * The follow row.
 *
 * The icon is derived from the address, so an editor adds a network by pasting its URL and nothing
 * in the CMS has to carry an icon name. A profile on a network this icon set has no mark for is
 * still a link — by its label — rather than an empty circle.
 */
describe('SiteFooter social links', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SiteFooter],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  const render = (
    social: { label: string; href?: string; children?: { label: string; href: string }[] }[],
  ) => {
    const fixture = TestBed.createComponent(SiteFooter);
    fixture.componentRef.setInput('social', social);
    fixture.detectChanges();
    return fixture;
  };

  // The icon's name is a signal input, so it is read from the component rather than the DOM: a
  // property binding writes no attribute.
  const icons = (fixture: ComponentFixture<SiteFooter>) =>
    fixture.debugElement.queryAll(By.css('.social li')).map((item) => {
      const icon = item.query(By.directive(Icon));
      const link = item.query(By.css('a')).nativeElement as HTMLAnchorElement;
      return {
        icon: icon ? (icon.componentInstance as Icon).name() : null,
        label: link.getAttribute('aria-label'),
        href: link.getAttribute('href'),
      };
    });

  it('draws a mark per network, whatever the path or regional host', () => {
    const fixture = render([
      { label: 'Instagram', href: 'https://www.instagram.com/klarahome/' },
      { label: 'Facebook', href: 'https://facebook.com/klarahome' },
      { label: 'X', href: 'https://x.com/klarahome' },
      { label: 'Twitch', href: 'https://www.twitch.tv/klarahome' },
    ]);

    expect(icons(fixture).map((link) => link.icon)).toEqual(['instagram', 'facebook', 'x', 'twitch']);
    expect(icons(fixture)[0].label).toBe('Instagram');
    expect(icons(fixture)[0].href).toBe('https://www.instagram.com/klarahome/');
  });

  it('takes the links from under a heading, and keeps the old bird address on X', () => {
    const fixture = render([
      {
        label: 'Follow us',
        children: [
          { label: 'Twitter', href: 'https://twitter.com/klarahome' },
          { label: 'YouTube', href: 'https://youtu.be/abc' },
        ],
      },
    ]);

    expect(icons(fixture).map((link) => link.icon)).toEqual(['x', 'youtube']);
  });

  it('shows a network it has no mark for by name, and renders nothing when there are none', () => {
    const fixture = render([{ label: 'Pinterest', href: 'https://pinterest.com/klarahome' }]);

    expect(icons(fixture)).toEqual([
      { icon: null, label: 'Pinterest', href: 'https://pinterest.com/klarahome' },
    ]);
    expect(render([]).nativeElement.querySelector('.social')).toBeNull();
  });
});

/**
 * The footer's layers.
 *
 * A store with no CMS menu must still get a footer that looks finished: the brand column and the
 * two storefront link columns are always drawn, they only ever point at routes the storefront
 * ships, and the CMS columns join them when there is a menu.
 */
describe('SiteFooter content', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SiteFooter],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  const render = (inputs: Record<string, unknown> = {}) => {
    const fixture = TestBed.createComponent(SiteFooter);
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const hrefs = (root: HTMLElement) =>
    Array.from(root.querySelectorAll('nav a')).map((a) => a.getAttribute('href'));

  it('draws the store name, the tagline and the always-valid links with no menu at all', () => {
    const root = render({ storeName: 'Klara Home', tagline: 'Soft furnishings, made to be lived in.' });

    expect(root.querySelector('.brand-name')?.textContent).toContain('Klara Home');
    expect(root.querySelector('.brand-tagline')?.textContent).toContain('Soft furnishings');
    expect(hrefs(root)).toEqual(['/search', '/cart', '/auth/login', '/auth/register']);
    expect(root.querySelector('.legal')?.textContent).toContain('Klara Home');
  });

  it('offers the account shortcuts, not the way in, to a signed-in shopper', () => {
    const root = render({ isAuthenticated: true });

    expect(hrefs(root)).toEqual(['/search', '/cart', '/account', '/account/orders', '/account/wishlist']);
  });

  it('omits the tagline when there is none, and adds the CMS columns after the built-in ones', () => {
    const root = render({
      menu: [{ label: 'Company', children: [{ label: 'Returns', href: '/pages/returns' }] }],
    });

    expect(root.querySelector('.brand-tagline')).toBeNull();
    expect(hrefs(root).at(-1)).toBe('/pages/returns');
    expect(root.querySelectorAll('nav.group').length).toBe(3);
  });

  it('puts the brand and every link column in the one grid, so none is placed outside it', () => {
    const root = render({
      menu: [
        { label: 'Shop', children: [{ label: 'All', href: '/c/all' }] },
        { label: 'Help', children: [{ label: 'FAQ', href: '/pages/faq' }] },
        { label: 'Company', children: [{ label: 'About', href: '/pages/about' }] },
      ],
    });

    const grid = root.querySelector('.groups');
    expect(grid?.children.length).toBe(1 + 2 + 3);
    expect(grid?.firstElementChild?.classList.contains('brand')).toBe(true);
  });
});
