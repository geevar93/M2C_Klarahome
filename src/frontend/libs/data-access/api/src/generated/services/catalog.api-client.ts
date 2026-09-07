/**
 * DO NOT EDIT. Generated from the API's OpenAPI document by tools/generate-api-client.mjs.
 *
 * Regenerate with:  pwsh tools/generate-api-client.ps1
 * CI fails if this file differs from what the current API produces.
 */
/* eslint-disable */

import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiRequestOptions, ApiTransport, toFormData } from '../../runtime';
import type * as Models from '../models';

/** Query string for `adminAttributesList`. */
export interface AdminAttributesListQuery {
  filterableOnly: boolean;
  variantDefiningOnly: boolean;
}

/** Query string for `adminBrandsList`. */
export interface AdminBrandsListQuery {
  search?: string;
  activeOnly: boolean;
  cursor?: string;
  size?: number;
}

/** Query string for `adminCatalogJobsList`. */
export interface AdminCatalogJobsListQuery {
  cursor?: string;
  size?: number;
}

/** Query string for `adminCategoriesTree`. */
export interface AdminCategoriesTreeQuery {
  parentId?: string;
  depth?: number;
  activeOnly?: boolean;
}

/** Query string for `adminListingsList`. */
export interface AdminListingsListQuery {
  status?: string;
  vendorId?: string;
  productId?: string;
  variantId?: string;
  search?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminProductModerationQueue`. */
export interface AdminProductModerationQueueQuery {
  status?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminProductsList`. */
export interface AdminProductsListQuery {
  status?: string;
  categoryId?: string;
  brandId?: string;
  vendorId?: string;
  search?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `storeBrands`. */
export interface StoreBrandsQuery {
  search?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `storeCategories`. */
export interface StoreCategoriesQuery {
  parentId?: string;
  depth?: number;
}

/** Query string for `storeProductOffers`. */
export interface StoreProductOffersQuery {
  variantId?: string;
}

/** `Catalog` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class CatalogApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Declares an attribute. Only select and multiselect can be variant axes.
   * `POST /api/v1/admin/attributes`
   */
  adminAttributeCreate(body: Models.AttributeBody, options?: ApiRequestOptions): Observable<Models.AttributeResponse> {
    return this.http.request<Models.AttributeResponse>('POST', `${this.baseUrl}/api/v1/admin/attributes`, body, undefined, options);
  }

