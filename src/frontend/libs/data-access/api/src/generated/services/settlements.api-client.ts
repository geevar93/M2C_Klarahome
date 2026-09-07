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

/** Query string for `adminExportStatutoryExtract`. */
export interface AdminExportStatutoryExtractQuery {
  from?: string;
  to?: string;
  vendorId?: string;
}

/** Query string for `adminExportVendorStatement`. */
export interface AdminExportVendorStatementQuery {
  from?: string;
  to?: string;
}

/** Query string for `adminGetPlatformRevenue`. */
export interface AdminGetPlatformRevenueQuery {
  from?: string;
  to?: string;
}

/** Query string for `adminGetStatutoryExtract`. */
export interface AdminGetStatutoryExtractQuery {
  from?: string;
  to?: string;
  vendorId?: string;
}

/** Query string for `adminGetVendorStatement`. */
export interface AdminGetVendorStatementQuery {
  from?: string;
  to?: string;
}

/** Query string for `adminListCommissionInvoices`. */
export interface AdminListCommissionInvoicesQuery {
  vendorId?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListLedgerEntries`. */
export interface AdminListLedgerEntriesQuery {
  vendorId?: string;
  entryType?: string;
  cycleId?: string;
  from?: string;
  to?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListPayoutBatches`. */
export interface AdminListPayoutBatchesQuery {
  status?: string;
  from?: string;
  to?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListSettlementCycles`. */
export interface AdminListSettlementCyclesQuery {
  vendorId?: string;
  status?: string;
  from?: string;
  to?: string;
  cursor?: string;
  size?: number;
}

/** `Settlements` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class SettlementsApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Signs a payout run off. Never by the person who raised it.
   * `POST /api/v1/admin/payout-batches/{id}/approve`
   */
  adminApprovePayoutBatch(id: string, options?: ApiRequestOptions): Observable<Models.PayoutBatchResponse> {
    return this.http.request<Models.PayoutBatchResponse>('POST', `${this.baseUrl}/api/v1/admin/payout-batches/${encodeURIComponent(String(id))}/approve`, undefined, undefined, options);
  }

  /**
   * Abandons a run nothing has left. Its periods become payable again.
   * `POST /api/v1/admin/payout-batches/{id}/cancel`
   */
  adminCancelPayoutBatch(id: string, body?: null | Models.CancelPayoutBatchBody, options?: ApiRequestOptions): Observable<Models.PayoutBatchResponse> {
    return this.http.request<Models.PayoutBatchResponse>('POST', `${this.baseUrl}/api/v1/admin/payout-batches/${encodeURIComponent(String(id))}/cancel`, body, undefined, options);
  }

  /**
   * Totals a settlement period and fixes it, applying TCS and TDS.
   * `POST /api/v1/admin/settlements/cycles/{id}/close`
   */
  adminCloseSettlementCycle(id: string, body?: null | Models.CloseCycleBody, options?: ApiRequestOptions): Observable<Models.SettlementCycleResponse> {
    return this.http.request<Models.SettlementCycleResponse>('POST', `${this.baseUrl}/api/v1/admin/settlements/cycles/${encodeURIComponent(String(id))}/close`, body, undefined, options);
  }

  /**
   * Closes the period that is currently due for one seller, opening it if need be.
   * `POST /api/v1/admin/settlements/cycles/close`
   */
  adminCloseVendorSettlementPeriod(body: Models.CloseCycleBody, options?: ApiRequestOptions): Observable<Models.SettlementCycleResponse> {
    return this.http.request<Models.SettlementCycleResponse>('POST', `${this.baseUrl}/api/v1/admin/settlements/cycles/close`, body, undefined, options);
  }

  /**
   * Builds a draft payout run from closed settlement periods. Sends nothing.
   * `POST /api/v1/admin/payout-batches`
   */
  adminCreatePayoutBatch(body?: null | Models.CreatePayoutBatchBody, options?: ApiRequestOptions): Observable<Models.PayoutBatchResponse> {
    return this.http.request<Models.PayoutBatchResponse>('POST', `${this.baseUrl}/api/v1/admin/payout-batches`, body, undefined, options);
  }

  /**
   * A short-lived link to the invoice PDF. Minting the link is the grant.
   * `GET /api/v1/admin/settlements/commission-invoices/{id}/download`
   */
  adminDownloadCommissionInvoice(id: string, options?: ApiRequestOptions): Observable<Models.CommissionInvoiceDownloadResponse> {
    return this.http.request<Models.CommissionInvoiceDownloadResponse>('GET', `${this.baseUrl}/api/v1/admin/settlements/commission-invoices/${encodeURIComponent(String(id))}/download`, undefined, undefined, options);
  }

  /**
   * The same extract as a spreadsheet, for the GST and income-tax filings.
   * `GET /api/v1/admin/reports/tcs-tds/export`
   */
  adminExportStatutoryExtract(query?: AdminExportStatutoryExtractQuery, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('GET', `${this.baseUrl}/api/v1/admin/reports/tcs-tds/export`, undefined, query, options);
  }

  /**
   * The same statement as a spreadsheet.
   * `GET /api/v1/admin/vendors/{vendorId}/ledger/export`
   */
  adminExportVendorStatement(vendorId: string, query?: AdminExportVendorStatementQuery, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('GET', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(vendorId))}/ledger/export`, undefined, query, options);
  }

  /**
   * One of the platform's own invoices, with its tax split by head.
   * `GET /api/v1/admin/settlements/commission-invoices/{id}`
   */
  adminGetCommissionInvoice(id: string, options?: ApiRequestOptions): Observable<Models.CommissionInvoiceResponse> {
    return this.http.request<Models.CommissionInvoiceResponse>('GET', `${this.baseUrl}/api/v1/admin/settlements/commission-invoices/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * One payout run, its transfers, and what may be done to it next.
   * `GET /api/v1/admin/payout-batches/{id}`
   */
  adminGetPayoutBatch(id: string, options?: ApiRequestOptions): Observable<Models.PayoutBatchResponse> {
    return this.http.request<Models.PayoutBatchResponse>('GET', `${this.baseUrl}/api/v1/admin/payout-batches/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * What the platform earned, which is exactly what the sellers were charged.
   * `GET /api/v1/admin/reports/platform-revenue`
   */
  adminGetPlatformRevenue(query?: AdminGetPlatformRevenueQuery, options?: ApiRequestOptions): Observable<Models.PlatformRevenueResponse> {
    return this.http.request<Models.PlatformRevenueResponse>('GET', `${this.baseUrl}/api/v1/admin/reports/platform-revenue`, undefined, query, options);
  }

  /**
   * One settlement period, with everything that was taken out of it.
   * `GET /api/v1/admin/settlements/cycles/{id}`
   */
  adminGetSettlementCycle(id: string, options?: ApiRequestOptions): Observable<Models.SettlementCycleResponse> {
    return this.http.request<Models.SettlementCycleResponse>('GET', `${this.baseUrl}/api/v1/admin/settlements/cycles/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Tax collected and deducted at source, one line per seller per period.
   * `GET /api/v1/admin/reports/tcs-tds`
   */
  adminGetStatutoryExtract(query?: AdminGetStatutoryExtractQuery, options?: ApiRequestOptions): Observable<Models.StatutoryExtractResponse> {
    return this.http.request<Models.StatutoryExtractResponse>('GET', `${this.baseUrl}/api/v1/admin/reports/tcs-tds`, undefined, query, options);
  }

  /**
   * What a seller is owed right now, and how much of it is waiting for a payout.
   * `GET /api/v1/admin/vendors/{vendorId}/balance`
   */
  adminGetVendorBalance(vendorId: string, options?: ApiRequestOptions): Observable<Models.VendorBalanceResponse> {
    return this.http.request<Models.VendorBalanceResponse>('GET', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(vendorId))}/balance`, undefined, undefined, options);
  }

  /**
   * One seller's statement: opening balance, movements, closing balance.
   * `GET /api/v1/admin/vendors/{vendorId}/ledger`
   */
  adminGetVendorStatement(vendorId: string, query?: AdminGetVendorStatementQuery, options?: ApiRequestOptions): Observable<Models.LedgerStatementResponse> {
    return this.http.request<Models.LedgerStatementResponse>('GET', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(vendorId))}/ledger`, undefined, query, options);
  }

  /**
   * The invoices the platform raised on sellers for commission and fees, newest first. A seller sees only their own.
   * `GET /api/v1/admin/settlements/commission-invoices`
   */
  adminListCommissionInvoices(query?: AdminListCommissionInvoicesQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfCommissionInvoiceResponse> {
    return this.http.request<Models.PagedResultOfCommissionInvoiceResponse>('GET', `${this.baseUrl}/api/v1/admin/settlements/commission-invoices`, undefined, query, options);
  }

  /**
   * The movements on sellers' accounts, newest first.
   * `GET /api/v1/admin/settlements/ledger`
   */
  adminListLedgerEntries(query?: AdminListLedgerEntriesQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfLedgerEntryResponse> {
    return this.http.request<Models.PagedResultOfLedgerEntryResponse>('GET', `${this.baseUrl}/api/v1/admin/settlements/ledger`, undefined, query, options);
  }

  /**
   * The payout runs, newest first.
   * `GET /api/v1/admin/payout-batches`
   */
  adminListPayoutBatches(query?: AdminListPayoutBatchesQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfPayoutBatchResponse> {
    return this.http.request<Models.PagedResultOfPayoutBatchResponse>('GET', `${this.baseUrl}/api/v1/admin/payout-batches`, undefined, query, options);
  }

  /**
   * The settlement periods, newest first.
   * `GET /api/v1/admin/settlements/cycles`
   */
  adminListSettlementCycles(query?: AdminListSettlementCyclesQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfSettlementCycleResponse> {
    return this.http.request<Models.PagedResultOfSettlementCycleResponse>('GET', `${this.baseUrl}/api/v1/admin/settlements/cycles`, undefined, query, options);
  }

  /**
   * Writes a correction into a seller's account. An append, never an edit.
   * `POST /api/v1/admin/settlements/adjustments`
   */
  adminPostSettlementAdjustment(body: Models.AdjustmentBody, options?: ApiRequestOptions): Observable<Models.LedgerEntryResponse> {
    return this.http.request<Models.LedgerEntryResponse>('POST', `${this.baseUrl}/api/v1/admin/settlements/adjustments`, body, undefined, options);
  }

  /**
   * Hands an approved run to the gateway. Resumable: call it again for a large batch.
   * `POST /api/v1/admin/payout-batches/{id}/process`
   */
  adminProcessPayoutBatch(id: string, options?: ApiRequestOptions): Observable<Models.PayoutBatchResponse> {
    return this.http.request<Models.PayoutBatchResponse>('POST', `${this.baseUrl}/api/v1/admin/payout-batches/${encodeURIComponent(String(id))}/process`, undefined, undefined, options);
  }
}
