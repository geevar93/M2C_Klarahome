export * from './lib/product-search.service';
export * from './lib/store-catalog.service';

/**
 * The shapes these services answer with.
 *
 * The same seam as `data-access-content`: a feature library re-exports the contract it answers
 * with, and nothing else. Types only — no client, no transport. A page that renders a product
 * unavoidably handles the type the product arrives as, and the alternative is a hand-written view
 * model per DTO, which is the duplication Step 22 removed when the client became generated.
 */
export type {
  AttributeValueResponse,
  CategoryNode,
  CategoryResponse,
  FacetGroup,
  FacetValue,
  MediaResponse,
  PartyPayload,
  ProductCardResponse,
  ProductSearchItem,
  ProductSearchResponse,
  SearchSuggestion,
  SeoPayload,
  SpecificationPayload,
  StorefrontOffer,
  StorefrontProduct,
  StorefrontVariant,
  StorefrontVendorResponse,
  SuggestionResponse,
} from '@klarahome/data-access-api';
