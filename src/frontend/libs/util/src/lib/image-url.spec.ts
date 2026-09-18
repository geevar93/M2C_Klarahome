import { usableVariants } from './image-url';

/**
 * Which WebP renditions reach a `srcset`.
 *
 * Worth a test because a wrong answer is silent: a `w` descriptor that overstates a file makes the
 * browser pick a blurred image, and a list that keeps the original out entirely is only noticed
 * when an SEO audit reports 600KB JPEGs on the home page again.
 */
describe('usableVariants', () => {
  const variant = (name: string, width: number) => ({ name, width, url: `https://cdn/${name}.webp` });

  it('drops the admin thumbnail and sorts narrowest first', () => {
    expect(usableVariants([variant('large', 1600), variant('thumb', 160), variant('small', 480)], 2880)).toEqual([
      variant('small', 480),
      variant('large', 1600),
    ]);
  });

  it('re-describes the first rendition wider than the original at the original width', () => {
    expect(
      usableVariants([variant('small', 480), variant('medium', 960), variant('large', 1600)], 679),
    ).toEqual([variant('small', 480), { ...variant('medium', 960), width: 679 }]);
  });

  it('keeps every rendition when the original size is unknown', () => {
    expect(usableVariants([variant('small', 480), variant('medium', 960)], null)).toHaveLength(2);
  });

  it('answers nothing for an image with no renditions', () => {
    expect(usableVariants(undefined, 1024)).toEqual([]);
    expect(usableVariants([], 1024)).toEqual([]);
  });
});
