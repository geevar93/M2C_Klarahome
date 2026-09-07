import { Injectable, inject } from '@angular/core';
import {
  AttributeValueResponse,
  CategoryNode,
  MediaResponse,
  ProductCardResponse,
  ProductSearchItem,
  SearchSuggestion,
  SpecificationPayload,
  StorefrontOffer,
  StorefrontProduct,
  StorefrontVariant,
  StorefrontVendorResponse,
} from '@klarahome/data-access-catalog';
import { ServiceabilityResponse } from '@klarahome/data-access-delivery';
import { QuestionResponse, RatingSummaryResponse, ReviewResponse } from '@klarahome/data-access-engagement';
import { INR, Money, money } from '@klarahome/domain';
import {
  DeliveryEstimateView,
  GalleryImageView,
  OfferView,
  ProductCardView,
  QuestionView,
  RatingBreakdownView,
  ReviewView,
  SellerView,
  SpecificationGroupView,
  SuggestionView,
  VariantView,
} from '@klarahome/ui-patterns';
import { ImageSource, ImageUrls } from '@klarahome/util';

/**
 * The API's contracts, in the shapes the presentation layer draws.
 *
 * This is the seam the lint boundary describes: `ui` may not import `data-access`, so something
 * has to translate, and it is better that it be one file than forty components each reaching for
 * a different field of a different DTO. Everything here is mechanical — no decisions, no fetching,
 * no state.
 *
 * Three rules hold throughout:
 *
 *  - **Money arrives as a number and leaves as `Money`.** The currency is attached at the boundary
 *    so it cannot be lost further in; no arithmetic happens on the way through.
 *  - **An image is resolved here**, because whether a file id can become a URL is a deployment
 *    question (`ImageUrls`) and a component must not have to ask it.
 *  - **A `null` stays `null`.** An absent rating is not a zero, and an absent MRP is not the price.
 */
@Injectable({ providedIn: 'root' })
export class CatalogMapper {
  private readonly images = inject(ImageUrls);

  /** A search result row. Its price is the buy-box price of the variant the row is. */
  searchItemToCard(item: ProductSearchItem): ProductCardView {
    return {
      variantId: item.variantId,
      productId: item.productId,
      listingId: item.listingId,
      name: item.name,
      href: `/p/${item.slug}`,
      brand: item.brandName,
      price: money(item.price, item.currencyCode || INR),
      mrp: item.mrp > item.price ? money(item.mrp, item.currencyCode || INR) : null,
      image: this.images.sourceForFile(item.imageFileId, item.name),
      rating: item.ratingAverage,
      ratingCount: item.ratingCount,
      isPurchasable: item.isAvailable,
      reference: item.sku,
    };
  }

  /** A card as the CMS and the collection pages answer with. */
  productCardToCard(card: ProductCardResponse): ProductCardView {
    return {
      variantId: card.variantId,
      productId: card.productId,
      listingId: card.listingId,
      name: card.name,
      href: `/p/${card.slug}`,
      brand: card.brandName,
      price: money(card.price, card.currencyCode || INR),
      mrp: card.mrp > card.price ? money(card.mrp, card.currencyCode || INR) : null,
      image: this.images.sourceForImage(card.image, card.name),
      rating: card.ratingAverage,
      ratingCount: card.ratingCount,
      isPurchasable: card.isPurchasable,
    };
  }

  /**
   * A product's variants.
   *
   * The price on a variant is its **buy box's** price, not the variant's MRP: what the shopper
   * pays is what a seller offered, and a variant with no offer is not purchasable at all. That is
   * why `price` is nullable here and the PDP shows "currently unavailable" rather than an MRP with
   * no way to buy it.
   */
  variants(product: StorefrontProduct): VariantView[] {
    return product.variants.map((variant) => ({
      id: variant.id,
      sku: variant.sku,
      nameSuffix: variant.nameSuffix,
      netQuantity: variant.netQuantity,
      options: variant.options.map((option) => ({
        code: option.code,
        name: option.name,
        value: this.optionValue(option),
      })),
      price: variant.buyBox ? money(variant.buyBox.sellingPrice) : null,
      mrp: this.mrpOf(variant),
      isPurchasable: variant.buyBox !== null,
      images: this.gallery(variant.media.length > 0 ? variant.media : product.media, product.name),
    }));
  }