  /**
   * Removes an attribute. Refused while anything still uses it.
   * `DELETE /api/v1/admin/attributes/{id}`
   */
  adminAttributeDelete(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/attributes/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Reads one attribute.
   * `GET /api/v1/admin/attributes/{id}`
   */
  adminAttributeGet(id: string, options?: ApiRequestOptions): Observable<Models.AttributeResponse> {
    return this.http.request<Models.AttributeResponse>('GET', `${this.baseUrl}/api/v1/admin/attributes/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Declares an attribute set — the form a category's products are described with.
   * `POST /api/v1/admin/attribute-sets`
   */
  adminAttributeSetCreate(body: Models.AttributeSetBody, options?: ApiRequestOptions): Observable<Models.AttributeSetResponse> {
    return this.http.request<Models.AttributeSetResponse>('POST', `${this.baseUrl}/api/v1/admin/attribute-sets`, body, undefined, options);
  }

  /**
   * Removes an attribute set. Refused while a category still uses it.
   * `DELETE /api/v1/admin/attribute-sets/{id}`
   */
  adminAttributeSetDelete(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/attribute-sets/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Reads one attribute set.
   * `GET /api/v1/admin/attribute-sets/{id}`
   */
  adminAttributeSetGet(id: string, options?: ApiRequestOptions): Observable<Models.AttributeSetResponse> {
    return this.http.request<Models.AttributeSetResponse>('GET', `${this.baseUrl}/api/v1/admin/attribute-sets/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Lists attribute sets with their membership.
   * `GET /api/v1/admin/attribute-sets`
   */
  adminAttributeSetsList(options?: ApiRequestOptions): Observable<Models.AttributeSetResponse[]> {
    return this.http.request<Models.AttributeSetResponse[]>('GET', `${this.baseUrl}/api/v1/admin/attribute-sets`, undefined, undefined, options);
  }

  /**
   * Updates an attribute set and reconciles its membership.
   * `PUT /api/v1/admin/attribute-sets/{id}`
   */
  adminAttributeSetUpdate(id: string, body: Models.AttributeSetBody, options?: ApiRequestOptions): Observable<Models.AttributeSetResponse> {
    return this.http.request<Models.AttributeSetResponse>('PUT', `${this.baseUrl}/api/v1/admin/attribute-sets/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Lists attributes with their permitted values.
   * `GET /api/v1/admin/attributes`
   */
  adminAttributesList(query: AdminAttributesListQuery, options?: ApiRequestOptions): Observable<Models.AttributeResponse[]> {
    return this.http.request<Models.AttributeResponse[]>('GET', `${this.baseUrl}/api/v1/admin/attributes`, undefined, query, options);
  }

  /**
   * Updates an attribute and reconciles its options. The data type cannot change.
   * `PUT /api/v1/admin/attributes/{id}`
   */
  adminAttributeUpdate(id: string, body: Models.AttributeBody, options?: ApiRequestOptions): Observable<Models.AttributeResponse> {
    return this.http.request<Models.AttributeResponse>('PUT', `${this.baseUrl}/api/v1/admin/attributes/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Registers a brand.
   * `POST /api/v1/admin/brands`
   */
  adminBrandCreate(body: Models.BrandBody, options?: ApiRequestOptions): Observable<Models.BrandResponse> {
    return this.http.request<Models.BrandResponse>('POST', `${this.baseUrl}/api/v1/admin/brands`, body, undefined, options);
  }

  /**
   * Removes a brand. Refused while products still use it.
   * `DELETE /api/v1/admin/brands/{id}`
   */
  adminBrandDelete(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/brands/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Reads one brand.
   * `GET /api/v1/admin/brands/{id}`
   */
  adminBrandGet(id: string, options?: ApiRequestOptions): Observable<Models.BrandResponse> {
    return this.http.request<Models.BrandResponse>('GET', `${this.baseUrl}/api/v1/admin/brands/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Lists brands alphabetically.
   * `GET /api/v1/admin/brands`
   */
  adminBrandsList(query: AdminBrandsListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfBrandResponse> {
    return this.http.request<Models.PagedResultOfBrandResponse>('GET', `${this.baseUrl}/api/v1/admin/brands`, undefined, query, options);
  }

  /**
   * Updates a brand.
   * `PUT /api/v1/admin/brands/{id}`
   */
  adminBrandUpdate(id: string, body: Models.BrandBody, options?: ApiRequestOptions): Observable<Models.BrandResponse> {
    return this.http.request<Models.BrandResponse>('PUT', `${this.baseUrl}/api/v1/admin/brands/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Reads a job's progress, its validation report, and an export's download link.
   * `GET /api/v1/admin/jobs/{id}`
   */
  adminCatalogJobGet(id: string, options?: ApiRequestOptions): Observable<Models.CatalogJobResponse> {
    return this.http.request<Models.CatalogJobResponse>('GET', `${this.baseUrl}/api/v1/admin/jobs/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Lists bulk jobs, newest first. A vendor caller sees only their own.
   * `GET /api/v1/admin/jobs`
   */
  adminCatalogJobsList(query?: AdminCatalogJobsListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfCatalogJobResponse> {
    return this.http.request<Models.PagedResultOfCatalogJobResponse>('GET', `${this.baseUrl}/api/v1/admin/jobs`, undefined, query, options);
  }

  /**
   * Returns the browse tree, or the subtree beneath one node.
   * `GET /api/v1/admin/categories`
   */
  adminCategoriesTree(query?: AdminCategoriesTreeQuery, options?: ApiRequestOptions): Observable<Models.CategoryNode[]> {
    return this.http.request<Models.CategoryNode[]>('GET', `${this.baseUrl}/api/v1/admin/categories`, undefined, query, options);
  }

  /**
   * Creates a category under an optional parent.
   * `POST /api/v1/admin/categories`
   */
  adminCategoryCreate(body: Models.CategoryBody, options?: ApiRequestOptions): Observable<Models.CategoryResponse> {
    return this.http.request<Models.CategoryResponse>('POST', `${this.baseUrl}/api/v1/admin/categories`, body, undefined, options);
  }

  /**
   * Retires a category. Refused while it has children or products.
   * `DELETE /api/v1/admin/categories/{id}`
   */
  adminCategoryDelete(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/categories/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Reads one category, with the number of live products beneath it.
   * `GET /api/v1/admin/categories/{id}`
   */
  adminCategoryGet(id: string, options?: ApiRequestOptions): Observable<Models.CategoryResponse> {
    return this.http.request<Models.CategoryResponse>('GET', `${this.baseUrl}/api/v1/admin/categories/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Renames a category, or moves it and its whole subtree.
   * `PUT /api/v1/admin/categories/{id}`
   */
  adminCategoryUpdate(id: string, body: Models.CategoryBody, options?: ApiRequestOptions): Observable<Models.CategoryResponse> {
    return this.http.request<Models.CategoryResponse>('PUT', `${this.baseUrl}/api/v1/admin/categories/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Puts the offer on the storefront. Refused unless the seller, variant and product are all live.
   * `POST /api/v1/admin/listings/{id}/activate`
   */
  adminListingActivate(id: string, body?: null | Models.ListingStatusBody, options?: ApiRequestOptions): Observable<Models.ListingResponse> {
    return this.http.request<Models.ListingResponse>('POST', `${this.baseUrl}/api/v1/admin/listings/${encodeURIComponent(String(id))}/activate`, body, undefined, options);
  }

  /**
   * Retires the offer for good. Terminal, because order lines point at it.
   * `POST /api/v1/admin/listings/{id}/archive`
   */
  adminListingArchive(id: string, body?: null | Models.ListingStatusBody, options?: ApiRequestOptions): Observable<Models.ListingResponse> {
    return this.http.request<Models.ListingResponse>('POST', `${this.baseUrl}/api/v1/admin/listings/${encodeURIComponent(String(id))}/archive`, body, undefined, options);
  }

  /**
   * Opens an offer against a variant. One offer per seller per variant.
   * `POST /api/v1/admin/listings`
   */
  adminListingCreate(body: Models.CreateListingBody, options?: ApiRequestOptions): Observable<Models.ListingResponse> {
    return this.http.request<Models.ListingResponse>('POST', `${this.baseUrl}/api/v1/admin/listings`, body, undefined, options);
  }

  /**
   * Pauses the offer. It stops competing for the buy box immediately.
   * `POST /api/v1/admin/listings/{id}/deactivate`
   */
  adminListingDeactivate(id: string, body?: null | Models.ListingStatusBody, options?: ApiRequestOptions): Observable<Models.ListingResponse> {
    return this.http.request<Models.ListingResponse>('POST', `${this.baseUrl}/api/v1/admin/listings/${encodeURIComponent(String(id))}/deactivate`, body, undefined, options);
  }

  /**
   * Reads one offer. Answers 404 for an offer outside the caller's scope.
   * `GET /api/v1/admin/listings/{id}`
   */
  adminListingGet(id: string, options?: ApiRequestOptions): Observable<Models.ListingResponse> {
    return this.http.request<Models.ListingResponse>('GET', `${this.baseUrl}/api/v1/admin/listings/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Lists offers, newest first. A vendor caller sees only their own.
   * `GET /api/v1/admin/listings`
   */
  adminListingsList(query?: AdminListingsListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfListingResponse> {
    return this.http.request<Models.PagedResultOfListingResponse>('GET', `${this.baseUrl}/api/v1/admin/listings`, undefined, query, options);
  }

  /**
   * Changes an offer's price and terms. The price may never exceed the MRP.
   * `PUT /api/v1/admin/listings/{id}`
   */
  adminListingUpdate(id: string, body: Models.UpdateListingBody, options?: ApiRequestOptions): Observable<Models.ListingResponse> {
    return this.http.request<Models.ListingResponse>('PUT', `${this.baseUrl}/api/v1/admin/listings/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Approves a submitted product and publishes it.
   * `POST /api/v1/admin/products/{id}/approve`
   */
  adminProductApprove(id: string, body?: null | Models.ModerationBody, options?: ApiRequestOptions): Observable<Models.ProductResponse> {
    return this.http.request<Models.ProductResponse>('POST', `${this.baseUrl}/api/v1/admin/products/${encodeURIComponent(String(id))}/approve`, body, undefined, options);
  }

  /**
   * Retires a product for good. Terminal.
   * `POST /api/v1/admin/products/{id}/archive`
   */
  adminProductArchive(id: string, options?: ApiRequestOptions): Observable<Models.ProductResponse> {
    return this.http.request<Models.ProductResponse>('POST', `${this.baseUrl}/api/v1/admin/products/${encodeURIComponent(String(id))}/archive`, undefined, undefined, options);
  }

  /**
   * Moves several products to one status and reports on each. A product that could not move does not stop the ones that could; the response names it and why.
   * `POST /api/v1/admin/products/bulk-status`
   */
  adminProductBulkStatus(body: Models.BulkProductStatusBody, options?: ApiRequestOptions): Observable<Models.BulkProductStatusResponse> {
    return this.http.request<Models.BulkProductStatusResponse>('POST', `${this.baseUrl}/api/v1/admin/products/bulk-status`, body, undefined, options);
  }

  /**
   * Drafts a product. It is not sellable until it has a variant and is published.
   * `POST /api/v1/admin/products`
   */
  adminProductCreate(body: Models.ProductBody, options?: ApiRequestOptions): Observable<Models.ProductResponse> {
    return this.http.request<Models.ProductResponse>('POST', `${this.baseUrl}/api/v1/admin/products`, body, undefined, options);
  }

  /**
   * Retires a product, its variants and every offer against them.
   * `DELETE /api/v1/admin/products/{id}`
   */
  adminProductDelete(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/products/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Queues an export of the catalogue in the import's own column layout.
   * `POST /api/v1/admin/products/export`
   */
  adminProductExport(options?: ApiRequestOptions): Observable<Models.CatalogJobResponse> {
    return this.http.request<Models.CatalogJobResponse>('POST', `${this.baseUrl}/api/v1/admin/products/export`, undefined, undefined, options);
  }

  /**
   * Reads one product with its variants, gallery, attributes and compliance gaps.
   * `GET /api/v1/admin/products/{id}`
   */
  adminProductGet(id: string, options?: ApiRequestOptions): Observable<Models.ProductResponse> {
    return this.http.request<Models.ProductResponse>('GET', `${this.baseUrl}/api/v1/admin/products/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Queues a CSV import and returns the job id to poll.
   * `POST /api/v1/admin/products/import`
   */
  adminProductImport(body: { file: Blob }, options?: ApiRequestOptions): Observable<Models.CatalogJobResponse> {
    return this.http.request<Models.CatalogJobResponse>('POST', `${this.baseUrl}/api/v1/admin/products/import`, toFormData(body), undefined, options);
  }

  /**
   * The column headers a product import must be shaped like, in order.
   * `GET /api/v1/admin/products/import-template`
   */
  adminProductImportTemplate(options?: ApiRequestOptions): Observable<Models.ProductImportTemplateResponse> {
    return this.http.request<Models.ProductImportTemplateResponse>('GET', `${this.baseUrl}/api/v1/admin/products/import-template`, undefined, undefined, options);
  }

  /**
   * The moderation queue, oldest submission first.
   * `GET /api/v1/admin/product-moderation`
   */
  adminProductModerationQueue(query?: AdminProductModerationQueueQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfModerationResponse> {
    return this.http.request<Models.PagedResultOfModerationResponse>('GET', `${this.baseUrl}/api/v1/admin/product-moderation`, undefined, query, options);
  }

  /**
   * Puts a product on the storefront. Refused while its mandatory disclosures are incomplete.
   * `POST /api/v1/admin/products/{id}/publish`
   */
  adminProductPublish(id: string, options?: ApiRequestOptions): Observable<Models.ProductResponse> {
    return this.http.request<Models.ProductResponse>('POST', `${this.baseUrl}/api/v1/admin/products/${encodeURIComponent(String(id))}/publish`, undefined, undefined, options);
  }

  /**
   * Rejects a submitted product and returns it to draft. A reason is required.
   * `POST /api/v1/admin/products/{id}/reject`
   */
  adminProductReject(id: string, body: Models.ModerationBody, options?: ApiRequestOptions): Observable<Models.ProductResponse> {
    return this.http.request<Models.ProductResponse>('POST', `${this.baseUrl}/api/v1/admin/products/${encodeURIComponent(String(id))}/reject`, body, undefined, options);
  }

  /**
   * Replaces the product's gallery with the supplied, ordered list.
   * `PUT /api/v1/admin/products/{id}/media`
   */
  adminProductSetMedia(id: string, body: Models.MediaBody, options?: ApiRequestOptions): Observable<Models.ProductResponse> {
    return this.http.request<Models.ProductResponse>('PUT', `${this.baseUrl}/api/v1/admin/products/${encodeURIComponent(String(id))}/media`, body, undefined, options);
  }

  /**
   * Lists products, newest first. A vendor caller sees their own and the platform's.
   * `GET /api/v1/admin/products`
   */
  adminProductsList(query?: AdminProductsListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfProductListItem> {
    return this.http.request<Models.PagedResultOfProductListItem>('GET', `${this.baseUrl}/api/v1/admin/products`, undefined, query, options);
  }

  /**
   * Sends a product for moderation. Platform staff publish it outright.
   * `POST /api/v1/admin/products/{id}/submit`
   */
  adminProductSubmit(id: string, options?: ApiRequestOptions): Observable<Models.ProductResponse> {
    return this.http.request<Models.ProductResponse>('POST', `${this.baseUrl}/api/v1/admin/products/${encodeURIComponent(String(id))}/submit`, undefined, undefined, options);
  }

  /**
   * Takes a product off the storefront and pauses every offer against it.
   * `POST /api/v1/admin/products/{id}/unpublish`
   */
  adminProductUnpublish(id: string, options?: ApiRequestOptions): Observable<Models.ProductResponse> {
    return this.http.request<Models.ProductResponse>('POST', `${this.baseUrl}/api/v1/admin/products/${encodeURIComponent(String(id))}/unpublish`, undefined, undefined, options);
  }

  /**
   * Updates a product's copy, taxonomy and compliance declarations.
   * `PUT /api/v1/admin/products/{id}`
   */
  adminProductUpdate(id: string, body: Models.ProductBody, options?: ApiRequestOptions): Observable<Models.ProductResponse> {
    return this.http.request<Models.ProductResponse>('PUT', `${this.baseUrl}/api/v1/admin/products/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Makes the variant sellable. Refused while its mandatory disclosures are incomplete.
   * `POST /api/v1/admin/variants/{id}/activate`
   */
  adminVariantActivate(id: string, options?: ApiRequestOptions): Observable<Models.VariantResponse> {
    return this.http.request<Models.VariantResponse>('POST', `${this.baseUrl}/api/v1/admin/variants/${encodeURIComponent(String(id))}/activate`, undefined, undefined, options);
  }

  /**
   * Adds a variant. Its combination of options must be unique within the product.
   * `POST /api/v1/admin/products/{id}/variants`
   */
  adminVariantCreate(id: string, body: Models.VariantBody, options?: ApiRequestOptions): Observable<Models.VariantResponse> {
    return this.http.request<Models.VariantResponse>('POST', `${this.baseUrl}/api/v1/admin/products/${encodeURIComponent(String(id))}/variants`, body, undefined, options);
  }

  /**
   * Withdraws the variant and pauses every offer against it.
   * `POST /api/v1/admin/variants/{id}/deactivate`
   */
  adminVariantDeactivate(id: string, options?: ApiRequestOptions): Observable<Models.VariantResponse> {
    return this.http.request<Models.VariantResponse>('POST', `${this.baseUrl}/api/v1/admin/variants/${encodeURIComponent(String(id))}/deactivate`, undefined, undefined, options);
  }

  /**
   * Retires a variant. Refused if it is the last active one of a published product.
   * `DELETE /api/v1/admin/variants/{id}`
   */
  adminVariantDelete(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/variants/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Updates a variant, its pack declarations and its defining combination.
   * `PUT /api/v1/admin/variants/{id}`
   */
  adminVariantUpdate(id: string, body: Models.VariantBody, options?: ApiRequestOptions): Observable<Models.VariantResponse> {
    return this.http.request<Models.VariantResponse>('PUT', `${this.baseUrl}/api/v1/admin/variants/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * The brands this store carries, alphabetically.
   * `GET /api/v1/store/brands`
   */
  storeBrands(query?: StoreBrandsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfBrandResponse> {
    return this.http.request<Models.PagedResultOfBrandResponse>('GET', `${this.baseUrl}/api/v1/store/brands`, undefined, query, options);
  }

  /**
   * The browse tree, or the subtree beneath one node.
   * `GET /api/v1/store/categories`
   */
  storeCategories(query?: StoreCategoriesQuery, options?: ApiRequestOptions): Observable<Models.CategoryNode[]> {
    return this.http.request<Models.CategoryNode[]>('GET', `${this.baseUrl}/api/v1/store/categories`, undefined, query, options);
  }

  /**
   * One category page. A hidden category answers 404.
   * `GET /api/v1/store/categories/{slug}`
   */
  storeCategoryBySlug(slug: string, options?: ApiRequestOptions): Observable<Models.CategoryResponse> {
    return this.http.request<Models.CategoryResponse>('GET', `${this.baseUrl}/api/v1/store/categories/${encodeURIComponent(String(slug))}`, undefined, undefined, options);
  }

  /**
   * A product page: its variants, its mandatory disclosures, and each variant's buy box.
   * `GET /api/v1/store/products/{slug}`
   */
  storeProductBySlug(slug: string, options?: ApiRequestOptions): Observable<Models.StorefrontProduct> {
    return this.http.request<Models.StorefrontProduct>('GET', `${this.baseUrl}/api/v1/store/products/${encodeURIComponent(String(slug))}`, undefined, undefined, options);
  }

  /**
   * Every seller's offer for one variant, in buy-box order.
   * `GET /api/v1/store/products/{slug}/offers`
   */
  storeProductOffers(slug: string, query?: StoreProductOffersQuery, options?: ApiRequestOptions): Observable<Models.StorefrontOffer[]> {
    return this.http.request<Models.StorefrontOffer[]>('GET', `${this.baseUrl}/api/v1/store/products/${encodeURIComponent(String(slug))}/offers`, undefined, query, options);
  }
}
