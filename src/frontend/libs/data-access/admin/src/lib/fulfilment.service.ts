import { Injectable, inject } from '@angular/core';
import {
  BookBody,
  CourierEventResponse,
  CreateManifestBody,
  CreateShipmentBody,
  ManifestResponse,
  NdrAction,
  NdrActionBody,
  NdrResponse,
  PackBody,
  PickListLineResponse,
  ShipmentResponse,
  ShipmentSummaryResponse,
  ShippingApiClient,
  WeighBody,
} from '@klarahome/data-access-api';
import { Observable, map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

export interface ShipmentFilters {
  readonly status?: string;
  readonly vendorId?: string;
  readonly orderId?: string;
  readonly subOrderId?: string;
  readonly awb?: string;
  /** Free text across the order number, the recipient and the AWB. */
  readonly q?: string;
}

export interface NdrFilters {
  readonly action?: NdrAction;
  readonly vendorId?: string;
  readonly reasonCode?: string;
}

export interface ManifestFilters {
  readonly vendorId?: string;
}

export interface CourierEventFilters {
  readonly status?: string;
  readonly awb?: string;
}

/**
 * Getting a parcel out of the building.
 *
 * The workflow this service exposes is deliberately the physical one, in the order it happens
 * (Step 16): **pick → pack → weigh → book → manifest → dispatch**. Each is its own endpoint
 * because each is its own moment, and a screen that collapsed them into "ship it" would have to
 * guess the weight — which is the number the courier bills on and the one that turns a ₹60
 * shipment into a ₹190 one when it is wrong.
 *
 * Booking is where the courier is chosen, and it is the one call that can fail for reasons nobody
 * in the warehouse can fix: no account, no serviceability, an aggregator that is down. That is why
 * `BookBody` carries `manualAwb`/`manualCourier` — the manual adapter from Step 16A means a
 * deployment with no logistics account can still dispatch, with the waybill typed in from the
 * courier's own book.
 *
 * The NDR queue is here rather than beside returns because a failed delivery is still a shipment:
 * the parcel exists, it is somewhere, and the four actions on it — reattempt, reschedule, correct
 * the address, send it back — are instructions to the courier holding it.
 */
@Injectable({ providedIn: 'root' })
export class FulfilmentService {
  private readonly api = inject(ShippingApiClient);

  // ---- Picking ----------------------------------------------------------------------------------

  /**
   * The pick list: what to take off the shelves, for every shipment awaiting packing.
   *
   * Unpaged and capped by `size`, because it is a piece of paper — a warehouse works one printed
   * round at a time, and a pick list with a Next button is a pick list somebody will half-do.
   */
  /**
   * Everything waiting to be packed, one row per item.
   *
   * `warehouseId` narrows it to one location. Without it a store with two warehouses hands every
   * picker every parcel, and neither can tell which are theirs — which is the failure Step 28B's
   * deliverable 12 describes, and why each row now names the shelf it is on.
   */
  pickList(vendorId?: string, warehouseId?: string, size = 100): Observable<PickListLineResponse[]> {
    return this.api.adminShipmentPickList({ vendorId, warehouseId, size });
  }

  // ---- Shipments --------------------------------------------------------------------------------

  shipments(
    filters: ShipmentFilters = {},
    pageSize = 25,
  ): CursorList<ShipmentSummaryResponse, ShipmentFilters> {
    return new CursorList<ShipmentSummaryResponse, ShipmentFilters>(
      (current, cursor, size) =>
        this.api
          .adminListShipments({
            status: current.status,
            vendorId: current.vendorId,
            orderId: current.orderId,
            subOrderId: current.subOrderId,
            awb: current.awb,
            q: current.q,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<ShipmentSummaryResponse> => result)),
      filters,
      pageSize,
    );
  }

  shipment(id: string): Observable<ShipmentResponse> {
    return this.api.adminGetShipment(id);
  }

  /** Opens a shipment against a sub-order — one parcel, or one of several. */
  create(subOrderId: string, body: CreateShipmentBody): Observable<ShipmentResponse> {
    return this.api.adminCreateShipment(subOrderId, body);
  }

  /** Records what actually went in the box, which need not be everything on the sub-order. */
  pack(shipmentId: string, body: PackBody): Observable<ShipmentResponse> {
    return this.api.adminPackShipment(shipmentId, body);
  }

  /**
   * The weight and the box.
   *
   * Both, because the courier charges on the greater of actual and volumetric weight, and a
   * dimension left at zero silently bills the parcel as if it were a brick.
   */
  weigh(shipmentId: string, body: WeighBody): Observable<ShipmentResponse> {
    return this.api.adminCaptureShipmentWeight(shipmentId, body);
  }

  /** Asks the courier for a waybill — or accepts one typed in, when there is no account. */
  book(shipmentId: string, body: BookBody): Observable<ShipmentResponse> {
    return this.api.adminBookShipment(shipmentId, body);
  }

  schedulePickup(shipmentId: string, pickupAt: string | null): Observable<ShipmentResponse> {
    return this.api.adminSchedulePickup(shipmentId, { pickupAt });
  }

  /** Handed to the courier. The last thing the warehouse does. */
  dispatch(shipmentId: string): Observable<ShipmentResponse> {
    return this.api.adminDispatchShipment(shipmentId);
  }

  cancel(shipmentId: string, reason: string | null): Observable<ShipmentResponse> {
    return this.api.adminCancelShipment(shipmentId, { reason });
  }

  /** Pulls the courier's current view, for the parcel whose webhook never arrived. */
  syncTracking(shipmentId: string): Observable<ShipmentResponse> {
    return this.api.adminSyncShipmentTracking(shipmentId);
  }

  /** Records a scan by hand — the manual-AWB deployment's substitute for a webhook. */
  recordTracking(
    shipmentId: string,
    status: string,
    remark: string | null,
    occurredAt: string | null,
  ): Observable<ShipmentResponse> {
    return this.api.adminRecordShipmentTracking(shipmentId, { status, remark, occurredAt });
  }

  // The label itself is a PDF rather than JSON, so it is fetched by `DocumentPrintService` — see
  // that class for why the generated client cannot answer it.

  // ---- Manifests --------------------------------------------------------------------------------

  manifests(filters: ManifestFilters = {}, pageSize = 25): CursorList<ManifestResponse, ManifestFilters> {
    return new CursorList<ManifestResponse, ManifestFilters>(
      (current, cursor, size) =>
        this.api
          .adminListManifests({
            vendorId: current.vendorId,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<ManifestResponse> => result)),
      filters,
      pageSize,
    );
  }

  /** The handover sheet the courier signs. Closes the named shipments into one collection. */
  createManifest(body: CreateManifestBody): Observable<ManifestResponse> {
    return this.api.adminCreateManifest(body);
  }

  // ---- Failed deliveries ------------------------------------------------------------------------

  ndr(filters: NdrFilters = {}, pageSize = 25): CursorList<NdrResponse, NdrFilters> {
    return new CursorList<NdrResponse, NdrFilters>(
      (current, cursor, size) =>
        this.api
          .adminListNdr({
            action: current.action,
            vendorId: current.vendorId,
            reasonCode: current.reasonCode,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<NdrResponse> => result)),
      filters,
      pageSize,
    );
  }

  actionNdr(ndrId: string, body: NdrActionBody): Observable<NdrResponse> {
    return this.api.adminActionNdr(ndrId, body);
  }

  // ---- The courier's own messages ---------------------------------------------------------------

  /**
   * The webhook dead-letter queue.
   *
   * A courier event that could not be applied is kept rather than dropped, and `replay` is what an
   * operator does once the reason is fixed. Shown on the shipments screen because that is where
   * somebody notices a parcel whose status has not moved since Tuesday.
   */
  courierEvents(
    filters: CourierEventFilters = {},
    pageSize = 25,
  ): CursorList<CourierEventResponse, CourierEventFilters> {
    return new CursorList<CourierEventResponse, CourierEventFilters>(
      (current, cursor, size) =>
        this.api
          .adminListCourierEvents({
            status: current.status,
            awb: current.awb,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<CourierEventResponse> => result)),
      filters,
      pageSize,
    );
  }

  replayCourierEvent(id: string): Observable<CourierEventResponse> {
    return this.api.adminReplayCourierEvent(id);
  }
}
