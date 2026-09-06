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

/** Query string for `adminListCourierEvents`. */
export interface AdminListCourierEventsQuery {
  status?: string;
  awb?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListManifests`. */
export interface AdminListManifestsQuery {
  vendorId?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListNdr`. */
export interface AdminListNdrQuery {
  action?: string;
  vendorId?: string;
  reasonCode?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListShipments`. */
export interface AdminListShipmentsQuery {
  status?: string;
  vendorId?: string;
  orderId?: string;
  subOrderId?: string;
  awb?: string;
  q?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListShippingRates`. */
export interface AdminListShippingRatesQuery {
  zoneId?: string;
  vendorId?: string;
  method?: string;
  includeInactive?: boolean;
}

/** Query string for `adminListShippingZones`. */
export interface AdminListShippingZonesQuery {
  includeInactive?: boolean;
}

/** Query string for `adminShipmentPickList`. */
export interface AdminShipmentPickListQuery {
  vendorId?: string;
  size?: number;
}

/** `Shipping` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class ShippingApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Reattempt, reschedule, correct the address, or send the parcel back.
   * `POST /api/v1/admin/ndr/{id}/action`
   */
  adminActionNdr(id: string, body: Models.NdrActionBody, options?: ApiRequestOptions): Observable<Models.NdrResponse> {
    return this.http.request<Models.NdrResponse>('POST', `${this.baseUrl}/api/v1/admin/ndr/${encodeURIComponent(String(id))}/action`, body, undefined, options);
  }

  /**
   * Books a packed parcel with a courier, or records an air waybill obtained by hand.
   * `POST /api/v1/admin/shipments/{id}/book`
   */
  adminBookShipment(id: string, body?: null | Models.BookBody, options?: ApiRequestOptions): Observable<Models.ShipmentResponse> {
    return this.http.request<Models.ShipmentResponse>('POST', `${this.baseUrl}/api/v1/admin/shipments/${encodeURIComponent(String(id))}/book`, body, undefined, options);
  }

  /**
   * Calls off a parcel the courier has not yet collected.
   * `POST /api/v1/admin/shipments/{id}/cancel`
   */
  adminCancelShipment(id: string, body?: null | Models.CancelShipmentBody, options?: ApiRequestOptions): Observable<Models.ShipmentResponse> {
    return this.http.request<Models.ShipmentResponse>('POST', `${this.baseUrl}/api/v1/admin/shipments/${encodeURIComponent(String(id))}/cancel`, body, undefined, options);
  }

  /**
   * Records what the packer weighed and measured.
   * `POST /api/v1/admin/shipments/{id}/weight`
   */
  adminCaptureShipmentWeight(id: string, body: Models.WeighBody, options?: ApiRequestOptions): Observable<Models.ShipmentResponse> {
    return this.http.request<Models.ShipmentResponse>('POST', `${this.baseUrl}/api/v1/admin/shipments/${encodeURIComponent(String(id))}/weight`, body, undefined, options);
  }

  /**
   * Produces the handover sheet a courier's driver signs.
   * `POST /api/v1/admin/manifests`
   */
  adminCreateManifest(body?: null | Models.CreateManifestBody, options?: ApiRequestOptions): Observable<Models.ManifestResponse> {
    return this.http.request<Models.ManifestResponse>('POST', `${this.baseUrl}/api/v1/admin/manifests`, body, undefined, options);
  }

  /**
   * Packs, weighs and books a parcel for one seller's part of an order.
   * `POST /api/v1/admin/sub-orders/{id}/shipments`
   */
  adminCreateShipment(id: string, body: Models.CreateShipmentBody, options?: ApiRequestOptions): Observable<Models.ShipmentResponse> {
    return this.http.request<Models.ShipmentResponse>('POST', `${this.baseUrl}/api/v1/admin/sub-orders/${encodeURIComponent(String(id))}/shipments`, body, undefined, options);
  }

  /**
   * Adds a rule to the rate card.
   * `POST /api/v1/admin/shipping/rates`
   */
  adminCreateShippingRate(body: Models.RateBody, options?: ApiRequestOptions): Observable<Models.ShippingRateResponse> {
    return this.http.request<Models.ShippingRateResponse>('POST', `${this.baseUrl}/api/v1/admin/shipping/rates`, body, undefined, options);
  }

  /**
   * Opens a delivery zone.
   * `POST /api/v1/admin/shipping/zones`
   */
  adminCreateShippingZone(body: Models.ZoneBody, options?: ApiRequestOptions): Observable<Models.ShippingZoneResponse> {
    return this.http.request<Models.ShippingZoneResponse>('POST', `${this.baseUrl}/api/v1/admin/shipping/zones`, body, undefined, options);
  }

