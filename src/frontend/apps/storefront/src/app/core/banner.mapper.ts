import { StoreBannerResponse } from '@klarahome/data-access-content';
import { BannerView } from '@klarahome/ui-patterns';

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
  return {
    id: banner.id,
    imageUrl: banner.image?.url ?? null,
    mobileImageUrl: banner.mobileImage?.url ?? banner.image?.url ?? null,
    message: banner.message,

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
