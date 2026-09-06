import { RuntimeConfig } from '@klarahome/util';

/**
 * The runtime configuration tests run against.
 *
 * A fixed, obviously-fake origin, so a test that accidentally makes a real request fails loudly
 * on DNS instead of quietly hitting somebody's development API.
 */
export const TEST_API_BASE_URL = 'http://api.klarahome.test';

export const TEST_RUNTIME_CONFIG: RuntimeConfig = {
  apiBaseUrl: TEST_API_BASE_URL,
  tenantCode: 'test',
  locale: 'en-IN',
  timeZone: 'Asia/Kolkata',
  environment: 'local',
  features: {},
};

/** The same config with specific flags switched on, for a test of a flagged capability. */
export function testConfigWithFeatures(features: Record<string, boolean>): RuntimeConfig {
  return { ...TEST_RUNTIME_CONFIG, features };
}
