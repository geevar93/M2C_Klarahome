import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { CartApiClient, CartResponse, isApiError } from '@klarahome/data-access-api';
import { Observable, catchError, finalize, of, tap, throwError } from 'rxjs';

import { CartSummaryStore } from './cart-summary.store';

/**
 * The basket, with everything that changes it.
 *
 * `CartSummaryStore` is the shell's read-only view — a badge and a mini-cart on every page. This is
 * the cart *page*: quantities, removal, save-for-later and the coupon. They are two objects rather
 * than one because a half-built mutation reachable from the header is a mutation that will be
 * called from the header, and because the shell must not pull the cart page's chunk into the
 * initial bundle.
 *
 * **Every mutation is optimistic** (docs/05-frontend-architecture.md §3.6). A shopper going from
 * one to four taps plus four times in about a second; a cart that waits for a round trip between
 * taps loses three of them, and one that disables the control while it waits is worse. So the line
 * is updated in place, the request goes out, and a refusal restores exactly what was there before
 * along with the reason.
 *
 * **The API's answer always wins.** Every cart endpoint returns the whole basket including a fresh
 * quote, so a successful write replaces the local state outright rather than merging into it. That
 * is what keeps the client from having a private opinion about a total: the optimistic state is a
 * guess held for a few hundred milliseconds, never a computation the customer is charged against.
 *
 * The one number this store does compute is a line's `unitPrice × quantity`, and only while a
 * change is in flight — the single sum `@klarahome/domain` sanctions, replaced by the server's own
 * figure the moment it arrives.
 */
@Injectable({ providedIn: 'root' })
export class CartStore {
  private readonly api = inject(CartApiClient);
  private readonly summary = inject(CartSummaryStore);

  private readonly cart = signal<CartResponse | null>(null);
  private readonly loading = signal(false);
  private readonly loaded = signal(false);
  /** Line ids with a write in flight. A set, because two lines can be changing at once. */
  private readonly pending = signal<readonly string[]>([]);
  private readonly couponBusy = signal(false);
  private readonly couponMessage = signal<string | null>(null);

  readonly current: Signal<CartResponse | null> = this.cart.asReadonly();
  readonly isLoading: Signal<boolean> = this.loading.asReadonly();
  readonly hasLoaded: Signal<boolean> = this.loaded.asReadonly();
  readonly isCouponBusy: Signal<boolean> = this.couponBusy.asReadonly();

  /** Why the code the shopper typed did nothing, or null. Comes off the quote, not off an error. */
  readonly couponRejection = computed(
    () => this.couponMessage() ?? this.cart()?.quote?.couponRejection ?? null,
  );

  readonly lines = computed(() => (this.cart()?.lines ?? []).filter((line) => !line.savedForLater));
  readonly savedForLater = computed(() => this.cart()?.savedForLater ?? []);
  readonly groups = computed(() => this.cart()?.groups ?? []);
  readonly quote = computed(() => this.cart()?.quote ?? null);
  readonly currencyCode = computed(() => this.cart()?.currencyCode ?? 'INR');
  readonly couponCode = computed(() => this.cart()?.couponCode ?? null);

  /** Basket-level problems — a coupon that expired, a total under a minimum. */
  readonly issues = computed(() => this.cart()?.issues ?? []);

  readonly itemCount = computed(() => this.lines().reduce((total, line) => total + line.quantity, 0));
  readonly isEmpty = computed(
    () => this.loaded() && this.lines().length === 0 && this.savedForLater().length === 0,
  );

  /**
   * Whether checkout may be started.
   *
   * The API's own verdict, not a rule reimplemented here. It knows about stock, seller
   * serviceability, minimum order values and the coupon; a client that decided for itself would
   * either block a valid basket or send one that is refused at the next screen.
   */
  readonly isReadyForCheckout = computed(() => this.cart()?.isReadyForCheckout ?? false);

  isPending(lineId: string): boolean {
    return this.pending().includes(lineId);
  }

  /** Loads the basket. The cart page calls this on entry; it is one GET. */
  load(): void {
    if (this.loading()) return;
    this.loading.set(true);

    this.api
      .storeGetCart({ silentErrors: true })
      .pipe(
        catchError(() => {
          // No basket yet is the ordinary case for a first-time visitor, and indistinguishable
          // here from an unwell service. Both mean "there is nothing in your cart".
          return of(null);
        }),
        finalize(() => {
          this.loading.set(false);
          this.loaded.set(true);
        }),
      )
      .subscribe((cart) => this.apply(cart));
  }

  /**
   * Changes a line's quantity.
   *
   * Optimistic: the line and its total move now, the request goes out, and a refusal puts back the
   * basket exactly as it was — not a recomputed version of it, which would lose a second change
   * the shopper made while this one was in flight.
   */
  updateQuantity(lineId: string, quantity: number): Observable<CartResponse> {
    const before = this.cart();
    this.apply(this.withLineQuantity(before, lineId, quantity));
    return this.write(
      lineId,
      this.api.storeUpdateCartItem(lineId, { quantity, savedForLater: null }, { silentErrors: true }),
      before,
    );
  }

  remove(lineId: string): Observable<CartResponse> {
    const before = this.cart();
    this.apply(this.withoutLine(before, lineId));
    return this.write(lineId, this.api.storeRemoveCartItem(lineId, { silentErrors: true }), before);
  }

