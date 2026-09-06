import { BootstrapContext, bootstrapApplication } from '@angular/platform-browser';
import { runtimeConfigFromEnv } from '@klarahome/util';

import { App } from './app/app';
import { serverAppConfig } from './app/app.config.server';

/**
 * The server entry point.
 *
 * Configuration comes from the process environment rather than from `config.json`: there is no
 * origin to fetch a file from here, and the container is configured the same way every other
 * container in this platform is.
 */
const bootstrap = (context: BootstrapContext) =>
  bootstrapApplication(App, serverAppConfig(runtimeConfigFromEnv(process.env)), context);

export default bootstrap;
