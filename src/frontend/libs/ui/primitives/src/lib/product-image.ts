import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ImageSource } from '@klarahome/util';

/**
 * A product image, or the grey box that stands in for one.
 *
 * Every catalogue image on the storefront goes through here, for three reasons that are all
 * performance rather than looks:
 *
 *  - **The box is reserved before the bytes arrive.** `width`, `height` and an `aspect-ratio` are
 *    always set, so a grid of forty cards does not reflow as the images land — that is the whole
 *    of the 0.1 CLS budget (docs/05-frontend-architecture.md §3.4).
 *  - **`loading` and `fetchpriority` are decided by the caller**, because only the caller knows
 *    whether this is the LCP element. The default is lazy; the PDP's first gallery image and the
 *    home page's first block opt into `priority`, and nothing else may.
 *  - **A missing image is a first-class state.** No resizer configured, or a file the CMS lost:
 *    the placeholder renders at the same size, so the layout is identical either way.
 *
 * `alt` is required and comes from the source. An image whose alt is genuinely decorative passes
 * an empty string, which is a decision, not an omission.
 */
@Component({
  selector: 'kh-product-image',
  template: `
    @if (source(); as image) {
      <img
        [attr.src]="image.src"
        [attr.srcset]="image.srcset || null"
        [attr.sizes]="image.srcset ? sizes() : null"
        [attr.alt]="image.alt"
        [attr.width]="image.width"
        [attr.height]="image.height"
        [attr.loading]="priority() ? 'eager' : 'lazy'"
        [attr.fetchpriority]="priority() ? 'high' : null"
        [attr.decoding]="priority() ? 'sync' : 'async'"
      />
    } @else {
      <!-- \`aria-hidden\`: the name of the thing is always beside the box, and "no image" is not
           information a screen-reader user needs read to them once per card. -->
      <div class="kh-placeholder-media" aria-hidden="true">{{ placeholderLabel() }}</div>
    }
  `,
  styles: `
    :host {
      display: block;
      inline-size: 100%;
    }

    img {
      inline-size: 100%;
      block-size: auto;
      aspect-ratio: var(--kh-image-ratio, 1 / 1);
      object-fit: contain;
      background: var(--color-surface);
      border-radius: var(--radius-md);
    }

    .kh-placeholder-media {
      aspect-ratio: var(--kh-image-ratio, 1 / 1);
      inline-size: 100%;
      font-size: var(--text-xs);
      text-align: center;
      padding: var(--space-2);
      overflow: hidden;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[style.--kh-image-ratio]': 'ratio()' },
})
export class ProductImage {
  readonly source = input<ImageSource | null>(null);
  /** The `sizes` attribute. The default matches a card in the auto-fill product grid. */
  readonly sizes = input('(min-width: 1024px) 20rem, (min-width: 768px) 33vw, 50vw');
  /** CSS `aspect-ratio`. Square for a catalogue image; a banner sets its own. */
  readonly ratio = input('1 / 1');
  /**
   * Eagerly loaded and fetched first. **At most one image per page may set this** — it marks the
   * LCP element, and marking several is the same as marking none.
   */
  readonly priority = input(false);
  /** What the placeholder box says. A SKU or a short name, so the box is not anonymous. */
  readonly placeholder = input<string | null>(null);

  protected readonly placeholderLabel = computed(() => this.placeholder()?.slice(0, 40) ?? 'No image');
}
