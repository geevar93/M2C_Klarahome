import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Observable, retry, throwError, timer } from 'rxjs';

import { ApiHeaders } from '../runtime/api-config';
import { RETRY_COUNT } from '../runtime/http-context';

/** Requests the server treats as repeatable. Nothing else is retried without an explicit key. */
const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);

/** Failures worth trying again. A 4xx will fail identically every time; a 502 may not. */
const TRANSIENT_STATUSES = new Set([0, 408, 429, 502, 503, 504]);

const DEFAULT_ATTEMPTS = 2;
const BASE_DELAY_MS = 300;
const MAX_DELAY_MS = 4000;

/**
 * Retries only what is safe to repeat.
 *
 * The rule is the HTTP method, not the URL: a GET may be repeated because the server promises it
 * changes nothing, and a POST may not — **unless it carries an `Idempotency-Key`, which is the
 * server's promise that the same key twice is the same order once**
 * (docs/04-api-specification.md §1). That is exactly what the key is for, and it is why a dropped
 * connection during checkout does not cost the customer a second order.
 *
 * The backoff is exponential with jitter, and a `Retry-After` from the server always wins — a 429
 * or a 503 has told us when to come back, and guessing sooner is how a struggling API is turned
 * into a failing one.
 */
export const retryInterceptor: HttpInterceptorFn = (request, next) => {
  const repeatable = SAFE_METHODS.has(request.method) || request.headers.has(ApiHeaders.idempotencyKey);
  const attempts = request.context.get(RETRY_COUNT) ?? (repeatable ? DEFAULT_ATTEMPTS : 0);
  if (attempts <= 0 || !repeatable) return next(request);

  return next(request).pipe(
    retry({
      count: attempts,
      delay: (error: unknown, attempt: number): Observable<number> => {
        if (!(error instanceof HttpErrorResponse) || !TRANSIENT_STATUSES.has(error.status)) {
          return throwError(() => error);
        }
        return timer(delayFor(error, attempt));
      },
    }),
  );
};

function delayFor(error: HttpErrorResponse, attempt: number): number {
  const retryAfter = parseRetryAfter(error.headers?.get('Retry-After'));
  if (retryAfter !== undefined) return Math.min(retryAfter, MAX_DELAY_MS);

  // Full jitter. Without it, every client that failed on the same outage retries in the same
  // millisecond and the recovering server is knocked over by its own clients.
  const ceiling = Math.min(BASE_DELAY_MS * 2 ** (attempt - 1), MAX_DELAY_MS);
  return Math.round(ceiling * (0.5 + Math.random() * 0.5));
}

/** `Retry-After` is either a delay in seconds or an HTTP date. Both are accepted. */
function parseRetryAfter(value: string | null | undefined): number | undefined {
  if (!value) return undefined;
  const seconds = Number(value);
  if (Number.isFinite(seconds)) return Math.max(0, seconds * 1000);
  const date = Date.parse(value);
  return Number.isNaN(date) ? undefined : Math.max(0, date - Date.now());
}
