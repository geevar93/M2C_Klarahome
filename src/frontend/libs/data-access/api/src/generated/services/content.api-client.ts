/**
 * DO NOT EDIT. Generated from the API's OpenAPI document by tools/generate-api-client.mjs.
 *
 * Regenerate with:  pwsh tools/generate-api-client.ps1
 * CI fails if this file differs from what the current API produces.
 */
/* eslint-disable */

import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiRequestOptions, ApiTransport } from '../../runtime';
import type * as Models from '../models';

/** Query string for `adminGetStructuredData`. */
export interface AdminGetStructuredDataQuery {
  path?: string;
}

/** Query string for `adminListBanners`. */
export interface AdminListBannersQuery {
  placement?: Models.BannerPlacement;
  activeOnly?: boolean;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListCollectionItems`. */
export interface AdminListCollectionItemsQuery {
  cursor?: string;
  size?: number;
}

/** Query string for `adminListCollections`. */
export interface AdminListCollectionsQuery {
  search?: string;
  kind?: string;
  activeOnly?: boolean;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListMenus`. */
export interface AdminListMenusQuery {
  placement?: string;
}

/** Query string for `adminListPages`. */
export interface AdminListPagesQuery {
  search?: string;
  type?: Models.PageType;
  status?: Models.PageStatus;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListRedirects`. */
export interface AdminListRedirectsQuery {
  search?: string;
  activeOnly?: boolean;
  cursor?: string;
  size?: number;
}

/** Query string for `adminPreviewPage`. */
export interface AdminPreviewPageQuery {
  version?: number;
}

/** Query string for `storeGetBanners`. */
export interface StoreGetBannersQuery {
  placement?: string;
}

/** Query string for `storeGetCollection`. */
export interface StoreGetCollectionQuery {
  cursor?: string;
  size?: number;
}

/** Query string for `storeGetSitemapSection`. */
export interface StoreGetSitemapSectionQuery {
  page?: number;
}

/** Query string for `storeGetStructuredData`. */
export interface StoreGetStructuredDataQuery {
  path?: string;
}

/** Query string for `storeListBlogPosts`. */
export interface StoreListBlogPostsQuery {
  tag?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `storeResolveRedirect`. */
export interface StoreResolveRedirectQuery {
  path?: string;
}

/** `Content` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class ContentApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Adds one product to the end of the hand-picked membership, leaving the rest alone. A product already there is re-pinned rather than refused.
   * `POST /api/v1/admin/collections/{id}/items`
   */
  adminAddCollectionItem(id: string, body: Models.AddCollectionItemBody, options?: ApiRequestOptions): Observable<Models.CollectionResponse> {
    return this.http.request<Models.CollectionResponse>('POST', `${this.baseUrl}/api/v1/admin/collections/${encodeURIComponent(String(id))}/items`, body, undefined, options);
  }

  /**
   * Opens a banner.
   * `POST /api/v1/admin/banners`
   */
  adminCreateBanner(body: Models.BannerBody, options?: ApiRequestOptions): Observable<Models.BannerResponse> {
    return this.http.request<Models.BannerResponse>('POST', `${this.baseUrl}/api/v1/admin/banners`, body, undefined, options);
  }

  /**
   * Opens a collection, hand-picked to begin with.
   * `POST /api/v1/admin/collections`
   */
  adminCreateCollection(body: Models.CreateCollectionBody, options?: ApiRequestOptions): Observable<Models.CollectionResponse> {
    return this.http.request<Models.CollectionResponse>('POST', `${this.baseUrl}/api/v1/admin/collections`, body, undefined, options);
  }

  /**
   * Opens a menu.
   * `POST /api/v1/admin/menus`
   */
  adminCreateMenu(body: Models.CreateMenuBody, options?: ApiRequestOptions): Observable<Models.MenuResponse> {
    return this.http.request<Models.MenuResponse>('POST', `${this.baseUrl}/api/v1/admin/menus`, body, undefined, options);
  }

  /**
   * Opens a page, as a draft.
   * `POST /api/v1/admin/pages`
   */
  adminCreatePage(body: Models.CreatePageBody, options?: ApiRequestOptions): Observable<Models.PageResponse> {
    return this.http.request<Models.PageResponse>('POST', `${this.baseUrl}/api/v1/admin/pages`, body, undefined, options);
  }

  /**
   * Declares a redirect.
   * `POST /api/v1/admin/redirects`
   */
  adminCreateRedirect(body: Models.CreateRedirectBody, options?: ApiRequestOptions): Observable<Models.RedirectResponse> {
    return this.http.request<Models.RedirectResponse>('POST', `${this.baseUrl}/api/v1/admin/redirects`, body, undefined, options);
  }

  /**
   * Removes a banner.
   * `DELETE /api/v1/admin/banners/{id}`
   */
  adminDeleteBanner(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/banners/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Removes a collection.
   * `DELETE /api/v1/admin/collections/{id}`
   */
  adminDeleteCollection(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/collections/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Removes a menu.
   * `DELETE /api/v1/admin/menus/{id}`
   */
  adminDeleteMenu(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/menus/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Removes a page that has never been published. Archive the rest.
   * `DELETE /api/v1/admin/pages/{id}`
   */
  adminDeletePage(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/pages/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Removes a redirect.
   * `DELETE /api/v1/admin/redirects/{id}`
   */
  adminDeleteRedirect(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/redirects/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * One banner.
   * `GET /api/v1/admin/banners/{id}`
   */
  adminGetBanner(id: string, options?: ApiRequestOptions): Observable<Models.BannerResponse> {
    return this.http.request<Models.BannerResponse>('GET', `${this.baseUrl}/api/v1/admin/banners/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * One collection, with its rule.
   * `GET /api/v1/admin/collections/{id}`
   */
  adminGetCollection(id: string, options?: ApiRequestOptions): Observable<Models.CollectionResponse> {
    return this.http.request<Models.CollectionResponse>('GET', `${this.baseUrl}/api/v1/admin/collections/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * One menu, items and all.
   * `GET /api/v1/admin/menus/{id}`
   */
  adminGetMenu(id: string, options?: ApiRequestOptions): Observable<Models.MenuResponse> {
    return this.http.request<Models.MenuResponse>('GET', `${this.baseUrl}/api/v1/admin/menus/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * One page, blocks and all, with the moves this caller may make.
   * `GET /api/v1/admin/pages/{id}`
   */
  adminGetPage(id: string, options?: ApiRequestOptions): Observable<Models.PageResponse> {
    return this.http.request<Models.PageResponse>('GET', `${this.baseUrl}/api/v1/admin/pages/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * One version of a page, in full.
   * `GET /api/v1/admin/pages/{id}/versions/{version}`
   */
  adminGetPageVersion(id: string, version: number, options?: ApiRequestOptions): Observable<Models.PageVersionResponse> {
    return this.http.request<Models.PageVersionResponse>('GET', `${this.baseUrl}/api/v1/admin/pages/${encodeURIComponent(String(id))}/versions/${encodeURIComponent(String(version))}`, undefined, undefined, options);
  }

  /**
   * The robots document this deployment currently publishes.
   * `GET /api/v1/admin/seo/robots`
   */
  adminGetRobots(options?: ApiRequestOptions): Observable<string> {
    return this.http.request<string>('GET', `${this.baseUrl}/api/v1/admin/seo/robots`, undefined, undefined, options);
  }

  /**
   * Which sitemaps exist, and how many URLs each carries.
   * `GET /api/v1/admin/seo/sitemap`
   */
  adminGetSitemapIndex(options?: ApiRequestOptions): Observable<Models.SitemapIndexResponse> {
    return this.http.request<Models.SitemapIndexResponse>('GET', `${this.baseUrl}/api/v1/admin/seo/sitemap`, undefined, undefined, options);
  }

  /**
   * The schema.org graph a crawler is served for one path.
   * `GET /api/v1/admin/seo/structured-data`
   */
  adminGetStructuredData(query?: AdminGetStructuredDataQuery, options?: ApiRequestOptions): Observable<Models.StructuredDataResponse> {
    return this.http.request<Models.StructuredDataResponse>('GET', `${this.baseUrl}/api/v1/admin/seo/structured-data`, undefined, query, options);
  }

  /**
   * The store's banners, newest first.
   * `GET /api/v1/admin/banners`
   */
  adminListBanners(query?: AdminListBannersQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfBannerResponse> {
    return this.http.request<Models.PagedResultOfBannerResponse>('GET', `${this.baseUrl}/api/v1/admin/banners`, undefined, query, options);
  }

  /**
   * The block types this storefront renders, with their schemas.
   * `GET /api/v1/admin/pages/block-types`
   */
  adminListBlockTypes(options?: ApiRequestOptions): Observable<Models.BlockTypeResponse[]> {
    return this.http.request<Models.BlockTypeResponse[]>('GET', `${this.baseUrl}/api/v1/admin/pages/block-types`, undefined, undefined, options);
  }

  /**
   * What is in a collection, in its own order.
   * `GET /api/v1/admin/collections/{id}/items`
   */
  adminListCollectionItems(id: string, query?: AdminListCollectionItemsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfProductCardResponse> {
    return this.http.request<Models.PagedResultOfProductCardResponse>('GET', `${this.baseUrl}/api/v1/admin/collections/${encodeURIComponent(String(id))}/items`, undefined, query, options);
  }

  /**
   * The store's collections, newest first.
   * `GET /api/v1/admin/collections`
   */
  adminListCollections(query?: AdminListCollectionsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfCollectionSummaryResponse> {
    return this.http.request<Models.PagedResultOfCollectionSummaryResponse>('GET', `${this.baseUrl}/api/v1/admin/collections`, undefined, query, options);
  }

  /**
   * The store's menus.
   * `GET /api/v1/admin/menus`
   */
  adminListMenus(query?: AdminListMenusQuery, options?: ApiRequestOptions): Observable<Models.MenuSummaryResponse[]> {
    return this.http.request<Models.MenuSummaryResponse[]>('GET', `${this.baseUrl}/api/v1/admin/menus`, undefined, query, options);
  }

  /**
   * The pages an editor can work on, newest first.
   * `GET /api/v1/admin/pages`
   */
  adminListPages(query?: AdminListPagesQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfPageSummaryResponse> {
    return this.http.request<Models.PagedResultOfPageSummaryResponse>('GET', `${this.baseUrl}/api/v1/admin/pages`, undefined, query, options);
  }

  /**
   * A page's version history, newest first.
   * `GET /api/v1/admin/pages/{id}/versions`
   */
  adminListPageVersions(id: string, options?: ApiRequestOptions): Observable<Models.PageVersionSummaryResponse[]> {
    return this.http.request<Models.PageVersionSummaryResponse[]>('GET', `${this.baseUrl}/api/v1/admin/pages/${encodeURIComponent(String(id))}/versions`, undefined, undefined, options);
  }

  /**
   * The redirect rules, by path.
   * `GET /api/v1/admin/redirects`
   */
  adminListRedirects(query?: AdminListRedirectsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfRedirectResponse> {
    return this.http.request<Models.PagedResultOfRedirectResponse>('GET', `${this.baseUrl}/api/v1/admin/redirects`, undefined, query, options);
  }

  /**
   * Renders a page exactly as the storefront would, whatever its status.
   * `GET /api/v1/admin/pages/{id}/preview`
   */
  adminPreviewPage(id: string, query?: AdminPreviewPageQuery, options?: ApiRequestOptions): Observable<Models.StorePageResponse> {
    return this.http.request<Models.StorePageResponse>('GET', `${this.baseUrl}/api/v1/admin/pages/${encodeURIComponent(String(id))}/preview`, undefined, query, options);
  }

  /**
   * Evaluates a rule now, rather than waiting for the sweep.
   * `POST /api/v1/admin/collections/{id}/refresh`
   */
  adminRefreshCollection(id: string, options?: ApiRequestOptions): Observable<Models.CollectionResponse> {
    return this.http.request<Models.CollectionResponse>('POST', `${this.baseUrl}/api/v1/admin/collections/${encodeURIComponent(String(id))}/refresh`, undefined, undefined, options);
  }

  /**
   * Takes one product out of the hand-picked membership. A row the rule put there is refused: it would come back on the next refresh.
   * `DELETE /api/v1/admin/collections/{id}/items/{productId}`
   */
  adminRemoveCollectionItem(id: string, productId: string, options?: ApiRequestOptions): Observable<Models.CollectionResponse> {
    return this.http.request<Models.CollectionResponse>('DELETE', `${this.baseUrl}/api/v1/admin/collections/${encodeURIComponent(String(id))}/items/${encodeURIComponent(String(productId))}`, undefined, undefined, options);
  }

  /**
   * Restores a version's content, as a new version. Never changes the status.
   * `POST /api/v1/admin/pages/{id}/versions/{version}/rollback`
   */
  adminRollbackPage(id: string, version: number, options?: ApiRequestOptions): Observable<Models.PageResponse> {
    return this.http.request<Models.PageResponse>('POST', `${this.baseUrl}/api/v1/admin/pages/${encodeURIComponent(String(id))}/versions/${encodeURIComponent(String(version))}/rollback`, undefined, undefined, options);
  }

  /**
   * Switches a banner on or off without touching its schedule.
   * `POST /api/v1/admin/banners/{id}/active`
   */
  adminSetBannerActive(id: string, body: Models.SetBannerActiveBody, options?: ApiRequestOptions): Observable<Models.BannerResponse> {
    return this.http.request<Models.BannerResponse>('POST', `${this.baseUrl}/api/v1/admin/banners/${encodeURIComponent(String(id))}/active`, body, undefined, options);
  }

  /**
   * Replaces the hand-picked membership in one ordered list. This is how a collection is reordered; adding or removing a single product has its own routes, because sending back only the rows a screen has loaded would delete the rest.
   * `PUT /api/v1/admin/collections/{id}/items`
   */
  adminSetCollectionItems(id: string, body: Models.SetCollectionItemsBody, options?: ApiRequestOptions): Observable<Models.CollectionResponse> {
    return this.http.request<Models.CollectionResponse>('PUT', `${this.baseUrl}/api/v1/admin/collections/${encodeURIComponent(String(id))}/items`, body, undefined, options);
  }

  /**
   * Writes or clears a collection's rule, and evaluates it at once.
   * `PUT /api/v1/admin/collections/{id}/rule`
   */
  adminSetCollectionRule(id: string, body: Models.SetCollectionRuleBody, options?: ApiRequestOptions): Observable<Models.CollectionResponse> {
    return this.http.request<Models.CollectionResponse>('PUT', `${this.baseUrl}/api/v1/admin/collections/${encodeURIComponent(String(id))}/rule`, body, undefined, options);
  }

  /**
   * Submits, publishes, schedules, unpublishes or archives a page.
   * `POST /api/v1/admin/pages/{id}/transition`
   */
  adminTransitionPage(id: string, body: Models.TransitionPageBody, options?: ApiRequestOptions): Observable<Models.PageResponse> {
    return this.http.request<Models.PageResponse>('POST', `${this.baseUrl}/api/v1/admin/pages/${encodeURIComponent(String(id))}/transition`, body, undefined, options);
  }

  /**
   * Rewrites a banner. Its placement is not editable.
   * `PUT /api/v1/admin/banners/{id}`
   */
  adminUpdateBanner(id: string, body: Models.BannerBody, options?: ApiRequestOptions): Observable<Models.BannerResponse> {
    return this.http.request<Models.BannerResponse>('PUT', `${this.baseUrl}/api/v1/admin/banners/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Rewrites a collection's details.
   * `PUT /api/v1/admin/collections/{id}`
   */
  adminUpdateCollection(id: string, body: Models.UpdateCollectionBody, options?: ApiRequestOptions): Observable<Models.CollectionResponse> {
    return this.http.request<Models.CollectionResponse>('PUT', `${this.baseUrl}/api/v1/admin/collections/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Rewrites a menu and its whole tree.
   * `PUT /api/v1/admin/menus/{id}`
   */
  adminUpdateMenu(id: string, body: Models.UpdateMenuBody, options?: ApiRequestOptions): Observable<Models.MenuResponse> {
    return this.http.request<Models.MenuResponse>('PUT', `${this.baseUrl}/api/v1/admin/menus/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Rewrites a page. A save is never a publish.
   * `PUT /api/v1/admin/pages/{id}`
   */
  adminUpdatePage(id: string, body: Models.UpdatePageBody, options?: ApiRequestOptions): Observable<Models.PageResponse> {
    return this.http.request<Models.PageResponse>('PUT', `${this.baseUrl}/api/v1/admin/pages/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Rewrites a redirect's destination. The path it matches is not editable.
   * `PUT /api/v1/admin/redirects/{id}`
   */
  adminUpdateRedirect(id: string, body: Models.UpdateRedirectBody, options?: ApiRequestOptions): Observable<Models.RedirectResponse> {
    return this.http.request<Models.RedirectResponse>('PUT', `${this.baseUrl}/api/v1/admin/redirects/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * The banners live in a placement right now, best first.
   * `GET /api/v1/store/content/banners`
   */
  storeGetBanners(query?: StoreGetBannersQuery, options?: ApiRequestOptions): Observable<Models.StoreBannerResponse[]> {
    return this.http.request<Models.StoreBannerResponse[]>('GET', `${this.baseUrl}/api/v1/store/content/banners`, undefined, query, options);
  }

  /**
   * One published post.
   * `GET /api/v1/store/content/blog/{slug}`
   */
  storeGetBlogPost(slug: string, options?: ApiRequestOptions): Observable<Models.StorePageResponse> {
    return this.http.request<Models.StorePageResponse>('GET', `${this.baseUrl}/api/v1/store/content/blog/${encodeURIComponent(String(slug))}`, undefined, undefined, options);
  }

  /**
   * A curated collection's landing page and a page of its products.
   * `GET /api/v1/store/collections/{slug}`
   */
  storeGetCollection(slug: string, query?: StoreGetCollectionQuery, options?: ApiRequestOptions): Observable<Models.StoreCollectionResponse> {
    return this.http.request<Models.StoreCollectionResponse>('GET', `${this.baseUrl}/api/v1/store/collections/${encodeURIComponent(String(slug))}`, undefined, query, options);
  }

  /**
   * The published home page, blocks resolved and windowed to now.
   * `GET /api/v1/store/content/home`
   */
  storeGetHomePage(options?: ApiRequestOptions): Observable<Models.StorePageResponse> {
    return this.http.request<Models.StorePageResponse>('GET', `${this.baseUrl}/api/v1/store/content/home`, undefined, undefined, options);
  }

  /**
   * One menu, nested, with every target resolved into a path.
   * `GET /api/v1/store/content/menus/{code}`
   */
  storeGetMenu(code: string, options?: ApiRequestOptions): Observable<Models.StoreMenuResponse> {
    return this.http.request<Models.StoreMenuResponse>('GET', `${this.baseUrl}/api/v1/store/content/menus/${encodeURIComponent(String(code))}`, undefined, undefined, options);
  }

  /**
   * One published page, blocks resolved and windowed to now.
   * `GET /api/v1/store/content/pages/{slug}`
   */
  storeGetPage(slug: string, options?: ApiRequestOptions): Observable<Models.StorePageResponse> {
    return this.http.request<Models.StorePageResponse>('GET', `${this.baseUrl}/api/v1/store/content/pages/${encodeURIComponent(String(slug))}`, undefined, undefined, options);
  }

  /**
   * The robots document, ready to serve as text/plain.
   * `GET /api/v1/store/content/seo/robots`
   */
  storeGetRobots(options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('GET', `${this.baseUrl}/api/v1/store/content/seo/robots`, undefined, undefined, options);
  }

  /**
   * The canonical origin, the title template and the indexing switch.
   * `GET /api/v1/store/content/seo/config`
   */
  storeGetSeoConfig(options?: ApiRequestOptions): Observable<Models.SeoConfigResponse> {
    return this.http.request<Models.SeoConfigResponse>('GET', `${this.baseUrl}/api/v1/store/content/seo/config`, undefined, undefined, options);
  }

  /**
   * The sitemap index: which sitemaps exist, and where.
   * `GET /api/v1/store/content/seo/sitemap`
   */
  storeGetSitemapIndex(options?: ApiRequestOptions): Observable<Models.SitemapIndexResponse> {
    return this.http.request<Models.SitemapIndexResponse>('GET', `${this.baseUrl}/api/v1/store/content/seo/sitemap`, undefined, undefined, options);
  }

  /**
   * One page of one sitemap section.
   * `GET /api/v1/store/content/seo/sitemap/{section}`
   */
  storeGetSitemapSection(section: string, query?: StoreGetSitemapSectionQuery, options?: ApiRequestOptions): Observable<Models.SitemapPageResponse> {
    return this.http.request<Models.SitemapPageResponse>('GET', `${this.baseUrl}/api/v1/store/content/seo/sitemap/${encodeURIComponent(String(section))}`, undefined, query, options);
  }

  /**
   * The schema.org graph for one path, ready for a script tag.
   * `GET /api/v1/store/content/seo/structured-data`
   */
  storeGetStructuredData(query?: StoreGetStructuredDataQuery, options?: ApiRequestOptions): Observable<Models.StructuredDataResponse> {
    return this.http.request<Models.StructuredDataResponse>('GET', `${this.baseUrl}/api/v1/store/content/seo/structured-data`, undefined, query, options);
  }

  /**
   * The published posts, newest first.
   * `GET /api/v1/store/content/blog`
   */
  storeListBlogPosts(query?: StoreListBlogPostsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfBlogCardResponse> {
    return this.http.request<Models.PagedResultOfBlogCardResponse>('GET', `${this.baseUrl}/api/v1/store/content/blog`, undefined, query, options);
  }

  /**
   * What to do with a path nothing else claims: 301, 302 or 410.
   * `GET /api/v1/store/content/redirects/resolve`
   */
  storeResolveRedirect(query?: StoreResolveRedirectQuery, options?: ApiRequestOptions): Observable<Models.RedirectResolutionResponse> {
    return this.http.request<Models.RedirectResolutionResponse>('GET', `${this.baseUrl}/api/v1/store/content/redirects/resolve`, undefined, query, options);
  }
}
