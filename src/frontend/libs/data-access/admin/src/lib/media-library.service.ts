import { Injectable, inject } from '@angular/core';
import { MediaApiClient, MediaFileResponse, MediaLinkResponse } from '@klarahome/data-access-api';
import { Observable, map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

export interface MediaFilters {
  readonly visibility?: string;
  readonly contentType?: string;
}

/**
 * The media library.
 *
 * The catalogue's media manager, the brand logo picker and the category image picker are the same
 * problem — choose a file the platform already holds, or add one — so they are one service and,
 * on screen, one component.
 *
 * Two properties of `platform.media_files` shape what a caller has to do with the answer
 * (Step 8, ADR-015/016):
 *
 *  - **A file is not usable the moment it is uploaded.** `scanState` runs asynchronously, and a
 *    file that has not cleared the scanner has no public URL. The picker therefore shows the state
 *    rather than a broken image, and refusing to select an unscanned file is the screen's job, not
 *    this service's.
 *  - **A private file has no durable URL.** `link` mints a short-lived signed one, which is why it
 *    is a request and not a property: a URL held in a component for an hour is a URL that has
 *    expired by the time somebody clicks it.
 */
@Injectable({ providedIn: 'root' })
export class MediaLibraryService {
  private readonly api = inject(MediaApiClient);

  files(filters: MediaFilters = {}, pageSize = 24): CursorList<MediaFileResponse, MediaFilters> {
    return new CursorList<MediaFileResponse, MediaFilters>(
      (current, cursor, size) =>
        this.api
          .adminMediaList({
            visibility: current.visibility,
            contentType: current.contentType,
            cursor: cursor ?? undefined,
            size: size,
          })
          .pipe(map((result): CursorPage<MediaFileResponse> => result)),
      filters,
      pageSize,
    );
  }

  file(id: string): Observable<MediaFileResponse> {
    return this.api.adminMediaGet(id);
  }

  /**
   * Uploads one file.
   *
   * `ownerType`/`ownerId` are passed where the caller knows them, so a product's images are
   * attributable in the media list rather than being forty anonymous JPEGs. `visibility` defaults
   * to public here because every caller in this step is uploading catalogue imagery; anything
   * private is uploaded by the module that owns it.
   */
  upload(
    file: File,
    owner?: { readonly ownerType?: string; readonly ownerId?: string; readonly visibility?: string },
  ): Observable<MediaFileResponse> {
    return this.api.adminMediaUpload(
      { file },
      {
        visibility: owner?.visibility ?? 'Public',
        ownerType: owner?.ownerType,
        ownerId: owner?.ownerId,
      },
    );
  }

  /** A short-lived signed URL for a private file. Requested at the moment it is needed. */
  link(id: string): Observable<MediaLinkResponse> {
    return this.api.adminMediaLink(id);
  }

  remove(id: string): Observable<void> {
    return this.api.adminMediaDelete(id);
  }
}
