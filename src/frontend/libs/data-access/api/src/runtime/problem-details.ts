/**
 * The error shape the whole frontend switches on.
 *
 * The API answers failures as RFC 9457 ProblemDetails carrying a stable machine-readable `code`
 * (docs/04-api-specification.md §1.2). **The frontend switches on `code`, never on message text**
 * — the text is a translation away from changing, and the code is a contract.
 */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  /** The stable code. `CART_ITEM_OUT_OF_STOCK`, `PRICE_CHANGED`, `COD_NOT_SERVICEABLE`… */
  readonly code?: string;
  readonly detail?: string;
  readonly instance?: string;
  readonly correlationId?: string;
  /** Field-keyed validation messages, as `lines[2].quantity: ['Only 3 units are available.']`. */
  readonly errors?: Record<string, readonly string[]>;
}

/**
 * The codes the client itself produces, for failures that never reached the server. They are
 * spelled like the server's so a `switch` over `code` does not need two shapes.
 */
export const ClientErrorCodes = {
  /** The request never left, or no answer came back. */
  network: 'NETWORK_UNAVAILABLE',
  /** The request was aborted — a navigation, a cancelled typeahead. */
  aborted: 'REQUEST_ABORTED',
  /** A 5xx or an unparseable body. */
  unexpected: 'UNEXPECTED_ERROR',
} as const;

/**
 * Every failure that leaves the interceptor chain is one of these, so a caller never has to ask
 * whether it got an `HttpErrorResponse`, a `TypeError` from fetch, or an HTML error page.
 */
export class ApiError extends Error {
  /** The stable machine-readable code. Always present — see `ClientErrorCodes`. */
  readonly code: string;
  /** HTTP status, or 0 when the request never got an answer. */
  readonly status: number;
  readonly problem: ProblemDetails;
  readonly correlationId?: string;
  readonly url?: string;

  constructor(problem: ProblemDetails, url?: string) {
    super(problem.detail ?? problem.title ?? problem.code ?? 'Request failed');
    this.name = 'ApiError';
    this.code = problem.code ?? ClientErrorCodes.unexpected;
    this.status = problem.status ?? 0;
    this.problem = problem;
    this.correlationId = problem.correlationId;
    this.url = url;
  }

  /** Field-keyed validation messages, empty when the failure was not a validation failure. */
  get fieldErrors(): Record<string, readonly string[]> {
    return this.problem.errors ?? {};
  }

  /** True when the caller may reasonably offer a "try again" button. */
  get isRetryable(): boolean {
    return this.status === 0 || this.status === 429 || this.status === 503 || this.status >= 500;
  }
}

/** Narrows an unknown rejection to an `ApiError`, which is what the chain always produces. */
export function isApiError(value: unknown): value is ApiError {
  return value instanceof ApiError;
}
