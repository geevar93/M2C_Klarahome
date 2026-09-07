import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CartActions } from '@klarahome/data-access-cart';
import {
  CategoryNode,
  StoreCatalogService,
  StorefrontOffer,
  StorefrontProduct,
} from '@klarahome/data-access-catalog';
import { DeliveryService } from '@klarahome/data-access-delivery';
import { ProductEngagementService, WishlistStore } from '@klarahome/data-access-engagement';
import { Alert, Badge, Button, Disclosure, Price, QuantityStepper, Rating } from '@klarahome/ui-primitives';
import {
  DeliveryEstimateView,
  DeliveryEstimator,
  OfferList,
  OfferView,
  ProductCardView,
  ProductCarousel,
  ProductGallery,
  QuestionList,
  QuestionView,
  RatingBreakdownView,
  ReviewList,
  ReviewView,
  SellerCard,
  StickyAction,
  VariantSelector,
  VariantView,
} from '@klarahome/ui-patterns';
import { MoneyPipe } from '@klarahome/i18n';
import {
  AnalyticsEvents,
  AnalyticsService,
  BreadcrumbTrail,
  LiveAnnouncer,
  SeoService,
  ToastService,
} from '@klarahome/util';
import { map } from 'rxjs';

import { CatalogMapper } from '../core/catalog.mapper';
import { RecentlyViewedStore } from '../core/recently-viewed.store';

/**
 * The product detail page — `/p/:productSlug`.
 *
 * The most-indexed page in the shop and the one every performance budget is written for, so what
 * arrives when has been decided deliberately:
 *
 *  - **Resolved, and therefore server-rendered:** the product, its variants, the buy box and its
 *    price. These are the page. They are in the HTML a crawler receives, along with the
 *    `Product` + `Offer` + `AggregateRating` structured data built from them.
 *  - **Deferred to the viewport:** reviews and questions. They are below the fold on every screen
 *    size, `@defer (on viewport)` means their chunks are never downloaded by a shopper who does not
 *    scroll, and a review section that failed to load must not be able to take a purchase down
 *    with it.
 *  - **Deferred to a tap:** the other sellers' offers, which are read when that section is opened.
 *    A product with fourteen sellers would otherwise put fourteen offers into the HTML of a page
 *    that shows one.
 *  - **Deferred to the browser:** the delivery estimate and the recently-viewed rail, both of
 *    which read `localStorage`. Neither may be in a server-rendered document — that document is
 *    edge-cacheable (Step 32), and one visitor's PIN code must never be in a page served to
 *    another.
 *
 * **The variant is in the URL** (`?variant=`), not in a component field. A shopper who picks
 * "beige, large" and shares the link has shared beige large; the alternative is a link that opens
 * on whatever the default variant happens to be.
 */
