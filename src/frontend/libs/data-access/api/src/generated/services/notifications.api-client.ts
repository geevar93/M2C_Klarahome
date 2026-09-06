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

/** Query string for `adminNotificationsList`. */
export interface AdminNotificationsListQuery {
  Status?: string;
  Channel?: string;
  EventKey?: string;
  From?: string;
  To?: string;
  Cursor?: string;
  Size?: number;
}

/** Query string for `adminNotificationTemplatesList`. */
export interface AdminNotificationTemplatesListQuery {
  channel?: string;
  eventKey?: string;
}

/** `Notifications` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class NotificationsApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Returns one delivery-log entry.
   * `GET /api/v1/admin/notifications/{id}`
   */
  adminNotificationGet(id: string, options?: ApiRequestOptions): Observable<Models.NotificationLogResponse> {
    return this.http.request<Models.NotificationLogResponse>('GET', `${this.baseUrl}/api/v1/admin/notifications/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Puts a failed or suppressed message back in the queue.
   * `POST /api/v1/admin/notifications/{id}/retry`
   */
  adminNotificationRetry(id: string, options?: ApiRequestOptions): Observable<Models.NotificationLogResponse> {
    return this.http.request<Models.NotificationLogResponse>('POST', `${this.baseUrl}/api/v1/admin/notifications/${encodeURIComponent(String(id))}/retry`, undefined, undefined, options);
  }

  /**
   * Searches the delivery log, newest first. Recipients are masked.
   * `GET /api/v1/admin/notifications`
   */
  adminNotificationsList(query?: AdminNotificationsListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfNotificationLogResponse> {
    return this.http.request<Models.PagedResultOfNotificationLogResponse>('GET', `${this.baseUrl}/api/v1/admin/notifications`, undefined, query, options);
  }

  /**
   * Returns one template.
   * `GET /api/v1/admin/notification-templates/{id}`
   */
  adminNotificationTemplateGet(id: string, options?: ApiRequestOptions): Observable<Models.TemplateResponse> {
    return this.http.request<Models.TemplateResponse>('GET', `${this.baseUrl}/api/v1/admin/notification-templates/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Rewrites one template. An active SMS template must carry its DLT id.
   * `PUT /api/v1/admin/notification-templates/{id}`
   */
  adminNotificationTemplatePut(id: string, body: Models.UpdateTemplateRequest, options?: ApiRequestOptions): Observable<Models.TemplateResponse> {
    return this.http.request<Models.TemplateResponse>('PUT', `${this.baseUrl}/api/v1/admin/notification-templates/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Lists every notification template, with the placeholders each expects.
   * `GET /api/v1/admin/notification-templates`
   */
  adminNotificationTemplatesList(query?: AdminNotificationTemplatesListQuery, options?: ApiRequestOptions): Observable<Models.TemplateResponse[]> {
    return this.http.request<Models.TemplateResponse[]>('GET', `${this.baseUrl}/api/v1/admin/notification-templates`, undefined, query, options);
  }

  /**
   * Sends the built-in test message, to prove a channel works end to end.
   * `POST /api/v1/admin/notifications/test`
   */
  adminNotificationTest(body: Models.SendTestRequest, options?: ApiRequestOptions): Observable<Models.QueuedNotification[]> {
    return this.http.request<Models.QueuedNotification[]>('POST', `${this.baseUrl}/api/v1/admin/notifications/test`, body, undefined, options);
  }

  /**
   * Returns what this account has chosen to receive, per category and channel.
   * `GET /api/v1/store/me/notification-preferences`
   */
  storeNotificationPreferencesGet(options?: ApiRequestOptions): Observable<Models.PreferencesResponse> {
    return this.http.request<Models.PreferencesResponse>('GET', `${this.baseUrl}/api/v1/store/me/notification-preferences`, undefined, undefined, options);
  }

  /**
   * Changes one category's choices and returns the whole set.
   * `PUT /api/v1/store/me/notification-preferences`
   */
  storeNotificationPreferencesPut(body: Models.UpdatePreferenceRequest, options?: ApiRequestOptions): Observable<Models.PreferencesResponse> {
    return this.http.request<Models.PreferencesResponse>('PUT', `${this.baseUrl}/api/v1/store/me/notification-preferences`, body, undefined, options);
  }
}
