import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { CmsBlockRenderer, CmsBlockView, ProductCardView } from '@klarahome/ui-patterns';
import { SeoService } from '@klarahome/util';

/**
 * A hostile-content stress fixture for `CmsBlockRenderer`, rendered at a real route so it can be
 * driven by a real browser at every breakpoint — `apps/storefront-e2e/src/cms-blocks-stress
 * .responsive.spec.ts`.
 *
 * **Why a route, not an intercepted API response.** The obvious way to test "what does a CMS page
 * with hostile content look like" is to fetch `/pages/:slug` and stub the API response — but the
 * CMS page route resolves its content on the server (`cmsPageResolver`), and Playwright's
 * `page.route()` can only intercept requests the *browser* makes, not the ones the storefront's
 * own SSR process makes to the API container directly over the docker network. A page authored
 * through the real admin CMS hits the same wall from the other side: `platform-admin` is a
 * mandatory-two-factor role, this account's authenticator is already enrolled from earlier work,
 * and neither re-enrolling nor resetting it is something a stress-test fixture should be doing to
 * shared dev credentials. This route sidesteps both: the fixture below is passed to
 * `CmsBlockRenderer` directly, as a literal, so there is no API round trip to intercept and nothing
 * to authenticate — the same component, the same global styles, the same real browser layout
 * engine, with content deliberately chosen to be worse than anything a merchant is likely to type.
 *
 * **Not linked from anywhere, and not indexable.** `noIndex: true`, same as every other utility
 * route in this file (403/404/500/offline) — this exists for the test suite, not for a shopper.
 */
