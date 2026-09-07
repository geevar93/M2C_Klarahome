import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Badge, Button, EmptyState, ProductImage, Rating } from '@klarahome/ui-primitives';
import { KhDatePipe, KhNumberPipe } from '@klarahome/i18n';

import { RatingBreakdownView, ReviewView } from './catalog.model';

/**
 * What customers said, and the histogram above it.
 *
 * The histogram is the part that earns its place: an average of 4.2 built from forty fives and ten
 * ones is a different product from one built from fifty fours, and the bars are the only way to
 * see which. Each bar is a `<meter>`-shaped row with its own text, so the distribution is readable
 * without seeing the widths at all.
 *
 * "Helpful" is a button with `aria-pressed`, not a counter that goes up: a vote is a row keyed on
 * the voter (Step 21), so it can be withdrawn, and the control has to say which state it is in.
 *
 * The seller's reply is nested inside its review rather than listed separately — a reply detached
 * from what it answers is how a complaints thread becomes unreadable.
 */
@Component({
  selector: 'kh-review-list',
  imports: [Badge, Button, EmptyState, KhDatePipe, KhNumberPipe, ProductImage, Rating],
  template: `
    @if (canWrite()) {
      <div class="write">
        <button khButton variant="secondary" size="sm" type="button" (click)="writeRequested.emit()">
          Write a review
        </button>
      </div>
    }

    @if (breakdown(); as summary) {
      <div class="summary">
        <div class="average">
          <p class="score">{{ summary.average === null ? '—' : summary.average.toFixed(1) }}</p>
          <kh-rating [average]="summary.average" [count]="summary.count" [showEmpty]="true" />
        </div>

        <ul class="bars">
          @for (bar of summary.bars; track bar.stars) {
            <li>
              <span class="stars">{{ bar.stars }} star</span>
              <span class="track"><span class="fill" [style.inline-size.%]="bar.share * 100"></span></span>
              <span class="count">{{ bar.count | khNumber }}</span>
            </li>
          }
        </ul>
      </div>
    }

    @if (reviews().length === 0) {
      <kh-empty-state
        heading="No reviews yet"
        message="Only customers who received this item can review it, so every review here is from a real purchase."
      />
    } @else {
      <ol class="reviews">
        @for (review of reviews(); track review.id) {
          <li>
            <div class="head">
              <kh-rating size="sm" [average]="review.rating" />
              @if (review.isVerifiedPurchase) {
                <kh-badge tone="success">Verified purchase</kh-badge>
              }
              <span class="when">{{ review.publishedAt | khDate: 'd MMM y' }}</span>
            </div>

            @if (review.title) {
              <h3>{{ review.title }}</h3>
            }
            @if (review.body) {
              <p class="body">{{ review.body }}</p>
            }

            @if (review.images.length > 0) {
              <ul class="photos" aria-label="Customer photos">
                @for (image of review.images; track image.src) {
                  <li><kh-product-image [source]="image" sizes="6rem" /></li>
                }
              </ul>
            }

            <div class="foot">
              <span class="author">{{ review.author || 'A customer' }}</span>
              <button
                khButton
                variant="tertiary"
                size="sm"
                type="button"
                [attr.aria-pressed]="votedIds().includes(review.id)"
                (click)="voted.emit(review)"
              >
                Helpful ({{ review.helpfulCount | khNumber }})
              </button>
            </div>

            @if (review.sellerReply) {
              <blockquote class="reply">
                <p class="reply-label">Seller's reply</p>
                <p>{{ review.sellerReply }}</p>
              </blockquote>
            }
          </li>
        }
      </ol>

      @if (hasMore()) {
        <button
          khButton
          variant="secondary"
          type="button"
          [disabled]="loadingMore()"
          (click)="moreRequested.emit()"
        >
          {{ loadingMore() ? 'Loading…' : 'More reviews' }}
        </button>
      }
    }
  `,
  styles: `
    .write {
      display: flex;
      justify-content: flex-end;
      margin-block-end: var(--space-3);
    }

    :host {
      display: block;
    }

    .summary {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-6);
      padding-block-end: var(--space-4);
      border-block-end: 1px solid var(--color-border);
    }

    .score {
      margin: 0;
      font-size: var(--text-4xl);
      font-weight: var(--weight-bold);
      line-height: 1;
    }

    .bars {
      flex: 1;
      min-inline-size: 12rem;
      margin: 0;
      padding: 0;
      list-style: none;
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
    }

    .bars li {
      display: grid;
      grid-template-columns: 4.5rem 1fr 3rem;
      align-items: center;
      gap: var(--space-2);
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .track {
      block-size: var(--space-2);
      border-radius: var(--radius-full);
      background: var(--color-surface);
      overflow: hidden;
    }

    .fill {
      display: block;
      block-size: 100%;
      background: var(--color-warning);
    }

    .count {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }

    .reviews {
      margin: 0;
      padding: 0;
      list-style: none;
      display: flex;
      flex-direction: column;
      gap: var(--space-6);
      padding-block: var(--space-4);
    }

    .head {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: var(--space-2);
    }

    .when,
    .author {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    h3 {
      margin: var(--space-2) 0 0;
      font-size: var(--text-base);
    }

    .body {
      margin: var(--space-1) 0 0;
      white-space: pre-line;
    }

    .photos {
      display: flex;
      gap: var(--space-2);
      margin: var(--space-2) 0 0;
      padding: 0;
      list-style: none;
      overflow-x: auto;
    }

    .photos li {
      inline-size: 5rem;
      flex: none;
    }

    .foot {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
      margin-block-start: var(--space-2);
    }

    .reply {
      margin: var(--space-3) 0 0;
      padding-inline-start: var(--space-4);
      border-inline-start: 2px solid var(--color-border);
    }

    .reply p {
      margin: 0;
      font-size: var(--text-sm);
    }

    .reply-label {
      font-weight: var(--weight-medium);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReviewList {
  readonly reviews = input.required<readonly ReviewView[]>();
  readonly breakdown = input<RatingBreakdownView | null>(null);
  /** Reviews this visitor has already marked helpful, so the control shows the state it is in. */
  readonly votedIds = input<readonly string[]>([]);
  readonly hasMore = input(false);
  readonly loadingMore = input(false);

  /**
   * Whether this shopper may write one.
   *
   * The API decides, from whether they received the item (Step 21); this is only whether to offer
   * the control. Hidden rather than disabled, because "you cannot review this" is true of every
   * product a visitor has not bought and a greyed-out button on all of them would be noise.
   */
  readonly canWrite = input(false);

  readonly voted = output<ReviewView>();
  readonly writeRequested = output<void>();
  readonly moreRequested = output<void>();
}
