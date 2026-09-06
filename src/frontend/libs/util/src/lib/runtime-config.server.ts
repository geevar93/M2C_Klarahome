import { DEFAULT_RUNTIME_CONFIG, RuntimeConfig } from './runtime-config';

/**
 * The same configuration, read from the environment instead of from `config.json`.
 *
 * The storefront renders on Node, where there is no origin to fetch a file from and no browser to
 * fetch it with. The container is configured the way every other container in this platform is —
 * environment variables, one image, no baked-in environment file — and this is the one place that
 * translates them into the object the app injects.
 *
 * The names match the `.env` convention the backend already uses, so a compose file configures the
 * whole stack in one vocabulary.
 */
export function runtimeConfigFromEnv(env: Record<string, string | undefined>): RuntimeConfig {
  const features: Record<string, boolean> = {};

  // Environment variable names cannot contain `.` or `-`, and a flag key has both:
  // `search.external-engine`. The encoding is `__` for a dot and `_` for a hyphen, so
  // `KH_FEATURE_search__external_engine=true` is that flag — one rule, and it reverses.
  const FEATURE_PREFIX = 'KH_FEATURE_';
  for (const [key, value] of Object.entries(env)) {
    if (!key.startsWith(FEATURE_PREFIX)) continue;
    const flag = key
      .slice(FEATURE_PREFIX.length)
      .split('__')
      .map((segment) => segment.replace(/_/g, '-'))
      .join('.');
    features[flag] = value === 'true';
  }

  return {
    apiBaseUrl: (env['KH_API_BASE_URL'] ?? DEFAULT_RUNTIME_CONFIG.apiBaseUrl).replace(/\/+$/, ''),
    tenantCode: env['KH_TENANT_CODE'] ?? DEFAULT_RUNTIME_CONFIG.tenantCode,
    locale: env['KH_LOCALE'] ?? DEFAULT_RUNTIME_CONFIG.locale,
    timeZone: env['KH_TIME_ZONE'] ?? DEFAULT_RUNTIME_CONFIG.timeZone,
    environment:
      (env['KH_ENVIRONMENT'] as RuntimeConfig['environment']) ?? DEFAULT_RUNTIME_CONFIG.environment,
    features,
    analytics: {
      provider: env['KH_ANALYTICS_PROVIDER'] ?? '',
      measurementId: env['KH_ANALYTICS_MEASUREMENT_ID'] ?? '',
    },
    imageBaseUrl: env['KH_IMAGE_BASE_URL'] ?? '',
  };
}
