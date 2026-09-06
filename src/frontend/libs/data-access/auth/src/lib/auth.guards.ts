import { inject } from '@angular/core';
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
 * Requires a session; restores one from the refresh cookie before giving up.
 *
 * Written as a plain function rather than only as a `CanActivateFn` so `permissionGuard` can
 * compose it: `CanActivateFn`'s return type includes `RedirectCommand` and an `Observable`, and a
 * caller that wanted to inspect the answer would have to narrow a union it never produces.
 * It must be called from an injection context, which both guards are.
 */
export async function requireSession(returnUrl: string): Promise<boolean | UrlTree> {
  const store = inject(SessionStore);
  const auth = inject(AuthService);
  const router = inject(Router);

  if (store.isAuthenticated()) return true;
  if (await firstValueFrom(auth.refresh())) return true;

  // The URL is carried so sign-in can return the user to where they were going, which is the
  // difference between a deep link that works and one that dumps them on a dashboard.
  return router.createUrlTree(['/auth/login'], { queryParams: { returnUrl } });
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
