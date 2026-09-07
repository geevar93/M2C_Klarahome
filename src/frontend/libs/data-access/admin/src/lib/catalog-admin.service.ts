import { Injectable, inject } from '@angular/core';
import {
  AttributeBody,
  AttributeResponse,
  AttributeSetBody,
  AttributeSetResponse,
  BrandBody,
  BrandResponse,
  BulkProductStatusResponse,
  CatalogApiClient,
  CatalogJobResponse,
  CategoryBody,
  CategoryNode,
  CategoryResponse,
  CreateListingBody,
  ListingResponse,
  MediaBody,
  ModerationBody,
  ModerationResponse,
  ProductBody,
  ProductListItem,
  ProductResponse,
  ProductStatus,
  UpdateListingBody,
  VariantBody,
  VariantResponse,
} from '@klarahome/data-access-api';
import { Observable, map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

/** What the product list may be narrowed by. Every key is a query parameter the API declares. */
export interface ProductFilters {
  readonly status?: string;
  readonly categoryId?: string;
  readonly brandId?: string;
  readonly vendorId?: string;
  readonly search?: string;
}

export interface BrandFilters {
  readonly search?: string;
  readonly activeOnly?: boolean;
}

export interface ModerationFilters {
  readonly status?: string;
}

export interface ListingFilters {
  readonly status?: string;
  readonly vendorId?: string;
  readonly productId?: string;
  readonly variantId?: string;
  readonly search?: string;
}

/**
 * The catalogue, as the back office works it.
 *
 * A seam over the generated client rather than a repository: nothing is cached and nothing is
 * transformed. Two things earn it its place.
 *
 * **It is where the query vocabulary is written down once.** The generated client's query objects
 * are PascalCase for some endpoints and camelCase for others — that is what the API's own
 * parameter names are, and the generator is right to reproduce them rather than tidy them. A
 * screen should not have to know which; it names a filter and this translates.
 *
 * **A list is a `CursorList`, and a command is an `Observable`.** That distinction is the shape of
 * every admin screen: a list is stateful, because it remembers cursors nobody else can compute,
 * and a command is one-shot and belongs to the component that fires it — which is what lets a
 * button own its own in-flight flag and its own failure message.
 *
 * The moderation queue is deliberately here rather than in a service of its own: approving a
 * product and editing one are two verbs on one aggregate, and splitting them would put the
 * product's identity in two places.
 */
@Injectable({ providedIn: 'root' })
export class CatalogAdminService {
  private readonly api = inject(CatalogApiClient);

  // ---- Products ---------------------------------------------------------------------------------

  products(filters: ProductFilters = {}, pageSize = 25): CursorList<ProductListItem, ProductFilters> {
    return new CursorList<ProductListItem, ProductFilters>(
      (current, cursor, size) =>
        this.api
          .adminProductsList({
            status: current.status,
            categoryId: current.categoryId,
            brandId: current.brandId,
            vendorId: current.vendorId,
            search: current.search,
            cursor: cursor ?? undefined,
            size: size,
          })
          .pipe(map((result): CursorPage<ProductListItem> => result)),
      filters,
      pageSize,
    );
  }

  product(id: string): Observable<ProductResponse> {
    return this.api.adminProductGet(id);
  }

  createProduct(body: ProductBody): Observable<ProductResponse> {
    return this.api.adminProductCreate(body);
  }

  updateProduct(id: string, body: ProductBody): Observable<ProductResponse> {
    return this.api.adminProductUpdate(id, body);
  }

  deleteProduct(id: string): Observable<void> {
    return this.api.adminProductDelete(id);
  }

  setProductMedia(id: string, body: MediaBody): Observable<ProductResponse> {
    return this.api.adminProductSetMedia(id, body);
  }

  /**
   * The product lifecycle, one method per edge.
   *
   * Named after the transitions rather than offered as `transition(status)` because these are not
   * one endpoint: submit, approve, reject, publish, unpublish and archive are six routes with six
   * permissions, and a single method taking a status string would hide that a caller needs
   * `catalog.product.moderate` for two of them and `catalog.product.publish` for another.
   */
  submitProduct(id: string): Observable<ProductResponse> {
    return this.api.adminProductSubmit(id);
  }

  approveProduct(id: string, notes: string | null): Observable<ProductResponse> {
    return this.api.adminProductApprove(id, { notes });
  }

  rejectProduct(id: string, body: ModerationBody): Observable<ProductResponse> {
    return this.api.adminProductReject(id, body);
  }

  publishProduct(id: string): Observable<ProductResponse> {
    return this.api.adminProductPublish(id);
  }

  unpublishProduct(id: string): Observable<ProductResponse> {
    return this.api.adminProductUnpublish(id);
  }

  archiveProduct(id: string): Observable<ProductResponse> {
    return this.api.adminProductArchive(id);
  }

  /**
   * Moves several products to one status in one request.
   *
   * The response reports on each product rather than answering yes or no: publishing forty where
   * two have incomplete mandatory disclosures publishes thirty-eight and names the two, which is
   * the only outcome that is both honest and useful (Step 28B, deliverable 8).
   */
  bulkProductStatus(
    productIds: readonly string[],
    status: ProductStatus,
  ): Observable<BulkProductStatusResponse> {
    return this.api.adminProductBulkStatus({ productIds: [...productIds], status });
  }

  // ---- Variants ---------------------------------------------------------------------------------

  createVariant(productId: string, body: VariantBody): Observable<VariantResponse> {
    return this.api.adminVariantCreate(productId, body);
  }

  updateVariant(variantId: string, body: VariantBody): Observable<VariantResponse> {
    return this.api.adminVariantUpdate(variantId, body);
  }

  activateVariant(variantId: string): Observable<VariantResponse> {
    return this.api.adminVariantActivate(variantId);
  }

  deactivateVariant(variantId: string): Observable<VariantResponse> {
    return this.api.adminVariantDeactivate(variantId);
  }

  deleteVariant(variantId: string): Observable<void> {
    return this.api.adminVariantDelete(variantId);
  }

  // ---- Listings (a seller's offer against a variant) ---------------------------------------------

  listings(filters: ListingFilters = {}, pageSize = 25): CursorList<ListingResponse, ListingFilters> {
    return new CursorList<ListingResponse, ListingFilters>(
      (current, cursor, size) =>
        this.api
          .adminListingsList({
            status: current.status,
            vendorId: current.vendorId,
            productId: current.productId,
            variantId: current.variantId,
            search: current.search,
            cursor: cursor ?? undefined,
            size: size,
          })
          .pipe(map((result): CursorPage<ListingResponse> => result)),
      filters,
      pageSize,
    );
  }

  /**
   * A short list of products matching a search, for a picker.
   *
   * The same shape and the same reasoning as `searchListings`: a typeahead shows a handful and is
   * re-run on the next keystroke, so a paging cursor is state nobody reads.
   */
  searchProducts(term: string, take = 10): Observable<ProductListItem[]> {
    return this.api.adminProductsList({ search: term, size: take }).pipe(map((page) => page.items));
  }

  /**
   * A short list of listings matching a search, for a picker.
   *
   * A plain observable rather than a `CursorList`: a typeahead shows the first handful and is
   * re-run on the next keystroke, so paging state would be state nobody reads. Added at Step 28B
   * for the entity picker (deliverable 15).
   */
  searchListings(term: string, take = 10): Observable<ListingResponse[]> {
    return this.api.adminListingsList({ search: term, size: take }).pipe(map((page) => page.items));
  }

  createListing(body: CreateListingBody): Observable<ListingResponse> {
    return this.api.adminListingCreate(body);
  }

  updateListing(id: string, body: UpdateListingBody): Observable<ListingResponse> {
    return this.api.adminListingUpdate(id, body);
  }

  activateListing(id: string): Observable<ListingResponse> {
    return this.api.adminListingActivate(id, null);
  }

  deactivateListing(id: string, reason: string | null): Observable<ListingResponse> {
    return this.api.adminListingDeactivate(id, { reason });
  }

  archiveListing(id: string, reason: string | null): Observable<ListingResponse> {
    return this.api.adminListingArchive(id, { reason });
  }

  // ---- Taxonomy ---------------------------------------------------------------------------------

  /**
   * The category tree.
   *
   * A tree rather than a page, because that is what the endpoint answers and what the screen is:
   * `catalog.categories` is a materialised path (Step 10), the depth is small and bounded, and a
   * cursor-paged flat list of categories would be a paging control over eighty rows.
   */
  categoryTree(activeOnly = false): Observable<CategoryNode[]> {
    return this.api.adminCategoriesTree({ activeOnly });
  }

  category(id: string): Observable<CategoryResponse> {
    return this.api.adminCategoryGet(id);
  }

  createCategory(body: CategoryBody): Observable<CategoryResponse> {
    return this.api.adminCategoryCreate(body);
  }

  updateCategory(id: string, body: CategoryBody): Observable<CategoryResponse> {
    return this.api.adminCategoryUpdate(id, body);
  }

  deleteCategory(id: string): Observable<void> {
    return this.api.adminCategoryDelete(id);
  }

  brands(filters: BrandFilters = {}, pageSize = 25): CursorList<BrandResponse, BrandFilters> {
    return new CursorList<BrandResponse, BrandFilters>(
      (current, cursor, size) =>
        this.api
          .adminBrandsList({
            search: current.search,
            activeOnly: current.activeOnly ?? false,
            cursor: cursor ?? undefined,
            size: size,
          })
          .pipe(map((result): CursorPage<BrandResponse> => result)),
      filters,
      pageSize,
    );
  }

  createBrand(body: BrandBody): Observable<BrandResponse> {
    return this.api.adminBrandCreate(body);
  }

  updateBrand(id: string, body: BrandBody): Observable<BrandResponse> {
    return this.api.adminBrandUpdate(id, body);
  }

  deleteBrand(id: string): Observable<void> {
    return this.api.adminBrandDelete(id);
  }

  /**
   * Every attribute.
   *
   * Unpaged, because the endpoint is: attributes are the catalogue's own vocabulary, there are
   * tens of them rather than thousands, and a product editor needs all of them at once to render
   * a form at all.
   */
  attributes(filterableOnly = false, variantDefiningOnly = false): Observable<AttributeResponse[]> {
    return this.api.adminAttributesList({
      filterableOnly: filterableOnly,
      variantDefiningOnly: variantDefiningOnly,
    });
  }

  createAttribute(body: AttributeBody): Observable<AttributeResponse> {
    return this.api.adminAttributeCreate(body);
  }

  updateAttribute(id: string, body: AttributeBody): Observable<AttributeResponse> {
    return this.api.adminAttributeUpdate(id, body);
  }

  deleteAttribute(id: string): Observable<void> {
    return this.api.adminAttributeDelete(id);
  }

  attributeSets(): Observable<AttributeSetResponse[]> {
    return this.api.adminAttributeSetsList();
  }

  createAttributeSet(body: AttributeSetBody): Observable<AttributeSetResponse> {
    return this.api.adminAttributeSetCreate(body);
  }

  updateAttributeSet(id: string, body: AttributeSetBody): Observable<AttributeSetResponse> {
    return this.api.adminAttributeSetUpdate(id, body);
  }

  deleteAttributeSet(id: string): Observable<void> {
    return this.api.adminAttributeSetDelete(id);
  }

  // ---- Moderation -------------------------------------------------------------------------------

  moderationQueue(
    filters: ModerationFilters = { status: 'Pending' },
    pageSize = 25,
  ): CursorList<ModerationResponse, ModerationFilters> {
    return new CursorList<ModerationResponse, ModerationFilters>(
      (current, cursor, size) =>
        this.api
          .adminProductModerationQueue({
            status: current.status,
            cursor: cursor ?? undefined,
            size: size,
          })
          .pipe(map((result): CursorPage<ModerationResponse> => result)),
      filters,
      pageSize,
    );
  }

  // ---- Bulk import and export -------------------------------------------------------------------

  /**
   * The catalogue jobs, newest first.
   *
   * An import is asynchronous by construction — a 20,000-row CSV is not a request — so the screen
   * uploads, gets a job back, and watches it. `errors` on the job is what makes the error report a
   * report rather than a status: it names the row, the column and the SKU that failed, which is
   * the difference between "412 rows failed" and a file somebody can fix.
   */
  jobs(pageSize = 20): CursorList<CatalogJobResponse, Record<string, never>> {
    return new CursorList<CatalogJobResponse, Record<string, never>>(
      (_filters, cursor, size) =>
        this.api
          .adminCatalogJobsList({ cursor: cursor ?? undefined, size: size })
          .pipe(map((result): CursorPage<CatalogJobResponse> => result)),
      {},
      pageSize,
    );
  }

  /** One job. Polled while it runs, so it neither raises the loading bar nor toasts a blip. */
  job(id: string): Observable<CatalogJobResponse> {
    return this.api.adminCatalogJobGet(id, { silentErrors: true, showLoading: false });
  }

  importProducts(file: File): Observable<CatalogJobResponse> {
    return this.api.adminProductImport({ file });
  }

  exportProducts(): Observable<CatalogJobResponse> {
    return this.api.adminProductExport();
  }
}
