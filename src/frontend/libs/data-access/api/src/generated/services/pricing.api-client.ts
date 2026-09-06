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

/** Query string for `adminPriceListItems`. */
export interface AdminPriceListItemsQuery {
  listingId?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminPriceListsList`. */
export interface AdminPriceListsListQuery {
  VendorId?: string;
  Type?: string;
  ActiveOnly?: boolean;
  Search?: string;
  Cursor?: string;
  Size?: number;
}

/** Query string for `adminPriceResolve`. */
export interface AdminPriceResolveQuery {
  listingId: string;
  quantity?: number;
}

/** Query string for `adminPromotionRedemptions`. */
export interface AdminPromotionRedemptionsQuery {
  customerId?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminPromotionsList`. */
export interface AdminPromotionsListQuery {
  Type?: string;
  Code?: string;
  ActiveOnly?: boolean;
  Search?: string;
  Cursor?: string;
  Size?: number;
}

/** Query string for `adminTaxRateResolve`. */
export interface AdminTaxRateResolveQuery {
  hsnCode: string;
  asOf?: string;
}

/** Query string for `adminTaxRatesList`. */
export interface AdminTaxRatesListQuery {
  HsnCode?: string;
  ActiveOnly?: boolean;
  Cursor?: string;
  Size?: number;
}

/** Query string for `adminWalletsList`. */
export interface AdminWalletsListQuery {
  customerId?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminWalletTransactions`. */
export interface AdminWalletTransactionsQuery {
  cursor?: string;
  size?: number;
}

/** Query string for `storeWalletTransactions`. */
export interface StoreWalletTransactionsQuery {
  cursor?: string;
  size?: number;
}

/** `Pricing` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class PricingApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Switches a price list on. Announces the new price of everything in it.
   * `POST /api/v1/admin/price-lists/{id}/activate`
   */
  adminPriceListActivate(id: string, options?: ApiRequestOptions): Observable<Models.PriceListResponse> {
    return this.http.request<Models.PriceListResponse>('POST', `${this.baseUrl}/api/v1/admin/price-lists/${encodeURIComponent(String(id))}/activate`, undefined, undefined, options);
  }

  /**
   * Opens a price list. It is active immediately; its window decides when it applies.
   * `POST /api/v1/admin/price-lists`
   */
  adminPriceListCreate(body: Models.CreatePriceListBody, options?: ApiRequestOptions): Observable<Models.PriceListResponse> {
    return this.http.request<Models.PriceListResponse>('POST', `${this.baseUrl}/api/v1/admin/price-lists`, body, undefined, options);
  }

  /**
   * Switches a price list off. Offers in it fall back to the next list that applies.
   * `POST /api/v1/admin/price-lists/{id}/deactivate`
   */
  adminPriceListDeactivate(id: string, options?: ApiRequestOptions): Observable<Models.PriceListResponse> {
    return this.http.request<Models.PriceListResponse>('POST', `${this.baseUrl}/api/v1/admin/price-lists/${encodeURIComponent(String(id))}/deactivate`, undefined, undefined, options);
  }

  /**
   * Removes a price list and every price in it.
   * `DELETE /api/v1/admin/price-lists/{id}`
   */
  adminPriceListDelete(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/price-lists/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Reads one price list. Answers 404 for a list outside the caller's scope.
   * `GET /api/v1/admin/price-lists/{id}`
   */
  adminPriceListGet(id: string, options?: ApiRequestOptions): Observable<Models.PriceListResponse> {
    return this.http.request<Models.PriceListResponse>('GET', `${this.baseUrl}/api/v1/admin/price-lists/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Removes one price. The offer falls back to the next list that applies.
   * `DELETE /api/v1/admin/price-lists/{id}/items/{itemId}`
   */
  adminPriceListItemDelete(id: string, itemId: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/price-lists/${encodeURIComponent(String(id))}/items/${encodeURIComponent(String(itemId))}`, undefined, undefined, options);
  }

  /**
   * Lists the prices in a list. Filter by offer to see all of its quantity tiers.
   * `GET /api/v1/admin/price-lists/{id}/items`
   */
  adminPriceListItems(id: string, query?: AdminPriceListItemsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfPriceListItemResponse> {
    return this.http.request<Models.PagedResultOfPriceListItemResponse>('GET', `${this.baseUrl}/api/v1/admin/price-lists/${encodeURIComponent(String(id))}/items`, undefined, query, options);
  }

  /**
   * Adds or restates a batch of prices. A quantity tier is a row per minQuantity.
   * `PUT /api/v1/admin/price-lists/{id}/items`
   */
  adminPriceListItemsUpsert(id: string, body: Models.UpsertPriceListItemsBody, options?: ApiRequestOptions): Observable<Models.PriceListItemResponse[]> {
    return this.http.request<Models.PriceListItemResponse[]>('PUT', `${this.baseUrl}/api/v1/admin/price-lists/${encodeURIComponent(String(id))}/items`, body, undefined, options);
  }

  /**
   * Lists price lists. A vendor caller sees their own and the platform's.
   * `GET /api/v1/admin/price-lists`
   */
  adminPriceListsList(query?: AdminPriceListsListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfPriceListResponse> {
    return this.http.request<Models.PagedResultOfPriceListResponse>('GET', `${this.baseUrl}/api/v1/admin/price-lists`, undefined, query, options);
  }

  /**
   * Renames a price list and restates its rank and window.
   * `PUT /api/v1/admin/price-lists/{id}`
   */
  adminPriceListUpdate(id: string, body: Models.UpdatePriceListBody, options?: ApiRequestOptions): Observable<Models.PriceListResponse> {
    return this.http.request<Models.PriceListResponse>('PUT', `${this.baseUrl}/api/v1/admin/price-lists/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Explains what an offer costs and which price list decided it.
   * `GET /api/v1/admin/prices/resolve`
   */
  adminPriceResolve(query: AdminPriceResolveQuery, options?: ApiRequestOptions): Observable<Models.EffectivePrice> {
    return this.http.request<Models.EffectivePrice>('GET', `${this.baseUrl}/api/v1/admin/prices/resolve`, undefined, query, options);
  }

  /**
   * Switches a promotion on. Its window still decides when it actually applies.
   * `POST /api/v1/admin/promotions/{id}/activate`
   */
  adminPromotionActivate(id: string, options?: ApiRequestOptions): Observable<Models.PromotionResponse> {
    return this.http.request<Models.PromotionResponse>('POST', `${this.baseUrl}/api/v1/admin/promotions/${encodeURIComponent(String(id))}/activate`, undefined, undefined, options);
  }

  /**
   * Drafts a promotion. It is inactive until it is activated.
   * `POST /api/v1/admin/promotions`
   */
  adminPromotionCreate(body: Models.PromotionBody, options?: ApiRequestOptions): Observable<Models.PromotionResponse> {
    return this.http.request<Models.PromotionResponse>('POST', `${this.baseUrl}/api/v1/admin/promotions`, body, undefined, options);
  }

  /**
   * Switches a promotion off immediately. The route to stop a leaked code.
   * `POST /api/v1/admin/promotions/{id}/deactivate`
   */
  adminPromotionDeactivate(id: string, options?: ApiRequestOptions): Observable<Models.PromotionResponse> {
    return this.http.request<Models.PromotionResponse>('POST', `${this.baseUrl}/api/v1/admin/promotions/${encodeURIComponent(String(id))}/deactivate`, undefined, undefined, options);
  }

  /**
   * Removes a promotion that has never been used. A used one is deactivated instead.
   * `DELETE /api/v1/admin/promotions/{id}`
   */
  adminPromotionDelete(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/promotions/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Reads one promotion, with its scope, conditions and usage.
   * `GET /api/v1/admin/promotions/{id}`
   */
  adminPromotionGet(id: string, options?: ApiRequestOptions): Observable<Models.PromotionResponse> {
    return this.http.request<Models.PromotionResponse>('GET', `${this.baseUrl}/api/v1/admin/promotions/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Lists a promotion's uses, including the ones that were reversed.
   * `GET /api/v1/admin/promotions/{id}/redemptions`
   */
  adminPromotionRedemptions(id: string, query?: AdminPromotionRedemptionsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfPromotionRedemptionResponse> {
    return this.http.request<Models.PagedResultOfPromotionRedemptionResponse>('GET', `${this.baseUrl}/api/v1/admin/promotions/${encodeURIComponent(String(id))}/redemptions`, undefined, query, options);
  }

  /**
   * Prices a basket and reports every promotion considered, applied or not, with the reason.
   * `POST /api/v1/admin/promotions/simulate`
   */
  adminPromotionSimulate(body: Models.SimulatePromotionBody, options?: ApiRequestOptions): Observable<Models.QuoteResult> {
    return this.http.request<Models.QuoteResult>('POST', `${this.baseUrl}/api/v1/admin/promotions/simulate`, body, undefined, options);
  }

  /**
   * Lists coupon codes and automatic cart rules.
   * `GET /api/v1/admin/promotions`
   */
  adminPromotionsList(query?: AdminPromotionsListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfPromotionResponse> {
    return this.http.request<Models.PagedResultOfPromotionResponse>('GET', `${this.baseUrl}/api/v1/admin/promotions`, undefined, query, options);
  }

  /**
   * Restates a promotion. The code is fixed once created — shoppers have already seen it.
   * `PUT /api/v1/admin/promotions/{id}`
   */
  adminPromotionUpdate(id: string, body: Models.PromotionBody, options?: ApiRequestOptions): Observable<Models.PromotionResponse> {
    return this.http.request<Models.PromotionResponse>('PUT', `${this.baseUrl}/api/v1/admin/promotions/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Records a GST rate for an HSN code from a date. A rate change is a new row.
   * `POST /api/v1/admin/tax-rates`
   */
  adminTaxRateCreate(body: Models.CreateTaxRateBody, options?: ApiRequestOptions): Observable<Models.TaxRateResponse> {
    return this.http.request<Models.TaxRateResponse>('POST', `${this.baseUrl}/api/v1/admin/tax-rates`, body, undefined, options);
  }

  /**
   * Removes a GST rate that was entered in error. Close the window instead where it was used.
   * `DELETE /api/v1/admin/tax-rates/{id}`
   */
  adminTaxRateDelete(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/tax-rates/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Reads one GST rate.
   * `GET /api/v1/admin/tax-rates/{id}`
   */
  adminTaxRateGet(id: string, options?: ApiRequestOptions): Observable<Models.TaxRateResponse> {
    return this.http.request<Models.TaxRateResponse>('GET', `${this.baseUrl}/api/v1/admin/tax-rates/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * The rate in force for a code on a date. The same resolver the quote engine uses.
   * `GET /api/v1/admin/tax-rates/resolve`
   */
  adminTaxRateResolve(query: AdminTaxRateResolveQuery, options?: ApiRequestOptions): Observable<Models.TaxRateResolutionResponse> {
    return this.http.request<Models.TaxRateResolutionResponse>('GET', `${this.baseUrl}/api/v1/admin/tax-rates/resolve`, undefined, query, options);
  }

  /**
   * Lists GST rates. Filter by HSN code to read one code's whole history.
   * `GET /api/v1/admin/tax-rates`
   */
  adminTaxRatesList(query?: AdminTaxRatesListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfTaxRateResponse> {
    return this.http.request<Models.PagedResultOfTaxRateResponse>('GET', `${this.baseUrl}/api/v1/admin/tax-rates`, undefined, query, options);
  }

  /**
   * Amends a GST rate. Audited with its before and after, because a filing may turn on it.
   * `PUT /api/v1/admin/tax-rates/{id}`
   */
  adminTaxRateUpdate(id: string, body: Models.UpdateTaxRateBody, options?: ApiRequestOptions): Observable<Models.TaxRateResponse> {
    return this.http.request<Models.TaxRateResponse>('PUT', `${this.baseUrl}/api/v1/admin/tax-rates/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Credits or debits a customer's store credit by hand. Audited with the actor.
   * `POST /api/v1/admin/wallets/{customerId}/adjust`
   */
  adminWalletAdjust(customerId: string, body: Models.AdjustWalletBody, options?: ApiRequestOptions): Observable<Models.WalletResponse> {
    return this.http.request<Models.WalletResponse>('POST', `${this.baseUrl}/api/v1/admin/wallets/${encodeURIComponent(String(customerId))}/adjust`, body, undefined, options);
  }

  /**
   * Reads one customer's store-credit balance.
   * `GET /api/v1/admin/wallets/{customerId}`
   */
  adminWalletGet(customerId: string, options?: ApiRequestOptions): Observable<Models.WalletResponse> {
    return this.http.request<Models.WalletResponse>('GET', `${this.baseUrl}/api/v1/admin/wallets/${encodeURIComponent(String(customerId))}`, undefined, undefined, options);
  }

  /**
   * Lists store-credit wallets.
   * `GET /api/v1/admin/wallets`
   */
  adminWalletsList(query?: AdminWalletsListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfWalletResponse> {
    return this.http.request<Models.PagedResultOfWalletResponse>('GET', `${this.baseUrl}/api/v1/admin/wallets`, undefined, query, options);
  }

  /**
   * One customer's store-credit statement, newest first.
   * `GET /api/v1/admin/wallets/{customerId}/transactions`
   */
  adminWalletTransactions(customerId: string, query?: AdminWalletTransactionsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfWalletTransactionResponse> {
    return this.http.request<Models.PagedResultOfWalletTransactionResponse>('GET', `${this.baseUrl}/api/v1/admin/wallets/${encodeURIComponent(String(customerId))}/transactions`, undefined, query, options);
  }

  /**
   * Prices a basket the server is not holding, itemised, with the full GST breakdown.
   * `POST /api/v1/store/quote`
   */
  storeQuote(body: Models.StoreQuoteBody, options?: ApiRequestOptions): Observable<Models.QuoteResult> {
    return this.http.request<Models.QuoteResult>('POST', `${this.baseUrl}/api/v1/store/quote`, body, undefined, options);
  }

  /**
   * The signed-in shopper's own store-credit balance.
   * `GET /api/v1/store/me/wallet`
   */
  storeWallet(options?: ApiRequestOptions): Observable<Models.WalletResponse> {
    return this.http.request<Models.WalletResponse>('GET', `${this.baseUrl}/api/v1/store/me/wallet`, undefined, undefined, options);
  }

  /**
   * The signed-in shopper's own store-credit statement, newest first.
   * `GET /api/v1/store/me/wallet/transactions`
   */
  storeWalletTransactions(query?: StoreWalletTransactionsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfWalletTransactionResponse> {
    return this.http.request<Models.PagedResultOfWalletTransactionResponse>('GET', `${this.baseUrl}/api/v1/store/me/wallet/transactions`, undefined, query, options);
  }
}
