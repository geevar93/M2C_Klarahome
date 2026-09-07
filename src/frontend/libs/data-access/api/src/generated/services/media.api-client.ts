/**
 * DO NOT EDIT. Generated from the API's OpenAPI document by tools/generate-api-client.mjs.
 *
 * Regenerate with:  pwsh tools/generate-api-client.ps1
 * CI fails if this file differs from what the current API produces.
 */
/* eslint-disable */

import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiRequestOptions, ApiTransport, toFormData } from '../../runtime';
import type * as Models from '../models';

/** Query string for `adminMediaList`. */
export interface AdminMediaListQuery {
  visibility?: string;
  contentType?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminMediaUpload`. */
export interface AdminMediaUploadQuery {
  visibility?: string;
  ownerType?: string;
  ownerId?: string;
}

/** `Media` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class MediaApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Retires a file and removes the object behind it.
   * `DELETE /api/v1/admin/media/{id}`
   */
  adminMediaDelete(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/media/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Returns one file's registry entry.
   * `GET /api/v1/admin/media/{id}`
   */
  adminMediaGet(id: string, options?: ApiRequestOptions): Observable<Models.MediaFileResponse> {
    return this.http.request<Models.MediaFileResponse>('GET', `${this.baseUrl}/api/v1/admin/media/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Mints a short-lived link to a private file.
   * `GET /api/v1/admin/media/{id}/link`
   */
  adminMediaLink(id: string, options?: ApiRequestOptions): Observable<Models.MediaLinkResponse> {
    return this.http.request<Models.MediaLinkResponse>('GET', `${this.baseUrl}/api/v1/admin/media/${encodeURIComponent(String(id))}/link`, undefined, undefined, options);
  }

  /**
   * Lists stored files, newest first.
   * `GET /api/v1/admin/media`
   */
  adminMediaList(query?: AdminMediaListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfMediaFileResponse> {
    return this.http.request<Models.PagedResultOfMediaFileResponse>('GET', `${this.baseUrl}/api/v1/admin/media`, undefined, query, options);
  }

  /**
   * Uploads a file and returns its id, URL and responsive renditions.
   * `POST /api/v1/admin/media`
   */
  adminMediaUpload(body: { file: Blob }, query?: AdminMediaUploadQuery, options?: ApiRequestOptions): Observable<Models.MediaFileResponse> {
    return this.http.request<Models.MediaFileResponse>('POST', `${this.baseUrl}/api/v1/admin/media`, toFormData(body), query, options);
  }
}
