import { Injectable, inject } from '@angular/core';
import {
  CatalogApiClient,
  CategoryNode,
  CategoryResponse,
  StorefrontOffer,
  StorefrontProduct,
  VendorsApiClient,
  StorefrontVendorResponse,
} from '@klarahome/data-access-api';
import { Observable, catchError, of, shareReplay } from 'rxjs';

/**
 * The storefront's read side of the catalogue.
 *
 * The split between this and `ProductSearchService` follows the API's own: a *product* is read
 * from the catalogue by slug, while a *list of products* is read from the search projection, which
 * is a denormalised row per variant with its facet counts. Two services rather than one, because
 * they answer different questions and because pretending otherwise is how a PDP ends up rendering
 * a search row that has half the fields it needs.
 *
 * The category tree is cached for the life of the page: it changes about once a month, every
 * listing page needs it for its breadcrumb, and re-fetching it per navigation is a request that
 * buys nothing. A product is not cached — its price and its buy box are the two most volatile
 * things in the shop.
 */
@Injectable({ providedIn: 'root' })
export class StoreCatalogService {
  private readonly api = inject(CatalogApiClient);
  private readonly vendors = inject(VendorsApiClient);
  private tree?: Observable<CategoryNode[]>;

  /**
   * The whole active category tree.
   *
   * A single read rather than one per level: the taxonomy is a materialised path (Step 10) and the
   * API answers the whole tree in one document, so walking it client-side to find a slug's
   * ancestors is cheaper than the four requests the alternative costs.
   */
  categories(): Observable<CategoryNode[]> {
    this.tree ??= this.api.storeCategories(undefined, { silentErrors: true }).pipe(
      // A shop with no navigation tree is degraded, not broken: the listing pages still work from
      // their own slugs.
      catchError(() => of<CategoryNode[]>([])),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.tree;
  }

  /** One category by slug. Errors propagate — the route turns a 404 into a real 404. */
  category(slug: string): Observable<CategoryResponse> {
    return this.api.storeCategoryBySlug(slug, { silentErrors: true });
  }

  /** A product and all of its variants, each with the offer that won its buy box. */
  product(slug: string): Observable<StorefrontProduct> {
    return this.api.storeProductBySlug(slug, { silentErrors: true });
  }

  /**
   * Every seller's offer on one variant.
   *
   * Loaded separately from the product and only when the "other sellers" section is opened: a
   * product with fourteen sellers would otherwise put fourteen offers into the server-rendered
   * HTML of a page that shows one.
   */
  offers(slug: string, variantId: string): Observable<StorefrontOffer[]> {
    return this.api.storeProductOffers(slug, { variantId }, { silentErrors: true });
  }

  /** A seller's storefront page. */
  vendor(slug: string): Observable<StorefrontVendorResponse> {
    return this.vendors.storeVendorGet(slug, { silentErrors: true });
  }
}
