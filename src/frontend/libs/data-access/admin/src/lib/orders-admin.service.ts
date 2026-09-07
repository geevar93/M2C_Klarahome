import { Injectable, inject } from '@angular/core';
import {
  CancelOrderBody,
  InvoiceDownloadResponse,
  InvoiceResponse,
  OrderResponse,
  OrderSummaryResponse,
  OrdersApiClient,
  SubOrderResponse,
} from '@klarahome/data-access-api';
import { Observable, map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

export interface OrderFilters {
  readonly status?: string;
  readonly paymentStatus?: string;
  readonly customerId?: string;
  /** An order number, matched exactly. The one thing a support call actually starts from. */
  readonly number?: string;
  readonly from?: string;
  readonly to?: string;
}

export interface SubOrderFilters {
  readonly status?: string;
  readonly vendorId?: string;
  /** Past the seller's dispatch cut-off. The fulfilment queue's own definition of urgent. */
  readonly overdueOnly?: boolean;
}

/**
 * Orders, as the back office reads and moves them.
 *
 * The single fact that shapes every screen above this service: **the order is not the unit of
 * work; the sub-order is** (Step 14). A basket split across three sellers is one order and three
 * sub-orders, each with its own status, its own dispatch promise, its own invoice and its own
 * cancellation. The order's status is derived from its parts and is not settable — there is no
 * endpoint that takes one, and offering an "order status" control would be inventing a write the
 * domain does not have.
 *
 * So `orders` is the search surface — the list a support agent lands on with a number — and
 * `subOrders` is the work queue. `transition` acts on a sub-order, and the statuses it may be
 * given come from `SubOrderResponse.nextStatuses`, which the API computes from the same transition
 * table that will judge the request. **The buttons come off the machine rather than out of a
 * developer's head**, which is what stops the screen offering an edge that has since been removed.
 */
@Injectable({ providedIn: 'root' })
export class OrdersAdminService {
  private readonly api = inject(OrdersApiClient);

  orders(filters: OrderFilters = {}, pageSize = 25): CursorList<OrderSummaryResponse, OrderFilters> {
    return new CursorList<OrderSummaryResponse, OrderFilters>(
      (current, cursor, size) =>
        this.api
          .adminListOrders({
            status: current.status,
            paymentStatus: current.paymentStatus,
            customerId: current.customerId,
            number: current.number,
            from: current.from,
            to: current.to,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<OrderSummaryResponse> => result)),
      filters,
      pageSize,
    );
  }

  subOrders(filters: SubOrderFilters = {}, pageSize = 25): CursorList<SubOrderResponse, SubOrderFilters> {
    return new CursorList<SubOrderResponse, SubOrderFilters>(
      (current, cursor, size) =>
        this.api
          .adminListSubOrders({
            status: current.status,
            vendorId: current.vendorId,
            overdueOnly: current.overdueOnly,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<SubOrderResponse> => result)),
      filters,
      pageSize,
    );
  }

  /** One order, with its sub-orders, its lines and its whole timeline in the same response. */
  order(id: string): Observable<OrderResponse> {
    return this.api.adminGetOrder(id);
  }

  /**
   * Moves a sub-order along.
   *
   * `status` must be one of that sub-order's `nextStatuses`. The API refuses anything else with
   * the reason, and the screen never composes the list itself.
   */
  transition(subOrderId: string, status: string, reason: string | null): Observable<SubOrderResponse> {
    return this.api.adminTransitionSubOrder(subOrderId, { status, reason });
  }

  /**
   * Cancels a sub-order, whole or in part.
   *
   * Omitting `lines` cancels all of it; naming lines and quantities cancels those. Either way the
   * API is what releases the stock and starts the refund — this is one request, not an
   * orchestration, because a client that ran three steps could stop after the second.
   */
  cancelSubOrder(subOrderId: string, body: CancelOrderBody): Observable<SubOrderResponse> {
    return this.api.adminCancelSubOrder(subOrderId, body);
  }

  /**
   * Adds a note to the timeline.
   *
   * `isCustomerVisible` is the whole point of the flag and the reason it is never defaulted true:
   * an internal note about a fraud check and a message to the buyer are the same control one
   * checkbox apart, and getting it wrong is not recoverable.
   */
  addNote(orderId: string, message: string, isCustomerVisible: boolean): Observable<OrderResponse> {
    return this.api.adminAddOrderNote(orderId, { message, isCustomerVisible });
  }

  // ---- Invoices ---------------------------------------------------------------------------------

  invoices(orderId: string): Observable<InvoiceResponse[]> {
    return this.api.adminListOrderInvoices(orderId);
  }

  /** Issues the GST invoice for one sub-order, on that vendor's gapless counter. */
  issueInvoice(subOrderId: string): Observable<InvoiceResponse> {
    return this.api.adminIssueInvoice(subOrderId);
  }

  /** A signed URL for the PDF. Requested on the click, because it expires. */
  downloadInvoice(invoiceId: string): Observable<InvoiceDownloadResponse> {
    return this.api.adminDownloadInvoice(invoiceId);
  }
}
