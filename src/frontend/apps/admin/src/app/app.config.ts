import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { AuthService, provideKlaraHomeHttp } from '@klarahome/data-access-auth';
import { provideKlaraHomeI18n } from '@klarahome/i18n';
import { RuntimeConfig, provideRuntimeConfig } from '@klarahome/util';
import { inject, provideAppInitializer } from '@angular/core';

import { appRoutes } from './app.routes';

/**
 * The admin app's providers.
 *
 * No SSR and no hydration: the admin is behind a login, is never indexed, and rendering a
 * data-dense screen on the server buys nothing (docs/05-frontend-architecture.md §4.1).
 *
 * One app serves platform staff **and** vendors. There is no separate vendor build: the
 * navigation, the routes and the API scope all come off the authenticated user's permissions and
 * `vendorId` claim, so the two can never drift apart.
 */
export function appConfig(config: RuntimeConfig): ApplicationConfig {
  return {
    providers: [
      provideZonelessChangeDetection(),
      provideBrowserGlobalErrorListeners(),

      provideRuntimeConfig(config),
      provideKlaraHomeI18n(config.locale),

      provideRouter(
        appRoutes,
        withComponentInputBinding(),
        withInMemoryScrolling({ scrollPositionRestoration: 'top' }),
      ),

      provideKlaraHomeHttp(config),

      // The admin refreshes against the staff cookie, not the customer one. Set before the first
      // route resolves, so the authenticated guard's restore attempt hits the right endpoint.
      provideAppInitializer(() => {
        inject(AuthService).surface = 'admin';
      }),
    ],
  };
}
