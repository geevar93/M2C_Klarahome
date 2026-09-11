import { Money } from '@klarahome/domain';
import { ImageSource } from '@klarahome/util';

/**
 * The discovery surface's view models.
 *
 * The same reasoning as `navigation.model.ts`, and it matters more here than anywhere else in the
 * application: the PLP and the PDP consume nine different API contracts between them, and a
 * presentation layer that took those types directly could not be rendered without a mock server,
 * would change shape every time a backend module added a field, and would have to know which of
 * three endpoints a price came from. Everything below is what a component needs to draw itself —
 * the app maps the responses on the way in (`core/catalog.mapper.ts`).
 *
 * Two conventions run through all of it:
 *
 *  - **Money is `Money`, never a number and never a formatted string.** The currency travels with
 *    the amount, and `khMoney` stays the only thing that knows how to write ₹1,23,456.
 *  - **An image is an `ImageSource`, already resolved.** Which URL to build, and whether one can
 *    be built at all, is a deployment question (`ImageUrls`), not a component's.
 */

/** A product as a grid, rail or drawer shows it. */
export interface ProductCardView {
  /** The variant — what is added to a cart, and what a wishlist saves. */
  readonly variantId: string;
  readonly productId: string;
  /** The listing whose offer won the buy box. Absent when nothing is purchasable. */
  readonly listingId: string | null;
  readonly name: string;
  /** The PDP path, ready for `routerLink`. */
  readonly href: string;
  readonly brand: string | null;
  /** Null when there is no offer to quote — the card omits the price rather than showing ₹0. */
  readonly price: Money | null;
  readonly mrp: Money | null;
  readonly image: ImageSource | null;
  readonly rating: number | null;
  readonly ratingCount: number;
  /** False when the buy box is empty or the seller is out of stock: the card says so. */
  readonly isPurchasable: boolean;
  /** SKU or another short string, shown inside the placeholder box while there is no image. */
  readonly reference?: string | null;
}

/** One selectable value inside a facet. */
export interface FacetValueView {
  readonly value: string;
  readonly label: string;
  readonly count: number;
  readonly selected: boolean;
}

/** A facet group — brand, price band, rating, or an attribute a merchandiser invented. */
export interface FacetGroupView {
  /** The query-string key this group writes: `brand`, `attr.colour`. */
  readonly key: string;
  readonly label: string;
  readonly values: readonly FacetValueView[];
  readonly selectedCount: number;
}

/** A filter the shopper has applied, as the removable row above the results shows it. */
export interface AppliedFilterView {
  readonly key: string;
  readonly value: string;
  readonly label: string;
}

/** One entry in the sort control. */
export interface SortOptionView {
  readonly value: string;
  readonly label: string;
}

/** An image in the PDP gallery. */
export interface GalleryImageView {
  readonly id: string;
  readonly source: ImageSource | null;
  readonly caption: string | null;
}

/** One axis of variant choice — "Colour", "Size" — and the values available on it. */
export interface VariantAxisView {
  readonly code: string;
  readonly name: string;
  readonly values: readonly VariantAxisValueView[];
}

export interface VariantAxisValueView {
  readonly value: string;
  readonly label: string;
  /** False when no variant with this value can be bought; it is shown, struck, not hidden. */
  readonly available: boolean;
  readonly selected: boolean;
}

/** A purchasable variant of a product. */
export interface VariantView {
  readonly id: string;
  readonly sku: string;
  /** "Beige / Large" — appended to the product name in the title and the cart. */
  readonly nameSuffix: string | null;
  readonly netQuantity: string | null;
  readonly options: readonly { readonly code: string; readonly name: string; readonly value: string }[];
  readonly price: Money | null;
  readonly mrp: Money | null;
  readonly isPurchasable: boolean;
  readonly images: readonly GalleryImageView[];
}

/** A seller's offer on a variant, as the "other sellers" list shows it. */
export interface OfferView {
  readonly listingId: string;
  readonly sellerName: string;
  readonly sellerHref: string;
  readonly sellerRating: number | null;
  readonly price: Money;
  readonly mrp: Money | null;
  readonly isCodAllowed: boolean;
  readonly dispatchHours: number;
  readonly isBuyBox: boolean;
  readonly maxOrderQuantity: number | null;
}

