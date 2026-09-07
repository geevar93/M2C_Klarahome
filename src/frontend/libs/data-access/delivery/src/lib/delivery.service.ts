import { Injectable, inject } from '@angular/core';
import { ServiceabilityResponse, ShippingApiClient } from '@klarahome/data-access-api';
import { BrowserStorage } from '@klarahome/util';
import { Observable, catchError, of, shareReplay, tap } from 'rxjs';

/** Where the shopper's PIN code is remembered between visits. */
const PINCODE_KEY = 'kh.delivery.pincode';

/**
 * "Can you deliver to my PIN code, and can I pay cash?"
 *
 * PIN-code-first is a stated behaviour of this storefront
 * (docs/05-frontend-architecture.md §3.6): the code is asked for on the product page, remembered,
 * and pre-applied at checkout. This service owns all three halves of that — the check, the memory
 * and the cache — so the PDP, the cart and the checkout cannot disagree about what was entered.
 *
 * **Answers are cached per PIN code for the life of the page.** Serviceability is a property of a
 * PIN code and a courier network, not of a product, and the API already caches it server-side
 * (Step 16A); a shopper comparing four products should not send four identical requests.
 *
 * The remembered code lives in `localStorage` through `BrowserStorage`, which answers null during
 * server rendering rather than throwing — so the PDP renders on the server with no estimate and
 * fills one in on hydration, which is the correct order: an SSR document that carried one
 * visitor's PIN code could not be edge-cached (Step 32).
 */
@Injectable({ providedIn: 'root' })
export class DeliveryService {
  private readonly api = inject(ShippingApiClient);
  private readonly storage = inject(BrowserStorage);
  private readonly cache = new Map<string, Observable<ServiceabilityResponse>>();

  /** The PIN code this visitor last checked, or an empty string. */
  remembered(): string {
    return this.storage.get(PINCODE_KEY) ?? '';
  }

  /**
   * Checks a PIN code, remembering it if the API recognised the format.
   *
   * A refusal is a *result*, not an error: "we do not deliver to this area yet" comes back as a
   * 200 with `deliverable: false` and a message that distinguishes an uncovered area from an
   * unserviceable PIN code (Step 16A). Only a transport failure lands in `catchError`, and that
   * answers a not-deliverable result too — a shopper cannot act on the difference, and a toast
   * over a product page they are reading is not the way to say it.
   */
  check(pincode: string): Observable<ServiceabilityResponse> {
    const cached = this.cache.get(pincode);
    if (cached) return cached;

    const request = this.api.storeShippingServiceability(pincode, { silentErrors: true }).pipe(
      tap(() => this.storage.set(PINCODE_KEY, pincode)),
      catchError(() =>
        of<ServiceabilityResponse>({
          pincode,
          deliverable: false,
          covered: false,
          isServiceable: false,
          prepaidOk: false,
          codOk: false,
          etaDays: null,
          courier: null,
          city: null,
          state: null,
          reason: 'SERVICE_UNAVAILABLE',
          message: 'We could not check this PIN code just now. Please try again.',
          checkedAt: null,
        }),
      ),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    this.cache.set(pincode, request);
    return request;
  }

  /** Forgets the remembered code. For a "deliver somewhere else" control. */
  forget(): void {
    this.storage.remove(PINCODE_KEY);
  }
}
