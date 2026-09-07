import { Injectable, inject } from '@angular/core';
import {
  BannerPlacement,
  ContentApiClient,
  SeoConfigResponse,
  StoreBannerResponse,
  StoreCollectionResponse,
  StoreMenuResponse,
  StorePageResponse,
} from '@klarahome/data-access-api';
import { Observable, catchError, of, shareReplay } from 'rxjs';

/**
 * The storefront's read side of the CMS.
 *
 * Two policies live here rather than in each caller:
 *
 *  1. **The shell's reads are cached for the life of the page.** A menu is fetched once, not once
 *     per navigation, and `shareReplay` makes the second subscriber cheap. Combined with the
 *     transfer cache, a menu rendered on the server is not fetched again on hydration at all.
 *  2. **The shell's reads never fail loudly.** A header whose menu 404s is a degraded header; a
 *     header that throws is a blank shop. The menu calls answer an empty menu and suppress the
 *     toast — `silentErrors` — because there is nothing the shopper can do about it. Page reads
 *     do *not* do this: a CMS page that is missing is a 404, and the route has to know.
 */
@Injectable({ providedIn: 'root' })
export class StoreContentService {
  private readonly api = inject(ContentApiClient);
  private readonly menus = new Map<string, Observable<StoreMenuResponse>>();
  private readonly bannersByPlacement = new Map<string, Observable<StoreBannerResponse[]>>();
  private seo?: Observable<SeoConfigResponse>;

  /** The menu with this code, or an empty one. Cached per code. */
  menu(code: string): Observable<StoreMenuResponse> {
    const cached = this.menus.get(code);
    if (cached) return cached;

    const request = this.api.storeGetMenu(code, { silentErrors: true }).pipe(
      catchError(() => of<StoreMenuResponse>({ code, name: code, placement: null, items: [] })),
      // `refCount: false`: the replay outlives the last subscriber, which is the whole point —
      // the header unsubscribing between navigations must not cause a refetch.
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    this.menus.set(code, request);
    return request;
  }

  /**
   * The site-wide SEO configuration — canonical origin, title template, whether this deployment
   * may be indexed. Fetched once, because it is a property of the deployment and not of a page.
   */
  seoConfig(): Observable<SeoConfigResponse> {
    this.seo ??= this.api.storeGetSeoConfig({ silentErrors: true }).pipe(
      catchError(() =>
        of<SeoConfigResponse>({
          canonicalBaseUrl: '',
          // Refusing to be indexed is the safe failure: a deployment whose SEO configuration
          // could not be read is one we know nothing about, and an unwanted `noindex` costs a
          // day while an unwanted `index` on a staging host costs months.
          allowIndexing: false,
          titleTemplate: '{title}',
          defaultMetaDescription: '',
          twitterCardType: 'summary_large_image',
          robotsUrl: '',
          sitemapUrl: '',
        }),
      ),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.seo;
  }

  /** A published CMS page by slug. Errors propagate: the route decides what a 404 means. */
  page(slug: string): Observable<StorePageResponse> {
    return this.api.storeGetPage(slug, { silentErrors: true });
  }

  /** The home page's block document. */
  homePage(): Observable<StorePageResponse> {
    return this.api.storeGetHomePage({ silentErrors: true });
  }

  /**
   * A curated collection and its first page of products.
   *
   * A collection is merchandised rather than searched — its members are rows a rule materialised
   * or a merchandiser pinned (Step 20) — so it is read from the CMS and not from the search
   * projection, and it carries no facets. Errors propagate: a slug that does not resolve is a 404
   * the route has to answer honestly.
   */
  collection(slug: string, cursor?: string, size?: number): Observable<StoreCollectionResponse> {
    return this.api.storeGetCollection(slug, { cursor, size }, { silentErrors: true });
  }

  /**
   * The live banners for one placement, highest priority first.
   *
   * Cached per placement for the life of the page, on the shell's own policy: the announcement bar
   * is on every page and re-fetching it per navigation would be a request per click for a strip of
   * text that changes weekly. A failure answers an empty list and no toast — a banner is
   * merchandising, and a shop with one fewer promotion is a shop, whereas a shop that throws is not.
   *
   * Built at Step 20 and rendered nowhere until Step 28B: three storefront steps each named the
   * next one as the owner (deliverable 19).
   */
  banners(placement: BannerPlacement): Observable<StoreBannerResponse[]> {
    const cached = this.bannersByPlacement.get(placement);
    if (cached) return cached;

    const request = this.api.storeGetBanners({ placement }, { silentErrors: true }).pipe(
      catchError(() => of<StoreBannerResponse[]>([])),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    this.bannersByPlacement.set(placement, request);
    return request;
  }
}
