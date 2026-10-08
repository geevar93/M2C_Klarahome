import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import type { HeaderStyle } from './site-header';
import { SiteHeader } from './site-header';

/**
 * The header styles.
 *
 * Every style re-places the same parts, so what has to hold is that none of them loses a control:
 * the drawer button, the menu, search, account and cart are present whichever is chosen, and
 * `inline` — the default — carries the marker the stylesheet keys on.
 */
describe('SiteHeader styles', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SiteHeader],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  const render = (inputs: Record<string, unknown> = {}) => {
    const fixture = TestBed.createComponent(SiteHeader);
    fixture.componentRef.setInput('menu', [{ label: 'Rugs', href: '/c/rugs' }]);
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const styles: HeaderStyle[] = ['inline', 'centred', 'search', 'wordmark'];

  it('defaults to inline', () => {
    expect(render().querySelector('.bar')?.getAttribute('data-style')).toBe('inline');
  });

  it.each(styles)('%s keeps the menu button, name, menu, account and cart', (style) => {
    const root = render({ style, storeName: 'Klara Home', cartCount: 2 });

    expect(root.querySelector('.bar')?.getAttribute('data-style')).toBe(style);
    expect(root.querySelector('.menu-button')).not.toBeNull();
    expect(root.querySelector('.wordmark')?.textContent).toContain('Klara Home');
    expect(root.querySelector('nav.primary a')?.getAttribute('href')).toBe('/c/rugs');
    expect(root.querySelector('a.account')).not.toBeNull();
    expect(root.querySelector('button.cart')?.getAttribute('aria-label')).toBe('Cart, 2 items');
    expect(root.querySelector('.search')).not.toBeNull();
  });

  it('wordmark is text only, never a logo image', () => {
    const root = render({ style: 'wordmark', storeName: 'Klara Home' });

    expect(root.querySelector('header img')).toBeNull();
    expect(root.querySelector('.wordmark')?.tagName).toBe('A');
  });
});
