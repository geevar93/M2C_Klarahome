import { expect, test } from '@playwright/test';

/**
 * Responsive visual QA — Step 30. See the storefront's `responsive.responsive.spec.ts` for the
 * method (overflow assertion + risk-area screenshot). Coverage here is the two screens reachable
 * without a `platform-admin` session — see `playwright.responsive.config.mts` for why the
 * authenticated dashboard/data-table/form/modal screens are out of reach this turn.
 */

const routes: { name: string; path: string }[] = [
  { name: 'sign-in', path: '/login' },
  { name: 'forgot-password', path: '/forgot-password' },
];

for (const route of routes) {
  test(`${route.name} has no horizontal overflow`, async ({ page }, testInfo) => {
    await page.goto(route.path);
    await expect(page.locator('main.pane')).toBeAttached();
    await page.waitForLoadState('networkidle');

    const overflow = await page.evaluate(() => {
      const doc = document.documentElement;
      return { scrollWidth: doc.scrollWidth, clientWidth: doc.clientWidth };
    });

    expect(
      overflow.scrollWidth,
      `${route.name} at ${testInfo.project.name}: document is ${overflow.scrollWidth}px wide ` +
        `but the viewport is only ${overflow.clientWidth}px — something overflows horizontally`,
    ).toBeLessThanOrEqual(overflow.clientWidth);
  });
}
