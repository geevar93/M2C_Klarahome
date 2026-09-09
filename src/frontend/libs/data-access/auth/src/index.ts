export * from './lib/auth.guards';
export * from './lib/auth.interceptor';
export * from './lib/auth.service';
export * from './lib/error-handling.providers';
export * from './lib/has-permission.directive';
export * from './lib/http.providers';
export * from './lib/session.store';

/**
 * The failure vocabulary, re-exported for the apps.
 *
 * Every failure in this application arrives as an `ApiError` carrying a stable `code`, and a page
 * that wants to say something specific about `CART_ITEM_OUT_OF_STOCK` has to be able to name the
 * type. It is re-exported here because this library is already where an app gets its HTTP stack
 * and its error handler, and because the boundary that stops an app importing the generated client
 * is about the *transport*, not about the shape of a failure.
 */
export { ApiError, ClientErrorCodes, isApiError } from '@klarahome/data-access-api';
export type {
  AuthenticatedUserResponse,
  OtpRequestedResponse,
  ProblemDetails,
  SignInResponse,
  TwoFactorChallengeResponse,
  TwoFactorSetupResponse,
} from '@klarahome/data-access-api';