@Component({
  selector: 'kh-product-page',
  imports: [
    Alert,
    Badge,
    Button,
    DeliveryEstimator,
    Disclosure,
    MoneyPipe,
    OfferList,
    Price,
    ProductCarousel,
    ProductGallery,
    QuantityStepper,
    QuestionList,
    Rating,
    ReviewList,
    RouterLink,
    SellerCard,
    StickyAction,
    VariantSelector,
  ],
  template: `
    <article class="pdp">
      <div class="media">
        <kh-product-gallery [images]="gallery()" [productName]="product().name" />
      </div>

      <div class="buy">
        <header>
          <h1>{{ title() }}</h1>
          @if (product().ratingCount > 0) {
            <a class="rating-link" href="#reviews">
              <kh-rating [average]="product().ratingAverage" [count]="product().ratingCount" />
            </a>
          }
        </header>

        @if (buyBox(); as offer) {
          <kh-price size="lg" [price]="offer.price" [mrp]="offer.mrp" taxNote="Inclusive of all taxes" />
        } @else {
          <kh-alert tone="warning" heading="Currently unavailable">
            No seller is offering this right now. Try another option, or check back later.
          </kh-alert>
        }

        @if (variants().length > 1) {
          <kh-variant-selector
            [variants]="variants()"
            [selectedId]="selectedVariantId()"
            (selected)="chooseVariant($event)"
          />
        }

        @if (selectedVariant(); as variant) {
          @if (variant.netQuantity) {
            <p class="net">Net quantity: {{ variant.netQuantity }}</p>
          }
        }

        <div class="quantity">
          <kh-quantity-stepper [(quantity)]="quantity" [max]="maxQuantity()" [disabled]="!buyBox()" />
          <button
            khButton
            variant="primary"
            type="button"
            [disabled]="!buyBox() || adding()"
            (click)="addToCart()"
          >
            {{ adding() ? 'Adding…' : 'Add to cart' }}
          </button>
          <button
            khButton
            variant="secondary"
            type="button"
            [attr.aria-pressed]="isWishlisted()"
            (click)="toggleWishlist()"
          >
            {{ isWishlisted() ? 'Saved' : 'Save for later' }}
          </button>
        </div>

        <!-- Client-only: it reads and writes the remembered PIN code. -->
        @defer (on immediate) {
          <kh-delivery-estimator
            [initialPincode]="rememberedPincode()"
            [estimate]="deliveryEstimate()"
            [checking]="checkingDelivery()"
            (checked)="checkDelivery($event)"
          />
        }

        @if (seller(); as sellerView) {
          <kh-seller-card [seller]="sellerView" />
        }

        @if (buyBox()?.isCodAllowed) {
          <kh-badge tone="info">Cash on delivery available</kh-badge>
        }

        @if (product().shortDescription) {
          <p class="summary">{{ product().shortDescription }}</p>
        }
      </div>

      <div class="detail">
        @if (product().description) {
          <kh-disclosure heading="About this product" [open]="true">
            <p class="prose">{{ product().description }}</p>
          </kh-disclosure>
        }

        @for (group of specifications(); track group.group) {
          <kh-disclosure [heading]="group.group">
            <dl class="specs">
              @for (row of group.rows; track row.label) {
                <div>
                  <dt>{{ row.label }}</dt>
                  <dd>{{ row.value }}</dd>
                </div>
              }
            </dl>
          </kh-disclosure>
        }

        <!-- The offers are fetched when this is opened, not with the page: a product with
             fourteen sellers would otherwise put fourteen offers into the SSR HTML of a page that
             shows one. \`<details>\` keeps the section crawlable either way. -->
        <kh-disclosure heading="Other sellers" [hint]="offerHint()" [(open)]="offersOpen">
          @if (loadingOffers()) {
            <p class="prose">Loading the other sellers…</p>
          } @else if (offers().length > 0) {
            <kh-offer-list
              [offers]="offers()"
              [selectedListingId]="selectedListingId()"
              (chosen)="chooseOffer($event)"
            />
          } @else {
            <p class="prose">No other seller is offering this variant.</p>
          }
        </kh-disclosure>
      </div>

      <section class="reviews" id="reviews">
        <h2>Ratings and reviews</h2>
        @defer (on viewport) {
          <kh-review-list
            [reviews]="reviews()"
            [breakdown]="ratingBreakdown()"
            [hasMore]="reviewsCursor() !== null"
            [loadingMore]="loadingReviews()"
            (voted)="voteHelpful($event)"
            (moreRequested)="loadMoreReviews()"
          />
        } @placeholder {
          <p class="prose">Scroll to read what customers said.</p>
        }
      </section>

      <section class="questions">
        @defer (on viewport) {
          <kh-question-list
            [questions]="questions()"
            [hasMore]="questionsCursor() !== null"
            [loadingMore]="loadingQuestions()"
            (askRequested)="askQuestion()"
            (moreRequested)="loadMoreQuestions()"
          />
        } @placeholder {
          <p class="prose">Scroll to read the questions other customers asked.</p>
        }
      </section>

      @if (recentlyViewed().length > 0) {
        <kh-product-carousel heading="Recently viewed" [products]="recentlyViewed()" />
      }
    </article>

    <!-- The thumb-zone action. In the shell's bar, so it stays put while the page scrolls. -->
    <ng-template khStickyAction>
      @if (buyBox(); as offer) {
        <span class="bar-price">{{ offer.price | khMoney }}</span>
        <button
          khButton
          variant="primary"
          [block]="true"
          type="button"
          [disabled]="adding()"
          (click)="addToCart()"
        >
          {{ adding() ? 'Adding…' : 'Add to cart' }}
        </button>
      } @else {
        <a khButton variant="secondary" [block]="true" routerLink="/">Browse other products</a>
      }
    </ng-template>
  `,
  styles: `
    .pdp {
      display: grid;
      gap: var(--space-6);
      padding-block: var(--space-4) var(--space-10);
    }

    .buy {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    h1 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-2xl);
    }

    .rating-link {
      text-decoration: none;
    }

    .quantity {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3);
    }

    .net,
    .summary,
    .prose {
      margin: 0;
      color: var(--color-text-muted);
    }

    .summary {
      color: var(--color-text);
    }

    .specs {
      display: grid;
      grid-template-columns: minmax(8rem, auto) 1fr;
      gap: var(--space-2) var(--space-4);
      margin: 0;
      font-size: var(--text-sm);
    }

    .specs > div {
      display: contents;
    }

    dt {
      color: var(--color-text-muted);
    }

    dd {
      margin: 0;
    }

    h2 {
      font-size: var(--text-xl);
    }

    .reviews,
    .questions {
      padding-block-start: var(--space-8);
      border-block-start: 1px solid var(--color-border);
    }

    .bar-price {
      font-weight: var(--weight-bold);
      white-space: nowrap;
    }

    /* From 'lg' the gallery and the buy column sit side by side and everything else runs full
       width beneath them. 1024px is the \`lg\` breakpoint from _breakpoints.scss. */
    @media (min-width: 1024px) {
      .pdp {
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
        align-items: start;
      }

      .detail,
      .reviews,
      .questions,
      kh-product-carousel {
        grid-column: 1 / -1;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly catalog = inject(StoreCatalogService);
  private readonly engagement = inject(ProductEngagementService);
  private readonly delivery = inject(DeliveryService);
  private readonly cart = inject(CartActions);
  private readonly mapper = inject(CatalogMapper);
  private readonly seo = inject(SeoService);
  private readonly breadcrumbs = inject(BreadcrumbTrail);
  private readonly analytics = inject(AnalyticsService);
  private readonly announcer = inject(LiveAnnouncer);
  private readonly toasts = inject(ToastService);
  private readonly recent = inject(RecentlyViewedStore);
  private readonly wishlist = inject(WishlistStore);

  protected readonly product = toSignal(
    this.route.data.pipe(map((data) => data['product'] as StorefrontProduct)),
    { requireSync: true },
  );

  private readonly queryParams = toSignal(this.route.queryParams, { requireSync: true });
  private readonly tree = toSignal(this.catalog.categories(), { initialValue: [] as CategoryNode[] });

  protected readonly quantity = signal(1);
  protected readonly adding = signal(false);

  private readonly chosenListingId = signal<string | null>(null);
  private readonly offerRows = signal<readonly StorefrontOffer[]>([]);
  protected readonly offersOpen = signal(false);
  protected readonly loadingOffers = signal(false);
  private readonly reviewRows = signal<readonly ReviewView[]>([]);
  private readonly questionRows = signal<readonly QuestionView[]>([]);
  private readonly breakdown = signal<RatingBreakdownView | null>(null);
  private readonly votedReviewIds = signal<readonly string[]>([]);

  protected readonly reviewsCursor = signal<string | null>(null);
  protected readonly questionsCursor = signal<string | null>(null);
  protected readonly loadingReviews = signal(false);
  protected readonly loadingQuestions = signal(false);

  protected readonly deliveryEstimate = signal<DeliveryEstimateView | null>(null);
  protected readonly checkingDelivery = signal(false);
  protected readonly rememberedPincode = signal('');

  protected readonly variants = computed<readonly VariantView[]>(() => this.mapper.variants(this.product()));

  /**
   * The variant the URL asks for, the default one, or the first purchasable one.
   *
   * In that order on purpose: a shared link wins, then the catalogue's own default, and only then
   * a guess — and the guess prefers something that can actually be bought, because landing a
   * shopper on the one sold-out size of an otherwise available product is a page that looks broken.
   */
  protected readonly selectedVariantId = computed(() => {
    const requested = this.queryParams()['variant'] as string | undefined;
    const variants = this.variants();
    if (requested && variants.some((variant) => variant.id === requested)) return requested;

    const product = this.product();
    const preferred = product.variants.find((variant) => variant.isDefault)?.id;
    if (preferred) return preferred;

    return variants.find((variant) => variant.isPurchasable)?.id ?? variants[0]?.id ?? null;
  });

  protected readonly selectedVariant = computed(
    () => this.variants().find((variant) => variant.id === this.selectedVariantId()) ?? null,
  );

  protected readonly gallery = computed(
    () => this.selectedVariant()?.images ?? this.mapper.gallery(this.product().media, this.product().name),
  );

  /** The offer being bought from: the one the shopper chose, else the variant's buy box. */
  protected readonly buyBox = computed<OfferView | null>(() => {
    const chosen = this.chosenListingId();
    if (chosen) {
      const found = this.offers().find((offer) => offer.listingId === chosen);
      if (found) return found;
    }

    const raw = this.product().variants.find((variant) => variant.id === this.selectedVariantId())?.buyBox;
    return raw ? this.mapper.offer(raw) : null;
  });

  protected readonly selectedListingId = computed(() => this.buyBox()?.listingId ?? null);

  /** The seller card, built from the offer actually being bought from — chosen or buy box. */
  protected readonly seller = computed(() => {
    const offer = this.buyBox();
    return offer ? this.mapper.seller(offer, this.product()) : null;
  });
  protected readonly offers = computed(() => this.offerRows().map((offer) => this.mapper.offer(offer)));
  protected readonly reviews = this.reviewRows.asReadonly();
  protected readonly questions = this.questionRows.asReadonly();
  protected readonly ratingBreakdown = this.breakdown.asReadonly();
  protected readonly specifications = computed(() => this.mapper.specifications(this.product()));
  protected readonly recentlyViewed = computed(() => this.recent.except(this.product().slug));
  protected readonly isWishlisted = computed(() =>
    this.selectedVariantId() ? this.wishlist.variantIds().includes(this.selectedVariantId()!) : false,
  );

  protected readonly title = computed(() => {
    const suffix = this.selectedVariant()?.nameSuffix;
    return suffix ? `${this.product().name} — ${suffix}` : this.product().name;
  });

  protected readonly offerHint = computed(() => {
    const count =
      this.product().variants.find((variant) => variant.id === this.selectedVariantId())?.offerCount ?? 0;
    return count > 1 ? `${count} sellers` : null;
  });

  /**
   * The most a shopper may ask for.
   *
   * The seller's per-order limit when there is one, otherwise ten — a cap the storefront picks so
   * the stepper is finite. It is a courtesy either way: the authoritative check is at placement,
   * where the stock reservation happens (Step 13).
   */
  protected readonly maxQuantity = computed(() => this.buyBox()?.maxOrderQuantity ?? 10);

  constructor() {
    this.wishlist.loadOnce();

    // The `Product` block must not outlive the product. A category page still carrying the last
    // PDP's offer is markup that contradicts the page it is on.
    inject(DestroyRef).onDestroy(() => this.seo.clearJsonLd('product'));

    this.route.data.pipe(takeUntilDestroyed()).subscribe((data) => {
      const product = data['product'] as StorefrontProduct;
      // A slug change reuses this component; everything loaded for the previous product goes.
      this.reset();
      this.applySeo(product);
      this.loadEngagement(product);
      this.recent.record(product.slug, product.name, product.media[0]?.fileId ?? null);
      this.analytics.track(AnalyticsEvents.viewItem, { item_id: product.id, item_name: product.name });
    });

    // The variant decides the price, the gallery and the structured data, and it changes without a
    // navigation — an effect is the only thing that sees both the resolver's product and a
    // query-parameter change.
    effect(() => this.publishStructuredData());

    // Offers are read when the section is opened, and re-read when the variant changes underneath
    // an already-open section.
    effect(() => {
      const variantId = this.selectedVariantId();
      if (this.offersOpen() && variantId) this.loadOffers(variantId);
    });

    // The taxonomy trail. The tree arrives after the first render on a cold cache, so this is an
    // effect rather than a one-off: a path that appeared a tick late would never be shown.
    effect(() => {
      const path = this.mapper.categoryPathById(this.tree(), this.product().categoryId);
      this.breadcrumbs.setAncestors(path.map((node) => ({ label: node.name, path: `/c/${node.slug}` })));
    });

    this.rememberedPincode.set(this.delivery.remembered());
    if (this.rememberedPincode()) this.checkDelivery(this.rememberedPincode());
  }

  protected chooseVariant(variant: VariantView): void {
    // Into the URL, not into a field: the selection is part of what the page *is*, so a shared
    // link and a back button both carry it. `replaceUrl` keeps twelve swatch taps out of history.
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { variant: variant.id },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });

    this.chosenListingId.set(null);
    this.quantity.set(1);
  }

  protected chooseOffer(offer: OfferView): void {
    this.chosenListingId.set(offer.listingId);
    this.quantity.set(1);
    this.announcer.announce(`Buying from ${offer.sellerName}.`);
  }

  protected addToCart(): void {
    const offer = this.buyBox();
    if (!offer || this.adding()) return;

    this.adding.set(true);
    this.cart.add(offer.listingId, this.quantity()).subscribe({
      next: () => {
        this.adding.set(false);
        this.toasts.success(`${this.title()} was added to your cart.`);
        this.analytics.track(AnalyticsEvents.addToCart, {
          item_id: this.selectedVariantId(),
          item_name: this.title(),
          price: offer.price.amount,
          quantity: this.quantity(),
        });
      },
      error: (error: unknown) => {
        this.adding.set(false);
        this.toasts.warning(this.cart.describeFailure(error));
      },
    });
  }

  protected toggleWishlist(): void {
    const variantId = this.selectedVariantId();
    if (!variantId) return;

    if (!this.wishlist.toggle(variantId)) {
      this.toasts.info('Sign in to save products to your wishlist.');
      return;
    }

    this.analytics.track(AnalyticsEvents.addToWishlist, { item_id: variantId, item_name: this.title() });
  }

  protected checkDelivery(pincode: string): void {
    this.checkingDelivery.set(true);
    this.delivery.check(pincode).subscribe((response) => {
      this.deliveryEstimate.set(this.mapper.delivery(response));
      this.checkingDelivery.set(false);
    });
  }

  protected voteHelpful(review: ReviewView): void {
    const voted = this.votedReviewIds().includes(review.id);

    const request = voted ? this.engagement.withdrawVote(review.id) : this.engagement.voteHelpful(review.id);

    request.subscribe({
      next: () => {
        this.votedReviewIds.update((ids) =>
          voted ? ids.filter((id) => id !== review.id) : [...ids, review.id],
        );
        this.reviewRows.update((rows) =>
          rows.map((row) =>
            row.id === review.id ? { ...row, helpfulCount: row.helpfulCount + (voted ? -1 : 1) } : row,
          ),
        );
      },
      error: () => this.toasts.info('Sign in to tell us whether a review was helpful.'),
    });
  }

  protected loadMoreReviews(): void {
    const cursor = this.reviewsCursor();
    if (!cursor || this.loadingReviews()) return;

    this.loadingReviews.set(true);
    this.engagement.reviews(this.product().id, cursor).subscribe((page) => {
      this.reviewRows.update((rows) => [...rows, ...page.items.map((review) => this.mapper.review(review))]);
      this.reviewsCursor.set(page.page.nextCursor);
      this.loadingReviews.set(false);
    });
  }

  protected loadMoreQuestions(): void {
    const cursor = this.questionsCursor();
    if (!cursor || this.loadingQuestions()) return;

    this.loadingQuestions.set(true);
    this.engagement.questions(this.product().id, cursor).subscribe((page) => {
      this.questionRows.update((rows) => [
        ...rows,
        ...page.items.map((question) => this.mapper.question(question)),
      ]);
      this.questionsCursor.set(page.page.nextCursor);
      this.loadingQuestions.set(false);
    });
  }

  /**
   * Asking a question.
   *
   * The form is Step 25's — it needs the signed-in customer, the character limit and the
   * moderation notice that go with an account surface. Until then this says so rather than
   * offering a control that does nothing.
   */
  protected askQuestion(): void {
    this.toasts.info('Asking a question is coming with the account pages.');
  }

  private reset(): void {
    this.offerRows.set([]);
    this.reviewRows.set([]);
    this.questionRows.set([]);
    this.breakdown.set(null);
    this.reviewsCursor.set(null);
    this.questionsCursor.set(null);
    this.chosenListingId.set(null);
    this.quantity.set(1);
    this.offersOpen.set(false);
  }

  private loadOffers(variantId: string): void {
    this.loadingOffers.set(true);
    this.catalog.offers(this.product().slug, variantId).subscribe({
      next: (offers) => {
        this.offerRows.set(offers);
        this.loadingOffers.set(false);
      },
      error: () => {
        this.offerRows.set([]);
        this.loadingOffers.set(false);
      },
    });
  }

  private loadEngagement(product: StorefrontProduct): void {
    this.engagement
      .rating(product.id)
      .subscribe((summary) => this.breakdown.set(this.mapper.ratingBreakdown(summary)));

    this.engagement.reviews(product.id).subscribe((page) => {
      this.reviewRows.set(page.items.map((review) => this.mapper.review(review)));
      this.reviewsCursor.set(page.page.nextCursor);
    });

    this.engagement.questions(product.id).subscribe((page) => {
      this.questionRows.set(page.items.map((question) => this.mapper.question(question)));
      this.questionsCursor.set(page.page.nextCursor);
    });
  }

  private applySeo(product: StorefrontProduct): void {
    const path = `/p/${product.slug}`;

    this.seo.apply({
      title: product.seo.metaTitle || product.name,
      description: product.seo.metaDescription || product.shortDescription || '',
      // Always the product's own path, never the variant URL: `?variant=` is the same product to a
      // crawler, and letting each variant claim its own canonical is how one product becomes nine
      // competing pages in an index (docs/05-frontend-architecture.md §3.5).
      canonicalPath: product.seo.canonicalUrl || path,
      noIndex: product.seo.noIndex,
      ogType: 'product',
      imageUrl: product.media[0]?.url ?? undefined,
    });

    this.breadcrumbs.setLeafLabel(product.name);
  }

  /**
   * `Product`, its `Offer` and its `AggregateRating` (docs/05-frontend-architecture.md §3.5).
   *
   * Written from the buy box rather than from the cheapest offer, because the buy box is the price
   * the page shows — a rich result quoting a price the page does not is a Merchant Center penalty
   * and, more simply, a lie. Republished when the variant changes, keyed so it replaces rather
   * than accumulates.
   */
  private publishStructuredData(): void {
    const product = this.product();
    const offer = this.buyBox();
    const variant = this.selectedVariant();

    this.seo.setJsonLd('product', {
      '@context': 'https://schema.org',
      '@type': 'Product',
      name: this.title(),
      sku: variant?.sku,
      description: product.seo.metaDescription || product.shortDescription || '',
      image: product.media.map((item) => item.url).filter(Boolean),
      countryOfOrigin: product.countryOfOrigin ?? undefined,
      aggregateRating:
        product.ratingCount > 0 && product.ratingAverage !== null
          ? {
              '@type': 'AggregateRating',
              ratingValue: product.ratingAverage,
              reviewCount: product.ratingCount,
            }
          : undefined,
      offers: offer
        ? {
            '@type': 'Offer',
            price: offer.price.amount,
            priceCurrency: offer.price.currency,
            availability: 'https://schema.org/InStock',
            url: this.seo.absolute(`/p/${product.slug}`),
            seller: { '@type': 'Organization', name: offer.sellerName },
          }
        : {
            '@type': 'Offer',
            availability: 'https://schema.org/OutOfStock',
            url: this.seo.absolute(`/p/${product.slug}`),
          },
    });
  }
}
