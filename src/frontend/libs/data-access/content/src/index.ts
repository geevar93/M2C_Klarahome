export * from './lib/reference-data.service';
export * from './lib/store-config.service';
export * from './lib/store-content.service';

/**
 * The shapes these services answer with.
 *
 * The lint boundary stops an app importing `@klarahome/data-access-api` — and it should: an app
 * has no business with the transport, the interceptors or a client it could call directly. But an
 * app that renders a CMS page unavoidably handles the type that page arrives as, and the
 * alternative is a hand-written view model per DTO, which is exactly the duplication Step 22
 * removed when the client became generated.
 *
 * So the seam is: **a feature library re-exports the contract it answers with, and nothing else.**
 * Types only — no client, no transport.
 */
export type {
  CategoryTileResponse,
  ContentImageResponse,
  PincodeResponse,
  ProductCardResponse,
  SeoConfigResponse,
  SeoResponse,
  StateResponse,
  StoreBannerResponse,
  StoreBlockResponse,
  StoreCollectionResponse,
  StoreMenuItemResponse,
  StoreMenuResponse,
  StorePageResponse,
} from '@klarahome/data-access-api';
