import { KycDocumentType, VendorBusinessType, VendorStatus } from '@klarahome/data-access-admin';

/**
 * The seller vocabulary.
 *
 * Every value here **is** typed in the contract: `VendorStatus`, `VendorBusinessType`,
 * `KycDocumentType` and `CommissionPlanType` are real enums in the generated client, so the arrays
 * below are checked by the compiler and a value renamed on the server breaks the build rather than
 * a request. Step 28B made that true of the rest of the back office too.
 *
 * The labels are what an operator reads, and several of them are deliberately not the enum's own
 * word: `Applied` is a state, "waiting for us" is a job.
 */

export interface Choice<T extends string = string> {
  readonly value: T;
  readonly label: string;
  readonly hint?: string;
}

export const VENDOR_STATUSES: readonly Choice<VendorStatus>[] = [
  { value: 'Applied', label: 'Applied — waiting for us' },
  { value: 'UnderReview', label: 'Under review' },
  { value: 'Approved', label: 'Approved — not trading yet' },
  { value: 'Active', label: 'Active' },
  { value: 'Suspended', label: 'Suspended' },
  { value: 'Offboarded', label: 'Offboarded' },
];

export const BUSINESS_TYPES: readonly Choice<VendorBusinessType>[] = [
  { value: 'Individual', label: 'Individual' },
  { value: 'SoleProprietorship', label: 'Sole proprietorship' },
  { value: 'Partnership', label: 'Partnership' },
  { value: 'LimitedLiabilityPartnership', label: 'LLP' },
  { value: 'PrivateLimited', label: 'Private limited' },
  { value: 'PublicLimited', label: 'Public limited' },
  { value: 'HinduUndividedFamily', label: 'Hindu undivided family' },
  { value: 'Trust', label: 'Trust' },
];

export const KYC_DOCUMENT_TYPES: readonly Choice<KycDocumentType>[] = [
  { value: 'Pan', label: 'PAN card' },
  { value: 'Gstin', label: 'GST registration' },
  { value: 'CancelledCheque', label: 'Cancelled cheque' },
  { value: 'AddressProof', label: 'Proof of address' },
  { value: 'IdentityProof', label: 'Proof of identity' },
  { value: 'IncorporationCertificate', label: 'Certificate of incorporation' },
];

/**
 * The onboarding edges: which endpoint takes a seller to which state, and what to call it.
 *
 * **It no longer says when an edge is available.** Onboarding is six named endpoints rather than
 * one transition endpoint — "approve" and "suspend" are different acts with different permissions
 * and different required reasons — but `VendorResponse.nextStatuses` now carries the server's own
 * transition table, as an order, a return and a payout batch already did. The screen offers the
 * edges whose `to` is in that list, so what remains here is naming and wording: the last
 * client-side copy of a server rule in this application is gone (Step 28B, deliverable 5).
 */
export interface VendorTransition {
  readonly key: 'submit' | 'approve' | 'activate' | 'return' | 'suspend' | 'offboard';
  readonly label: string;
  /** Where this edge takes the seller. Matched against the server's `nextStatuses`. */
  readonly to: VendorStatus;
  readonly destructive?: boolean;
  /** Whether a reason is required rather than optional. */
  readonly requiresReason?: boolean;
}

export const VENDOR_TRANSITIONS: readonly VendorTransition[] = [
  { key: 'submit', label: 'Send for review', to: 'UnderReview' },
  { key: 'approve', label: 'Approve', to: 'Approved' },
  { key: 'activate', label: 'Activate', to: 'Active' },
  {
    key: 'return',
    label: 'Send back for more information',
    to: 'Applied',
    requiresReason: true,
  },
  {
    key: 'suspend',
    label: 'Suspend',
    to: 'Suspended',
    destructive: true,
    requiresReason: true,
  },
  {
    key: 'offboard',
    label: 'Offboard',
    to: 'Offboarded',
    destructive: true,
    requiresReason: true,
  },
];
