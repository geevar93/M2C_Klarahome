import { ContentImageResponse, StoreBannerResponse } from '@klarahome/data-access-content';
import { BannerView } from '@klarahome/ui-patterns';
import { usableVariants } from '@klarahome/util';

/**
 * The API's banner, as the storefront's components take it.
 *
 * A function rather than an injectable, because there is nothing to inject: the images already
 * carry their resolved URLs, and turning a link into a router path is string work. The one decision
 * it makes is the fallback — a banner with only a desktop image serves that image on a phone too,
 * rather than nothing, because a slightly wrong crop is a banner and no banner is not.
 *
 * Built at Step 20 and rendered nowhere until Step 28B (deliverable 19).
 */
export function toBannerView(banner: StoreBannerResponse): BannerView {
  const mobile = banner.mobileImage?.url ? banner.mobileImage : banner.image;

  return {
    id: banner.id,
    imageUrl: widestFor(banner.image),
    imageSrcset: srcsetFor(banner.image),
    imageWidth: banner.image?.width ?? null,
    imageHeight: banner.image?.height ?? null,
    mobileImageUrl: widestFor(mobile),
    mobileImageSrcset: srcsetFor(mobile),
    mobileImageWidth: mobile?.width ?? null,
    mobileImageHeight: mobile?.height ?? null,
    message: banner.message,
    marquee: banner.isMarquee,

    // The banner's own alt text wins; the image's is the fallback, and an empty string is a real
    // answer meaning "decorative" rather than a missing one.
    altText: banner.altText ?? banner.image?.alt ?? '',
    link: banner.link,
    ctaLabel: banner.ctaLabel,
  };
}

/** The whole placement, mapped. */
export function toBannerViews(banners: readonly StoreBannerResponse[]): BannerView[] {
  return banners.map(toBannerView);
}

/**
 * The WebP renditions the media module made of an image, as a `srcset`.
 *
 * A hero is the largest picture on the home page and its original is whatever the designer
 * exported — a 2880px PNG, typically. Offering the renditions lets a phone take the 480px WebP
 * instead of the full file. Empty when there are none, and the `<img>` falls back to the original.
 */
function srcsetFor(image: ContentImageResponse | null | undefined): string {
  if (!image) return '';
  return usableVariants(image.variants, image.width)
    .map((variant) => `${variant.url} ${variant.width}w`)
    .join(', ');
}

/** The widest rendition, or the original when nothing was rendered — the `src` a `srcset` sits on. */
function widestFor(image: ContentImageResponse | null | undefined): string | null {
  if (!image?.url) return null;
  return usableVariants(image.variants, image.width).at(-1)?.url ?? image.url;
}
