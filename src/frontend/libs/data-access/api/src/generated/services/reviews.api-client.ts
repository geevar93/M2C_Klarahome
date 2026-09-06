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

/** Query string for `adminListContentReports`. */
export interface AdminListContentReportsQuery {
  status?: string;
  reason?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListQuestions`. */
export interface AdminListQuestionsQuery {
  status?: string;
  productId?: string;
  unanswered?: boolean;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListReviews`. */
export interface AdminListReviewsQuery {
  status?: string;
  productId?: string;
  vendorId?: string;
  rating?: number;
  reported?: boolean;
  cursor?: string;
  size?: number;
}

/** Query string for `storeGetWishlist`. */
export interface StoreGetWishlistQuery {
  listId?: string;
}

/** Query string for `storeListMyReviews`. */
export interface StoreListMyReviewsQuery {
  cursor?: string;
  size?: number;
}

/** Query string for `storeListMySubscriptions`. */
export interface StoreListMySubscriptionsQuery {
  activeOnly?: boolean;
}

/** Query string for `storeListProductQuestions`. */
export interface StoreListProductQuestionsQuery {
  unanswered?: boolean;
  cursor?: string;
  size?: number;
}

/** Query string for `storeListProductReviews`. */
export interface StoreListProductReviewsQuery {
  rating?: number;
  withImages?: boolean;
  sort?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `storeRemoveFromWishlist`. */
export interface StoreRemoveFromWishlistQuery {
  listId?: string;
}

/** `Reviews` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class ReviewsApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Answers a question as the seller or as the store.
   * `POST /api/v1/admin/questions/{id}/answers`
   */
  adminAnswerQuestion(id: string, body: Models.AnswerBody, options?: ApiRequestOptions): Observable<Models.QuestionResponse> {
    return this.http.request<Models.QuestionResponse>('POST', `${this.baseUrl}/api/v1/admin/questions/${encodeURIComponent(String(id))}/answers`, body, undefined, options);
  }

