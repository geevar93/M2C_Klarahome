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
      <div class="kh-placeholder-media ph" aria-hidden="true">
        <!-- A quiet line glyph (a frame with a horizon and a sun), drawn in \`currentColor\` so a
             theme recolours it. Deliberately not the brand mark: it has to read as "picture pending"
             on every tenant, and a tenant's own mark belongs to the tenant's header. -->
        <svg class="ph-glyph" viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" focusable="false">
          <rect x="6" y="9" width="36" height="30" rx="4" />
          <circle cx="17" cy="19" r="3" />
          <path d="M6 33l10-9 8 7 6-5 12 10" />
        </svg>
        <span class="ph-label">{{ placeholderLabel() }}</span>
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
      inline-size: 100%;
    }

    img {
      inline-size: 100%;
      /* Both overridable from an ancestor's plain CSS (not an Angular \`[style.x]\` binding, which
         would set them inline and be unbeatable by exactly the kind of external rule that needs to
         win here) — the CMS hero block is the one caller that needs the image to fill a box whose
         height *it* does not control, cropping rather than letter-boxing. Every other caller sets
         neither, and gets exactly the previous hardcoded behaviour. */
      block-size: var(--kh-image-block-size, auto);
      aspect-ratio: var(--kh-image-ratio, 1 / 1);
      object-fit: var(--kh-image-fit, contain);
      background: var(--color-surface);
      border-radius: var(--radius-md);
    }

    /* The shared \`.kh-placeholder-media\` supplies the box; this restates it as a deliberate,
       quiet tile: the sunken surface, a hairline, a muted glyph and a small label. The ratio is
       still reserved exactly as before, so the layout (and CLS) is identical to a real photograph. */
    .kh-placeholder-media {
      aspect-ratio: var(--kh-image-ratio, 1 / 1);
      inline-size: 100%;
      overflow: hidden;
    }

    .ph {
      /* The box is its own query container (its width is set by the host, never by its content, so
         inline-size containment is safe here) for the small-thumbnail rule below. */
      container-type: inline-size;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--space-2);
      padding: var(--space-3);
      background: var(--color-surface-sunken);
      border: 1px solid var(--color-border-subtle);
      color: var(--color-text-muted);
      font-size: var(--text-xs);
      text-align: center;
    }

    .ph-glyph {
      /* A share of the box, bounded, so it is a mark and not a poster on a 600px gallery stage and
         still legible inside a 3rem search-suggestion thumbnail. */
      inline-size: clamp(1.25rem, 28%, 3rem);
      block-size: auto;
      opacity: 0.55;
    }

    .ph-label {
      max-inline-size: 100%;
      color: var(--color-text-subtle);
      line-height: var(--leading-snug);
      overflow-wrap: anywhere;
      display: -webkit-box;
      -webkit-box-orient: vertical;
      -webkit-line-clamp: 2;
      line-clamp: 2;
      overflow: hidden;
    }

    /* Inside a small thumbnail (a cart line, a search suggestion) the label is noise: the glyph
       alone says "no picture", and the product's name is already beside it. */
    @container (max-inline-size: 6rem) {
      .ph-label {
        display: none;
      }
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
