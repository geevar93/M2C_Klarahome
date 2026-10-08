import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { CmsBlockView } from './catalog.model';
import { CmsBlockRenderer } from './cms-block-renderer';

type HeroBlock = Extract<CmsBlockView, { kind: 'hero' }>;

/**
 * What a shopper is shown for each block, chosen by what the block holds.
 *
 * The rules under test come from the template review (docs/design/storefront-designer.md): the
 * hero is one of three openers, a grid has exactly as many columns as cells, nothing on a public
 * page says "No image", and rich text is either at the gutter or centred.
 */
describe('CmsBlockRenderer', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CmsBlockRenderer],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  const image = { src: '/x.jpg', alt: 'x', srcset: '' } as unknown as NonNullable<HeroBlock['image']>;

  const render = (blocks: CmsBlockView[]) => {
    const fixture = TestBed.createComponent(CmsBlockRenderer);
    fixture.componentRef.setInput('blocks', blocks);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const hero = (overrides: Partial<HeroBlock> = {}): CmsBlockView => ({
    kind: 'hero',
    id: 'h',
    headline: 'Made slowly',
    subheadline: null,
    image,
    ctaLabel: 'Shop',
    ctaHref: '/shop',
    align: 'centre',
    ...overrides,
  });

  describe('hero', () => {
    it('centred with a picture is the full-bleed overlay', () => {
      const root = render([hero()]);
      expect(root.querySelector('.hero')?.getAttribute('data-layout')).toBe('bleed');
      expect(root.querySelector('.hero-media .scrim')).not.toBeNull();
    });

    it('left or right with a picture is a split card with the picture beside the copy', () => {
      for (const align of ['left', 'right'] as const) {
        const root = render([hero({ align })]);
        expect(root.querySelector('.hero')?.getAttribute('data-layout')).toBe('split');
        expect(root.querySelector('.hero-split .hero-copy')).not.toBeNull();
        expect(root.querySelector('.hero-split .hero-pic')).not.toBeNull();
        expect(root.querySelector('.scrim')).toBeNull();
      }
    });

    it('without a picture is a light compact panel, not an ink slab', () => {
      const root = render([hero({ image: null })]);
      expect(root.querySelector('.hero')?.getAttribute('data-layout')).toBe('plain');
      expect(root.querySelector('.hero-plain')).not.toBeNull();
      expect(root.querySelector('.hero-media')).toBeNull();
    });
  });

  describe('grids', () => {
    const tile = (n: number) => ({ label: `Room ${n}`, href: `/c/${n}`, image: null });

    it('never have more columns than cells', () => {
      const root = render([
        { kind: 'categoryTiles', id: 't', heading: null, columns: 3, items: [tile(1), tile(2)] },
      ]);
      const grid = root.querySelector('kh-grid') as HTMLElement;
      // Two tiles in a three-column block are two halves: one column's share is a half.
      expect(grid.style.gridTemplateColumns).toContain('/ 2)');
    });

    it('draws an image-less tile or banner as a flat tile with no placeholder text', () => {
      const root = render([
        { kind: 'categoryTiles', id: 't', heading: null, columns: 2, items: [tile(1), tile(2)] },
        {
          kind: 'bannerGrid',
          id: 'b',
          heading: null,
          columns: 2,
          items: [{ image: null, caption: 'New in', href: '/new' }],
        },
      ]);
      expect(root.querySelectorAll('.tile--flat').length).toBe(2);
      expect(root.querySelectorAll('.banner--flat').length).toBe(1);
      expect(root.textContent).not.toContain('No image');
      expect(root.querySelector('kh-product-image')).toBeNull();
    });

    it('sizes testimonial columns to the number of quotes', () => {
      const columnsFor = (count: number) => {
        const root = render([
          {
            kind: 'testimonial',
            id: 'q',
            heading: null,
            items: Array.from({ length: count }, (_, i) => ({
              quote: `Quote ${i}`,
              author: 'A',
              location: null,
              rating: null,
            })),
          },
        ]);
        return (root.querySelector('kh-grid') as HTMLElement).style.gridTemplateColumns;
      };
      expect(columnsFor(2)).toContain('/ 2)');
      expect(columnsFor(3)).toContain('/ 3)');
      expect(columnsFor(4)).toContain('/ 2)');
    });
  });

  describe('rich text', () => {
    const rich = (align?: 'start' | 'centre'): CmsBlockView => ({
      kind: 'richText',
      id: 'r',
      heading: null,
      html: '<p>Hello</p>',
      width: 'narrow',
      ...(align ? { align } : {}),
    });

    it('defaults to start and reads centre from the block', () => {
      expect(render([rich()]).querySelector('.rich')?.getAttribute('data-align')).toBe('start');
      expect(render([rich('centre')]).querySelector('.rich')?.getAttribute('data-align')).toBe('centre');
    });
  });
});
