import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
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
    @for (block of blocks(); track block.id; let first = $first) {
      @switch (block.kind) {
        @case ('hero') {
          <section class="hero" [attr.data-align]="block.align">
            <kh-product-image
              [source]="block.image"
              [priority]="first && prioritiseFirst()"
              ratio="16 / 9"
              sizes="100vw"
            />
            <div class="hero-copy">
              <h2>{{ block.headline }}</h2>
              @if (block.subheadline) {
                <p>{{ block.subheadline }}</p>
              }
              @if (block.ctaHref && block.ctaLabel) {
                <a khButton variant="primary" [routerLink]="block.ctaHref">{{ block.ctaLabel }}</a>
              }
            </div>
          </section>
        }

        @case ('bannerGrid') {
          <section>
            @if (block.heading) {
              <h2>{{ block.heading }}</h2>
            }
            <kh-grid [fixedColumns]="block.columns" [gap]="3">
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
          <kh-product-carousel
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
            <kh-grid [fixedColumns]="block.columns" [gap]="3">
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
            <div [innerHTML]="block.html"></div>
          </section>
        }

        @case ('faq') {
          <section>
            <h2>{{ block.heading || 'Questions' }}</h2>
            @for (item of block.items; track item.question) {
              <kh-disclosure [heading]="item.question">
                <div [innerHTML]="item.answer"></div>
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
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--space-8);
    }

    h2 {
      font-size: var(--text-xl);
    }

    .hero {
      position: relative;
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }

    .hero-copy h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-2xl);
    }

    .hero-copy p {
      margin: 0 0 var(--space-3);
      color: var(--color-text-muted);
    }

    .hero[data-align='centre'] .hero-copy {
      text-align: center;
    }

    .hero[data-align='right'] .hero-copy {
      text-align: end;
    }

    .banner,
    .tile {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      color: var(--color-text);
      text-decoration: none;
      font-size: var(--text-sm);
      text-align: center;
    }

    .rich[data-width='narrow'] {
      max-inline-size: 68ch;
    }

    .rich :where(img) {
      border-radius: var(--radius-md);
    }

    .quote {
      margin: 0;
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
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
  /** Whether the first block may claim the LCP image. False for a page that has its own. */
  readonly prioritiseFirst = input(true);

  readonly productOpened = output<ProductCardView>();
}
