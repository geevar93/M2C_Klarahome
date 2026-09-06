import { setupServer } from 'msw/node';
import type { SetupServerApi } from 'msw/node';

import { defaultHandlers } from './api-handlers';

/**
 * The shared MSW server.
 *
 * `onUnhandledRequest: 'error'` is deliberate and is most of the value: a request the test did not
 * declare a handler for fails the test instead of silently returning nothing and leaving the
 * component in a loading state that the assertions then misread as a bug in the component.
 */
export const apiMockServer: SetupServerApi = setupServer(...defaultHandlers);

/**
 * Wires the server into a Jest file's lifecycle. Call it once at the top of a spec.
 *
 * Handlers are reset between tests so one test's `server.use(...)` cannot leak into the next —
 * which is the failure mode that makes a suite pass alone and fail in order.
 */
export function useApiMocks(): void {
  beforeAll(() => apiMockServer.listen({ onUnhandledRequest: 'error' }));
  afterEach(() => apiMockServer.resetHandlers());
  afterAll(() => apiMockServer.close());
}
