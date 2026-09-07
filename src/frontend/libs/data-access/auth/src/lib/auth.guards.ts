import { InjectionToken, inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { AuthService } from './auth.service';
import { SessionStore } from './session.store';

/**
 * Route guards.
 *
 * They decide what is *rendered*, never what is *allowed*. The API enforces every permission
 * itself and answers 403 to a caller who reaches an endpoint another way — a guard that hid a
 * button is a courtesy to the user, not a security boundary (docs/07-security-compliance.md).
 */

/**
 * Where an unauthenticated visitor is sent.
 *
 * A token rather than a constant because the two apps do not agree: the storefront signs in at
 * `/auth/login`, alongside registration and OTP, while the back office has one way in and it is
 * `/login` (docs/05-frontend-architecture.md §4.2). Hard-coding either would make this library
 * usable by one app, and duplicating the guards would make them drift.
 */
export const SIGN_IN_PATH = new InjectionToken<string>('KH_SIGN_IN_PATH', {
  providedIn: 'root',
  factory: () => '/auth/login',
});

/**
 * Requires a session; restores one from the refresh cookie before giving up.
 *
 * Written as a plain function rather than only as a `CanActivateFn` so `permissionGuard` can
 * compose it: `CanActivateFn`'s return type includes `RedirectCommand` and an `Observable`, and a
 * caller that wanted to inspect the answer would have to narrow a union it never produces.
 * It must be called from an injection context, which both guards are.
 */
export async function requireSession(returnUrl: string): Promise<boolean | UrlTree> {
  // Every injection happens before the first `await`. After one the injection context is gone and
  // `inject()` throws — which is why the sign-in path is read here rather than where it is used.
  const store = inject(SessionStore);
  const auth = inject(AuthService);
  const router = inject(Router);
  const signInPath = inject(SIGN_IN_PATH);

  if (store.isAuthenticated()) return true;
  if (await firstValueFrom(auth.refresh())) return true;

  // The URL is carried so sign-in can return the user to where they were going, which is the
  // difference between a deep link that works and one that dumps them on a dashboard.
  return router.createUrlTree([signInPath], { queryParams: { returnUrl } });
}

export const authenticatedGuard: CanActivateFn = (_route, state) => requireSession(state.url);

/**
 * Requires one of a set of permissions.
 *
 * Any-of rather than all-of: a screen is usually reachable by more than one role, and the finer
 * distinctions inside it are made by hiding individual controls.
 */
export const permissionGuard = (...permissions: readonly string[]): CanActivateFn => {
  return async (_route, state): Promise<boolean | UrlTree> => {
    // Both injected before the first `await`. After one, the injection context is gone and
    // `inject()` throws — which would turn "no permission" into a blank screen and a stack trace.
    const store = inject(SessionStore);
    const router = inject(Router);

    const session = await requireSession(state.url);
    if (session !== true) return session;

    return store.hasAnyPermission(permissions) ? true : router.createUrlTree(['/403']);
  };
};

/** Keeps a signed-in user off the sign-in screen. */
export const anonymousOnlyGuard: CanActivateFn = (): boolean | UrlTree => {
  const store = inject(SessionStore);
  return store.isAuthenticated() ? inject(Router).createUrlTree(['/']) : true;
};

/**
 * Keeps a seller out of the platform's own screens.
 *
 * **Permission is not enough here, and that is the whole reason this exists.** A vendor owner
 * holds `vendors.vendor.read` — they read *their own* seller record with it — so a route guarded
 * only on that permission would put the platform's seller directory in their navigation. What
 * separates the two is not a permission but a scope: a token carrying a `vendorId` is confined to
 * one seller by the API's query filter, and the platform-wide screens are not for it.
 *
 * As ever, the API is what enforces this; the guard is what stops the screen being offered.
 */
export const platformOnlyGuard: CanActivateFn = async (_route, state): Promise<boolean | UrlTree> => {
  const store = inject(SessionStore);
  const router = inject(Router);

  const session = await requireSession(state.url);
  if (session !== true) return session;

  return store.vendorId() === null ? true : router.createUrlTree(['/403']);
};

/** The mirror: a seller's own screens, which platform staff have no seller to look at through. */
export const vendorOnlyGuard: CanActivateFn = async (_route, state): Promise<boolean | UrlTree> => {
  const store = inject(SessionStore);
  const router = inject(Router);

  const session = await requireSession(state.url);
  if (session !== true) return session;

  return store.vendorId() !== null ? true : router.createUrlTree(['/403']);
};

/**
 * Requires a permission **and** platform scope.
 *
 * Composed rather than written out at each route, because the combination is common — most of the
 * back office's platform-only screens are also permission-gated — and because writing
 * `canActivate: [permissionGuard(...), platformOnlyGuard]` runs `requireSession` twice.
 */
export const platformPermissionGuard = (...permissions: readonly string[]): CanActivateFn => {
  return async (_route, state): Promise<boolean | UrlTree> => {
    const store = inject(SessionStore);
    const router = inject(Router);

    const session = await requireSession(state.url);
    if (session !== true) return session;

    const allowed = store.vendorId() === null && store.hasAnyPermission(permissions);
    return allowed ? true : router.createUrlTree(['/403']);
  };
};
