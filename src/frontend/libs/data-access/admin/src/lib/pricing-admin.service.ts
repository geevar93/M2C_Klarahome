import { Injectable, inject } from '@angular/core';
import {
  CreatePriceListBody,
  CreateTaxRateBody,
  EffectivePrice,
  PriceListItemResponse,
  PriceListResponse,
  PricingApiClient,
  PromotionBody,
  PromotionRedemptionResponse,
  PromotionResponse,
  QuoteResult,
  SimulatePromotionBody,
  TaxRateResolutionResponse,
  TaxRateResponse,
  UpdatePriceListBody,
  UpdateTaxRateBody,
  UpsertPriceListItemsBody,
} from '@klarahome/data-access-api';
import { Observable, map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

export interface PromotionFilters {
  readonly type?: string;
  readonly code?: string;
  readonly activeOnly?: boolean;
  readonly search?: string;
}

export interface PriceListFilters {
  readonly vendorId?: string;
  readonly type?: string;
  readonly activeOnly?: boolean;
  readonly search?: string;
}

export interface TaxRateFilters {
  readonly hsnCode?: string;
  readonly activeOnly?: boolean;
}

/**
 * What a thing costs, and what may be taken off it.
 *
 * Three surfaces that look unrelated on a menu and are one module for a reason: the price a
 * shopper is shown is the price list's answer, less whatever promotion applied, plus the tax the
 * HSN code says — resolved by one engine (Step 12's `IPriceQuoteEngine`) rather than three, so
 * that a basket, an order and an invoice cannot disagree about a rupee.
 *
 * Two of the methods here are the ones the screens are actually built around, and neither reads a
 * stored value:
 *
 *  - **`simulate`** answers what a promotion *would* do to a basket, through the same quote engine
 *    that will run at checkout. That is what makes a rule builder honest: a merchandiser who has
 *    written "20% off, stacking allowed, capped at ₹500" finds out here rather than on the
 *    storefront. Nothing is saved, and the promotion need not exist yet.
 *  - **`resolveTaxRate`** and **`resolvePrice`** answer what the engine would pick *right now* for
 *    one HSN code or one listing. A tax rate has an effective window and a price list has a
 *    priority, so "which of these seven rows wins" is a question only the server can answer.
 *
 * Note what is missing: nothing here activates a promotion by writing `isActive`. Activation is
 * its own endpoint because a promotion also has a schedule, and a row that is `isActive` outside
 * its window is not live — the two facts together are what `PromotionResponse` reports.
 */
@Injectable({ providedIn: 'root' })
export class PricingAdminService {
  private readonly api = inject(PricingApiClient);

  // ---- Promotions -------------------------------------------------------------------------------

  promotions(filters: PromotionFilters = {}, pageSize = 25): CursorList<PromotionResponse, PromotionFilters> {
    return new CursorList<PromotionResponse, PromotionFilters>(
      (current, cursor, size) =>
        this.api
          .adminPromotionsList({
            Type: current.type,
            Code: current.code,
            ActiveOnly: current.activeOnly,
            Search: current.search,
            Cursor: cursor ?? undefined,
            Size: size,
          })
          .pipe(map((result): CursorPage<PromotionResponse> => result)),
      filters,
      pageSize,
    );
  }

  promotion(id: string): Observable<PromotionResponse> {
    return this.api.adminPromotionGet(id);
  }

  createPromotion(body: PromotionBody): Observable<PromotionResponse> {
    return this.api.adminPromotionCreate(body);
  }

  updatePromotion(id: string, body: PromotionBody): Observable<PromotionResponse> {
    return this.api.adminPromotionUpdate(id, body);
  }

  /** Live, subject to its schedule. The window still decides whether anybody can use it today. */
  activatePromotion(id: string): Observable<PromotionResponse> {
    return this.api.adminPromotionActivate(id);
  }

  deactivatePromotion(id: string): Observable<PromotionResponse> {
    return this.api.adminPromotionDeactivate(id);
  }

  /** Refused by the API once the promotion has been redeemed; deactivating is the other answer. */
  deletePromotion(id: string): Observable<void> {
    return this.api.adminPromotionDelete(id);
  }

  /** Who used it, on which order, for how much — and which redemptions a cancellation reversed. */
  redemptions(
    promotionId: string,
    pageSize = 25,
  ): CursorList<PromotionRedemptionResponse, { readonly customerId?: string }> {
    return new CursorList<PromotionRedemptionResponse, { readonly customerId?: string }>(
      (current, cursor, size) =>
        this.api
          .adminPromotionRedemptions(promotionId, {
            customerId: current.customerId,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<PromotionRedemptionResponse> => result)),
      {},
      pageSize,
    );
  }

  /**
   * What this basket would cost, with the coupon and without it.
   *
   * The whole quote comes back — every line, every promotion the engine chose to apply, the tax
   * and the totals — because a merchandiser's real question is not "did my coupon match" but "what
   * does the customer pay", and stacking means the answer depends on the other promotions too.
   */
  simulate(body: SimulatePromotionBody): Observable<QuoteResult> {
    return this.api.adminPromotionSimulate(body);
  }

  // ---- Price lists ------------------------------------------------------------------------------

  priceLists(filters: PriceListFilters = {}, pageSize = 25): CursorList<PriceListResponse, PriceListFilters> {
    return new CursorList<PriceListResponse, PriceListFilters>(
      (current, cursor, size) =>
        this.api
          .adminPriceListsList({
            VendorId: current.vendorId,
            Type: current.type,
            ActiveOnly: current.activeOnly,
            Search: current.search,
            Cursor: cursor ?? undefined,
            Size: size,
          })
          .pipe(map((result): CursorPage<PriceListResponse> => result)),
      filters,
      pageSize,
    );
  }

  priceList(id: string): Observable<PriceListResponse> {
    return this.api.adminPriceListGet(id);
  }

  createPriceList(body: CreatePriceListBody): Observable<PriceListResponse> {
    return this.api.adminPriceListCreate(body);
  }

  updatePriceList(id: string, body: UpdatePriceListBody): Observable<PriceListResponse> {
    return this.api.adminPriceListUpdate(id, body);
  }

  activatePriceList(id: string): Observable<PriceListResponse> {
    return this.api.adminPriceListActivate(id);
  }

  deactivatePriceList(id: string): Observable<PriceListResponse> {
    return this.api.adminPriceListDeactivate(id);
  }

  deletePriceList(id: string): Observable<void> {
    return this.api.adminPriceListDelete(id);
  }

  priceListItems(
    priceListId: string,
    pageSize = 50,
  ): CursorList<PriceListItemResponse, { readonly listingId?: string }> {
    return new CursorList<PriceListItemResponse, { readonly listingId?: string }>(
      (current, cursor, size) =>
        this.api
          .adminPriceListItems(priceListId, {
            listingId: current.listingId,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<PriceListItemResponse> => result)),
      {},
      pageSize,
    );
  }

  /**
   * Adds or replaces rows, keyed on listing **and quantity**.
   *
   * One listing can carry several rows — a unit price, a price from ten, a price from fifty — so
   * the key is the pair. Upsert rather than replace-all, because a price list of nine thousand
   * listings is not something a screen should be able to blank by sending a short array.
   */
  upsertPriceListItems(
    priceListId: string,
    body: UpsertPriceListItemsBody,
  ): Observable<PriceListItemResponse[]> {
    return this.api.adminPriceListItemsUpsert(priceListId, body);
  }

  deletePriceListItem(priceListId: string, itemId: string): Observable<void> {
    return this.api.adminPriceListItemDelete(priceListId, itemId);
  }

  /** Which of the overlapping lists actually wins for this listing, at this quantity, today. */
  resolvePrice(listingId: string, quantity: number): Observable<EffectivePrice> {
    return this.api.adminPriceResolve({ listingId, quantity });
  }

  // ---- Tax rates --------------------------------------------------------------------------------

  taxRates(filters: TaxRateFilters = {}, pageSize = 25): CursorList<TaxRateResponse, TaxRateFilters> {
    return new CursorList<TaxRateResponse, TaxRateFilters>(
      (current, cursor, size) =>
        this.api
          .adminTaxRatesList({
            HsnCode: current.hsnCode,
            ActiveOnly: current.activeOnly,
            Cursor: cursor ?? undefined,
            Size: size,
          })
          .pipe(map((result): CursorPage<TaxRateResponse> => result)),
      filters,
      pageSize,
    );
  }

  taxRate(id: string): Observable<TaxRateResponse> {
    return this.api.adminTaxRateGet(id);
  }

  createTaxRate(body: CreateTaxRateBody): Observable<TaxRateResponse> {
    return this.api.adminTaxRateCreate(body);
  }

  updateTaxRate(id: string, body: UpdateTaxRateBody): Observable<TaxRateResponse> {
    return this.api.adminTaxRateUpdate(id, body);
  }

  deleteTaxRate(id: string): Observable<void> {
    return this.api.adminTaxRateDelete(id);
  }

  /**
   * The rate an invoice raised on `asOf` would carry for this HSN code.
   *
   * Rates have effective windows and they overlap while a change is being staged, so the only
   * trustworthy answer to "what will we charge on the first of next month" is the server's.
   */
  resolveTaxRate(hsnCode: string, asOf?: string): Observable<TaxRateResolutionResponse> {
    return this.api.adminTaxRateResolve({ hsnCode, asOf });
  }
}