  /**
   * Records that the courier has taken the parcel. This is what ships the order.
   * `POST /api/v1/admin/shipments/{id}/dispatch`
   */
  adminDispatchShipment(id: string, options?: ApiRequestOptions): Observable<Models.ShipmentResponse> {
    return this.http.request<Models.ShipmentResponse>('POST', `${this.baseUrl}/api/v1/admin/shipments/${encodeURIComponent(String(id))}/dispatch`, undefined, undefined, options);
  }

  /**
   * Where this store currently delivers.
   * `GET /api/v1/admin/shipping/coverage`
   */
  adminGetDeliveryCoverage(options?: ApiRequestOptions): Observable<Models.DeliveryCoverageResponse> {
    return this.http.request<Models.DeliveryCoverageResponse>('GET', `${this.baseUrl}/api/v1/admin/shipping/coverage`, undefined, undefined, options);
  }

  /**
   * What the cache says about a PIN code, and when it last asked.
   * `GET /api/v1/admin/shipping/serviceability/{pincode}`
   */
  adminGetServiceability(pincode: string, options?: ApiRequestOptions): Observable<Models.ServiceabilityResponse> {
    return this.http.request<Models.ServiceabilityResponse>('GET', `${this.baseUrl}/api/v1/admin/shipping/serviceability/${encodeURIComponent(String(pincode))}`, undefined, undefined, options);
  }