  /** The gallery for a variant, falling back to the product's own media. */
  gallery(media: readonly MediaResponse[], productName: string): GalleryImageView[] {
    return [...media]
      .filter((item) => item.kind === 'Image')
      .sort((left, right) => left.position - right.position)
      .map((item) => ({
        id: item.id,
        source: this.images.sourceForImage(
          { url: item.url, fileId: item.fileId, alt: item.altText },
          productName,
        ),
        caption: item.altText,
      }));
  }

  offer(offer: StorefrontOffer): OfferView {
    return {
      listingId: offer.listingId,
      sellerName: offer.vendorName,
      sellerHref: `/vendor/${offer.vendorSlug}`,
      sellerRating: offer.vendorRating,
      price: money(offer.sellingPrice),
      mrp: offer.mrp > offer.sellingPrice ? money(offer.mrp) : null,
      isCodAllowed: offer.isCodAllowed,
      dispatchHours: offer.dispatchHours,
      isBuyBox: offer.isBuyBox,
      maxOrderQuantity: offer.maxOrderQuantity,
    };
  }

  /**
   * The seller behind an offer.
   *
   * The return window comes from the *product* rather than from the vendor, because that is where
   * the policy is resolved (product → vendor → store, first opinion winning; Step 17) and the
   * product's answer is the one that has already been through that resolution.
   */
  seller(offer: OfferView, product: StorefrontProduct, vendor?: StorefrontVendorResponse | null): SellerView {
    return {
      name: offer.sellerName,
      href: offer.sellerHref,
      rating: offer.sellerRating,
      dispatchHours: offer.dispatchHours,
      returnWindowDays: product.isReturnable ? product.returnWindowDays : null,
      about: vendor?.about ?? null,
    };
  }

  /**
   * The specifications, grouped as the catalogue grouped them.
   *
   * A row with no group falls under "General" rather than being dropped or floated above the
   * others: a specification an editor did not categorise is still a specification.
   */
  specifications(product: StorefrontProduct): SpecificationGroupView[] {
    const groups = new Map<string, { label: string; value: string }[]>();

    const add = (group: string, label: string, value: string): void => {
      if (!value) return;
      const rows = groups.get(group) ?? [];
      rows.push({ label, value });
      groups.set(group, rows);
    };

    for (const spec of product.specifications as readonly SpecificationPayload[]) {
      add(spec.group || 'General', spec.label, spec.value);
    }

    for (const attribute of product.attributes) {
      add('Details', attribute.name, this.optionValue(attribute));
    }

    // The disclosures Indian legal metrology requires on a packaged good. They are specifications
    // in every sense a shopper cares about, and burying them elsewhere is how they get missed.
    add('Product information', 'Country of origin', product.countryOfOrigin ?? '');
    add('Product information', 'Manufacturer', this.party(product.manufacturer));
    add('Product information', 'Packer', this.party(product.packer));
    add('Product information', 'Importer', this.party(product.importer));
    add('Product information', 'Warranty', product.warranty ?? '');

    return [...groups.entries()].map(([group, rows]) => ({ group, rows }));
  }

  ratingBreakdown(summary: RatingSummaryResponse): RatingBreakdownView {
    const counts: readonly [number, number][] = [
      [5, summary.fiveStar],
      [4, summary.fourStar],
      [3, summary.threeStar],
      [2, summary.twoStar],
      [1, summary.oneStar],
    ];

    // Against the total, so the five bars are comparable to one another. Against the largest bar
    // they would all be relative to whichever star happened to win, which reads as a flatter
    // distribution than the product actually has.
    const total = Math.max(1, summary.count);

    return {
      average: summary.average,
      count: summary.count,
      bars: counts.map(([stars, count]) => ({ stars, count, share: count / total })),
    };
  }

  review(review: ReviewResponse): ReviewView {
    return {
      id: review.id,
      rating: review.rating,
      title: review.title,
      body: review.body,
      author: review.authorName,
      isVerifiedPurchase: review.isVerifiedPurchase,
      helpfulCount: review.helpfulCount,
      publishedAt: review.publishedAt,
      images: review.images
        .map((image) =>
          this.images.sourceForImage({ url: image.url, fileId: image.fileId }, 'Customer photo'),
        )
        .filter((image): image is ImageSource => image !== null),
      sellerReply: review.vendorReply,
    };
  }

