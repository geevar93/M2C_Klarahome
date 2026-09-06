/**
 * DO NOT EDIT. Generated from the API's OpenAPI document by tools/generate-api-client.mjs.
 *
 * Regenerate with:  pwsh tools/generate-api-client.ps1
 * CI fails if this file differs from what the current API produces.
 */
/* eslint-disable */

import { CartApiClient } from './services/cart.api-client';
import { CatalogApiClient } from './services/catalog.api-client';
import { CheckoutApiClient } from './services/checkout.api-client';
import { ContentApiClient } from './services/content.api-client';
import { IdentityApiClient } from './services/identity.api-client';
import { InventoryApiClient } from './services/inventory.api-client';
import { MediaApiClient } from './services/media.api-client';
import { MetaApiClient } from './services/meta.api-client';
import { NotificationsApiClient } from './services/notifications.api-client';
import { OrdersApiClient } from './services/orders.api-client';
import { PaymentsApiClient } from './services/payments.api-client';
import { PlatformApiClient } from './services/platform.api-client';
import { PricingApiClient } from './services/pricing.api-client';
import { ReportingApiClient } from './services/reporting.api-client';
import { ReturnsApiClient } from './services/returns.api-client';
import { ReviewsApiClient } from './services/reviews.api-client';
import { SearchApiClient } from './services/search.api-client';
import { SettlementsApiClient } from './services/settlements.api-client';
import { ShippingApiClient } from './services/shipping.api-client';
import { VendorsApiClient } from './services/vendors.api-client';
import { WebhooksApiClient } from './services/webhooks.api-client';

export * from './models';
export * from './services/cart.api-client';
export * from './services/catalog.api-client';
export * from './services/checkout.api-client';
export * from './services/content.api-client';
export * from './services/identity.api-client';
export * from './services/inventory.api-client';
export * from './services/media.api-client';
export * from './services/meta.api-client';
export * from './services/notifications.api-client';
export * from './services/orders.api-client';
export * from './services/payments.api-client';
export * from './services/platform.api-client';
export * from './services/pricing.api-client';
export * from './services/reporting.api-client';
export * from './services/returns.api-client';
export * from './services/reviews.api-client';
export * from './services/search.api-client';
export * from './services/settlements.api-client';
export * from './services/shipping.api-client';
export * from './services/vendors.api-client';
export * from './services/webhooks.api-client';

/**
 * Every generated client, so an app can assert at build time that it has them all.
 * Consumers inject the individual clients; this exists for the DI smoke check.
 */
export const GENERATED_API_CLIENTS = [
  CartApiClient,
  CatalogApiClient,
  CheckoutApiClient,
  ContentApiClient,
  IdentityApiClient,
  InventoryApiClient,
  MediaApiClient,
  MetaApiClient,
  NotificationsApiClient,
  OrdersApiClient,
  PaymentsApiClient,
  PlatformApiClient,
  PricingApiClient,
  ReportingApiClient,
  ReturnsApiClient,
  ReviewsApiClient,
  SearchApiClient,
  SettlementsApiClient,
  ShippingApiClient,
  VendorsApiClient,
  WebhooksApiClient,
] as const;
