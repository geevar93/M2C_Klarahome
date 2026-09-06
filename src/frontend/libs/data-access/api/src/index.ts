/**
 * The generated API client and the runtime it sits on.
 *
 * Nothing in `generated/` is written by hand — it is emitted from the API's OpenAPI document by
 * `tools/generate-api-client.mjs`, and CI fails if the committed output has drifted from the
 * current contract (docs/04-api-specification.md §7). To change a DTO, change the endpoint.
 *
 * The lint rules stop anything but `data-access` importing this library: apps and `ui` talk to
 * feature-facing services, never to transport.
 */
export * from './generated';
export * from './runtime';
export * from './interceptors/correlation-id.interceptor';
export * from './interceptors/error-normalisation.interceptor';
export * from './interceptors/loading.interceptor';
export * from './interceptors/retry.interceptor';
