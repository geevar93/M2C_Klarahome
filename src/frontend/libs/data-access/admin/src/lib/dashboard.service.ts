import { Injectable, inject } from '@angular/core';
import {
  CatalogApiClient,
  NotificationsApiClient,
  OrderSummaryResponse,
  OrdersApiClient,
  ReturnsApiClient,
  VendorsApiClient,
} from '@klarahome/data-access-api';
import { SessionStore } from '@klarahome/data-access-auth';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';

/** One figure on the dashboard. `value` is null when the server declined to count. */
export interface DashboardTile {
  readonly key: string;
  readonly label: string;
  readonly hint: string;
  /** A count, or `"50+"` when the queue runs past one page; null when it could not be read. */
  readonly value: number | string | null;
  readonly path: string;
}

/**
 * The role-aware dashboard.
 *
 * Each tile is a **work queue, not a vanity metric**: the number of things somebody has to deal
 * with, linking to the screen where they deal with them. Revenue, conversion and the rest belong
 * to the reporting screens, which have an API designed for them; a dashboard that opened with
 * yesterday's turnover and no way to act on it would be a poster.
 *
 * Two properties are load-bearing:
 *
 *  - **A tile is requested only if the caller may see it.** The set of tiles is therefore already
 *    role-aware before anything is fetched, and a vendor sees their own four rather than the
 *    platform's nine — with the numbers themselves scoped to their seller by the API's vendor
 *    filter, not by anything here.
 *  - **A count is the rows on one full page, not `page.total`.** These endpoints page by keyset and
 *    `total` is nullable (`docs/04-api-specification.md` §1.1) — in practice no endpoint fills it
 *    in, so reading it left every tile a permanent em dash. Instead a tile asks for
 *    {@link CountPage} rows and counts them; when a next cursor comes back there are more than
 *    that, and the tile says `50+`. That is exact for a queue someone is expected to work down, and
 *    honest past it. `50` is the smallest module page cap (Orders, Returns), so no endpoint
 *    silently clamps the request to fewer and turns `50+` into a lie.
 *
 * Every request is silent and does not raise the loading bar: six parallel counts should not make
 * the whole application look busy, and a dashboard tile that fails is a missing number, not an
 * error the user can do anything about.
 */
@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly session = inject(SessionStore);
  private readonly orders = inject(OrdersApiClient);
  private readonly returns = inject(ReturnsApiClient);
  private readonly catalog = inject(CatalogApiClient);
  private readonly vendors = inject(VendorsApiClient);
  private readonly notifications = inject(NotificationsApiClient);

  private static readonly Quiet = { silentErrors: true, showLoading: false } as const;

  /** The tiles this user may see, with their counts. */
  /**
   * The five most recent orders, for the dashboard's "Recent orders" card.
   *
   * Empty (not an error) when the session may not read orders or the call fails: the card is a
   * convenience, and the dashboard must not turn red because a side list could not load.
   */
  recentOrders(): Observable<readonly OrderSummaryResponse[]> {
    if (!this.session.hasPermission('orders.order.read')) return of([]);
    return this.orders
      .adminListOrders({ size: 5 }, DashboardService.Quiet)
      .pipe(map((result) => result.items), catchError(() => of([])));
  }

  tiles(): Observable<readonly DashboardTile[]> {
    const quiet = DashboardService.Quiet;
    const sources: Observable<DashboardTile | null>[] = [];

    if (this.session.hasPermission('orders.order.read')) {
      sources.push(
        this.orders.adminListSubOrders({ status: 'Confirmed', size: CountPage }, quiet).pipe(
          map((result) =>
            tile(
              'to-pack',
              'Awaiting packing',
              'Confirmed, not yet packed',
              countOf(result),
              '/fulfilment',
            ),
          ),
          catchError(() => of(null)),
        ),
        this.orders.adminListSubOrders({ overdueOnly: true, size: CountPage }, quiet).pipe(
          map((result) =>
            tile(
              'overdue',
              'Past dispatch due',
              'The seller has missed the cut-off',
              countOf(result),
              '/fulfilment',
            ),
          ),
          catchError(() => of(null)),
        ),
      );
    }

    if (this.session.hasPermission('returns.return.read')) {
      sources.push(
        this.returns.adminListReturns({ status: 'Requested', size: CountPage }, quiet).pipe(
          map((result) =>
            tile(
              'returns',
              'Returns to decide',
              'Requested, awaiting a decision',
              countOf(result),
              '/returns',
            ),
          ),
          catchError(() => of(null)),
        ),
      );
    }

    if (this.session.hasPermission('catalog.product.moderate')) {
      sources.push(
        this.catalog.adminProductModerationQueue({ status: 'Pending', size: CountPage }, quiet).pipe(
          map((result) =>
            tile(
              'moderation',
              'Products to review',
              'Submitted by sellers',
              countOf(result),
              '/catalog/moderation',
            ),
          ),
          catchError(() => of(null)),
        ),
      );
    }

    if (this.session.hasPermission('vendors.vendor.approve')) {
      sources.push(
        this.vendors.adminVendorsList({ status: 'UnderReview', size: CountPage }, quiet).pipe(
          map((result) =>
            tile('vendors', 'Sellers to approve', 'Applications under review', countOf(result), '/vendors'),
          ),
          catchError(() => of(null)),
        ),
      );
    }

    if (this.session.hasPermission('notifications.log.read')) {
      sources.push(
        this.notifications.adminNotificationsList({ status: 'Failed', size: CountPage }, quiet).pipe(
          map((result) =>
            tile(
              'notifications',
              'Messages that failed',
              'Email or SMS not delivered',
              countOf(result),
              '/notifications',
            ),
          ),
          catchError(() => of(null)),
        ),
      );
    }

    if (sources.length === 0) return of([]);

    return forkJoin(sources).pipe(
      map((tiles) => tiles.filter((entry): entry is DashboardTile => entry !== null)),
    );
  }
}

/** Rows asked for per tile. See the class notes for why it is this number. */
const CountPage = 50;

/** The rows on the page, or `50+` when the server says there is another page after it. */
function countOf(result: {
  readonly items: readonly unknown[];
  readonly page: { readonly nextCursor?: string | null };
}): number | string {
  return result.page.nextCursor ? `${CountPage}+` : result.items.length;
}

function tile(
  key: string,
  label: string,
  hint: string,
  value: number | string | null | undefined,
  path: string,
): DashboardTile {
  return { key, label, hint, value: value ?? null, path };
}
