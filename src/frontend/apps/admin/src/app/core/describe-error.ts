import { ApiError, isApiError } from '@klarahome/data-access-auth';

/**
 * The sentence to show for a failed request.
 *
 * The twin of the storefront's `describeError`, and deliberately a second copy rather than a
 * shared helper: it depends on `isApiError` from `data-access`, and `@klarahome/util` — the only
 * library both apps could put it in — may not import `data-access`. The two copies share their
 * shape and differ only in wording (a back office is not "the shop"); keep them in step.
 *
 * Three things decide what is shown, in this order:
 *
 *  1. **A code this function refuses to speak for.** Every `AUTH_*` and `IDENTITY_USER_NOT_FOUND`
 *     code lands here, and the caller's own fallback is used instead of the server's words — the
 *     API's lock-out and unknown-user wording names the account's state, and saying so on the sign-in
 *     page is an account-enumeration oracle (docs/07-security-compliance.md). The login page's
 *     fallback is written to give nothing away; this is what makes that promise hold.
 *  2. **A generic category the whole app meets the same way**, matched on the HTTP status rather
 *     than the specific code — a caller raises `NotFound`/`Conflict` with a code of its own choosing,
 *     so the status is what is common to all of them. Validation, not-found, conflict, forbidden,
 *     rate-limited and network each get one plain sentence here.
 *  3. **The API's own words**, for the specific business failures neither of the above catches —
 *     the server knows things a screen does not, such as *which* of a bulk action's forty rows was
 *     refused, and why — and the caller's fallback covers a transport failure that never reached
 *     an endpoint.
 */
export function describeError(error: unknown, fallback: string): string {
  if (!isApiError(error)) return fallback;
  if (isAuthCode(error.code)) return fallback;
  return categoryMessage(error) ?? (error.message || fallback);
}

/**
 * Codes that must never reach the screen as the server wrote them, wherever `describeError` is
 * called from. Whichever half of a sign-in was wrong, a code that expired, an account that is
 * locked — all of it is said the same anti-enumeration way by the caller instead.
 */
function isAuthCode(code: string): boolean {
  return code.startsWith('AUTH_') || code === 'IDENTITY_USER_NOT_FOUND';
}

/** One plain sentence per HTTP status category, for the codes the caller does not name itself. */
const CATEGORY_MESSAGES: Readonly<Record<number, string>> = {
  400: 'Some of what was entered is not valid. Check the highlighted fields and try again.',
  422: 'Some of what was entered is not valid. Check the highlighted fields and try again.',
  404: 'That could not be found. It may have been removed since this screen was opened.',
  409: 'That could not be completed because something changed underneath it. Refresh and try again.',
  403: 'Your account does not have permission to do that.',
  429: 'Too many requests in a short time. Wait a moment and try again.',
};

function categoryMessage(error: ApiError): string | null {
  if (error.status === 0 || error.status === 503) {
    return 'The back office could not be reached. Check your connection and try again.';
  }
  // A 400/422 that names no field is a business rule the server has put into words ("a movement
  // of zero is not a movement"); those words are the message, not the generic sentence.
  if ((error.status === 400 || error.status === 422) && !fieldErrors(error)) return null;
  return CATEGORY_MESSAGES[error.status] ?? null;
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
