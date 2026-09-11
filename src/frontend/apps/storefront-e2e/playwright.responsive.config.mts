import { defineConfig } from '@playwright/test';

/**
 * Responsive visual QA — Step 30, closing the last open deliverable.
 *
 * Deliberately a third config, not a third project bolted onto `playwright.visual.config.mts`:
 * that config's job is pixel-diffing two screens against committed baselines. This one's job is
 * different in kind — sweep every route across the breakpoint matrix and assert there is no
 * horizontal overflow, then screenshot the known risk areas (filter panel, mobile nav, cart) for
 * a human to actually look at. Mixing the two would have made the visual-regression baselines
 * four times bigger for no pixel-diff benefit, and made this sweep's overflow assertions live
 * inside a config whose whole point is "don't run unless you mean to update a baseline".
 *
 * Points at the same themed docker dev stack as the visual-regression config, for the same
 * reason: `nx run storefront:serve` never applies a tenant's runtime theme tokens.
 *
 * Breakpoints are not invented for this suite — they are the ones this repo already committed
 * to:
 *  - 360×640  — docs/05-frontend-architecture.md §3.3, "design and build at 360×640 first",
 *               and the exact viewport docs/09-nfr-testing-observability.md's journey #16 names.
 *  - 768×1024 — the `md` token in the same §3.3 breakpoint list.
 *  - 1024×900 — the `lg` token — the one that matters most here, because §3.3 says the filter
 *               panel switches from a bottom sheet to a sidebar exactly at `lg`.
 *  - 1280×900 — the `xl` token, and the same width the `desktop` project already screenshots in
 *               `playwright.visual.config.mts`, so this sweep and the pixel baselines agree on
 *               what "desktop" means.
 *
 * Run: npx nx run storefront-e2e:responsive-qa
 */
const baseURL = process.env['BASE_URL'] || 'https://klarahome.localhost';

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