@Component({
  selector: 'kh-cms-stress-test-page',
  imports: [CmsBlockRenderer],
  template: `<kh-cms-block-renderer [blocks]="blocks" [prioritiseFirst]="false" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CmsStressTestPage {
  protected readonly blocks = STRESS_BLOCKS;

  constructor() {
    inject(SeoService).apply({ title: 'CMS block stress fixture', noIndex: true });
  }
}

// ---- Shared hostile fragments ---------------------------------------------------------------

/** A single unbroken 140-character run — no spaces for `overflow-wrap` to break on except mid-word. */
const LONG_UNBROKEN_WORD =
  'Supercalifragilisticexpialidociousandthenevenlongerthanthatbecauseamerchantwilltypewhatevertheylikeandneverabreakinit';

/** A URL, which is the other common unbroken-run case a caption or a rich-text paragraph carries. */
const LONG_URL =
  'https://example.com/catalogue/home-and-living/soft-furnishings/cushions-and-throws/this-is-an-extremely-long-path-segment-with-no-spaces-anywhere-in-it';

const STOCK_IMAGE = {
  src: '/brand/og-default.png',
  srcset: '',
  width: 1200,
  height: 630,
  alt: 'A stress-test stand-in photograph',
};

function banner(caption: string | null, href: string | null = '/c/cushions') {
  return { image: STOCK_IMAGE, caption, href };
}

function tile(label: string, index: number) {
  return { label, href: `/c/stress-${index}`, image: index % 3 === 0 ? null : STOCK_IMAGE };
}

function product(index: number, overrides: Partial<ProductCardView> = {}): ProductCardView {
  return {
    variantId: `stress-variant-${index}`,
    productId: `stress-product-${index}`,
    listingId: `stress-listing-${index}`,
    name: index === 2 ? LONG_UNBROKEN_WORD : `Stress-test product ${index} — a reasonably named item`,
    href: `/p/stress-${index}`,
    brand: index % 4 === 0 ? null : 'Stress & Co.',
    price: index % 5 === 0 ? null : { amount: 999 + index * 111, currency: 'INR' },
    mrp: index % 3 === 0 ? null : { amount: 1499 + index * 111, currency: 'INR' },
    image: index % 6 === 0 ? null : STOCK_IMAGE,
    rating: index % 4 === 0 ? null : 3.5 + (index % 2) * 0.5,
    ratingCount: index * 7,
    isPurchasable: index % 7 !== 0,
    reference: `SKU-${1000 + index}`,
    ...overrides,
  };
}

const testimonialQuotes = [
  'Good.',
  'Exactly what I wanted — arrived fast, looked even better in person than in the photos, and the packaging alone made it feel like a proper gift rather than something ordered off a screen at midnight.',
  'Three words: buy it now.',
  `This review is deliberately long, because a merchandiser writing a testimonial block does not know or care that a design system somewhere has a "measure" it would prefer text stayed under, and the layout has to hold regardless — it should wrap onto as many lines as it needs to, inside a card that never stretches to some awkward, disproportionate width just because it happens to be the only testimonial in its row, and it should never overflow the card's own edge no matter how many sentences a genuinely enthusiastic customer decides to write about a cushion cover.`,
  'Not bad.',
];

// ---- The blocks -------------------------------------------------------------------------------

export const STRESS_BLOCKS: readonly CmsBlockView[] = [
  // Hero: a very long headline (word-wrap must hold), left-aligned, with an image and a CTA.
  {
    kind: 'hero',
    id: 'hero-left-long',
    headline: `Everything In This Sofa Collection Is On Sale Until The End Of The Month, ${LONG_UNBROKEN_WORD}`,
    subheadline:
      'A subheadline long enough to wrap onto several lines on its own, sitting underneath a headline that has already forced the box taller than any 16:9 or 4:5 photograph would otherwise be.',
    image: STOCK_IMAGE,
    ctaLabel: 'Shop the sale',
    ctaHref: '/c/cushions',
    align: 'left',
  },
  // Hero: centre-aligned, image, moderate copy — the middle case between the two extremes either
  // side of it.
  {
    kind: 'hero',
    id: 'hero-centre',
    headline: 'New in: hand-loomed textiles',
    subheadline: 'A shorter, more typical headline and subheadline pair, centred.',
    image: STOCK_IMAGE,
    ctaLabel: 'Explore',
    ctaHref: '/c/cushions',
    align: 'centre',
  },
  // Hero: right-aligned, with the long URL rather than the long word, in the subheadline this time.
  {
    kind: 'hero',
    id: 'hero-right-url',
    headline: 'Free delivery this weekend',
    subheadline: LONG_URL,
    image: STOCK_IMAGE,
    ctaLabel: null,
    ctaHref: null,
    align: 'right',
  },
  // Hero: no image at all — the flat, token-surfaced treatment.
  {
    kind: 'hero',
    id: 'hero-no-image',
    headline: 'A hero with no photograph yet',
    subheadline: 'A merchandiser who has not uploaded an image still gets a deliberate hero, not a blank strip.',
    image: null,
    ctaLabel: 'Browse the catalogue',
    ctaHref: '/c/cushions',
    align: 'centre',
  },

  // bannerGrid: the extremes of the columns×items matrix the brief named.
  { kind: 'bannerGrid', id: 'banners-1x1', heading: '1 column, 1 item', columns: 1, items: [banner('A single banner')] },
  {
    kind: 'bannerGrid',
    id: 'banners-6x1',
    heading: '6 columns configured, 1 item',
    columns: 6,
    items: [banner('One banner in a six-column block — must not stretch to the full row width')],
  },
  {
    kind: 'bannerGrid',
    id: 'banners-2x2',
    heading: '2 columns, 2 items',
    columns: 2,
    items: [banner('First'), banner(LONG_UNBROKEN_WORD)],
  },
  {
    kind: 'bannerGrid',
    id: 'banners-4x12',
    heading: '4 columns configured, 12 items',
    columns: 4,
    items: Array.from({ length: 12 }, (_, i) => banner(i === 3 ? null : `Banner ${i + 1}`, i % 5 === 0 ? null : '/c/cushions')),
  },

  // categoryTiles: same matrix idea, smaller floor, some tiles with no image (placeholder path).
  { kind: 'categoryTiles', id: 'tiles-1x1', heading: '1 column, 1 item', columns: 1, items: [tile('Cushions', 1)] },
  {
    kind: 'categoryTiles',
    id: 'tiles-6x2',
    heading: '6 columns configured, 2 items',
    columns: 6,
    items: [tile('Rugs', 1), tile(LONG_UNBROKEN_WORD, 2)],
  },
  {
    kind: 'categoryTiles',
    id: 'tiles-3x12',
    heading: '3 columns configured, 12 items',
    columns: 3,
    items: Array.from({ length: 12 }, (_, i) => tile(`Category ${i + 1}`, i)),
  },
  {
    kind: 'categoryTiles',
    id: 'tiles-2x5-long',
    heading: '2 columns, 5 items, one with a very long label',
    columns: 2,
    items: Array.from({ length: 5 }, (_, i) => tile(i === 2 ? LONG_UNBROKEN_WORD : `Shop by room ${i + 1}`, i)),
  },

  // richText: an 8-column table, an image, a blockquote, lists, and the long URL — at each width tier.
  {
    kind: 'richText',
    id: 'rich-full-everything',
    heading: 'A merchant-authored page, at full width',
    width: 'full',
    html: `
      <p>A paragraph carrying an unbroken run: ${LONG_URL}</p>
      <h3>A heading inside the body</h3>
      <ul>
        <li>A short list item</li>
        <li>${LONG_UNBROKEN_WORD}</li>
        <li>A third item, of ordinary length, for comparison</li>
      </ul>
      <blockquote>A blockquote, which gets its own left rule and muted colour from the same tokens as the testimonial block's quote card.</blockquote>
      <img src="/brand/og-default.png" alt="An embedded image inside rich text" width="1200" height="630" />
      <table>
        <thead>
          <tr><th>Material</th><th>Weave</th><th>Width</th><th>Weight (gsm)</th><th>Colourfastness</th><th>Origin</th><th>Certification</th><th>Care</th></tr>
        </thead>
        <tbody>
          <tr><td>Linen</td><td>Plain</td><td>140cm</td><td>210</td><td>Grade 4</td><td>India</td><td>GOTS</td><td>Machine wash cold</td></tr>
          <tr><td>Cotton</td><td>Twill</td><td>150cm</td><td>180</td><td>Grade 3</td><td>India</td><td>OEKO-TEX</td><td>Hand wash</td></tr>
        </tbody>
      </table>
    `,
  },
  {
    kind: 'richText',
    id: 'rich-wide',
    heading: 'The same block at the wide tier',
    width: 'wide',
    html: `<p>Capped at <code>var(--container-wide)</code> and centred, rather than the full container.</p>`,
  },
  {
    kind: 'richText',
    id: 'rich-narrow',
    heading: 'And at the narrow, reading-measure tier',
    width: 'narrow',
    html: `<p>Capped at <code>var(--measure)</code> and centred — the tightest of the three, for a policy page or a long-form article.</p>`,
  },

  // faq: a long list, one very long answer with a nested list and an unbroken run.
  {
    kind: 'faq',
    id: 'faq-long',
    heading: 'Questions',
    items: [
      { question: 'What is your returns policy?', answer: '<p>Thirty days, unworn, with the original tags.</p>' },
      { question: 'Do you ship internationally?', answer: '<p>Not yet — India only for now.</p>' },
      { question: 'How long does delivery take?', answer: '<p>Three to seven business days, depending on the pincode.</p>' },
      { question: 'Is cash on delivery available?', answer: '<p>Yes, on orders under a threshold your store settings define.</p>' },
      {
        question: 'This is a much longer question than the others, on purpose, to see whether the disclosure summary wraps cleanly onto two lines without pushing the chevron marker off the row',
        answer: `<p>And this is a correspondingly long answer, containing an unbroken run — ${LONG_UNBROKEN_WORD} — and a list:</p><ul><li>First consideration</li><li>Second consideration</li><li>Third, with a link-shaped run: ${LONG_URL}</li></ul>`,
      },
      { question: 'Can I change my address after ordering?', answer: '<p>Before it ships, yes — contact support.</p>' },
      { question: 'Do you offer gift wrapping?', answer: '<p>At checkout, for a small additional charge.</p>' },
      { question: 'What payment methods do you accept?', answer: '<p>Cards, UPI, netbanking and wallets via Razorpay.</p>' },
    ],
  },

  // testimonial: very different lengths, missing ratings, missing locations.
  {
    kind: 'testimonial',
    id: 'testimonials-mixed',
    heading: 'What people say',
    items: [
      { quote: testimonialQuotes[0], author: 'A.', location: null, rating: null },
      { quote: testimonialQuotes[1], author: 'Priya Sharma', location: 'Bengaluru', rating: 5 },
      { quote: testimonialQuotes[2], author: 'R.', location: null, rating: 4 },
      { quote: testimonialQuotes[3], author: 'A Customer Who Writes At Considerable Length', location: 'Somewhere with a fairly long place name attached to it', rating: 3.5 },
      { quote: testimonialQuotes[4], author: 'M.', location: 'Pune', rating: null },
    ],
  },

  // productCarousel: a realistic rail, with the same edge cases (no image, no price, no rating,
  // an unbroken product name) product-grid already has to survive.
  {
    kind: 'productCarousel',
    id: 'carousel-stress',
    heading: 'You might also like',
    viewAllHref: '/c/cushions',
    products: Array.from({ length: 8 }, (_, i) => product(i)),
  },
];
