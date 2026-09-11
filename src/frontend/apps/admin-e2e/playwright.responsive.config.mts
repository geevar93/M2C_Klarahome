import { defineConfig } from '@playwright/test';

/**
 * Responsive visual QA — Step 30. See the equivalent storefront config
 * (`apps/storefront-e2e/playwright.responsive.config.mts`) for why this is a third config rather
 * than folded into `playwright.visual.config.mts`, and for where the breakpoint matrix comes from.
 *
 * Coverage is the same two screens `playwright.visual.config.mts` baselines, for the same reason
 * recorded there: every other admin screen sits behind `authenticatedGuard`, and the
 * `platform-admin` role is mandatory-two-factor (docs/dev-setup.md), which a Playwright fixture
 * for this turn does not script. Sign-in and forgot-password are what is actually reachable.
 *
 * Run: npx nx run admin-e2e:responsive-qa
 */
const baseURL = process.env['BASE_URL'] || 'https://admin.klarahome.localhost';

export default defineConfig({
  testDir: './src',
  testMatch: '**/*.responsive.spec.ts',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  reporter: [['list']],
  use: {
    baseURL,
    ignoreHTTPSErrors: true,
  },
  projects: [
    { name: 'mobile-360', use: { viewport: { width: 360, height: 640 } } },
    { name: 'tablet-768', use: { viewport: { width: 768, height: 1024 } } },
    { name: 'laptop-1024', use: { viewport: { width: 1024, height: 900 } } },
    { name: 'desktop-1280', use: { viewport: { width: 1280, height: 900 } } },
  ],
});
