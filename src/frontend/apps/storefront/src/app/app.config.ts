import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideClientHydration, withEventReplay } from '@angular/platform-browser';
import { provideRouter, withInMemoryScrolling, withViewTransitions } from '@angular/router';
import { provideKlaraHomeHttp } from '@klarahome/data-access-auth';
import { provideKlaraHomeI18n } from '@klarahome/i18n';
import { RuntimeConfig, provideRuntimeConfig } from '@klarahome/util';

import { appRoutes } from './app.routes';

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

      provideRuntimeConfig(config),
      provideKlaraHomeI18n(config.locale),

      // `withEventReplay` so a tap on a server-rendered button before hydration is not lost —
      // on a slow connection that gap is seconds, and a dropped "add to cart" reads as a bug.
      provideClientHydration(withEventReplay()),

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
