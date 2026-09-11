import { defineConfig, devices } from '@playwright/test';

/**
 * Visual-regression config — Step 30.
 *
 * Deliberately separate from `playwright.config.mts`: that config boots its own dev server via
 * `nx run storefront:serve` (Angular's dev build, `optimization: false`, no runtime theme
 * tokens applied the way `ThemeService`/`ShellStore.initialise()` apply them against real
 * settings data). A visual baseline taken against that server would not be a baseline of the
 * themed product — it would be a baseline of the placeholder. This config instead points at the
 * already-running docker dev stack (`infra/compose/docker-compose.dev.yml`, storefront container
 * behind Traefik), which is the themed build the client signed off on
 * (docs/steps/step-30-design-system-theming-and-visual-identity.md).
 *
 * Run:
 *   npx nx run storefront-e2e:visual-regression              # check against the committed baselines
 *   npx nx run storefront-e2e:visual-regression -- --update-snapshots   # accept a new baseline
 *
 * Update a baseline only when the visual change is intentional — review the diff PNG Playwright
 * writes under test-output/ first.
 */
const baseURL = process.env['BASE_URL'] || 'https://klarahome.localhost';

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
      // A themed page still carries live catalog imagery and a clock-driven "recently viewed"
      // rail; a small per-pixel tolerance absorbs that without hiding a real layout/colour
      // regression, which moves far more than a handful of pixels.
      maxDiffPixelRatio: 0.02,
      animations: 'disabled',
    },
  },
  use: {
    baseURL,
    ignoreHTTPSErrors: true, // the dev stack's Traefik cert is self-signed
  },
  projects: [
    {
      // Chromium's mobile emulation (Pixel 5), not iPhone/webkit — this workspace's existing
      // e2e config (apps/storefront-e2e/playwright.config.mts) makes the same choice, and it
      // keeps this suite to the browser already installed for CI rather than requiring webkit.
      name: 'mobile',
      use: { ...devices['Pixel 5'] },
    },
    {
      name: 'desktop',
      use: { viewport: { width: 1280, height: 900 } },
    },
  ],
});
