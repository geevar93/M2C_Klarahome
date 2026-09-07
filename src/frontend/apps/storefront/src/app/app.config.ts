import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import {
  provideClientHydration,
  withEventReplay,
  withHttpTransferCacheOptions,
  withIncrementalHydration,
} from '@angular/platform-browser';
import { provideRouter, withInMemoryScrolling, withViewTransitions } from '@angular/router';
import { provideKlaraHomeErrorHandling, provideKlaraHomeHttp } from '@klarahome/data-access-auth';
import { provideKlaraHomeI18n } from '@klarahome/i18n';
import { RuntimeConfig, provideRuntimeConfig } from '@klarahome/util';

import { appRoutes } from './app.routes';
import { isTransferCacheable } from './core/transfer-cache';

/**
 * The storefront's providers.
 *
 * A function of the runtime configuration rather than a constant, because the API base URL is not
 * known until `config.json` has been read (browser) or the environment inspected (server). That
 * is what lets one built image serve every environment (docs/05-frontend-architecture.md §5).
 */
export function appConfig(config: RuntimeConfig): ApplicationConfig {
  return {
    providers: [
      // Zoneless. Measurably faster on the low-end Android this storefront is built for, and it
      // forces every piece of state to be an explicit signal rather than something zone.js
      // happened to notice (docs/05-frontend-architecture.md §2).
      provideZonelessChangeDetection(),
      provideBrowserGlobalErrorListeners(),
      provideKlaraHomeErrorHandling(),

      provideRuntimeConfig(config),
      provideKlaraHomeI18n(config.locale),

      provideClientHydration(
        // `withEventReplay` so a tap on a server-rendered button before hydration is not lost —
        // on a slow connection that gap is seconds, and a dropped "add to cart" reads as a bug.
        withEventReplay(),
        // Incremental hydration, so a `@defer (hydrate on viewport)` block below the fold costs
        // nothing until it is scrolled to. The PDP's reviews, recommendations and Q&A are the
        // reason it is switched on here rather than at the step that writes them (§3.1).
        withIncrementalHydration(),
        // What the server fetched, the browser does not fetch again — for the public reads only.
        // See `core/transfer-cache.ts`: the allow-list is the safety property.
        withHttpTransferCacheOptions({
          includeRequestsWithCredentials: true,
          filter: isTransferCacheable,
        }),
      ),

      provideRouter(
        appRoutes,
        // Restores the scroll position on back, and honours `#fragment` links.
        withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' }),
        withViewTransitions({ skipInitialTransition: true }),
      ),

      provideKlaraHomeHttp(config),
    ],
  };
}
