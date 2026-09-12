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
 * A file id is turned into a URL by the API itself: `GET /store/media/{fileId}/image?width=N`
 * answers with a redirect to the rendition nearest that width — imgproxy where the deployment has
 * one, the original otherwise. The apps used to build an imgproxy path of their own from
 * `imageBaseUrl`, and that path was one no service served: every id-only image on a deployment
 * that set the variable was a broken picture. Only the media module knows the object key and the
 * signature, so only the media module builds the address.
 *
 * The one id that is not a file is the all-zero GUID, which the catalogue projections use for
 * "no image"; it answers `null` here so the placeholder box renders instead of a 404.
 */

/** The widths a product image is requested at. One per column count the grid can produce. */
export const IMAGE_WIDTHS = [180, 240, 360, 480, 720, 960] as const;

/** What the catalogue projections send when a product has no image at all. */
const EMPTY_FILE_ID = '00000000-0000-0000-0000-000000000000';

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
  private readonly base = (inject(RUNTIME_CONFIG).apiBaseUrl ?? '').replace(/\/+$/, '');

  /** Whether a file id can be turned into a URL at all in this deployment. */
  get canResize(): boolean {
    return this.base.length > 0;
  }

  /**
   * One URL for a stored file at a given width, or `null` when there is no file to ask for.
   *
   * The path is the media module's own public route (`StoreMediaEndpoints`), built here rather
   * than at each call site so that changing it is one edit.
   */
  forFile(fileId: string | null | undefined, width: number): string | null {
    if (!fileId || fileId === EMPTY_FILE_ID || !this.canResize) return null;
    return `${this.base}/api/v1/store/media/${encodeURIComponent(fileId)}/image?width=${width}`;
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
