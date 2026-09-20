import { TestBed } from '@angular/core/testing';
import { InboxMessageResponse, InboxResponse, NotificationsApiClient } from '@klarahome/data-access-api';
import { Observable, of, throwError } from 'rxjs';

import { NotificationInboxStore } from './notification-inbox.store';

function message(id: string, readAt: string | null = null): InboxMessageResponse {
  return {
    id,
    eventKey: 'orders.suborder.shipped',
    category: 'Shipping',
    subject: `Message ${id}`,
    body: 'Body',
    createdAt: '2026-09-20T10:00:00Z',
    readAt,
  };
}

function page(items: InboxMessageResponse[], unreadCount: number, nextCursor: string | null = null): InboxResponse {
  return { items, page: { size: 20, nextCursor }, unreadCount };
}

/** A stand-in for the generated client, recording what the store asked it for. */
class FakeApi {
  list = jest.fn<Observable<InboxResponse>, [unknown, unknown]>(() => of(page([], 0)));
  markRead = jest.fn((id: string) => of(message(id, '2026-09-20T12:00:00Z')));
  markAllRead = jest.fn(() => of({ markedCount: 2 }));
  unreadCount = jest.fn(() => of({ unreadCount: 7 }));

  storeNotificationsList = (query: unknown, options: unknown) => this.list(query, options);
  storeNotificationMarkRead = (id: string) => this.markRead(id);
  storeNotificationsMarkAllRead = () => this.markAllRead();
  storeNotificationsUnreadCount = () => this.unreadCount();
}

/**
 * The in-app inbox's client state.
 *
 * Worth testing because the badge and the list are two views of one number, and every way they can
 * disagree is silent: a decrement that runs twice, a failed request that clears the badge, a
 * "load more" that replaces the list instead of appending to it. None of those throw.
 */
describe('NotificationInboxStore', () => {
  let api: FakeApi;
  let store: NotificationInboxStore;

  beforeEach(() => {
    api = new FakeApi();
    TestBed.configureTestingModule({
      providers: [{ provide: NotificationsApiClient, useValue: api }],
    });
    store = TestBed.inject(NotificationInboxStore);
  });

  it('holds the first page and the count the server reported', () => {
    api.list.mockReturnValue(of(page([message('a'), message('b', '2026-09-20T11:00:00Z')], 1)));

    store.load().subscribe();

    expect(store.items()).toHaveLength(2);
    expect(store.unreadCount()).toBe(1);
    expect(store.hasLoaded()).toBe(true);
  });

  it('appends the next page rather than replacing what is on screen', () => {
    api.list.mockReturnValueOnce(of(page([message('a')], 2, 'cursor-1')));
    store.load().subscribe();

    api.list.mockReturnValueOnce(of(page([message('b')], 2)));
    store.loadMore().subscribe();

    expect(store.items().map((item) => item.id)).toEqual(['a', 'b']);
    expect(store.hasMore()).toBe(false);
  });

  it('does not ask for another page when there is none', () => {
    api.list.mockReturnValueOnce(of(page([message('a')], 0)));
    store.load().subscribe();
    api.list.mockClear();

    store.loadMore().subscribe();

    expect(api.list).not.toHaveBeenCalled();
  });

  it('clears the badge optimistically when a message is read', () => {
    api.list.mockReturnValue(of(page([message('a')], 1)));
    store.load().subscribe();

    store.markRead('a').subscribe();

    expect(store.unreadCount()).toBe(0);
    expect(store.items()[0].readAt).not.toBeNull();
  });

  it('does not decrement twice for a message that is already read', () => {
    // The row is a link as well as a button; a double click must not take the badge below the
    // number of messages that are actually unread.
    api.list.mockReturnValue(of(page([message('a'), message('b')], 2)));
    store.load().subscribe();

    store.markRead('a').subscribe();
    store.markRead('a').subscribe();

    expect(store.unreadCount()).toBe(1);
    expect(api.markRead).toHaveBeenCalledTimes(1);
  });

  it('marks everything read and reports what the server changed', () => {
    api.list.mockReturnValue(of(page([message('a'), message('b')], 2)));
    store.load().subscribe();

    let reported = -1;
    store.markAllRead().subscribe((count) => (reported = count));

    expect(reported).toBe(2);
    expect(store.unreadCount()).toBe(0);
    expect(store.items().every((item) => item.readAt !== null)).toBe(true);
  });

  it('leaves the badge alone when the count cannot be read', () => {
    // A badge that resets to zero because one poll failed tells the shopper their messages have
    // been dealt with.
    api.list.mockReturnValue(of(page([message('a')], 4)));
    store.load().subscribe();

    api.unreadCount.mockReturnValue(throwError(() => new Error('offline')));
    store.refreshUnreadCount();

    expect(store.unreadCount()).toBe(4);
  });

  it('forgets one account\'s messages on sign-out', () => {
    api.list.mockReturnValue(of(page([message('a')], 3)));
    store.load().subscribe();

    store.clear();

    expect(store.items()).toEqual([]);
    expect(store.unreadCount()).toBe(0);
    expect(store.hasLoaded()).toBe(false);
  });
});
