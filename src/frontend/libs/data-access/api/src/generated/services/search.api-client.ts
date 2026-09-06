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

/** Query string for `adminListStopWords`. */
export interface AdminListStopWordsQuery {
  activeOnly?: boolean;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListSynonyms`. */
export interface AdminListSynonymsQuery {
  search?: string;
  activeOnly?: boolean;
  cursor?: string;
  size?: number;
}

/** Query string for `adminSearchQueryReport`. */
export interface AdminSearchQueryReportQuery {
  from?: string;
  to?: string;
  source?: string;
  size?: number;
}

/** Query string for `adminSearchZeroResults`. */
export interface AdminSearchZeroResultsQuery {
  from?: string;
  to?: string;
  source?: string;
  size?: number;
}

/** Query string for `storeSearchSuggest`. */
export interface StoreSearchSuggestQuery {
  q?: string;
  limit?: number;
}

/** `Search` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class SearchApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Adds a word to the stop list.
   * `POST /api/v1/admin/search/stop-words`
   */
  adminCreateStopWord(body: Models.CreateStopWordBody, options?: ApiRequestOptions): Observable<Models.StopWordResponse> {
    return this.http.request<Models.StopWordResponse>('POST', `${this.baseUrl}/api/v1/admin/search/stop-words`, body, undefined, options);
  }

  /**
   * Adds a synonym rule. Single words only.
   * `POST /api/v1/admin/search/synonyms`
   */
  adminCreateSynonym(body: Models.CreateSynonymBody, options?: ApiRequestOptions): Observable<Models.SynonymResponse> {
    return this.http.request<Models.SynonymResponse>('POST', `${this.baseUrl}/api/v1/admin/search/synonyms`, body, undefined, options);
  }

  /**
   * Removes a word from the stop list.
   * `DELETE /api/v1/admin/search/stop-words/{id}`
   */
  adminDeleteStopWord(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/search/stop-words/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Removes a synonym rule.
   * `DELETE /api/v1/admin/search/synonyms/{id}`
   */
  adminDeleteSynonym(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/search/synonyms/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * The words this store ignores in a query.
   * `GET /api/v1/admin/search/stop-words`
   */
  adminListStopWords(query?: AdminListStopWordsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfStopWordResponse> {
    return this.http.request<Models.PagedResultOfStopWordResponse>('GET', `${this.baseUrl}/api/v1/admin/search/stop-words`, undefined, query, options);
  }

  /**
   * The store's synonym rules, alphabetically.
   * `GET /api/v1/admin/search/synonyms`
   */
  adminListSynonyms(query?: AdminListSynonymsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfSynonymResponse> {
    return this.http.request<Models.PagedResultOfSynonymResponse>('GET', `${this.baseUrl}/api/v1/admin/search/synonyms`, undefined, query, options);
  }

  /**
   * Rebuilds index rows from the catalogue. Bounded and resumable.
   * `POST /api/v1/admin/search/index/rebuild`
   */
  adminRebuildSearchIndex(body?: null | Models.RebuildIndexBody, options?: ApiRequestOptions): Observable<Models.ReindexResponse> {
    return this.http.request<Models.ReindexResponse>('POST', `${this.baseUrl}/api/v1/admin/search/index/rebuild`, body, undefined, options);
  }

  /**
   * How many rows are indexed, how many have fallen behind, and which engine answers.
   * `GET /api/v1/admin/search/index`
   */
  adminSearchIndexStatus(options?: ApiRequestOptions): Observable<Models.SearchIndexStatus> {
    return this.http.request<Models.SearchIndexStatus>('GET', `${this.baseUrl}/api/v1/admin/search/index`, undefined, undefined, options);
  }

  /**
   * What shoppers searched for, most-asked first, with click-through.
   * `GET /api/v1/admin/search/queries`
   */
  adminSearchQueryReport(query?: AdminSearchQueryReportQuery, options?: ApiRequestOptions): Observable<Models.SearchQueryReportRow[]> {
    return this.http.request<Models.SearchQueryReportRow[]>('GET', `${this.baseUrl}/api/v1/admin/search/queries`, undefined, query, options);
  }

  /**
   * What shoppers searched for and did not find. The buying team's list.
   * `GET /api/v1/admin/search/queries/zero-results`
   */
  adminSearchZeroResults(query?: AdminSearchZeroResultsQuery, options?: ApiRequestOptions): Observable<Models.SearchQueryReportRow[]> {
    return this.http.request<Models.SearchQueryReportRow[]>('GET', `${this.baseUrl}/api/v1/admin/search/queries/zero-results`, undefined, query, options);
  }

  /**
   * Switches a stop word on or off without losing it.
   * `PUT /api/v1/admin/search/stop-words/{id}`
   */
  adminSetStopWordActive(id: string, body: Models.SetStopWordActiveBody, options?: ApiRequestOptions): Observable<Models.StopWordResponse> {
    return this.http.request<Models.StopWordResponse>('PUT', `${this.baseUrl}/api/v1/admin/search/stop-words/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Changes what a word is taken to mean, or switches the rule off.
   * `PUT /api/v1/admin/search/synonyms/{id}`
   */
  adminUpdateSynonym(id: string, body: Models.UpdateSynonymBody, options?: ApiRequestOptions): Observable<Models.SynonymResponse> {
    return this.http.request<Models.SynonymResponse>('PUT', `${this.baseUrl}/api/v1/admin/search/synonyms/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Records which result a shopper opened, so ranking can be measured.
   * `POST /api/v1/store/search/click`
   */
  storeSearchClick(body: Models.SearchClickBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/store/search/click`, body, undefined, options);
  }

  /**
   * The faceted product listing: search, browse, filter, sort and page.
   * `GET /api/v1/store/products`
   */
  storeSearchProducts(options?: ApiRequestOptions): Observable<Models.ProductSearchResponse> {
    return this.http.request<Models.ProductSearchResponse>('GET', `${this.baseUrl}/api/v1/store/products`, undefined, undefined, options);
  }

  /**
   * Autocomplete: products, popular searches, brands and categories.
   * `GET /api/v1/store/search/suggest`
   */
  storeSearchSuggest(query?: StoreSearchSuggestQuery, options?: ApiRequestOptions): Observable<Models.SuggestionResponse> {
    return this.http.request<Models.SuggestionResponse>('GET', `${this.baseUrl}/api/v1/store/search/suggest`, undefined, query, options);
  }
}
