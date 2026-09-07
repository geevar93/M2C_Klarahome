import { KycDocumentType, VendorBusinessType, VendorStatus } from '@klarahome/data-access-admin';

/**
 * The seller vocabulary.
 *
 * Unusually for this application, most of these **are** typed in the contract:
 * `VendorStatus`, `VendorBusinessType`, `KycDocumentType` and `CommissionPlanType` are real enums
 * in the generated client, so the arrays below are checked by the compiler and a value renamed on
 * the server breaks the build rather than a request. That is what the Step 24/26/27 parking-lot
 * rows are asking for everywhere else, and it is worth naming where it already exists.
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
 * The onboarding edges, in the order they are normally taken.
 *
 * Unlike an order or a return, a seller's record carries **no `nextStatuses`** — the transitions
 * are separate endpoints rather than one transition table, so this list is a client-side statement
 * about which button to offer from which state. It is therefore a second copy of a rule the server
 * owns, and it is recorded in `PARKING_LOT.md` as such: **the API refuses an edge that is not
 * allowed, and the screen shows the refusal**, so the copy can be wrong without being unsafe.
 */
export interface VendorTransition {
  readonly key: 'submit' | 'approve' | 'activate' | 'return' | 'suspend' | 'offboard';
  readonly label: string;
  /** The statuses this edge is normally taken from. */
  readonly from: readonly VendorStatus[];
  readonly destructive?: boolean;
  /** Whether a reason is required rather than optional. */
  readonly requiresReason?: boolean;
}

export const VENDOR_TRANSITIONS: readonly VendorTransition[] = [
  { key: 'submit', label: 'Send for review', from: ['Applied'] },
  { key: 'approve', label: 'Approve', from: ['UnderReview'] },
  { key: 'activate', label: 'Activate', from: ['Approved', 'Suspended'] },
  {
    key: 'return',
    label: 'Send back for more information',
    from: ['UnderReview'],
    requiresReason: true,
  },
  {
    key: 'suspend',
    label: 'Suspend',
    from: ['Active'],
    destructive: true,
    requiresReason: true,
  },
  {
    key: 'offboard',
    label: 'Offboard',
    from: ['Active', 'Suspended', 'Approved'],
    destructive: true,
    requiresReason: true,
  },
];
