import { Injectable, Signal, computed, signal } from '@angular/core';

/**
 * How many requests are in flight, so a progress bar can be a signal rather than a mess of
 * booleans set and unset by whoever remembers.
 *
 * A count and not a flag: two overlapping requests must not have the first one to finish switch
 * the bar off while the second is still running.
 */
@Injectable({ providedIn: 'root' })
export class LoadingIndicator {
  private readonly inFlight = signal(0);

  readonly pending: Signal<number> = this.inFlight.asReadonly();
  readonly isLoading: Signal<boolean> = computed(() => this.inFlight() > 0);

  begin(): void {
    this.inFlight.update((count) => count + 1);
  }

  end(): void {
    this.inFlight.update((count) => Math.max(0, count - 1));
  }
}
