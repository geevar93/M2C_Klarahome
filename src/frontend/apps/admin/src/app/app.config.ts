import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import {
  TitleStrategy,
  provideRouter,
  withComponentInputBinding,
  withInMemoryScrolling,
} from '@angular/router';
import {
  AuthService,
  SIGN_IN_PATH,
  provideKlaraHomeErrorHandling,
  provideKlaraHomeHttp,
} from '@klarahome/data-access-auth';
import { StoreConfigService } from '@klarahome/data-access-content';
import { provideKlaraHomeI18n } from '@klarahome/i18n';
import { RuntimeConfig, ThemeService, provideRuntimeConfig } from '@klarahome/util';

import { AdminTitleStrategy } from './core/title.strategy';
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

      // The last catch for anything thrown that nobody handled. The storefront has installed this
      // since Step 23; the admin did not, which meant an unhandled error here was a console line
      // and a screen that did not change.
      provideKlaraHomeErrorHandling(),

      // The back office signs in at `/login`, not at the storefront's `/auth/login`
      // (docs/05-frontend-architecture.md §4.2). The guards in `data-access-auth` read this.
      { provide: SIGN_IN_PATH, useValue: '/login' },

      { provide: TitleStrategy, useClass: AdminTitleStrategy },

      // The admin refreshes against the staff cookie, not the customer one. Set before the first
      // route resolves, so the authenticated guard's restore attempt hits the right endpoint.
      provideAppInitializer(() => {
        inject(AuthService).surface = 'admin';
      }),

      // The white-label mechanism (docs/10-design-system.md §6), wired for the admin the same way
      // `ShellStore.initialise()` wires it for the storefront: `GET /store/config` is the same
      // anonymous document either app reads, and `ThemeService` is the same one place that ever
      // calls `style.setProperty` on `:root`. The admin has no SSR pass to do this during, so the
      // tokens land after bootstrap instead — before the router activates the first route, rather
      // than after a screen has already painted with the compiled Klara Home defaults.
      provideAppInitializer(() => {
        const config = inject(StoreConfigService);
        const theme = inject(ThemeService);
        return new Promise<void>((resolve) => {
          config.load().subscribe({
            next: () => {
              theme.apply(config.branding().themeTokens);
              resolve();
            },
            error: () => resolve(),
          });
        });
      }),
    ],
  };
}
