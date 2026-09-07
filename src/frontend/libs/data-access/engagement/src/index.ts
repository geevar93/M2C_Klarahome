export * from './lib/product-engagement.service';
export * from './lib/wishlist.store';

/**
 * The shapes these services answer with.
 *
 * The same seam as `data-access-content` and `data-access-catalog`: types only, so a page can name
 * what it renders without reaching for the transport.
 */
export type {
  AnswerResponse,
  PagedResultOfQuestionResponse,
  PagedResultOfReviewResponse,
  QuestionResponse,
  RatingSummaryResponse,
  ReviewImageResponse,
  ReviewResponse,
  WishlistItemResponse,
  WishlistResponse,
} from '@klarahome/data-access-api';
