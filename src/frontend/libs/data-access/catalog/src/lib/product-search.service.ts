import { Injectable, inject } from '@angular/core';
import { ProductSearchResponse, SearchApiClient, SuggestionResponse } from '@klarahome/data-access-api';
import { Observable, catchError, of } from 'rxjs';

/**
 * Everything the faceted listing endpoint accepts.
 *
 * A typed record rather than a bag of strings, because this shape is the contract between the
 * URL (which the page owns), the request (which this service builds) and the facet panel (which
 * reads back what is selected). All three would otherwise agree by convention.
 *
 * `attributes` is the open half: `{ colour: ['beige'], size: ['l', 'xl'] }` becomes
 * `?attr.colour=beige&attr.size=l&attr.size=xl`. Which attributes exist is a merchandising
 * decision, so no closed type can enumerate them.
 */
export interface ProductSearchParams {
  readonly q?: string | null;
  readonly category?: string | null;
  readonly brand?: readonly string[];
  readonly vendor?: readonly string[];
  readonly minPrice?: number | null;
  readonly maxPrice?: number | null;
  /** The minimum average rating — "4 and above". */
  readonly rating?: number | null;
  /** The minimum discount percentage. */
  readonly discount?: number | null;
  readonly inStock?: boolean | null;
  readonly attributes?: Readonly<Record<string, readonly string[]>>;
  readonly sort?: string | null;
  /** Keyset cursor from the previous page's `nextCursor`. There are no page numbers. */
  readonly cursor?: string | null;
  readonly size?: number | null;
}

/** The empty result, used when the search service is unreachable. */
const NO_RESULTS: ProductSearchResponse = {
  items: [],
  total: 0,
  nextCursor: null,
  size: 0,
  facets: [],
  query: null,
  corrected: false,
  queryToken: null,
};

/**
 * Turns the parameters into the query string the endpoint reads.
 *
 * Exported and pure, because it is the one piece of this service worth testing on its own and
 * because the page needs the same mapping to write the URL it is about to navigate to.
 *
 * Empty values are dropped rather than sent blank: `?q=` is a search for the empty string as far
 * as the API's model binder is concerned, which is not the same as browsing a category.
 */
export function toSearchQuery(
  params: ProductSearchParams,
): Record<string, string | number | boolean | readonly string[]> {
  const query: Record<string, string | number | boolean | readonly string[]> = {};

  if (params.q?.trim()) query['q'] = params.q.trim();
  if (params.category) query['category'] = params.category;
  if (params.brand?.length) query['brand'] = [...params.brand];
  if (params.vendor?.length) query['vendor'] = [...params.vendor];
  if (params.minPrice !== null && params.minPrice !== undefined) query['minPrice'] = params.minPrice;
  if (params.maxPrice !== null && params.maxPrice !== undefined) query['maxPrice'] = params.maxPrice;
  if (params.rating) query['rating'] = params.rating;
  if (params.discount) query['discount'] = params.discount;
  if (params.inStock) query['inStock'] = true;
  if (params.sort) query['sort'] = params.sort;
  if (params.cursor) query['cursor'] = params.cursor;
  if (params.size) query['size'] = params.size;

  for (const [code, values] of Object.entries(params.attributes ?? {})) {
    if (values.length > 0) query[`attr.${code}`] = [...values];
  }

  return query;
}

/**
 * The listing page's one read, and the autocomplete beside it.
 *
 * **The query is passed through `options.params`, not through a generated query interface**, and
 * that is not a shortcut: `GET /store/products` reads its filters off the request by prefix,
 * because attribute filter names are data (see `StoreSearchEndpoints.ReadQuery` and
 * `ApiRequestOptions.params`). The operation therefore declares no query parameters and the
 * generator has nothing to emit. Recorded in `PARKING_LOT.md` — the closed half of the query
 * string *could* be declared in the contract, and should be, but the open half never can.
 *
 * A failed search answers an empty result set rather than throwing. A listing page that renders
 * "no products matched" with its filters intact is recoverable; one that throws to the error page
 * loses the shopper's place. The toast is suppressed for the same reason.
 */
@Injectable({ providedIn: 'root' })
export class ProductSearchService {
  private readonly api = inject(SearchApiClient);

  search(params: ProductSearchParams): Observable<ProductSearchResponse> {
    return this.api
      .storeSearchProducts({ params: toSearchQuery(params), silentErrors: true })
      .pipe(catchError(() => of(NO_RESULTS)));
  }

  /** Autocomplete. Answers an empty list on failure — a broken suggestion is not worth a toast. */
  suggest(query: string, limit = 8): Observable<SuggestionResponse> {
    return this.api
      .storeSearchSuggest({ q: query, limit }, { silentErrors: true, showLoading: false })
      .pipe(catchError(() => of<SuggestionResponse>({ query, suggestions: [] })));
  }

  /**
   * Reports which result was opened, so ranking can be measured against behaviour.
   *
   * Fire-and-forget by design: a shopper navigating to a product must not wait for an analytics
   * write, and a query token the log no longer holds succeeds silently on the server. Subscribed
   * here rather than returned, because no caller should ever be tempted to await it.
   */
  reportClick(queryToken: string | null, position: number, variantId: string): void {
    if (!queryToken) return;

    this.api
      .storeSearchClick({ queryToken, position, variantId }, { silentErrors: true, showLoading: false })
      .pipe(catchError(() => of(void 0)))
      .subscribe();
  }
}
