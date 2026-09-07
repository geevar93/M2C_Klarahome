/**
 * DO NOT EDIT. Generated from the API's OpenAPI document by tools/generate-api-client.mjs.
 *
 * Regenerate with:  pwsh tools/generate-api-client.ps1
 * CI fails if this file differs from what the current API produces.
 */
/* eslint-disable */

import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiRequestOptions, ApiTransport } from '../../runtime';
import type * as Models from '../models';

/** Query string for `adminListOrders`. */
export interface AdminListOrdersQuery {
  status?: string;
  paymentStatus?: string;
  customerId?: string;
  number?: string;
  from?: string;
  to?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListSubOrders`. */
export interface AdminListSubOrdersQuery {
  status?: string;
  vendorId?: string;
  warehouseId?: string;
  overdueOnly?: boolean;
  cursor?: string;
  size?: number;
}

/** Query string for `storeListOrders`. */
export interface StoreListOrdersQuery {
  status?: string;
  cursor?: string;
  size?: number;
}

/** `Orders` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class OrdersApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Appends a note to the timeline. Internal unless the body says otherwise.
   * `POST /api/v1/admin/orders/{id}/notes`
   */
  adminAddOrderNote(id: string, body: Models.OrderNoteBody, options?: ApiRequestOptions): Observable<Models.OrderResponse> {
    return this.http.request<Models.OrderResponse>('POST', `${this.baseUrl}/api/v1/admin/orders/${encodeURIComponent(String(id))}/notes`, body, undefined, options);
  }

  /**
   * Cancels a sub-order, or the units the body names. A reason is required after dispatch.
   * `POST /api/v1/admin/sub-orders/{id}/cancel`
   */
  adminCancelSubOrder(id: string, body?: null | Models.CancelOrderBody, options?: ApiRequestOptions): Observable<Models.SubOrderResponse> {
    return this.http.request<Models.SubOrderResponse>('POST', `${this.baseUrl}/api/v1/admin/sub-orders/${encodeURIComponent(String(id))}/cancel`, body, undefined, options);
  }

  /**
   * A short-lived link to the invoice PDF. Minting the link is the grant.
   * `GET /api/v1/admin/invoices/{id}/download`
   */
  adminDownloadInvoice(id: string, options?: ApiRequestOptions): Observable<Models.InvoiceDownloadResponse> {
    return this.http.request<Models.InvoiceDownloadResponse>('GET', `${this.baseUrl}/api/v1/admin/invoices/${encodeURIComponent(String(id))}/download`, undefined, undefined, options);
  }

  /**
   * One order in full: sellers, lines, invoices and the whole timeline.
   * `GET /api/v1/admin/orders/{id}`
   */
  adminGetOrder(id: string, options?: ApiRequestOptions): Observable<Models.OrderResponse> {
    return this.http.request<Models.OrderResponse>('GET', `${this.baseUrl}/api/v1/admin/orders/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Raises the tax invoice by hand, for the parcel that was dispatched without one.
   * `POST /api/v1/admin/sub-orders/{id}/invoice`
   */
  adminIssueInvoice(id: string, options?: ApiRequestOptions): Observable<Models.InvoiceResponse> {
    return this.http.request<Models.InvoiceResponse>('POST', `${this.baseUrl}/api/v1/admin/sub-orders/${encodeURIComponent(String(id))}/invoice`, undefined, undefined, options);
  }

  /**
   * The tax invoices raised against the order, one per seller.
   * `GET /api/v1/admin/orders/{id}/invoices`
   */
  adminListOrderInvoices(id: string, options?: ApiRequestOptions): Observable<Models.InvoiceResponse[]> {
    return this.http.request<Models.InvoiceResponse[]>('GET', `${this.baseUrl}/api/v1/admin/orders/${encodeURIComponent(String(id))}/invoices`, undefined, undefined, options);
  }

