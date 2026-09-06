/**
 * DO NOT EDIT. Generated from the API's OpenAPI document by tools/generate-api-client.mjs.
 *
 * Regenerate with:  pwsh tools/generate-api-client.ps1
 * CI fails if this file differs from what the current API produces.
 */
/* eslint-disable */

import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiRequestOptions, ApiTransport } from '../../runtime';
import type * as Models from '../models';

/** Query string for `adminMeSessionsRevokeAll`. */
export interface AdminMeSessionsRevokeAllQuery {
  exceptCurrent?: boolean;
}

/** Query string for `adminUsersGet`. */
export interface AdminUsersGetQuery {
  Search?: string;
  UserType?: Models.UserType;
  Cursor?: string;
  Size?: number;
}

/** Query string for `storeAuthExternalCallback`. */
export interface StoreAuthExternalCallbackQuery {
  code?: string;
  state?: string;
}

/** Query string for `storeAuthExternalStart`. */
export interface StoreAuthExternalStartQuery {
  returnUrl?: string;
}

/** Query string for `storeMeSessionsRevokeAll`. */
export interface StoreMeSessionsRevokeAllQuery {
  exceptCurrent?: boolean;
}

/** `Identity` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class IdentityApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Signs in with an email address and a password. May answer with a two-factor challenge.
   * `POST /api/v1/admin/auth/login`
   */
  adminAuthLogin(body: Models.LoginBody, options?: ApiRequestOptions): Observable<Models.SignInResponse> {
    return this.http.request<Models.SignInResponse>('POST', `${this.baseUrl}/api/v1/admin/auth/login`, body, undefined, options);
  }

  /**
   * Ends this session and clears the refresh cookie.
   * `POST /api/v1/admin/auth/logout`
   */
  adminAuthLogout(options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/admin/auth/logout`, undefined, undefined, options);
  }

  /**
   * Replaces a password and issues the session. Not gated by a flag: it is the route out of an administrator-issued temporary password, which exists precisely because email delivery is off.
   * `POST /api/v1/admin/auth/password/change`
   */
  adminAuthPasswordChange(body: Models.ChangePasswordBody, options?: ApiRequestOptions): Observable<Models.SignInResponse> {
    return this.http.request<Models.SignInResponse>('POST', `${this.baseUrl}/api/v1/admin/auth/password/change`, body, undefined, options);
  }

  /**
   * Sends a password-reset link. Always answers 204, whether or not the address is known.
   * `POST /api/v1/admin/auth/password/forgot`
   */
  adminAuthPasswordForgot(body: Models.ForgotPasswordBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/admin/auth/password/forgot`, body, undefined, options);
  }

  /**
   * Sets a new password from a reset link, and signs every device out.
   * `POST /api/v1/admin/auth/password/reset`
   */
  adminAuthPasswordReset(body: Models.ResetPasswordBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/admin/auth/password/reset`, body, undefined, options);
  }

  /**
   * Exchanges the refresh cookie for a new access token, rotating the refresh token.
   * `POST /api/v1/admin/auth/refresh`
   */
  adminAuthRefresh(options?: ApiRequestOptions): Observable<Models.SignInResponse> {
    return this.http.request<Models.SignInResponse>('POST', `${this.baseUrl}/api/v1/admin/auth/refresh`, undefined, undefined, options);
  }

  /**
   * Returns a new authenticator secret for a sign-in that stopped at enrolment.
   * `POST /api/v1/admin/auth/2fa/enrol`
   */
  adminAuthTwoFactorEnrol(body: Models.TwoFactorEnrolBody, options?: ApiRequestOptions): Observable<Models.TwoFactorSetupResponse> {
    return this.http.request<Models.TwoFactorSetupResponse>('POST', `${this.baseUrl}/api/v1/admin/auth/2fa/enrol`, body, undefined, options);
  }

  /**
   * Answers the second factor and completes the sign-in, enrolling a staged secret if this is the first code it has produced.
   * `POST /api/v1/admin/auth/2fa/verify`
   */
  adminAuthTwoFactorVerify(body: Models.TwoFactorVerifyBody, options?: ApiRequestOptions): Observable<Models.SignInResponse> {
    return this.http.request<Models.SignInResponse>('POST', `${this.baseUrl}/api/v1/admin/auth/2fa/verify`, body, undefined, options);
  }

  /**
   * The caller's account: identity, roles, permissions and shopper profile.
   * `GET /api/v1/admin/me`
   */
  adminMeGet(options?: ApiRequestOptions): Observable<Models.MeResponse> {
    return this.http.request<Models.MeResponse>('GET', `${this.baseUrl}/api/v1/admin/me`, undefined, undefined, options);
  }

  /**
   * Updates the caller's own details and marketing consent.
   * `PATCH /api/v1/admin/me`
   */
  adminMePatch(body: Models.UpdateMeBody, options?: ApiRequestOptions): Observable<Models.MeResponse> {
    return this.http.request<Models.MeResponse>('PATCH', `${this.baseUrl}/api/v1/admin/me`, body, undefined, options);
  }

  /**
   * Signs one device out.
   * `DELETE /api/v1/admin/me/sessions/{id}`
   */
  adminMeSessionRevoke(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/me/sessions/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * The caller's signed-in devices, with the current one flagged.
   * `GET /api/v1/admin/me/sessions`
   */
  adminMeSessionsGet(options?: ApiRequestOptions): Observable<Models.SessionResponse[]> {
    return this.http.request<Models.SessionResponse[]>('GET', `${this.baseUrl}/api/v1/admin/me/sessions`, undefined, undefined, options);
  }

  /**
   * Signs every device out. Keeps the current session unless exceptCurrent is false.
   * `DELETE /api/v1/admin/me/sessions`
   */
  adminMeSessionsRevokeAll(query?: AdminMeSessionsRevokeAllQuery, options?: ApiRequestOptions): Observable<Models.RevokedSessionsResponse> {
    return this.http.request<Models.RevokedSessionsResponse>('DELETE', `${this.baseUrl}/api/v1/admin/me/sessions`, undefined, query, options);
  }

  /**
   * Removes the second factor. Refused for roles that require one.
   * `POST /api/v1/admin/me/2fa/disable`
   */
  adminMeTwoFactorDisable(body: Models.DisableTwoFactorBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/admin/me/2fa/disable`, body, undefined, options);
  }

  /**
   * Turns on the staged second factor, once it has produced a valid code.
   * `POST /api/v1/admin/me/2fa/enable`
   */
  adminMeTwoFactorEnable(body: Models.EnableTwoFactorBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/admin/me/2fa/enable`, body, undefined, options);
  }

  /**
   * Returns a new authenticator secret, staged but not yet in force.
   * `POST /api/v1/admin/me/2fa/setup`
   */
  adminMeTwoFactorSetup(options?: ApiRequestOptions): Observable<Models.TwoFactorSetupResponse> {
    return this.http.request<Models.TwoFactorSetupResponse>('POST', `${this.baseUrl}/api/v1/admin/me/2fa/setup`, undefined, undefined, options);
  }

  /**
   * Marks an email address or mobile number verified.
   * `POST /api/v1/admin/me/verify/confirm`
   */
  adminMeVerifyConfirm(body: Models.VerificationConfirmBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/admin/me/verify/confirm`, body, undefined, options);
  }

  /**
   * Sends a code or link proving the caller owns their email address or mobile number.
   * `POST /api/v1/admin/me/verify/request`
   */
  adminMeVerifyRequest(body: Models.VerificationRequestBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/admin/me/verify/request`, body, undefined, options);
  }

  /**
   * The permission catalogue, grouped as the role editor renders it.
   * `GET /api/v1/admin/permissions`
   */
  adminPermissionsGet(options?: ApiRequestOptions): Observable<Models.PermissionGroupResponse[]> {
    return this.http.request<Models.PermissionGroupResponse[]>('GET', `${this.baseUrl}/api/v1/admin/permissions`, undefined, undefined, options);
  }

  /**
   * Defines a role of this deployment's own.
   * `POST /api/v1/admin/roles`
   */
  adminRoleCreate(body: Models.CreateRoleBody, options?: ApiRequestOptions): Observable<Models.RoleResponse> {
    return this.http.request<Models.RoleResponse>('POST', `${this.baseUrl}/api/v1/admin/roles`, body, undefined, options);
  }

  /**
   * Changes what a role grants. Refused for the roles the platform itself defines.
   * `PUT /api/v1/admin/roles/{id}`
   */
  adminRolePut(id: string, body: Models.UpdateRoleBody, options?: ApiRequestOptions): Observable<Models.RoleResponse> {
    return this.http.request<Models.RoleResponse>('PUT', `${this.baseUrl}/api/v1/admin/roles/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Lists the roles and what each one grants.
   * `GET /api/v1/admin/roles`
   */
  adminRolesGet(options?: ApiRequestOptions): Observable<Models.RoleResponse[]> {
    return this.http.request<Models.RoleResponse[]>('GET', `${this.baseUrl}/api/v1/admin/roles`, undefined, undefined, options);
  }

  /**
   * Creates a staff or vendor account and emails them a link to set their password. No password is ever chosen on their behalf.
   * `POST /api/v1/admin/users`
   */
  adminUserCreate(body: Models.CreateUserBody, options?: ApiRequestOptions): Observable<Models.AdminUserResponse> {
    return this.http.request<Models.AdminUserResponse>('POST', `${this.baseUrl}/api/v1/admin/users`, body, undefined, options);
  }

  /**
   * Reads one account. Answers 404 for an account outside the caller's scope.
   * `GET /api/v1/admin/users/{id}`
   */
  adminUserGet(id: string, options?: ApiRequestOptions): Observable<Models.AdminUserResponse> {
    return this.http.request<Models.AdminUserResponse>('GET', `${this.baseUrl}/api/v1/admin/users/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Issues a temporary password for another account, to be conveyed out of band. The account cannot be used until it is replaced, and every session is ended. This exists because email delivery is off (ADR-014); it is a weaker control than a reset link and is withdrawn when email returns.
   * `PUT /api/v1/admin/users/{id}/password`
   */
  adminUserPasswordPut(id: string, body: Models.SetTemporaryPasswordBody, options?: ApiRequestOptions): Observable<Models.AdminUserResponse> {
    return this.http.request<Models.AdminUserResponse>('PUT', `${this.baseUrl}/api/v1/admin/users/${encodeURIComponent(String(id))}/password`, body, undefined, options);
  }

  /**
   * Replaces an account's roles and signs it out, so the change takes effect at once.
   * `PUT /api/v1/admin/users/{id}/roles`
   */
  adminUserRolesPut(id: string, body: Models.SetUserRolesBody, options?: ApiRequestOptions): Observable<Models.AdminUserResponse> {
    return this.http.request<Models.AdminUserResponse>('PUT', `${this.baseUrl}/api/v1/admin/users/${encodeURIComponent(String(id))}/roles`, body, undefined, options);
  }

  /**
   * Lists user accounts, keyset-paginated. A vendor caller sees only their own organisation.
   * `GET /api/v1/admin/users`
   */
  adminUsersGet(query?: AdminUsersGetQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfAdminUserResponse> {
    return this.http.request<Models.PagedResultOfAdminUserResponse>('GET', `${this.baseUrl}/api/v1/admin/users`, undefined, query, options);
  }

  /**
   * Suspends, reinstates or unlocks an account. Suspending it ends its sessions.
   * `PUT /api/v1/admin/users/{id}/status`
   */
  adminUserStatusPut(id: string, body: Models.SetUserStatusBody, options?: ApiRequestOptions): Observable<Models.AdminUserResponse> {
    return this.http.request<Models.AdminUserResponse>('PUT', `${this.baseUrl}/api/v1/admin/users/${encodeURIComponent(String(id))}/status`, body, undefined, options);
  }

  /**
   * Completes the sign-in the provider redirected back from, and returns the browser to the storefront with a refresh cookie set.
   * `GET /api/v1/store/auth/external/{provider}/callback`
   */
  storeAuthExternalCallback(provider: string, query?: StoreAuthExternalCallbackQuery, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('GET', `${this.baseUrl}/api/v1/store/auth/external/${encodeURIComponent(String(provider))}/callback`, undefined, query, options);
  }

  /**
   * The identity providers this store offers, for the sign-in page's buttons. A provider that is switched on but not configured is not listed.
   * `GET /api/v1/store/auth/external/providers`
   */
  storeAuthExternalProviders(options?: ApiRequestOptions): Observable<Models.ExternalProviderResponse[]> {
    return this.http.request<Models.ExternalProviderResponse[]>('GET', `${this.baseUrl}/api/v1/store/auth/external/providers`, undefined, undefined, options);
  }

  /**
   * Redirects the browser to the provider, carrying the anti-forgery state and the PKCE challenge.
   * `GET /api/v1/store/auth/external/{provider}/start`
   */
  storeAuthExternalStart(provider: string, query?: StoreAuthExternalStartQuery, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('GET', `${this.baseUrl}/api/v1/store/auth/external/${encodeURIComponent(String(provider))}/start`, undefined, query, options);
  }

  /**
   * Signs in with an email address and a password. May answer with a two-factor challenge.
   * `POST /api/v1/store/auth/login`
   */
  storeAuthLogin(body: Models.LoginBody, options?: ApiRequestOptions): Observable<Models.SignInResponse> {
    return this.http.request<Models.SignInResponse>('POST', `${this.baseUrl}/api/v1/store/auth/login`, body, undefined, options);
  }

  /**
   * Ends this session and clears the refresh cookie.
   * `POST /api/v1/store/auth/logout`
   */
  storeAuthLogout(options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/store/auth/logout`, undefined, undefined, options);
  }

  /**
   * Sends a one-time code to a mobile number. The response is the same whether or not the number is registered.
   * `POST /api/v1/store/auth/otp/request`
   */
  storeAuthOtpRequest(body: Models.OtpRequestBody, options?: ApiRequestOptions): Observable<Models.OtpRequestedResponse> {
    return this.http.request<Models.OtpRequestedResponse>('POST', `${this.baseUrl}/api/v1/store/auth/otp/request`, body, undefined, options);
  }

  /**
   * Signs in with a one-time code, registering the customer if the number is new.
   * `POST /api/v1/store/auth/otp/verify`
   */
  storeAuthOtpVerify(body: Models.OtpVerifyBody, options?: ApiRequestOptions): Observable<Models.SignInResponse> {
    return this.http.request<Models.SignInResponse>('POST', `${this.baseUrl}/api/v1/store/auth/otp/verify`, body, undefined, options);
  }

  /**
   * Replaces a password and issues the session. Not gated by a flag: it is the route out of an administrator-issued temporary password, which exists precisely because email delivery is off.
   * `POST /api/v1/store/auth/password/change`
   */
  storeAuthPasswordChange(body: Models.ChangePasswordBody, options?: ApiRequestOptions): Observable<Models.SignInResponse> {
    return this.http.request<Models.SignInResponse>('POST', `${this.baseUrl}/api/v1/store/auth/password/change`, body, undefined, options);
  }

  /**
   * Sends a password-reset link. Always answers 204, whether or not the address is known.
   * `POST /api/v1/store/auth/password/forgot`
   */
  storeAuthPasswordForgot(body: Models.ForgotPasswordBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/store/auth/password/forgot`, body, undefined, options);
  }

  /**
   * Sets a new password from a reset link, and signs every device out.
   * `POST /api/v1/store/auth/password/reset`
   */
  storeAuthPasswordReset(body: Models.ResetPasswordBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/store/auth/password/reset`, body, undefined, options);
  }

  /**
   * Exchanges the refresh cookie for a new access token, rotating the refresh token.
   * `POST /api/v1/store/auth/refresh`
   */
  storeAuthRefresh(options?: ApiRequestOptions): Observable<Models.SignInResponse> {
    return this.http.request<Models.SignInResponse>('POST', `${this.baseUrl}/api/v1/store/auth/refresh`, undefined, undefined, options);
  }

  /**
   * Registers a shopper with an email address and a password.
   * `POST /api/v1/store/auth/register`
   */
  storeAuthRegister(body: Models.RegisterBody, options?: ApiRequestOptions): Observable<Models.SignInResponse> {
    return this.http.request<Models.SignInResponse>('POST', `${this.baseUrl}/api/v1/store/auth/register`, body, undefined, options);
  }

  /**
   * Returns a new authenticator secret for a sign-in that stopped at enrolment.
   * `POST /api/v1/store/auth/2fa/enrol`
   */
  storeAuthTwoFactorEnrol(body: Models.TwoFactorEnrolBody, options?: ApiRequestOptions): Observable<Models.TwoFactorSetupResponse> {
    return this.http.request<Models.TwoFactorSetupResponse>('POST', `${this.baseUrl}/api/v1/store/auth/2fa/enrol`, body, undefined, options);
  }

  /**
   * Answers the second factor and completes the sign-in, enrolling a staged secret if this is the first code it has produced.
   * `POST /api/v1/store/auth/2fa/verify`
   */
  storeAuthTwoFactorVerify(body: Models.TwoFactorVerifyBody, options?: ApiRequestOptions): Observable<Models.SignInResponse> {
    return this.http.request<Models.SignInResponse>('POST', `${this.baseUrl}/api/v1/store/auth/2fa/verify`, body, undefined, options);
  }

  /**
   * Saves an address. The first one saved becomes both defaults.
   * `POST /api/v1/store/me/addresses`
   */
  storeMeAddressCreate(body: Models.AddressInput, options?: ApiRequestOptions): Observable<Models.AddressResponse> {
    return this.http.request<Models.AddressResponse>('POST', `${this.baseUrl}/api/v1/store/me/addresses`, body, undefined, options);
  }

  /**
   * Removes an address. Orders already placed to it are unaffected.
   * `DELETE /api/v1/store/me/addresses/{id}`
   */
  storeMeAddressDelete(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/store/me/addresses/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * The caller's saved addresses, the default shipping one first.
   * `GET /api/v1/store/me/addresses`
   */
  storeMeAddressesGet(options?: ApiRequestOptions): Observable<Models.AddressResponse[]> {
    return this.http.request<Models.AddressResponse[]>('GET', `${this.baseUrl}/api/v1/store/me/addresses`, undefined, undefined, options);
  }

  /**
   * Replaces one address. Addresses are edited whole, never patched.
   * `PUT /api/v1/store/me/addresses/{id}`
   */
  storeMeAddressUpdate(id: string, body: Models.AddressInput, options?: ApiRequestOptions): Observable<Models.AddressResponse> {
    return this.http.request<Models.AddressResponse>('PUT', `${this.baseUrl}/api/v1/store/me/addresses/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Unlinks a provider. Refused when it is the only way into the account.
   * `DELETE /api/v1/store/me/external-logins/{id}`
   */
  storeMeExternalLoginDelete(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/store/me/external-logins/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * The identity providers linked to the caller's account.
   * `GET /api/v1/store/me/external-logins`
   */
  storeMeExternalLoginsGet(options?: ApiRequestOptions): Observable<Models.ExternalLoginResponse[]> {
    return this.http.request<Models.ExternalLoginResponse[]>('GET', `${this.baseUrl}/api/v1/store/me/external-logins`, undefined, undefined, options);
  }

  /**
   * The caller's account: identity, roles, permissions and shopper profile.
   * `GET /api/v1/store/me`
   */
  storeMeGet(options?: ApiRequestOptions): Observable<Models.MeResponse> {
    return this.http.request<Models.MeResponse>('GET', `${this.baseUrl}/api/v1/store/me`, undefined, undefined, options);
  }

  /**
   * Updates the caller's own details and marketing consent.
   * `PATCH /api/v1/store/me`
   */
  storeMePatch(body: Models.UpdateMeBody, options?: ApiRequestOptions): Observable<Models.MeResponse> {
    return this.http.request<Models.MeResponse>('PATCH', `${this.baseUrl}/api/v1/store/me`, body, undefined, options);
  }

  /**
   * Signs one device out.
   * `DELETE /api/v1/store/me/sessions/{id}`
   */
  storeMeSessionRevoke(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/store/me/sessions/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * The caller's signed-in devices, with the current one flagged.
   * `GET /api/v1/store/me/sessions`
   */
  storeMeSessionsGet(options?: ApiRequestOptions): Observable<Models.SessionResponse[]> {
    return this.http.request<Models.SessionResponse[]>('GET', `${this.baseUrl}/api/v1/store/me/sessions`, undefined, undefined, options);
  }

  /**
   * Signs every device out. Keeps the current session unless exceptCurrent is false.
   * `DELETE /api/v1/store/me/sessions`
   */
  storeMeSessionsRevokeAll(query?: StoreMeSessionsRevokeAllQuery, options?: ApiRequestOptions): Observable<Models.RevokedSessionsResponse> {
    return this.http.request<Models.RevokedSessionsResponse>('DELETE', `${this.baseUrl}/api/v1/store/me/sessions`, undefined, query, options);
  }

  /**
   * Removes the second factor. Refused for roles that require one.
   * `POST /api/v1/store/me/2fa/disable`
   */
  storeMeTwoFactorDisable(body: Models.DisableTwoFactorBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/store/me/2fa/disable`, body, undefined, options);
  }

  /**
   * Turns on the staged second factor, once it has produced a valid code.
   * `POST /api/v1/store/me/2fa/enable`
   */
  storeMeTwoFactorEnable(body: Models.EnableTwoFactorBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/store/me/2fa/enable`, body, undefined, options);
  }

  /**
   * Returns a new authenticator secret, staged but not yet in force.
   * `POST /api/v1/store/me/2fa/setup`
   */
  storeMeTwoFactorSetup(options?: ApiRequestOptions): Observable<Models.TwoFactorSetupResponse> {
    return this.http.request<Models.TwoFactorSetupResponse>('POST', `${this.baseUrl}/api/v1/store/me/2fa/setup`, undefined, undefined, options);
  }

  /**
   * Marks an email address or mobile number verified.
   * `POST /api/v1/store/me/verify/confirm`
   */
  storeMeVerifyConfirm(body: Models.VerificationConfirmBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/store/me/verify/confirm`, body, undefined, options);
  }

  /**
   * Sends a code or link proving the caller owns their email address or mobile number.
   * `POST /api/v1/store/me/verify/request`
   */
  storeMeVerifyRequest(body: Models.VerificationRequestBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/store/me/verify/request`, body, undefined, options);
  }
}
