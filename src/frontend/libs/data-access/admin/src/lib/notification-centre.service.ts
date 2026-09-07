import { Injectable, Signal, inject, signal } from '@angular/core';
import { NotificationLogResponse, NotificationsApiClient } from '@klarahome/data-access-api';
import { Observable, map, tap } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

/** The notification log's filters, in the API's own vocabulary. */
export interface NotificationFilters {
  readonly status?: string;
  readonly channel?: string;
  readonly eventKey?: string;
  readonly from?: string;
  readonly to?: string;
}

/** The statuses that mean a message did not reach its recipient and somebody should look. */
export const FAILED_STATUSES = ['Failed', 'Bounced'] as const;

/**
 * The notifications centre.
 *
 * **What this platform means by "notifications" is the message log**, not an inbox of alerts
 * addressed to the signed-in user: the API has `GET /admin/notifications`, which is every email
 * and SMS the platform has tried to send, with what became of it. There is no per-user in-app
 * notification endpoint, and inventing a fake one on the client would be a bell that lights up
 * for nobody.
 *
 * That turns out to be the more useful surface anyway, because it answers the second question of
 * nearly every support conversation — "did they get the email?" — and, when the answer is no, the
 * retry is here rather than in a database. A dead notification is invisible otherwise: nothing
 * fails loudly when an SMS provider rejects a number.
 *
 * The badge count is the number of failed messages, refreshed on demand rather than polled. A
 * back office that polls every thirty seconds across forty open tabs is a self-inflicted load
 * test, and this number does not change quickly enough to be worth one.
 */
@Injectable({ providedIn: 'root' })
export class NotificationCentreService {
  private readonly notifications = inject(NotificationsApiClient);
  private readonly failing = signal<number | null>(null);

  /** Messages needing attention, or null until it has been counted once. */
  readonly attentionCount: Signal<number | null> = this.failing.asReadonly();

  list(
    filters: NotificationFilters = {},
    pageSize = 25,
  ): CursorList<NotificationLogResponse, NotificationFilters> {
    return new CursorList<NotificationLogResponse, NotificationFilters>(
      (current, cursor, size) =>
        this.notifications
          .adminNotificationsList({
            Status: current.status,
            Channel: current.channel,
            EventKey: current.eventKey,
            From: current.from,
            To: current.to,
            Cursor: cursor ?? undefined,
            Size: size,
          })
          .pipe(map((result): CursorPage<NotificationLogResponse> => result)),
      filters,
      pageSize,
    );
  }

  /**
   * Counts the failures, for the badge in the top bar.
   *
   * It asks for one row and reads `page.total`, so the count costs a page of one rather than a
   * page of fifty. When the endpoint declines to count — `total` is nullable by design — the
   * badge shows nothing at all rather than a zero it cannot stand behind.
   *
   * Silent: a failure here must not raise a toast on every screen the user visits. A bell that
   * cannot be counted is a bell without a number, which is a smaller problem than an error
   * message the user can do nothing about.
   */
  refreshAttentionCount(): Observable<number | null> {
    return this.notifications
      .adminNotificationsList({ Status: 'Failed', Size: 1 }, { silentErrors: true, showLoading: false })
      .pipe(
        map((result) => result.page.total ?? null),
        tap((count) => this.failing.set(count)),
      );
  }

  /** One message in full, including the error the provider gave. */
  get(id: string): Observable<NotificationLogResponse> {
    return this.notifications.adminNotificationGet(id);
  }

  /**
   * Queues a failed message for another attempt.
   *
   * Not idempotent, and deliberately not given an idempotency key: retrying twice is *meant* to
   * send twice, because the operator pressing it a second time has decided the first retry did
   * not work. Sending a duplicate email is recoverable; silently swallowing the operator's second
   * decision is not.
   */
  retry(id: string): Observable<NotificationLogResponse> {
    return this.notifications.adminNotificationRetry(id, { silentErrors: true });
  }

  /** Drops the count on sign-out, so the next user does not inherit it. */
  clear(): void {
    this.failing.set(null);
  }
}
