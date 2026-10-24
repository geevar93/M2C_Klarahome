import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideKlaraHomeHttp } from '@klarahome/data-access-auth';
import { provideKlaraHomeI18n } from '@klarahome/i18n';
import { RUNTIME_CONFIG } from '@klarahome/util';

import { PREVIEW_DELAY_MS, StorefrontEditorPage, saveBody } from './editor.page';

/**
 * The editor's draft, its dirty flag and its debounced re-render, against the real services and a
 * stubbed network.
 *
 * What is being held here: the local draft is the source of truth. Typing moves `dirty` at once,
 * the preview request follows once typing stops (one request for a burst, not one per key), a
 * refused preview leaves the picture and the draft alone, and a save sends the page as loaded with
 * only the blocks replaced.
 */
describe('StorefrontEditorPage', () => {
  const config = {
    apiBaseUrl: 'http://api.klarahome.test',
    tenantCode: 'test',
    locale: 'en-IN',
    timeZone: 'Asia/Kolkata',
    environment: 'local' as const,
    features: {},
  };

  const heroType = {
    type: 'Hero',
    label: 'Hero',
    description: 'A banner',
    maxItems: 0,
    isPrivileged: false,
    itemFields: null,
    fields: [
      { name: 'headline', kind: 'Text', isRequired: true, isList: false, maxLength: 120, choices: null },
    ],
  };

  const loadedPage = {
    id: 'page-1',
    slug: 'home',
    title: 'Home',
    type: 'Home',
    status: 'Draft',
    version: 3,
    publishedAt: null,
    scheduledAt: null,
    contentChangedAt: null,
    summary: 'Welcome',
    author: 'Ops',
    tags: ['a'],
    coverImage: null,
    seo: {
      metaTitle: 'Home title',
      metaDescription: null,
      metaKeywords: null,
      canonicalUrl: null,
      ogTitle: 'Open graph title',
      ogDescription: null,
      ogImage: { fileId: 'og-1' },
      ogType: 'website',
      noIndex: false,
      noFollow: true,
      sitemapPriority: 0.8,
    },
    allowedTransitions: ['Published'],
    blocks: [
      {
        id: 'b1',
        type: 'Hero',
        position: 0,
        config: { headline: 'Handwoven for the home' },
        isVisible: true,
        startsAt: null,
        endsAt: null,
      },
    ],
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: null,
  };

  const rendered = (headline: string) => ({
    id: 'page-1',
    slug: 'home',
    type: 'Home',
    title: 'Home',
    summary: null,
    seo: loadedPage.seo,
    coverImage: null,
    author: null,
    tags: [],
    publishedAt: null,
    updatedAt: null,
    blocks: [
      {
        id: 'b1',
        type: 'Hero',
        position: 0,
        config: { headline, align: 'left' },
        images: [],
        products: [],
        categories: [],
      },
    ],
  });

  let http: HttpTestingController;
  let fixture: ComponentFixture<StorefrontEditorPage>;
  const root = () => fixture.nativeElement as HTMLElement;
  const previews = () => http.match((request) => request.url.endsWith('/pages/page-1/preview-draft'));

  const tick = (ms: number) => {
    vi.advanceTimersByTime(ms);
    fixture.detectChanges();
  };

  /** Brings the page up: block types, branding, the home page and the first render. */
  const open = () => {
    fixture = TestBed.createComponent(StorefrontEditorPage);
    fixture.detectChanges();

    http.expectOne((request) => request.url.endsWith('/pages/block-types')).flush([heroType]);
    http
      .expectOne((request) => request.url.endsWith('/admin/settings'))
      .flush({
        sections: [
          {
            key: 'branding',
            isPublic: true,
            value: { storeName: 'Test shop', themeTokens: { '--kh-theme': 'coral' }, headerStyle: 'centred' },
          },
        ],
      });
    http
      .expectOne((request) => request.url.endsWith('/admin/pages') && request.params.get('type') === 'Home')
      .flush({ items: [{ id: 'page-1', slug: 'home', type: 'Home', status: 'Draft' }], page: { total: 1 } });
    http.expectOne((request) => request.url.endsWith('/admin/pages/page-1')).flush(loadedPage);
    fixture.detectChanges();

    // The first render does not wait out the debounce.
    tick(0);
    const first = previews();
    expect(first).toHaveLength(1);
    first[0].flush(rendered('Handwoven for the home'));
    fixture.detectChanges();
  };

  const select = () => {
    root().querySelector<HTMLButtonElement>('.blocks .pick')!.click();
    fixture.detectChanges();
  };

  const type = (value: string) => {
    const input = root().querySelector<HTMLInputElement>('input[id="b1-headline"]')!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  };

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval'] });
    await TestBed.configureTestingModule({
      imports: [StorefrontEditorPage],
      providers: [
        { provide: RUNTIME_CONFIG, useValue: config },
        provideKlaraHomeI18n(config.locale),
        provideKlaraHomeHttp(config),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('draws the home page draft in a frame per block, labelled, and starts clean', () => {
    open();

    const frame = root().querySelector<HTMLElement>('[data-block-id="b1"]')!;
    expect(frame.textContent).toContain('Hero: Handwoven for the home');
    expect(frame.textContent).toContain('Handwoven for the home');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('turns dirty on the first keystroke, and re-renders once for a burst of typing', () => {
    open();
    select();

    type('S');
    type('Sp');
    type('Spring');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);

    // Nothing goes out while typing continues...
    tick(PREVIEW_DELAY_MS - 1);
    expect(previews()).toHaveLength(0);

    // ...and one request, carrying the latest text, when it stops.
    tick(1);
    const sent = previews();
    expect(sent).toHaveLength(1);
    expect(sent[0].request.method).toBe('POST');
    expect(sent[0].request.body.blocks).toEqual([
      expect.objectContaining({ id: 'b1', type: 'Hero', config: { headline: 'Spring' } }),
    ]);

    sent[0].flush(rendered('Spring'));
    fixture.detectChanges();
    expect(root().querySelector('[data-block-id="b1"]')!.textContent).toContain('Spring');
  });

  it('is clean again when the text is put back', () => {
    open();
    select();

    type('Changed');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    type('Handwoven for the home');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('keeps the last picture and the draft when a preview is refused', () => {
    open();
    select();

    type('');
    tick(PREVIEW_DELAY_MS);
    const [sent] = previews();
    sent.flush(
      { title: 'Validation failed', status: 400, errors: { 'blocks[0].config.headline': ['Headline is required.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(root().textContent).toContain('The preview is paused');
    // The picture is the last good one, and the empty field is still the draft.
    expect(root().querySelector('[data-block-id="b1"]')!.textContent).toContain('Handwoven for the home');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('saves the page as loaded with only the blocks replaced, then is clean', () => {
    open();
    select();
    type('Spring');
    tick(PREVIEW_DELAY_MS);
    previews().forEach((request) => request.flush(rendered('Spring')));

    const save = Array.from(root().querySelectorAll<HTMLButtonElement>('button')).find((button) =>
      button.textContent?.includes('Save draft'),
    )!;
    expect(save.disabled).toBe(false);
    save.click();
    fixture.detectChanges();

    const put: TestRequest = http.expectOne(
      (request) => request.method === 'PUT' && request.url.endsWith('/admin/pages/page-1'),
    );
    expect(put.request.body.blocks).toEqual([
      expect.objectContaining({ id: 'b1', config: { headline: 'Spring' }, isVisible: true }),
    ]);
    // Everything the designer has no form for goes back as it came, SEO included.
    expect(put.request.body).toEqual(expect.objectContaining({ slug: 'home', title: 'Home', summary: 'Welcome', author: 'Ops', tags: ['a'] }));
    expect(put.request.body.seo).toEqual(
      expect.objectContaining({ ogTitle: 'Open graph title', ogImageFileId: 'og-1', noFollow: true, sitemapPriority: 0.8 }),
    );

    put.flush({
      ...loadedPage,
      version: 4,
      blocks: [{ ...loadedPage.blocks[0], config: { headline: 'Spring' } }],
    });
    fixture.detectChanges();
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('says so, and offers the way in, when there is no home page', () => {
    fixture = TestBed.createComponent(StorefrontEditorPage);
    fixture.detectChanges();

    http.expectOne((request) => request.url.endsWith('/pages/block-types')).flush([heroType]);
    http.expectOne((request) => request.url.endsWith('/admin/settings')).flush({ sections: [] });
    http
      .expectOne((request) => request.url.endsWith('/admin/pages') && request.params.get('type') === 'Home')
      .flush({ items: [], page: { total: 0 } });
    fixture.detectChanges();

    expect(root().textContent).toContain('There is no home page yet');
    expect(root().querySelector('a[href="/storefront/templates"]')).not.toBeNull();
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });
});

describe('saveBody', () => {
  it('maps the SEO response across whole, including the image as a file id', () => {
    const body = saveBody(
      {
        slug: 's',
        title: 't',
        summary: null,
        author: null,
        tags: [],
        coverImage: { fileId: 'cover' },
        seo: {
          metaTitle: 'm',
          metaDescription: null,
          metaKeywords: 'k',
          canonicalUrl: null,
          ogTitle: null,
          ogDescription: null,
          ogImage: null,
          ogType: null,
          noIndex: true,
          noFollow: false,
          sitemapPriority: null,
        },
      } as never,
      [],
    );

    expect(body.coverImageFileId).toBe('cover');
    expect(body.seo).toEqual(expect.objectContaining({ metaTitle: 'm', metaKeywords: 'k', noIndex: true, ogImageFileId: null }));
  });
});
