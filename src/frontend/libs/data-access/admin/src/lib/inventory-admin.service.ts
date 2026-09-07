import { Injectable, inject } from '@angular/core';
import {
  CreatePurchaseOrderBody,
  CreateStockTakeBody,
  CreateSupplierBody,
  CreateWarehouseBody,
  GoodsReceiptResponse,
  InventoryApiClient,
  PurchaseOrderResponse,
  ReceivePurchaseOrderBody,
  StockAdjustmentBody,
  StockItemResponse,
  StockLedgerEntryResponse,
  StockSettingsBody,
  StockTakeCountsBody,
  StockTakeResponse,
  StockTransferBody,
  SupplierResponse,
  UpdatePurchaseOrderBody,
  UpdateSupplierBody,
  UpdateWarehouseBody,
  WarehouseResponse,
} from '@klarahome/data-access-api';
import { Observable, map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

export interface StockFilters {
  readonly warehouseId?: string;
  readonly listingId?: string;
  readonly vendorId?: string;
  /** The low-stock queue: on hand at or below the reorder level. */
  readonly lowStock?: boolean;
  readonly outOfStock?: boolean;
  readonly search?: string;
}

export interface StockLedgerFilters {
  readonly from?: string;
  readonly to?: string;
  readonly reason?: string;
}

export interface PurchaseOrderFilters {
  readonly status?: string;
  readonly supplierId?: string;
  readonly warehouseId?: string;
}

export interface StockTakeFilters {
  readonly status?: string;
  readonly warehouseId?: string;
}

export interface WarehouseFilters {
  readonly vendorId?: string;
  readonly activeOnly?: boolean;
  readonly search?: string;
}

export interface SupplierFilters {
  readonly activeOnly?: boolean;
  readonly search?: string;
}

/**
 * Stock, purchasing and counting.
 *
 * The one thing worth saying about this whole surface: **nothing here sets a quantity**. Every
 * write is a *movement* — an adjustment with a reason, a receipt against a purchase order, a
 * counted variance — because `inventory.stock_ledger` is append-only and the balance is derived
 * from it (Step 11). A screen offering "quantity on hand: [____]" would be asking the operator to
 * overwrite the one record that explains what happened, and there is deliberately no endpoint
 * that would let it.
 *
 * `adjust` therefore takes a signed `change` and a `reason` from a closed vocabulary, and the
 * ledger is the only view of the past. That is also why the low-stock queue is a filter on the
 * stock list rather than a table of its own: "low" is `quantityOnHand <= reorderLevel`, a property
 * of the row, and a second screen would be a second definition of it.
 */
@Injectable({ providedIn: 'root' })
export class InventoryAdminService {
  private readonly api = inject(InventoryApiClient);

  // ---- Stock ------------------------------------------------------------------------------------

  stock(filters: StockFilters = {}, pageSize = 25): CursorList<StockItemResponse, StockFilters> {
    return new CursorList<StockItemResponse, StockFilters>(
      (current, cursor, size) =>
        this.api
          .adminStockList({
            WarehouseId: current.warehouseId,
            ListingId: current.listingId,
            VendorId: current.vendorId,
            LowStock: current.lowStock,
            OutOfStock: current.outOfStock,
            Search: current.search,
            Cursor: cursor ?? undefined,
            Size: size,
          })
          .pipe(map((result): CursorPage<StockItemResponse> => result)),
      filters,
      pageSize,
    );
  }

  stockItem(id: string): Observable<StockItemResponse> {
    return this.api.adminStockGet(id);
  }

  /**
   * One stock item's ledger.
   *
   * The audit trail of the quantity: every movement, its reason, what it referenced and the
   * balance it left behind. Paged by cursor like everything else, newest first.
   */
  ledger(
    stockItemId: string,
    filters: StockLedgerFilters = {},
    pageSize = 25,
  ): CursorList<StockLedgerEntryResponse, StockLedgerFilters> {
    return new CursorList<StockLedgerEntryResponse, StockLedgerFilters>(
      (current, cursor, size) =>
        this.api
          .adminStockLedger(stockItemId, {
            From: current.from,
            To: current.to,
            Reason: current.reason,
            Cursor: cursor ?? undefined,
            Size: size,
          })
          .pipe(map((result): CursorPage<StockLedgerEntryResponse> => result)),
      filters,
      pageSize,
    );
  }

  /** A signed movement with a reason. There is no way to set a balance directly, by design. */
  adjust(body: StockAdjustmentBody): Observable<StockItemResponse> {
    return this.api.adminStockAdjust(body);
  }

  /** Two ledger rows, `TransferOut` and `TransferIn`, so the stock is never briefly nowhere. */
  transfer(body: StockTransferBody): Observable<StockItemResponse[]> {
    return this.api.adminStockTransfer(body);
  }

  configure(stockItemId: string, body: StockSettingsBody): Observable<StockItemResponse> {
    return this.api.adminStockConfigure(stockItemId, body);
  }

  /** Starts tracking a listing at a warehouse. The quantity begins at zero and is moved, not set. */
  open(listingId: string, warehouseId: string): Observable<StockItemResponse> {
    return this.api.adminStockOpen({ listingId, warehouseId });
  }

  // ---- Warehouses -------------------------------------------------------------------------------

  warehouses(filters: WarehouseFilters = {}, pageSize = 25): CursorList<WarehouseResponse, WarehouseFilters> {
    return new CursorList<WarehouseResponse, WarehouseFilters>(
      (current, cursor, size) =>
        this.api
          .adminWarehousesList({
            VendorId: current.vendorId,
            ActiveOnly: current.activeOnly,
            Search: current.search,
            Cursor: cursor ?? undefined,
            Size: size,
          })
          .pipe(map((result): CursorPage<WarehouseResponse> => result)),
      filters,
      pageSize,
    );
  }

  createWarehouse(body: CreateWarehouseBody): Observable<WarehouseResponse> {
    return this.api.adminWarehouseCreate(body);
  }

  updateWarehouse(id: string, body: UpdateWarehouseBody): Observable<WarehouseResponse> {
    return this.api.adminWarehouseUpdate(id, body);
  }

  deleteWarehouse(id: string): Observable<void> {
    return this.api.adminWarehouseDelete(id);
  }

  // ---- Suppliers --------------------------------------------------------------------------------

  suppliers(filters: SupplierFilters = {}, pageSize = 25): CursorList<SupplierResponse, SupplierFilters> {
    return new CursorList<SupplierResponse, SupplierFilters>(
      (current, cursor, size) =>
        this.api
          .adminSuppliersList({
            ActiveOnly: current.activeOnly,
            Search: current.search,
            Cursor: cursor ?? undefined,
            Size: size,
          })
          .pipe(map((result): CursorPage<SupplierResponse> => result)),
      filters,
      pageSize,
    );
  }

  createSupplier(body: CreateSupplierBody): Observable<SupplierResponse> {
    return this.api.adminSupplierCreate(body);
  }

  updateSupplier(id: string, body: UpdateSupplierBody): Observable<SupplierResponse> {
    return this.api.adminSupplierUpdate(id, body);
  }

  // ---- Purchase orders and goods receipts -------------------------------------------------------

  purchaseOrders(
    filters: PurchaseOrderFilters = {},
    pageSize = 25,
  ): CursorList<PurchaseOrderResponse, PurchaseOrderFilters> {
    return new CursorList<PurchaseOrderResponse, PurchaseOrderFilters>(
      (current, cursor, size) =>
        this.api
          .adminPurchaseOrdersList({
            Status: current.status,
            SupplierId: current.supplierId,
            WarehouseId: current.warehouseId,
            Cursor: cursor ?? undefined,
            Size: size,
          })
          .pipe(map((result): CursorPage<PurchaseOrderResponse> => result)),
      filters,
      pageSize,
    );
  }

  purchaseOrder(id: string): Observable<PurchaseOrderResponse> {
    return this.api.adminPurchaseOrderGet(id);
  }

  createPurchaseOrder(body: CreatePurchaseOrderBody): Observable<PurchaseOrderResponse> {
    return this.api.adminPurchaseOrderCreate(body);
  }

  updatePurchaseOrder(id: string, body: UpdatePurchaseOrderBody): Observable<PurchaseOrderResponse> {
    return this.api.adminPurchaseOrderUpdate(id, body);
  }

  submitPurchaseOrder(id: string): Observable<PurchaseOrderResponse> {
    return this.api.adminPurchaseOrderSubmit(id);
  }

  cancelPurchaseOrder(id: string): Observable<PurchaseOrderResponse> {
    return this.api.adminPurchaseOrderCancel(id);
  }

  /**
   * Receiving: the goods receipt note.
   *
   * Accepted and rejected are counted separately per line, because they are: a receipt that
   * recorded only "30 arrived" against an order of 40 cannot say whether ten were short-shipped or
   * ten were damaged, and those are a supplier conversation and an insurance claim respectively.
   * Only the accepted quantity moves the ledger.
   */
  receivePurchaseOrder(id: string, body: ReceivePurchaseOrderBody): Observable<GoodsReceiptResponse> {
    return this.api.adminPurchaseOrderReceive(id, body);
  }

  goodsReceipts(
    purchaseOrderId?: string,
    pageSize = 25,
  ): CursorList<GoodsReceiptResponse, { readonly purchaseOrderId?: string }> {
    return new CursorList<GoodsReceiptResponse, { readonly purchaseOrderId?: string }>(
      (current, cursor, size) =>
        this.api
          .adminGoodsReceiptsList({
            purchaseOrderId: current.purchaseOrderId,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<GoodsReceiptResponse> => result)),
      { purchaseOrderId },
      pageSize,
    );
  }

  goodsReceipt(id: string): Observable<GoodsReceiptResponse> {
    return this.api.adminGoodsReceiptGet(id);
  }

  // ---- Stock takes ------------------------------------------------------------------------------

  stockTakes(filters: StockTakeFilters = {}, pageSize = 25): CursorList<StockTakeResponse, StockTakeFilters> {
    return new CursorList<StockTakeResponse, StockTakeFilters>(
      (current, cursor, size) =>
        this.api
          .adminStockTakesList({
            Status: current.status,
            WarehouseId: current.warehouseId,
            Cursor: cursor ?? undefined,
            Size: size,
          })
          .pipe(map((result): CursorPage<StockTakeResponse> => result)),
      filters,
      pageSize,
    );
  }

  stockTake(id: string): Observable<StockTakeResponse> {
    return this.api.adminStockTakeGet(id);
  }

  createStockTake(body: CreateStockTakeBody): Observable<StockTakeResponse> {
    return this.api.adminStockTakeCreate(body);
  }

  /** Records counts. Saved as often as the counter likes; nothing moves until it is submitted. */
  countStockTake(id: string, body: StockTakeCountsBody): Observable<StockTakeResponse> {
    return this.api.adminStockTakeCount(id, body);
  }

  /** Submitting is what writes the variances to the ledger, as `Correction` movements. */
  submitStockTake(id: string): Observable<StockTakeResponse> {
    return this.api.adminStockTakeSubmit(id);
  }

  cancelStockTake(id: string): Observable<StockTakeResponse> {
    return this.api.adminStockTakeCancel(id);
  }
}