  /**
   * Moves a line between the basket and the saved list.
   *
   * Not optimistic, and deliberately: the line moves between two lists rather than changing in
   * place, so a guess means rendering it in both or in neither for a moment. It is also a
   * considered action rather than a repeated tap, so the round trip is not in anybody's way.
   */
  setSavedForLater(lineId: string, savedForLater: boolean): Observable<CartResponse> {
    const before = this.cart();
    return this.write(
      lineId,
      this.api.storeUpdateCartItem(lineId, { quantity: null, savedForLater }, { silentErrors: true }),
      before,
    );
  }

  /**
   * Applies a coupon.
   *
   * **A code that does not apply is not an error.** The API answers 200 with the basket, its price
   * unchanged, and `quote.couponRejection` saying why — because a basket with a bad coupon on it
   * still has a price, and refusing to quote one would leave the cart with nothing to render
   * (Step 12). So the rejection is read off the quote, and only a transport failure lands in
   * `catchError`.
   */
  applyCoupon(code: string): Observable<CartResponse> {
    this.couponBusy.set(true);
    this.couponMessage.set(null);

    return this.api.storeApplyCoupon({ code: code.trim().toUpperCase() }, { silentErrors: true }).pipe(
      tap((cart) => this.apply(cart)),
      catchError((error: unknown) => {
        this.couponMessage.set(
          isApiError(error) ? error.message : 'We could not check that code just now. Please try again.',
        );
        return throwError(() => error);
      }),
      finalize(() => this.couponBusy.set(false)),
    );
  }

  removeCoupon(): Observable<CartResponse> {
    this.couponBusy.set(true);
    this.couponMessage.set(null);

    return this.api.storeRemoveCoupon({ silentErrors: true }).pipe(
      tap((cart) => this.apply(cart)),
      finalize(() => this.couponBusy.set(false)),
    );
  }

  /**
   * Merges the anonymous basket into the customer's on sign-in.
   *
   * Called by the sign-in flow, not by the auth library: which of the two baskets survives a
   * conflict is the API's decision (Step 13), and what the customer is told about it belongs to
   * whichever screen they signed in from.
   */
  mergeAfterSignIn(): Observable<CartResponse | null> {
    return this.api.storeMergeCart({ silentErrors: true }).pipe(
      tap((cart) => this.apply(cart)),
      catchError(() => {
        // Nothing to merge — a customer who signed in without an anonymous basket. Their own cart
        // is read on the next navigation like anybody else's.
        this.load();
        return of(null);
      }),
    );
  }

  /** Drops the basket. Called on sign-out, where the next cart belongs to somebody else. */
  clear(): void {
    this.cart.set(null);
    this.loaded.set(false);
    this.couponMessage.set(null);
    this.summary.clear();
  }

  /**
   * Sends one line's write and keeps the two stores in step.
   *
   * The shell's summary is updated from the same response rather than refetched: the write's own
   * answer is the freshest basket there is, and a second GET after it would cost a request and
   * could race the write it was meant to reflect.
   */
  private write(
    lineId: string,
    request: Observable<CartResponse>,
    before: CartResponse | null,
  ): Observable<CartResponse> {
    this.pending.update((ids) => (ids.includes(lineId) ? ids : [...ids, lineId]));

    return request.pipe(
      tap((cart) => this.apply(cart)),
      catchError((error: unknown) => {
        this.apply(before);
        return throwError(() => error);
      }),
      finalize(() => this.pending.update((ids) => ids.filter((id) => id !== lineId))),
    );
  }

  private apply(cart: CartResponse | null): void {
    this.cart.set(cart);
    this.loaded.set(true);
    if (cart) this.summary.replace(cart);
    else this.summary.clear();
  }

  /**
   * The optimistic basket after a quantity change.
   *
   * The line total moves with the quantity so the row is not visibly wrong for the length of a
   * round trip. **The basket's own totals are left alone**: they carry tax, shipping bands and
   * promotions, and a client that guessed at them would show a number the shopper is not charged.
   * The summary panel reads the quote, so it holds its previous figures for a moment rather than
   * showing an invented one — which is the honest thing for it to do.
   */
  private withLineQuantity(cart: CartResponse | null, lineId: string, quantity: number): CartResponse | null {
    if (!cart) return cart;
    return {
      ...cart,
      lines: cart.lines.map((line) =>
        line.id === lineId ? { ...line, quantity, lineTotal: round(line.unitPrice * quantity) } : line,
      ),
    };
  }

  private withoutLine(cart: CartResponse | null, lineId: string): CartResponse | null {
    if (!cart) return cart;
    return {
      ...cart,
      lines: cart.lines.filter((line) => line.id !== lineId),
      lineCount: Math.max(0, cart.lineCount - 1),
    };
  }
}

/**
 * Rupees, to the paise, with the binary floating point taken out.
 *
 * The one multiplication a client is allowed to do — a line's `unitPrice × quantity` while a change
 * is in flight (`@klarahome/domain`'s `money.ts`). Done in paise because `19.99 * 3` is not
 * `59.97` in binary, and a total assembled from such sums drifts visibly.
 */
const round = (amount: number): number => Math.round(amount * 100) / 100;