/** What a PIN code check came back with. */
export interface DeliveryEstimateView {
  readonly pincode: string;
  readonly deliverable: boolean;
  readonly etaDays: number | null;
  readonly codAvailable: boolean;
  readonly place: string | null;
  /** The API's own sentence when it refuses; shown verbatim rather than reworded. */
  readonly message: string | null;
}

/** The seller behind the buy box. */
export interface SellerView {
  readonly name: string;
  readonly href: string;
  readonly rating: number | null;
  readonly dispatchHours: number;
  readonly returnWindowDays: number | null;
  readonly about: string | null;
}

/** A row of the specifications table. */
export interface SpecificationGroupView {
  readonly group: string;
  readonly rows: readonly { readonly label: string; readonly value: string }[];
}

/** The star histogram beside a product's average. */
export interface RatingBreakdownView {
  readonly average: number | null;
  readonly count: number;
  /** Five entries, five stars first. `share` is the fraction of the bar to paint. */
  readonly bars: readonly { readonly stars: number; readonly count: number; readonly share: number }[];
}

export interface ReviewView {
  readonly id: string;
  readonly rating: number;
  readonly title: string | null;
  readonly body: string | null;
  readonly author: string | null;
  readonly isVerifiedPurchase: boolean;
  readonly helpfulCount: number;
  readonly publishedAt: string | null;
  readonly images: readonly ImageSource[];
  readonly sellerReply: string | null;
}

export interface QuestionView {
  readonly id: string;
  readonly body: string;
  readonly author: string | null;
  readonly publishedAt: string | null;
  readonly answers: readonly {
    readonly id: string;
    readonly body: string;
    readonly author: string | null;
    readonly authorType: string;
    readonly publishedAt: string | null;
  }[];
}

/**
 * A CMS block, reduced to the eight things the storefront can draw.
 *
 * A discriminated union rather than the API's `{ type, config: unknown }`, because a template
 * cannot narrow `unknown` and a renderer that cast it would be one editor's typo away from a
 * blank home page. The mapping is one function in the app, and a type it does not recognise
 * becomes `unknown` here — which renders nothing rather than crashing.
 */
export type CmsBlockView =
  | {
      readonly kind: 'hero';
      readonly id: string;
      readonly headline: string;
      readonly subheadline: string | null;
      readonly image: ImageSource | null;
      readonly ctaLabel: string | null;
      readonly ctaHref: string | null;
      readonly align: 'left' | 'centre' | 'right';
    }
  | {
      readonly kind: 'bannerGrid';
      readonly id: string;
      readonly heading: string | null;
      readonly columns: number;
      readonly items: readonly {
        readonly image: ImageSource | null;
        readonly caption: string | null;
        readonly href: string | null;
      }[];
    }
  | {
      readonly kind: 'productCarousel';
      readonly id: string;
      readonly heading: string | null;
      readonly viewAllHref: string | null;
      readonly products: readonly ProductCardView[];
    }
  | {
      readonly kind: 'categoryTiles';
      readonly id: string;
      readonly heading: string | null;
      readonly columns: number;
      readonly items: readonly {
        readonly label: string;
        readonly href: string;
        readonly image: ImageSource | null;
      }[];
    }
  | {
      readonly kind: 'richText';
      readonly id: string;
      readonly heading: string | null;
      readonly html: string;
      readonly width: 'narrow' | 'wide' | 'full';
    }
  | {
      readonly kind: 'faq';
      readonly id: string;
      readonly heading: string | null;
      readonly items: readonly { readonly question: string; readonly answer: string }[];
    }
  | {
      readonly kind: 'testimonial';
      readonly id: string;
      readonly heading: string | null;
      readonly items: readonly {
        readonly quote: string;
        readonly author: string;
        readonly location: string | null;
        readonly rating: number | null;
      }[];
    }
  | { readonly kind: 'unknown'; readonly id: string; readonly type: string };

/** What the search box offers while the shopper types. */
export interface SuggestionView {
  readonly kind: 'product' | 'query' | 'brand' | 'category';
  readonly text: string;
  /** Where selecting it goes. Already a path, so the box does not build URLs. */
  readonly href: string;
  readonly image: ImageSource | null;
  readonly price: Money | null;
}
