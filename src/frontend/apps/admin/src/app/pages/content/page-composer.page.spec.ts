import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideKlaraHomeHttp } from '@klarahome/data-access-auth';
import { provideKlaraHomeI18n } from '@klarahome/i18n';
import { RUNTIME_CONFIG } from '@klarahome/util';

import { PageComposerPage } from './page-composer.page';

/**
 * Reference fields are tags, not text boxes, and adding to them adds.
 *
 * A product carousel's `productIds` is the case that motivated both: it used to be a textarea of
 * UUIDs, and the first version of the browse dialog replaced the list with only the new choices.
 */
describe('PageComposerPage reference fields', () => {
  const config = {
    apiBaseUrl: 'http://api.klarahome.test',
    tenantCode: 'test',
    locale: 'en-IN',
    timeZone: 'Asia/Kolkata',
    environment: 'local' as const,
    features: {},
  };

  const product = (id: string, name: string) => ({
    id,
    name,
    slug: name.toLowerCase().replace(/\s+/g, '-'),
    status: 'Active',
    categoryId: 'c1',
    brandId: null,
    vendorId: null,
    variantCount: 1,
    listingCount: 1,
    primaryImageFileId: `file-${id}`,
    ratingAverage: null,
    ratingCount: 0,
    publishedAt: null,
    createdAt: '2026-09-01T00:00:00Z',
  });

  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PageComposerPage],
      providers: [
        { provide: RUNTIME_CONFIG, useValue: config },
        provideKlaraHomeI18n(config.locale),
        provideKlaraHomeHttp(config),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'page-1' }) } },
        },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
  });

  const settle = async (fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  const productSearches = () =>
    http.match((request) => request.url.endsWith('/admin/products') && request.params.has('search'));

  it('shows held products as named tags, and adding keeps what was already there', async () => {
    const fixture = TestBed.createComponent(PageComposerPage);
    const element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();

    http
      .match((request) => request.url.endsWith('/block-types'))
      .forEach((request) =>
        request.flush([
          {
            type: 'productCarousel',
            label: 'Product carousel',
            description: '',
            maxItems: 0,
            itemFields: null,
            fields: [
              {
                name: 'productIds',
                kind: 'ProductRef',
                isRequired: false,
                isList: true,
                maxLength: 24,
                choices: null,
              },
            ],
          },
        ]),
      );
    http.match((request) => request.url.endsWith('/versions')).forEach((request) => request.flush([]));
    http
      .match((request) => request.url.endsWith('/pages/page-1'))
      .forEach((request) =>
        request.flush({
          id: 'page-1',
          slug: 'home',
          title: 'Home',
          type: 'Home',
          status: 'Draft',
          version: 1,
          publishedAt: null,
          summary: null,
          author: null,
          tags: [],
          coverImage: null,
          seo: { metaTitle: null, metaDescription: null, canonicalUrl: null, noIndex: false },
          allowedTransitions: [],
          blocks: [
            {
              id: 'b1',
              type: 'productCarousel',
              position: 0,
              config: { productIds: ['p1', 'p2'] },
              isVisible: true,
              startsAt: null,
              endsAt: null,
            },
          ],
        }),
      );
    await settle(fixture);

    // The two held ids are looked up in one request, by id.
    const lookup = productSearches();
    expect(lookup.map((request) => request.request.params.get('search'))).toEqual(['p1 p2']);
    lookup[0].flush({ items: [product('p1', 'Brass lamp'), product('p2', 'Jute rug')], page: {} });
    await settle(fixture);

    const tagText = () =>
      Array.from(element.querySelectorAll('kh-reference-tags .tag .text')).map((tag) =>
        tag.textContent?.trim(),
      );
    expect(tagText()).toEqual(['Brass lamp', 'Jute rug']);
    expect(element.querySelector('kh-reference-tags img')?.getAttribute('src')).toContain(
      '/store/media/file-p1/image',
    );
    expect(element.querySelector('article.block textarea')).toBeNull();

    // Add a third through the dialog.
    const browse = Array.from(element.querySelectorAll<HTMLButtonElement>('kh-reference-tags button')).find(
      (button) => button.textContent?.trim() === 'Add products',
    );
    browse?.click();
    await settle(fixture);
    await new Promise((resolve) => setTimeout(resolve, 0));

    productSearches().forEach((request) =>
      request.flush({ items: [product('p1', 'Brass lamp'), product('p3', 'Cotton throw')], page: {} }),
    );
    await settle(fixture);

    const row = Array.from(document.querySelectorAll<HTMLLabelElement>('kh-entity-multi-picker .row')).find(
      (label) => label.textContent?.includes('Cotton throw'),
    );
    row?.querySelector<HTMLInputElement>('input[type=checkbox]')?.click();
    await settle(fixture);

    Array.from(document.querySelectorAll<HTMLButtonElement>('kh-entity-multi-picker button'))
      .find((button) => button.textContent?.trim() === 'Add 1')
      ?.click();
    await settle(fixture);

    expect(tagText()).toEqual(['Brass lamp', 'Jute rug', 'Cotton throw']);
  });
});
