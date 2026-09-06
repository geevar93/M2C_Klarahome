import { bootstrapApplication } from '@angular/platform-browser';
import { loadRuntimeConfig } from '@klarahome/util';

import { App } from './app/app';
import { appConfig } from './app/app.config';

/** Boot. See `apps/storefront/src/main.ts` — the reasoning is the same. */
loadRuntimeConfig()
  .then((config) => bootstrapApplication(App, appConfig(config)))
  .catch((error) => console.error(error));
