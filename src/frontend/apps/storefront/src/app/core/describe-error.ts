import { isApiError } from '@klarahome/data-access-auth';

/**
 * The sentence to show a customer for a failed request.
 *
 * Every failure in this application arrives as an `ApiError` carrying a stable `code` and a message
 * the error-normalisation interceptor has already made presentable (Step 22). So the API's own
 * words are used whenever there are any — it knows things a page does not, such as *which* of a
 * basket's four problems stopped the checkout — and the caller's fallback covers a transport failure
 * that never reached an endpoint.
 *
 * A page that wants to say something specific about a particular code checks the code itself and
 * calls this only for the rest; `CartActions.describeFailure` is the pattern.
 */
export function describeError(error: unknown, fallback: string): string {
  if (isApiError(error) && error.message) return error.message;
  return fallback;
}
