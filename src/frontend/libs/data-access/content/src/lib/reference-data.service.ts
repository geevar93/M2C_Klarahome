import { Injectable, inject } from '@angular/core';
import { PincodeResponse, PlatformApiClient, StateResponse } from '@klarahome/data-access-api';
import { Observable, catchError, map, of, shareReplay } from 'rxjs';

/**
 * India's states, and what a PIN code resolves to.
 *
 * Both are reference data the platform module publishes anonymously (Step 6), and both are needed
 * by exactly one screen shape — the address form — which is why they live together.
 *
 * **The state list is cached for the life of the page.** It is thirty-six rows that change when
 * Parliament redraws a border; re-reading it every time a form opens would be a request per
 * checkout for a list that has not moved since the build.
 *
 * **The PIN code lookup is cached per code.** A customer correcting a typo types 500081 twice, and
 * the answer is a property of the code rather than of the moment. Note this is *not* the
 * serviceability check — that is `DeliveryService`, and it answers a different question: this one
 * says where a PIN code is, that one says whether we deliver there.
 */
@Injectable({ providedIn: 'root' })
export class ReferenceDataService {
  private readonly api = inject(PlatformApiClient);

  private states$?: Observable<readonly StateResponse[]>;
  private readonly pincodes = new Map<string, Observable<PincodeResponse | null>>();

  /**
   * The states, sorted by name.
   *
   * Sorted here rather than relied on from the API: the address form's `<select>` is the one place
   * a customer scans thirty-six items looking for theirs, and alphabetical is the only order that
   * makes that possible.
   */
  states(): Observable<readonly StateResponse[]> {
    this.states$ ??= this.api.storeStatesGet({ silentErrors: true }).pipe(
      map((states) => [...states].sort((left, right) => left.name.localeCompare(right.name))),
      // An empty list rather than a failure: an address form with a blank state dropdown is a form
      // that cannot be submitted, but it is still a page — and the toast this would otherwise raise
      // lands on a customer who can do nothing about it.
      catchError(() => of<readonly StateResponse[]>([])),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.states$;
  }

  /**
   * The city and state a PIN code belongs to, or null when it is not one we know.
   *
   * Null, not an error: an unrecognised PIN code is a thing a customer can type, and the form's
   * answer to it is to let them fill the city in themselves.
   */
  pincode(code: string): Observable<PincodeResponse | null> {
    const cached = this.pincodes.get(code);
    if (cached) return cached;

    const request = this.api.storePincodeGet(code, { silentErrors: true, showLoading: false }).pipe(
      map((response) => response as PincodeResponse | null),
      catchError(() => of(null)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    this.pincodes.set(code, request);
    return request;
  }
}
