/**
 * One banner, as the storefront renders it.
 *
 * The API's own shape minus the placement: a component is already rendering one placement's
 * banners, and carrying the name of the placement into every row would be a field nothing reads.
 *
 * `image` and `message` are both optional because the two kinds of banner are genuinely different:
 * an announcement bar is a line of text, and every other placement is a picture. The API enforces
 * that a banner has whichever of them its placement needs (Step 20); this shape carries both and
 * the components render what is there.
 */
export interface BannerView {
  readonly id: string;
  /** The desktop image, when this is a picture banner. */
  readonly imageUrl?: string | null;
  /** The mobile image, where a different crop was uploaded. */
  readonly mobileImageUrl?: string | null;
  /** The words, for an announcement bar. */
  readonly message?: string | null;
  /** Whether an announcement bar scrolls its words across the strip instead of centring them. */
  readonly marquee?: boolean;
  /** Alt text. Empty is correct for a purely decorative banner and is not the same as absent. */
  readonly altText?: string | null;
  /** Where clicking it goes, as a router path. */
  readonly link?: string | null;
  /** The call to action's wording, when the banner has a button. */
  readonly ctaLabel?: string | null;
}
