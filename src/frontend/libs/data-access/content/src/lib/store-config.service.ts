import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { PlatformApiClient } from '@klarahome/data-access-api';
import { FeatureFlags } from '@klarahome/util';
import { Observable, catchError, of, shareReplay, tap } from 'rxjs';

/** The header treatments the storefront can draw (docs/design/storefront-designer.md). */
export type StoreHeaderStyle = 'inline' | 'centred' | 'search' | 'wordmark';
/** The footer treatments the storefront can draw. */
export type StoreFooterStyle = 'columns' | 'slim' | 'columnsNewsletter';

const HEADER_STYLES: readonly StoreHeaderStyle[] = ['inline', 'centred', 'search', 'wordmark'];
const FOOTER_STYLES: readonly StoreFooterStyle[] = ['columns', 'slim', 'columnsNewsletter'];

/** The public branding section, as the shell reads it. Every field has a usable default. */
export interface StoreBranding {
  readonly storeName: string;
  readonly tagline: string;
  /**
   * Design-token overrides for this tenant, keyed by CSS custom property name — the white-label
   * mechanism (docs/10-design-system.md §6). Empty for a tenant that has never set them, which
   * leaves the compiled `_tokens.scss` defaults in place. Applied by `ThemeService`, not read here.
   */
  readonly themeTokens: Readonly<Record<string, string>>;
  /** The storefront template this store was started from. Informational; empty when unset. */
  readonly templateId: string;
  /** Falls back to `inline` when the API omits the field or sends a value this build does not know. */
  readonly headerStyle: StoreHeaderStyle;
  /** Falls back to `columns` for the same reasons. */
  readonly footerStyle: StoreFooterStyle;
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
    // Typed loosely: the backend may predate the style fields, and an unknown value is dropped
    // below rather than trusted.
    const branding = (this.sections()['branding'] ?? {}) as Partial<
      Record<'templateId' | 'headerStyle' | 'footerStyle', string>
    > &
      Partial<StoreBranding> & { themeTokens?: Readonly<Record<string, string>> };
    return {
      storeName: branding.storeName?.trim() || 'Klara Home',
      tagline: branding.tagline?.trim() || '',
      themeTokens: branding.themeTokens ?? {},
      templateId: branding.templateId?.trim() || '',
      headerStyle: HEADER_STYLES.find((style) => style === branding.headerStyle) ?? 'inline',
      footerStyle: FOOTER_STYLES.find((style) => style === branding.footerStyle) ?? 'columns',
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
