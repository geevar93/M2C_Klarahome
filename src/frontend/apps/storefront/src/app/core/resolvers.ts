import { inject } from '@angular/core';
import { RedirectCommand, ResolveFn, Router } from '@angular/router';
import { isApiError } from '@klarahome/data-access-auth';
import {
  CategoryResponse,
  StoreCatalogService,
  StorefrontProduct,
  StorefrontVendorResponse,
} from '@klarahome/data-access-catalog';
import {
  StoreCollectionResponse,
  StoreContentService,
  StorePageResponse,
} from '@klarahome/data-access-content';
import { Observable, catchError, of } from 'rxjs';

/**
 * Route-level data loading.
 *
 * A resolver rather than a fetch in `ngOnInit` for the pages whose content *is* the page: the
 * router does not activate the route until the data is there, so the server renders HTML with the
 * content in it and there is no flash of an empty page on the client. A page that loads its own
 * data after activation server-renders as a skeleton, which is exactly what a crawler would
 * index.
 *
 * The rule for the rest of the storefront: **resolve what the page is, load what the page shows.**
 * A product's identity and its price are resolved; its reviews and recommendations are not, and
 * arrive behind `@defer` when they are scrolled to (Step 24).
 */

/** Sends the navigation to the 404 page. See `notFound` for why this is a redirect. */
function notFound(): RedirectCommand {
  return new RedirectCommand(inject(Router).parseUrl('/404'));
}

/**
 * The CMS page behind `/pages/:slug`.
 *
 * A slug that does not resolve **redirects** rather than rendering a not-found page in place. It
 * costs one extra round trip and buys the only honest status code available to us: `/404` is
 * declared with `status: 404` in the server routes, so a crawler that follows the redirect is
 * told the page is gone. Rendering the same component under the original URL would answer 200 —
 * a soft 404, which is how thousands of dead URLs stay in an index for months.
 */
export const cmsPageResolver: ResolveFn<StorePageResponse | RedirectCommand> = (
  route,
): Observable<StorePageResponse | RedirectCommand> => {
  const slug = route.paramMap.get('slug') ?? '';
  const redirect = notFound();

  if (!slug) return of(redirect);

  return inject(StoreContentService)
    .page(slug)
    .pipe(
      catchError((error: unknown) => {
        // A 404 is the page being absent; anything else is the API being unwell. Both end up on
        // the same screen for the visitor, but only the second is worth a log line, and the
        // interceptor has already written one.
        if (isApiError(error) && error.status !== 404) console.warn('[cms] page load failed', error.code);
        return of(redirect);
      }),
    );
};

/**
 * The product behind `/p/:productSlug`.
 *
 * Resolved rather than fetched in the component, and this is the page where that matters most: the
 * PDP is the storefront's most-indexed page, and its `Product` structured data, its title and its
 * price have to be in the HTML the crawler receives rather than added a tick later. A component
 * that fetched its own product would server-render a skeleton.
 *
 * What is **not** resolved: the offers, the reviews, the questions and the recommendations. They
 * are below the fold, they arrive behind `@defer`, and blocking the navigation on four more
 * requests would trade the thing the resolver was for.
 */
export const productResolver: ResolveFn<StorefrontProduct | RedirectCommand> = (
  route,
): Observable<StorefrontProduct | RedirectCommand> => {
  const slug = route.paramMap.get('productSlug') ?? '';
  const redirect = notFound();

  if (!slug) return of(redirect);

  return inject(StoreCatalogService)
    .product(slug)
    .pipe(
      catchError((error: unknown) => {
        if (isApiError(error) && error.status !== 404) console.warn('[pdp] product load failed', error.code);
        return of(redirect);
      }),
    );
};

/**
 * The category behind `/c/:categorySlug`.
 *
 * The category itself is resolved because the page's title, description and breadcrumb are its
 * name — the *products* are not, because the listing re-queries on every filter change and a
 * resolver would make each of those a blocking navigation.
 */
export const categoryResolver: ResolveFn<CategoryResponse | RedirectCommand> = (
  route,
): Observable<CategoryResponse | RedirectCommand> => {
  const slug = route.paramMap.get('categorySlug') ?? '';
  const redirect = notFound();

  if (!slug) return of(redirect);

  return inject(StoreCatalogService)
    .category(slug)
    .pipe(
      catchError((error: unknown) => {
        if (isApiError(error) && error.status !== 404) console.warn('[plp] category load failed', error.code);
        return of(redirect);
      }),
    );
};

/** The curated collection behind `/collections/:slug`, with its first page of products. */
export const collectionResolver: ResolveFn<StoreCollectionResponse | RedirectCommand> = (
  route,
): Observable<StoreCollectionResponse | RedirectCommand> => {
  const slug = route.paramMap.get('slug') ?? '';
  const redirect = notFound();

  if (!slug) return of(redirect);

  return inject(StoreContentService)
    .collection(slug)
    .pipe(
      catchError((error: unknown) => {
        if (isApiError(error) && error.status !== 404) console.warn('[collection] load failed', error.code);
        return of(redirect);
      }),
    );
};

/**
 * The home page's block document.
 *
 * A home page whose CMS document is missing is **not** a 404 — it is a shop with an empty front
 * page, which still has a header, a footer and a working search box. So this resolves to `null`
 * rather than redirecting, and the page renders its own honest empty state.
 */
export const homePageResolver: ResolveFn<
  StorePageResponse | null
> = (): Observable<StorePageResponse | null> =>
  inject(StoreContentService)
    .homePage()
    .pipe(catchError(() => of(null)));

/** The seller storefront behind `/vendor/:slug`. */
export const vendorResolver: ResolveFn<StorefrontVendorResponse | RedirectCommand> = (
  route,
): Observable<StorefrontVendorResponse | RedirectCommand> => {
  const slug = route.paramMap.get('slug') ?? '';
  const redirect = notFound();

  if (!slug) return of(redirect);

  return inject(StoreCatalogService)
    .vendor(slug)
    .pipe(catchError(() => of(redirect)));
};
