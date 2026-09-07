import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import {
  NotificationsApiClient,
  PreferencesResponse,
  UpdatePreferenceRequest,
} from '@klarahome/data-access-api';
import { Observable, finalize, tap } from 'rxjs';

/**
 * What the customer wants to hear about, and where.
 *
 * A grid: one row per category — order updates, delivery, returns, promotions — and one column per
 * channel the deployment actually has. **The categories and the channels both come from the API**
 * (Step 8), which is what makes this screen correct in a deployment with no WhatsApp account and in
 * one with three: the columns are `availableChannels`, not a constant compiled into the bundle.
 *
 * A save is **one category at a time**, because that is the endpoint's shape and it is the right
 * one — a customer ticking one box should not have their other nine preferences rewritten by a
 * payload assembled from whatever the page happened to be showing.
 *
 * The local state is updated from the response rather than from the request. The server refuses
 * some combinations outright — an order confirmation cannot be turned off entirely, because it
 * carries the invoice — and a page that assumed its own write had been honoured would show a switch
 * in a position the server disagrees with.
 */
@Injectable({ providedIn: 'root' })
export class NotificationPreferencesStore {
  private readonly api = inject(NotificationsApiClient);

  private readonly preferences = signal<PreferencesResponse | null>(null);
  private readonly loading = signal(false);
  private readonly loaded = signal(false);
  /** The category currently being written, so one row can spin without freezing the grid. */
  private readonly saving = signal<string | null>(null);

  readonly current: Signal<PreferencesResponse | null> = this.preferences.asReadonly();
  readonly isLoading: Signal<boolean> = this.loading.asReadonly();
  readonly hasLoaded: Signal<boolean> = this.loaded.asReadonly();
  readonly savingCategory: Signal<string | null> = this.saving.asReadonly();

  readonly categories = computed(() => this.preferences()?.categories ?? []);
  /** Only the channels this deployment can actually send on. */
  readonly channels = computed(() => this.preferences()?.availableChannels ?? []);

  load(): Observable<PreferencesResponse> {
    this.loading.set(true);
    return this.api.storeNotificationPreferencesGet({ silentErrors: true }).pipe(
      tap((preferences) => this.preferences.set(preferences)),
      finalize(() => {
        this.loading.set(false);
        this.loaded.set(true);
      }),
    );
  }

  loadOnce(): void {
    if (this.loaded() || this.loading()) return;
    this.load().subscribe({ error: () => this.loaded.set(true) });
  }

  save(request: UpdatePreferenceRequest): Observable<PreferencesResponse> {
    this.saving.set(request.category);
    return this.api.storeNotificationPreferencesPut(request, { silentErrors: true }).pipe(
      tap((preferences) => this.preferences.set(preferences)),
      finalize(() => this.saving.set(null)),
    );
  }

  clear(): void {
    this.preferences.set(null);
    this.loaded.set(false);
  }
}
