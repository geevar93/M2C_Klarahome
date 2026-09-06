import { EnvironmentProviders, InjectionToken, makeEnvironmentProviders } from '@angular/core';

/**
 * The configuration one built image reads at start-up.
 *
 * **This is the reason there is no `environment.prod.ts`.** The storefront and the admin app are
 * each built once and run in every environment and for every tenant; what differs between them is
 * a `config.json` served beside the bundle, fetched before the app bootstraps
 * (docs/05-frontend-architecture.md §5). A rebuild is never the way to point at a different API.
 */
export interface RuntimeConfig {
  /**
   * Where the API is hosted, with no path and no trailing slash — `https://api.klarahome.dev`.
   *
   * The version segment is NOT part of it: every generated client method carries its own
   * `/api/v1/...` path, because the version is part of the contract the client was generated
   * from and not part of where that contract is deployed.
   */
  readonly apiBaseUrl: string;
  /** Tenant code, echoed in analytics and used to scope per-tenant storage keys. */
  readonly tenantCode: string;
  /** BCP 47 locale. `en-IN` is the only one built today; the structure is ready for more. */
  readonly locale: string;
  /** IANA zone the storefront renders dates in. */
  readonly timeZone: string;
  /** Environment name, for the log sink and for hiding developer affordances in production. */
  readonly environment: 'local' | 'development' | 'staging' | 'production';
  /**
   * Feature flags as the server last published them. Treated as a default: the flags service
   * refreshes from `/store/config` once the app is running, so a flag flipped by an operator
   * takes effect without a redeploy.
   */
  readonly features: Readonly<Record<string, boolean>>;
  /** Analytics destination. Blank disables the sink entirely — see `AnalyticsService`. */
  readonly analytics?: {
    readonly provider?: string;
    readonly measurementId?: string;
  };
  /** Where product images are resized. Blank means images are served from the API's own URLs. */
  readonly imageBaseUrl?: string;
}

export const RUNTIME_CONFIG = new InjectionToken<RuntimeConfig>('KLARA_HOME_RUNTIME_CONFIG');

/**
 * What the app falls back to when `config.json` is missing or unreadable.
 *
 * It points at the local compose stack, so a developer who has not copied the file still gets a
 * working app rather than a blank screen and a console error. In every other environment the
 * absence of the file is a deployment fault, and `loadRuntimeConfig` says so in the console.
 */
export const DEFAULT_RUNTIME_CONFIG: RuntimeConfig = {
  apiBaseUrl: 'https://api.klarahome.localhost',
  tenantCode: 'klarahome',
  locale: 'en-IN',
  timeZone: 'Asia/Kolkata',
  environment: 'local',
  features: {},
};

/**
 * Loads `config.json` before the app bootstraps.
 *
 * It deliberately uses `fetch` rather than `HttpClient`: this runs before there is an injector,
 * which is the only way the API base URL can be a real DI token instead of a mutable global that
 * every consumer has to remember to read late.
 */
export async function loadRuntimeConfig(url = '/config.json'): Promise<RuntimeConfig> {
  try {
    const response = await fetch(url, { cache: 'no-cache', credentials: 'omit' });
    if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
    const loaded = (await response.json()) as Partial<RuntimeConfig>;
    return { ...DEFAULT_RUNTIME_CONFIG, ...loaded, features: { ...loaded.features } };
  } catch (error) {
    // Not thrown: an app that will not start at all is a worse failure than one running against
    // its documented defaults, and the message names the file so the cause is not a mystery.
    console.error(`[config] Could not load ${url}; falling back to local defaults.`, error);
    return DEFAULT_RUNTIME_CONFIG;
  }
}

/** Registers a loaded configuration so `RUNTIME_CONFIG` can be injected anywhere. */
export function provideRuntimeConfig(config: RuntimeConfig): EnvironmentProviders {
  return makeEnvironmentProviders([{ provide: RUNTIME_CONFIG, useValue: config }]);
}
