import { RenderMode, ServerRoute } from '@angular/ssr';

/**
 * How each route is rendered, and it is a different decision for each kind of page
 * (docs/05-frontend-architecture.md §3.1).
 *
 * **Server** for everything a crawler or a first-time visitor sees: home, category, collection,
 * product, search and the CMS pages. These are the pages whose HTML has to arrive complete —
 * the LCP budget of 2.5 s on a 4G connection is not reachable if the browser has to boot a
 * framework and then make two requests before there is anything to look at.
 *
 * **Client** for everything personal: cart, checkout, account, and the sign-in screens. There is
 * nothing to index, the content is per-customer, and rendering it on the server means the SSR
 * process holds a customer's data and its cache has to be reasoned about — a cost with no benefit
 * (and, in the case of a shared cache, a hazard).
 *
 * **Nothing is prerendered.** A prerender runs at build time, where there is no API and no runtime
 * configuration, so a prerendered page ships whatever the build machine failed to fetch. Even the
 * error pages are left server-rendered so they carry the same header and footer as the rest of the
 * site, which come from the CMS.
 *
 * A route not listed here is not rendered — Angular checks every application route against this
 * table and refuses to build if one is uncovered. That is the property that makes the catch-all
 * below safe: `**` genuinely means "no known route matched", so it can answer 404.
 */
export const serverRoutes: ServerRoute[] = [
  // ---- Indexable ------------------------------------------------------------------------------
  { path: '', renderMode: RenderMode.Server },
  { path: 'c/:categorySlug', renderMode: RenderMode.Server },
  { path: 'collections/:slug', renderMode: RenderMode.Server },
  { path: 'p/:productSlug', renderMode: RenderMode.Server },
  { path: 'vendor/:slug', renderMode: RenderMode.Server },
  { path: 'pages/:slug', renderMode: RenderMode.Server },
  { path: 'blog', renderMode: RenderMode.Server },
  { path: 'blog/:slug', renderMode: RenderMode.Server },

  // The first page of results is server-rendered so a shared link opens on content; the paging
  // and facet changes after it are client-side (§3.1).
  { path: 'search', renderMode: RenderMode.Server },

  // ---- Personal: rendered in the browser ------------------------------------------------------
  { path: 'cart', renderMode: RenderMode.Client },
  { path: 'checkout', renderMode: RenderMode.Client },
  { path: 'checkout/confirmation/:orderNumber', renderMode: RenderMode.Client },
  { path: 'account', renderMode: RenderMode.Client },
  { path: 'account/**', renderMode: RenderMode.Client },
  { path: 'auth/**', renderMode: RenderMode.Client },

  // ---- States ---------------------------------------------------------------------------------
  { path: '403', renderMode: RenderMode.Server, status: 403 },
  { path: '404', renderMode: RenderMode.Server, status: 404 },
  { path: '500', renderMode: RenderMode.Server, status: 500 },
  // The offline page is the one thing a service worker serves when there is no network, so it has
  // to be cacheable and identical for everyone. It is still SSR rather than prerendered, for the
  // reason above; Step 31 caches it in the worker.
  { path: 'offline', renderMode: RenderMode.Server },

  /**
   * Everything else. **404, not 200.**
   *
   * A storefront that answers 200 with a "page not found" body — a soft 404 — keeps every dead
   * URL in the index and spends its crawl budget on them. Because every real route above is
   * enumerated, anything reaching here is genuinely unknown, and saying so is both honest and
   * cheap.
   */
  { path: '**', renderMode: RenderMode.Server, status: 404 },
];
