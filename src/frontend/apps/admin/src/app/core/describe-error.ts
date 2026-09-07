import { isApiError } from '@klarahome/data-access-auth';

/**
 * The sentence to show for a failed request.
 *
 * The twin of the storefront's `describeError`, and deliberately a second copy rather than a
 * shared helper: it depends on `isApiError` from `data-access`, and `@klarahome/util` — the only
 * library both apps could put it in — may not import `data-access`. Ten lines duplicated is a
 * better trade than a library that exists to hold ten lines, or a boundary bent to avoid it.
 *
 * The API's own words are used whenever there are any: the error-normalisation interceptor has
 * already made them presentable, and the server knows things a screen does not — *which* of a
 * bulk action's forty rows was refused, and why.
 */
export function describeError(error: unknown, fallback: string): string {
  if (isApiError(error) && error.message) return error.message;
  return fallback;
}

/**
 * The field errors a 422 carried, ready for `FormGroup.applyServerErrors`.
 *
 * Answers null for anything that is not a validation failure, so a caller can tell "the server
 * objected to these three fields" from "the request never arrived" — which want different
 * treatment on screen.
 */
export function fieldErrors(error: unknown): Readonly<Record<string, readonly string[]>> | null {
  if (!isApiError(error)) return null;
  const errors = error.problem?.errors;
  return errors && Object.keys(errors).length > 0 ? errors : null;
}
