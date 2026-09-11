// =============================================================================
// Klara Home - admin theme-token check (Step 30 white-label proof, admin half)
// =============================================================================
// The admin app is CSR-only (no `server` build target - docs/05-frontend-architecture.md §4.1),
// so there is no server-rendered HTML to grep the way `verify-white-label-theming.sh` greps the
// storefront's. What "proven" means here is necessarily different in kind: this loads the admin's
// sign-in screen in a real (headless) browser, lets `provideAppInitializer` run - the same hook
// that sets `AuthService.surface` - and asserts the tenant's tokens are on `document.documentElement`
// after bootstrap, before any route below `/login` has rendered.
//
// Lives in `src/frontend/scripts/`, not `infra/scripts/`, so Node's ESM resolution finds the
// `playwright` devDependency by walking up from this file to `src/frontend/node_modules` - it would
// not if this file lived outside that tree, regardless of the process's cwd.
//
// Run from anywhere (needs `src/frontend`'s `playwright` devDependency installed):
//   node src/frontend/scripts/check-admin-theme-tokens.mjs <url> '<json of expected --token: value>'
//
// An empty object as the second argument asserts NO inline style landed (the default-branding case).
// =============================================================================
import { chromium } from 'playwright';

const url = process.argv[2];
const expected = JSON.parse(process.argv[3] ?? '{}');

if (!url) {
  console.error('usage: node check-admin-theme-tokens.mjs <url> <expected-json>');
  process.exit(2);
}

const browser = await chromium.launch();
try {
  const page = await browser.newPage({ ignoreHTTPSErrors: true });
  await page.goto(url, { waitUntil: 'networkidle' });
  // `networkidle` covers the `/store/config` request; the initializer's `.subscribe` callback that
  // calls `ThemeService.apply` runs in the same tick the response resolves, but give the zoneless
  // change-detection + style write a beat regardless rather than racing it.
  await page.waitForTimeout(1000);
  const style = await page.evaluate(() => document.documentElement.getAttribute('style'));

  const expectedEntries = Object.entries(expected);
  if (expectedEntries.length === 0) {
    if (style) {
      console.error(`FAIL: expected no inline style on <html>, found: ${style}`);
      process.exit(1);
    }
    console.log('PASS: <html> carries no theme-token override (default branding)');
    process.exit(0);
  }

  for (const [key, value] of expectedEntries) {
    if (!style || !style.includes(`${key}: ${value}`)) {
      console.error(`FAIL: <html style> missing "${key}: ${value}" (got: ${style ?? '(none)'})`);
      process.exit(1);
    }
  }
  console.log(`PASS: <html> carries the tenant's theme tokens after bootstrap (${style})`);
} finally {
  await browser.close();
}
