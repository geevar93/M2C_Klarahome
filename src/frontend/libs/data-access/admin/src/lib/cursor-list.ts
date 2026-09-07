import { Signal, WritableSignal, computed, signal } from '@angular/core';
import { PageInfo } from '@klarahome/data-access-api';
import { Observable, Subscription } from 'rxjs';

/** One page, as every list endpoint on this platform answers it. */
export interface CursorPage<TRow> {
  readonly items: readonly TRow[];
  readonly page: PageInfo;
}

/** Fetches one page. The store supplies the cursor; the caller closes over everything else. */
export type CursorFetch<TRow, TFilters> = (
  filters: TFilters,
  cursor: string | null,
  size: number,
) => Observable<CursorPage<TRow>>;

/**
 * A cursor-paged list, with the cursors it has already seen.
 *
 * Every list endpoint in this API pages by **keyset** (`docs/04-api-specification.md` §1.1): a
 * response carries `nextCursor` and nothing else, because a page fetched while rows are being
 * inserted must not repeat or skip one. That is the right guarantee and it has one consequence
 * every screen has to deal with — **the server cannot go backwards**. Only the client knows where
 * page two started, and only because it was there.
 *
 * So this keeps a stack of the cursors it has used. Back pops one and refetches; a filter change
 * empties it, because the previous page under the old filter is not a page under the new one.
 * Written once here rather than in each of the twenty admin lists that need it, because a
 * half-emptied cursor stack shows the user a page of somebody else's results and looks like a
 * data bug rather than a paging bug.
 *
 * The in-flight request is **cancelled when a newer one is issued**. Without that, a slow first
 * page arriving after a fast filtered one replaces the filtered results with the unfiltered ones,
 * and the screen contradicts the filter bar above it.
 */
export class CursorList<TRow, TFilters> {
  private readonly items = signal<readonly TRow[]>([]);
  private readonly busy = signal(false);
  private readonly failure = signal<string | null>(null);
  private readonly info = signal<PageInfo | null>(null);
  private readonly seenCursors: WritableSignal<readonly string[]> = signal([]);
  private readonly currentFilters: WritableSignal<TFilters>;

  private inFlight: Subscription | null = null;
  /** The cursor that produced the page currently on screen. Null on the first page. */
  private activeCursor: string | null = null;

  readonly rows: Signal<readonly TRow[]> = this.items.asReadonly();
  readonly loading: Signal<boolean> = this.busy.asReadonly();
  /** The last failure's message, or null. Rendered by the page; not a toast. */
  readonly error: Signal<string | null> = this.failure.asReadonly();
  readonly filters: Signal<TFilters>;

  readonly nextCursor = computed(() => this.info()?.nextCursor ?? null);
  readonly hasPrevious = computed(() => this.seenCursors().length > 0);
  /** The server's count where it offers one, and null where it does not. Never a page count. */
  readonly total = computed(() => this.info()?.total ?? null);
  readonly size: Signal<number>;
  readonly isEmpty = computed(() => !this.busy() && this.items().length === 0);

  constructor(
    private readonly fetch: CursorFetch<TRow, TFilters>,
    initialFilters: TFilters,
    pageSize = 25,
  ) {
    this.currentFilters = signal(initialFilters);
    this.filters = this.currentFilters.asReadonly();
    this.size = signal(pageSize).asReadonly();
  }

  /** Loads the first page under the current filters. */
  load(): void {
    this.seenCursors.set([]);
    this.request(null);
  }

  /** Replaces the filters and returns to the first page — which is the only page they describe. */
  setFilters(filters: TFilters): void {
    this.currentFilters.set(filters);
    this.load();
  }

  next(): void {
    const cursor = this.nextCursor();
    if (!cursor || this.busy()) return;

    // The cursor that produced the page being left, so Back can ask for it again.
    this.seenCursors.update((stack) => [...stack, this.activeCursor ?? '']);
    this.request(cursor);
  }

  previous(): void {
    if (!this.hasPrevious() || this.busy()) return;

    const stack = [...this.seenCursors()];
    const cursor = stack.pop() ?? '';
    this.seenCursors.set(stack);
    this.request(cursor === '' ? null : cursor);
  }

  /** Refetches the page on screen — after a bulk action, or a manual refresh. */
  refresh(): void {
    this.request(this.activeCursor);
  }

  /** Drops the rows without fetching. Used on sign-out, so the next user sees nothing. */
  clear(): void {
    this.inFlight?.unsubscribe();
    this.inFlight = null;
    this.items.set([]);
    this.info.set(null);
    this.seenCursors.set([]);
    this.failure.set(null);
    this.busy.set(false);
  }

  private request(cursor: string | null): void {
    // A newer request wins. See the class remarks: the alternative is a stale page overwriting a
    // fresh one and contradicting the filters on screen.
    this.inFlight?.unsubscribe();
    this.busy.set(true);
    this.failure.set(null);
    this.activeCursor = cursor;

    this.inFlight = this.fetch(this.currentFilters(), cursor, this.size()).subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.info.set(result.page);
        this.busy.set(false);
      },
      error: (error: unknown) => {
        // The rows are kept rather than blanked: a failed refresh should leave the last good page
        // on screen under an error, not an empty table that looks like "no results".
        this.failure.set(messageOf(error));
        this.busy.set(false);
      },
    });
  }
}

function messageOf(error: unknown): string {
  const message = (error as { message?: unknown } | null)?.message;
  return typeof message === 'string' && message.length > 0
    ? message
    : 'That list could not be loaded. Try again.';
}