  /**
   * One parcel in full, with what is in it and everywhere it has been.
   * `GET /api/v1/admin/shipments/{id}`
   */
  adminGetShipment(id: string, options?: ApiRequestOptions): Observable<Models.ShipmentResponse> {
    return this.http.request<Models.ShipmentResponse>('GET', `${this.baseUrl}/api/v1/admin/shipments/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * The label to print: the courier's own where there is one, ours where there is not.
   * `GET /api/v1/admin/shipments/{id}/label`
   */
  adminGetShipmentLabel(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('GET', `${this.baseUrl}/api/v1/admin/shipments/${encodeURIComponent(String(id))}/label`, undefined, undefined, options);
  }

  /**
   * The courier webhook log and its dead-letter queue.
   * `GET /api/v1/admin/courier-events`
   */
  adminListCourierEvents(query?: AdminListCourierEventsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfCourierEventResponse> {
    return this.http.request<Models.PagedResultOfCourierEventResponse>('GET', `${this.baseUrl}/api/v1/admin/courier-events`, undefined, query, options);
  }

  /**
   * Handover sheets, newest first.
   * `GET /api/v1/admin/manifests`
   */
  adminListManifests(query?: AdminListManifestsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfManifestResponse> {
    return this.http.request<Models.PagedResultOfManifestResponse>('GET', `${this.baseUrl}/api/v1/admin/manifests`, undefined, query, options);
  }

  /**
   * Failed delivery attempts. Unactioned ones by default: this is the queue.
   * `GET /api/v1/admin/ndr`
   */
  adminListNdr(query?: AdminListNdrQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfNdrResponse> {
    return this.http.request<Models.PagedResultOfNdrResponse>('GET', `${this.baseUrl}/api/v1/admin/ndr`, undefined, query, options);
  }

  /**
   * Parcels, newest first, filtered by status, seller, order or air waybill.
   * `GET /api/v1/admin/shipments`
   */
  adminListShipments(query?: AdminListShipmentsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfShipmentSummaryResponse> {
    return this.http.request<Models.PagedResultOfShipmentSummaryResponse>('GET', `${this.baseUrl}/api/v1/admin/shipments`, undefined, query, options);
  }

  /**
   * The rate card. A seller sees the platform's rules and their own overrides.
   * `GET /api/v1/admin/shipping/rates`
   */
  adminListShippingRates(query?: AdminListShippingRatesQuery, options?: ApiRequestOptions): Observable<Models.ShippingRateResponse[]> {
    return this.http.request<Models.ShippingRateResponse[]>('GET', `${this.baseUrl}/api/v1/admin/shipping/rates`, undefined, query, options);
  }

  /**
   * The delivery map, in the precedence order the rate engine applies.
   * `GET /api/v1/admin/shipping/zones`
   */
  adminListShippingZones(query?: AdminListShippingZonesQuery, options?: ApiRequestOptions): Observable<Models.ShippingZoneResponse[]> {
    return this.http.request<Models.ShippingZoneResponse[]>('GET', `${this.baseUrl}/api/v1/admin/shipping/zones`, undefined, query, options);
  }

  /**
   * Replaces what is in a parcel. Refused once it has been booked.
   * `PUT /api/v1/admin/shipments/{id}/contents`
   */
  adminPackShipment(id: string, body: Models.PackBody, options?: ApiRequestOptions): Observable<Models.ShipmentResponse> {
    return this.http.request<Models.ShipmentResponse>('PUT', `${this.baseUrl}/api/v1/admin/shipments/${encodeURIComponent(String(id))}/contents`, body, undefined, options);
  }

  /**
   * Records a courier's cash remittance against the parcels it covers.
   * `POST /api/v1/admin/shipping/cod-remittances`
   */
  adminRecordCourierRemittance(body: Models.CourierRemittanceBody, options?: ApiRequestOptions): Observable<number> {
    return this.http.request<number>('POST', `${this.baseUrl}/api/v1/admin/shipping/cod-remittances`, body, undefined, options);
  }

  /**
   * Records a movement learned outside the platform, for a hand-booked parcel.
   * `POST /api/v1/admin/shipments/{id}/tracking`
   */
  adminRecordShipmentTracking(id: string, body: Models.RecordTrackingBody, options?: ApiRequestOptions): Observable<Models.ShipmentResponse> {
    return this.http.request<Models.ShipmentResponse>('POST', `${this.baseUrl}/api/v1/admin/shipments/${encodeURIComponent(String(id))}/tracking`, body, undefined, options);
  }

  /**
   * Asks the courier about a PIN code now, rather than waiting for the nightly job.
   * `POST /api/v1/admin/shipping/serviceability/{pincode}/refresh`
   */
  adminRefreshServiceability(pincode: string, options?: ApiRequestOptions): Observable<Models.ServiceabilityResponse> {
    return this.http.request<Models.ServiceabilityResponse>('POST', `${this.baseUrl}/api/v1/admin/shipping/serviceability/${encodeURIComponent(String(pincode))}/refresh`, undefined, undefined, options);
  }

  /**
   * Puts a failed or dead-lettered event back in the queue.
   * `POST /api/v1/admin/courier-events/{id}/replay`
   */
  adminReplayCourierEvent(id: string, options?: ApiRequestOptions): Observable<Models.CourierEventResponse> {
    return this.http.request<Models.CourierEventResponse>('POST', `${this.baseUrl}/api/v1/admin/courier-events/${encodeURIComponent(String(id))}/replay`, undefined, undefined, options);
  }

  /**
   * Asks the courier to collect.
   * `POST /api/v1/admin/shipments/{id}/schedule-pickup`
   */
  adminSchedulePickup(id: string, body?: null | Models.SchedulePickupBody, options?: ApiRequestOptions): Observable<Models.ShipmentResponse> {
    return this.http.request<Models.ShipmentResponse>('POST', `${this.baseUrl}/api/v1/admin/shipments/${encodeURIComponent(String(id))}/schedule-pickup`, body, undefined, options);
  }

  /**
   * Everything waiting to be packed, one row per item, soonest deadline first.
   * `GET /api/v1/admin/shipments/pick-list`
   */
  adminShipmentPickList(query?: AdminShipmentPickListQuery, options?: ApiRequestOptions): Observable<Models.PickListLineResponse[]> {
    return this.http.request<Models.PickListLineResponse[]>('GET', `${this.baseUrl}/api/v1/admin/shipments/pick-list`, undefined, query, options);
  }

  /**
   * Re-reads the parcel from its courier and applies what they say.
   * `POST /api/v1/admin/shipments/{id}/sync`
   */
  adminSyncShipmentTracking(id: string, options?: ApiRequestOptions): Observable<Models.ShipmentResponse> {
    return this.http.request<Models.ShipmentResponse>('POST', `${this.baseUrl}/api/v1/admin/shipments/${encodeURIComponent(String(id))}/sync`, undefined, undefined, options);
  }

  /**
   * Whether one address would be accepted, and which check refuses it.
   * `GET /api/v1/admin/shipping/coverage/test/{pincode}`
   */
  adminTestDeliveryCoverage(pincode: string, options?: ApiRequestOptions): Observable<Models.ServiceabilityResponse> {
    return this.http.request<Models.ServiceabilityResponse>('GET', `${this.baseUrl}/api/v1/admin/shipping/coverage/test/${encodeURIComponent(String(pincode))}`, undefined, undefined, options);
  }

  /**
   * Changes what a rule charges and promises.
   * `PUT /api/v1/admin/shipping/rates/{id}`
   */
  adminUpdateShippingRate(id: string, body: Models.RateBody, options?: ApiRequestOptions): Observable<Models.ShippingRateResponse> {
    return this.http.request<Models.ShippingRateResponse>('PUT', `${this.baseUrl}/api/v1/admin/shipping/rates/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Redraws a delivery zone.
   * `PUT /api/v1/admin/shipping/zones/{id}`
   */
  adminUpdateShippingZone(id: string, body: Models.ZoneBody, options?: ApiRequestOptions): Observable<Models.ShippingZoneResponse> {
    return this.http.request<Models.ShippingZoneResponse>('PUT', `${this.baseUrl}/api/v1/admin/shipping/zones/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Whether this store can deliver to a PIN code, and how quickly.
   * `GET /api/v1/store/shipping/serviceability/{pincode}`
   */
  storeShippingServiceability(pincode: string, options?: ApiRequestOptions): Observable<Models.ServiceabilityResponse> {
    return this.http.request<Models.ServiceabilityResponse>('GET', `${this.baseUrl}/api/v1/store/shipping/serviceability/${encodeURIComponent(String(pincode))}`, undefined, undefined, options);
  }
}
