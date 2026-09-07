import { HttpRequest } from '@angular/common/http';

/**
 * Which server-rendered responses are carried into the browser.
 *
 * Without a transfer cache, every request the server made is made again by the browser the moment
 * it hydrates: the menus, the store configuration and the page's own content, all fetched twice —
 * once for the HTML and once for the app that was rendered from it. On a 4G connection that is
 * the difference between a page that is interactive and one that flickers.
 *
 * **This is an allow-list, and that is the whole design.** Angular's default is to skip any
 * request carrying credentials, and every call this application makes carries them (`ApiTransport`
 * sets `withCredentials` so the refresh cookie reaches the API's origin). Turning that exclusion
 * off is what makes a transfer cache possible here at all — and it is only safe because nothing
 * personal is on the list below.
 *
 * The test to apply before adding a prefix: **if this response were embedded in an HTML page that
 * an edge cache served to somebody else, would anything be wrong?** A category listing, no. A
 * basket, a wishlist, an order — obviously yes. Step 32 puts a cache in front of the SSR process;
 * this list is what keeps that safe rather than a comment asking someone to remember.
 */
const PUBLIC_PREFIXES = [
  '/api/v1/store/categories',
  '/api/v1/store/brands',
  '/api/v1/store/collections',
  '/api/v1/store/products',
  '/api/v1/store/vendors',
  '/api/v1/store/search',
  '/api/v1/store/config',
  '/api/v1/store/states',
  '/api/v1/store/pincodes',
  '/api/v1/store/content/menus',
  '/api/v1/store/content/pages',
  '/api/v1/store/content/home',
  '/api/v1/store/content/collections',
  '/api/v1/store/content/seo',
  '/api/v1/store/questions',
  '/api/v1/store/reviews',
] as const;

/**
 * `/store/content/banners` is deliberately absent.
 *
 * It is the one read on the storefront's content surface whose answer differs for a signed-in
 * visitor, which makes it exactly the response that must not travel inside a cacheable document
 * (recorded against Step 20 in `PARKING_LOT.md`). It costs one request on hydration; that is the
 * right price.
 */
export function isTransferCacheable(request: HttpRequest<unknown>): boolean {
  if (request.method !== 'GET') return false;

  // A relative URL cannot be parsed without a base, and the API's origin is not this app's.
  const path = pathOf(request.url);
  return PUBLIC_PREFIXES.some((prefix) => path === prefix || path.startsWith(`${prefix}/`));
}

function pathOf(url: string): string {
  try {
    return new URL(url, 'http://placeholder.invalid').pathname;
  } catch {
    return '';
  }
}
