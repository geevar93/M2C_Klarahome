import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { StorefrontVendorResponse } from '@klarahome/data-access-catalog';
import { Rating } from '@klarahome/ui-primitives';
import { BreadcrumbTrail, SeoService } from '@klarahome/util';
import { map } from 'rxjs';

import { ProductListing } from './listing/product-listing';

/**
 * A seller's storefront — `/vendor/:slug`.
 *
 * The same faceted listing as a category page, narrowed to one seller. The seller is passed to the
 * listing rather than written into the query string, because it is what this page *is* — the path
 * already says so, and a `?vendor=` beside it would be two URLs for one page. Every other facet
 * still works inside it, so a shopper can narrow further within a seller's shop.
 *
 * The header states the seller's promises — rating, dispatch, return policy — because on a
 * marketplace they belong to the seller and not to the store, and a shopper choosing between two
 * offers of the same product is choosing between these.
 */
@Component({
  selector: 'kh-vendor-page',
  imports: [ProductListing, Rating],
  template: `
    <header class="head">
      <h1>{{ vendor().displayName }}</h1>
      @if (vendor().rating !== null) {
        <kh-rating [average]="vendor().rating" [showEmpty]="true" />
      }
      <p class="promise">
        Dispatches within {{ dispatchDays() }} {{ dispatchDays() === 1 ? 'day' : 'days' }}
        @if (vendor().returnPolicy.acceptsReturns) {
          · {{ vendor().returnPolicy.windowDays }}-day returns
        }
      </p>
      @if (vendor().about) {
        <p class="about">{{ vendor().about }}</p>
      }
    </header>

    <kh-product-listing
      [vendorId]="vendor().id"
      [emptyHeading]="'Nothing from ' + vendor().displayName + ' matched'"
    />
  `,
  styles: `
    .head {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      padding-block: var(--space-6) var(--space-2);
    }

    h1 {
      margin: 0;
      font-size: var(--text-2xl);
    }

    .promise {
      margin: 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .about {
      margin: 0;
      max-inline-size: var(--measure);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VendorPage {
  private readonly route = inject(ActivatedRoute);
  private readonly seo = inject(SeoService);
  private readonly breadcrumbs = inject(BreadcrumbTrail);

  protected readonly vendor = toSignal(
    this.route.data.pipe(map((data) => data['vendor'] as StorefrontVendorResponse)),
    { requireSync: true },
  );

  protected readonly dispatchDays = () => Math.max(1, Math.ceil(this.vendor().dispatchSlaHours / 24));

  constructor() {
    this.route.data
      .pipe(takeUntilDestroyed())
      .subscribe((data) => this.apply(data['vendor'] as StorefrontVendorResponse));
  }

  private apply(vendor: StorefrontVendorResponse): void {
    this.seo.apply({
      title: vendor.displayName,
      description: vendor.about || `Products sold by ${vendor.displayName}.`,
      canonicalPath: `/vendor/${vendor.slug}`,
    });

    this.breadcrumbs.setLeafLabel(vendor.displayName);
  }
}
