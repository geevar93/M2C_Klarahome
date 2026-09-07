import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Badge, Button, EmptyState } from '@klarahome/ui-primitives';
import { KhDatePipe } from '@klarahome/i18n';

import { QuestionView } from './catalog.model';

/**
 * Questions about the product, and the answers under them.
 *
 * A definition list would be the obvious markup and is the wrong one: an answer can come from the
 * seller, from the store, or from another customer, and *who answered* changes how much the reader
 * should trust it. So each question is an article with its answers attributed — the seller's badge
 * is the point of the whole section on a marketplace.
 *
 * Asking is a button rather than an inline form. The form needs a signed-in customer and a
 * character limit and a moderation notice, and putting it behind a control keeps the page — which
 * is server-rendered and indexed — free of a form nobody has asked for yet.
 */
@Component({
  selector: 'kh-question-list',
  imports: [Badge, Button, EmptyState, KhDatePipe],
  template: `
    <div class="head">
      <h2>Questions and answers</h2>
      <button khButton variant="secondary" size="sm" type="button" (click)="askRequested.emit()">
        Ask a question
      </button>
    </div>

    @if (questions().length === 0) {
      <kh-empty-state
        heading="No questions yet"
        message="Ask anything about this product — the seller and other customers can answer."
      />
    } @else {
      <ol>
        @for (question of questions(); track question.id) {
          <li>
            <article>
              <p class="question">{{ question.body }}</p>
              <p class="asked">
                Asked by {{ question.author || 'a customer' }} ·
                {{ question.publishedAt | khDate: 'd MMM y' }}
              </p>

              @if (question.answers.length === 0) {
                <p class="unanswered">No answers yet.</p>
              } @else {
                <ul class="answers">
                  @for (answer of question.answers; track answer.id) {
                    <li>
                      <p class="answer">{{ answer.body }}</p>
                      <p class="by">
                        {{ answer.author || 'A customer' }}
                        @if (answer.authorType !== 'Customer') {
                          <kh-badge tone="info">{{ answer.authorType }}</kh-badge>
                        }
                      </p>
                    </li>
                  }
                </ul>
              }
            </article>
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
          {{ loadingMore() ? 'Loading…' : 'More questions' }}
        </button>
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    h2 {
      margin: 0;
      font-size: var(--text-xl);
    }

    ol {
      margin: var(--space-4) 0 var(--space-4);
      padding: 0;
      list-style: none;
      display: flex;
      flex-direction: column;
      gap: var(--space-5);
    }

    .question {
      margin: 0;
      font-weight: var(--weight-medium);
    }

    .asked,
    .by,
    .unanswered {
      margin: var(--space-1) 0 0;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .by {
      display: flex;
      align-items: center;
      gap: var(--space-2);
    }

    .answers {
      margin: var(--space-3) 0 0;
      padding-inline-start: var(--space-4);
      border-inline-start: 2px solid var(--color-border);
      list-style: none;
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }

    .answer {
      margin: 0;
      font-size: var(--text-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionList {
  readonly questions = input.required<readonly QuestionView[]>();
  readonly hasMore = input(false);
  readonly loadingMore = input(false);

  readonly askRequested = output<void>();
  readonly moreRequested = output<void>();
}
