import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Grid } from '@klarahome/ui-layout';
import { Button, Disclosure, ProductImage, Rating } from '@klarahome/ui-primitives';

import { CmsBlockView, ProductCardView } from './catalog.model';
import { ProductCarousel } from './product-carousel';

/**
 * A CMS page, drawn from its blocks.
 *
 * The home page and every editorial page in the shop is an ordered list of typed blocks (Step 20),
 * and this is the one component that turns that list into a page. Three properties matter:
 *
 *  - **The union is closed, and the unknown case is silent.** An editor publishing a block type a
 *    deployed storefront does not know about gets nothing where that block was — not an error, not
 *    a "coming soon" box. The rest of the page is unaffected, which is the behaviour a
 *    merchandiser needs during a rollout.
 *  - **Only the first block may hold the LCP image.** A home page with four heroes marking
 *    themselves `fetchpriority="high"` is a home page with no priority at all.
 *  - **Rich text is not `innerHTML` here.** The API sanitises on write (Step 20) and the block's
 *    body arrives as trusted markup; it is still rendered through Angular's own sanitiser by
 *    binding `[innerHTML]`, which strips scripts and event handlers a second time. `CustomHtml`
 *    blocks are privileged on the API and never reach this union.
 */
@Component({
  selector: 'kh-cms-block-renderer',
  imports: [Button, Disclosure, Grid, ProductCarousel, ProductImage, Rating, RouterLink],
  template: `
    @for (block of visibleBlocks(); track block.id; let first = $first) {
      @switch (block.kind) {
        @case ('hero') {
          <!-- Text over the photograph, not stacked above it: a stacked CSS grid (every child in
               the same cell) rather than \`position: absolute\`, so the box's height is the taller of
               the image's own aspect ratio and whatever the copy actually needs — a long headline,
               a subheadline and a CTA never get clipped or overlap, they just make the box taller
               and the photo crops a little further instead. -->
          <section class="hero" [attr.data-align]="block.align">
            @if (block.image) {
              <div class="hero-media">
                <kh-product-image
                  class="hero-image"
                  [source]="block.image"
                  [priority]="first && prioritiseFirst()"
                  sizes="100vw"
                />
                <div class="scrim" aria-hidden="true"></div>
                <div class="hero-copy">
                  <h2>{{ block.headline }}</h2>
                  @if (block.subheadline) {
                    <p>{{ block.subheadline }}</p>
                  }
                  @if (block.ctaHref && block.ctaLabel) {
                    <a khButton variant="inverse" [routerLink]="block.ctaHref">{{ block.ctaLabel }}</a>
                  }
                </div>
              </div>
            } @else {
              <!-- No photograph: the same copy, on the same dark surface the scrim above guarantees
                   contrast against — a merchandiser who has not uploaded an image yet still gets a
                   deliberate hero, not a blank strip. -->
              <div class="hero-media hero-media--flat">
                <div class="hero-copy">
                  <h2>{{ block.headline }}</h2>
                  @if (block.subheadline) {
                    <p>{{ block.subheadline }}</p>
                  }
                  @if (block.ctaHref && block.ctaLabel) {
                    <a khButton variant="inverse" [routerLink]="block.ctaHref">{{ block.ctaLabel }}</a>
                  }
                </div>
              </div>
            }
          </section>
        }

        @case ('bannerGrid') {
          <section>
            @if (block.heading) {
              <h2>{{ block.heading }}</h2>
            }
            <kh-grid [fixedColumns]="block.columns" minColumnWidth="14rem" [gap]="3">
              @for (item of block.items; track $index) {
                @if (item.href) {
                  <a class="banner" [routerLink]="item.href">
                    <kh-product-image
                      [source]="item.image"
                      ratio="4 / 3"
                      sizes="(min-width: 768px) 33vw, 100vw"
                    />
                    @if (item.caption) {
                      <span>{{ item.caption }}</span>
                    }
                  </a>
                } @else {
                  <div class="banner">
                    <kh-product-image
                      [source]="item.image"
                      ratio="4 / 3"
                      sizes="(min-width: 768px) 33vw, 100vw"
                    />
                    @if (item.caption) {
                      <span>{{ item.caption }}</span>
                    }
                  </div>
                }
              }
            </kh-grid>
          </section>
        }

        @case ('productCarousel') {
          <!-- \`margin-block: 0\`: \`kh-product-carousel\` carries its own \`margin-block\` for the
               contexts where it stands alone (the PDP and the home page's "recently viewed" rail).
               As a block here it is already one flex item among others in a \`gap\`-based column, and
               a flex item's margin never collapses with that gap — without this override, a
               carousel block sat next to a hero or a rich-text block twice the rhythm of any other
               two consecutive blocks. -->
          <kh-product-carousel
            style="margin-block: 0"
            [heading]="block.heading || 'Featured'"
            [products]="block.products"
            [viewAllHref]="block.viewAllHref"
            (opened)="productOpened.emit($event)"
          />
        }

        @case ('categoryTiles') {
          <section>
            @if (block.heading) {
              <h2>{{ block.heading }}</h2>
            }
            <kh-grid [fixedColumns]="block.columns" minColumnWidth="7rem" [gap]="3">
              @for (tile of block.items; track tile.href) {
                <a class="tile" [routerLink]="tile.href">
                  <kh-product-image
                    [source]="tile.image"
                    [placeholder]="tile.label"
                    sizes="(min-width: 768px) 20vw, 45vw"
                  />
                  <span>{{ tile.label }}</span>
                </a>
              }
            </kh-grid>
          </section>
        }

        @case ('richText') {
          <section class="rich" [attr.data-width]="block.width">
            @if (block.heading) {
              <h2>{{ block.heading }}</h2>
            }
            <div class="rich-body" [innerHTML]="block.html"></div>
          </section>
        }

        @case ('faq') {
          <section>
            <h2>{{ block.heading || 'Questions' }}</h2>
            @for (item of block.items; track item.question) {
              <kh-disclosure [heading]="item.question">
                <div class="rich-body" [innerHTML]="item.answer"></div>
              </kh-disclosure>
            }
          </section>
        }

        @case ('testimonial') {
          <section>
            @if (block.heading) {
              <h2>{{ block.heading }}</h2>
            }
            <kh-grid minColumnWidth="16rem" [gap]="3">
              @for (item of block.items; track item.quote) {
                <figure class="quote">
                  <blockquote>{{ item.quote }}</blockquote>
                  @if (item.rating !== null) {
                    <kh-rating size="sm" [average]="item.rating" />
                  }
                  <figcaption>{{ item.author }}{{ item.location ? ', ' + item.location : '' }}</figcaption>
                </figure>
              }
            </kh-grid>
          </section>
        }
      }
    }
  `,
  styles: `
    /* One rhythm for the gap between blocks, regardless of which two kinds sit next to each other —
       \`--space-section\` is the token the design system already defines for "the gap between one
       band of a page and the next" (docs/10-design-system.md §5 / _tokens.scss), so a home page
       assembled as hero → banner grid → carousel breathes the same as one assembled in any other
       order. */
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--space-section);
    }

    h2 {
      font-size: var(--text-xl);
    }

    /* A merchant's headline, caption, quote or label is free text with no length limit on the CMS
       side. Every text-bearing element in a block gets to break mid-word rather than push the
       block wider than its container — the single most common way user content overflows a layout
       the developer's own seed content never would. */
    h2,
    p,
    span,
    figcaption,
    blockquote {
      overflow-wrap: anywhere;
    }

    .hero {
      min-width: 0;
    }

    /* No \`overflow\` here, deliberately. A box with an \`aspect-ratio\` only grows past that ratio to
       fit its content (\`min-height: auto\` resolving to the content's min-content size) while it is
       not a scroll container — and \`overflow: hidden\` makes it one. With it, the ratio became a
       fixed height and a long headline's subheadline and CTA were clipped at the bottom edge at
       every breakpoint (caught by the stress spec's clipping assertion, not by the overflow one).
       The rounded corners are clipped on the layers that fill the box instead (\`.hero-image\`,
       \`.scrim\`). */
    .hero-media {
      position: relative;
      display: grid;
      border-radius: var(--radius-lg);
      /* The floor, not the ceiling: 4/5 keeps a hero that is nearly all copy from being a 200px-tall
         strip on a 360px phone (16/9 at 360px is ~202px — not enough room for a headline, a
         subheadline and a CTA without the text already crowding the photo before a merchant has
         typed a word). Widens to the landscape crop from 'md', where there is width to spend on it. */
      aspect-ratio: 4 / 5;
    }

    @media (min-width: 768px) {
      .hero-media {
        aspect-ratio: 16 / 9;
      }
    }

    /* Every direct child shares the one cell — the image, the scrim and the copy are layers, not a
       stack of boxes, which is what makes the row's height "whichever of them needs the most",
       automatically, with no JS measuring anything. */
    .hero-media > * {
      grid-area: 1 / 1;
    }

    .hero-image {
      /* Ancestor-set custom properties, not inputs: see the comment on \`img\` in \`product-image.ts\`.
         Fills the cell — whatever height the copy ends up forcing it to — and crops rather than
         letter-boxing, which \`object-fit: contain\` (every other caller's default) would do. */
      --kh-image-block-size: 100%;
      --kh-image-fit: cover;
      min-width: 0;
      /* The photograph must not size the box, only fill it: without \`contain: size\` its own
         aspect ratio (\`kh-product-image\` defaults to square) would count towards the row's height,
         and a 1248px-wide desktop hero would be 1248px tall. The copy and the box's ratio decide
         the height; the image is stretched to it and cropped by \`object-fit: cover\`. */
      contain: size;
      overflow: hidden;
      border-radius: inherit;
    }

    /* A flat, no-photograph hero gets the same dark surface the scrim guarantees contrast against,
       so the copy reads identically either way. Sized generously rather than aspect-ratio-locked:
       there is no photograph's proportions to respect, only the copy's own rhythm. */
    .hero-media--flat {
      /* Still a (single-cell) grid, not flex: \`.hero-copy\`'s \`justify-self\`/\`align-self\` below is
         how both this branch and the photograph branch position the copy, and \`justify-self\` is not
         reliably a flex-item property. */
      align-items: center;
      aspect-ratio: auto;
      min-block-size: 18rem;
      padding: var(--space-8) var(--space-6);
      background: var(--color-surface-inverse);
    }

    @media (min-width: 768px) {
      .hero-media--flat {
        min-block-size: 22rem;
      }
    }

    /* The scrim. Guarantees WCAG AA for --color-text-on-image against a *worst-case pure-white*
       photograph — verified, not assumed: --color-surface-inverse (ink-900) has a relative
       luminance of ~0.0099, and blending it at 92% over a pure-white background (linear-light, per
       WCAG's own maths) leaves a background luminance of ~0.083. --color-text-on-image
       (sand-050, luminance ~0.926) against that clears 9.8:1 — comfortably past the 4.5:1 a
       subheadline needs and the 3:1 a large headline needs, with margin for a photo that is merely
       very light rather than literally blank, and for the softer, partially-transparent edge of the
       gradient below. 92%, not 100%: the point is the photograph still reads as a photograph. */
    .scrim {
      border-radius: inherit;
      background: color-mix(in srgb, var(--color-surface-inverse) 92%, transparent);
    }

    /* Below 'md' the copy runs close to the full width of the box (see \`.hero-copy\`'s own rule),
       so the scrim is the same flat, fully-guaranteed tint everywhere — a left/right gradient sized
       for a narrower desktop column would not still be safely opaque under a full-width mobile one.
       From 'md' the gradient follows \`align\`: darkest under the copy, fading out on the side
       nothing sits on. Every stop is a percentage of the box, matched to \`.hero-copy\`'s own
       percentage width below, not a fixed length — so the relationship holds at any width in this
       tier, not only at the breakpoint's own edge. */
    @media (min-width: 768px) {
      .hero[data-align='left'] .scrim {
        background: linear-gradient(
          to right,
          color-mix(in srgb, var(--color-surface-inverse) 92%, transparent) 0%,
          color-mix(in srgb, var(--color-surface-inverse) 92%, transparent) 48%,
          transparent 80%
        );
      }

      .hero[data-align='right'] .scrim {
        background: linear-gradient(
          to left,
          color-mix(in srgb, var(--color-surface-inverse) 92%, transparent) 0%,
          color-mix(in srgb, var(--color-surface-inverse) 92%, transparent) 48%,
          transparent 80%
        );
      }

      .hero[data-align='centre'] .scrim {
        background: linear-gradient(
          to right,
          transparent 5%,
          color-mix(in srgb, var(--color-surface-inverse) 92%, transparent) 22%,
          color-mix(in srgb, var(--color-surface-inverse) 92%, transparent) 78%,
          transparent 95%
        );
      }
    }

    .hero-copy {
      position: relative;
      z-index: 1;
      display: flex;
      flex-direction: column;
      align-self: center;
      justify-self: start;
      /* The CTA keeps its own width, as it does in a centred or right-aligned hero, instead of
         stretching across the whole copy column — a 400px button on a desktop left-aligned hero. */
      align-items: flex-start;
      gap: var(--space-3);
      max-inline-size: 100%;
      padding: var(--space-6) var(--space-5);
      color: var(--color-text-on-image);
      text-align: start;
    }

    @media (min-width: 768px) {
      .hero-copy {
        /* Matched to the scrim's own plateau above: 38% stays inside the 0–48%
           (left) / 52–100% (right) / 22–78% (centre) guaranteed-opaque zone with margin at every
           width from here up, because both are the same kind of value (a percentage of the box). */
        max-inline-size: 38%;
        padding: var(--space-8);
      }
    }

    .hero-media--flat .hero-copy {
      padding: 0;
    }

    .hero[data-align='centre'] .hero-copy {
      justify-self: center;
      text-align: center;
      align-items: center;
    }

    .hero[data-align='right'] .hero-copy {
      justify-self: end;
      text-align: end;
      align-items: flex-end;
    }

    .hero-copy h2 {
      margin: 0;
      font-size: var(--text-display-sm);
      font-family: var(--font-display);
      font-weight: var(--weight-display);
      letter-spacing: var(--tracking-display);
      line-height: var(--leading-tight);
    }

    .hero-copy p {
      margin: 0;
    }

    /* The ink ring is unreadable against the same ink the scrim is built from — the inverse ring,
       same as \`.kh-band--inverse\` (_base.scss) uses for the same reason on the footer. */
    .hero-copy :where(a, button, input, select, textarea, summary, [tabindex]):focus-visible {
      outline-color: var(--color-focus-ring-inverse);
    }

    .banner,
    .tile {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      /* Grid items default to \`min-width: auto\`, which lets an unbroken caption push a narrow
         column wider than its track and overflow the grid. */
      min-width: 0;
      color: var(--color-text);
      text-decoration: none;
      font-size: var(--text-sm);
      text-align: center;
    }

    /* Three width tiers, all centred the same way — \`narrow\` and \`wide\` inset from the full-width
       blocks around them symmetrically rather than hugging the left edge with a growing gap on the
       right, which is what a bare \`max-inline-size\` with no \`margin-inline\` would do. \`full\` needs
       no rule of its own: with neither set it already fills \`khContainer\`, same as every other
       block. \`narrow\` is the reading measure — the same token \`kh-prose\`/\`khContainer\`'s own
       narrow size use, not a second literal for this component to drift out of step with if the
       token ever changes. \`wide\` is the new middle tier: a merchandising page with a wide
       photograph or a table that still should not run the full 1280px a listing page needs. */
    .rich[data-width='narrow'],
    .rich[data-width='wide'] {
      margin-inline: auto;
    }

    .rich[data-width='narrow'] {
      max-inline-size: var(--measure);
    }

    .rich[data-width='wide'] {
      max-inline-size: var(--container-wide);
    }

    /* \`.rich-body\` itself is fine as a scoped style — Angular renders this \`<div>\`, so it carries
       the usual \`_ngcontent\` scoping attribute. What it wraps via \`[innerHTML]\` is a different
       story: content set through \`innerHTML\` is parsed by the browser directly and never receives
       an \`_ngcontent\` attribute, so a scoped selector reaching for one of its descendants —
       \`.rich-body :where(table)\`, \`.rich-body :where(img)\`, and so on — compiles to
       \`.rich-body[_ngcontent-X] :where(table[_ngcontent-X])\` and can **never match**, because the
       table has no such attribute to match against. Verified by hand: every one of those rules sat
       in this file, compiled correctly, and never once applied — an 8-column stress table's headers
       rendered as unstyled, un-widthed browser defaults with no warning of any kind, from either
       Angular or the browser. \`.kh-prose\` (\`_typography.scss\`) solved the identical problem for
       product descriptions and policy pages the same way this now does: the descendant rules moved
       to \`_base.scss\`, globally, under \`.rich-body\` — unscoped CSS has no \`_ngcontent\` requirement
       to fail to match. Only the container's own rule stays here. */
    .rich-body {
      overflow-wrap: anywhere;
      min-width: 0;
    }

    .quote {
      margin: 0;
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      min-width: 0;
    }

    .quote blockquote {
      margin: 0 0 var(--space-2);
    }

    figcaption {
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CmsBlockRenderer {
  readonly blocks = input.required<readonly CmsBlockView[]>();

  /**
   * The blocks worth drawing. A block whose whole content is a list that resolved to nothing — a
   * category-tiles block none of whose categories resolved, a carousel whose products have all
   * left the index — is skipped the same way an unknown kind is: a heading over empty space reads
   * as a broken page to a shopper, and the rest of the document is unaffected. The merchant still
   * sees the block, empty, in the admin composer, which is where it can be fixed.
   */
  protected readonly visibleBlocks = computed(() =>
    this.blocks().filter((block) => {
      switch (block.kind) {
        case 'bannerGrid':
        case 'categoryTiles':
        case 'faq':
        case 'testimonial':
          return block.items.length > 0;
        case 'productCarousel':
          return block.products.length > 0;
        default:
          return true;
      }
    }),
  );
  /** Whether the first block may claim the LCP image. False for a page that has its own. */
  readonly prioritiseFirst = input(true);

  readonly productOpened = output<ProductCardView>();
}
