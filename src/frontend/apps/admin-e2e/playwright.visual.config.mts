import { defineConfig, devices } from '@playwright/test';

/**
 * Visual-regression config — Step 30. See the equivalent storefront config
 * (`apps/storefront-e2e/playwright.visual.config.mts`) for why this points at the docker dev
 * stack rather than an `nx serve` dev-build server.
 *
 * The admin's screens behind `authenticatedGuard` (dashboard, any data table) require a
 * `platform-admin` sign-in, and that role is mandatory-two-factor (docs/dev-setup.md, "Signing
 * in locally") — scripting a TOTP enrolment into a Playwright fixture was out of scope for this
 * turn. This suite baselines the two screens actually reachable without a session: sign-in and
 * forgot-password. Baselining an authenticated dashboard/table screen is left for whoever adds a
 * scripted-2FA test fixture (deferred, not silently dropped — see the step doc's Outcome/Notes).
 */
const baseURL = process.env['BASE_URL'] || 'https://admin.klarahome.localhost';

export default defineConfig({
  testDir: './src',
  testMatch: '**/*.visual.spec.ts',
  snapshotPathTemplate: '{testDir}/__screenshots__/{testFilePath}/{projectName}/{arg}{ext}',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  reporter: [['list']],
  expect: {
    toHaveScreenshot: {
      maxDiffPixelRatio: 0.02,
      animations: 'disabled',
    },
  },
  use: {
    baseURL,
    ignoreHTTPSErrors: true,
  },
  projects: [
    {
      name: 'mobile',
      use: { ...devices['Pixel 5'] },
    },
    {
      name: 'desktop',
      use: { viewport: { width: 1280, height: 900 } },
    },
  ],
});
