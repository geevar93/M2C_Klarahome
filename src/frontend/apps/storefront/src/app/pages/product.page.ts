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
import {
  EligiblePurchaseResponse,
  ProductEngagementService,
  WishlistStore,
} from '@klarahome/data-access-engagement';
import {
  Alert,
  Badge,
  Button,
  Control,
  Disclosure,
  Drawer,
  Field,
  Price,
  QuantityStepper,
  Rating,
  Skeleton,
} from '@klarahome/ui-primitives';
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
import { describeError } from '../core/describe-error';
import { RecentlyViewedStore } from '../core/recently-viewed.store';

/**
 * Legal-metrology and GST unit codes a catalogue is likely to carry, as [singular, plural].
 * Metric symbols (kg, ml, cm) are the same in both forms.
 */
const NET_QUANTITY_UNITS: Readonly<Record<string, readonly [string, string]>> = {
  N: ['unit', 'units'],
  NOS: ['unit', 'units'],
  PC: ['piece', 'pieces'],
  PCS: ['piece', 'pieces'],
  SET: ['set', 'sets'],
  PAIR: ['pair', 'pairs'],
  KG: ['kg', 'kg'],
  G: ['g', 'g'],
  L: ['litre', 'litres'],
  ML: ['ml', 'ml'],
  M: ['m', 'm'],
  CM: ['cm', 'cm'],
  MM: ['mm', 'mm'],
};

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
    Control,
    DeliveryEstimator,
    Disclosure,
    Drawer,
    Field,
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
    Skeleton,
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

        <!-- One raised panel for everything a purchase needs, in the order a shopper decides it:
             the price, which option, how many, then the actions, then whether it can reach them. -->
        <div class="panel">
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
            <p class="net">Net quantity: {{ netQuantityLabel(variant.netQuantity) }}</p>
          }
        }

        <div class="quantity">
          <kh-quantity-stepper [(quantity)]="quantity" [max]="maxQuantity()" [disabled]="!buyBox()" />
          <!-- The id is what the sticky bar waits on: it appears once this button has scrolled out of
               view, so the two are never on screen together. -->
          <button
            id="pdp-add-to-cart"
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
            [disabled]="!buyBox() || adding() || buying()"
            (click)="buyNow()"
          >
            {{ buying() ? 'Opening checkout…' : 'Buy now' }}
          </button>
          <button
            khButton
            variant="tertiary"
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
        </div>

        @if (seller(); as sellerView) {
          <kh-seller-card [seller]="sellerView" />
        }

        @if (buyBox()?.isCodAllowed) {
          <kh-badge tone="info" class="cod">Cash on delivery available</kh-badge>
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
            [canWrite]="eligiblePurchases().length > 0"
            [hasMore]="reviewsCursor() !== null"
            [loadingMore]="loadingReviews()"
            (voted)="voteHelpful($event)"
            (writeRequested)="startReview()"
            (moreRequested)="loadMoreReviews()"
          />
        } @placeholder {
          <!-- A block the shape of the section, not a sentence: "scroll to read" was an instruction
               to somebody who may not need to scroll at all. The words are for assistive tech. -->
          <div class="defer-skeleton" role="status">
            <span class="kh-visually-hidden">Customer reviews are loading.</span>
            <kh-skeleton height="5rem" radius="var(--radius-lg)" />
            <kh-skeleton [lines]="3" height="0.875rem" />
          </div>
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
          <div class="defer-skeleton" role="status">
            <span class="kh-visually-hidden">Customer questions are loading.</span>
            <kh-skeleton [lines]="3" height="0.875rem" />
          </div>
        }
      </section>

      @if (recentlyViewed().length > 0) {
        <kh-product-carousel heading="Recently viewed" [products]="recentlyViewed()" />
      }
    </article>

    <!-- The thumb-zone action. In the shell's bar, so it stays put while the page scrolls. -->
    <ng-template khStickyAction mobileOnly revealAfter="#pdp-add-to-cart">
      @if (buyBox(); as offer) {
        <span class="bar-price">{{ offer.price | khMoney }}</span>
        <button
          khButton
          variant="secondary"
          type="button"
          [disabled]="adding() || buying()"
          (click)="buyNow()"
        >
          {{ buying() ? 'Opening…' : 'Buy now' }}
        </button>
        <button
          khButton
          variant="primary"
          type="button"
          [disabled]="adding() || buying()"
          (click)="addToCart()"
        >
          {{ adding() ? 'Adding…' : 'Add to cart' }}
        </button>
      } @else {
        <a khButton variant="secondary" [block]="true" routerLink="/">Browse other products</a>
      }
    </ng-template>

    <!--
      Both forms are drawers rather than pages. Writing a review or asking a question is something a
      shopper does *about the product they are looking at*, and a route change would take away the
      photographs and the price they are describing (Step 28B, deliverable 20).
    -->
    <kh-drawer [open]="reviewOpen()" side="end" label="Write a review" (closed)="reviewOpen.set(false)">
      <h2>Write a review</h2>
      <p class="prose">
        Only the customer who received an item can review it, so every review on this page is from a real
        purchase. Yours is published after moderation.
      </p>

      @if (writeError(); as message) {
        <kh-alert tone="danger" heading="That could not be posted">{{ message }}</kh-alert>
      }

      @if (eligiblePurchases().length > 1) {
        <kh-field label="Which purchase" for="review-purchase">
          <select
            khControl
            id="review-purchase"
            [value]="reviewOrderLineId()"
            (change)="reviewOrderLineId.set($any($event.target).value)"
          >
            @for (purchase of eligiblePurchases(); track purchase.orderLineId) {
              <option [value]="purchase.orderLineId">{{ purchase.orderNumber }} — {{ purchase.sku }}</option>
            }
          </select>
        </kh-field>
      }

      <kh-field label="Rating" for="review-rating">
        <select
          khControl
          id="review-rating"
          [value]="reviewRating()"
          (change)="reviewRating.set($any($event.target).value)"
        >
          <option value="5">5 — excellent</option>
          <option value="4">4 — good</option>
          <option value="3">3 — all right</option>
          <option value="2">2 — poor</option>
          <option value="1">1 — bad</option>
        </select>
      </kh-field>

      <kh-field label="Headline" for="review-title" [optional]="true">
        <input
          khControl
          id="review-title"
          type="text"
          maxlength="120"
          [value]="reviewTitle()"
          (input)="reviewTitle.set($any($event.target).value)"
        />
      </kh-field>

      <kh-field label="What you thought" for="review-body">
        <textarea
          khControl
          id="review-body"
          rows="5"
          maxlength="2000"
          [value]="reviewBody()"
          (input)="reviewBody.set($any($event.target).value)"
        ></textarea>
      </kh-field>

      <div slot="footer">
        <button
          khButton
          type="button"
          variant="secondary"
          [disabled]="posting()"
          (click)="reviewOpen.set(false)"
        >
          Cancel
        </button>
        <button
          khButton
          type="button"
          [disabled]="posting() || reviewBody().trim().length === 0"
          (click)="submitReview()"
        >
          {{ posting() ? 'Posting…' : 'Post the review' }}
        </button>
      </div>
    </kh-drawer>

    <kh-drawer [open]="questionOpen()" side="end" label="Ask a question" (closed)="questionOpen.set(false)">
      <h2>Ask a question</h2>
      <p class="prose">
        Answered by the seller or by us, and published on this page once it has been moderated — so write it
        as something another shopper would find useful.
      </p>

      @if (writeError(); as message) {
        <kh-alert tone="danger" heading="That could not be posted">{{ message }}</kh-alert>
      }

      <kh-field label="Your question" for="question-body">
        <textarea
          khControl
          id="question-body"
          rows="4"
          maxlength="1000"
          [value]="questionBody()"
          (input)="questionBody.set($any($event.target).value)"
        ></textarea>
      </kh-field>

      <div slot="footer">
        <button
          khButton
          type="button"
          variant="secondary"
          [disabled]="posting()"
          (click)="questionOpen.set(false)"
        >
          Cancel
        </button>
        <button
          khButton
          type="button"
          [disabled]="posting() || questionBody().trim().length === 0"
          (click)="submitQuestion()"
        >
          {{ posting() ? 'Posting…' : 'Ask it' }}
        </button>
      </div>
    </kh-drawer>
  `,
  styles: `
    /* Mobile first: the base rules are the 360px phone, and each media query below only adds.
       One column in this order — gallery, title and price, the buy controls, details, reviews,
       questions, recently viewed — which is the order a shopper reads a product in.

       \`minmax(0, 1fr)\`, not the implicit \`auto\` track: an \`auto\` column is as wide as the
       min-content of its widest child, so one long unbroken spec value, a seller name or the
       gallery's image pushed the whole page wider than the phone and gave it a sideways scroll. */
    .pdp {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      gap: var(--space-5);
      padding-block: var(--space-3) var(--space-8);
    }

    .pdp > * {
      min-inline-size: 0;
    }

    .buy {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    /* The buy box as one object. Depth from the shadow rather than a heavier edge: the border is
       the quiet hairline, the panel stands off the page by casting. It carries the whole purchase
       so the eye lands on one thing and reads down it. */
    .panel {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
      padding: var(--space-4);
      border: 1px solid var(--color-border-subtle);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-md);
    }

    /* The delivery check is the footnote of the panel, set off by a rule, not a second panel. */
    .panel kh-delivery-estimator {
      padding-block-start: var(--space-4);
      border-block-start: 1px solid var(--color-border-subtle);
    }

    .defer-skeleton {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      min-block-size: 8rem;
    }

    /* Product names, descriptions and spec values are merchant free text with no length limit. */
    .buy,
    .detail {
      overflow-wrap: anywhere;
    }

    h1 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-xl);
      line-height: var(--leading-snug);
      text-wrap: balance;
    }

    .rating-link {
      display: inline-flex;
      align-items: center;
      min-block-size: var(--touch-target-min);
      text-decoration: none;
    }

    /* On a phone: the stepper and "Add to cart" share a row, with the button taking every pixel
       the stepper leaves, and "Save for later" gets a full-width row of its own beneath — three
       controls wrapping wherever \`flex-wrap\` happened to break them left a lone, half-width
       button that was easy to mistake for the primary action. */
    .quantity {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr);
      align-items: center;
      gap: var(--space-3);
    }

    .quantity > button {
      inline-size: 100%;
    }

    /* "Buy now" and "Save for later" each take a full row beneath: the fast path is one tap
       wide, and the quiet path never sits beside the primary action pretending to be its equal. */
    .quantity > button:nth-child(n + 3) {
      grid-column: 1 / -1;
    }

    /* A label sized to its words. The column stretches its children, and a stretched badge is a
       banner. */
    .cod {
      align-self: flex-start;
    }

    .net,
    .summary,
    .prose {
      margin: 0;
      color: var(--color-text-muted);
    }

    .net {
      font-size: var(--text-sm);
    }

    .summary {
      color: var(--color-text);
    }

    /* A phone stacks each label over its value: a fixed 8rem label column left a 360px screen
       about 150px for the value, and "Dimensions (W × D × H)" took four lines to say what it is. */
    .specs {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      gap: var(--space-3);
      margin: 0;
      font-size: var(--text-sm);
    }

    .specs > div {
      display: grid;
      gap: var(--space-1);
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
      padding-block-start: var(--space-6);
      border-block-start: 1px solid var(--color-border);
    }

    .bar-price {
      font-weight: var(--weight-bold);
      white-space: nowrap;
    }

    /* From 480px (\`sm\`) there is room for the label beside its value, and for the three buy
       controls on one line. */
    @media (min-width: 480px) {
      .specs {
        grid-template-columns: minmax(6rem, 35%) minmax(0, 1fr);
        gap: var(--space-2) var(--space-4);
      }

      .specs > div {
        display: contents;
      }

    }

    @media (min-width: 768px) {
      .panel {
        padding: var(--space-5);
      }
    }

    @media (min-width: 768px) {
      .pdp {
        gap: var(--space-6);
        padding-block: var(--space-4) var(--space-10);
      }

      h1 {
        font-size: var(--text-2xl);
      }

      .reviews,
      .questions {
        padding-block-start: var(--space-8);
      }
    }

    /* From 'lg' the gallery and the buy column sit side by side and everything else runs full
       width beneath them. 1024px is the \`lg\` breakpoint from _breakpoints.scss. */
    @media (min-width: 1024px) {
      .pdp {
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
        align-items: start;
        gap: var(--space-8);
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
  protected readonly buying = signal(false);

  // ---- Writing a review, asking a question (Step 28B, deliverable 20) ----------------------------

  protected readonly reviewOpen = signal(false);
  protected readonly questionOpen = signal(false);
  protected readonly posting = signal(false);
  protected readonly writeError = signal<string | null>(null);

  /**
   * The deliveries this shopper could review.
   *
   * Empty for a visitor who is not signed in and for one who has not received the item, which is
   * the same thing as far as this page is concerned: no control is offered either way. It is
   * fetched with the rest of the engagement data rather than on the button, so the button's
   * presence is already correct when the section renders.
   */
  protected readonly eligiblePurchases = signal<readonly EligiblePurchaseResponse[]>([]);

  protected readonly reviewOrderLineId = signal('');
  protected readonly reviewRating = signal('5');
  protected readonly reviewTitle = signal('');
  protected readonly reviewBody = signal('');
  protected readonly questionBody = signal('');

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

  /**
   * "1 N" read as the code it is. Presentational only: the stored value is untouched, and a unit
   * this table does not know is shown exactly as the merchant typed it, never guessed at.
   */
  protected netQuantityLabel(raw: string): string {
    const match = /^(\d+(?:\.\d+)?)\s*([A-Za-z]+)$/.exec(raw.trim());
    if (!match) return raw;

    const [, amount, code] = match;
    const unit = NET_QUANTITY_UNITS[code.toUpperCase()];
    if (!unit) return raw;

    const plural = Number(amount) !== 1;
    return `${amount} ${plural ? unit[1] : unit[0]}`;
  }

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

  /**
   * The fast path: into the cart and straight to the checkout in one tap.
   *
   * It is the same add as "Add to cart" — the same line, the same quantity, the same seller —
   * followed by the navigation the shopper would otherwise make by hand. Nothing about the order
   * is decided here; the checkout still reads the basket the API holds.
   */
  protected buyNow(): void {
    const offer = this.buyBox();
    if (!offer || this.adding() || this.buying()) return;

    this.buying.set(true);
    this.cart.add(offer.listingId, this.quantity()).subscribe({
      next: () => {
        this.analytics.track(AnalyticsEvents.addToCart, {
          item_id: this.selectedVariantId(),
          item_name: this.title(),
          price: offer.price.amount,
          quantity: this.quantity(),
        });
        this.buying.set(false);
        void this.router.navigate(['/checkout']);
      },
      error: (error: unknown) => {
        this.buying.set(false);
        this.toasts.warning(this.cart.describeFailure(error));
      },
    });
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

  protected startReview(): void {
    const first = this.eligiblePurchases()[0];
    if (!first) return;

    this.writeError.set(null);
    this.reviewOrderLineId.set(first.orderLineId);
    this.reviewRating.set('5');
    this.reviewTitle.set('');
    this.reviewBody.set('');
    this.reviewOpen.set(true);
  }

  protected submitReview(): void {
    const orderLineId = this.reviewOrderLineId();
    if (!orderLineId || this.posting()) return;

    this.posting.set(true);
    this.writeError.set(null);

    this.engagement
      .writeReview(this.product().id, {
        orderLineId,
        rating: Number(this.reviewRating()) || 5,
        title: this.reviewTitle().trim() || null,
        body: this.reviewBody().trim(),

        // Photographs are the media library's, and uploading one from the storefront is an upload
        // surface this app does not have. The field is sent null rather than omitted.
        images: null,
      })
      .subscribe({
        next: () => {
          this.posting.set(false);
          this.reviewOpen.set(false);
          this.toasts.success('Thank you. Your review is published once it has been moderated.');

          // Not added to the list: it is not published yet, and showing it would tell the shopper
          // that everybody can see it.
          this.eligiblePurchases.update((current) =>
            current.filter((purchase) => purchase.orderLineId !== orderLineId),
          );
        },
        error: (error: unknown) => {
          this.posting.set(false);
          this.writeError.set(describeError(error, 'Your review could not be posted.'));
        },
      });
  }

  /**
   * Asking a question.
   *
   * Offered to anyone: unlike a review, a question does not require a purchase — the people who
   * most need to ask one are the people deciding whether to buy. An anonymous visitor is refused by
   * the API and the refusal is what they read.
   */
  protected askQuestion(): void {
    this.writeError.set(null);
    this.questionBody.set('');
    this.questionOpen.set(true);
  }

  protected submitQuestion(): void {
    const body = this.questionBody().trim();
    if (!body || this.posting()) return;

    this.posting.set(true);
    this.writeError.set(null);

    this.engagement.askQuestion(this.product().id, body).subscribe({
      next: () => {
        this.posting.set(false);
        this.questionOpen.set(false);
        this.toasts.success('Asked. It appears here once it has been answered and moderated.');
      },
      error: (error: unknown) => {
        this.posting.set(false);
        this.writeError.set(describeError(error, 'Your question could not be posted.'));
      },
    });
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

    this.engagement
      .reviewEligibility(product.id)
      .subscribe((eligibility) => this.eligiblePurchases.set(eligibility.eligible));
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
