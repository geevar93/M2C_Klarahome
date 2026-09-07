import { Injectable, inject } from '@angular/core';
import {
  AdjustmentBody,
  LedgerEntryResponse,
  LedgerStatementResponse,
  PayoutBatchResponse,
  PlatformRevenueResponse,
  SettlementCycleResponse,
  SettlementsApiClient,
  StatutoryExtractResponse,
  VendorBalanceResponse,
} from '@klarahome/data-access-api';
import { Observable, map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

export interface LedgerFilters {
  readonly vendorId?: string;
  readonly entryType?: string;
  readonly cycleId?: string;
  readonly from?: string;
  readonly to?: string;
}

export interface CycleFilters {
  readonly vendorId?: string;
  readonly status?: string;
  readonly from?: string;
  readonly to?: string;
}

export interface PayoutFilters {
  readonly status?: string;
  readonly from?: string;
  readonly to?: string;
}

export interface StatementRange {
  readonly from?: string;
  readonly to?: string;
}

/**
 * Money owed, and money sent.
 *
 * **Nothing in this service edits a balance, because there is no balance to edit.** A seller's
 * balance is `Σ credits − Σ debits` over an append-only ledger (Step 18), so the only write that
 * changes it is `postAdjustment`, which adds a signed row carrying its reason. A screen that
 * offered "set balance" would be asking for a column that does not exist, and building one would
 * throw away the reason every rupee moved.
 *
 * **The payout batch is maker–checker, and it is refused in three places.** The person who creates
 * a batch may not approve it: the handler refuses it, the aggregate refuses it, and a database
 * `CHECK` refuses it. This service therefore exposes create, approve, process and cancel as four
 * separate calls rather than a "pay these sellers" convenience — the separation *is* the control.
 * `PayoutBatchResponse.nextStatuses` carries which of them the batch will currently accept, so the
 * screen offers edges rather than guessing at them.
 *
 * **Closing a period is not the same as paying it.** `closeCycle` draws the line under a half-open
 * period and computes what is payable, holding back what returns may still claw back;
 * `createPayoutBatch` then gathers closed cycles into a payment run. Two operations because they
 * are two decisions, usually taken by two people on two different days.
 */
@Injectable({ providedIn: 'root' })
export class SettlementsAdminService {
  private readonly api = inject(SettlementsApiClient);

  // ---- Cycles -----------------------------------------------------------------------------------

  cycles(filters: CycleFilters = {}, pageSize = 25): CursorList<SettlementCycleResponse, CycleFilters> {
    return new CursorList<SettlementCycleResponse, CycleFilters>(
      (current, cursor, size) =>
        this.api
          .adminListSettlementCycles({
            vendorId: current.vendorId,
            status: current.status,
            from: current.from,
            to: current.to,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<SettlementCycleResponse> => result)),
      filters,
      pageSize,
    );
  }

  cycle(id: string): Observable<SettlementCycleResponse> {
    return this.api.adminGetSettlementCycle(id);
  }

  /** Closes one open cycle. `force` overrides the return hold, and is a decision with a name. */
  closeCycle(id: string, force = false): Observable<SettlementCycleResponse> {
    return this.api.adminCloseSettlementCycle(id, { vendorId: null, force });
  }

  /** Closes the current period for one seller, or for every seller when `vendorId` is null. */
  closePeriod(vendorId: string | null, force = false): Observable<SettlementCycleResponse> {
    return this.api.adminCloseVendorSettlementPeriod({ vendorId, force });
  }

  // ---- Payout batches ---------------------------------------------------------------------------

  payoutBatches(filters: PayoutFilters = {}, pageSize = 25): CursorList<PayoutBatchResponse, PayoutFilters> {
    return new CursorList<PayoutBatchResponse, PayoutFilters>(
      (current, cursor, size) =>
        this.api
          .adminListPayoutBatches({
            status: current.status,
            from: current.from,
            to: current.to,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<PayoutBatchResponse> => result)),
      filters,
      pageSize,
    );
  }

  payoutBatch(id: string): Observable<PayoutBatchResponse> {
    return this.api.adminGetPayoutBatch(id);
  }

  /** The maker's step. An empty `cycleIds` gathers every closed, unpaid cycle. */
  createPayoutBatch(cycleIds: readonly string[] | null): Observable<PayoutBatchResponse> {
    return this.api.adminCreatePayoutBatch({ cycleIds: cycleIds ? [...cycleIds] : null });
  }

  /** The checker's step. Refused for the maker in the handler, the aggregate and the database. */
  approvePayoutBatch(id: string): Observable<PayoutBatchResponse> {
    return this.api.adminApprovePayoutBatch(id);
  }

  /** Hands the approved batch to the payout provider. Money leaves here. */
  processPayoutBatch(id: string): Observable<PayoutBatchResponse> {
    return this.api.adminProcessPayoutBatch(id);
  }

  cancelPayoutBatch(id: string, reason: string | null): Observable<PayoutBatchResponse> {
    return this.api.adminCancelPayoutBatch(id, { reason });
  }

  // ---- The ledger -------------------------------------------------------------------------------

  ledger(filters: LedgerFilters = {}, pageSize = 50): CursorList<LedgerEntryResponse, LedgerFilters> {
    return new CursorList<LedgerEntryResponse, LedgerFilters>(
      (current, cursor, size) =>
        this.api
          .adminListLedgerEntries({
            vendorId: current.vendorId,
            entryType: current.entryType,
            cycleId: current.cycleId,
            from: current.from,
            to: current.to,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<LedgerEntryResponse> => result)),
      filters,
      pageSize,
    );
  }

  /** One seller's statement: opening balance, the movements, closing balance. */
  statement(vendorId: string, range: StatementRange = {}): Observable<LedgerStatementResponse> {
    return this.api.adminGetVendorStatement(vendorId, { from: range.from, to: range.to });
  }

  balance(vendorId: string): Observable<VendorBalanceResponse> {
    return this.api.adminGetVendorBalance(vendorId);
  }

  /** A signed row with a reason — the only write that moves a balance. */
  postAdjustment(body: AdjustmentBody): Observable<LedgerEntryResponse> {
    return this.api.adminPostSettlementAdjustment(body);
  }

  // ---- What the platform kept -------------------------------------------------------------------

  platformRevenue(range: StatementRange = {}): Observable<PlatformRevenueResponse> {
    return this.api.adminGetPlatformRevenue({ from: range.from, to: range.to });
  }

  /**
   * TCS and TDS, per seller, for a period.
   *
   * Two taxes on **two different bases** (Step 18): TCS under CGST s.52 on net taxable supplies,
   * TDS under s.194-O on gross sales. They are reported together because one filing needs both,
   * and they are never added together for the same reason.
   */
  statutoryExtract(range: StatementRange = {}, vendorId?: string): Observable<StatutoryExtractResponse> {
    return this.api.adminGetStatutoryExtract({ from: range.from, to: range.to, vendorId });
  }
}
