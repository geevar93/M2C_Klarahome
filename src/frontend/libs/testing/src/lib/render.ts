import { Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideKlaraHomeHttp } from '@klarahome/data-access-auth';
import { provideKlaraHomeI18n } from '@klarahome/i18n';
import { RUNTIME_CONFIG, RuntimeConfig } from '@klarahome/util';

import { TEST_RUNTIME_CONFIG } from './test-config';

/**
 * Configures a TestBed with the same stack the apps install.
 *
 * A component test that provides `HttpClient` by hand tests a different application from the one
 * that ships: no interceptors, so no auth header, no correlation id and no error normalisation.
 * Every test goes through the real chain and stops at MSW.
 */
export function configureKlaraHomeTestingModule(options: KlaraHomeTestOptions = {}): void {
  const config = options.config ?? TEST_RUNTIME_CONFIG;

  TestBed.configureTestingModule({
    imports: options.imports ?? [],
    providers: [
      { provide: RUNTIME_CONFIG, useValue: config },
      provideKlaraHomeI18n(config.locale),
      provideKlaraHomeHttp(config),
      ...(options.providers ?? []),
    ],
  });
}

export interface KlaraHomeTestOptions {
  /** Overrides the runtime config — used to switch a feature flag on for one test. */
  readonly config?: RuntimeConfig;
  /** Standalone components, directives or pipes under test. */
  readonly imports?: readonly unknown[];
  /** Extra providers, applied after the defaults so they can replace one. */
  readonly providers?: readonly Provider[];
}
