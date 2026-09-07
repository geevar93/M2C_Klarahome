import { Injectable, inject } from '@angular/core';

import { RUNTIME_CONFIG } from './runtime-config';

/**
 * Where a product image comes from, and at what width.
 *
 * The API answers with two different things depending on the endpoint: a content image carries a
 * ready `url`, while a catalogue projection carries only a `fileId` — the listing row is
 * denormalised for speed and does not join the media table. Both have to end up as an `<img src>`,
 * and neither component that renders one should know the difference.
 *
 * `imageBaseUrl` is the imgproxy origin from the runtime configuration
 * (docs/05-frontend-architecture.md §5). When it is blank — the local stack, and any deployment
 * without a resizer — there is no honest URL to build from a file id, and this answers `null`
 * rather than a guess. A `null` renders the placeholder box, which is the correct thing to show
 * for an image that does not exist yet and is also the rule until Step 30
 * (docs/10-design-system-placeholder.md §1.3).
 */

/** The widths a product image is requested at. One per column count the grid can produce. */
export const IMAGE_WIDTHS = [180, 240, 360, 480, 720, 960] as const;

/** What a component needs to render one image without knowing where it came from. */
export interface ImageSource {
  readonly src: string;
  /** The `srcset`, or an empty string when only one URL can be built. */
  readonly srcset: string;
  /** Intrinsic dimensions, when known. Written to `width`/`height` so the box never shifts. */
  readonly width: number | null;
  readonly height: number | null;
  readonly alt: string;
}

@Injectable({ providedIn: 'root' })
export class ImageUrls {
  private readonly base = (inject(RUNTIME_CONFIG).imageBaseUrl ?? '').replace(/\/+$/, '');

  /** Whether a file id can be turned into a URL at all in this deployment. */
  get canResize(): boolean {
    return this.base.length > 0;
  }

  /**
   * One URL for a stored file at a given width, or `null` when no resizer is configured.
   *
   * The path shape — `/{width}x/{fileId}` — is the resizing convention the media module's
   * imgproxy deployment serves (Step 8, ADR-016). It is built here rather than at each call site
   * so that changing the resizer is one edit.
   */
  forFile(fileId: string | null | undefined, width: number): string | null {
    if (!fileId || !this.canResize) return null;
    return `${this.base}/${width}x/${encodeURIComponent(fileId)}`;
  }

  /**
   * The whole `<img>` payload for a file id.
   *
   * `srcset` over `sizes` rather than a fixed width, because a product card is a different number
   * of pixels wide in a grid, in a drawer and on a 4K screen, and the browser picks better than a
   * media query written a year earlier (docs/05-frontend-architecture.md §3.3).
   */
  sourceForFile(fileId: string | null | undefined, alt: string, intrinsicWidth = 720): ImageSource | null {
    const src = this.forFile(fileId, intrinsicWidth);
    if (!src) return null;

    return {
      src,
      srcset: IMAGE_WIDTHS.map((width) => `${this.forFile(fileId, width)} ${width}w`).join(', '),
      // A square box: the catalogue's own aspect ratio is not known from a projection row, and
      // reserving a square is what keeps CLS at zero for a grid of cards.
      width: intrinsicWidth,
      height: intrinsicWidth,
      alt,
    };
  }

  /**
   * The `<img>` payload for an image the API has already resolved to a URL.
   *
   * A ready URL is used as it is — it may be a signed link, and rewriting it through the resizer
   * would strip the signature. The file id is the fallback for a payload whose `url` is null,
   * which is what the CMS returns when the file has been deleted underneath a published page.
   */
  sourceForImage(
    image:
      | {
          readonly url?: string | null;
          readonly fileId?: string | null;
          readonly width?: number | null;
          readonly height?: number | null;
          readonly alt?: string | null;
        }
      | null
      | undefined,
    fallbackAlt: string,
  ): ImageSource | null {
    if (!image) return null;
    const alt = image.alt?.trim() || fallbackAlt;

    if (image.url) {
      return { src: image.url, srcset: '', width: image.width ?? null, height: image.height ?? null, alt };
    }

    return this.sourceForFile(image.fileId, alt);
  }
}
