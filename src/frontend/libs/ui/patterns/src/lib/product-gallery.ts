import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  linkedSignal,
  signal,
} from '@angular/core';
import { ProductImage } from '@klarahome/ui-primitives';

import { GalleryImageView } from './catalog.model';

/**
 * The product's images: one large, the rest as thumbnails.
 *
 * **Zoom is a CSS transform on tap, not a lightbox.** A modal image viewer on a phone is a second
 * focus trap, a second Escape handler and another 6 KB of JavaScript, and what a shopper actually
 * wants is to see the weave of the fabric. Tapping the main image scales it about the point that
 * was tapped; tapping again releases it. On a desktop the same transform follows the pointer.
 * There is nothing to trap and nothing to dismiss.
 *
 * The thumbnails are a tab list in behaviour but plain buttons in markup, deliberately: a real
 * `role="tablist"` obliges arrow-key navigation between the thumbnails and takes them out of the
 * tab order, which on a gallery of eight images is worse than eight ordinary buttons.
 *
 * Only the first image is `priority` — it is the PDP's LCP element on nearly every product page.
 */
@Component({
  selector: 'kh-product-gallery',
  imports: [ProductImage],
  template: `
    <!-- A real button, so Enter, Space and the button role come from the platform rather than
         from three handlers and a \`tabindex\` that would have to be kept in step with them. -->
    <button
      type="button"
      class="stage"
      [class.zoomed]="zoomed()"
      [attr.aria-pressed]="zoomed()"
      [attr.aria-label]="zoomed() ? 'Zoom out' : 'Zoom in on the product image'"
      (click)="toggleZoom($event)"
      (mousemove)="trackPointer($event)"
    >
      <kh-product-image
        [source]="current()?.source ?? null"
        [placeholder]="productName()"
        [priority]="true"
        sizes="(min-width: 1024px) 32rem, 100vw"
      />
    </button>

    @if (images().length > 1) {
      <ul class="thumbs" aria-label="Product images">
        @for (image of images(); track image.id; let index = $index) {
          <li>
            <button
              type="button"
              class="thumb"
              [class.is-current]="index === selectedIndex()"
              [attr.aria-current]="index === selectedIndex()"
              [attr.aria-label]="'Show image ' + (index + 1) + ' of ' + images().length"
              (click)="select(index)"
            >
              <kh-product-image [source]="image.source" [placeholder]="productName()" sizes="5rem" />
            </button>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .stage {
      position: relative;
      display: block;
      inline-size: 100%;
      padding: 0;
      border: 0;
      background: transparent;
      overflow: hidden;
      border-radius: var(--radius-md);
      cursor: zoom-in;
      touch-action: pan-y;
    }

    .stage.zoomed {
      cursor: zoom-out;
    }

    .stage.zoomed kh-product-image {
      display: block;
      transform: scale(2);
      transform-origin: var(--kh-zoom-x, 50%) var(--kh-zoom-y, 50%);
      transition: transform var(--duration-base) var(--ease-standard);
    }

    .thumbs {
      display: flex;
      gap: var(--space-2);
      margin: var(--space-3) 0 0;
      padding: 0;
      list-style: none;
      overflow-x: auto;
    }

    .thumb {
      inline-size: 4.5rem;
      padding: var(--space-1);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .thumb.is-current {
      border-color: var(--color-primary);
      border-width: 2px;
    }

    @media (prefers-reduced-motion: reduce) {
      .stage.zoomed kh-product-image {
        transition: none;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[style.--kh-zoom-x.%]': 'zoomX()',
    '[style.--kh-zoom-y.%]': 'zoomY()',
  },
})
export class ProductGallery {
  readonly images = input.required<readonly GalleryImageView[]>();
  /** Used as the placeholder caption when a product has no images at all. */
  readonly productName = input('');

  /**
   * Reset to the first image whenever the set changes — selecting a variant swaps the gallery,
   * and holding index 4 into a new three-image set would show nothing.
   */
  protected readonly selectedIndex = linkedSignal({ source: this.images, computation: () => 0 });

  /**
   * Annotated, not inferred. Indexing an array is typed as always hitting, so an inferred type
   * here would be `GalleryImageView` and the template's `?.` would be flagged as redundant — while
   * at runtime an empty gallery genuinely yields `undefined`.
   */
  protected readonly current = computed<GalleryImageView | null>(
    () => this.images()[this.selectedIndex()] ?? this.images()[0] ?? null,
  );

  protected readonly zoomed = signal(false);
  protected readonly zoomX = signal(50);
  protected readonly zoomY = signal(50);

  constructor() {
    // A variant change that swaps the images must not leave the stage zoomed into a corner of a
    // picture that is no longer there.
    effect(() => {
      this.images();
      this.zoomed.set(false);
    });
  }

  protected select(index: number): void {
    this.selectedIndex.set(index);
    this.zoomed.set(false);
  }

  protected toggleZoom(event?: MouseEvent): void {
    if (event) this.trackPointer(event, true);
    this.zoomed.update((on) => !on);
  }

  /** Moves the transform origin to the pointer, so zooming magnifies what is under it. */
  protected trackPointer(event: MouseEvent, force = false): void {
    if (!force && !this.zoomed()) return;
    const target = event.currentTarget as HTMLElement | null;
    if (!target) return;

    const box = target.getBoundingClientRect();
    if (box.width === 0 || box.height === 0) return;

    this.zoomX.set(Math.round(((event.clientX - box.left) / box.width) * 100));
    this.zoomY.set(Math.round(((event.clientY - box.top) / box.height) * 100));
  }
}
