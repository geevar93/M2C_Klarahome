import { Injectable, inject } from '@angular/core';
import {
  CatalogApiClient,
  IdentityApiClient,
  OrdersApiClient,
  VendorsApiClient,
} from '@klarahome/data-access-api';
import { SessionStore } from '@klarahome/data-access-auth';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';

/** One thing found. `path` is where the back office shows it. */
export interface SearchHit {
  readonly id: string;
  readonly title: string;
  readonly subtitle: string;
  readonly path: string;
}

/** Hits of one kind, under the heading they are listed beneath. */
export interface SearchGroup {
  readonly key: string;
  readonly label: string;
  readonly hits: readonly SearchHit[];
}

/**
 * Search across the back office.
 *
 * There is no `GET /admin/search`, and inventing one on the client is not a workaround — it is the
 * only correct implementation available, because a cross-module search endpoint would have to
 * query five schemas, and this platform's architecture forbids exactly that (a module reads
 * another module's data through a declared seam or not at all). So this fans out to the four list
 * endpoints that already accept a search term and merges what comes back.
 *
 * Three properties make it safe rather than merely convenient:
 *
 *  - **Every source is gated on the caller's own permission** before it is called. A support user
 *    without `catalog.product.read` does not see products in their results, and — the part that
 *    matters — never sends the request, so the screen is not a row of 403s.
 *  - **A source that fails is dropped, not fatal.** Four requests in parallel means four chances
 *    to fail; a search that returned nothing because sellers were briefly unavailable would be
 *    worse than one missing a heading.
 *  - **The vendor scope is the API's, not this code's.** A seller searching "sofa" reaches the
 *    same endpoint and gets their own products, because the vendor query filter is applied in the
 *    data layer. Nothing here filters by seller, which is why nothing here can get it wrong.
 *
 * Orders are matched by **number only**, because that is the sole term
 * `GET /admin/orders` accepts — searching orders by customer name would need an endpoint that does
 * not exist, and a box that silently ignores half of what is typed into it is worse than one that
 * says what it matches.
 */
@Injectable({ providedIn: 'root' })
export class GlobalSearchService {
  private readonly session = inject(SessionStore);
  private readonly orders = inject(OrdersApiClient);
  private readonly catalog = inject(CatalogApiClient);
  private readonly vendors = inject(VendorsApiClient);
  private readonly identity = inject(IdentityApiClient);

  /** How many hits each source contributes. Enough to recognise one, not enough to scroll. */
  private static readonly PerSource = 5;

  /**
   * Runs the search. Answers only the groups that found something.
   *
   * A term shorter than two characters answers nothing without a request: one character matches
   * most of the catalogue, and the results would be noise paid for with four queries.
   */
  search(term: string): Observable<readonly SearchGroup[]> {
    const query = term.trim();
    if (query.length < 2) return of([]);

    const sources: Observable<SearchGroup | null>[] = [];
    const size = GlobalSearchService.PerSource;
    const quiet = { silentErrors: true, showLoading: false } as const;

    if (this.session.hasPermission('orders.order.read')) {
      sources.push(
        this.orders.adminListOrders({ number: query, size }, quiet).pipe(
          map((result) =>
            group('orders', 'Orders', result.items, (order) => ({
              id: order.id,
              title: order.orderNumber,
              subtitle: `${order.customerName} · ${order.status}`,
              path: `/orders/${order.id}`,
            })),
          ),
          catchError(() => of(null)),
        ),
      );
    }

    if (this.session.hasPermission('catalog.product.read')) {
      sources.push(
        this.catalog.adminProductsList({ search: query, size: size }, quiet).pipe(
          map((result) =>
            group('products', 'Products', result.items, (product) => ({
              id: product.id,
              title: product.name,
              subtitle: `${product.status} · ${product.variantCount} variant(s)`,
              path: `/catalog/products/${product.id}`,
            })),
          ),
          catchError(() => of(null)),
        ),
      );
    }

    if (this.session.hasPermission('vendors.vendor.read')) {
      sources.push(
        this.vendors.adminVendorsList({ search: query, size: size }, quiet).pipe(
          map((result) =>
            group('vendors', 'Sellers', result.items, (vendor) => ({
              id: vendor.id,
              title: vendor.displayName,
              subtitle: `${vendor.code} · ${vendor.status}`,
              path: `/vendors/${vendor.id}`,
            })),
          ),
          catchError(() => of(null)),
        ),
      );
    }

    if (this.session.hasPermission('identity.user.read')) {
      sources.push(
        this.identity.adminUsersGet({ search: query, size: size }, quiet).pipe(
          map((result) =>
            group('users', 'Users', result.items, (user) => ({
              id: user.id,
              title: user.email ?? user.mobile ?? user.id,
              subtitle: `${user.userType} · ${user.status}`,
              path: `/settings/users/${user.id}`,
            })),
          ),
          catchError(() => of(null)),
        ),
      );
    }

    // Nothing the caller may search. Answers empty rather than an error: the search box is hidden
    // for such a user anyway, and a thrown exception here would be a crash on a keystroke.
    if (sources.length === 0) return of([]);

    return forkJoin(sources).pipe(
      map((groups) =>
        groups.filter((entry): entry is SearchGroup => entry !== null && entry.hits.length > 0),
      ),
    );
  }
}

function group<T>(
  key: string,
  label: string,
  items: readonly T[],
  toHit: (item: T) => SearchHit,
): SearchGroup {
  return { key, label, hits: items.map(toHit) };
}
