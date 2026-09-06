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

/** Query string for `adminListCodCollections`. */
export interface AdminListCodCollectionsQuery {
  status?: string;
  vendorId?: string;
  orderId?: string;
  from?: string;
  to?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListGatewayEvents`. */
export interface AdminListGatewayEventsQuery {
  status?: string;
  type?: string;
  paymentId?: string;
  from?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListPayments`. */
export interface AdminListPaymentsQuery {
  status?: string;
  method?: string;
  provider?: string;
  orderId?: string;
  q?: string;
  from?: string;
  to?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListRefunds`. */
export interface AdminListRefundsQuery {
  status?: string;
  orderId?: string;
  from?: string;
  to?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListSettlementEntries`. */
export interface AdminListSettlementEntriesQuery {
  matchStatus?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListSettlements`. */
export interface AdminListSettlementsQuery {
  from?: string;
  to?: string;
  cursor?: string;
  size?: number;
}

/** `Payments` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class PaymentsApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * The second signature. Refused if it is the same person who raised it.
   * `POST /api/v1/admin/refunds/{id}/approve`
   */
  adminApproveRefund(id: string, options?: ApiRequestOptions): Observable<Models.RefundResponse> {
    return this.http.request<Models.RefundResponse>('POST', `${this.baseUrl}/api/v1/admin/refunds/${encodeURIComponent(String(id))}/approve`, undefined, undefined, options);
  }

  /**
   * Takes money the gateway is holding on an authorised payment.
   * `POST /api/v1/admin/payments/{id}/capture`
   */
  adminCapturePayment(id: string, body?: null | Models.CaptureBody, options?: ApiRequestOptions): Observable<Models.PaymentResponse> {
    return this.http.request<Models.PaymentResponse>('POST', `${this.baseUrl}/api/v1/admin/payments/${encodeURIComponent(String(id))}/capture`, body, undefined, options);
  }

  /**
   * One stored webhook, with the body exactly as it arrived.
   * `GET /api/v1/admin/gateway-events/{id}`
   */
  adminGetGatewayEvent(id: string, options?: ApiRequestOptions): Observable<Models.GatewayEventResponse> {
    return this.http.request<Models.GatewayEventResponse>('GET', `${this.baseUrl}/api/v1/admin/gateway-events/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * One collection in full, with every attempt and every refund.
   * `GET /api/v1/admin/payments/{id}`
   */
  adminGetPayment(id: string, options?: ApiRequestOptions): Observable<Models.PaymentResponse> {
    return this.http.request<Models.PaymentResponse>('GET', `${this.baseUrl}/api/v1/admin/payments/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * One settlement report.
   * `GET /api/v1/admin/settlements/{id}`
   */
  adminGetSettlement(id: string, options?: ApiRequestOptions): Observable<Models.SettlementResponse> {
    return this.http.request<Models.SettlementResponse>('GET', `${this.baseUrl}/api/v1/admin/settlements/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Pulls settlement reports for a window and matches their lines.
   * `POST /api/v1/admin/settlements/import`
   */
  adminImportSettlements(body?: null | Models.ImportSettlementsBody, options?: ApiRequestOptions): Observable<Models.SettlementIngestionSummary> {
    return this.http.request<Models.SettlementIngestionSummary>('POST', `${this.baseUrl}/api/v1/admin/settlements/import`, body, undefined, options);
  }

  /**
   * Cash owed and collected at doors. Filter status=Collected for what is owed to us.
   * `GET /api/v1/admin/cod-collections`
   */
  adminListCodCollections(query?: AdminListCodCollectionsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfCodCollectionResponse> {
    return this.http.request<Models.PagedResultOfCodCollectionResponse>('GET', `${this.baseUrl}/api/v1/admin/cod-collections`, undefined, query, options);
  }

  /**
   * The webhook log and the dead-letter queue, one list. Filter status=DeadLettered.
   * `GET /api/v1/admin/gateway-events`
   */
  adminListGatewayEvents(query?: AdminListGatewayEventsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfGatewayEventSummaryResponse> {
    return this.http.request<Models.PagedResultOfGatewayEventSummaryResponse>('GET', `${this.baseUrl}/api/v1/admin/gateway-events`, undefined, query, options);
  }

  /**
   * Collections, newest first, filtered by status, method, provider or order.
   * `GET /api/v1/admin/payments`
   */
  adminListPayments(query?: AdminListPaymentsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfPaymentSummaryResponse> {
    return this.http.request<Models.PagedResultOfPaymentSummaryResponse>('GET', `${this.baseUrl}/api/v1/admin/payments`, undefined, query, options);
  }

  /**
   * Refunds, newest first. Filter to Requested for the approvals queue.
   * `GET /api/v1/admin/refunds`
   */
  adminListRefunds(query?: AdminListRefundsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfRefundResponse> {
    return this.http.request<Models.PagedResultOfRefundResponse>('GET', `${this.baseUrl}/api/v1/admin/refunds`, undefined, query, options);
  }

  /**
   * A report's lines. Filter matchStatus=Mismatched for what needs acting on.
   * `GET /api/v1/admin/settlements/{id}/entries`
   */
  adminListSettlementEntries(id: string, query?: AdminListSettlementEntriesQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfSettlementEntryResponse> {
    return this.http.request<Models.PagedResultOfSettlementEntryResponse>('GET', `${this.baseUrl}/api/v1/admin/settlements/${encodeURIComponent(String(id))}/entries`, undefined, query, options);
  }

  /**
   * Imported settlement reports, newest first, with their reconciliation counts.
   * `GET /api/v1/admin/settlements`
   */
  adminListSettlements(query?: AdminListSettlementsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfSettlementResponse> {
    return this.http.request<Models.PagedResultOfSettlementResponse>('GET', `${this.baseUrl}/api/v1/admin/settlements`, undefined, query, options);
  }

  /**
   * Raises a refund. Above the threshold it waits for a second signature.
   * `POST /api/v1/admin/payments/{id}/refunds`
   */
  adminRaiseRefund(id: string, body: Models.RefundBody, options?: ApiRequestOptions): Observable<Models.RefundResponse> {
    return this.http.request<Models.RefundResponse>('POST', `${this.baseUrl}/api/v1/admin/payments/${encodeURIComponent(String(id))}/refunds`, body, undefined, options);
  }

  /**
   * Records that the courier took the cash at the door.
   * `POST /api/v1/admin/cod-collections/{id}/collect`
   */
  adminRecordCodCollection(id: string, body: Models.CollectCashBody, options?: ApiRequestOptions): Observable<Models.CodCollectionResponse> {
    return this.http.request<Models.CodCollectionResponse>('POST', `${this.baseUrl}/api/v1/admin/cod-collections/${encodeURIComponent(String(id))}/collect`, body, undefined, options);
  }

  /**
   * Records a courier's remittance against a batch of collections, in one go.
   * `POST /api/v1/admin/cod-collections/remit`
   */
  adminRecordCodRemittance(body: Models.RemitCashBody, options?: ApiRequestOptions): Observable<Models.CodCollectionResponse[]> {
    return this.http.request<Models.CodCollectionResponse[]>('POST', `${this.baseUrl}/api/v1/admin/cod-collections/remit`, body, undefined, options);
  }

  /**
   * Withholds the second signature. Nothing is sent to the gateway.
   * `POST /api/v1/admin/refunds/{id}/reject`
   */
  adminRejectRefund(id: string, body?: null | Models.RejectRefundBody, options?: ApiRequestOptions): Observable<Models.RefundResponse> {
    return this.http.request<Models.RefundResponse>('POST', `${this.baseUrl}/api/v1/admin/refunds/${encodeURIComponent(String(id))}/reject`, body, undefined, options);
  }

  /**
   * Re-queues a failed or dead-lettered event. It is not re-verified.
   * `POST /api/v1/admin/gateway-events/{id}/replay`
   */
  adminReplayGatewayEvent(id: string, options?: ApiRequestOptions): Observable<Models.GatewayEventSummaryResponse> {
    return this.http.request<Models.GatewayEventSummaryResponse>('POST', `${this.baseUrl}/api/v1/admin/gateway-events/${encodeURIComponent(String(id))}/replay`, undefined, undefined, options);
  }

  /**
   * Runs the reconciliation sweep now instead of waiting for the timer.
   * `POST /api/v1/admin/payments/reconcile`
   */
  adminRunReconciliation(options?: ApiRequestOptions): Observable<Models.ReconciliationSummary> {
    return this.http.request<Models.ReconciliationSummary>('POST', `${this.baseUrl}/api/v1/admin/payments/reconcile`, undefined, undefined, options);
  }

  /**
   * Re-reads the payment from the gateway and applies what it says.
   * `POST /api/v1/admin/payments/{id}/sync`
   */
  adminSyncPayment(id: string, options?: ApiRequestOptions): Observable<Models.PaymentResponse> {
    return this.http.request<Models.PaymentResponse>('POST', `${this.baseUrl}/api/v1/admin/payments/${encodeURIComponent(String(id))}/sync`, undefined, undefined, options);
  }

  /**
   * Re-reads the refund from the gateway and applies what it says.
   * `POST /api/v1/admin/refunds/{id}/sync`
   */
  adminSyncRefund(id: string, options?: ApiRequestOptions): Observable<Models.RefundResponse> {
    return this.http.request<Models.RefundResponse>('POST', `${this.baseUrl}/api/v1/admin/refunds/${encodeURIComponent(String(id))}/sync`, undefined, undefined, options);
  }

  /**
   * Where the money for one of the caller's own orders stands.
   * `GET /api/v1/store/payments/orders/{orderId}`
   */
  storeGetOrderPayment(orderId: string, options?: ApiRequestOptions): Observable<Models.MyPaymentResponse> {
    return this.http.request<Models.MyPaymentResponse>('GET', `${this.baseUrl}/api/v1/store/payments/orders/${encodeURIComponent(String(orderId))}`, undefined, undefined, options);
  }

  /**
   * A fresh payment instruction for an order that was not paid for.
   * `POST /api/v1/store/payments/orders/{orderId}/retry`
   */
  storeRetryPayment(orderId: string, options?: ApiRequestOptions): Observable<Models.PaymentInstructionResponse> {
    return this.http.request<Models.PaymentInstructionResponse>('POST', `${this.baseUrl}/api/v1/store/payments/orders/${encodeURIComponent(String(orderId))}/retry`, undefined, undefined, options);
  }

  /**
   * Records the browser's callback as an attempt. It confirms nothing.
   * `POST /api/v1/store/payments/orders/{orderId}/verify`
   */
  storeVerifyCheckout(orderId: string, body: Models.VerifyCheckoutBody, options?: ApiRequestOptions): Observable<Models.MyPaymentResponse> {
    return this.http.request<Models.MyPaymentResponse>('POST', `${this.baseUrl}/api/v1/store/payments/orders/${encodeURIComponent(String(orderId))}/verify`, body, undefined, options);
  }
}
