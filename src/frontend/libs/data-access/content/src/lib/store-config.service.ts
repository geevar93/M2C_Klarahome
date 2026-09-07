import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { PlatformApiClient } from '@klarahome/data-access-api';
import { FeatureFlags } from '@klarahome/util';
import { Observable, catchError, of, shareReplay, tap } from 'rxjs';

/** The public branding section, as the shell reads it. Every field has a usable default. */
export interface StoreBranding {
  readonly storeName: string;
  readonly tagline: string;
}

/**
 * What the store says about itself, read once at start-up.
 *
 * `GET /store/config` is the anonymous document Step 6 built: the public settings sections and
 * every feature flag as an operator has them set right now. It is what makes the header say the
 * store's name rather than a constant compiled into the bundle, and it is where `FeatureFlags`
 * gets its live values — `config.json` only ever carried the copy that was true at deploy time,
 * and a flag flipped this morning has to take effect without a redeploy.
 *
 * It lives in the content library because it is the storefront's "what is this shop" read, and it
 * has to live in a `data-access` library because nothing else may touch the generated client.
 */
@Injectable({ providedIn: 'root' })
export class StoreConfigService {
  private readonly api = inject(PlatformApiClient);
  private readonly flags = inject(FeatureFlags);

  private readonly sections = signal<Record<string, unknown>>({});
  private request?: Observable<unknown>;

  /** Branding, with the defaults the API itself declares — never a blank header. */
  readonly branding: Signal<StoreBranding> = computed(() => {
    const branding = (this.sections()['branding'] ?? {}) as Partial<StoreBranding>;
    return {
      storeName: branding.storeName?.trim() || 'Klara Home',
      tagline: branding.tagline?.trim() || '',
    };
  });

  /** One public settings section, for a caller that knows its shape. */
  section<T>(key: string): T | undefined {
    return this.sections()[key] as T | undefined;
  }

  /**
   * Loads the document. Idempotent — the shell calls it on start-up and the result is shared, so
   * a second caller during SSR does not produce a second request.
   *
   * A failure is swallowed: the storefront runs on `config.json`'s copy of the flags and the
   * default branding, which is a working shop with a generic name rather than no shop at all.
   */
  load(): Observable<unknown> {
    this.request ??= this.api.storeConfigGet({ silentErrors: true }).pipe(
      tap((config) => {
        this.sections.set(config.settings ?? {});
        if (config.features) this.flags.replaceAll(config.features);
      }),
      catchError(() => of(null)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.request;
  }
}
