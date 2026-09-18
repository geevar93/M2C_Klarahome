import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Button } from '@klarahome/ui-primitives';

import { BannerView } from './banner.model';

/**
 * One merchandising placement's banner.
 *
 * **The highest-priority live one, and only one.** The API answers a placement's banners in
 * priority order and windowed to now, so choosing is `[0]` — the decision about which banner wins a
 * slot is a merchandiser's, made in the CMS, and a storefront that stacked them would be
 * overriding it.
 *
 * **Two images, one `<picture>`.** A mobile crop is uploaded separately because a hero that works
 * on a desktop is a letterbox on a phone; where one exists it is served below 40rem and the desktop
 * image above it. Where it does not, the desktop image is used at both sizes rather than nothing.
 *
 * **`alt` is the banner's own, and an empty one is honoured.** A banner whose words are in the
 * image needs alt text; a decorative strip beside a heading that already says it needs `alt=""`,
 * and inventing "Banner" for it would make a screen reader read a word that means nothing.
 *
 * **`width` and `height` are the image's own**, so the box is reserved before the bytes arrive.
 * The CSS still sizes it to the column; the attributes only give the browser its aspect ratio.
 *
 * It renders nothing when the placement is empty. Reserving space for a banner that is not there is
 * a hole in the page on every load of a shop that runs no campaign.
 */
@Component({
  selector: 'kh-banner-slot',
  imports: [Button, NgTemplateOutlet, RouterLink],
  template: `
    @if (banner(); as current) {
      <div class="slot">
        @if (current.link) {
          <a [routerLink]="current.link" class="media">
            <ng-container *ngTemplateOutlet="picture; context: { $implicit: current }" />
          </a>
        } @else {
          <span class="media">
            <ng-container *ngTemplateOutlet="picture; context: { $implicit: current }" />
          </span>
        }

        @if (current.link && current.ctaLabel) {
          <a khButton [routerLink]="current.link" class="cta">{{ current.ctaLabel }}</a>
        }
      </div>

      <ng-template #picture let-value>
        <picture>
          @if (value.mobileImageUrl) {
            <source
              [srcset]="value.mobileImageSrcset || value.mobileImageUrl"
              [attr.sizes]="value.mobileImageSrcset ? '100vw' : null"
              [attr.width]="value.mobileImageWidth ?? null"
              [attr.height]="value.mobileImageHeight ?? null"
              media="(max-width: 40rem)"
            />
          }
          <img
            [src]="value.imageUrl"
            [attr.srcset]="value.imageSrcset || null"
            [attr.sizes]="value.imageSrcset ? '100vw' : null"
            [attr.width]="value.imageWidth ?? null"
            [attr.height]="value.imageHeight ?? null"
            [alt]="value.altText ?? ''"
            [attr.loading]="priority() ? 'eager' : 'lazy'"
            [attr.fetchpriority]="priority() ? 'high' : null"
            decoding="async"
          />
        </picture>
      </ng-template>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .slot {
      position: relative;
      /* On the slot, not the host, so an empty placement reserves nothing (see above). */
      margin-block-start: var(--space-4);
    }

    .media {
      display: block;
    }

    img {
      display: block;
      inline-size: 100%;
      block-size: auto;
      border-radius: var(--radius-md);
    }

    .cta {
      position: absolute;
      inset-block-end: var(--space-4);
      inset-inline-start: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BannerSlot {
  /** The placement's banners, in priority order. */
  readonly banners = input<readonly BannerView[]>([]);

  /**
   * Whether this banner is the page's LCP element — the home page's masthead is.
   *
   * Loaded eagerly and fetched at high priority when it is. Lazy-loading the largest picture above
   * the fold makes the browser wait for layout before asking for it, which is the single most
   * expensive thing a page can do to its LCP. Every other placement stays lazy.
   */
  readonly priority = input(false);

  /** The one that wins the slot: the highest-priority one that actually has an image. */
  protected readonly banner = computed(() => this.banners().find((candidate) => candidate.imageUrl) ?? null);
}
