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

/** Query string for `adminGoodsReceiptsList`. */
export interface AdminGoodsReceiptsListQuery {
  purchaseOrderId?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminPurchaseOrdersList`. */
export interface AdminPurchaseOrdersListQuery {
  status?: string;
  supplierId?: string;
  warehouseId?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminStockLedger`. */
export interface AdminStockLedgerQuery {
  from?: string;
  to?: string;
  reason?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminStockList`. */
export interface AdminStockListQuery {
  warehouseId?: string;
  listingId?: string;
  vendorId?: string;
  lowStock?: boolean;
  outOfStock?: boolean;
  search?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminStockReservations`. */
export interface AdminStockReservationsQuery {
  status?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminStockSerials`. */
export interface AdminStockSerialsQuery {
  status?: string;
}

/** Query string for `adminStockTakesList`. */
export interface AdminStockTakesListQuery {
  status?: string;
  warehouseId?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminSuppliersList`. */
export interface AdminSuppliersListQuery {
  activeOnly?: boolean;
  search?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminWarehousesList`. */
export interface AdminWarehousesListQuery {
  vendorId?: string;
  activeOnly?: boolean;
  search?: string;
  cursor?: string;
  size?: number;
}

/** `Inventory` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class InventoryApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Reads one goods receipt: what was accepted, what was refused, and why.
   * `GET /api/v1/admin/goods-receipts/{id}`
   */
  adminGoodsReceiptGet(id: string, options?: ApiRequestOptions): Observable<Models.GoodsReceiptResponse> {
    return this.http.request<Models.GoodsReceiptResponse>('GET', `${this.baseUrl}/api/v1/admin/goods-receipts/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Lists goods receipts, newest first.
   * `GET /api/v1/admin/goods-receipts`
   */
  adminGoodsReceiptsList(query?: AdminGoodsReceiptsListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfGoodsReceiptResponse> {
    return this.http.request<Models.PagedResultOfGoodsReceiptResponse>('GET', `${this.baseUrl}/api/v1/admin/goods-receipts`, undefined, query, options);
  }

  /**
   * Calls the order off. Refused once any of it has arrived.
   * `POST /api/v1/admin/purchase-orders/{id}/cancel`
   */
  adminPurchaseOrderCancel(id: string, options?: ApiRequestOptions): Observable<Models.PurchaseOrderResponse> {
    return this.http.request<Models.PurchaseOrderResponse>('POST', `${this.baseUrl}/api/v1/admin/purchase-orders/${encodeURIComponent(String(id))}/cancel`, undefined, undefined, options);
  }

  /**
   * Raises a draft purchase order. Nothing moves until goods are received.
   * `POST /api/v1/admin/purchase-orders`
   */
  adminPurchaseOrderCreate(body: Models.CreatePurchaseOrderBody, options?: ApiRequestOptions): Observable<Models.PurchaseOrderResponse> {
    return this.http.request<Models.PurchaseOrderResponse>('POST', `${this.baseUrl}/api/v1/admin/purchase-orders`, body, undefined, options);
  }

  /**
   * Reads one purchase order, lines and all.
   * `GET /api/v1/admin/purchase-orders/{id}`
   */
  adminPurchaseOrderGet(id: string, options?: ApiRequestOptions): Observable<Models.PurchaseOrderResponse> {
    return this.http.request<Models.PurchaseOrderResponse>('GET', `${this.baseUrl}/api/v1/admin/purchase-orders/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Books goods in against the order and returns the GRN. This is what moves stock.
   * `POST /api/v1/admin/purchase-orders/{id}/receive`
   */
  adminPurchaseOrderReceive(id: string, body: Models.ReceivePurchaseOrderBody, options?: ApiRequestOptions): Observable<Models.GoodsReceiptResponse> {
    return this.http.request<Models.GoodsReceiptResponse>('POST', `${this.baseUrl}/api/v1/admin/purchase-orders/${encodeURIComponent(String(id))}/receive`, body, undefined, options);
  }

  /**
   * Lists purchase orders without their lines, newest first.
   * `GET /api/v1/admin/purchase-orders`
   */
  adminPurchaseOrdersList(query?: AdminPurchaseOrdersListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfPurchaseOrderResponse> {
    return this.http.request<Models.PagedResultOfPurchaseOrderResponse>('GET', `${this.baseUrl}/api/v1/admin/purchase-orders`, undefined, query, options);
  }

  /**
   * Sends the order to its supplier. The lines are frozen from then on.
   * `POST /api/v1/admin/purchase-orders/{id}/submit`
   */
  adminPurchaseOrderSubmit(id: string, options?: ApiRequestOptions): Observable<Models.PurchaseOrderResponse> {
    return this.http.request<Models.PurchaseOrderResponse>('POST', `${this.baseUrl}/api/v1/admin/purchase-orders/${encodeURIComponent(String(id))}/submit`, undefined, undefined, options);
  }

  /**
   * Rewrites a draft. Refused once the order has been sent to the supplier.
   * `PUT /api/v1/admin/purchase-orders/{id}`
   */
  adminPurchaseOrderUpdate(id: string, body: Models.UpdatePurchaseOrderBody, options?: ApiRequestOptions): Observable<Models.PurchaseOrderResponse> {
    return this.http.request<Models.PurchaseOrderResponse>('PUT', `${this.baseUrl}/api/v1/admin/purchase-orders/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Moves stock by hand with a reason. Refused if it would take the location below empty.
   * `POST /api/v1/admin/stock/adjustments`
   */
  adminStockAdjust(body: Models.StockAdjustmentBody, options?: ApiRequestOptions): Observable<Models.StockItemResponse> {
    return this.http.request<Models.StockItemResponse>('POST', `${this.baseUrl}/api/v1/admin/stock/adjustments`, body, undefined, options);
  }

  /**
   * The lots held against one stock row, soonest to expire first.
   * `GET /api/v1/admin/stock/{id}/batches`
   */
  adminStockBatches(id: string, options?: ApiRequestOptions): Observable<Models.StockBatchResponse[]> {
    return this.http.request<Models.StockBatchResponse[]>('GET', `${this.baseUrl}/api/v1/admin/stock/${encodeURIComponent(String(id))}/batches`, undefined, undefined, options);
  }

  /**
   * Records a lot. A second delivery of the same lot number adds to it.
   * `POST /api/v1/admin/stock/{id}/batches`
   */
  adminStockBatchRecord(id: string, body: Models.StockBatchBody, options?: ApiRequestOptions): Observable<Models.StockBatchResponse> {
    return this.http.request<Models.StockBatchResponse>('POST', `${this.baseUrl}/api/v1/admin/stock/${encodeURIComponent(String(id))}/batches`, body, undefined, options);
  }

  /**
   * Sets the reorder level, the backorder and pre-order flags, and the tracking mode.
   * `PUT /api/v1/admin/stock/{id}/settings`
   */
  adminStockConfigure(id: string, body: Models.StockSettingsBody, options?: ApiRequestOptions): Observable<Models.StockItemResponse> {
    return this.http.request<Models.StockItemResponse>('PUT', `${this.baseUrl}/api/v1/admin/stock/${encodeURIComponent(String(id))}/settings`, body, undefined, options);
  }

  /**
   * Reads one stock row, with its on-hand, reserved and available counts.
   * `GET /api/v1/admin/stock/{id}`
   */
  adminStockGet(id: string, options?: ApiRequestOptions): Observable<Models.StockItemResponse> {
    return this.http.request<Models.StockItemResponse>('GET', `${this.baseUrl}/api/v1/admin/stock/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Every movement of one stock row, newest first. Append-only and complete.
   * `GET /api/v1/admin/stock/{id}/ledger`
   */
  adminStockLedger(id: string, query?: AdminStockLedgerQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfStockLedgerEntryResponse> {
    return this.http.request<Models.PagedResultOfStockLedgerEntryResponse>('GET', `${this.baseUrl}/api/v1/admin/stock/${encodeURIComponent(String(id))}/ledger`, undefined, query, options);
  }

  /**
   * Lists stock rows. A vendor caller sees only their own.
   * `GET /api/v1/admin/stock`
   */
  adminStockList(query?: AdminStockListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfStockItemResponse> {
    return this.http.request<Models.PagedResultOfStockItemResponse>('GET', `${this.baseUrl}/api/v1/admin/stock`, undefined, query, options);
  }

  /**
   * Opens a stock row for an offer at a location, at zero. One row per pair.
   * `POST /api/v1/admin/stock`
   */
  adminStockOpen(body: Models.OpenStockItemBody, options?: ApiRequestOptions): Observable<Models.StockItemResponse> {
    return this.http.request<Models.StockItemResponse>('POST', `${this.baseUrl}/api/v1/admin/stock`, body, undefined, options);
  }

  /**
   * The holds against one stock row: who has it, how much, and until when.
   * `GET /api/v1/admin/stock/{id}/reservations`
   */
  adminStockReservations(id: string, query?: AdminStockReservationsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfStockReservationResponse> {
    return this.http.request<Models.PagedResultOfStockReservationResponse>('GET', `${this.baseUrl}/api/v1/admin/stock/${encodeURIComponent(String(id))}/reservations`, undefined, query, options);
  }

  /**
   * The individually identified units held against one stock row.
   * `GET /api/v1/admin/stock/{id}/serials`
   */
  adminStockSerials(id: string, query?: AdminStockSerialsQuery, options?: ApiRequestOptions): Observable<Models.StockSerialResponse[]> {
    return this.http.request<Models.StockSerialResponse[]>('GET', `${this.baseUrl}/api/v1/admin/stock/${encodeURIComponent(String(id))}/serials`, undefined, query, options);
  }

  /**
   * Books units in by serial number. Numbers already known are skipped, not refused.
   * `POST /api/v1/admin/stock/{id}/serials`
   */
  adminStockSerialsRecord(id: string, body: Models.StockSerialsBody, options?: ApiRequestOptions): Observable<Models.StockSerialResponse[]> {
    return this.http.request<Models.StockSerialResponse[]>('POST', `${this.baseUrl}/api/v1/admin/stock/${encodeURIComponent(String(id))}/serials`, body, undefined, options);
  }

  /**
   * Abandons the count without posting anything.
   * `POST /api/v1/admin/stock-takes/{id}/cancel`
   */
  adminStockTakeCancel(id: string, options?: ApiRequestOptions): Observable<Models.StockTakeResponse> {
    return this.http.request<Models.StockTakeResponse>('POST', `${this.baseUrl}/api/v1/admin/stock-takes/${encodeURIComponent(String(id))}/cancel`, undefined, undefined, options);
  }

  /**
   * Records counts. Partial submissions are normal; uncounted rows stay uncounted.
   * `PUT /api/v1/admin/stock-takes/{id}/lines`
   */
  adminStockTakeCount(id: string, body: Models.StockTakeCountsBody, options?: ApiRequestOptions): Observable<Models.StockTakeResponse> {
    return this.http.request<Models.StockTakeResponse>('PUT', `${this.baseUrl}/api/v1/admin/stock-takes/${encodeURIComponent(String(id))}/lines`, body, undefined, options);
  }

  /**
   * Opens a count sheet with today's book figures frozen onto it.
   * `POST /api/v1/admin/stock-takes`
   */
  adminStockTakeCreate(body: Models.CreateStockTakeBody, options?: ApiRequestOptions): Observable<Models.StockTakeResponse> {
    return this.http.request<Models.StockTakeResponse>('POST', `${this.baseUrl}/api/v1/admin/stock-takes`, body, undefined, options);
  }

  /**
   * Reads one stock take with its sheet: expected, counted and the variance.
   * `GET /api/v1/admin/stock-takes/{id}`
   */
  adminStockTakeGet(id: string, options?: ApiRequestOptions): Observable<Models.StockTakeResponse> {
    return this.http.request<Models.StockTakeResponse>('GET', `${this.baseUrl}/api/v1/admin/stock-takes/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Lists stock takes without their sheets, newest first.
   * `GET /api/v1/admin/stock-takes`
   */
  adminStockTakesList(query?: AdminStockTakesListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfStockTakeResponse> {
    return this.http.request<Models.PagedResultOfStockTakeResponse>('GET', `${this.baseUrl}/api/v1/admin/stock-takes`, undefined, query, options);
  }

  /**
   * Posts one correction per non-zero variance. Uncounted rows are left alone.
   * `POST /api/v1/admin/stock-takes/{id}/submit`
   */
  adminStockTakeSubmit(id: string, options?: ApiRequestOptions): Observable<Models.StockTakeResponse> {
    return this.http.request<Models.StockTakeResponse>('POST', `${this.baseUrl}/api/v1/admin/stock-takes/${encodeURIComponent(String(id))}/submit`, undefined, undefined, options);
  }

  /**
   * Moves units between two locations as one movement, written as two ledger entries.
   * `POST /api/v1/admin/stock/transfers`
   */
  adminStockTransfer(body: Models.StockTransferBody, options?: ApiRequestOptions): Observable<Models.StockItemResponse[]> {
    return this.http.request<Models.StockItemResponse[]>('POST', `${this.baseUrl}/api/v1/admin/stock/transfers`, body, undefined, options);
  }

  /**
   * Adds a supplier. The code is unique across the store.
   * `POST /api/v1/admin/suppliers`
   */
  adminSupplierCreate(body: Models.CreateSupplierBody, options?: ApiRequestOptions): Observable<Models.SupplierResponse> {
    return this.http.request<Models.SupplierResponse>('POST', `${this.baseUrl}/api/v1/admin/suppliers`, body, undefined, options);
  }

  /**
   * Reads one supplier.
   * `GET /api/v1/admin/suppliers/{id}`
   */
  adminSupplierGet(id: string, options?: ApiRequestOptions): Observable<Models.SupplierResponse> {
    return this.http.request<Models.SupplierResponse>('GET', `${this.baseUrl}/api/v1/admin/suppliers/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Lists suppliers. A vendor caller sees their own and the platform's.
   * `GET /api/v1/admin/suppliers`
   */
  adminSuppliersList(query?: AdminSuppliersListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfSupplierResponse> {
    return this.http.request<Models.PagedResultOfSupplierResponse>('GET', `${this.baseUrl}/api/v1/admin/suppliers`, undefined, query, options);
  }

  /**
   * Restates a supplier, and opens or closes them to new orders.
   * `PUT /api/v1/admin/suppliers/{id}`
   */
  adminSupplierUpdate(id: string, body: Models.UpdateSupplierBody, options?: ApiRequestOptions): Observable<Models.SupplierResponse> {
    return this.http.request<Models.SupplierResponse>('PUT', `${this.baseUrl}/api/v1/admin/suppliers/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Opens a stock location. The code is unique across the store.
   * `POST /api/v1/admin/warehouses`
   */
  adminWarehouseCreate(body: Models.CreateWarehouseBody, options?: ApiRequestOptions): Observable<Models.WarehouseResponse> {
    return this.http.request<Models.WarehouseResponse>('POST', `${this.baseUrl}/api/v1/admin/warehouses`, body, undefined, options);
  }

  /**
   * Removes an empty location. One holding stock is closed instead, not removed.
   * `DELETE /api/v1/admin/warehouses/{id}`
   */
  adminWarehouseDelete(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/warehouses/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Reads one location. Answers 404 for a location outside the caller's scope.
   * `GET /api/v1/admin/warehouses/{id}`
   */
  adminWarehouseGet(id: string, options?: ApiRequestOptions): Observable<Models.WarehouseResponse> {
    return this.http.request<Models.WarehouseResponse>('GET', `${this.baseUrl}/api/v1/admin/warehouses/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Lists stock locations. A vendor caller sees their own and the platform's.
   * `GET /api/v1/admin/warehouses`
   */
  adminWarehousesList(query?: AdminWarehousesListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfWarehouseResponse> {
    return this.http.request<Models.PagedResultOfWarehouseResponse>('GET', `${this.baseUrl}/api/v1/admin/warehouses`, undefined, query, options);
  }

  /**
   * Renames a location, restates where it is, and opens or closes it.
   * `PUT /api/v1/admin/warehouses/{id}`
   */
  adminWarehouseUpdate(id: string, body: Models.UpdateWarehouseBody, options?: ApiRequestOptions): Observable<Models.WarehouseResponse> {
    return this.http.request<Models.WarehouseResponse>('PUT', `${this.baseUrl}/api/v1/admin/warehouses/${encodeURIComponent(String(id))}`, body, undefined, options);
  }
}
