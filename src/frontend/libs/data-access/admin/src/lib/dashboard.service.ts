import { Injectable, inject } from '@angular/core';
import {
  CatalogApiClient,
  NotificationsApiClient,
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
  readonly value: number | null;
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
 *  - **A count is `page.total`, asked for with a page of one.** These endpoints page by keyset and
 *    `total` is explicitly nullable (`docs/04-api-specification.md` §1.1), so a tile shows an em
 *    dash where the server did not count. That is the honest rendering; a `0` would be a number
 *    somebody would act on.
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
  tiles(): Observable<readonly DashboardTile[]> {
    const quiet = DashboardService.Quiet;
    const sources: Observable<DashboardTile | null>[] = [];

    if (this.session.hasPermission('orders.order.read')) {
      sources.push(
        this.orders.adminListSubOrders({ status: 'Confirmed', size: 1 }, quiet).pipe(
          map((result) =>
            tile(
              'to-pack',
              'Awaiting packing',
              'Confirmed, not yet packed',
              result.page.total,
              '/fulfilment',
            ),
          ),
          catchError(() => of(null)),
        ),
        this.orders.adminListSubOrders({ overdueOnly: true, size: 1 }, quiet).pipe(
          map((result) =>
            tile(
              'overdue',
              'Past dispatch due',
              'The seller has missed the cut-off',
              result.page.total,
              '/fulfilment',
            ),
          ),
          catchError(() => of(null)),
        ),
      );
    }

    if (this.session.hasPermission('returns.return.read')) {
      sources.push(
        this.returns.adminListReturns({ status: 'Requested', size: 1 }, quiet).pipe(
          map((result) =>
            tile(
              'returns',
              'Returns to decide',
              'Requested, awaiting a decision',
              result.page.total,
              '/returns',
            ),
          ),
          catchError(() => of(null)),
        ),
      );
    }

    if (this.session.hasPermission('catalog.product.moderate')) {
      sources.push(
        this.catalog.adminProductModerationQueue({ status: 'Pending', size: 1 }, quiet).pipe(
          map((result) =>
            tile(
              'moderation',
              'Products to review',
              'Submitted by sellers',
              result.page.total,
              '/catalog/moderation',
            ),
          ),
          catchError(() => of(null)),
        ),
      );
    }

    if (this.session.hasPermission('vendors.vendor.approve')) {
      sources.push(
        this.vendors.adminVendorsList({ status: 'UnderReview', size: 1 }, quiet).pipe(
          map((result) =>
            tile('vendors', 'Sellers to approve', 'Applications under review', result.page.total, '/vendors'),
          ),
          catchError(() => of(null)),
        ),
      );
    }

    if (this.session.hasPermission('notifications.log.read')) {
      sources.push(
        this.notifications.adminNotificationsList({ status: 'Failed', size: 1 }, quiet).pipe(
          map((result) =>
            tile(
              'notifications',
              'Messages that failed',
              'Email or SMS not delivered',
              result.page.total,
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

function tile(
  key: string,
  label: string,
  hint: string,
  value: number | null | undefined,
  path: string,
): DashboardTile {
  return { key, label, hint, value: value ?? null, path };
}
