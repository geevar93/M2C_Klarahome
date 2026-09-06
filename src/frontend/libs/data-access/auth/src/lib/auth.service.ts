import { Injectable, inject } from '@angular/core';
import { IdentityApiClient, SignInResponse } from '@klarahome/data-access-api';
import { Observable, catchError, map, of, shareReplay, tap } from 'rxjs';

import { Session, SessionStore } from './session.store';

/** Which surface this app talks to. The two have separate refresh endpoints and cookies. */
export type AuthSurface = 'store' | 'admin';

/**
 * Sign-in, sign-out, and the silent refresh both of them depend on.
 *
 * The refresh is **single-flight**: when five requests find an expired token at once, one refresh
 * goes out and all five wait on it. Without that, five refreshes race, the rotating refresh token
 * makes four of them invalid, and the server — correctly — treats the reuse as a stolen token and
 * revokes the whole family. Concurrency here is not an optimisation; it is the difference between
 * a working app and one that signs the user out whenever a page loads two things at once.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly identity = inject(IdentityApiClient);
  private readonly store = inject(SessionStore);

  /** The refresh currently in flight, if any. Shared by every caller that arrives while it runs. */
  private inFlightRefresh: Observable<boolean> | null = null;

  /**
   * Set by the app that owns this injector. The storefront refreshes a customer session and the
   * admin app a staff or vendor one; they are different cookies and must not be crossed.
   */
  surface: AuthSurface = 'store';

  /**
   * Re-establishes the session from the HttpOnly refresh cookie.
   *
   * Answers `false` rather than throwing when there is no session to restore: at start-up "not
   * signed in" is the ordinary case, not an error, and a rejected promise there would surface as
   * a toast on every anonymous visit.
   */
  refresh(): Observable<boolean> {
    if (this.inFlightRefresh) return this.inFlightRefresh;

    const request =
      this.surface === 'admin'
        ? this.identity.adminAuthRefresh({ skipAuth: true, silentErrors: true, showLoading: false })
        : this.identity.storeAuthRefresh({ skipAuth: true, silentErrors: true, showLoading: false });

    this.inFlightRefresh = request.pipe(
      map((response) => this.adopt(response)),
      catchError(() => {
        this.store.signOut();
        return of(false);
      }),
      // The refresh must run once and be seen by everyone waiting; `shareReplay` with
      // refCount:false keeps the answer for the callers that subscribe a tick late.
      shareReplay({ bufferSize: 1, refCount: false }),
      tap({ finalize: () => (this.inFlightRefresh = null) }),
    );

    return this.inFlightRefresh;
  }

  /** Ends the session on the server, then locally — in that order, so the cookie is really gone. */
  signOut(): Observable<void> {
    const request =
      this.surface === 'admin'
        ? this.identity.adminAuthLogout({ silentErrors: true })
        : this.identity.storeAuthLogout({ silentErrors: true });

    return request.pipe(
      catchError(() => of(undefined)),
      tap(() => this.store.signOut()),
      map(() => undefined),
    );
  }

  /**
   * Takes a sign-in answer into the session store.
   *
   * Answers `false` for a response carrying a two-factor challenge instead of a token: the user
   * has proved one factor and is not signed in yet, and the caller routes them to the TOTP step.
   */
  adopt(response: SignInResponse): boolean {
    if (!response.accessToken || !response.user || !response.expiresAt) {
      this.store.signOut();
      return false;
    }

    const session: Session = {
      userId: response.user.id,
      displayName: response.user.email ?? response.user.mobile ?? response.user.id,
      vendorId: response.user.vendorId ?? undefined,
      permissions: response.user.permissions,
      roles: response.user.roles,
      expiresAt: Date.parse(response.expiresAt),
    };

    this.store.signIn(response.accessToken, session);
    return true;
  }
}
