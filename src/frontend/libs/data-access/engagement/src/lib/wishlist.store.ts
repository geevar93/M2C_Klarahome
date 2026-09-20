import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { ReviewsApiClient, WishlistResponse } from '@klarahome/data-access-api';
import { SessionStore, isApiError } from '@klarahome/data-access-auth';
import { catchError, of, tap } from 'rxjs';

/**
 * The customer's default wishlist, and the heart on every product card.
 *
 * **A wishlist belongs to a signed-in customer.** There is no anonymous wishlist on the API — a
 * saved item has to survive a device change, which a cookie cannot promise — so the store holds
 * nothing while signed out and `toggle` reports that instead of failing. The page turns that into
 * "sign in to save this", which is the only honest thing to offer.
 *
 * The toggle is **optimistic**. Saving something is a gesture, often mid-scroll, and a heart that
 * fills a second later has already been tapped twice. The set is updated in place and reverted if
 * the write fails; the API is keyed on the variant, so a double tap resolves to one state rather
 * than two rows.
 */
@Injectable({ providedIn: 'root' })
export class WishlistStore {
  private readonly api = inject(ReviewsApiClient);
  private readonly session = inject(SessionStore);

  private readonly list = signal<WishlistResponse | null>(null);
  private readonly savedIds = signal<readonly string[]>([]);
  private readonly loaded = signal(false);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal(false);

  readonly current: Signal<WishlistResponse | null> = this.list.asReadonly();
  /** The variant ids on the list — what a product card checks to decide its heart. */
  readonly variantIds: Signal<readonly string[]> = this.savedIds.asReadonly();
  readonly itemCount = computed(() => this.savedIds().length);
  /** Whether a fetch is in flight. A page's loading skeleton should read this, not `!current()`. */
  readonly loading: Signal<boolean> = this.loadingSignal.asReadonly();
  /**
   * Whether the last fetch failed for a reason other than "nothing saved yet". `current()` stays
   * `null` in both cases, so a page that used it alone as its loading proxy would skeleton forever
   * on a genuine failure — this is what tells it to show `kh-error-state` instead.
   */
  readonly error: Signal<boolean> = this.errorSignal.asReadonly();

  /** Loads once per application lifetime, and only for a signed-in customer. */
  loadOnce(): void {
    if (this.loaded() || !this.session.isAuthenticated()) return;
    this.loaded.set(true);
    this.load();
  }

  load(): void {
    if (!this.session.isAuthenticated()) return;

    this.loadingSignal.set(true);
    this.errorSignal.set(false);

    this.api
      .storeGetWishlist(undefined, { silentErrors: true, showLoading: false })
      .pipe(
        tap((wishlist) => {
          this.apply(wishlist);
          this.loadingSignal.set(false);
        }),
        catchError((error: unknown) => {
          this.loadingSignal.set(false);
          if (isApiError(error) && error.status === 404) {
            // No list yet is the ordinary case for a new customer — not a failure.
            this.apply(null);
          } else {
            this.errorSignal.set(true);
          }
          return of(null);
        }),
      )
      .subscribe();
  }

  isSaved(variantId: string): boolean {
    return this.savedIds().includes(variantId);
  }

  /**
   * Adds or removes a variant, optimistically.
   *
   * Answers whether the request was made at all — false means the visitor is not signed in, which
   * is a state the caller has to handle rather than an error to report.
   *
   * `options.onSettled` fires once the request resolves, with whether it removed or added the item
   * and whether it succeeded — a page that wants to toast the outcome (the wishlist page does) needs
   * that, since the optimistic update itself is silent by design.
   */
  toggle(
    variantId: string,
    options?: { readonly onSettled?: (result: { readonly removed: boolean; readonly ok: boolean }) => void },
  ): boolean {
    if (!this.session.isAuthenticated()) return false;

    const wasSaved = this.isSaved(variantId);
    const before = this.savedIds();
    this.savedIds.set(wasSaved ? before.filter((id) => id !== variantId) : [...before, variantId]);

    // Two branches rather than one request variable: a remove answers 204 and an add answers the
    // whole list, and pretending they are the same call means sniffing the response to find out
    // which one came back.
    const rollback = (): void => {
      // Put it back exactly as it was. Recomputing from the current set would lose a second
      // toggle the shopper made while this one was in flight.
      this.savedIds.set(before);
      options?.onSettled?.({ removed: wasSaved, ok: false });
    };

    if (wasSaved) {
      this.api.storeRemoveFromWishlist(variantId, undefined, { silentErrors: true }).subscribe({
        next: () => options?.onSettled?.({ removed: true, ok: true }),
        error: rollback,
      });
    } else {
      this.api
        .storeAddToWishlist({ variantId, wishlistId: null, note: null, priority: 0 }, { silentErrors: true })
        .subscribe({
          next: (wishlist) => {
            this.apply(wishlist);
            options?.onSettled?.({ removed: false, ok: true });
          },
          error: rollback,
        });
    }

    return true;
  }

  /** Drops everything. Called on sign-out, where the next wishlist belongs to somebody else. */
  clear(): void {
    this.list.set(null);
    this.savedIds.set([]);
    this.loaded.set(false);
  }

  private apply(wishlist: WishlistResponse | null): void {
    this.list.set(wishlist);
    this.savedIds.set((wishlist?.items ?? []).map((item) => item.variantId));
  }
}
