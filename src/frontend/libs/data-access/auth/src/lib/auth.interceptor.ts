import { HttpErrorResponse, HttpEvent, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { ApiHeaders, SKIP_AUTH } from '@klarahome/data-access-api';
import { Observable, catchError, switchMap, throwError } from 'rxjs';

import { AuthService } from './auth.service';
import { SessionStore } from './session.store';

/**
 * Attaches the bearer token, and recovers a session that expired mid-request.
 *
 * Two things happen here, and the order matters:
 *
 *  1. If the token is within its expiry margin, the request **waits** for a refresh rather than
 *     going out doomed. One less round trip, and no user-visible flicker.
 *  2. If a request comes back 401 anyway — a token revoked server-side, a clock out of step — one
 *     refresh is attempted and the request is replayed **once**. A second 401 is final: it is
 *     passed on with the session cleared, and the app's route guards take the user to sign-in.
 *
 * The retry is capped at one on purpose. A refresh that succeeds and a request that still 401s is
 * not a token problem; retrying it again is an infinite loop with a server on the other end.
 *
 * Requests that carry `skipAuth` — sign-in, refresh itself, the public catalogue — pass straight
 * through. Refreshing on behalf of the refresh call would recurse.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (request.context.get(SKIP_AUTH)) return next(request);

  const store = inject(SessionStore);
  const auth = inject(AuthService);

  const send = (): Observable<HttpEvent<unknown>> => next(withToken(request, store.accessToken()));

  const attempt =
    store.isAuthenticated() && store.isExpiring() ? auth.refresh().pipe(switchMap(send)) : send();

  return attempt.pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !store.isAuthenticated()) {
        return throwError(() => error);
      }
      return auth.refresh().pipe(switchMap((refreshed) => (refreshed ? send() : throwError(() => error))));
    }),
  );
};

function withToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  if (!token) return request;
  return request.clone({ setHeaders: { [ApiHeaders.authorization]: `Bearer ${token}` } });
}
