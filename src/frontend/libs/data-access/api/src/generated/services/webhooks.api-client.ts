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


/** `Webhooks` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class WebhooksApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Receives a signed Razorpay webhook, stores it, and answers immediately.
   * `POST /api/v1/webhooks/razorpay`
   */
  razorpayWebhook(options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/webhooks/razorpay`, undefined, undefined, options);
  }

  /**
   * Receives a signed courier webhook, stores it, and answers immediately.
   * `POST /api/v1/webhooks/shipping/{provider}`
   */
  shippingWebhook(provider: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/webhooks/shipping/${encodeURIComponent(String(provider))}`, undefined, undefined, options);
  }
}
