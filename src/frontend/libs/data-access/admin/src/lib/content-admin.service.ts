import { Injectable, inject } from '@angular/core';
import {
  BannerBody,
  BannerResponse,
  BlockTypeResponse,
  CollectionResponse,
  CollectionSummaryResponse,
  ContentApiClient,
  CreateCollectionBody,
  CreateMenuBody,
  CreatePageBody,
  CreateRedirectBody,
  MenuResponse,
  MenuSummaryResponse,
  PageResponse,
  PageSummaryResponse,
  PageVersionResponse,
  PageVersionSummaryResponse,
  ProductCardResponse,
  RedirectResponse,
  SetCollectionItemsBody,
  SetCollectionRuleBody,
  StorePageResponse,
  TransitionPageBody,
  UpdateCollectionBody,
  UpdateMenuBody,
  UpdatePageBody,
  UpdateRedirectBody,
} from '@klarahome/data-access-api';
import { Observable, map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

export interface PageFilters {
  readonly search?: string;
  readonly type?: string;
  readonly status?: string;
}

export interface BannerFilters {
  readonly placement?: string;
  readonly activeOnly?: boolean;
}

export interface CollectionFilters {
  readonly search?: string;
  readonly kind?: string;
  readonly activeOnly?: boolean;
}

export interface RedirectFilters {
  readonly search?: string;
  readonly activeOnly?: boolean;
}

/**
 * The merchandising surface: what the storefront says, and where it points.
 *
 * Five things live here, and the first of them decides the shape of a screen rather than just
 * feeding one.
 *
 * **Block types are read back from the API.** `blockTypes()` returns the schema of every block a
 * page may contain — its fields, their kinds, whether each is required, how long it may be, and
 * which choices it accepts. Step 20 made those schemas *data* precisely so that the composer's
 * form and the validator that judges what it submits cannot drift: there is one declaration, the
 * server owns it, and a block type added on the server appears in the composer with no change
 * here. That is why this service has no notion of what a "hero" or a "product grid" is.
 *
 * **A page moves through a transition table, not through a status field.** `PageResponse` carries
 * `allowedTransitions`, so the composer offers the edges the server will accept — the same
 * arrangement the orders and returns screens use. Scheduling is one of those edges rather than a
 * separate concept: a scheduled page is one whose transition carried a time, and only the clock
 * publishes it.
 *
 * **A version is a snapshot taken on publish.** That is what makes `preview` honest — it renders
 * the stored version rather than re-deriving one — and what makes `rollback` a single write
 * instead of a reconstruction.
 *
 * **A collection is rows, even when it is a rule.** The rule is evaluated server-side into
 * materialised items (Step 20), so `refreshCollection` is a real operation with a real cost and
 * the screen says so. `isPinned` is why a refresh does not overturn a merchandiser's choice.
 */
@Injectable({ providedIn: 'root' })
export class ContentAdminService {
  private readonly api = inject(ContentApiClient);

  // ---- Pages ------------------------------------------------------------------------------------

  pages(filters: PageFilters = {}, pageSize = 25): CursorList<PageSummaryResponse, PageFilters> {
    return new CursorList<PageSummaryResponse, PageFilters>(
      (current, cursor, size) =>
        this.api
          .adminListPages({
            search: current.search,
            type: current.type,
            status: current.status,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<PageSummaryResponse> => result)),
      filters,
      pageSize,
    );
  }

  page(id: string): Observable<PageResponse> {
    return this.api.adminGetPage(id);
  }

  createPage(body: CreatePageBody): Observable<PageResponse> {
    return this.api.adminCreatePage(body);
  }

  updatePage(id: string, body: UpdatePageBody): Observable<PageResponse> {
    return this.api.adminUpdatePage(id, body);
  }

  deletePage(id: string): Observable<void> {
    return this.api.adminDeletePage(id);
  }

  /** The edges the page's own transition table will accept, taken one at a time. */
  transitionPage(id: string, body: TransitionPageBody): Observable<PageResponse> {
    return this.api.adminTransitionPage(id, body);
  }

  /** Every block type, with its field schema. The composer's form is built from this. */
  blockTypes(): Observable<BlockTypeResponse[]> {
    return this.api.adminListBlockTypes();
  }

  /** What the storefront would render — the draft, or a named version. */
  previewPage(id: string, version?: number): Observable<StorePageResponse> {
    return this.api.adminPreviewPage(id, version === undefined ? undefined : { version });
  }

  pageVersions(id: string): Observable<PageVersionSummaryResponse[]> {
    return this.api.adminListPageVersions(id);
  }

  pageVersion(id: string, version: number): Observable<PageVersionResponse> {
    return this.api.adminGetPageVersion(id, version);
  }

  /** Restores a snapshot as the current draft. The snapshot itself is untouched. */
  rollbackPage(id: string, version: number): Observable<PageResponse> {
    return this.api.adminRollbackPage(id, version);
  }

  // ---- Banners ----------------------------------------------------------------------------------

  banners(filters: BannerFilters = {}, pageSize = 25): CursorList<BannerResponse, BannerFilters> {
    return new CursorList<BannerResponse, BannerFilters>(
      (current, cursor, size) =>
        this.api
          .adminListBanners({
            placement: current.placement,
            activeOnly: current.activeOnly,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<BannerResponse> => result)),
      filters,
      pageSize,
    );
  }

  banner(id: string): Observable<BannerResponse> {
    return this.api.adminGetBanner(id);
  }

  createBanner(body: BannerBody): Observable<BannerResponse> {
    return this.api.adminCreateBanner(body);
  }

  updateBanner(id: string, body: BannerBody): Observable<BannerResponse> {
    return this.api.adminUpdateBanner(id, body);
  }

  /**
   * Switches a banner on or off without opening it.
   *
   * Its own endpoint rather than a field on the update, because taking a wrong banner down is the
   * one thing on this screen somebody does in a hurry, and it should not require sending back
   * every other field of a record they have not read.
   */
  setBannerActive(id: string, isActive: boolean): Observable<BannerResponse> {
    return this.api.adminSetBannerActive(id, { isActive });
  }

  deleteBanner(id: string): Observable<void> {
    return this.api.adminDeleteBanner(id);
  }

  // ---- Menus ------------------------------------------------------------------------------------

  /** Menus are few and are not paged; the API answers the whole list. */
  menus(placement?: string): Observable<MenuSummaryResponse[]> {
    return this.api.adminListMenus(placement ? { placement } : undefined);
  }

  menu(id: string): Observable<MenuResponse> {
    return this.api.adminGetMenu(id);
  }

  createMenu(body: CreateMenuBody): Observable<MenuResponse> {
    return this.api.adminCreateMenu(body);
  }

  /**
   * Replaces the menu and its whole item tree in one write.
   *
   * The items are sent as a list rather than patched one at a time because position and parentage
   * are properties of the *tree*: moving one item changes the position of its siblings, and three
   * requests to express one drag would leave the menu briefly wrong in a way a shopper could see.
   */
  updateMenu(id: string, body: UpdateMenuBody): Observable<MenuResponse> {
    return this.api.adminUpdateMenu(id, body);
  }

  deleteMenu(id: string): Observable<void> {
    return this.api.adminDeleteMenu(id);
  }

  // ---- Collections ------------------------------------------------------------------------------

  collections(
    filters: CollectionFilters = {},
    pageSize = 25,
  ): CursorList<CollectionSummaryResponse, CollectionFilters> {
    return new CursorList<CollectionSummaryResponse, CollectionFilters>(
      (current, cursor, size) =>
        this.api
          .adminListCollections({
            search: current.search,
            kind: current.kind,
            activeOnly: current.activeOnly,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<CollectionSummaryResponse> => result)),
      filters,
      pageSize,
    );
  }

  collection(id: string): Observable<CollectionResponse> {
    return this.api.adminGetCollection(id);
  }

  createCollection(body: CreateCollectionBody): Observable<CollectionResponse> {
    return this.api.adminCreateCollection(body);
  }

  updateCollection(id: string, body: UpdateCollectionBody): Observable<CollectionResponse> {
    return this.api.adminUpdateCollection(id, body);
  }

  deleteCollection(id: string): Observable<void> {
    return this.api.adminDeleteCollection(id);
  }

  collectionItems(
    collectionId: string,
    pageSize = 50,
  ): CursorList<ProductCardResponse, Record<string, never>> {
    return new CursorList<ProductCardResponse, Record<string, never>>(
      (_filters, cursor, size) =>
        this.api
          .adminListCollectionItems(collectionId, { cursor: cursor ?? undefined, size })
          .pipe(map((result): CursorPage<ProductCardResponse> => result)),
      {},
      pageSize,
    );
  }

  /** Pins and unpins. A pinned item survives the next rule refresh; the rest do not. */
  setCollectionItems(id: string, body: SetCollectionItemsBody): Observable<CollectionResponse> {
    return this.api.adminSetCollectionItems(id, body);
  }

  /** Turns a manual collection into a rule-driven one, or edits the rule it already has. */
  setCollectionRule(id: string, body: SetCollectionRuleBody): Observable<CollectionResponse> {
    return this.api.adminSetCollectionRule(id, body);
  }

  /** Re-evaluates the rule now, rather than waiting for a catalogue event or the sweep. */
  refreshCollection(id: string): Observable<CollectionResponse> {
    return this.api.adminRefreshCollection(id);
  }

  // ---- Redirects --------------------------------------------------------------------------------

  redirects(filters: RedirectFilters = {}, pageSize = 25): CursorList<RedirectResponse, RedirectFilters> {
    return new CursorList<RedirectResponse, RedirectFilters>(
      (current, cursor, size) =>
        this.api
          .adminListRedirects({
            search: current.search,
            activeOnly: current.activeOnly,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<RedirectResponse> => result)),
      filters,
      pageSize,
    );
  }

  createRedirect(body: CreateRedirectBody): Observable<RedirectResponse> {
    return this.api.adminCreateRedirect(body);
  }

  updateRedirect(id: string, body: UpdateRedirectBody): Observable<RedirectResponse> {
    return this.api.adminUpdateRedirect(id, body);
  }

  deleteRedirect(id: string): Observable<void> {
    return this.api.adminDeleteRedirect(id);
  }
}
