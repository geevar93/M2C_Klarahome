import { bootstrapApplication } from '@angular/platform-browser';
import { loadRuntimeConfig } from '@klarahome/util';

import { App } from './app/app';
import { appConfig } from './app/app.config';

/**
 * Boot.
 *
 * The configuration is read **before** the injector exists, so `API_BASE_URL` can be a real DI
 * token with a real value rather than a mutable global every consumer has to remember to read
 * late. The cost is one blocking request for a file that is a few hundred bytes and served from
 * the same origin as the bundle; the benefit is that one built image runs anywhere.
 */
loadRuntimeConfig()
  .then((config) => bootstrapApplication(App, appConfig(config)))
  .catch((error) => console.error(error));
