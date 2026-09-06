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


/** `Checkout` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class CheckoutApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * One checkout session, its address snapshots and its place-order attempts.
   * `GET /api/v1/admin/checkout-sessions/{id}`
   */
  adminGetCheckoutSession(id: string, options?: ApiRequestOptions): Observable<Models.CheckoutResponse> {
    return this.http.request<Models.CheckoutResponse>('GET', `${this.baseUrl}/api/v1/admin/checkout-sessions/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Closes a session the shopper backed out of. The basket is left alone.
   * `POST /api/v1/store/checkout/{id}/abandon`
   */
  storeAbandonCheckout(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/store/checkout/${encodeURIComponent(String(id))}/abandon`, undefined, undefined, options);
  }

  /**
   * What this basket may be paid by, with the reason when cash on delivery may not be used.
   * `GET /api/v1/store/checkout/{id}/payment-methods`
   */
  storeCheckoutPaymentMethods(id: string, options?: ApiRequestOptions): Observable<Models.PaymentMethodResponse[]> {
    return this.http.request<Models.PaymentMethodResponse[]>('GET', `${this.baseUrl}/api/v1/store/checkout/${encodeURIComponent(String(id))}/payment-methods`, undefined, undefined, options);
  }

  /**
   * The delivery services available for each seller in the basket, with the promised window.
   * `GET /api/v1/store/checkout/{id}/shipping-options`
   */
  storeCheckoutShippingOptions(id: string, options?: ApiRequestOptions): Observable<Models.VendorShippingOptionsResponse[]> {
    return this.http.request<Models.VendorShippingOptionsResponse[]>('GET', `${this.baseUrl}/api/v1/store/checkout/${encodeURIComponent(String(id))}/shipping-options`, undefined, undefined, options);
  }

  /**
   * The session as it stands, with the basket re-priced against it.
   * `GET /api/v1/store/checkout/{id}`
   */
  storeGetCheckout(id: string, options?: ApiRequestOptions): Observable<Models.CheckoutResponse> {
    return this.http.request<Models.CheckoutResponse>('GET', `${this.baseUrl}/api/v1/store/checkout/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Reserves the stock and creates the order. Idempotent on the Idempotency-Key header.
   * `POST /api/v1/store/checkout/{id}/place-order`
   */
  storePlaceOrder(id: string, options?: ApiRequestOptions): Observable<Models.PlaceOrderResponse> {
    return this.http.request<Models.PlaceOrderResponse>('POST', `${this.baseUrl}/api/v1/store/checkout/${encodeURIComponent(String(id))}/place-order`, undefined, undefined, options);
  }

  /**
   * Re-validates and re-prices the session, immediately before payment.
   * `GET /api/v1/store/checkout/{id}/review`
   */
  storeReviewCheckout(id: string, options?: ApiRequestOptions): Observable<Models.CheckoutResponse> {
    return this.http.request<Models.CheckoutResponse>('GET', `${this.baseUrl}/api/v1/store/checkout/${encodeURIComponent(String(id))}/review`, undefined, undefined, options);
  }

  /**
   * Chooses where the order goes and who it is billed to, freezing both onto the session.
   * `PUT /api/v1/store/checkout/{id}/address`
   */
  storeSetCheckoutAddress(id: string, body: Models.CheckoutAddressBody, options?: ApiRequestOptions): Observable<Models.CheckoutResponse> {
    return this.http.request<Models.CheckoutResponse>('PUT', `${this.baseUrl}/api/v1/store/checkout/${encodeURIComponent(String(id))}/address`, body, undefined, options);
  }

  /**
   * Chooses how the order is paid for, checking cash-on-delivery eligibility first.
   * `PUT /api/v1/store/checkout/{id}/payment-method`
   */
  storeSetCheckoutPaymentMethod(id: string, body: Models.CheckoutPaymentMethodBody, options?: ApiRequestOptions): Observable<Models.CheckoutResponse> {
    return this.http.request<Models.CheckoutResponse>('PUT', `${this.baseUrl}/api/v1/store/checkout/${encodeURIComponent(String(id))}/payment-method`, body, undefined, options);
  }

  /**
   * Chooses a delivery service for every seller in the basket.
   * `PUT /api/v1/store/checkout/{id}/shipping`
   */
  storeSetCheckoutShipping(id: string, body: Models.CheckoutShippingBody, options?: ApiRequestOptions): Observable<Models.CheckoutResponse> {
    return this.http.request<Models.CheckoutResponse>('PUT', `${this.baseUrl}/api/v1/store/checkout/${encodeURIComponent(String(id))}/shipping`, body, undefined, options);
  }

  /**
   * Opens a checkout against the caller's basket, refusing one that is not ready.
   * `POST /api/v1/store/checkout`
   */
  storeStartCheckout(options?: ApiRequestOptions): Observable<Models.CheckoutResponse> {
    return this.http.request<Models.CheckoutResponse>('POST', `${this.baseUrl}/api/v1/store/checkout`, undefined, undefined, options);
  }
}