  /**
   * One review in full, with its images and its complaint count.
   * `GET /api/v1/admin/reviews/{id}`
   */
  adminGetReview(id: string, options?: ApiRequestOptions): Observable<Models.ModeratedReviewResponse> {
    return this.http.request<Models.ModeratedReviewResponse>('GET', `${this.baseUrl}/api/v1/admin/reviews/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * The complaints queue, unlawful content first and then oldest first.
   * `GET /api/v1/admin/content-reports`
   */
  adminListContentReports(query?: AdminListContentReportsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfAbuseReportResponse> {
    return this.http.request<Models.PagedResultOfAbuseReportResponse>('GET', `${this.baseUrl}/api/v1/admin/content-reports`, undefined, query, options);
  }

  /**
   * The question queue, every answer under each shown whatever its state.
   * `GET /api/v1/admin/questions`
   */
  adminListQuestions(query?: AdminListQuestionsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfModeratedQuestionResponse> {
    return this.http.request<Models.PagedResultOfModeratedQuestionResponse>('GET', `${this.baseUrl}/api/v1/admin/questions`, undefined, query, options);
  }

  /**
   * The review queue. Pending first, oldest first; a seller sees only their own.
   * `GET /api/v1/admin/reviews`
   */
  adminListReviews(query?: AdminListReviewsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfModeratedReviewResponse> {
    return this.http.request<Models.PagedResultOfModeratedReviewResponse>('GET', `${this.baseUrl}/api/v1/admin/reviews`, undefined, query, options);
  }

  /**
   * Approves or refuses one answer.
   * `POST /api/v1/admin/questions/{id}/answers/{answerId}/moderate`
   */
  adminModerateAnswer(id: string, answerId: string, body: Models.ModerationBody, options?: ApiRequestOptions): Observable<Models.ModeratedQuestionResponse> {
    return this.http.request<Models.ModeratedQuestionResponse>('POST', `${this.baseUrl}/api/v1/admin/questions/${encodeURIComponent(String(id))}/answers/${encodeURIComponent(String(answerId))}/moderate`, body, undefined, options);
  }

  /**
   * Approves or refuses a question. The answers under it keep their own states.
   * `POST /api/v1/admin/questions/{id}/moderate`
   */
  adminModerateQuestion(id: string, body: Models.ModerationBody, options?: ApiRequestOptions): Observable<Models.ModeratedQuestionResponse> {
    return this.http.request<Models.ModeratedQuestionResponse>('POST', `${this.baseUrl}/api/v1/admin/questions/${encodeURIComponent(String(id))}/moderate`, body, undefined, options);
  }

  /**
   * Approves or refuses a review, republishing the rating if the decision moved it.
   * `POST /api/v1/admin/reviews/{id}/moderate`
   */
  adminModerateReview(id: string, body: Models.ModerationBody, options?: ApiRequestOptions): Observable<Models.ModeratedReviewResponse> {
    return this.http.request<Models.ModeratedReviewResponse>('POST', `${this.baseUrl}/api/v1/admin/reviews/${encodeURIComponent(String(id))}/moderate`, body, undefined, options);
  }

  /**
   * Writes a seller's public reply to a review of their own sale.
   * `POST /api/v1/admin/reviews/{id}/reply`
   */
  adminReplyToReview(id: string, body: Models.ReplyBody, options?: ApiRequestOptions): Observable<Models.ModeratedReviewResponse> {
    return this.http.request<Models.ModeratedReviewResponse>('POST', `${this.baseUrl}/api/v1/admin/reviews/${encodeURIComponent(String(id))}/reply`, body, undefined, options);
  }

  /**
   * Closes a complaint, taking the content down if it is upheld.
   * `POST /api/v1/admin/content-reports/{id}/resolve`
   */
  adminResolveContentReport(id: string, body: Models.ResolveReportBody, options?: ApiRequestOptions): Observable<Models.AbuseReportResponse> {
    return this.http.request<Models.AbuseReportResponse>('POST', `${this.baseUrl}/api/v1/admin/content-reports/${encodeURIComponent(String(id))}/resolve`, body, undefined, options);
  }

  /**
   * Saves something for later.
   * `POST /api/v1/store/wishlist/items`
   */
  storeAddToWishlist(body: Models.SaveItemBody, options?: ApiRequestOptions): Observable<Models.WishlistResponse> {
    return this.http.request<Models.WishlistResponse>('POST', `${this.baseUrl}/api/v1/store/wishlist/items`, body, undefined, options);
  }

  /**
   * Answers a question. How the answer is labelled comes from the caller's token.
   * `POST /api/v1/store/questions/{id}/answers`
   */
  storeAnswerQuestion(id: string, body: Models.AnswerBody, options?: ApiRequestOptions): Observable<Models.QuestionResponse> {
    return this.http.request<Models.QuestionResponse>('POST', `${this.baseUrl}/api/v1/store/questions/${encodeURIComponent(String(id))}/answers`, body, undefined, options);
  }

  /**
   * Asks a question about a product.
   * `POST /api/v1/store/products/{productId}/questions`
   */
  storeAskQuestion(productId: string, body: Models.QuestionBody, options?: ApiRequestOptions): Observable<Models.QuestionResponse> {
    return this.http.request<Models.QuestionResponse>('POST', `${this.baseUrl}/api/v1/store/products/${encodeURIComponent(String(productId))}/questions`, body, undefined, options);
  }

  /**
   * Withdraws a standing alert.
   * `DELETE /api/v1/store/stock-subscriptions/{id}`
   */
  storeCancelSubscription(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/store/stock-subscriptions/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Opens a new named list.
   * `POST /api/v1/store/wishlist/lists`
   */
  storeCreateWishlist(body: Models.WishlistBody, options?: ApiRequestOptions): Observable<Models.WishlistResponse> {
    return this.http.request<Models.WishlistResponse>('POST', `${this.baseUrl}/api/v1/store/wishlist/lists`, body, undefined, options);
  }

  /**
   * Deletes a list. The default one cannot be deleted.
   * `DELETE /api/v1/store/wishlist/lists/{id}`
   */
  storeDeleteWishlist(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/store/wishlist/lists/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * A product's average score and its histogram.
   * `GET /api/v1/store/products/{productId}/rating`
   */
  storeGetProductRating(productId: string, options?: ApiRequestOptions): Observable<Models.RatingSummaryResponse> {
    return this.http.request<Models.RatingSummaryResponse>('GET', `${this.baseUrl}/api/v1/store/products/${encodeURIComponent(String(productId))}/rating`, undefined, undefined, options);
  }

  /**
   * Whether the caller may review this product, and against which purchase.
   * `GET /api/v1/store/products/{productId}/reviews/eligibility`
   */
  storeGetReviewEligibility(productId: string, options?: ApiRequestOptions): Observable<Models.ReviewEligibilityResponse> {
    return this.http.request<Models.ReviewEligibilityResponse>('GET', `${this.baseUrl}/api/v1/store/products/${encodeURIComponent(String(productId))}/reviews/eligibility`, undefined, undefined, options);
  }

  /**
   * A shared list, by its link. No notes and no token in the response.
   * `GET /api/v1/store/wishlist/shared/{token}`
   */
  storeGetSharedWishlist(token: string, options?: ApiRequestOptions): Observable<Models.SharedWishlistResponse> {
    return this.http.request<Models.SharedWishlistResponse>('GET', `${this.baseUrl}/api/v1/store/wishlist/shared/${encodeURIComponent(String(token))}`, undefined, undefined, options);
  }

  /**
   * The caller's list, every card priced against today's catalogue.
   * `GET /api/v1/store/wishlist`
   */
  storeGetWishlist(query?: StoreGetWishlistQuery, options?: ApiRequestOptions): Observable<Models.WishlistResponse> {
    return this.http.request<Models.WishlistResponse>('GET', `${this.baseUrl}/api/v1/store/wishlist`, undefined, query, options);
  }

  /**
   * What the caller has written, pending and refused included.
   * `GET /api/v1/store/me/reviews`
   */
  storeListMyReviews(query?: StoreListMyReviewsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfModeratedReviewResponse> {
    return this.http.request<Models.PagedResultOfModeratedReviewResponse>('GET', `${this.baseUrl}/api/v1/store/me/reviews`, undefined, query, options);
  }

  /**
   * What the caller is waiting to hear about.
   * `GET /api/v1/store/stock-subscriptions`
   */
  storeListMySubscriptions(query?: StoreListMySubscriptionsQuery, options?: ApiRequestOptions): Observable<Models.StockSubscriptionResponse[]> {
    return this.http.request<Models.StockSubscriptionResponse[]>('GET', `${this.baseUrl}/api/v1/store/stock-subscriptions`, undefined, query, options);
  }

  /**
   * A product's published questions, best answered first.
   * `GET /api/v1/store/products/{productId}/questions`
   */
  storeListProductQuestions(productId: string, query?: StoreListProductQuestionsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfQuestionResponse> {
    return this.http.request<Models.PagedResultOfQuestionResponse>('GET', `${this.baseUrl}/api/v1/store/products/${encodeURIComponent(String(productId))}/questions`, undefined, query, options);
  }

  /**
   * A product's published reviews.
   * `GET /api/v1/store/products/{productId}/reviews`
   */
  storeListProductReviews(productId: string, query?: StoreListProductReviewsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfReviewResponse> {
    return this.http.request<Models.PagedResultOfReviewResponse>('GET', `${this.baseUrl}/api/v1/store/products/${encodeURIComponent(String(productId))}/reviews`, undefined, query, options);
  }

  /**
   * The caller's lists, without their contents.
   * `GET /api/v1/store/wishlist/lists`
   */
  storeListWishlists(options?: ApiRequestOptions): Observable<Models.WishlistResponse[]> {
    return this.http.request<Models.WishlistResponse[]>('GET', `${this.baseUrl}/api/v1/store/wishlist/lists`, undefined, undefined, options);
  }

  /**
   * Takes something off a list.
   * `DELETE /api/v1/store/wishlist/items/{variantId}`
   */
  storeRemoveFromWishlist(variantId: string, query?: StoreRemoveFromWishlistQuery, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/store/wishlist/items/${encodeURIComponent(String(variantId))}`, undefined, query, options);
  }

