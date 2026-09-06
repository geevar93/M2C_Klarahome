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

/** Query string for `adminAuditLogsGet`. */
export interface AdminAuditLogsGetQuery {
  EntityType?: string;
  EntityId?: string;
  ActorId?: string;
  Action?: string;
  From?: string;
  To?: string;
  Cursor?: string;
  Size?: number;
}

/** `Platform` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class PlatformApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Searches the immutable audit trail, newest first.
   * `GET /api/v1/admin/audit-logs`
   */
  adminAuditLogsGet(query?: AdminAuditLogsGetQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfAuditLogResponse> {
    return this.http.request<Models.PagedResultOfAuditLogResponse>('GET', `${this.baseUrl}/api/v1/admin/audit-logs`, undefined, query, options);
  }

  /**
   * Lists every declared feature flag and its current configuration.
   * `GET /api/v1/admin/feature-flags`
   */
  adminFeatureFlagsGet(options?: ApiRequestOptions): Observable<Models.FeatureFlagResponse[]> {
    return this.http.request<Models.FeatureFlagResponse[]>('GET', `${this.baseUrl}/api/v1/admin/feature-flags`, undefined, undefined, options);
  }

  /**
   * Turns one feature flag on or off and sets who it reaches.
   * `PUT /api/v1/admin/feature-flags/{key}`
   */
  adminFeatureFlagsPut(key: string, body: Models.UpdateFeatureFlagRequest, options?: ApiRequestOptions): Observable<Models.FeatureFlagResponse> {
    return this.http.request<Models.FeatureFlagResponse>('PUT', `${this.baseUrl}/api/v1/admin/feature-flags/${encodeURIComponent(String(key))}`, body, undefined, options);
  }

  /**
   * Returns every store settings section with its current value.
   * `GET /api/v1/admin/settings`
   */
  adminSettingsGet(options?: ApiRequestOptions): Observable<Models.StoreSettingsResponse> {
    return this.http.request<Models.StoreSettingsResponse>('GET', `${this.baseUrl}/api/v1/admin/settings`, undefined, undefined, options);
  }

  /**
   * Replaces one settings section. The section is edited as a whole.
   * `PUT /api/v1/admin/settings/{key}`
   */
  adminSettingsPut(key: string, body: unknown, options?: ApiRequestOptions): Observable<Models.SettingsSectionResponse> {
    return this.http.request<Models.SettingsSectionResponse>('PUT', `${this.baseUrl}/api/v1/admin/settings/${encodeURIComponent(String(key))}`, body, undefined, options);
  }

  /**
   * Public store configuration: branding, legal and support details, commerce rules and the feature flags the storefront switches on.
   * `GET /api/v1/store/config`
   */
  storeConfigGet(options?: ApiRequestOptions): Observable<Models.StoreConfigResponse> {
    return this.http.request<Models.StoreConfigResponse>('GET', `${this.baseUrl}/api/v1/store/config`, undefined, undefined, options);
  }

  /**
   * City, district and state for a PIN code, for address autofill. Serviceability is added by the Shipping module.
   * `GET /api/v1/store/pincodes/{pincode}`
   */
  storePincodeGet(pincode: string, options?: ApiRequestOptions): Observable<Models.PincodeResponse> {
    return this.http.request<Models.PincodeResponse>('GET', `${this.baseUrl}/api/v1/store/pincodes/${encodeURIComponent(String(pincode))}`, undefined, undefined, options);
  }

  /**
   * The Indian states and union territories, with their GST state codes.
   * `GET /api/v1/store/states`
   */
  storeStatesGet(options?: ApiRequestOptions): Observable<Models.StateResponse[]> {
    return this.http.request<Models.StateResponse[]>('GET', `${this.baseUrl}/api/v1/store/states`, undefined, undefined, options);
  }
}
