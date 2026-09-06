import { InjectionToken } from '@angular/core';

/**
 * Where the API is hosted, with no path and no trailing slash — for example
 * `https://api.klarahome.localhost`. The `/api/v1` prefix belongs to each generated method.
 *
 * It is a token rather than a constant because one Docker image has to serve every environment
 * and every tenant (docs/05-frontend-architecture.md §5): the value comes from the runtime
 * `config.json` the app loads before it bootstraps, never from a compiled-in environment file.
 */
export const API_BASE_URL = new InjectionToken<string>('KLARA_HOME_API_BASE_URL');

/**
 * The header names the API and the client agree on. Kept here so an interceptor and a mock
 * cannot disagree about the spelling (docs/04-api-specification.md §1).
 */
export const ApiHeaders = {
  /** Echoed on every response; the one string that ties a UI report to a server log. */
  correlationId: 'X-Correlation-Id',
  /** Required on any POST that creates an order, a payment, a refund or a payout. */
  idempotencyKey: 'Idempotency-Key',
  authorization: 'Authorization',
} as const;
