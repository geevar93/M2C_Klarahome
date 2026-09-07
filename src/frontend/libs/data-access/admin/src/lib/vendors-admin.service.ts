import { Injectable, inject } from '@angular/core';
import {
  AddBankAccountBody,
  BankAccountResponse,
  CommissionPlanResponse,
  CommissionQuote,
  CreateCommissionPlanBody,
  CreateVendorBody,
  KycSummaryResponse,
  PickupLocationBody,
  PickupLocationResponse,
  ServiceableRegionsResponse,
  SetServiceableRegionsBody,
  SubmitKycBody,
  UpdateCommissionPlanBody,
  UpdateVendorBody,
  UpdateVendorOperationsBody,
  UpdateVendorProfileBody,
  VendorListItem,
  VendorReadiness,
  VendorResponse,
  VendorStaffBody,
  VendorStaffResponse,
  VendorsApiClient,
} from '@klarahome/data-access-api';
import { Observable, map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

export interface VendorFilters {
  readonly status?: string;
  readonly search?: string;
}

/**
 * Sellers — the directory the platform keeps, and the record a seller keeps of itself.
 *
 * **One set of endpoints serves both screens**, which is the fact that shapes this service. A
 * platform manager reads a seller by id; a seller reads itself through `mine()`, and then uses the
 * id that came back for everything else. The API scopes each write to the caller's own vendor
 * where they hold no platform permission, so there is no second, seller-flavoured surface to keep
 * in step — and no way for a screen here to hand a seller somebody else's id and have it work.
 *
 * **Onboarding is a state machine, not a form.** `submit`, `approve`, `activate`, `suspend`,
 * `return_` and `offboard` are the edges; each takes a reason, because every one of them is a
 * decision a person will be asked to justify later. `readiness()` is the other half: it answers
 * what is still missing — documents, a verified bank account, a pickup address — so the seller's
 * own screen can show the list rather than letting them submit into a refusal.
 *
 * **The bank account is the one place this service deliberately knows less than the form.**
 * `AddBankAccountBody` carries a full account number on the way in and `BankAccountResponse`
 * carries only the last four on the way back (Step 9 encrypts it at rest). Nothing here holds it,
 * and nothing here can read it back.
 */
@Injectable({ providedIn: 'root' })
export class VendorsAdminService {
  private readonly api = inject(VendorsApiClient);

  // ---- The directory ----------------------------------------------------------------------------

  vendors(filters: VendorFilters = {}, pageSize = 25): CursorList<VendorListItem, VendorFilters> {
    return new CursorList<VendorListItem, VendorFilters>(
      (current, cursor, size) =>
        this.api
          .adminVendorsList({
            Status: current.status,
            Search: current.search,
            Cursor: cursor ?? undefined,
            Size: size,
          })
          .pipe(map((result): CursorPage<VendorListItem> => result)),
      filters,
      pageSize,
    );
  }

  vendor(id: string): Observable<VendorResponse> {
    return this.api.adminVendorGet(id);
  }

  /** The caller's own seller. Platform staff have none and are answered a 404, deliberately. */
  mine(): Observable<VendorResponse> {
    return this.api.adminVendorGetMine();
  }

  createVendor(body: CreateVendorBody): Observable<VendorResponse> {
    return this.api.adminVendorCreate(body);
  }

  /** The legal record: name, constitution, PAN, GSTIN, registered address. */
  updateVendor(id: string, body: UpdateVendorBody): Observable<VendorResponse> {
    return this.api.adminVendorUpdate(id, body);
  }

  /** The shopfront: display name, story, logo, banner, support contacts. */
  updateProfile(id: string, body: UpdateVendorProfileBody): Observable<VendorResponse> {
    return this.api.adminVendorUpdateProfile(id, body);
  }

  /** The promises: dispatch SLA, return policy, whether they ship everywhere. */
  updateOperations(id: string, body: UpdateVendorOperationsBody): Observable<VendorResponse> {
    return this.api.adminVendorUpdateOperations(id, body);
  }

  /** What still stands between this seller and being able to sell. */
  readiness(id: string): Observable<VendorReadiness> {
    return this.api.adminVendorReadiness(id);
  }

  // ---- The onboarding machine -------------------------------------------------------------------

  submit(id: string, reason: string | null = null): Observable<VendorResponse> {
    return this.api.adminVendorSubmit(id, { reason });
  }

  approve(id: string, reason: string | null = null): Observable<VendorResponse> {
    return this.api.adminVendorApprove(id, { reason });
  }

  activate(id: string, reason: string | null = null): Observable<VendorResponse> {
    return this.api.adminVendorActivate(id, { reason });
  }

  /** Sends an application back for more information. The reason is what the seller reads. */
  return_(id: string, reason: string | null): Observable<VendorResponse> {
    return this.api.adminVendorReturn(id, { reason });
  }

  suspend(id: string, reason: string | null): Observable<VendorResponse> {
    return this.api.adminVendorSuspend(id, { reason });
  }

  offboard(id: string, reason: string | null): Observable<VendorResponse> {
    return this.api.adminVendorOffboard(id, { reason });
  }

  // ---- KYC --------------------------------------------------------------------------------------

  /** Every document submitted, what is required, and what is still missing. */
  kyc(id: string): Observable<KycSummaryResponse> {
    return this.api.adminVendorKycList(id);
  }

  submitKyc(id: string, body: SubmitKycBody): Observable<unknown> {
    return this.api.adminVendorKycSubmit(id, body);
  }

  /** A rejection must carry its reason; the API refuses one without. */
  verifyKyc(
    id: string,
    documentId: string,
    approve: boolean,
    rejectionReason: string | null,
  ): Observable<unknown> {
    return this.api.adminVendorKycVerify(id, documentId, { approve, rejectionReason });
  }

  // ---- Bank accounts ----------------------------------------------------------------------------

  bankAccounts(id: string): Observable<BankAccountResponse[]> {
    return this.api.adminVendorBankAccountsList(id);
  }

  addBankAccount(id: string, body: AddBankAccountBody): Observable<BankAccountResponse> {
    return this.api.adminVendorBankAccountAdd(id, body);
  }

  /** Where a payout goes. Only one account can hold it. */
  makeBankAccountPrimary(id: string, accountId: string): Observable<BankAccountResponse> {
    return this.api.adminVendorBankAccountSetPrimary(id, accountId);
  }

  verifyBankAccount(
    id: string,
    accountId: string,
    verified: boolean,
    note: string | null,
  ): Observable<BankAccountResponse> {
    return this.api.adminVendorBankAccountVerify(id, accountId, { verified, note });
  }

  removeBankAccount(id: string, accountId: string): Observable<void> {
    return this.api.adminVendorBankAccountRemove(id, accountId);
  }

  // ---- Pickup locations -------------------------------------------------------------------------

  pickupLocations(id: string): Observable<PickupLocationResponse[]> {
    return this.api.adminVendorPickupLocationsList(id);
  }

  addPickupLocation(id: string, body: PickupLocationBody): Observable<PickupLocationResponse> {
    return this.api.adminVendorPickupLocationAdd(id, body);
  }

  updatePickupLocation(
    id: string,
    locationId: string,
    body: PickupLocationBody,
  ): Observable<PickupLocationResponse> {
    return this.api.adminVendorPickupLocationUpdate(id, locationId, body);
  }

  removePickupLocation(id: string, locationId: string): Observable<void> {
    return this.api.adminVendorPickupLocationRemove(id, locationId);
  }

  // ---- Where they will ship ---------------------------------------------------------------------

  serviceableRegions(id: string): Observable<ServiceableRegionsResponse> {
    return this.api.adminVendorServiceableRegionsGet(id);
  }

  /** Regions are inclusions and exclusions together; an exclusion beats an inclusion. */
  setServiceableRegions(id: string, body: SetServiceableRegionsBody): Observable<ServiceableRegionsResponse> {
    return this.api.adminVendorServiceableRegionsSet(id, body);
  }

  // ---- Staff ------------------------------------------------------------------------------------

  staff(id: string): Observable<VendorStaffResponse[]> {
    return this.api.adminVendorStaffList(id);
  }

  addStaff(id: string, body: VendorStaffBody): Observable<VendorStaffResponse> {
    return this.api.adminVendorStaffAdd(id, body);
  }

  updateStaff(id: string, membershipId: string, body: VendorStaffBody): Observable<VendorStaffResponse> {
    return this.api.adminVendorStaffUpdate(id, membershipId, body);
  }

  removeStaff(id: string, membershipId: string): Observable<void> {
    return this.api.adminVendorStaffRemove(id, membershipId);
  }

  // ---- Commission -------------------------------------------------------------------------------

  commissionPlans(includeInactive = false): Observable<CommissionPlanResponse[]> {
    return this.api.adminCommissionPlansList({ includeInactive });
  }

  commissionPlan(id: string): Observable<CommissionPlanResponse> {
    return this.api.adminCommissionPlanGet(id);
  }

  createCommissionPlan(body: CreateCommissionPlanBody): Observable<CommissionPlanResponse> {
    return this.api.adminCommissionPlanCreate(body);
  }

  updateCommissionPlan(id: string, body: UpdateCommissionPlanBody): Observable<CommissionPlanResponse> {
    return this.api.adminCommissionPlanUpdate(id, body);
  }

  assignCommissionPlan(vendorId: string, planId: string | null): Observable<VendorResponse> {
    return this.api.adminVendorAssignCommissionPlan(vendorId, { planId });
  }

  /**
   * What the platform would charge this seller for one sale at this price in this category.
   *
   * Resolved by the same code that will freeze the rate onto an order line, so the answer a
   * merchandiser reads here and the number on a settlement statement come from one place. It also
   * names which rule matched, which is the second question every seller asks.
   */
  previewCommission(vendorId: string, unitPrice: number, categoryId?: string): Observable<CommissionQuote> {
    return this.api.adminCommissionPreview({
      VendorId: vendorId,
      UnitPrice: unitPrice,
      CategoryId: categoryId,
    });
  }
}
