import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { SessionStore, provideKlaraHomeHttp } from '@klarahome/data-access-auth';
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

    // Blocks load collapsed to a one-line summary; open the block to see its fields.
    element.querySelector<HTMLElement>('kh-disclosure summary')?.click();
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

/**
 * A published page is live: saving it changes what shoppers see immediately. The composer must say
 * so plainly and make saving go through a confirmation, rather than the old "nothing reaches the
 * storefront until it is saved" wording — which was true for a draft and false for a published page.
 */
describe('PageComposerPage saving a live page', () => {
  const config = {
    apiBaseUrl: 'http://api.klarahome.test',
    tenantCode: 'test',
    locale: 'en-IN',
    timeZone: 'Asia/Kolkata',
    environment: 'local' as const,
    features: {},
  };

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

    // The save button sits behind *khHasPermission, so the page needs someone allowed to edit it.
    TestBed.inject(SessionStore).signIn('token', {
      userId: 'u1',
      displayName: 'Editor',
      permissions: ['content.content.manage'],
      roles: [],
      expiresAt: Date.now() + 3_600_000,
    });
  });

  const settle = async (fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  const publishedPage = (title: string) => ({
    id: 'page-1',
    slug: 'home',
    title,
    type: 'Home',
    status: 'Published',
    version: 2,
    publishedAt: '2026-09-01T00:00:00Z',
    summary: null,
    author: null,
    tags: [],
    coverImage: null,
    seo: { metaTitle: null, metaDescription: null, canonicalUrl: null, noIndex: false },
    allowedTransitions: [],
    blocks: [],
  });

  it('says the page is live, and asks for confirmation before saving it', async () => {
    const fixture = TestBed.createComponent(PageComposerPage);
    const element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();

    http.match((request) => request.url.endsWith('/block-types')).forEach((request) => request.flush([]));
    http.match((request) => request.url.endsWith('/versions')).forEach((request) => request.flush([]));
    http
      .match((request) => request.url.endsWith('/pages/page-1') && request.method === 'GET')
      .forEach((request) => request.flush(publishedPage('Home')));
    await settle(fixture);

    const titleInput = element.querySelector<HTMLInputElement>('#page-title');
    if (!titleInput) throw new Error('Title input not found.');
    titleInput.value = 'Home, updated';
    titleInput.dispatchEvent(new Event('input'));
    await settle(fixture);

    // The banner tells the truth about a published page: saving updates the live site.
    expect(element.textContent).toContain('This page is live');
    expect(element.textContent).not.toContain('Nothing on this screen reaches the storefront');

    const saveButton = Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find((button) =>
      button.textContent?.includes('Save & update live site'),
    );
    expect(saveButton).toBeTruthy();
    saveButton?.click();
    await settle(fixture);

    // Nothing is written until the confirmation is accepted.
    http.expectNone((request) => request.url.endsWith('/pages/page-1') && request.method !== 'GET');
    expect(document.body.textContent).toContain(
      'This page is live. Saving will update it for shoppers right away.',
    );

    const confirmButton = Array.from(document.querySelectorAll<HTMLButtonElement>('button')).find(
      (button) => button.textContent?.trim() === 'Save & update',
    );
    confirmButton?.click();

    const saveRequest = http.expectOne(
      (request) => request.url.endsWith('/pages/page-1') && request.method !== 'GET',
    );
    saveRequest.flush(publishedPage('Home, updated'));
    await settle(fixture);

    expect(element.querySelector('#page-title')).toHaveProperty('value', 'Home, updated');
  });
});
