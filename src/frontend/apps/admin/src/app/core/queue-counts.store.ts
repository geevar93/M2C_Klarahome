import { Injectable, computed, inject, signal } from '@angular/core';
import { DashboardService, DashboardTile } from '@klarahome/data-access-admin';

import { describeError } from './describe-error';

/** How long a set of counts is trusted before a navigation is allowed to refetch it. */
const FRESH_FOR_MS = 30_000;

/**
 * The work queues, counted once and shown in two places.
 *
 * The dashboard's tiles and the badges on the Orders and Products tabs are the same numbers — how
 * many parcels are unpacked, how many returns undecided, how many listings to review — and a
 * back office that fetched them twice would show two different answers whenever a parcel was
 * packed between the two requests. So they are fetched here, once, and both read this.
 *
 * **Freshness is a policy, not a poll.** The counts are refreshed on entry, whenever the dashboard
 * is opened (it is the page whose whole content they are), and on any navigation once they are
 * older than {@link FRESH_FOR_MS}. A person who packs six parcels and then taps Orders sees the
 * tab's badge fall; a person clicking between tabs every second does not fire six requests a
 * click. Nothing here runs on a timer, because a timer on a tab left open overnight is a request
 * per interval for nobody.
 *
 * Every request is quiet — the service already asks for `silentErrors` and no loading bar — and a
 * failure leaves the previous counts standing rather than blanking the badges: a stale number
 * with a warning on the dashboard is more useful than a badge that vanished.
 */
@Injectable({ providedIn: 'root' })
export class QueueCountsStore {
  private readonly dashboard = inject(DashboardService);

  private readonly tilesSignal = signal<readonly DashboardTile[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly failureSignal = signal<string | null>(null);
  private fetchedAt = 0;
  private everFetched = false;

  readonly tiles = this.tilesSignal.asReadonly();
  /** True only until the first answer; a refresh never puts the tiles back into skeletons. */
  readonly loading = computed(() => this.loadingSignal() && !this.everFetched);
  readonly failure = this.failureSignal.asReadonly();

  /**
   * The count for each screen that has a queue, keyed by its path.
   *
   * Two tiles can point at one screen (`Awaiting packing` and `Past dispatch due` both lead to
   * `/fulfilment`); the first declared wins, because the service declares the whole queue before
   * the subset of it.
   */
  readonly countsByPath = computed<ReadonlyMap<string, number | string>>(() => {
    const counts = new Map<string, number | string>();
    for (const tile of this.tilesSignal()) {
      if (tile.value === null || counts.has(tile.path)) continue;
      counts.set(tile.path, tile.value);
    }
    return counts;
  });

  /** Fetches the counts, unless they were fetched recently and `force` is not set. */
  refresh(force = false): void {
    const now = Date.now();
    if (this.loadingSignal()) return;
    if (!force && now - this.fetchedAt < FRESH_FOR_MS) return;

    this.loadingSignal.set(true);
    this.dashboard.tiles().subscribe({
      next: (tiles) => {
        this.tilesSignal.set(tiles);
        this.failureSignal.set(null);
        this.fetchedAt = Date.now();
        this.everFetched = true;
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => {
        this.failureSignal.set(describeError(error, 'The queue counts could not be loaded.'));
        this.fetchedAt = Date.now();
        this.everFetched = true;
        this.loadingSignal.set(false);
      },
    });
  }
}
