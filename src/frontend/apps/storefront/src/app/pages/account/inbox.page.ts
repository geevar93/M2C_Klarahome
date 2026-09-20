import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { InboxMessageResponse, NotificationInboxStore } from '@klarahome/data-access-account';
import { RelativeTimePipe } from '@klarahome/i18n';
import { Button, EmptyState, ErrorState, PageHeader, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

/**
 * Where an order or payment message links to, by the family of its event key.
 *
 * The same mapping the header bell uses, and coarse for the same reason: a message carries an
 * order *number*, which is what a shopper quotes, and the detail route wants an id.
 */
function linkFor(eventKey: string): string | null {
  return eventKey.startsWith('orders.') || eventKey.startsWith('payments.') ? '/account/orders' : null;
}

/**
 * The in-app inbox — `/account/inbox`.
 *
 * Everything the platform has told this customer inside the application, newest first. The
 * messages are the delivery log's own rows on the in-app channel (Step 8): there is no second
 * store and nothing to reconcile, which is why a message read here is read everywhere.
 *
 * **Reading is a side effect of opening, not a checkbox.** A message with somewhere to go is a
 * link and marking it read is what the click also does; one with nowhere to go is marked when its
 * body is expanded. Either way the shopper is never asked to tell the application what it can
 * already see.
 *
 * Paging is a button rather than an infinite scroll. This is a list somebody arrives at to find
 * one thing, and a footer that runs away as you reach for it is worse than a button that does not.
 */
@Component({
  selector: 'kh-account-inbox-page',
  imports: [
    Button,
    EmptyState,
    ErrorState,
    NgTemplateOutlet,
    PageHeader,
    RelativeTimePipe,
    RouterLink,
    Skeleton,
  ],
  template: `
    <kh-page-header
      title="Notifications"
      lead="Updates about your orders, deliveries and payments."
    >
      @if (store.unreadCount() > 0) {
        <button khButton variant="secondary" type="button" (click)="markAllRead()">
          Mark all as read
        </button>
      }
    </kh-page-header>

    @if (!store.hasLoaded()) {
      <kh-skeleton height="14rem" />
    } @else if (error()) {
      <kh-error-state (retry)="load()" />
    } @else if (store.items().length === 0) {
      <kh-empty-state
        heading="Nothing here yet"
        message="When something happens to an order — it ships, it arrives, a refund goes through — we will tell you here."
      >
        <a khButton variant="secondary" routerLink="/account/notifications">Choose what you hear about</a>
      </kh-empty-state>
    } @else {
      <!-- 'aria-label' rather than a visible heading: the page header above already names this
           list, and a second heading saying the same thing is one more thing to read past. -->
      <ul class="list" aria-label="Your notifications">
        @for (message of store.items(); track message.id) {
          <li [class.unread]="!isRead(message)">
            @if (linkOf(message); as href) {
              <a class="row" [routerLink]="href" (click)="read(message)">
                <ng-container
                  [ngTemplateOutlet]="content"
                  [ngTemplateOutletContext]="{ $implicit: message }"
                />
              </a>
            } @else {
              <button class="row" type="button" (click)="read(message)">
                <ng-container
                  [ngTemplateOutlet]="content"
                  [ngTemplateOutletContext]="{ $implicit: message }"
                />
              </button>
            }
          </li>
        }
      </ul>

      @if (store.hasMore()) {
        <div class="more">
          <button
            khButton
            variant="secondary"
            type="button"
            [disabled]="store.isLoadingMore()"
            (click)="loadMore()"
          >
            {{ store.isLoadingMore() ? 'Loading…' : 'Show older' }}
          </button>
        </div>
      }
    }

    <ng-template #content let-message>
      @if (!isRead(message)) {
        <span class="dot" aria-hidden="true"></span>
        <span class="sr-only">Unread.</span>
      }
      <span class="subject">{{ message.subject ?? 'Notification' }}</span>
      <span class="body">{{ message.body }}</span>
      <time class="when" [attr.datetime]="message.createdAt">{{ message.createdAt | khRelativeTime }}</time>
    </ng-template>
  `,
  styles: `
    :host {
      display: block;
    }

    .list {
      margin: 0;
      padding: 0;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      list-style: none;
    }

    .list li + li {
      border-block-start: 1px solid var(--color-border);
    }

    /* One rule for the link and the button forms, so a message with a destination and one without
       are the same object on the page. The button is reset to look like nothing, because what it
       is is a row. */
    .row {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: 0 var(--space-3);
      inline-size: 100%;
      min-height: var(--touch-target-min);
      padding: var(--space-4);
      border: 0;
      background: none;
      color: inherit;
      font: inherit;
      text-align: start;
      text-decoration: none;
      cursor: pointer;
    }

    .row:hover,
    .row:focus-visible {
      background: var(--color-bg);
    }

    .dot {
      grid-row: 1 / span 3;
      align-self: start;
      inline-size: 0.5rem;
      block-size: 0.5rem;
      /* Nudged down to sit on the subject's first line rather than above it. */
      margin-block-start: var(--space-1);
      border-radius: 50%;
      background: var(--color-primary);
    }

    .subject,
    .body,
    .when {
      grid-column: 2;
    }

    .subject {
      font-size: var(--text-base);
    }

    /* Unread carries its weight on the subject rather than a tinted row: a tint and the hover
       state are the same gesture, and one cancels the other out. */
    .unread .subject {
      font-weight: var(--weight-bold);
    }

    .body {
      margin-block-start: var(--space-1);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .when {
      margin-block-start: var(--space-1);
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .more {
      display: flex;
      justify-content: center;
      margin-block-start: var(--space-4);
    }

    .sr-only {
      position: absolute;
      clip-path: inset(50%);
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      white-space: nowrap;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountInboxPage {
  protected readonly store = inject(NotificationInboxStore);
  private readonly toasts = inject(ToastService);

  protected readonly error = signal(false);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.error.set(false);
    // A full load rather than `loadOnce`: arriving at this page is the shopper asking to see what
    // is there now, and a cached list from three navigations ago would be missing exactly the
    // message the badge sent them here for.
    this.store.load().subscribe({ error: () => this.error.set(true) });
  }

  protected loadMore(): void {
    this.store.loadMore().subscribe({ error: () => this.toasts.danger('Could not load older notifications.') });
  }

  protected markAllRead(): void {
    this.store.markAllRead().subscribe({
      next: (count) =>
        this.toasts.success(count === 1 ? '1 notification marked as read.' : `${count} notifications marked as read.`),
      error: () => this.toasts.danger('Could not mark them as read.'),
    });
  }

  protected read(message: InboxMessageResponse): void {
    this.store.markRead(message.id).subscribe({ error: () => undefined });
  }

  protected isRead(message: InboxMessageResponse): boolean {
    return message.readAt !== null;
  }

  protected linkOf(message: InboxMessageResponse): string | null {
    return linkFor(message.eventKey);
  }
}
