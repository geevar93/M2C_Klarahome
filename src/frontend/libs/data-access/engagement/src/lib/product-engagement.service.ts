import { Injectable, inject } from '@angular/core';
import {
  PagedResultOfQuestionResponse,
  PagedResultOfReviewResponse,
  QuestionResponse,
  RatingSummaryResponse,
  ReviewBody,
  ReviewEligibilityResponse,
  ReviewResponse,
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

  // ---- Writing (Step 28B, deliverable 20) --------------------------------------------------------

  /**
   * Whether this shopper may review this product, and which purchase each review would be against.
   *
   * A review exists if and only if the customer received the line (Step 21), so the answer is a
   * list of *deliveries* rather than a yes. A product bought twice can be reviewed twice, and the
   * form has to say which one it is about — which is why this returns the lines and not a boolean.
   *
   * A failure answers "no", quietly. An anonymous visitor gets a 401 here on every product page,
   * and a toast for it would be an error message for browsing a shop.
   */
  reviewEligibility(productId: string): Observable<ReviewEligibilityResponse> {
    return this.api
      .storeGetReviewEligibility(productId, { silentErrors: true })
      .pipe(catchError(() => of<ReviewEligibilityResponse>({ canReview: false, eligible: [] })));
  }

  /**
   * Writes a review.
   *
   * Errors are **not** swallowed. A shopper who has typed three sentences and pressed the button
   * must be told if it did not land, and the API's own refusal — already reviewed, not delivered,
   * too long — is the only text that says which.
   */
  writeReview(productId: string, body: ReviewBody): Observable<ReviewResponse> {
    return this.api.storeWriteReview(productId, body, { silentErrors: true });
  }

  /** Asks a question about a product. Answered publicly after moderation, which the form says. */
  askQuestion(productId: string, body: string): Observable<QuestionResponse> {
    return this.api.storeAskQuestion(productId, { body }, { silentErrors: true });
  }
}
