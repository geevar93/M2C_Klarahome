import { ApiError, isApiError } from '@klarahome/data-access-auth';

/**
 * The sentence to show a customer for a failed request.
 *
 * Every failure in this application arrives as an `ApiError` carrying a stable `code` and a message
 * the error-normalisation interceptor has already made presentable (Step 22). Three things decide
 * what is shown, in this order:
 *
 *  1. **A code this function refuses to speak for.** Every `AUTH_*` and `IDENTITY_USER_NOT_FOUND`
 *     code lands here, and the caller's own fallback is used instead of the server's words —
 *     whichever half of a sign-in was wrong, saying so by name is an account-enumeration oracle
 *     (docs/07-security-compliance.md), and that risk does not go away because the wording happens
 *     to be accurate.
 *  2. **A generic category the whole app meets the same way**, matched on the HTTP status rather
 *     than the specific code — a caller raises `NotFound`/`Conflict` with a code of its own choosing
 *     (`IDENTITY_ADDRESS_NOT_FOUND`, `CART_ITEM_OUT_OF_STOCK`…), so the status is what is common to
 *     all of them. `validation`, `not_found`, `conflict`, `forbidden`, `rate_limited` and `network`
 *     each get one plain sentence here.
 *  3. **The API's own words**, for the specific business failures neither of the above catches —
 *     it knows things a page does not, such as *which* of a basket's four problems stopped the
 *     checkout — and the caller's fallback covers a transport failure that never reached an
 *     endpoint.
 *
 * A page that wants to say something more specific about a particular code checks the code itself
 * and calls this only for the rest; `CartActions.describeFailure` is the pattern.
 */
export function describeError(error: unknown, fallback: string): string {
  if (!isApiError(error)) return fallback;
  if (isAuthCode(error.code)) return fallback;
  return categoryMessage(error) ?? (error.message || fallback);
}

/**
 * Codes that must never reach the screen as the server wrote them, wherever `describeError` is
 * called from. Whichever of a login's two halves was wrong, an OTP that expired, an account that
 * is locked — all of it is deliberately said the same anti-enumeration way by the caller instead.
 */
function isAuthCode(code: string): boolean {
  return code.startsWith('AUTH_') || code === 'IDENTITY_USER_NOT_FOUND';
}

/** One friendly sentence per HTTP status category, for the codes the dictionary above does not name. */
const CATEGORY_MESSAGES: Readonly<Record<number, string>> = {
  400: 'Some of what you entered was not valid. Please check the highlighted fields and try again.',
  422: 'Some of what you entered was not valid. Please check the highlighted fields and try again.',
  404: 'We could not find that.',
  409: 'That could not be completed because something changed. Please refresh and try again.',
  403: 'You do not have permission to do that.',
  429: 'Too many attempts. Please wait a moment and try again.',
};

function categoryMessage(error: ApiError): string | null {
  if (error.status === 0 || error.status === 503) {
    return 'We could not reach the shop. Check your connection and try again.';
  }
  return CATEGORY_MESSAGES[error.status] ?? null;
}