  /**
   * Renames a list.
   * `PUT /api/v1/store/wishlist/lists/{id}`
   */
  storeRenameWishlist(id: string, body: Models.WishlistBody, options?: ApiRequestOptions): Observable<Models.WishlistResponse> {
    return this.http.request<Models.WishlistResponse>('PUT', `${this.baseUrl}/api/v1/store/wishlist/lists/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Reports a review, question or answer to the moderation queue.
   * `POST /api/v1/store/content-reports`
   */
  storeReportContent(body: Models.ReportBody, options?: ApiRequestOptions): Observable<Models.AbuseReportResponse> {
    return this.http.request<Models.AbuseReportResponse>('POST', `${this.baseUrl}/api/v1/store/content-reports`, body, undefined, options);
  }

  /**
   * Rewrites a review the caller wrote. It returns to moderation.
   * `PUT /api/v1/store/reviews/{id}`
   */
  storeReviseReview(id: string, body: Models.ReviewBody, options?: ApiRequestOptions): Observable<Models.ReviewResponse> {
    return this.http.request<Models.ReviewResponse>('PUT', `${this.baseUrl}/api/v1/store/reviews/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Turns sharing on or off. Turning it on mints a new link and revokes the old one.
   * `POST /api/v1/store/wishlist/lists/{id}/share`
   */
  storeShareWishlist(id: string, body: Models.ShareBody, options?: ApiRequestOptions): Observable<Models.WishlistResponse> {
    return this.http.request<Models.WishlistResponse>('POST', `${this.baseUrl}/api/v1/store/wishlist/lists/${encodeURIComponent(String(id))}/share`, body, undefined, options);
  }

  /**
   * Asks to be told when something comes back or comes down.
   * `POST /api/v1/store/stock-subscriptions`
   */
  storeSubscribeToStock(body: Models.SubscriptionBody, options?: ApiRequestOptions): Observable<Models.StockSubscriptionResponse> {
    return this.http.request<Models.StockSubscriptionResponse>('POST', `${this.baseUrl}/api/v1/store/stock-subscriptions`, body, undefined, options);
  }

  /**
   * Records whether the caller found a review useful.
   * `POST /api/v1/store/reviews/{id}/helpful`
   */
  storeVoteOnReview(id: string, body: Models.VoteBody, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('POST', `${this.baseUrl}/api/v1/store/reviews/${encodeURIComponent(String(id))}/helpful`, body, undefined, options);
  }

  /**
   * Withdraws a helpfulness vote.
   * `DELETE /api/v1/store/reviews/{id}/helpful`
   */
  storeWithdrawReviewVote(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/store/reviews/${encodeURIComponent(String(id))}/helpful`, undefined, undefined, options);
  }

  /**
   * Writes a review against a delivered purchase.
   * `POST /api/v1/store/products/{productId}/reviews`
   */
  storeWriteReview(productId: string, body: Models.ReviewBody, options?: ApiRequestOptions): Observable<Models.ReviewResponse> {
    return this.http.request<Models.ReviewResponse>('POST', `${this.baseUrl}/api/v1/store/products/${encodeURIComponent(String(productId))}/reviews`, body, undefined, options);
  }
}
