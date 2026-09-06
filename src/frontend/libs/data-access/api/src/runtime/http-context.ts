import { HttpContext, HttpContextToken } from '@angular/common/http';

/**
 * Per-request policy, carried on the request itself rather than inferred from its URL.
 *
 * An interceptor that guesses — "retry anything that looks like a GET", "show the bar unless the
 * path contains /poll/" — is a rule nobody can find when it misfires. These tokens make each
 * decision a property of the call that made it, visible at the call site.
 */

/** Suppresses the global loading indicator for this request. Default: the bar is shown. */
export const SHOW_LOADING = new HttpContextToken<boolean>(() => true);

/**
 * How many times a failed request may be retried. Only ever applied to requests the server
 * treats as safe to repeat — see `retryInterceptor`. Default: 2 for a GET, 0 for everything else.
 */
export const RETRY_COUNT = new HttpContextToken<number | undefined>(() => undefined);

/** Skips the Authorization header and the silent-refresh handling. Used by the auth calls. */
export const SKIP_AUTH = new HttpContextToken<boolean>(() => false);

/**
 * Suppresses the automatic error toast. The caller has undertaken to show the failure itself —
 * a form mapping field errors, an optimistic cart rolling back with its own banner.
 */
export const SILENT_ERRORS = new HttpContextToken<boolean>(() => false);

/** Options that shape one call. Every generated client method accepts these. */
export interface ApiRequestOptions {
  /** Extra headers. `Idempotency-Key` has its own field; use that instead. */
  readonly headers?: Record<string, string>;
  /**
   * The idempotency key for a request that creates money or stock — an order, a payment, a
   * refund, a payout. Generate it once per user intent and reuse it across retries, which is the
   * whole point: the same key twice is the same order once.
   */
  readonly idempotencyKey?: string;
  /** Suppress the loading indicator for this call. */
  readonly showLoading?: boolean;
  /** Suppress the automatic error toast; the caller renders the failure itself. */
  readonly silentErrors?: boolean;
  /** Override the retry allowance. Ignored on methods the server does not treat as repeatable. */
  readonly retry?: number;
  /** Send no Authorization header. */
  readonly skipAuth?: boolean;
  /** Aborts the request when it fires. */
  readonly signal?: AbortSignal;
  /** An HttpContext to build on, when a caller already has one. */
  readonly context?: HttpContext;
}

/** Folds `ApiRequestOptions` into the `HttpContext` the interceptors read. */
export function toHttpContext(options?: ApiRequestOptions): HttpContext {
  const context = options?.context ?? new HttpContext();
  if (options?.showLoading !== undefined) context.set(SHOW_LOADING, options.showLoading);
  if (options?.silentErrors !== undefined) context.set(SILENT_ERRORS, options.silentErrors);
  if (options?.retry !== undefined) context.set(RETRY_COUNT, options.retry);
  if (options?.skipAuth !== undefined) context.set(SKIP_AUTH, options.skipAuth);
  return context;
}
