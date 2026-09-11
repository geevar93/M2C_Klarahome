import { expect, test } from '@playwright/test';

/**
 * CMS block responsiveness under hostile content — the gap `responsive-qa.responsive.spec.ts`
 * named and left open: it swept nine routes against the *seeded demo content*, which a real
 * merchant will never be limited to. Every block type (`hero`, `bannerGrid`, `productCarousel`,
 * `categoryTiles`, `richText`, `faq`, `testimonial`) is exercised here against long unbroken
 * words and URLs, merchant-configured column counts from 1 to 6 crossed with item counts from 1
 * to 12, an 8-column table, every hero `align` value, a hero with no image, and testimonials of
 * wildly different lengths — see `apps/storefront/src/app/pages/dev/cms-stress-test.page.ts` for
 * the fixture itself and why it is a route rather than an intercepted API response.
 *
 * Two checks, same as the rest of this suite: the cheap, exhaustive one (no horizontal overflow,
 * at every breakpoint) and a full-page screenshot per breakpoint, saved under
 * `test-output/responsive-review/`, so the layout was actually looked at rather than only
 * overflow-asserted.
 */

const path = '/__test/cms-blocks';

test('CMS block stress fixture has no horizontal overflow', async ({ page }, testInfo) => {
  await page.goto(path);
  await page.waitForLoadState('networkidle');

  const overflow = await page.evaluate(() => {
    const doc = document.documentElement;
    return { scrollWidth: doc.scrollWidth, clientWidth: doc.clientWidth };
  });

  expect(
    overflow.scrollWidth,
    `CMS block stress fixture at ${testInfo.project.name}: document is ${overflow.scrollWidth}px wide ` +
      `but the viewport is only ${overflow.clientWidth}px — something overflows horizontally`,
  ).toBeLessThanOrEqual(overflow.clientWidth);
});

/**
 * A hero's copy is laid over its photograph, so the box can clip it vertically — which the
 * horizontal-overflow check above cannot see, and which is exactly what the long-headline hero did
 * when this assertion was added: the box held the image's aspect ratio, and the subheadline and
 * CTA were cut off at the bottom edge at every breakpoint. The copy has to sit wholly inside its
 * box, and must not be scrolled or clipped inside itself either.
 */
test('CMS block stress fixture never clips a hero’s copy', async ({ page }, testInfo) => {
  await page.goto(path);
  await page.waitForLoadState('networkidle');

  const clipped = await page.evaluate(() =>
    Array.from(document.querySelectorAll<HTMLElement>('.hero-copy')).flatMap((copy, index) => {
      const box = copy.closest<HTMLElement>('.hero-media') ?? copy.closest<HTMLElement>('.hero');
      if (!box) return [];
      const inner = copy.getBoundingClientRect();
      const outer = box.getBoundingClientRect();
      const problems: string[] = [];
      if (inner.top < outer.top - 1) problems.push(`starts ${Math.round(outer.top - inner.top)}px above its box`);
      if (inner.bottom > outer.bottom + 1) {
        problems.push(`ends ${Math.round(inner.bottom - outer.bottom)}px below its box`);
      }
      if (copy.scrollHeight > copy.clientHeight + 1) problems.push('is clipped inside itself');
      return problems.map((problem) => `hero #${index + 1}: copy ${problem}`);
    }),
  );

  expect(clipped, `hero copy clipped at ${testInfo.project.name}`).toEqual([]);
});

test('CMS block stress fixture, looked at', async ({ page }) => {
  await page.goto(path);
  await page.waitForLoadState('networkidle');
  await page.screenshot({
    path: `test-output/responsive-review/cms-blocks-stress-${test.info().project.name}.png`,
    fullPage: true,
  });
});
