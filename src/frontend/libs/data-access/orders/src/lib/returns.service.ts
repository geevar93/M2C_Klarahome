import { Injectable, inject } from '@angular/core';
import {
  PagedResultOfReturnSummaryResponse,
  RaiseReturnBody,
  ReturnEligibilityResponse,
  ReturnResponse,
  ReturnsApiClient,
} from '@klarahome/data-access-api';
import { Observable } from 'rxjs';

/**
 * Returns and replacements, from the customer's side.
 *
 * The shape of this service follows the one rule that makes returns work: **ask before offering.**
 * `eligibility` answers which lines may be sent back, how many of each, until when, why not if not,
 * and which reasons apply — all resolved by the server against a policy that runs product →
 * vendor → store, first opinion winning (Step 17). The form is built from that answer, so a
 * customer is never offered a return the policy will refuse.
 *
 * Eligibility is per **sub-order**, not per order, because the policy and the window belong to the
 * seller. A basket from two sellers can have one returnable half and one that is past its window,
 * and a single order-level "return" button could only be wrong about one of them.
 */
@Injectable({ providedIn: 'root' })
export class ReturnsService {
  private readonly api = inject(ReturnsApiClient);

  /**
   * What may be sent back from one seller's part of an order.
   *
   * The refusal is a result, not an error: a window that has closed comes back as `isEligible:
   * false` with the reason, because that is something to show the customer rather than something
   * to fail on.
   */
  eligibility(subOrderNumber: string): Observable<ReturnEligibilityResponse> {
    return this.api.storeReturnEligibility(subOrderNumber, { silentErrors: true });
  }

  raise(body: RaiseReturnBody): Observable<ReturnResponse> {
    return this.api.storeRaiseReturn(body, { silentErrors: true });
  }

  list(
    options: { status?: string | null; cursor?: string | null; size?: number } = {},
  ): Observable<PagedResultOfReturnSummaryResponse> {
    return this.api.storeListReturns(
      {
        status: options.status ?? undefined,
        cursor: options.cursor ?? undefined,
        size: options.size ?? 10,
      },
      { silentErrors: true },
    );
  }

  get(returnNumber: string): Observable<ReturnResponse> {
    return this.api.storeGetReturn(returnNumber);
  }

  /**
   * Withdraws a request.
   *
   * Only while the seller has not acted on it — after that the transition table refuses, which is
   * the same table the customer's buttons are drawn from (`nextStatuses` on the return).
   */
  cancel(returnNumber: string, reason: string | null): Observable<ReturnResponse> {
    return this.api.storeCancelReturn(returnNumber, { reason }, { silentErrors: true });
  }
}
