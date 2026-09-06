import { HttpResponse, http } from 'msw';
import type { HttpHandler } from 'msw';

import { TEST_API_BASE_URL } from './test-config';

/**
 * MSW handlers shared by every integration-level test.
 *
 * Mocking at the network boundary rather than by replacing a service is the point: the request
 * that leaves the app is the request the test asserts on, so the interceptor chain — the auth
 * header, the correlation id, the retry — is exercised rather than stubbed past
 * (docs/05-frontend-architecture.md §7).
 *
 * The handlers here answer only what every test needs. A feature's own handlers are declared with
 * the feature and passed to `server.use(...)`, which keeps this file from becoming a second,
 * unversioned copy of the API.
 */

/** Builds an absolute URL for a path such as `/api/v1/store/config`. */
export const apiUrl = (path: string): string => `${TEST_API_BASE_URL}${path}`;

/**
 * An RFC 9457 ProblemDetails body, in the shape the real API sends
 * (docs/04-api-specification.md §1.2), so a test of error handling exercises the real parse.
 */
export function problemDetails(
  status: number,
  code: string,
  overrides: Record<string, unknown> = {},
): HttpResponse {
  return HttpResponse.json(
    {
      type: `https://klarahome.dev/errors/${code.toLowerCase().replace(/_/g, '-')}`,
      title: code,
      status,
      code,
      detail: `Test failure: ${code}.`,
      correlationId: '00000000-0000-4000-8000-000000000000',
      ...overrides,
    },
    { status, headers: { 'X-Correlation-Id': '00000000-0000-4000-8000-000000000000' } },
  );
}

/** A paged collection in the standard envelope (docs/04-api-specification.md §1.1). */
export const pagedResult = <T>(items: readonly T[], nextCursor: string | null = null) => ({
  items,
  page: { size: items.length, nextCursor, prevCursor: null, total: items.length },
});

/**
 * The baseline: an anonymous, unconfigured store that answers rather than hangs.
 *
 * Every test starts from a session that does not exist and a store with no flags on. A test that
 * needs otherwise says so, which makes its preconditions visible in the test itself.
 */
export const defaultHandlers: HttpHandler[] = [
  http.get(apiUrl('/api/v1/store/config'), () =>
    HttpResponse.json({ tenantCode: 'test', locale: 'en-IN', currency: 'INR', features: {} }),
  ),

  // Not signed in. 401 rather than an empty body, because that is what the real endpoint answers
  // when there is no refresh cookie, and the app's start-up path depends on the difference.
  http.post(apiUrl('/api/v1/store/auth/refresh'), () => problemDetails(401, 'UNAUTHENTICATED')),
  http.post(apiUrl('/api/v1/admin/auth/refresh'), () => problemDetails(401, 'UNAUTHENTICATED')),
];
