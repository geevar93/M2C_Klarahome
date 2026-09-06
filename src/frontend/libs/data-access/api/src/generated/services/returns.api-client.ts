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

/** Query string for `adminListCreditNotes`. */
export interface AdminListCreditNotesQuery {
  vendorId?: string;
  financialYear?: string;
  from?: string;
  to?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListReturnReasons`. */
export interface AdminListReturnReasonsQuery {
  includeInactive?: boolean;
}

/** Query string for `adminListReturns`. */
export interface AdminListReturnsQuery {
  status?: string;
  vendorId?: string;
  orderId?: string;
  from?: string;
  to?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `storeListReturns`. */
export interface StoreListReturnsQuery {
  status?: string;
  cursor?: string;
  size?: number;
}

/** `Returns` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class ReturnsApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Agrees to a return, and says whether the goods have to come back.
   * `POST /api/v1/admin/returns/{id}/approve`
   */
  adminApproveReturn(id: string, body?: null | Models.ApproveReturnBody, options?: ApiRequestOptions): Observable<Models.ReturnResponse> {
    return this.http.request<Models.ReturnResponse>('POST', `${this.baseUrl}/api/v1/admin/returns/${encodeURIComponent(String(id))}/approve`, body, undefined, options);
  }

  /**
   * Closes a return with nothing owed.
   * `POST /api/v1/admin/returns/{id}/close`
   */
  adminCloseReturn(id: string, body?: null | Models.CloseReturnBody, options?: ApiRequestOptions): Observable<Models.ReturnResponse> {
    return this.http.request<Models.ReturnResponse>('POST', `${this.baseUrl}/api/v1/admin/returns/${encodeURIComponent(String(id))}/close`, body, undefined, options);
  }

  /**
   * Opens a reason a shopper may give.
   * `POST /api/v1/admin/return-reasons`
   */
  adminCreateReturnReason(body: Models.ReturnReasonBody, options?: ApiRequestOptions): Observable<Models.ReturnReasonResponse> {
    return this.http.request<Models.ReturnReasonResponse>('POST', `${this.baseUrl}/api/v1/admin/return-reasons`, body, undefined, options);
  }

  /**
   * One credit note, with its tax split.
   * `GET /api/v1/admin/credit-notes/{id}`
   */
  adminGetCreditNote(id: string, options?: ApiRequestOptions): Observable<Models.CreditNoteResponse> {
    return this.http.request<Models.CreditNoteResponse>('GET', `${this.baseUrl}/api/v1/admin/credit-notes/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * One return in full, with its evidence and what may be done to it next.
   * `GET /api/v1/admin/returns/{id}`
   */
  adminGetReturn(id: string, options?: ApiRequestOptions): Observable<Models.ReturnResponse> {
    return this.http.request<Models.ReturnResponse>('GET', `${this.baseUrl}/api/v1/admin/returns/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * The credit note raised against one return.
   * `GET /api/v1/admin/returns/{id}/credit-note`
   */
  adminGetReturnCreditNote(id: string, options?: ApiRequestOptions): Observable<Models.CreditNoteResponse> {
    return this.http.request<Models.CreditNoteResponse>('GET', `${this.baseUrl}/api/v1/admin/returns/${encodeURIComponent(String(id))}/credit-note`, undefined, undefined, options);
  }

  /**
   * Records what quality control decided, and what becomes of the goods.
   * `POST /api/v1/admin/returns/{id}/qc`
   */
  adminInspectReturn(id: string, body: Models.QcBody, options?: ApiRequestOptions): Observable<Models.ReturnResponse> {
    return this.http.request<Models.ReturnResponse>('POST', `${this.baseUrl}/api/v1/admin/returns/${encodeURIComponent(String(id))}/qc`, body, undefined, options);
  }

  /**
   * The credit notes raised, newest first.
   * `GET /api/v1/admin/credit-notes`
   */
  adminListCreditNotes(query?: AdminListCreditNotesQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfCreditNoteResponse> {
    return this.http.request<Models.PagedResultOfCreditNoteResponse>('GET', `${this.baseUrl}/api/v1/admin/credit-notes`, undefined, query, options);
  }

  /**
   * The reason codes and the policy each carries.
   * `GET /api/v1/admin/return-reasons`
   */
  adminListReturnReasons(query?: AdminListReturnReasonsQuery, options?: ApiRequestOptions): Observable<Models.ReturnReasonResponse[]> {
    return this.http.request<Models.ReturnReasonResponse[]>('GET', `${this.baseUrl}/api/v1/admin/return-reasons`, undefined, query, options);
  }

  /**
   * The returns queue, newest first.
   * `GET /api/v1/admin/returns`
   */
  adminListReturns(query?: AdminListReturnsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfReturnSummaryResponse> {
    return this.http.request<Models.PagedResultOfReturnSummaryResponse>('GET', `${this.baseUrl}/api/v1/admin/returns`, undefined, query, options);
  }

  /**
   * Books a returned parcel in at the warehouse.
   * `POST /api/v1/admin/returns/{id}/receive`
   */
  adminReceiveReturn(id: string, body?: null | Models.ReceiveReturnBody, options?: ApiRequestOptions): Observable<Models.ReturnResponse> {
    return this.http.request<Models.ReturnResponse>('POST', `${this.baseUrl}/api/v1/admin/returns/${encodeURIComponent(String(id))}/receive`, body, undefined, options);
  }

  /**
   * Pays a return out and raises its credit note.
   * `POST /api/v1/admin/returns/{id}/refund`
   */
  adminRefundReturn(id: string, body?: null | Models.RefundReturnBody, options?: ApiRequestOptions): Observable<Models.ReturnResponse> {
    return this.http.request<Models.ReturnResponse>('POST', `${this.baseUrl}/api/v1/admin/returns/${encodeURIComponent(String(id))}/refund`, body, undefined, options);
  }

  /**
   * Refuses a return, with a reason the shopper is shown.
   * `POST /api/v1/admin/returns/{id}/reject`
   */
  adminRejectReturn(id: string, body?: null | Models.RejectReturnBody, options?: ApiRequestOptions): Observable<Models.ReturnResponse> {
    return this.http.request<Models.ReturnResponse>('POST', `${this.baseUrl}/api/v1/admin/returns/${encodeURIComponent(String(id))}/reject`, body, undefined, options);
  }

  /**
   * Records that a replacement was dispatched instead of a refund.
   * `POST /api/v1/admin/returns/{id}/replace`
   */
  adminReplaceReturn(id: string, body?: null | Models.ReplaceReturnBody, options?: ApiRequestOptions): Observable<Models.ReturnResponse> {
    return this.http.request<Models.ReturnResponse>('POST', `${this.baseUrl}/api/v1/admin/returns/${encodeURIComponent(String(id))}/replace`, body, undefined, options);
  }

  /**
   * Books a courier to collect an approved return.
   * `POST /api/v1/admin/returns/{id}/schedule-pickup`
   */
  adminScheduleReturnPickup(id: string, body?: null | Models.SchedulePickupBody, options?: ApiRequestOptions): Observable<Models.ReturnResponse> {
    return this.http.request<Models.ReturnResponse>('POST', `${this.baseUrl}/api/v1/admin/returns/${encodeURIComponent(String(id))}/schedule-pickup`, body, undefined, options);
  }

  /**
   * Changes a reason's wording or the policy it carries. The code never changes.
   * `PUT /api/v1/admin/return-reasons/{id}`
   */
  adminUpdateReturnReason(id: string, body: Models.ReturnReasonBody, options?: ApiRequestOptions): Observable<Models.ReturnReasonResponse> {
    return this.http.request<Models.ReturnReasonResponse>('PUT', `${this.baseUrl}/api/v1/admin/return-reasons/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Withdraws a return whose goods have not yet been collected.
   * `POST /api/v1/store/returns/{id}/cancel`
   */
  storeCancelReturn(id: string, body?: null | Models.CancelReturnBody, options?: ApiRequestOptions): Observable<Models.ReturnResponse> {
    return this.http.request<Models.ReturnResponse>('POST', `${this.baseUrl}/api/v1/store/returns/${encodeURIComponent(String(id))}/cancel`, body, undefined, options);
  }

  /**
   * One of the caller's own returns, in full.
   * `GET /api/v1/store/returns/{id}`
   */
  storeGetReturn(id: string, options?: ApiRequestOptions): Observable<Models.ReturnResponse> {
    return this.http.request<Models.ReturnResponse>('GET', `${this.baseUrl}/api/v1/store/returns/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * The credit note raised against one of the caller's own returns.
   * `GET /api/v1/store/returns/{id}/credit-note`
   */
  storeGetReturnCreditNote(id: string, options?: ApiRequestOptions): Observable<Models.CreditNoteResponse> {
    return this.http.request<Models.CreditNoteResponse>('GET', `${this.baseUrl}/api/v1/store/returns/${encodeURIComponent(String(id))}/credit-note`, undefined, undefined, options);
  }

  /**
   * The reasons this store accepts for a return.
   * `GET /api/v1/store/returns/reasons`
   */
  storeListReturnReasons(options?: ApiRequestOptions): Observable<Models.ReturnReasonOption[]> {
    return this.http.request<Models.ReturnReasonOption[]>('GET', `${this.baseUrl}/api/v1/store/returns/reasons`, undefined, undefined, options);
  }

  /**
   * The caller's own returns, newest first.
   * `GET /api/v1/store/returns`
   */
  storeListReturns(query?: StoreListReturnsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfReturnSummaryResponse> {
    return this.http.request<Models.PagedResultOfReturnSummaryResponse>('GET', `${this.baseUrl}/api/v1/store/returns`, undefined, query, options);
  }

  /**
   * Asks to send items back from one seller's part of an order.
   * `POST /api/v1/store/returns`
   */
  storeRaiseReturn(body: Models.RaiseReturnBody, options?: ApiRequestOptions): Observable<Models.ReturnResponse> {
    return this.http.request<Models.ReturnResponse>('POST', `${this.baseUrl}/api/v1/store/returns`, body, undefined, options);
  }

  /**
   * What can still be sent back from one seller's part, and until when.
   * `GET /api/v1/store/sub-orders/{id}/returnable`
   */
  storeReturnEligibility(id: string, options?: ApiRequestOptions): Observable<Models.ReturnEligibilityResponse> {
    return this.http.request<Models.ReturnEligibilityResponse>('GET', `${this.baseUrl}/api/v1/store/sub-orders/${encodeURIComponent(String(id))}/returnable`, undefined, undefined, options);
  }
}
