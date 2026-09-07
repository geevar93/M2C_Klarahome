import { Injectable, inject } from '@angular/core';
import { CartApiClient, CartResponse, isApiError } from '@klarahome/data-access-api';
import { Observable, tap } from 'rxjs';

import { CartSummaryStore } from './cart-summary.store';

/**
 * Adding to the basket from a product page or a grid.
 *
 * **Deliberately just this.** `CartSummaryStore` is read-only because the cart *page* — the
 * optimistic quantity edits, the rollback, the reason banner, the coupon — belongs to Step 25 and
 * has to be built against the checkout flow. What Step 24 needs is one write: put this listing in
 * the basket and let the header say so.
 *
 * The refresh after a successful add is the point. The API answers the whole cart, so the badge,
 * the mini-cart and the subtotal are all correct from the same response rather than from a second
 * request that might race the first.
 *
 * Failures are **not** swallowed. `CART_ITEM_OUT_OF_STOCK` and `PRICE_CHANGED` are things the
 * shopper has to be told (docs/05-frontend-architecture.md §3.6), and the caller names them —
 * hence `silentErrors`, which suppresses the generic toast so a specific message can replace it.
 */
@Injectable({ providedIn: 'root' })
export class CartActions {
  private readonly api = inject(CartApiClient);
  private readonly summary = inject(CartSummaryStore);

  /**
   * Adds a listing to the basket.
   *
   * The listing, not the variant: which seller is being bought from is a decision the shopper made
   * on the PDP — explicitly, or by accepting the buy box — and the cart line has to record it.
   *
   * **No stock is held here.** A reservation is taken at order placement and never at add-to-cart
   * (Step 13), which is why a basket can go stale and why the cart page re-validates before
   * checkout.
   */
  add(listingId: string, quantity = 1): Observable<CartResponse> {
    return this.api
      .storeAddCartItem({ listingId, quantity }, { silentErrors: true })
      .pipe(tap((cart) => this.summary.replace(cart)));
  }

  /**
   * The message to show for a failed add.
   *
   * The mapping lives here rather than in each page because the same three failures reach a
   * product card, a PDP and a wishlist, and three copies of this would eventually word the same
   * refusal three ways. An unrecognised code falls through to the API's own message, which the
   * error normalisation interceptor has already made presentable.
   */
  describeFailure(error: unknown): string {
    if (!isApiError(error)) return 'We could not add that to your cart. Please try again.';

    switch (error.code) {
      case 'CART_ITEM_OUT_OF_STOCK':
        return 'That is out of stock now. Try another seller or a different option.';
      case 'PRICE_CHANGED':
        return 'The price changed while you were looking. Your cart has the current price.';
      case 'CART_ITEM_LIMIT_REACHED':
      case 'CART_LIMIT_REACHED':
        return 'Your cart is full. Remove something before adding more.';
      case 'MAX_ORDER_QUANTITY_EXCEEDED':
        return 'This seller limits how many of these one order may have.';
      default:
        return error.message || 'We could not add that to your cart. Please try again.';
    }
  }
}
