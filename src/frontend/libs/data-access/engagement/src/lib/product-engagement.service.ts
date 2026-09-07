import { Injectable, inject } from '@angular/core';
import {
  PagedResultOfQuestionResponse,
  PagedResultOfReviewResponse,
  RatingSummaryResponse,
  ReviewsApiClient,
} from '@klarahome/data-access-api';
import { Observable, catchError, of } from 'rxjs';

/** An empty page, in the shape the API answers with. */
const emptyPage = { size: 0, nextCursor: null, total: 0 };
const emptyReviews: PagedResultOfReviewResponse = { items: [], page: emptyPage };
const emptyQuestions: PagedResultOfQuestionResponse = { items: [], page: emptyPage };

/**
 * What customers have said about a product: the ratings, the reviews and the questions.
 *
 * All three are **below the fold on the PDP and every one of them degrades to nothing**. A product
 * page whose reviews service is unwell still has to sell the product — so a failure here answers an
 * empty page, suppresses the toast, and lets the section render its own "no reviews yet" state.
 * That is a different policy from the catalogue, where a missing product is a 404 the route has to
 * know about.
 *
 * Nothing here is cached. A review posted five minutes ago should be visible on the next page load,
 * and the reads are already behind `@defer` on the PDP, so they cost nothing until scrolled to.
 */
@Injectable({ providedIn: 'root' })
export class ProductEngagementService {
  private readonly api = inject(ReviewsApiClient);

  /** The star histogram. Null average and zero counts until the first review lands. */
  rating(productId: string): Observable<RatingSummaryResponse> {
    return this.api.storeGetProductRating(productId, { silentErrors: true }).pipe(
      catchError(() =>
        of<RatingSummaryResponse>({
          productId,
          average: null,
          count: 0,
          oneStar: 0,
          twoStar: 0,
          threeStar: 0,
          fourStar: 0,
          fiveStar: 0,
        }),
      ),
    );
  }

  reviews(
    productId: string,
    cursor: string | null = null,
    size = 10,
  ): Observable<PagedResultOfReviewResponse> {
    return this.api
      .storeListProductReviews(productId, { cursor: cursor ?? undefined, size }, { silentErrors: true })
      .pipe(catchError(() => of(emptyReviews)));
  }

  questions(
    productId: string,
    cursor: string | null = null,
    size = 10,
  ): Observable<PagedResultOfQuestionResponse> {
    return this.api
      .storeListProductQuestions(productId, { cursor: cursor ?? undefined, size }, { silentErrors: true })
      .pipe(catchError(() => of(emptyQuestions)));
  }

  /**
   * Marks a review helpful.
   *
   * Errors are **not** swallowed: a vote is an action the shopper took deliberately, and one that
   * silently did nothing is worse than one that says so. The page decides what to show.
   */
  voteHelpful(reviewId: string, isHelpful = true): Observable<void> {
    return this.api.storeVoteOnReview(reviewId, { isHelpful });
  }

  /** Withdraws this visitor's vote. The API keys the vote on the voter, so it is idempotent. */
  withdrawVote(reviewId: string): Observable<void> {
    return this.api.storeWithdrawReviewVote(reviewId);
  }
}
