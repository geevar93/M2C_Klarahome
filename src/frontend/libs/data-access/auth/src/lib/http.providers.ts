import { provideHttpClient, withFetch, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import {
  API_BASE_URL,
  correlationIdInterceptor,
  errorNormalisationInterceptor,
  loadingInterceptor,
  retryInterceptor,
} from '@klarahome/data-access-api';
import { RuntimeConfig } from '@klarahome/util';

import { authInterceptor } from './auth.interceptor';

/**
 * The HTTP stack both apps install, with the interceptor chain in the one order that works.
 *
 * Angular runs interceptors outermost-first on the way out and innermost-first on the way back,
 * so the order below is read as "who wraps whom":
 *
 *   loading → retry → error normalisation → correlation id → auth → the network
 *
 *  - **loading** is outermost so the progress bar spans the retries, rather than blinking off
 *    between attempt one and attempt two.
 *  - **retry** sits above error normalisation so a retried-then-successful request never produces
 *    an error toast for its first attempt.
 *  - **correlation id** is set below retry deliberately: every attempt of one logical request
 *    shares an id, so the server's log lines for all of them join up.
 *  - **auth** is innermost, closest to the wire, because it is the only one that may replay the
 *    request itself after a silent refresh — and that replay should not re-enter the ones above.
 *
 * `withFetch` because the storefront renders on the server, where the XHR backend has nothing to
 * talk to. `withCredentials` is set per request by the auth calls that need the refresh cookie.
 */
export function provideKlaraHomeHttp(config: RuntimeConfig): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: API_BASE_URL, useValue: config.apiBaseUrl },
    provideHttpClient(
      withFetch(),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      withInterceptors([
        loadingInterceptor,
        retryInterceptor,
        errorNormalisationInterceptor,
        correlationIdInterceptor,
        authInterceptor,
      ]),
    ),
  ]);
}