  /**
   * Orders, newest first. A vendor caller sees only orders they have a part in.
   * `GET /api/v1/admin/orders`
   */
  adminListOrders(query?: AdminListOrdersQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfOrderSummaryResponse> {
    return this.http.request<Models.PagedResultOfOrderSummaryResponse>('GET', `${this.baseUrl}/api/v1/admin/orders`, undefined, query, options);
  }

  /**
   * The fulfilment worklist. A vendor caller sees only their own, and warehouseId narrows it to the parcels a given location is packing.
   * `GET /api/v1/admin/sub-orders`
   */
  adminListSubOrders(query?: AdminListSubOrdersQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfSubOrderResponse> {
    return this.http.request<Models.PagedResultOfSubOrderResponse>('GET', `${this.baseUrl}/api/v1/admin/sub-orders`, undefined, query, options);
  }

  /**
   * Moves a sub-order to the named state, if the machine allows it for this caller.
   * `POST /api/v1/admin/sub-orders/{id}/transition`
   */
  adminTransitionSubOrder(id: string, body: Models.TransitionBody, options?: ApiRequestOptions): Observable<Models.SubOrderResponse> {
    return this.http.request<Models.SubOrderResponse>('POST', `${this.baseUrl}/api/v1/admin/sub-orders/${encodeURIComponent(String(id))}/transition`, body, undefined, options);
  }

  /**
   * Cancels every part of the order that may still be cancelled, and says what could not.
   * `POST /api/v1/store/orders/{id}/cancel`
   */
  storeCancelOrder(id: string, body?: null | Models.CancelOrderBody, options?: ApiRequestOptions): Observable<Models.OrderResponse> {
    return this.http.request<Models.OrderResponse>('POST', `${this.baseUrl}/api/v1/store/orders/${encodeURIComponent(String(id))}/cancel`, body, undefined, options);
  }

  /**
   * Cancels one seller's part, or the units of it the body names.
   * `POST /api/v1/store/sub-orders/{id}/cancel`
   */
  storeCancelSubOrder(id: string, body?: null | Models.CancelOrderBody, options?: ApiRequestOptions): Observable<Models.OrderResponse> {
    return this.http.request<Models.OrderResponse>('POST', `${this.baseUrl}/api/v1/store/sub-orders/${encodeURIComponent(String(id))}/cancel`, body, undefined, options);
  }

  /**
   * A short-lived link to the invoice PDF. Minting the link is the grant.
   * `GET /api/v1/store/invoices/{id}/download`
   */
  storeDownloadInvoice(id: string, options?: ApiRequestOptions): Observable<Models.InvoiceDownloadResponse> {
    return this.http.request<Models.InvoiceDownloadResponse>('GET', `${this.baseUrl}/api/v1/store/invoices/${encodeURIComponent(String(id))}/download`, undefined, undefined, options);
  }

  /**
   * One of the caller's own orders in full, with a section per seller.
   * `GET /api/v1/store/orders/{id}`
   */
  storeGetOrder(id: string, options?: ApiRequestOptions): Observable<Models.OrderResponse> {
    return this.http.request<Models.OrderResponse>('GET', `${this.baseUrl}/api/v1/store/orders/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * What has happened to the order, oldest first. Internal entries are not included.
   * `GET /api/v1/store/orders/{id}/timeline`
   */
  storeGetOrderTimeline(id: string, options?: ApiRequestOptions): Observable<Models.OrderEventResponse[]> {
    return this.http.request<Models.OrderEventResponse[]>('GET', `${this.baseUrl}/api/v1/store/orders/${encodeURIComponent(String(id))}/timeline`, undefined, undefined, options);
  }

  /**
   * The tax invoices raised against the order, one per seller.
   * `GET /api/v1/store/orders/{id}/invoices`
   */
  storeListOrderInvoices(id: string, options?: ApiRequestOptions): Observable<Models.InvoiceResponse[]> {
    return this.http.request<Models.InvoiceResponse[]>('GET', `${this.baseUrl}/api/v1/store/orders/${encodeURIComponent(String(id))}/invoices`, undefined, undefined, options);
  }

  /**
   * The caller's own orders, newest first.
   * `GET /api/v1/store/orders`
   */
  storeListOrders(query?: StoreListOrdersQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfOrderSummaryResponse> {
    return this.http.request<Models.PagedResultOfOrderSummaryResponse>('GET', `${this.baseUrl}/api/v1/store/orders`, undefined, query, options);
  }
}
