import { expect, test } from '@playwright/test';

/**
 * The admin's accessibility floor, asserted in a real browser.
 *
 * There is no visible UI to test yet — the header, navigation and pages arrive at Step 26 — but
 * the landmarks and the skip link are the contract every one of those steps builds on, and they
 * are the part that is silently easy to break. Checking them here means a regression shows up as
 * a failing test rather than as a keyboard user who cannot reach the content.
 */
test('lands keyboard focus on a skip link that reaches the main landmark', async ({ page }) => {
  await page.goto('/');

  const main = page.locator('main#main-content');
  await expect(main).toBeAttached();

  // The skip link must be the first thing the keyboard reaches, before any header.
  await page.keyboard.press('Tab');
  const skipLink = page.locator('a.kh-skip-link');
  await expect(skipLink).toBeFocused();
  await expect(skipLink).toHaveAttribute('href', '#main-content');
});
