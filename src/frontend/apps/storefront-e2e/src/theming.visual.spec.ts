import { expect, test } from '@playwright/test';

/**
 * Visual-regression baselines for the storefront — Step 30.
 *
 * Small and deliberate: this proves the `toHaveScreenshot()` mechanism exists and has real,
 * committed baselines against the themed dev stack, not exhaustive page coverage. Four screens,
 * each at the mobile and desktop breakpoints declared in `playwright.visual.config.mts` (the
 * two ends of the range docs/05-frontend-architecture.md section 7 cares about) — 8 baselines
 * here, plus the admin app's 2 in `apps/admin-e2e`, is the "8-12 screenshots total" this
 * deliverable asked for.
 *
 * Update a baseline: `npx nx run storefront-e2e:visual-regression -- --update-snapshots`, review
 * the new PNGs actually changed for the reason you expect, then commit them.
 */

test.describe('storefront visual baseline', () => {
  test('home', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('main#main-content')).toBeAttached();
    await expect(page).toHaveScreenshot('home.png', { fullPage: true });
  });

  test('category listing', async ({ page }) => {
    await page.goto('/c/cushions');
    await expect(page.locator('main#main-content')).toBeAttached();
    await expect(page).toHaveScreenshot('category-listing.png', { fullPage: true });
  });

  test('product detail page', async ({ page }) => {
    await page.goto('/p/linen-cushion-cover');
    await expect(page.locator('main#main-content')).toBeAttached();
    await expect(page).toHaveScreenshot('pdp.png', { fullPage: true });
  });

  test('checkout', async ({ page }) => {
    await page.goto('/checkout');
    await expect(page.locator('main#main-content')).toBeAttached();
    await expect(page).toHaveScreenshot('checkout.png', { fullPage: true });
  });
});
