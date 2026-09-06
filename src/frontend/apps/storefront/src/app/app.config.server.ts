import { ApplicationConfig, mergeApplicationConfig } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { RuntimeConfig } from '@klarahome/util';

import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';

/** The browser providers plus server rendering. Configuration comes from the environment. */
export function serverAppConfig(config: RuntimeConfig): ApplicationConfig {
  return mergeApplicationConfig(appConfig(config), {
    providers: [provideServerRendering(withRoutes(serverRoutes))],
  });
}
