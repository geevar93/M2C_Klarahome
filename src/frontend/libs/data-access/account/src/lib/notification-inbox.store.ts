import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { InboxMessageResponse, InboxResponse, NotificationsApiClient } from '@klarahome/data-access-api';
import { Observable, finalize, map, of, tap } from 'rxjs';

/** How many messages the bell's dropdown asks for. Enough to be useful, short enough not to scroll. */
export const INBOX_PREVIEW_SIZE = 5;

/** How many the full inbox page asks for per request. */
export const INBOX_PAGE_SIZE = 20;

/**
 * What the platform has told this customer inside the application.
 *
 * The delivery log *is* the inbox (Step 8): a message on the in-app channel is a row that was
 * written and never handed to a provider, so there is nothing to sync and nothing that can be read
 * anywhere else. That makes this store the only place the unread count exists, which is why the
 * header badge and the inbox page share one instance rather than each counting for themselves.
 *
 * **Read state is server state, and the optimism here is deliberate and bounded.** Marking read is
 * applied locally before the request resolves, because a badge that waits for a round-trip to clear
 * feels broken; but the *count* is re-read from the server on every page load rather than being
 * kept as a running total, so a second tab, a phone, or a message that arrived while the page sat
 * open all correct it within a navigation.
 *
 * Paging is keyset, forward-only, and appends. The list is "newest first, older on demand" and
 * never a random-access pager: the cursor the API hands back describes one position in one
 * ordering, and a page number would be a lie about a table that grows at the top while you read it.
 */
@Injectable({ providedIn: 'root' })
export class NotificationInboxStore {
  private readonly api = inject(NotificationsApiClient);

  private readonly messages = signal<readonly InboxMessageResponse[]>([]);
  private readonly unread = signal(0);
  private readonly cursor = signal<string | null>(null);
  private readonly loading = signal(false);
  private readonly loadingMore = signal(false);
  private readonly loaded = signal(false);
  /** The message currently being marked, so one row can spin without freezing the list. */
  private readonly marking = signal<string | null>(null);

  readonly items: Signal<readonly InboxMessageResponse[]> = this.messages.asReadonly();
  readonly unreadCount: Signal<number> = this.unread.asReadonly();
  readonly isLoading: Signal<boolean> = this.loading.asReadonly();
  readonly isLoadingMore: Signal<boolean> = this.loadingMore.asReadonly();
  readonly hasLoaded: Signal<boolean> = this.loaded.asReadonly();
  readonly markingId: Signal<string | null> = this.marking.asReadonly();

  /** Whether there is another page behind this one. */
  readonly hasMore = computed(() => this.cursor() !== null);

  /** The newest few, for the header dropdown. */
  readonly preview = computed(() => this.messages().slice(0, INBOX_PREVIEW_SIZE));

  /**
   * Loads the first page, replacing whatever is held.
   *
   * @param size How many to ask for.
   */
  load(size = INBOX_PAGE_SIZE): Observable<InboxResponse> {
    this.loading.set(true);

    return this.api.storeNotificationsList({ size }, { silentErrors: true }).pipe(
      tap((response) => {
        this.messages.set(response.items);
        this.unread.set(response.unreadCount);
        this.cursor.set(response.page.nextCursor);
      }),
      finalize(() => {
        this.loading.set(false);
        this.loaded.set(true);
      }),
    );
  }

  /** Loads the first page once per session, for a component that only needs it to be there. */
  loadOnce(size = INBOX_PAGE_SIZE): void {
    if (this.loaded() || this.loading()) return;
    this.load(size).subscribe({ error: () => this.loaded.set(true) });
  }

  /**
   * Appends the next page.
   *
   * Does nothing at the end of the list or while a page is already in flight — the second guard is
   * what stops a scroll-triggered call from requesting the same cursor twice.
   */
  loadMore(size = INBOX_PAGE_SIZE): Observable<InboxResponse | null> {
    const cursor = this.cursor();
    if (cursor === null || this.loadingMore()) return of(null);

    this.loadingMore.set(true);

    return this.api.storeNotificationsList({ cursor, size }, { silentErrors: true }).pipe(
      tap((response) => {
        this.messages.update((current) => [...current, ...response.items]);
        this.unread.set(response.unreadCount);
        this.cursor.set(response.page.nextCursor);
      }),
      finalize(() => this.loadingMore.set(false)),
    );
  }

  /**
   * Re-reads the unread count alone.
   *
   * The header's only request on an ordinary page view. It carries no bodies, and it is what keeps
   * the badge honest without the shell holding a list nothing is displaying.
   */
  refreshUnreadCount(): void {
    this.api.storeNotificationsUnreadCount({ silentErrors: true }).subscribe({
      next: (response) => this.unread.set(response.unreadCount),
      error: () => {
        // Left as it was. A badge that resets to zero because one poll failed tells the shopper
        // their messages have been dealt with, which is worse than a stale number.
      },
    });
  }

  /**
   * Marks one message read.
   *
   * Applied locally first, then confirmed from the response. An already-read message is not sent
   * at all: the server keeps the first read's timestamp either way, and the request would buy
   * nothing.
   */
  markRead(id: string): Observable<InboxMessageResponse | null> {
    const existing = this.messages().find((message) => message.id === id);
    if (!existing || existing.readAt !== null) return of(null);

    this.marking.set(id);
    this.applyRead(id, new Date().toISOString());
    this.unread.update((count) => Math.max(0, count - 1));

    return this.api.storeNotificationMarkRead(id, { silentErrors: true }).pipe(
      tap((message) => this.applyRead(id, message.readAt)),
      finalize(() => this.marking.set(null)),
    );
  }

  /**
   * Marks everything unread as read.
   *
   * The count goes to zero locally rather than to the server's reported `markedCount`, which counts
   * what *it* changed. The two differ by anything that arrived between the page loading and the
   * button being pressed, and of those two numbers the shopper's own list is the one they are
   * looking at.
   */
  markAllRead(): Observable<number> {
    const now = new Date().toISOString();

    return this.api.storeNotificationsMarkAllRead({ silentErrors: true }).pipe(
      tap({
        next: () => {
          this.messages.update((current) =>
            current.map((message) => (message.readAt === null ? { ...message, readAt: now } : message)),
          );
          this.unread.set(0);
        },
        // Nothing was marked, so the badge has to come back. Re-read rather than restored from a
        // snapshot: by now the true count may have moved for reasons this failure knows nothing of.
        error: () => this.refreshUnreadCount(),
      }),
      // The caller gets what the server changed, which is the honest number to announce.
      map((response) => response.markedCount),
    );
  }

  /** Drops everything, for a sign-out. One account's messages must not survive into the next. */
  clear(): void {
    this.messages.set([]);
    this.unread.set(0);
    this.cursor.set(null);
    this.loaded.set(false);
  }

  private applyRead(id: string, readAt: string | null): void {
    this.messages.update((current) =>
      current.map((message) => (message.id === id ? { ...message, readAt } : message)),
    );
  }
}
