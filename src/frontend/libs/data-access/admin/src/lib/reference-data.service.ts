import { Injectable, inject } from '@angular/core';
import { PincodeResponse, PlatformApiClient, StateResponse } from '@klarahome/data-access-api';
import { Observable, shareReplay } from 'rxjs';

/**
 * The platform's jurisdiction list, for the back office.
 *
 * **Four screens asked an operator to type a raw `stateId`** — a warehouse, a pickup address, a
 * serviceable region and the promotion simulator's place of supply — because nothing in the back
 * office read the reference data at all. Step 28B gave them this (deliverable 3).
 *
 * **It reads the storefront's operations, and there is deliberately no `/admin` copy of them.** The
 * generated client is grouped by tag rather than by surface, so `PlatformApiClient` already carries
 * `storeStatesGet` alongside `adminSettingsGet` and the back office can call it directly. A second
 * pair of endpoints under `/admin` would have been the same two queries mapped twice, and — because
 * they answer with facts that are printed on every invoice in the country — two routes that wanted
 * no permission on a surface where every other route must declare one.
 *
 * **The list is fetched once per session and shared.** There are thirty-six states and union
 * territories, they change on the order of once a decade, and every form that needs them needs the
 * same list — so `shareReplay` here rather than a request per drawer opening. It is not cached
 * across sessions: the server already marks it as long-lived reference data and the browser's own
 * cache is the right place for that.
 */
@Injectable({ providedIn: 'root' })
export class ReferenceDataService {
  private readonly api = inject(PlatformApiClient);

  /** Every state and union territory, with its GST state code, alphabetically. */
  readonly states: Observable<StateResponse[]> = this.api
    .storeStatesGet()
    .pipe(shareReplay({ bufferSize: 1, refCount: false }));

  /**
   * City, district and state for a PIN code.
   *
   * Not shared: a lookup is about one code somebody just typed, and holding every one of them for
   * the session would be a cache of the postal system.
   */
  pincode(code: string): Observable<PincodeResponse> {
    return this.api.storePincodeGet(code);
  }
}
