import { Injectable, inject } from '@angular/core';
import {
  PagedResultOfWalletTransactionResponse,
  PricingApiClient,
  WalletResponse,
} from '@klarahome/data-access-api';
import { Observable, catchError, of } from 'rxjs';

/**
 * Store credit — the balance and the ledger behind it.
 *
 * The wallet is an **append-only ledger** on the server (Step 12): the balance is the sum of its
 * movements, never a column somebody could set. That is why this service reads two things rather
 * than one, and why the page shows both — a customer whose balance is not what they expect can see
 * the refund, the expiry or the redemption that explains it, which is the difference between a
 * number they trust and one they write in about.
 *
 * **Store credit is behind a feature flag** (`pricing.wallet`, Step 12). A deployment with it off
 * answers 404 or an empty balance, and the page is not routed to at all — the flag is checked by
 * the route, not here, because a service that silently swallowed the difference would leave the
 * account menu offering a page that says nothing.
 */
@Injectable({ providedIn: 'root' })
export class WalletService {
  private readonly api = inject(PricingApiClient);

  /**
   * The balance.
   *
   * A customer who has never been refunded has no wallet row, which the API answers as a failure
   * rather than as a zero. That is not something to show them: `null` here means "nothing to show",
   * and the page renders an empty state rather than an error.
   */
  balance(): Observable<WalletResponse | null> {
    return this.api.storeWallet({ silentErrors: true }).pipe(catchError(() => of(null)));
  }

  /** A page of movements, newest first, keyset-paged like every list on this API. */
  transactions(cursor?: string | null, size = 20): Observable<PagedResultOfWalletTransactionResponse> {
    return this.api.storeWalletTransactions({ cursor: cursor ?? undefined, size }, { silentErrors: true });
  }
}
