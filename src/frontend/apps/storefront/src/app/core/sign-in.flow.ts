import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AddressBookStore, ProfileStore } from '@klarahome/data-access-account';
import { AuthService, SessionStore, SignInResponse } from '@klarahome/data-access-auth';
import { CartStore } from '@klarahome/data-access-cart';
import { WishlistStore } from '@klarahome/data-access-engagement';
import { AnalyticsEvents, AnalyticsService, ToastService } from '@klarahome/util';

/**
 * What happens after a customer signs in — in one place, because four screens do it.
 *
 * Login, register, OTP and the two-factor step all end the same way, and the steps have to be the
 * same every time or the storefront is subtly different depending on which door was used. In order:
 *
 *  1. **Merge the anonymous basket.** Someone who filled a cart and then signed in to check out
 *     must not lose it (docs/05-frontend-architecture.md §3.6). The merge is the API's decision —
 *     it owns the conflict rules — and what the customer is told about it is decided here.
 *  2. **Load what the signed-in shell shows.** The wishlist behind the hearts, the profile behind
 *     the greeting, the address book the checkout is about to want.
 *  3. **Go where they were going.** A `returnUrl` is carried through the sign-in screens, which is
 *     the difference between a deep link that works and one that lands on a dashboard.
 *
 * This is app code and not a `data-access` service on purpose: merging a cart into a session
 * touches three libraries that have no business knowing about each other, and the one place they
 * legitimately meet is the application that composes them.
 */
@Injectable({ providedIn: 'root' })
export class SignInFlow {
  private readonly auth = inject(AuthService);
  private readonly session = inject(SessionStore);
  private readonly cart = inject(CartStore);
  private readonly wishlist = inject(WishlistStore);
  private readonly profile = inject(ProfileStore);
  private readonly addresses = inject(AddressBookStore);
  private readonly toasts = inject(ToastService);
  private readonly analytics = inject(AnalyticsService);
  private readonly router = inject(Router);

  /**
   * Completes a sign-in.
   *
   * Answers `false` when the response was a two-factor challenge rather than a session — the caller
   * routes to the code screen with the challenge token. Nothing below runs in that case, because
   * the customer is not signed in yet: one factor proved is not a session.
   */
  complete(response: SignInResponse, returnUrl: string | null): boolean {
    if (!this.session.isAuthenticated()) return false;

    this.analytics.track(AnalyticsEvents.login, { method: response.user?.mobile ? 'otp' : 'password' });

    // The merge is fired and not waited on. It is a server-side reconciliation of two baskets, and
    // holding the customer on a spinner while it happens buys nothing — the header badge updates
    // when it lands, and the cart page reads the merged basket whenever they reach it.
    this.cart.mergeAfterSignIn().subscribe({
      next: (merged) => {
        if (merged && merged.lines.length > 0) {
          this.toasts.info('We have kept the items you had in your cart.');
        }
      },
      error: () => undefined,
    });

    this.wishlist.load();
    this.profile.loadOnce();
    this.addresses.loadOnce();

    void this.router.navigateByUrl(this.safeReturnUrl(returnUrl));
    return true;
  }

  /**
   * Signs the customer out and clears everything that belonged to them.
   *
   * Every store is cleared explicitly rather than relying on a reload: this is a single-page app,
   * and a wishlist or an address book left in memory after a sign-out is the next person on that
   * device seeing somebody else's data.
   */
  signOut(): void {
    this.auth.signOut().subscribe(() => {
      this.cart.clear();
      this.wishlist.clear();
      this.profile.clear();
      this.addresses.clear();
      this.toasts.success('You are signed out.');
      void this.router.navigateByUrl('/');
    });
  }

  /**
   * The URL to return to, if it is one of ours.
   *
   * A `returnUrl` arrives in a query string, which means anybody can put anything in it. Only a
   * path beginning with a single `/` is honoured — `//evil.example` is a protocol-relative URL and
   * would send the customer off-site with our sign-in page as the referrer, which is the
   * open-redirect this check exists for (docs/07-security-compliance.md).
   */
  private safeReturnUrl(returnUrl: string | null): string {
    if (!returnUrl) return '/account';
    if (!returnUrl.startsWith('/') || returnUrl.startsWith('//')) return '/account';
    // The sign-in screens themselves are never a destination; returning to one loops.
    if (returnUrl.startsWith('/auth')) return '/account';
    return returnUrl;
  }
}
