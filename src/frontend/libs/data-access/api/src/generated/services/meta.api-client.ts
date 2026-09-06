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


/** `Meta` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class MetaApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Returns the API version, build version and server time.
   * `GET /api/v1/meta`
   */
  metaGet(options?: ApiRequestOptions): Observable<Models.MetaResponse> {
    return this.http.request<Models.MetaResponse>('GET', `${this.baseUrl}/api/v1/meta`, undefined, undefined, options);
  }
}
