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

/** Query string for `adminListAbandonedCarts`. */
export interface AdminListAbandonedCartsQuery {
  since?: string;
  minValue?: number;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListCarts`. */
export interface AdminListCartsQuery {
  status?: string;
  customerId?: string;
  hasCoupon?: boolean;
  cursor?: string;
  size?: number;
}

/** `Cart` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class CartApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Retires a basket. Audited, because it is a change to a shopper's own data.
   * `POST /api/v1/admin/carts/{id}/expire`
   */
  adminExpireCart(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/admin/carts/${encodeURIComponent(String(id))}/expire`, undefined, undefined, options);
  }

  /**
   * One basket in full: lines, seller groups and the live quote.
   * `GET /api/v1/admin/carts/{id}`
   */
  adminGetCart(id: string, options?: ApiRequestOptions): Observable<Models.CartResponse> {
    return this.http.request<Models.CartResponse>('GET', `${this.baseUrl}/api/v1/admin/carts/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * The abandoned-cart worklist, most valuable first.
   * `GET /api/v1/admin/carts/abandoned`
   */
  adminListAbandonedCarts(query?: AdminListAbandonedCartsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfAdminCartSummary> {
    return this.http.request<Models.PagedResultOfAdminCartSummary>('GET', `${this.baseUrl}/api/v1/admin/carts/abandoned`, undefined, query, options);
  }

  /**
   * Lists baskets, newest first.
   * `GET /api/v1/admin/carts`
   */
  adminListCarts(query?: AdminListCartsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfAdminCartSummary> {
    return this.http.request<Models.PagedResultOfAdminCartSummary>('GET', `${this.baseUrl}/api/v1/admin/carts`, undefined, query, options);
  }

  /**
   * Puts units of an offer into the basket, opening one if the caller has none.
   * `POST /api/v1/store/cart/items`
   */
  storeAddCartItem(body: Models.AddCartItemBody, options?: ApiRequestOptions): Observable<Models.CartResponse> {
    return this.http.request<Models.CartResponse>('POST', `${this.baseUrl}/api/v1/store/cart/items`, body, undefined, options);
  }

  /**
   * Records a coupon code. Whether it applies comes back on the quote, with the reason.
   * `POST /api/v1/store/cart/coupon`
   */
  storeApplyCoupon(body: Models.CouponBody, options?: ApiRequestOptions): Observable<Models.CartResponse> {
    return this.http.request<Models.CartResponse>('POST', `${this.baseUrl}/api/v1/store/cart/coupon`, body, undefined, options);
  }

  /**
   * Empties the basket, coupon included.
   * `DELETE /api/v1/store/cart`
   */
  storeClearCart(options?: ApiRequestOptions): Observable<Models.CartResponse> {
    return this.http.request<Models.CartResponse>('DELETE', `${this.baseUrl}/api/v1/store/cart`, undefined, undefined, options);
  }

  /**
   * The caller's own basket: lines, seller groups, the itemised price, and every issue.
   * `GET /api/v1/store/cart`
   */
  storeGetCart(options?: ApiRequestOptions): Observable<Models.CartResponse> {
    return this.http.request<Models.CartResponse>('GET', `${this.baseUrl}/api/v1/store/cart`, undefined, undefined, options);
  }

  /**
   * The itemised price of the caller's own basket, with the full GST breakdown.
   * `GET /api/v1/store/cart/quote`
   */
  storeGetCartQuote(options?: ApiRequestOptions): Observable<Models.QuoteResult> {
    return this.http.request<Models.QuoteResult>('GET', `${this.baseUrl}/api/v1/store/cart/quote`, undefined, undefined, options);
  }

  /**
   * Folds the browser's basket into the signed-in shopper's own.
   * `POST /api/v1/store/cart/merge`
   */
  storeMergeCart(options?: ApiRequestOptions): Observable<Models.CartResponse> {
    return this.http.request<Models.CartResponse>('POST', `${this.baseUrl}/api/v1/store/cart/merge`, undefined, undefined, options);
  }

  /**
   * Takes a line out of the basket.
   * `DELETE /api/v1/store/cart/items/{lineId}`
   */
  storeRemoveCartItem(lineId: string, options?: ApiRequestOptions): Observable<Models.CartResponse> {
    return this.http.request<Models.CartResponse>('DELETE', `${this.baseUrl}/api/v1/store/cart/items/${encodeURIComponent(String(lineId))}`, undefined, undefined, options);
  }

  /**
   * Takes the coupon off the basket.
   * `DELETE /api/v1/store/cart/coupon`
   */
  storeRemoveCoupon(options?: ApiRequestOptions): Observable<Models.CartResponse> {
    return this.http.request<Models.CartResponse>('DELETE', `${this.baseUrl}/api/v1/store/cart/coupon`, undefined, undefined, options);
  }

  /**
   * Changes a line's quantity, or sets it aside for later.
   * `PATCH /api/v1/store/cart/items/{lineId}`
   */
  storeUpdateCartItem(lineId: string, body: Models.UpdateCartItemBody, options?: ApiRequestOptions): Observable<Models.CartResponse> {
    return this.http.request<Models.CartResponse>('PATCH', `${this.baseUrl}/api/v1/store/cart/items/${encodeURIComponent(String(lineId))}`, body, undefined, options);
  }
}
