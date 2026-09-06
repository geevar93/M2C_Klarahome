/**
 * DO NOT EDIT. Generated from the API's OpenAPI document by tools/generate-api-client.mjs.
 *
 * Regenerate with:  pwsh tools/generate-api-client.ps1
 * CI fails if this file differs from what the current API produces.
 */
/* eslint-disable */

import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiRequestOptions, ApiTransport } from '../../runtime';
import type * as Models from '../models';

/** Query string for `adminCommissionPlansList`. */
export interface AdminCommissionPlansListQuery {
  includeInactive?: boolean;
}

/** Query string for `adminCommissionPreview`. */
export interface AdminCommissionPreviewQuery {
  VendorId: string;
  CategoryId?: string;
  UnitPrice: number;
}

/** Query string for `adminVendorsList`. */
export interface AdminVendorsListQuery {
  Status?: string;
  Search?: string;
  Cursor?: string;
  Size?: number;
}

/** `Vendors` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class VendorsApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Creates a commission plan.
   * `POST /api/v1/admin/commission-plans`
   */
  adminCommissionPlanCreate(body: Models.CreateCommissionPlanBody, options?: ApiRequestOptions): Observable<Models.CommissionPlanResponse> {
    return this.http.request<Models.CommissionPlanResponse>('POST', `${this.baseUrl}/api/v1/admin/commission-plans`, body, undefined, options);
  }

  /**
   * Reads one plan with its rules.
   * `GET /api/v1/admin/commission-plans/{id}`
   */
  adminCommissionPlanGet(id: string, options?: ApiRequestOptions): Observable<Models.CommissionPlanResponse> {
    return this.http.request<Models.CommissionPlanResponse>('GET', `${this.baseUrl}/api/v1/admin/commission-plans/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Lists the commission plans, the default first, with how many sellers are on each.
   * `GET /api/v1/admin/commission-plans`
   */
  adminCommissionPlansList(query?: AdminCommissionPlansListQuery, options?: ApiRequestOptions): Observable<Models.CommissionPlanResponse[]> {
    return this.http.request<Models.CommissionPlanResponse[]>('GET', `${this.baseUrl}/api/v1/admin/commission-plans`, undefined, query, options);
  }

  /**
   * Changes a plan and replaces its rules. Rates already quoted to a settlement are unaffected.
   * `PUT /api/v1/admin/commission-plans/{id}`
   */
  adminCommissionPlanUpdate(id: string, body: Models.UpdateCommissionPlanBody, options?: ApiRequestOptions): Observable<Models.CommissionPlanResponse> {
    return this.http.request<Models.CommissionPlanResponse>('PUT', `${this.baseUrl}/api/v1/admin/commission-plans/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Answers what a seller would be charged for a category and a price, without a sale. The one way to see which rule a price-banded ladder actually catches.
   * `GET /api/v1/admin/commission-plans/preview`
   */
  adminCommissionPreview(query: AdminCommissionPreviewQuery, options?: ApiRequestOptions): Observable<Models.CommissionQuote> {
    return this.http.request<Models.CommissionQuote>('GET', `${this.baseUrl}/api/v1/admin/commission-plans/preview`, undefined, query, options);
  }

  /**
   * Lets the seller trade. Refused unless every onboarding requirement is met.
   * `POST /api/v1/admin/vendors/{id}/activate`
   */
  adminVendorActivate(id: string, body?: null | Models.VendorStatusBody, options?: ApiRequestOptions): Observable<Models.VendorResponse> {
    return this.http.request<Models.VendorResponse>('POST', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/activate`, body, undefined, options);
  }

  /**
   * Records that the checks passed. The seller still cannot trade until they are activated.
   * `POST /api/v1/admin/vendors/{id}/approve`
   */
  adminVendorApprove(id: string, body?: null | Models.VendorStatusBody, options?: ApiRequestOptions): Observable<Models.VendorResponse> {
    return this.http.request<Models.VendorResponse>('POST', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/approve`, body, undefined, options);
  }

  /**
   * Puts a seller on a commission plan.
   * `PUT /api/v1/admin/vendors/{id}/commission-plan`
   */
  adminVendorAssignCommissionPlan(id: string, body: Models.AssignCommissionPlanBody, options?: ApiRequestOptions): Observable<Models.VendorResponse> {
    return this.http.request<Models.VendorResponse>('PUT', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/commission-plan`, body, undefined, options);
  }

  /**
   * Records a payout account. The number is encrypted at the column and never read back.
   * `POST /api/v1/admin/vendors/{id}/bank-accounts`
   */
  adminVendorBankAccountAdd(id: string, body: Models.AddBankAccountBody, options?: ApiRequestOptions): Observable<Models.BankAccountResponse> {
    return this.http.request<Models.BankAccountResponse>('POST', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/bank-accounts`, body, undefined, options);
  }

  /**
   * Removes an account the seller no longer uses.
   * `DELETE /api/v1/admin/vendors/{id}/bank-accounts/{accountId}`
   */
  adminVendorBankAccountRemove(id: string, accountId: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/bank-accounts/${encodeURIComponent(String(accountId))}`, undefined, undefined, options);
  }

  /**
   * Sends this seller's payouts to this account.
   * `PUT /api/v1/admin/vendors/{id}/bank-accounts/{accountId}/primary`
   */
  adminVendorBankAccountSetPrimary(id: string, accountId: string, options?: ApiRequestOptions): Observable<Models.BankAccountResponse> {
    return this.http.request<Models.BankAccountResponse>('PUT', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/bank-accounts/${encodeURIComponent(String(accountId))}/primary`, undefined, undefined, options);
  }

  /**
   * Lists a seller's payout accounts. Account numbers are never returned.
   * `GET /api/v1/admin/vendors/{id}/bank-accounts`
   */
  adminVendorBankAccountsList(id: string, options?: ApiRequestOptions): Observable<Models.BankAccountResponse[]> {
    return this.http.request<Models.BankAccountResponse[]>('GET', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/bank-accounts`, undefined, undefined, options);
  }

  /**
   * Records the outcome of a penny drop or a cancelled-cheque check.
   * `POST /api/v1/admin/vendors/{id}/bank-accounts/{accountId}/verify`
   */
  adminVendorBankAccountVerify(id: string, accountId: string, body: Models.VerifyBankAccountBody, options?: ApiRequestOptions): Observable<Models.BankAccountResponse> {
    return this.http.request<Models.BankAccountResponse>('POST', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/bank-accounts/${encodeURIComponent(String(accountId))}/verify`, body, undefined, options);
  }

  /**
   * Registers an application to sell. The seller starts in Applied.
   * `POST /api/v1/admin/vendors`
   */
  adminVendorCreate(body: Models.CreateVendorBody, options?: ApiRequestOptions): Observable<Models.VendorResponse> {
    return this.http.request<Models.VendorResponse>('POST', `${this.baseUrl}/api/v1/admin/vendors`, body, undefined, options);
  }

  /**
   * Reads one seller. Answers 404 for a seller outside the caller's scope.
   * `GET /api/v1/admin/vendors/{id}`
   */
  adminVendorGet(id: string, options?: ApiRequestOptions): Observable<Models.VendorResponse> {
    return this.http.request<Models.VendorResponse>('GET', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * Returns the seller the caller acts for.
   * `GET /api/v1/admin/vendors/me`
   */
  adminVendorGetMine(options?: ApiRequestOptions): Observable<Models.VendorResponse> {
    return this.http.request<Models.VendorResponse>('GET', `${this.baseUrl}/api/v1/admin/vendors/me`, undefined, undefined, options);
  }

  /**
   * Lists a seller's documents, what their legal form requires, and what is missing. Each document carries a short-lived link to its scan.
   * `GET /api/v1/admin/vendors/{id}/kyc-documents`
   */
  adminVendorKycList(id: string, options?: ApiRequestOptions): Observable<Models.KycSummaryResponse> {
    return this.http.request<Models.KycSummaryResponse>('GET', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/kyc-documents`, undefined, undefined, options);
  }

  /**
   * Submits a document, replacing any previous one of the same kind. The file must already have been uploaded as private media.
   * `POST /api/v1/admin/vendors/{id}/kyc-documents`
   */
  adminVendorKycSubmit(id: string, body: Models.SubmitKycBody, options?: ApiRequestOptions): Observable<Models.KycDocumentResponse> {
    return this.http.request<Models.KycDocumentResponse>('POST', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/kyc-documents`, body, undefined, options);
  }

  /**
   * Accepts or refuses a document. A refusal must say why.
   * `POST /api/v1/admin/vendors/{id}/kyc-documents/{documentId}/verify`
   */
  adminVendorKycVerify(id: string, documentId: string, body: Models.VerifyKycBody, options?: ApiRequestOptions): Observable<Models.KycDocumentResponse> {
    return this.http.request<Models.KycDocumentResponse>('POST', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/kyc-documents/${encodeURIComponent(String(documentId))}/verify`, body, undefined, options);
  }

  /**
   * Ends the relationship. Terminal, and a reason is required.
   * `POST /api/v1/admin/vendors/{id}/offboard`
   */
  adminVendorOffboard(id: string, body?: null | Models.VendorStatusBody, options?: ApiRequestOptions): Observable<Models.VendorResponse> {
    return this.http.request<Models.VendorResponse>('POST', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/offboard`, body, undefined, options);
  }

  /**
   * Adds a place a courier may collect from.
   * `POST /api/v1/admin/vendors/{id}/pickup-locations`
   */
  adminVendorPickupLocationAdd(id: string, body: Models.PickupLocationBody, options?: ApiRequestOptions): Observable<Models.PickupLocationResponse> {
    return this.http.request<Models.PickupLocationResponse>('POST', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/pickup-locations`, body, undefined, options);
  }

  /**
   * Removes a pickup location. A seller must keep at least one.
   * `DELETE /api/v1/admin/vendors/{id}/pickup-locations/{locationId}`
   */
  adminVendorPickupLocationRemove(id: string, locationId: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/pickup-locations/${encodeURIComponent(String(locationId))}`, undefined, undefined, options);
  }

  /**
   * Lists where couriers collect from, the default first.
   * `GET /api/v1/admin/vendors/{id}/pickup-locations`
   */
  adminVendorPickupLocationsList(id: string, options?: ApiRequestOptions): Observable<Models.PickupLocationResponse[]> {
    return this.http.request<Models.PickupLocationResponse[]>('GET', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/pickup-locations`, undefined, undefined, options);
  }

  /**
   * Changes a pickup location. Changing the address clears its courier registration.
   * `PUT /api/v1/admin/vendors/{id}/pickup-locations/{locationId}`
   */
  adminVendorPickupLocationUpdate(id: string, locationId: string, body: Models.PickupLocationBody, options?: ApiRequestOptions): Observable<Models.PickupLocationResponse> {
    return this.http.request<Models.PickupLocationResponse>('PUT', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/pickup-locations/${encodeURIComponent(String(locationId))}`, body, undefined, options);
  }

  /**
   * Lists everything standing between this seller and activation.
   * `GET /api/v1/admin/vendors/{id}/readiness`
   */
  adminVendorReadiness(id: string, options?: ApiRequestOptions): Observable<Models.VendorReadiness> {
    return this.http.request<Models.VendorReadiness>('GET', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/readiness`, undefined, undefined, options);
  }

  /**
   * Sends an application back to the seller for more information.
   * `POST /api/v1/admin/vendors/{id}/return`
   */
  adminVendorReturn(id: string, body?: null | Models.VendorStatusBody, options?: ApiRequestOptions): Observable<Models.VendorResponse> {
    return this.http.request<Models.VendorResponse>('POST', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/return`, body, undefined, options);
  }

  /**
   * Reads where this seller is willing to deliver.
   * `GET /api/v1/admin/vendors/{id}/serviceable-regions`
   */
  adminVendorServiceableRegionsGet(id: string, options?: ApiRequestOptions): Observable<Models.ServiceableRegionsResponse> {
    return this.http.request<Models.ServiceableRegionsResponse>('GET', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/serviceable-regions`, undefined, undefined, options);
  }

  /**
   * Replaces the whole set of serviceability rules.
   * `PUT /api/v1/admin/vendors/{id}/serviceable-regions`
   */
  adminVendorServiceableRegionsSet(id: string, body: Models.SetServiceableRegionsBody, options?: ApiRequestOptions): Observable<Models.ServiceableRegionsResponse> {
    return this.http.request<Models.ServiceableRegionsResponse>('PUT', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/serviceable-regions`, body, undefined, options);
  }

  /**
   * Lists sellers, newest first. A vendor caller sees only their own.
   * `GET /api/v1/admin/vendors`
   */
  adminVendorsList(query?: AdminVendorsListQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfVendorListItem> {
    return this.http.request<Models.PagedResultOfVendorListItem>('GET', `${this.baseUrl}/api/v1/admin/vendors`, undefined, query, options);
  }

  /**
   * Puts an existing user account on this seller. Create the account, and grant its vendor role, through the users endpoints first.
   * `POST /api/v1/admin/vendors/{id}/staff`
   */
  adminVendorStaffAdd(id: string, body: Models.VendorStaffBody, options?: ApiRequestOptions): Observable<Models.VendorStaffResponse> {
    return this.http.request<Models.VendorStaffResponse>('POST', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/staff`, body, undefined, options);
  }

  /**
   * Lists who operates this seller's account, the owner first.
   * `GET /api/v1/admin/vendors/{id}/staff`
   */
  adminVendorStaffList(id: string, options?: ApiRequestOptions): Observable<Models.VendorStaffResponse[]> {
    return this.http.request<Models.VendorStaffResponse[]>('GET', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/staff`, undefined, undefined, options);
  }

  /**
   * Takes a person off this seller. Their user account is untouched.
   * `DELETE /api/v1/admin/vendors/{id}/staff/{membershipId}`
   */
  adminVendorStaffRemove(id: string, membershipId: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/staff/${encodeURIComponent(String(membershipId))}`, undefined, undefined, options);
  }

  /**
   * Changes a member's job title, or moves the ownership to them.
   * `PUT /api/v1/admin/vendors/{id}/staff/{membershipId}`
   */
  adminVendorStaffUpdate(id: string, membershipId: string, body: Models.VendorStaffBody, options?: ApiRequestOptions): Observable<Models.VendorStaffResponse> {
    return this.http.request<Models.VendorStaffResponse>('PUT', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/staff/${encodeURIComponent(String(membershipId))}`, body, undefined, options);
  }

  /**
   * Sends an application for review.
   * `POST /api/v1/admin/vendors/{id}/submit`
   */
  adminVendorSubmit(id: string, body?: null | Models.VendorStatusBody, options?: ApiRequestOptions): Observable<Models.VendorResponse> {
    return this.http.request<Models.VendorResponse>('POST', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/submit`, body, undefined, options);
  }

  /**
   * Stops new sales. Open orders are unaffected. A reason is required.
   * `POST /api/v1/admin/vendors/{id}/suspend`
   */
  adminVendorSuspend(id: string, body?: null | Models.VendorStatusBody, options?: ApiRequestOptions): Observable<Models.VendorResponse> {
    return this.http.request<Models.VendorResponse>('POST', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/suspend`, body, undefined, options);
  }

  /**
   * Updates the registration details an approval depends on.
   * `PUT /api/v1/admin/vendors/{id}`
   */
  adminVendorUpdate(id: string, body: Models.UpdateVendorBody, options?: ApiRequestOptions): Observable<Models.VendorResponse> {
    return this.http.request<Models.VendorResponse>('PUT', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}`, body, undefined, options);
  }

  /**
   * Updates the dispatch SLA and return policy fulfilment reads.
   * `PUT /api/v1/admin/vendors/{id}/operations`
   */
  adminVendorUpdateOperations(id: string, body: Models.UpdateVendorOperationsBody, options?: ApiRequestOptions): Observable<Models.VendorResponse> {
    return this.http.request<Models.VendorResponse>('PUT', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/operations`, body, undefined, options);
  }

  /**
   * Updates the storefront profile: name, blurb, logo, banner and support contacts.
   * `PUT /api/v1/admin/vendors/{id}/profile`
   */
  adminVendorUpdateProfile(id: string, body: Models.UpdateVendorProfileBody, options?: ApiRequestOptions): Observable<Models.VendorResponse> {
    return this.http.request<Models.VendorResponse>('PUT', `${this.baseUrl}/api/v1/admin/vendors/${encodeURIComponent(String(id))}/profile`, body, undefined, options);
  }

  /**
   * A seller's public profile. A seller who is not trading answers 404, whatever the reason — a suspension is between the platform and the seller.
   * `GET /api/v1/store/vendors/{slug}`
   */
  storeVendorGet(slug: string, options?: ApiRequestOptions): Observable<Models.StorefrontVendorResponse> {
    return this.http.request<Models.StorefrontVendorResponse>('GET', `${this.baseUrl}/api/v1/store/vendors/${encodeURIComponent(String(slug))}`, undefined, undefined, options);
  }
}
