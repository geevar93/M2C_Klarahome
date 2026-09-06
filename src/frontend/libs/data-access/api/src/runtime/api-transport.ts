import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL, ApiHeaders } from './api-config';
import { ApiRequestOptions, toHttpContext } from './http-context';

/**
 * A generated query interface.
 *
 * Deliberately `object` rather than `Record<string, unknown>`: an interface with declared members
 * does not satisfy an index signature in TypeScript, so requiring one would mean every generated
 * query interface carried an `[key: string]: unknown` that defeated the point of typing it.
 */
export type QueryParameters = object;

/**
 * The one place the generated client touches Angular's HttpClient.
 *
 * Generated code calls `request(...)` and nothing else. Everything that is a policy rather than a
 * call — headers, query serialisation, the interceptor context — is decided here, so regenerating
 * the client can never silently change how a request is made.
 */
@Injectable({ providedIn: 'root' })
export class ApiTransport {
  private readonly http = inject(HttpClient);

  /** The API root, without a trailing slash. Read by the generated clients to build their URLs. */
  readonly baseUrl = inject(API_BASE_URL).replace(/\/+$/, '');

  request<T>(
    method: string,
    url: string,
    body: unknown,
    query: QueryParameters | undefined,
    options?: ApiRequestOptions,
  ): Observable<T> {
    return this.http.request<T>(method, url, {
      body: body ?? null,
      params: toHttpParams(query),
      headers: buildHeaders(options),
      context: toHttpContext(options),
      responseType: 'json',
      observe: 'body',
      // The refresh token is an HttpOnly cookie on the API's origin, which is not the app's
      // origin. Without this the browser withholds it and every silent refresh fails with a 401
      // that looks like an expired session. The API answers a fixed origin allow-list with
      // `Access-Control-Allow-Credentials`, so this is not a wildcard being trusted.
      withCredentials: true,
    }) as Observable<T>;
  }
}

function buildHeaders(options?: ApiRequestOptions): Record<string, string> | undefined {
  const headers: Record<string, string> = { ...(options?.headers ?? {}) };
  if (options?.idempotencyKey) headers[ApiHeaders.idempotencyKey] = options.idempotencyKey;
  return Object.keys(headers).length > 0 ? headers : undefined;
}

/**
 * Serialises the query object.
 *
 * `null` and `undefined` are dropped rather than sent as empty strings — the API's model binder
 * reads `?cursor=` as "an empty cursor", not "no cursor", and the two mean different pages.
 * An array becomes repeated keys, which is what ASP.NET Core binds a collection from.
 */
function toHttpParams(query: QueryParameters | undefined): HttpParams | undefined {
  if (!query) return undefined;
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query as Record<string, unknown>)) {
    if (value === null || value === undefined) continue;
    if (Array.isArray(value)) {
      for (const item of value) {
        if (item !== null && item !== undefined) params = params.append(key, serialise(item));
      }
      continue;
    }
    params = params.set(key, serialise(value));
  }
  return params.keys().length > 0 ? params : undefined;
}

function serialise(value: unknown): string {
  if (value instanceof Date) return value.toISOString();
  return String(value);
}

/**
 * Builds the multipart body for the two upload endpoints.
 *
 * A `File` keeps its filename; anything else is sent as JSON text, which is how ASP.NET Core
 * binds a complex member of a multipart form.
 */
export function toFormData(body: unknown): FormData {
  if (body instanceof FormData) return body;
  const form = new FormData();
  for (const [key, value] of Object.entries((body ?? {}) as Record<string, unknown>)) {
    if (value === null || value === undefined) continue;
    if (value instanceof Blob) form.append(key, value, value instanceof File ? value.name : undefined);
    else if (typeof value === 'object') form.append(key, JSON.stringify(value));
    else form.append(key, String(value));
  }
  return form;
}
