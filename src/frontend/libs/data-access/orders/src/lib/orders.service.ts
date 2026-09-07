import { Injectable, inject } from '@angular/core';
import {
  CancelOrderBody,
  InvoiceDownloadResponse,
  InvoiceResponse,
  OrderEventResponse,
  OrderResponse,
  OrdersApiClient,
  PagedResultOfOrderSummaryResponse,
} from '@klarahome/data-access-api';
import { Observable, catchError, of } from 'rxjs';

/**
 * The customer's own orders.
 *
 * Everything here is addressed by the **order number**, not by the id, and that is deliberate: the
 * order number is what appears on the invoice, in the confirmation email and in the customer's
 * message to support, so it is what belongs in `/account/orders/KH-2026-000481`. The API accepts
 * either on its `{id}` routes for exactly this reason (Step 14).
 *
 * The list is keyset-paged like every list on this API — a cursor, not a page number — because a
 * customer paging back through a year of orders while a new one is placed must not see a row twice
 * or miss one (docs/04-api-specification.md §1.1).
 */
@Injectable({ providedIn: 'root' })
export class OrdersService {
  private readonly api = inject(OrdersApiClient);

  list(
    options: { status?: string | null; cursor?: string | null; size?: number } = {},
  ): Observable<PagedResultOfOrderSummaryResponse> {
    return this.api.storeListOrders(
      {
        status: options.status ?? undefined,
        cursor: options.cursor ?? undefined,
        size: options.size ?? 10,
      },
      { silentErrors: true },
    );
  }

  get(orderNumber: string): Observable<OrderResponse> {
    return this.api.storeGetOrder(orderNumber);
  }

  /**
   * The order's history, as the customer is allowed to see it.
   *
   * The full order already carries a timeline, so this is only for refreshing one — a customer
   * sitting on a tracking page while a parcel is out for delivery — without re-reading the whole
   * order and its lines.
   */
  timeline(orderNumber: string): Observable<readonly OrderEventResponse[]> {
    return this.api
      .storeGetOrderTimeline(orderNumber, { silentErrors: true })
      .pipe(catchError(() => of<OrderEventResponse[]>([])));
  }

  /**
   * Cancels a whole order, or the named lines of it.
   *
   * Partial cancellation is line-level and is the same call: which lines, and how many of each
   * (Step 14). The API decides whether the state machine allows it — a customer's screen shows the
   * button because `isCancellable` said so, and the endpoint is what enforces it.
   */
  cancel(orderNumber: string, body: CancelOrderBody): Observable<OrderResponse> {
    return this.api.storeCancelOrder(orderNumber, body, { silentErrors: true });
  }

  /** Cancels one seller's part. A three-seller order is three sub-orders and cancels one at a time. */
  cancelSubOrder(subOrderNumber: string, body: CancelOrderBody): Observable<OrderResponse> {
    return this.api.storeCancelSubOrder(subOrderNumber, body, { silentErrors: true });
  }

  invoices(orderNumber: string): Observable<readonly InvoiceResponse[]> {
    return this.api
      .storeListOrderInvoices(orderNumber, { silentErrors: true })
      .pipe(catchError(() => of<InvoiceResponse[]>([])));
  }

  /**
   * A short-lived signed URL for an invoice PDF.
   *
   * The endpoint answers a URL rather than bytes, and the browser is sent to it. That is what keeps
   * the private bucket private (Step 8): the file is never proxied through the API, the link
   * expires, and no long-lived public URL for somebody's GST invoice ever exists.
   *
   * **Per sub-order, not per order.** Each seller raises their own invoice on their own gapless
   * counter (Step 14), so a basket from three sellers is three PDFs — there is no such thing as one
   * invoice for the order, and offering a single "download invoice" button would be a lie.
   */
  invoiceDownload(subOrderNumber: string): Observable<InvoiceDownloadResponse> {
    return this.api.storeDownloadInvoice(subOrderNumber, { silentErrors: true });
  }
}
