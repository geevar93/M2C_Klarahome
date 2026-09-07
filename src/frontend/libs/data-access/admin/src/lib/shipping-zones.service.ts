import { Injectable, inject } from '@angular/core';
import {
  DeliveryCoverageResponse,
  RateBody,
  ServiceabilityResponse,
  ShippingApiClient,
  ShippingRateResponse,
  ShippingZoneResponse,
  ZoneBody,
} from '@klarahome/data-access-api';
import { Observable } from 'rxjs';

/**
 * What it costs to send a parcel somewhere.
 *
 * **A zone is a set of destinations; a rate is a band inside a zone.** Neither is paged, because
 * a store has a handful of zones and a few dozen bands, and a rate card is read as a whole or not
 * at all — a banded price list split across cursors is one nobody can check for gaps.
 *
 * Two properties of the model decide how the screen behaves:
 *
 *  - **Zones are ordered, and the first match wins.** `priority` is therefore the most important
 *    field on the form, and a catch-all zone exists so that no PIN code falls through. A screen
 *    that let somebody reorder zones without seeing the catch-all would be one where a typo
 *    silently makes half of India unshippable.
 *  - **Rates overlap on purpose.** A band is (weight range × order-value range), with an optional
 *    per-seller override, and the engine picks the narrowest match. So the list is shown grouped
 *    by zone with the overrides beside the defaults, rather than as one flat table in which two
 *    rows that differ only by `vendorId` look like a duplicate.
 *
 * `coverage()` and `testPincode()` are the read-only half: which areas the deployment currently
 * serves (Step 16A — Hyderabad by default, editable without a deploy) and what the engine would
 * answer for one PIN code today. They are here rather than on a settings screen because the
 * question an operator actually asks is "why can this customer not order", and the answer is
 * either coverage or serviceability — two distinct refusals the API keeps distinct.
 */
@Injectable({ providedIn: 'root' })
export class ShippingZonesService {
  private readonly api = inject(ShippingApiClient);

  zones(includeInactive = true): Observable<ShippingZoneResponse[]> {
    return this.api.adminListShippingZones({ includeInactive });
  }

  createZone(body: ZoneBody): Observable<ShippingZoneResponse> {
    return this.api.adminCreateShippingZone(body);
  }

  updateZone(id: string, body: ZoneBody): Observable<ShippingZoneResponse> {
    return this.api.adminUpdateShippingZone(id, body);
  }

  rates(zoneId?: string, includeInactive = true): Observable<ShippingRateResponse[]> {
    return this.api.adminListShippingRates({ zoneId, includeInactive });
  }

  createRate(body: RateBody): Observable<ShippingRateResponse> {
    return this.api.adminCreateShippingRate(body);
  }

  updateRate(id: string, body: RateBody): Observable<ShippingRateResponse> {
    return this.api.adminUpdateShippingRate(id, body);
  }

  /** Which areas this deployment serves at all. Editable without a deploy; read-only here. */
  coverage(): Observable<DeliveryCoverageResponse> {
    return this.api.adminGetDeliveryCoverage();
  }

  /**
   * What the engine answers for one PIN code, right now.
   *
   * `DELIVERY_AREA_NOT_COVERED` and `PINCODE_NOT_SERVICEABLE` are different refusals — the first
   * is our decision, the second is the courier's — and keeping them apart is what lets an operator
   * answer "we do not deliver there yet" rather than "the courier is down".
   */
  testPincode(pincode: string): Observable<ServiceabilityResponse> {
    return this.api.adminTestDeliveryCoverage(pincode);
  }
}
