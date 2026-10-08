import { NgTemplateOutlet } from '@angular/common';
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
  imports: [Button, Disclosure, Grid, NgTemplateOutlet, ProductCarousel, ProductImage, Rating, RouterLink],
  template: `
    @for (block of visibleBlocks(); track block.id; let first = $first) {
      @switch (block.kind) {
        @case ('hero') {
          <!-- Three openers, chosen by what the block has, not by a template flag:
                 - a picture and \`align=centre\`: full-bleed, the picture is the ground and the scrim
                   sits under the copy (\`bleed\`);
                 - a picture and \`align=left|right\`: a split card, copy on one side and the picture
                   at 4:5 on the other (\`split\`);
                 - no picture: a compact, light panel in ink text with a primary CTA (\`plain\`), never
                   a tall dark slab with nothing in it.
               The overlay layout is a stacked CSS grid (every child in the same cell) rather than
               \`position: absolute\`, so the box's height is the taller of its aspect ratio and what
               the copy needs: a long headline never gets clipped, it just makes the box taller. -->
          <section
            class="hero"
            [class.hero--unbled]="!fullBleed()"
            [attr.data-align]="block.align"
            [attr.data-layout]="block.image ? (block.align === 'centre' ? 'bleed' : 'split') : 'plain'"
          >
            <ng-template #heroCopy let-cta="cta">
              <h2>{{ block.headline }}</h2>
              @if (block.subheadline) {
                <p>{{ block.subheadline }}</p>
              }
              @if (block.ctaHref && block.ctaLabel) {
                <a khButton [variant]="cta" [routerLink]="block.ctaHref">{{ block.ctaLabel }}</a>
              }
            </ng-template>

            @if (!block.image) {
              <div class="hero-plain">
                <div class="hero-copy">
                  <ng-container *ngTemplateOutlet="heroCopy; context: { cta: 'primary' }" />
                </div>
              </div>
            } @else if (block.align === 'centre') {
              <div class="hero-media">
                <kh-product-image
                  class="hero-image"
                  [source]="block.image"
                  [priority]="first && prioritiseFirst()"
                  sizes="100vw"
                />
                <div class="scrim" aria-hidden="true"></div>
                <div class="hero-copy">
                  <ng-container *ngTemplateOutlet="heroCopy; context: { cta: 'inverse' }" />
                </div>
              </div>
            } @else {
              <div class="hero-split">
                <div class="hero-copy">
                  <ng-container *ngTemplateOutlet="heroCopy; context: { cta: 'primary' }" />
                </div>
                <div class="hero-pic">
                  <kh-product-image
                    class="hero-image"
                    [source]="block.image"
                    [priority]="first && prioritiseFirst()"
                    sizes="(min-width: 1024px) 24rem, (min-width: 768px) 20rem, 100vw"
                  />
                </div>
              </div>
            }
          </section>
        }

        @case ('bannerGrid') {
          <section>
            @if (block.heading) {
              <h2 class="kh-section-heading">{{ block.heading }}</h2>
            }
            <!-- Never more columns than banners: one banner is a single full-width banner and two are
                 two halves, whatever the block's \`columns\` says. -->
            <kh-grid
              [fixedColumns]="fit(block.columns, block.items.length)"
              minColumnWidth="14rem"
              [gap]="3"
            >
              @for (item of block.items; track $index) {
                <!-- A banner without a picture is a tinted tile carrying its caption in the display
                     face. No placeholder glyph and no "No image" wording: that is an editor state and
                     belongs in the admin canvas, not on a page a shopper can see. -->
                @if (item.href) {
                  <a class="banner" [class.banner--flat]="!item.image" [routerLink]="item.href">
                    @if (item.image) {
                      <kh-product-image
                        [source]="item.image"
                        ratio="4 / 3"
                        sizes="(min-width: 768px) 33vw, 100vw"
                      />
                    }
                    @if (item.caption) {
                      <span>{{ item.caption }}</span>
                    }
                  </a>
                } @else {
                  <div class="banner" [class.banner--flat]="!item.image">
                    @if (item.image) {
                      <kh-product-image
                        [source]="item.image"
                        ratio="4 / 3"
                        sizes="(min-width: 768px) 33vw, 100vw"
                      />
                    }
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
            [limit]="block.limit ?? null"
            [viewAllHref]="block.viewAllHref"
            (opened)="productOpened.emit($event)"
          />
        }

        @case ('categoryTiles') {
          <section class="banded">
            @if (block.heading) {
              <h2 class="kh-section-heading">{{ block.heading }}</h2>
            }
            <kh-grid
              [fixedColumns]="fit(block.columns, block.items.length)"
              minColumnWidth="7rem"
              [gap]="3"
            >
              @for (tile of block.items; track tile.href) {
                <!-- With a photograph: the image and its caption beneath. Without one: a tinted tile
                     that carries the label once, in the display face. -->
                <a class="tile" [class.tile--flat]="!tile.image" [routerLink]="tile.href">
                  @if (tile.image) {
                    <kh-product-image
                      [source]="tile.image"
                      [placeholder]="tile.label"
                      ratio="4 / 3"
                      sizes="(min-width: 768px) 25vw, 45vw"
                    />
                  }
                  <span>{{ tile.label }}</span>
                </a>
              }
            </kh-grid>
          </section>
        }

        @case ('richText') {
          <section class="rich" [attr.data-width]="block.width" [attr.data-align]="block.align ?? 'start'">
            @if (block.heading) {
              <h2 class="kh-section-heading">{{ block.heading }}</h2>
            }
            <div class="rich-body" [innerHTML]="block.html"></div>
          </section>
        }

        @case ('faq') {
          <section class="banded">
            <h2 class="kh-section-heading">{{ block.heading || 'Questions' }}</h2>
            @for (item of block.items; track item.question) {
              <kh-disclosure [heading]="item.question">
                <div class="rich-body" [innerHTML]="item.answer"></div>
              </kh-disclosure>
            }
          </section>
        }

        @case ('testimonial') {
          <section class="banded">
            @if (block.heading) {
              <h2 class="kh-section-heading">{{ block.heading }}</h2>
            }
            <!-- Two quotes are two columns and three are three; four make two pairs, not a row of three
                 and an orphan. -->
            <kh-grid [fixedColumns]="quoteColumns(block.items.length)" minColumnWidth="16rem" [gap]="3">
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

    /* A tinted panel, for the blocks that are a *section of the page* rather than a grid of
       merchandise: the category tiles, the questions, the testimonials. A home page whose every
       block sits on the same ground is one flat wash from the header to the footer, which is what
       a themed storefront looked like before this — the palette was applied and had nowhere to
       show. The three kinds banded here are the ones whose content is not photography, so a tint
       behind them competes with nothing.

       A padded panel inside the container rather than a full-bleed \`.kh-band\`: the renderer does
       not own the page's container and cannot break out of it without knowing what wraps it, and a
       rounded panel reads as a band at every width without that coupling. */
    .banded {
      padding: var(--space-6) var(--space-5);
      border-radius: var(--radius-lg);
      background: var(--color-surface);
    }

    @media (min-width: 768px) {
      .banded {
        padding: var(--space-8);
      }
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

    /* Two bands in a row read as one surface: the second pulls up over the flex gap and the touching
       corners square off, so the seam is the tint continuing rather than two rounded cards a gap
       apart. A band under a hero or a bare block keeps the full section gap above it (the gap is
       the flex gap, and nothing here reduces it). */
    .banded + .banded {
      margin-block-start: calc(-1 * var(--space-section));
      border-start-start-radius: 0;
      border-start-end-radius: 0;
    }

    .banded:has(+ .banded) {
      border-end-start-radius: 0;
      border-end-end-radius: 0;
    }

    .hero {
      min-width: 0;
      /* One corner radius for every hero layout; the full-bleed one has none. */
      --hero-radius: var(--radius-lg);
    }

    /* Full-bleed: edge to edge of the viewport, out of the page container the renderer sits in. The
       renderer cannot know what wraps it, so it breaks out with the viewport arithmetic rather than
       negative margins sized to a container; \`body\` clips the sub-pixel overshoot a scrollbar's
       width would cause. The admin canvas turns it off (\`fullBleed\`) because a framed block must
       stay inside its frame. */
    .hero[data-layout='bleed'] {
      --hero-radius: 0;
    }

    .hero[data-layout='bleed']:not(.hero--unbled) {
      inline-size: 100vw;
      margin-inline: calc(50% - 50vw);
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
      border-radius: var(--hero-radius);
      /* The floor, not the ceiling: 4/5 keeps a hero that is nearly all copy from being a 200px-tall
         strip on a 360px phone. Widens to a landscape crop from 'md', capped to the viewport so the
         call to action is on screen without scrolling at any desktop size. */
      aspect-ratio: 4 / 5;
    }

    @media (min-width: 768px) {
      .hero-media {
        aspect-ratio: 21 / 9;
        max-block-size: 80dvh;
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

    /* The scrim is a desktop layer only. Below 'md' the copy is a full-width block at the bottom of
       the box and carries its own backing (\`.hero-media .hero-copy::before\`, below), so the top of
       the photograph is left alone instead of being washed flat to guarantee the text under it. */
    .scrim {
      display: none;
    }

    /* From 'md' the full-bleed hero is centred, so the gradient is darkest under the copy and fades
       out either side. The copy is \`max-inline-size: 38%\` of the box, so the 92% plateau runs from
       31% to 69% — the copy's own edges, which are also beyond the edge of its text (the padding
       sits inside those 38%). Every pixel of text therefore still sits on the full 92% tint, and the
       9.8:1 worst-case arithmetic above holds unchanged. */
    @media (min-width: 768px) {
      .scrim {
        display: block;
        background: linear-gradient(
          to right,
          transparent 15%,
          color-mix(in srgb, var(--color-surface-inverse) 92%, transparent) 31%,
          color-mix(in srgb, var(--color-surface-inverse) 92%, transparent) 69%,
          transparent 85%
        );
      }
    }

    /* The copy, in every layout: a column, with the CTA at its own width. The overlay, split and
       plain layouts each set the colour, padding and alignment around it. */
    .hero-copy {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: var(--space-3);
      min-inline-size: 0;
      text-align: start;
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

    /* The overlay copy (full-bleed). */
    .hero-media .hero-copy {
      position: relative;
      z-index: 1;
      /* Phone: the copy sits at the bottom of the box so the top of the photograph shows. */
      align-self: end;
      justify-self: center;
      align-items: center;
      text-align: center;
      /* The height of the fade above the copy on a phone, inside the copy's own box (see
         \`::before\`) — it must be padding, not a negative offset, because the box grows to fit the
         copy and a fade hanging above it would paint outside the hero once the copy is long. */
      --hero-fade: var(--space-10);
      max-inline-size: 100%;
      padding: calc(var(--space-6) + var(--hero-fade)) var(--space-5) var(--space-6);
      color: var(--color-text-on-image);
    }

    /* Phone backing. A bottom-up gradient on the copy's own box rather than a percentage of the
       hero: the copy's height varies with what an editor types, and a box-relative "opaque to 60%"
       stops covering the first line of a long headline. This one is opaque (the same 92% tint, so
       the same 9.8:1) exactly as far up as the copy goes, and the \`--hero-fade\` strip above it —
       which holds no text — is what fades into the photograph. z-index -1 stays above the photo
       and below the text because \`.hero-copy\` is its own stacking context. */
    .hero-media .hero-copy::before {
      content: '';
      position: absolute;
      inset: 0;
      z-index: -1;
      border-radius: 0 0 var(--hero-radius) var(--hero-radius);
      background: linear-gradient(
        to top,
        color-mix(in srgb, var(--color-surface-inverse) 92%, transparent) calc(100% - var(--hero-fade)),
        transparent 100%
      );
    }

    @media (min-width: 768px) {
      .hero-media .hero-copy {
        /* The scrim's plateau above ends exactly at this 38%, so the whole copy box (text and its
           padding) sits on the guaranteed-opaque zone at every width from here up, because both are
           the same kind of value (a percentage of the box). */
        max-inline-size: 38%;
        align-self: center;
        --hero-fade: 0px;
        padding: var(--space-8);
      }

      .hero-media .hero-copy::before {
        display: none;
      }
    }

    /* The ink ring is unreadable against the same ink the scrim is built from — the inverse ring,
       same as \`.kh-band--inverse\` (_base.scss) uses for the same reason on the footer. */
    .hero-media .hero-copy :where(a, button, input, select, textarea, summary, [tabindex]):focus-visible {
      outline-color: var(--color-focus-ring-inverse);
    }

    /* The split card: a light panel, copy on one side and the picture at 4:5 on the other. On a
       phone the picture comes first and is cropped shorter (4:3), so the headline is on screen
       with it; from 'md' the picture column is a fixed width, which is what keeps a 4:5 picture from
       being taller than the viewport. */
    .hero-split {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      overflow: hidden;
      border-radius: var(--hero-radius);
      background: var(--color-surface);
      color: var(--color-text);
    }

    .hero-split .hero-copy {
      justify-content: center;
      padding: var(--space-6) var(--space-5);
    }

    .hero-pic {
      display: grid;
      order: -1;
      aspect-ratio: 4 / 3;
      min-width: 0;
    }

    @media (min-width: 768px) {
      .hero-split {
        grid-template-columns: minmax(0, 1fr) minmax(0, 20rem);
      }

      .hero-split .hero-copy {
        padding: var(--space-8);
      }

      .hero-pic {
        order: 0;
        aspect-ratio: 4 / 5;
      }

      /* Mirrored when the block asks for the copy on the right. */
      .hero[data-align='right'] .hero-split {
        grid-template-columns: minmax(0, 20rem) minmax(0, 1fr);
      }

      .hero[data-align='right'] .hero-pic {
        order: -1;
      }
    }

    @media (min-width: 1024px) {
      .hero-split {
        grid-template-columns: minmax(0, 1fr) minmax(0, 24rem);
      }

      .hero[data-align='right'] .hero-split {
        grid-template-columns: minmax(0, 24rem) minmax(0, 1fr);
      }
    }

    /* No picture: a short light panel on the surface role, ink text, primary CTA. Content-sized, so
       it is as tall as its copy and about 240px at desktop; there is no ink slab to fill. */
    .hero-plain {
      display: grid;
      align-items: center;
      padding: var(--space-6) var(--space-5);
      border-radius: var(--hero-radius);
      background: var(--color-surface);
      color: var(--color-text);
    }

    @media (min-width: 768px) {
      .hero-plain {
        padding: var(--space-8);
      }
    }

    .hero-plain .hero-copy {
      max-inline-size: 40rem;
    }

    .hero[data-align='centre'] .hero-plain .hero-copy {
      justify-self: center;
      align-items: center;
      text-align: center;
    }

    .hero[data-align='right'] .hero-plain .hero-copy {
      justify-self: end;
      align-items: flex-end;
      text-align: end;
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

    /* The image-less banner and category tile: one tinted tile, the caption set in the display face
       inside it. Same shape and radius as the photographed ones, so a grid that mixes the two keeps
       its rhythm; the lift is shadow-only so a grid does not shuffle. */
    .banner--flat,
    .tile--flat {
      align-items: center;
      justify-content: center;
      aspect-ratio: 4 / 3;
      padding: var(--space-4);
      background: var(--color-surface-sunken);
      border-radius: var(--radius-lg);
      font-family: var(--font-display);
      font-size: var(--text-lg);
      font-weight: var(--weight-display);
      letter-spacing: var(--tracking-display);
      line-height: var(--leading-tight);
      transition:
        box-shadow var(--duration-base) var(--ease-standard),
        transform var(--duration-base) var(--ease-standard);
    }

    a.banner--flat:hover,
    .tile--flat:hover {
      box-shadow: var(--shadow-md);
      transform: translateY(-2px);
    }

    /* Rich text has two presentations. \`start\` (the default) sits at the page gutter on the reading
       measure, left-aligned like the content around it. \`centre\` centres the column *and* its text,
       so the paragraph and the margins either side of it agree. \`full\` needs no width rule: with
       neither set it already fills \`khContainer\`. \`narrow\` is the reading measure — the same token
       \`kh-prose\` uses. \`wide\` is the middle tier for a page with a wide photograph or a table. */
    .rich[data-width='narrow'] {
      max-inline-size: var(--measure);
    }

    .rich[data-width='wide'] {
      max-inline-size: var(--container-wide);
    }

    .rich[data-align='centre'] {
      margin-inline: auto;
      text-align: center;
    }

    .rich[data-align='centre'] .kh-section-heading::after {
      inset-inline-start: 50%;
      transform: translateX(-50%);
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
  /**
   * Whether a centred hero with a picture runs edge to edge of the viewport. True on the
   * storefront; the admin canvas turns it off because a block drawn inside a frame must stay in it.
   */
  readonly fullBleed = input(true);

  readonly productOpened = output<ProductCardView>();

  /** A grid never has more columns than it has cells, so two tiles are two halves, not two thirds. */
  protected fit(columns: number, count: number): number {
    return Math.max(1, Math.min(columns, count));
  }

  /** Quote columns: one to three across, and four make two pairs rather than three and an orphan. */
  protected quoteColumns(count: number): number {
    return count === 4 ? 2 : Math.max(1, Math.min(3, count));
  }
}
