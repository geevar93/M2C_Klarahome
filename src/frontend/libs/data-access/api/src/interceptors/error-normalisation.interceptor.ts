import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { ToastService } from '@klarahome/util';
import { catchError, throwError } from 'rxjs';

import { ApiHeaders } from '../runtime/api-config';
import { SILENT_ERRORS } from '../runtime/http-context';
import { ApiError, ClientErrorCodes, ProblemDetails } from '../runtime/problem-details';

/**
 * Turns every failure into one `ApiError` and, unless the caller opted out, shows it once.
 *
 * Without this, a component has to handle three unrelated shapes — an `HttpErrorResponse` with a
 * ProblemDetails body, an `HttpErrorResponse` with an HTML error page from a proxy, and a
 * `ProgressEvent` from a dropped connection. After it, there is exactly one, and it always
 * carries a `code` to switch on.
 *
 * **A 401 is passed through untouched.** The auth interceptor sits closer to the request and has
 * already had its chance to refresh the session; by the time a 401 reaches here the refresh has
 * failed, and the session — not this interceptor — decides what the user is told.
 */
export const errorNormalisationInterceptor: HttpInterceptorFn = (request, next) => {
  const toasts = inject(ToastService);

  return next(request).pipe(
    catchError((error: unknown) => {
      const problem = toProblemDetails(error, request.headers.get(ApiHeaders.correlationId));
      const apiError = new ApiError(problem, request.urlWithParams);

      const silent =
        request.context.get(SILENT_ERRORS) ||
        apiError.status === 401 ||
        apiError.code === ClientErrorCodes.aborted;
      if (!silent) {
        toasts.danger(userMessage(apiError), undefined, apiError.correlationId);
      }

      return throwError(() => apiError);
    }),
  );
};

function toProblemDetails(error: unknown, requestCorrelationId: string | null): ProblemDetails {
  if (!(error instanceof HttpErrorResponse)) {
    return { status: 0, code: ClientErrorCodes.unexpected, detail: String(error) };
  }

  // Status 0 means the answer never arrived: offline, DNS, CORS, or an aborted request.
  if (error.status === 0) {
    const aborted = error.error instanceof DOMException && error.error.name === 'AbortError';
    return {
      status: 0,
      code: aborted ? ClientErrorCodes.aborted : ClientErrorCodes.network,
      title: aborted ? 'Request cancelled' : 'No connection',
      detail: aborted
        ? 'The request was cancelled.'
        : 'We could not reach the server. Check your connection and try again.',
      correlationId: requestCorrelationId ?? undefined,
    };
  }

  const body = error.error;
  const correlationId =
    error.headers?.get(ApiHeaders.correlationId) ??
    (isRecord(body) && typeof body['correlationId'] === 'string' ? body['correlationId'] : undefined) ??
    requestCorrelationId ??
    undefined;

  // A ProblemDetails body — the normal case. Everything the server said is kept.
  if (isRecord(body) && (typeof body['code'] === 'string' || typeof body['title'] === 'string')) {
    return { ...(body as ProblemDetails), status: error.status, correlationId };
  }

  // Anything else: an HTML error page from a proxy, an empty body, a gateway timeout.
  return {
    status: error.status,
    code: ClientErrorCodes.unexpected,
    title: error.statusText || 'Request failed',
    detail: 'Something went wrong. Please try again.',
    correlationId,
  };
}

/**
 * What the user is shown.
 *
 * The server's `detail` is written for a person and is used when there is one. The fallbacks are
 * keyed on status, never on the code — a code the frontend does not recognise still deserves a
 * sentence, and a component that wants to say something better switches on `code` itself.
 */
function userMessage(error: ApiError): string {
  if (error.problem.detail) return error.problem.detail;
  switch (error.status) {
    case 401:
      return 'Please sign in to continue.';
    case 403:
      return 'You do not have permission to do that.';
    case 404:
      return 'We could not find that.';
    case 409:
      return 'That has changed since you loaded it. Refresh and try again.';
    case 410:
      return 'That session has expired. Please start again.';
    case 422:
      return 'Some details need correcting.';
    case 429:
      return 'Too many attempts. Please wait a moment and try again.';
    case 503:
      return 'That service is briefly unavailable. Please try again shortly.';
    default:
      return 'Something went wrong. Please try again.';
  }
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);
