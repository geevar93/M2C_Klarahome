import { Injectable, inject } from '@angular/core';
import {
  ApproveReturnBody,
  CreditNoteResponse,
  QcBody,
  RefundReturnBody,
  ReturnReasonBody,
  ReturnReasonResponse,
  ReturnResponse,
  ReturnSummaryResponse,
  ReturnsApiClient,
} from '@klarahome/data-access-api';
import { Observable, map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

export interface ReturnFilters {
  readonly status?: string;
  readonly vendorId?: string;
  readonly orderId?: string;
  readonly from?: string;
  readonly to?: string;
}

export interface CreditNoteFilters {
  readonly vendorId?: string;
  readonly financialYear?: string;
  readonly from?: string;
  readonly to?: string;
}

/**
 * Returns, from the customer's request to the money going back.
 *
 * The order the endpoints appear in below is the order an RMA actually moves through, and every
 * one of them is a decision somebody makes rather than a field somebody edits: **approve or
 * reject → schedule the pickup → receive it → inspect it → refund or replace → close**.
 *
 * Which of those are offered at any moment is not decided here and not decided by the screen:
 * `ReturnResponse.nextStatuses` carries the edges the API's own transition table will accept
 * (Step 17, where the table is data precisely so that a back-office button cannot invent an edge).
 * A screen that hard-coded "show Refund when status is Received" would be a second copy of that
 * table, and the copy is the one that goes stale.
 *
 * Two things are worth knowing about the money. **QC comes before the stock moves**: inspection
 * says what was accepted and what its disposition is, and only then does anything go back on the
 * shelf. And **a refund and a credit note are different records**: the credit note reduces the
 * seller's output tax and exists whether or not money moved, which is why it is fetched
 * separately rather than being a field on the return.
 */
@Injectable({ providedIn: 'root' })
export class ReturnsAdminService {
  private readonly api = inject(ReturnsApiClient);

  returns(filters: ReturnFilters = {}, pageSize = 25): CursorList<ReturnSummaryResponse, ReturnFilters> {
    return new CursorList<ReturnSummaryResponse, ReturnFilters>(
      (current, cursor, size) =>
        this.api
          .adminListReturns({
            status: current.status,
            vendorId: current.vendorId,
            orderId: current.orderId,
            from: current.from,
            to: current.to,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<ReturnSummaryResponse> => result)),
      filters,
      pageSize,
    );
  }

  return_(id: string): Observable<ReturnResponse> {
    return this.api.adminGetReturn(id);
  }

  // ---- The decisions ----------------------------------------------------------------------------

  /**
   * Agrees to the return.
   *
   * `amount` overrides the estimate the reason code computed, and `pickupRequired` overrides what
   * the reason code said about collection — both nullable, both meaning "leave the policy's
   * answer alone". An agent who has spoken to the customer knows things the policy does not.
   */
  approve(id: string, body: ApproveReturnBody): Observable<ReturnResponse> {
    return this.api.adminApproveReturn(id, body);
  }

  reject(id: string, reason: string | null): Observable<ReturnResponse> {
    return this.api.adminRejectReturn(id, { reason });
  }

  /** The reverse shipment: an ordinary shipment with its addresses inverted. */
  schedulePickup(id: string, pickupAt: string | null): Observable<ReturnResponse> {
    return this.api.adminScheduleReturnPickup(id, { pickupAt });
  }

  receive(id: string, note: string | null): Observable<ReturnResponse> {
    return this.api.adminReceiveReturn(id, { note });
  }

  /**
   * Quality control.
   *
   * Per line, because a two-item return is routinely one saleable and one not, and a single
   * pass/fail would refund both or neither. The disposition on each line is what decides where
   * the unit goes — back to saleable stock, to damaged, or scrapped — and it is recorded before
   * any stock moves.
   */
  inspect(id: string, body: QcBody): Observable<ReturnResponse> {
    return this.api.adminInspectReturn(id, body);
  }

  /** To the original instrument, or to store credit. `mode` null takes the policy's answer. */
  refund(id: string, body: RefundReturnBody): Observable<ReturnResponse> {
    return this.api.adminRefundReturn(id, body);
  }

  replace(id: string, replacementOrderId: string | null, note: string | null): Observable<ReturnResponse> {
    return this.api.adminReplaceReturn(id, { replacementOrderId, note });
  }

  close(id: string, note: string | null): Observable<ReturnResponse> {
    return this.api.adminCloseReturn(id, { note });
  }

  // ---- Reference data ---------------------------------------------------------------------------

  /**
   * The reason codes, each carrying its own policy.
   *
   * Read by the returns screens rather than hard-coded, because a reason is not a label: it says
   * whether evidence is needed, whether the goods come back, who pays the return freight and
   * whether it counts against the seller. Those are the facts the QC and refund screens explain
   * their own defaults with.
   */
  reasons(includeInactive = false): Observable<ReturnReasonResponse[]> {
    return this.api.adminListReturnReasons({ includeInactive });
  }

  createReason(body: ReturnReasonBody): Observable<ReturnReasonResponse> {
    return this.api.adminCreateReturnReason(body);
  }

  updateReason(id: string, body: ReturnReasonBody): Observable<ReturnReasonResponse> {
    return this.api.adminUpdateReturnReason(id, body);
  }

  // ---- Credit notes -----------------------------------------------------------------------------

  creditNotes(
    filters: CreditNoteFilters = {},
    pageSize = 25,
  ): CursorList<CreditNoteResponse, CreditNoteFilters> {
    return new CursorList<CreditNoteResponse, CreditNoteFilters>(
      (current, cursor, size) =>
        this.api
          .adminListCreditNotes({
            vendorId: current.vendorId,
            financialYear: current.financialYear,
            from: current.from,
            to: current.to,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<CreditNoteResponse> => result)),
      filters,
      pageSize,
    );
  }

  /** The credit note raised against one return, where one has been. */
  creditNoteForReturn(returnId: string): Observable<CreditNoteResponse> {
    return this.api.adminGetReturnCreditNote(returnId);
  }
}
