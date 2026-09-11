import { expect, test } from '@playwright/test';

/**
 * Visual-regression baselines for the admin app — Step 30. See the note on
 * `playwright.visual.config.mts` for why this covers only the unauthenticated screens.
 */

test.describe('admin visual baseline', () => {
  test('sign in', async ({ page }) => {
    await page.goto('/login');
    await expect(page.locator('main.pane')).toBeAttached();
    await expect(page).toHaveScreenshot('sign-in.png', { fullPage: true });
  });

  test('forgot password', async ({ page }) => {
    await page.goto('/forgot-password');
    await expect(page.locator('main.pane')).toBeAttached();
    await expect(page).toHaveScreenshot('forgot-password.png', { fullPage: true });
  });
});