  question(question: QuestionResponse): QuestionView {
    return {
      id: question.id,
      body: question.body,
      author: question.authorName,
      publishedAt: question.publishedAt,
      answers: question.answers.map((answer) => ({
        id: answer.id,
        body: answer.body,
        author: answer.authorName,
        authorType: answer.authorType,
        publishedAt: answer.publishedAt,
      })),
    };
  }

  delivery(response: ServiceabilityResponse): DeliveryEstimateView {
    return {
      pincode: response.pincode,
      // Both have to be true, and they are different facts: `covered` is whether this deployment
      // ships to the area at all (Step 16A's delivery coverage), `isServiceable` is whether a
      // courier will carry it. A shopper only needs the conjunction; the message says which.
      deliverable: response.deliverable,
      etaDays: response.etaDays,
      codAvailable: response.codOk,
      place: [response.city, response.state].filter(Boolean).join(', ') || null,
      message: response.message ?? response.reason,
    };
  }

  /**
   * An autocomplete row.
   *
   * The `kind` decides where selecting it goes, and a suggestion whose kind this build does not
   * know about becomes a plain query — which always works, because searching for its text is what
   * the shopper would have done anyway.
   */
  suggestion(suggestion: SearchSuggestion): SuggestionView {
    const kind = suggestion.kind.toLowerCase();

    switch (kind) {
      case 'product':
        return {
          kind: 'product',
          text: suggestion.text,
          href: `/p/${suggestion.slug ?? ''}`,
          image: this.images.sourceForFile(suggestion.imageFileId, suggestion.text),
          price: suggestion.price === null ? null : money(suggestion.price),
        };
      case 'category':
        return {
          kind: 'category',
          text: suggestion.text,
          href: `/c/${suggestion.slug ?? ''}`,
          image: null,
          price: null,
        };
      case 'brand':
        return {
          kind: 'brand',
          text: suggestion.text,
          href: `/search?q=${encodeURIComponent(suggestion.text)}`,
          image: null,
          price: null,
        };
      default:
        return {
          kind: 'query',
          text: suggestion.text,
          href: `/search?q=${encodeURIComponent(suggestion.text)}`,
          image: null,
          price: null,
        };
    }
  }

  /**
   * The path from the root of the taxonomy down to a category, for the breadcrumb.
   *
   * Walked over the cached tree rather than requested: the API answers the whole tree in one
   * document, and four requests to name four ancestors would be four round trips on the page
   * whose LCP budget is tightest.
   */
  categoryPath(tree: readonly CategoryNode[], slug: string): CategoryNode[] {
    return this.walk(tree, (node) => node.slug === slug);
  }

  /**
   * The same path, found by identifier.
   *
   * The PDP needs this one: a product carries its `categoryId` and no slug, and the breadcrumb has
   * to link to `/c/<slug>`.
   */
  categoryPathById(tree: readonly CategoryNode[], id: string): CategoryNode[] {
    return this.walk(tree, (node) => node.id === id);
  }

  private walk(tree: readonly CategoryNode[], matches: (node: CategoryNode) => boolean): CategoryNode[] {
    const search = (nodes: readonly CategoryNode[], trail: CategoryNode[]): CategoryNode[] | null => {
      for (const node of nodes) {
        const path = [...trail, node];
        if (matches(node)) return path;
        const found = search(node.children, path);
        if (found) return found;
      }
      return null;
    };

    return search(tree, []) ?? [];
  }

  /** A `Money` for a plain amount in the store's currency. */
  amount(value: number): Money {
    return money(value);
  }

  /**
   * The MRP printed on a variant, or null when it is not above what is being charged.
   *
   * The variant's own MRP wins over the offer's: the maximum retail price is a property of the
   * packaged good, and a seller quoting a different one is quoting their list price.
   */
  private mrpOf(variant: StorefrontVariant): Money | null {
    const price = variant.buyBox?.sellingPrice ?? null;
    const mrp = variant.mrp || variant.buyBox?.mrp || 0;
    if (price === null || mrp <= price) return null;
    return money(mrp);
  }

  /** An attribute's displayable value — the option's text, or the raw value for a free field. */
  private optionValue(attribute: AttributeValueResponse): string {
    const value = attribute.value ?? '';
    return attribute.unit ? `${value} ${attribute.unit}`.trim() : value;
  }

  /** A legal-metrology party, as one line. */
  private party(party: { name: string | null; address: string | null } | null | undefined): string {
    if (!party) return '';
    return [party.name, party.address].filter(Boolean).join(', ');
  }
}
