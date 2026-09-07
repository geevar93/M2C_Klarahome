import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { CartApiClient, CartResponse } from '@klarahome/data-access-api';
import { INR, Money, money } from '@klarahome/domain';
import { catchError, of, tap } from 'rxjs';

/**
 * What the shell knows about the basket.
 *
 * **Read-only, deliberately.** The header badge and the mini-cart need a count, a few lines and a
 * subtotal on every page; adding, removing and re-quoting need the optimistic update, the
 * rollback and the reason banner that Step 25 builds against the checkout flow. Splitting them
 * this way keeps a half-built mutation out of the shell, where it would be reachable from every
 * page in the shop.
 *
 * The cart is anonymous until sign-in — the API keys it on a cookie — so a failure here is
 * ordinary rather than exceptional, and it is swallowed: a shopper with an unreachable cart
 * service should see a shop with an empty badge, not a toast on every page they open.
 */
@Injectable({ providedIn: 'root' })
export class CartSummaryStore {
  private readonly api = inject(CartApiClient);

  private readonly cart = signal<CartResponse | null>(null);
  private readonly loading = signal(false);
  private readonly loadedOnce = signal(false);

  /** The cart as the API last returned it, or null if it has never been read. */
  readonly current: Signal<CartResponse | null> = this.cart.asReadonly();
  readonly isLoading: Signal<boolean> = this.loading.asReadonly();

  /**
   * The number of items, not the number of lines: a basket with one line of quantity three reads
   * as "3" on every storefront a shopper has used, and a badge saying "1" for it looks broken.
   */
  readonly itemCount = computed(() =>
    (this.cart()?.lines ?? []).reduce((total, line) => total + (line.savedForLater ? 0 : line.quantity), 0),
  );

  /** The lines in the basket, saved-for-later excluded. */
  readonly lines = computed(() => (this.cart()?.lines ?? []).filter((line) => !line.savedForLater));

  /**
   * The subtotal the API computed, or null while unknown.
   *
   * Read off the quote rather than summed here. The frontend does not do money arithmetic
   * (docs/04-api-specification.md §1) — the total that matters is the one the pricing engine
   * produced, and a client that added the lines up itself would eventually disagree with the
   * invoice by a rupee nobody could explain.
   */
  readonly subtotal = computed<Money | null>(() => {
    const cart = this.cart();
    if (!cart) return null;
    const quote = cart.quote;
    if (!quote) return null;
    return money(quote.subtotal, cart.currencyCode || INR);
  });

  /** Loads the cart. Safe to call on every navigation; it is one cheap GET behind the scenes. */
  load(): void {
    if (this.loading()) return;
    this.loading.set(true);

    this.api
      .storeGetCart({ silentErrors: true, showLoading: false })
      .pipe(
        tap((cart) => this.cart.set(cart)),
        catchError(() => {
          // No cart yet, or the service is unreachable. Both mean "nothing to show".
          this.cart.set(null);
          return of(null);
        }),
      )
      .subscribe(() => {
        this.loading.set(false);
        this.loadedOnce.set(true);
      });
  }

  /**
   * Installs a cart the API has just answered with.
   *
   * Every cart mutation returns the whole basket, so the write's own response is the freshest
   * thing there is — refetching after it would cost a request and could race the write it was
   * meant to reflect. `CartActions` calls this; nothing else should.
   */
  replace(cart: CartResponse): void {
    this.cart.set(cart);
    this.loadedOnce.set(true);
  }

  /** Loads once per application lifetime — what the shell calls on start-up. */
  loadOnce(): void {
    if (!this.loadedOnce()) this.load();
  }

  /** Drops the cached basket. Called on sign-out, where the next cart belongs to somebody else. */
  clear(): void {
    this.cart.set(null);
    this.loadedOnce.set(false);
  }
}
