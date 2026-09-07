import { Injectable, inject } from '@angular/core';
import {
  API_BASE_URL,
  ExternalProviderResponse,
  IdentityApiClient,
  OtpRequestedResponse,
  SignInResponse,
  TwoFactorSetupResponse,
} from '@klarahome/data-access-api';
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

  /**
   * The API origin, for the one call in this service that is a browser navigation rather than an
   * XHR. See {@link externalSignInUrl}.
   */
  private readonly apiBaseUrl = inject(API_BASE_URL).replace(/\/+$/, '');

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

  /**
   * Asks for a one-time code.
   *
   * OTP is the storefront's primary sign-in: most Indian shoppers have a mobile number and no
   * password, and the API creates the customer on first successful verification — so there is no
   * separate "register with mobile" flow to keep in step with this one.
   *
   * The answer says how long the code lasts and how many digits it has, and the screen is built
   * from that rather than from a constant: a deployment that shortens the code must not need a
   * front-end release. Note that SMS can be switched off by an operator (Step 7A), in which case
   * this is refused and the caller offers the password form instead.
   */
  requestOtp(mobile: string): Observable<OtpRequestedResponse> {
    return this.identity.storeAuthOtpRequest({ mobile }, { skipAuth: true, silentErrors: true });
  }

  /** Verifies a code. A true answer means the session is live; the caller routes from there. */
  verifyOtp(mobile: string, code: string): Observable<SignInResponse> {
    return this.identity
      .storeAuthOtpVerify({ mobile, code }, { skipAuth: true, silentErrors: true })
      .pipe(tap((response) => this.adopt(response)));
  }

  /**
   * Email and password.
   *
   * The **only** way into the admin app, and one of two ways into the storefront. Which endpoint
   * is called follows `surface`, exactly as `refresh` and `signOut` already do: the two issue
   * different cookies with different lifetimes and different session policies, and a storefront
   * login that minted a staff session would be the most serious defect this application could
   * have.
   *
   * A response carrying a `challenge` instead of a token is not a failure — it is the second
   * factor being demanded, and `adopt` answers false so the caller shows the code step.
   */
  signIn(email: string, password: string): Observable<SignInResponse> {
    const request =
      this.surface === 'admin'
        ? this.identity.adminAuthLogin({ email, password }, { skipAuth: true, silentErrors: true })
        : this.identity.storeAuthLogin({ email, password }, { skipAuth: true, silentErrors: true });

    return request.pipe(tap((response) => this.adopt(response)));
  }

  /**
   * Creates an account with an email address and a password.
   *
   * The response is adopted the same way a sign-in is, because the API signs the new customer in —
   * making them type their password again immediately after choosing it is a step that exists only
   * to lose people.
   */
  register(body: {
    email: string;
    password: string;
    mobile: string | null;
    marketingConsent: boolean;
  }): Observable<SignInResponse> {
    return this.identity
      .storeAuthRegister(body, { skipAuth: true, silentErrors: true })
      .pipe(tap((response) => this.adopt(response)));
  }

  /**
   * The identity providers this deployment actually offers, for the sign-in page's buttons.
   *
   * **The list is the server's to decide, and it is frequently empty.** A provider appears only
   * when the `identity.external-login` feature flag is on *and* that provider has been given a
   * client id and secret — a button that fails when somebody presses it is worse than no button,
   * so a half-configured provider is not listed (ADR-014). A fresh deployment has none of this,
   * which is the ordinary case rather than a fault, and the caller renders nothing.
   *
   * Errors are swallowed to an empty list for the same reason: whether Google is on offer is not
   * worth an error banner over a form the shopper can still complete with an email and a password.
   */
  externalProviders(): Observable<readonly ExternalProviderResponse[]> {
    // Admin has no external route on purpose — staff and vendors sign in with a password and a
    // second factor (ADR-014 decision 3), and there are no endpoints under /admin to call.
    if (this.surface === 'admin') return of([]);

    return this.identity
      .storeAuthExternalProviders({ skipAuth: true, silentErrors: true, showLoading: false })
      .pipe(
        map((providers) => providers ?? []),
        catchError(() => of([])),
      );
  }

  /**
   * Where to send the browser to begin signing in with a provider.
   *
   * A URL rather than a request, and that is the whole point: the flow is two top-level browser
   * redirects with a server-to-server token exchange between them (ADR-014 decision 2). Fetching
   * this with `HttpClient` would follow the 302 as an XHR, land the provider's consent screen in a
   * response body nobody can render, and drop the `SameSite=Lax` state cookie that only a real
   * navigation carries. The caller assigns it to `location.href`.
   *
   * `returnUrl` is passed through untouched and checked against the server's allow-list, never
   * here — an open-redirect check that lives in the client is not a check
   * (docs/07-security-compliance.md §3).
   */
  externalSignInUrl(provider: string, returnUrl?: string | null): string {
    const query = returnUrl ? `?returnUrl=${encodeURIComponent(returnUrl)}` : '';
    return `${this.apiBaseUrl}/api/v1/store/auth/external/${encodeURIComponent(provider)}/start${query}`;
  }

  /**
   * Starts a password reset.
   *
   * The API answers 204 whether or not the address is registered, and this passes that through
   * unchanged: an endpoint that distinguished the two would be an account-enumeration oracle
   * (docs/07-security-compliance.md). The screen therefore says "if that address is registered…"
   * rather than "we have sent you an email", because only one of those is true.
   */
  forgotPassword(email: string): Observable<void> {
    const body = { email };
    return this.surface === 'admin'
      ? this.identity.adminAuthPasswordForgot(body, { skipAuth: true, silentErrors: true })
      : this.identity.storeAuthPasswordForgot(body, { skipAuth: true, silentErrors: true });
  }

  /** Finishes a reset with the token from the emailed link. */
  resetPassword(email: string, token: string, newPassword: string): Observable<void> {
    const body = { email, token, newPassword };
    return this.surface === 'admin'
      ? this.identity.adminAuthPasswordReset(body, { skipAuth: true, silentErrors: true })
      : this.identity.storeAuthPasswordReset(body, { skipAuth: true, silentErrors: true });
  }

  /**
   * The second factor, when a sign-in came back with a challenge instead of a token.
   *
   * Rare on the storefront — a customer with TOTP enrolled — but the response shape allows it on
   * every sign-in path, and a screen that ignored the challenge would show a signed-in header for
   * a session that does not exist.
   */
  verifyTwoFactor(challengeToken: string, code: string): Observable<SignInResponse> {
    const body = { challengeToken, code };
    const request =
      this.surface === 'admin'
        ? this.identity.adminAuthTwoFactorVerify(body, { skipAuth: true, silentErrors: true })
        : this.identity.storeAuthTwoFactorVerify(body, { skipAuth: true, silentErrors: true });

    return request.pipe(tap((response) => this.adopt(response)));
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
   * Enrols a second factor during sign-in, when the API demands one before it will issue a token.
   *
   * Distinct from `AdminSessionService.startTwoFactorSetup`, which is a signed-in user choosing to
   * turn TOTP on. This is the other case: an operator has made two-factor mandatory for the role,
   * so the user cannot get in until they have enrolled — and the only credential they hold at that
   * moment is the challenge token their password earned.
   */
  enrolTwoFactor(challengeToken: string): Observable<TwoFactorSetupResponse> {
    const body = { challengeToken };
    return this.surface === 'admin'
      ? this.identity.adminAuthTwoFactorEnrol(body, { skipAuth: true, silentErrors: true })
      : this.identity.storeAuthTwoFactorEnrol(body, { skipAuth: true, silentErrors: true });
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
