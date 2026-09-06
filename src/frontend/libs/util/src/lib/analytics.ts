import { Injectable, InjectionToken, PLATFORM_ID, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

import { RUNTIME_CONFIG } from './runtime-config';

/**
 * The commerce event taxonomy, spelled once.
 *
 * These are the GA4 names on purpose. Not because GA4 is the destination — the destination is a
 * per-tenant decision and may be nothing at all — but because they are the names every analytics
 * product already understands, so a new sink is a new `AnalyticsSink` and not a re-tagging of
 * every component (docs/05-frontend-architecture.md §5).
 */
export const AnalyticsEvents = {
  viewItemList: 'view_item_list',
  viewItem: 'view_item',
  selectItem: 'select_item',
  addToCart: 'add_to_cart',
  removeFromCart: 'remove_from_cart',
  viewCart: 'view_cart',
  beginCheckout: 'begin_checkout',
  addShippingInfo: 'add_shipping_info',
  addPaymentInfo: 'add_payment_info',
  purchase: 'purchase',
  refund: 'refund',
  search: 'search',
  login: 'login',
  signUp: 'sign_up',
  addToWishlist: 'add_to_wishlist',
} as const;

export type AnalyticsEventName = (typeof AnalyticsEvents)[keyof typeof AnalyticsEvents] | (string & {});

/** Where events go. Implemented per tenant; absent means analytics is off. */
export interface AnalyticsSink {
  track(event: AnalyticsEventName, payload: Readonly<Record<string, unknown>>): void;
  identify?(userId: string | null): void;
  page?(path: string, title?: string): void;
}

export const ANALYTICS_SINK = new InjectionToken<AnalyticsSink>('KLARA_HOME_ANALYTICS_SINK');

/**
 * The facade every component calls.
 *
 * It is deliberately a no-op unless a sink is registered *and* the runtime config names a
 * provider. A tenant that has not consented to analytics gets an app with no tracking in it,
 * rather than an app with tracking that fails quietly — and nothing at all happens during SSR,
 * where there is no session to attribute anything to.
 */
@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private readonly sink = inject(ANALYTICS_SINK, { optional: true });
  private readonly config = inject(RUNTIME_CONFIG);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private get enabled(): boolean {
    return this.isBrowser && this.sink !== null && Boolean(this.config.analytics?.provider);
  }

  track(event: AnalyticsEventName, payload: Readonly<Record<string, unknown>> = {}): void {
    if (!this.enabled) return;
    try {
      this.sink?.track(event, { ...payload, tenant: this.config.tenantCode });
    } catch (error) {
      // A broken tag manager must never break a checkout.
      console.warn('[analytics] sink threw', error);
    }
  }

  identify(userId: string | null): void {
    if (!this.enabled) return;
    try {
      this.sink?.identify?.(userId);
    } catch (error) {
      console.warn('[analytics] sink threw', error);
    }
  }

  page(path: string, title?: string): void {
    if (!this.enabled) return;
    try {
      this.sink?.page?.(path, title);
    } catch (error) {
      console.warn('[analytics] sink threw', error);
    }
  }
}
