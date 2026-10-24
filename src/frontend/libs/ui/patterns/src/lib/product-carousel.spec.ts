import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ProductCardView } from './catalog.model';
import { ProductCarousel } from './product-carousel';

/**
 * The rail: how many products it draws, how many columns it promises, and where "View all" goes.
 * Layout itself (scroll-snap from 'md', the four-card phone grid) is CSS; this pins the numbers
 * the CSS is fed.
 */
describe('ProductCarousel', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProductCarousel],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  const product = (index: number): ProductCardView => ({
    variantId: `v${index}`,
    productId: `p${index}`,
    listingId: `l${index}`,
    name: `Product ${index}`,
    href: `/p/${index}`,
    brand: null,
    price: null,
    mrp: null,
    image: null,
    rating: null,
    ratingCount: 0,
    isPurchasable: true,
  });

  const render = (count: number, inputs: Record<string, unknown> = {}) => {
    const fixture = TestBed.createComponent(ProductCarousel);
    fixture.componentRef.setInput('heading', 'Trending');
    fixture.componentRef.setInput(
      'products',
      Array.from({ length: count }, (_, i) => product(i)),
    );
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('draws every product when there is no limit', () => {
    expect(render(8).querySelectorAll('.items > li').length).toBe(8);
  });

  it('honours the limit', () => {
    expect(render(8, { limit: 4 }).querySelectorAll('.items > li').length).toBe(4);
    expect(render(3, { limit: 6 }).querySelectorAll('.items > li').length).toBe(3);
  });

  it('sizes the rail from the limit, not from how many cards it has', () => {
    const rail = (root: HTMLElement) => root.querySelector('.items') as HTMLElement;
    const six = rail(render(8, { limit: 6 }));
    expect(six.style.getPropertyValue('--cols-md')).toBe('4');
    expect(six.style.getPropertyValue('--cols-lg')).toBe('6');

    // Fewer cards than the limit: same card width, space left on the right.
    expect(rail(render(2, { limit: 6 })).style.getPropertyValue('--cols-lg')).toBe('6');
    expect(rail(render(3)).style.getPropertyValue('--cols-lg')).toBe('6');

    // A limit of four or fewer is four across.
    expect(rail(render(4, { limit: 4 })).style.getPropertyValue('--cols-lg')).toBe('4');
    expect(rail(render(2, { limit: 3 })).style.getPropertyValue('--cols-lg')).toBe('4');
  });

  it('caps the phone grid at four only when there is a View all to carry the rest', () => {
    expect(render(8).querySelector('.items.capped')).toBeNull();
    const root = render(8, { viewAllHref: '/c/all' });
    expect(root.querySelector('.items.capped')).not.toBeNull();
    expect(root.querySelectorAll('a.view-all').length).toBe(2);
  });

  it('puts no second View all under a short set', () => {
    const root = render(3, { viewAllHref: '/c/all' });
    expect(root.querySelectorAll('a.view-all').length).toBe(1);
  });
});
