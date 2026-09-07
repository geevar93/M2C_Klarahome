/**
 * DO NOT EDIT. Generated from the API's OpenAPI document by tools/generate-api-client.mjs.
 *
 * Regenerate with:  pwsh tools/generate-api-client.ps1
 * CI fails if this file differs from what the current API produces.
 */
/* eslint-disable */

export interface AbuseReportResponse {
  id: string;
  target: string;
  targetId: string;
  reason: string;
  note: string | null;
  status: string;
  resolvedAt: string | null;
  resolution: string | null;
  createdAt: string;
}

export interface AddBankAccountBody {
  accountName: string;
  accountNumber: string;
  ifsc: string;
  bankName: string | null;
  branchName: string | null;
  makePrimary: boolean;
}

export interface AddCartItemBody {
  listingId: string;
  quantity: number | null;
}

export interface AddCollectionItemBody {
  productId: string;
  isPinned: boolean;
}

export interface AddressInput {
  label: string | null;
  recipientName: string;
  mobile: string;
  line1: string;
  line2: string | null;
  landmark: string | null;
  city: string;
  stateId: string;
  pincode: string;
  gstin: string | null;
  type: AddressType;
  isDefaultShipping: boolean;
  isDefaultBilling: boolean;
}

export interface AddressPayload {
  line1: string;
  line2: string | null;
  city: string;
  stateId: string;
  pincode: string;
}

export interface AddressResponse {
  id: string;
  label: string | null;
  recipientName: string;
  mobile: string;
  line1: string;
  line2: string | null;
  landmark: string | null;
  city: string;
  stateId: string;
  pincode: string;
  gstin: string | null;
  type: AddressType;
  isDefaultShipping: boolean;
  isDefaultBilling: boolean;
}

export type AddressType =
  | "Home"
  | "Office";

export interface AdjustWalletBody {
  amount: number;
  reason: string;
  note: string | null;
  expiresAt: string | null;
}

export interface AdjustmentBody {
  vendorId: string;
  direction: string | null;
  amount: number;
  reason: string | null;
}

export interface AdminCartSummary {
  id: string;
  customerId: string | null;
  status: string;
  currencyCode: string;
  couponCode: string | null;
  lineCount: number;
  estimatedValue: number;
  lastActivityAt: string;
  abandonedAt: string | null;
  reminderCount: number;
  convertedOrderId: string | null;
}

export interface AdminUserResponse {
  id: string;
  userType: UserType;
  email: string | null;
  mobile: string | null;
  status: UserStatus;
  mobileVerified: boolean;
  emailVerified: boolean;
  twoFactorEnabled: boolean;
  lockedUntil: string | null;
  lastLoginAt: string | null;
  createdAt: string;
  vendorId: string | null;
  roles: string[];
  mustChangePassword: boolean;
  passwordSetupPending: boolean;
}

export interface AnswerBody {
  body: string | null;
}

export interface AnswerResponse {
  id: string;
  body: string;
  authorType: string;
  authorName: string | null;
  publishedAt: string | null;
}

export interface ApproveReturnBody {
  amount: number | null;
  pickupRequired: boolean | null;
  note: string | null;
}

export interface AssignCommissionPlanBody {
  planId: string | null;
}

export interface AttributeBody {
  code: string | null;
  name: string;
  dataType: AttributeDataType;
  unit: string | null;
  isVariantDefining: boolean;
  isFilterable: boolean;
  isSearchable: boolean;
  isRequired: boolean;
  position: number;
  options: AttributeOptionPayload[] | null;
}

export type AttributeDataType =
  | "Text"
  | "Number"
  | "Boolean"
  | "Select"
  | "MultiSelect"
  | "Date";

export interface AttributeOptionPayload {
  id: string | null;
  value: string;
  label: string;
  swatchHex: string | null;
  position: number;
}

export interface AttributeOptionResponse {
  id: string;
  value: string;
  label: string;
  swatchHex: string | null;
  position: number;
}

export interface AttributeResponse {
  id: string;
  code: string;
  name: string;
  dataType: AttributeDataType;
  unit: string | null;
  isVariantDefining: boolean;
  isFilterable: boolean;
  isSearchable: boolean;
  isRequired: boolean;
  position: number;
  options: AttributeOptionResponse[];
}

export interface AttributeSetBody {
  code: string | null;
  name: string;
  description: string | null;
  attributes: AttributeSetMemberPayload[] | null;
}

export interface AttributeSetMemberPayload {
  attributeId: string;
  isRequired: boolean;
  position: number;
}

export interface AttributeSetMemberResponse {
  attributeId: string;
  code: string;
  name: string;
  dataType: AttributeDataType;
  isRequired: boolean;
  position: number;
}

export interface AttributeSetResponse {
  id: string;
  code: string;
  name: string;
  description: string | null;
  attributes: AttributeSetMemberResponse[];
}

export interface AttributeValuePayload {
  attributeId: string;
  text: string | null;
  optionId: string | null;
}

export interface AttributeValueResponse {
  attributeId: string;
  code: string;
  name: string;
  dataType: AttributeDataType;
  unit: string | null;
  value: string | null;
  optionId: string | null;
}

/** The class of actor behind an audited action. */
export type AuditActorType =
  | "System"
  | "Anonymous"
  | "Customer"
  | "VendorUser"
  | "StaffUser";

export interface AuditLogResponse {
  id: string;
  occurredAt: string;
  action: string;
  entityType: string;
  entityId: string | null;
  actorType: AuditActorType;
  actorId: string | null;
  before: null | unknown;
  after: null | unknown;
  ip: string | null;
  userAgent: string | null;
  correlationId: string | null;
}

export interface AuthenticatedUserResponse {
  id: string;
  userType: string;
  mobile: string | null;
  email: string | null;
  mobileVerified: boolean;
  emailVerified: boolean;
  twoFactorEnabled: boolean;
  vendorId: string | null;
  roles: string[];
  permissions: string[];
}

export interface BankAccountResponse {
  id: string;
  vendorId: string | null;
  accountName: string;
  accountNumberLast4: string;
  ifsc: string;
  bankName: string | null;
  branchName: string | null;
  isPrimary: boolean;
  verificationStatus: BankVerificationStatus;
  verifiedAt: string | null;
  verificationNote: string | null;
}

export type BankVerificationStatus =
  | "Unverified"
  | "Verified"
  | "Failed";

export type BannerAudience = string;

export interface BannerBody {
  name: string | null;
  placement: BannerPlacement;
  mediaFileId: string | null;
  mobileMediaFileId: string | null;
  message: string | null;
  altText: string | null;
  link: string | null;
  ctaLabel: string | null;
  priority: number;
  startsAt: string | null;
  endsAt: string | null;
  audience: null | BannerAudience;
  isActive: boolean;
}

export type BannerPlacement =
  | "AnnouncementBar"
  | "HomeHero"
  | "HomeStrip"
  | "CategoryHeader"
  | "ListingSidebar"
  | "ProductStrip"
  | "CartStrip";

export interface BannerResponse {
  id: string;
  name: string;
  placement: BannerPlacement;
  image: null | ContentImageResponse;
  mobileImage: null | ContentImageResponse;
  message: string | null;
  altText: string | null;
  link: string | null;
  ctaLabel: string | null;
  priority: number;
  startsAt: string | null;
  endsAt: string | null;
  audience: BannerAudience;
  isActive: boolean;
  isLive: boolean;
  updatedAt: string | null;
}

export interface BlockBody {
  id: string | null;
  type: string | null;
  config: unknown;
  isVisible: boolean;
  startsAt: string | null;
  endsAt: string | null;
}

export interface BlockFieldResponse {
  name: string;
  kind: string;
  isRequired: boolean;
  isList: boolean;
  maxLength: number;
  choices: string[] | null;
}

export interface BlockResponse {
  id: string;
  type: string;
  position: number;
  config: unknown;
  isVisible: boolean;
  startsAt: string | null;
  endsAt: string | null;
}

export interface BlockTypeResponse {
  type: string;
  label: string;
  description: string;
  fields: BlockFieldResponse[];
  itemFields: BlockFieldResponse[] | null;
  maxItems: number;
  isPrivileged: boolean;
}

export interface BlogCardResponse {
  slug: string;
  title: string;
  summary: string | null;
  author: string | null;
  tags: string[];
  coverImage: null | ContentImageResponse;
  publishedAt: string | null;
}

export interface BookBody {
  courier: string | null;
  pickupLocationId: string | null;
  manualAwb: string | null;
  manualCourier: string | null;
}

export interface BrandBody {
  name: string;
  slug: string | null;
  description: string | null;
  logoFileId: string | null;
  isActive: boolean;
  seo: null | SeoPayload;
}

export interface BrandResponse {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  logoFileId: string | null;
  isActive: boolean;
  seo: SeoPayload;
}

export interface BulkProductStatusBody {
  productIds: string[] | null;
  status: ProductStatus;
}

export interface BulkProductStatusOutcome {
  productId: string;
  changed: boolean;
  errorCode: string | null;
  message: string | null;
}

export interface BulkProductStatusResponse {
  changed: number;
  failed: number;
  results: BulkProductStatusOutcome[];
}

export interface CancelLineBody {
  orderLineId: string;
  quantity: number;
}

export interface CancelOrderBody {
  reason: string | null;
  lines: CancelLineBody[] | null;
}

export interface CancelPayoutBatchBody {
  reason: string | null;
}

export interface CancelReturnBody {
  reason: string | null;
}

export interface CancelShipmentBody {
  reason: string | null;
}

export interface CaptureBody {
  amount: number | null;
}

export interface CartIssue {
  code: string;
  message: string;
  isBlocking: boolean;
}

export interface CartLineResponse {
  id: string;
  listingId: string;
  vendorId: string;
  vendorName: string;
  sku: string;
  name: string;
  imageFileId: string | null;
  quantity: number;
  savedForLater: boolean;
  unitMrp: number;
  unitPrice: number;
  unitPriceWhenAdded: number;
  lineTotal: number;
  quantityAvailable: number;
  isCodAllowed: boolean;
  issues: CartIssue[];
}

export interface CartResponse {
  id: string;
  customerId: string | null;
  status: string;
  currencyCode: string;
  couponCode: string | null;
  lineCount: number;
  expiresAt: string;
  lines: CartLineResponse[];
  savedForLater: CartLineResponse[];
  groups: CartVendorGroupResponse[];
  quote: null | QuoteResult;
  issues: CartIssue[];
  isReadyForCheckout: boolean;
}

export interface CartVendorGroupResponse {
  vendorId: string;
  vendorName: string;
  dispatchSlaHours: number;
  lineIds: string[];
  subtotal: number;
  discount: number;
  taxTotal: number;
  shipping: number;
  total: number;
}

export type CatalogJobKind =
  | "ProductImport"
  | "ProductExport";

export interface CatalogJobResponse {
  id: string;
  kind: CatalogJobKind;
  status: CatalogJobStatus;
  fileName: string;
  totalRows: number;
  processedRows: number;
  succeededRows: number;
  failedRows: number;
  errors: ImportErrorResponse[];
  failureReason: string | null;
  downloadUrl: string | null;
  startedAt: string | null;
  completedAt: string | null;
  createdAt: string;
}

export type CatalogJobStatus =
  | "Queued"
  | "Running"
  | "Succeeded"
  | "PartiallySucceeded"
  | "Failed";

export type CatalogMediaKind =
  | "Image"
  | "Video"
  | "Document";

export interface CategoryBody {
  parentId: string | null;
  name: string;
  slug: string | null;
  description: string | null;
  imageFileId: string | null;
  attributeSetId: string | null;
  position: number;
  isActive: boolean;
  seo: null | SeoPayload;
}

export interface CategoryNode {
  id: string;
  name: string;
  slug: string;
  level: number;
  position: number;
  isActive: boolean;
  imageFileId: string | null;
  children: CategoryNode[];
}

export interface CategoryResponse {
  id: string;
  parentId: string | null;
  name: string;
  slug: string;
  description: string | null;
  path: string;
  level: number;
  position: number;
  isActive: boolean;
  imageFileId: string | null;
  attributeSetId: string | null;
  seo: SeoPayload;
  productCount: number;
}

export interface CategoryTileResponse {
  categoryId: string;
  name: string;
  slug: string;
  image: null | ContentImageResponse;
}

export interface ChangePasswordBody {
  challengeToken: string | null;
  currentPassword: string;
  newPassword: string;
}

export interface CheckoutAddressBody {
  shippingAddressId: string;
  billingAddressId: string | null;
  gstin: string | null;
}

export interface CheckoutAddressResponse {
  sourceAddressId: string;
  recipientName: string;
  mobile: string;
  line1: string;
  line2: string | null;
  landmark: string | null;
  city: string;
  stateId: string;
  pincode: string;
  gstin: string | null;
  isBusiness: boolean;
}

export interface CheckoutPaymentMethodBody {
  method: string;
}

export interface CheckoutResponse {
  id: string;
  cartId: string;
  status: string;
  currencyCode: string;
  shippingAddress: null | CheckoutAddressResponse;
  billingAddress: null | CheckoutAddressResponse;
  gstin: string | null;
  paymentMethod: string;
  shipments: VendorShippingOptionsResponse[];
  cart: CartResponse;
  expiresAt: string;
  orderNumber: string | null;
}

export interface CheckoutShippingBody {
  perVendor: VendorShippingChoiceBody[];
}

export interface CloseCycleBody {
  vendorId: string | null;
  force?: boolean;
}

export interface CloseReturnBody {
  note: string | null;
}

export interface CodCollectionResponse {
  id: string;
  orderId: string;
  subOrderId: string;
  vendorId: string | null;
  shipmentId: string | null;
  amount: number;
  currencyCode: string;
  status: string;
  collectedAmount: number | null;
  collectedAt: string | null;
  remittedAmount: number | null;
  remittedAt: string | null;
  remittanceReference: string | null;
  note: string | null;
}

export interface CollectCashBody {
  amount: number;
  collectedAt: string | null;
}

export interface CollectionItemBody {
  productId: string;
  isPinned: boolean;
}

export type CollectionKind =
  | "Manual"
  | "Rule";

export interface CollectionResponse {
  id: string;
  slug: string;
  name: string;
  description: string | null;
  kind: CollectionKind;
  rule: null | CollectionRuleResponse;
  seo: SeoResponse;
  heroImage: null | ContentImageResponse;
  isActive: boolean;
  isListed: boolean;
  itemCount: number;
  refreshedAt: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface CollectionRuleBody {
  matchAll: boolean;
  conditions: RuleConditionBody[] | null;
  sort: null | CollectionSort;
  limit: number;
  includeOutOfStock: boolean;
}

export interface CollectionRuleResponse {
  matchAll: boolean;
  conditions: RuleConditionResponse[];
  sort: CollectionSort;
  limit: number;
  includeOutOfStock: boolean;
}

export type CollectionSort =
  | "Newest"
  | "PriceAscending"
  | "PriceDescending"
  | "Discount"
  | "Rating";

export interface CollectionSummaryResponse {
  id: string;
  slug: string;
  name: string;
  kind: CollectionKind;
  isActive: boolean;
  isListed: boolean;
  itemCount: number;
  refreshedAt: string | null;
  updatedAt: string | null;
}

export interface CommissionInvoiceDownloadResponse {
  invoiceId: string;
  invoiceNumber: string;
  url: string;
}

export interface CommissionInvoiceResponse {
  id: string;
  settlementCycleId: string;
  vendorId: string;
  invoiceNumber: string;
  issuedAt: string;
  periodStart: string;
  periodEnd: string;
  commission: number;
  platformFee: number;
  paymentFee: number;
  taxableValue: number;
  gstRate: number;
  cgst: number;
  sgst: number;
  igst: number;
  taxTotal: number;
  total: number;
  currencyCode: string;
  supplierGstin: string | null;
  recipientGstin: string | null;
  placeOfSupplyStateCode: string | null;
  hasDocument: boolean;
}

export interface CommissionPlanResponse {
  id: string;
  code: string;
  name: string;
  description: string | null;
  planType: CommissionPlanType;
  defaultRate: number;
  defaultFixedFee: number;
  isActive: boolean;
  isDefault: boolean;
  rules: CommissionRulePayload[];
  vendorCount: number;
}

export type CommissionPlanType =
  | "Flat"
  | "Percentage"
  | "Tiered";

/** What the platform charges a seller for one sale. */
export interface CommissionQuote {
  /** The plan the answer came from. */
  planId: string;
  /** Its name, so a settlement statement can say which plan was applied. */
  planName: string;
  /** The commission rate as a percentage — `12.5000` means 12.5%. */
  ratePercent: number;
  /** A flat fee charged per unit sold, on top of the rate. Often zero. */
  fixedFee: number;
  /**
   * The category whose rule matched, or null when the plan's default was used. Recorded because
   * "why was I charged this" is the second question every seller asks.
   */
  matchedCategoryId: string | null;
}

export interface CommissionRulePayload {
  categoryId: string | null;
  minPrice: number | null;
  maxPrice: number | null;
  rate: number;
  fixedFee: number;
}

export interface ContentImageResponse {
  fileId: string;
  url: string | null;
  width: number | null;
  height: number | null;
  alt: string | null;
  variants: ContentImageVariantResponse[];
}

export interface ContentImageVariantResponse {
  name: string;
  width: number;
  url: string;
}

export interface CouponBody {
  code: string;
}

export interface CourierEventResponse {
  id: string;
  provider: string;
  providerEventId: string;
  eventType: string;
  awb: string | null;
  signatureValid: boolean;
  status: string;
  attempts: number;
  processError: string | null;
  shipmentId: string | null;
  occurredAt: string | null;
  receivedAt: string;
  processedAt: string | null;
}

export interface CourierRemittanceBody {
  awbs: string[];
  reference: string;
  amount: number | null;
  remittedAt: string | null;
}

export interface CreateCollectionBody {
  slug: string | null;
  name: string | null;
  description: string | null;
}

export interface CreateCommissionPlanBody {
  code: string;
  name: string;
  description: string | null;
  planType: CommissionPlanType;
  defaultRate: number;
  defaultFixedFee: number;
  rules: CommissionRulePayload[];
}

export interface CreateListingBody {
  variantId: string;
  vendorId: string | null;
  mrp: number | null;
  sellingPrice: number;
  vendorSku: string | null;
  handlingTimeHours: number;
  isCodAllowed: boolean;
  maxOrderQuantity: number | null;
}

export interface CreateManifestBody {
  shipmentIds: string[] | null;
  vendorId: string | null;
  pickupLocationId: string | null;
}

export interface CreateMenuBody {
  code: string | null;
  name: string | null;
  placement: string | null;
}

export interface CreatePageBody {
  slug: string | null;
  type: PageType;
  title: string | null;
  summary: string | null;
}

export interface CreatePayoutBatchBody {
  cycleIds: string[] | null;
}

export interface CreatePriceListBody {
  vendorId: string | null;
  code: string;
  name: string;
  type: PriceListType;
  priority: number;
  startsAt: string | null;
  endsAt: string | null;
}

export interface CreatePurchaseOrderBody {
  supplierId: string;
  warehouseId: string;
  expectedAt: string | null;
  notes: string | null;
  lines: PurchaseOrderLinePayload[];
}

export interface CreateRedirectBody {
  fromPath: string | null;
  toPath: string | null;
  statusCode: number;
  note: string | null;
}

export interface CreateRoleBody {
  code: string;
  name: string;
  scope: RoleScope;
  description: string;
  permissions: string[];
}

export interface CreateShipmentBody {
  lines: PackedLine[] | null;
  weight: number;
  dimensions: null | ParcelDimensions;
  courier: string | null;
  pickupLocationId: string | null;
  manualAwb: string | null;
  manualCourier: string | null;
}

export interface CreateStockTakeBody {
  warehouseId: string;
  scheduledFor: string | null;
  notes: string | null;
  stockItemIds: string[] | null;
}

export interface CreateStopWordBody {
  word: string | null;
}

export interface CreateSupplierBody {
  vendorId: string | null;
  code: string;
  name: string;
  contactName: string | null;
  email: string | null;
  phone: string | null;
  gstin: string | null;
  address: null | AddressPayload;
  paymentTermsDays: number;
}

export interface CreateSynonymBody {
  term: string | null;
  expansions: string[] | null;
  isBidirectional: boolean;
  note: string | null;
}

export interface CreateTaxRateBody {
  hsnCode: string;
  description: string | null;
  rate: number;
  cessRate: number;
  effectiveFrom: string;
  effectiveTo: string | null;
}

export interface CreateUserBody {
  email: string;
  mobile: string | null;
  userType: UserType;
  roleCodes: string[];
  vendorId: string | null;
}

export interface CreateVendorBody {
  legalName: string;
  displayName: string | null;
  businessType: VendorBusinessType;
  code: string | null;
  slug: string | null;
  pan: string | null;
  gstin: string | null;
  registeredAddress: null | AddressPayload;
  supportEmail: string | null;
  supportPhone: string | null;
}

export interface CreateWarehouseBody {
  vendorId: string | null;
  code: string;
  name: string;
  pincode: string;
  address: null | AddressPayload;
  priority: number;
}

export interface CreditNoteResponse {
  id: string;
  creditNoteNumber: string;
  returnId: string;
  orderId: string;
  subOrderId: string;
  vendorId: string | null;
  invoiceNumber: string | null;
  financialYear: string;
  taxableValue: number;
  cgst: number;
  sgst: number;
  igst: number;
  cess: number;
  total: number;
  currencyCode: string;
  fileId: string | null;
  issuedAt: string;
}

export interface CustomerProfileResponse {
  firstName: string | null;
  lastName: string | null;
  dateOfBirth: string | null;
  gender: string | null;
  gstin: string | null;
  marketingConsent: boolean;
  marketingConsentAt: string | null;
  referralCode: string;
}

export interface DeliveryCoverageResponse {
  enabled: boolean;
  allowedCities: string[];
  allowedPincodePrefixes: string[];
  allowedPincodes: string[];
  blockedPincodes: string[];
  message: string;
}

export interface DisableTwoFactorBody {
  password: string;
  code: string;
}

/** What one offer actually costs right now, and which rule decided it. */
export interface EffectivePrice {
  /** The offer. */
  listingId: string;
  /** Maximum retail price, from the catalogue. Statutory, and never below UnitPrice. */
  mrp: number;
  /** What the shopper pays per unit, inclusive of GST. */
  unitPrice: number;
  /** The quantity the price was resolved for; a tier may depend on it. */
  quantity: number;
  /** ISO 4217 code the amounts are in. */
  currencyCode: string;
  /** The price list that won, or null when the offer's own price stood. */
  priceListId: string | null;
  /** Its name, for the explanation. */
  priceListName: string | null;
  /** The quantity tier that applied, or 1 when no tier did. */
  minQuantity: number;
  /** How much is taken off the MRP, per unit. Zero when the offer sells at MRP. */
  unitSaving?: number;
  /** The saving as a whole-number percentage of MRP, for a "20% off" badge. */
  discountPercent?: number;
}

export interface EligiblePurchaseResponse {
  orderLineId: string;
  orderNumber: string;
  variantId: string;
  sku: string;
  name: string;
  deliveredAt: string;
}

export interface EnableTwoFactorBody {
  code: string;
}

export interface ExportReportBody {
  from: string | null;
  to: string | null;
  groupBy: string | null;
  vendorId: string | null;
}

export interface ExternalLoginResponse {
  id: string;
  provider: string;
  email: string | null;
  linkedAt: string;
  lastLoginAt: string;
}

export interface ExternalProviderResponse {
  provider: string;
  displayName: string;
}

export interface FacetGroup {
  key: string;
  label: string;
  values: FacetValue[];
}

export interface FacetValue {
  value: string;
  label: string;
  count: number;
  from?: number | null;
  to?: number | null;
}

export interface FeatureFlagResponse {
  key: string;
  enabled: boolean;
  description: string;
  rollout: RolloutModel;
}

export interface ForgotPasswordBody {
  email: string;
}

export interface GatewayEventResponse {
  event: GatewayEventSummaryResponse;
  payload: string;
}

export interface GatewayEventSummaryResponse {
  id: string;
  provider: string;
  providerEventId: string;
  eventType: string;
  signatureValid: boolean;
  status: string;
  attempts: number;
  processError: string | null;
  paymentId: string | null;
  receivedAt: string;
  processedAt: string | null;
  nextAttemptAt: string | null;
}

export interface GoodsReceiptLinePayload {
  purchaseOrderLineId: string;
  accepted: number;
  rejected: number;
  rejectionReason: string | null;
  batchCode: string | null;
  expiresOn: string | null;
}

export interface GoodsReceiptLineResponse {
  id: string;
  purchaseOrderLineId: string;
  stockItemId: string;
  quantityAccepted: number;
  quantityRejected: number;
  rejectionReason: string | null;
  batchCode: string | null;
  expiresOn: string | null;
}

export interface GoodsReceiptResponse {
  id: string;
  number: string;
  purchaseOrderId: string;
  warehouseId: string;
  status: GoodsReceiptStatus;
  receivedAt: string;
  receivedBy: string | null;
  notes: string | null;
  lines: GoodsReceiptLineResponse[];
  createdAt: string;
}

export type GoodsReceiptStatus =
  | "Draft"
  | "Posted";

export interface ImpersonateBody {
  reason: string;
}

export interface ImpersonationResponse {
  accessToken: string;
  expiresAt: string;
  sessionId: string;
  reason: string;
  user: AuthenticatedUserResponse;
}

export interface ImportErrorResponse {
  rowNumber: number;
  column: string | null;
  sku: string | null;
  message: string;
}

export interface ImportSettlementsBody {
  from: string | null;
  to: string | null;
}

export interface InvoiceDownloadResponse {
  invoiceId: string;
  invoiceNumber: string;
  url: string;
}

export interface InvoiceResponse {
  id: string;
  invoiceNumber: string;
  series: string;
  financialYear: string;
  taxableValue: number;
  cgst: number;
  sgst: number;
  igst: number;
  cess: number;
  total: number;
  currencyCode: string;
  status: string;
  fileId: string | null;
  irn: string | null;
  issuedAt: string;
}

export interface KycDocumentResponse {
  id: string;
  vendorId: string | null;
  documentType: KycDocumentType;
  fileId: string;
  numberMasked: string | null;
  status: KycVerificationStatus;
  rejectionReason: string | null;
  verifiedAt: string | null;
  submittedAt: string;
  downloadUrl: string | null;
}

export type KycDocumentType =
  | "Pan"
  | "Gstin"
  | "CancelledCheque"
  | "AddressProof"
  | "IdentityProof"
  | "IncorporationCertificate";

export interface KycSummaryResponse {
  documents: KycDocumentResponse[];
  required: KycDocumentType[];
  missing: KycDocumentType[];
}

export type KycVerificationStatus =
  | "Pending"
  | "Verified"
  | "Rejected";

export type LedgerDirection =
  | "Credit"
  | "Debit";

export interface LedgerEntryResponse {
  id: string;
  vendorId: string;
  entryType: LedgerEntryType;
  direction: LedgerDirection;
  amount: number;
  signedAmount: number;
  taxableValue: number;
  currencyCode: string;
  referenceType: string;
  referenceId: string | null;
  subOrderId: string | null;
  settlementCycleId: string | null;
  note: string | null;
  occurredAt: string;
}

export type LedgerEntryType =
  | "Sale"
  | "Commission"
  | "PlatformTax"
  | "PlatformFee"
  | "PaymentFee"
  | "ShippingFee"
  | "Refund"
  | "RefundCommissionReversal"
  | "Tcs"
  | "Tds"
  | "Adjustment"
  | "Payout";

export interface LedgerStatementResponse {
  vendorId: string;
  from: string;
  to: string;
  openingBalance: number;
  closingBalance: number;
  currentBalance: number;
  totals: LedgerTotalResponse[];
  currencyCode: string;
  entries: LedgerEntryResponse[];
}

export interface LedgerTotalResponse {
  entryType: LedgerEntryType;
  count: number;
  amount: number;
  signedAmount: number;
}

export interface ListingResponse {
  id: string;
  vendorId: string;
  variantId: string;
  productId: string;
  sku: string;
  productName: string;
  status: ListingStatus;
  statusReason: string | null;
  mrp: number;
  sellingPrice: number;
  vendorSku: string | null;
  handlingTimeHours: number;
  isCodAllowed: boolean;
  maxOrderQuantity: number | null;
  publishedAt: string | null;
  createdAt: string;
}

export type ListingStatus =
  | "Draft"
  | "Active"
  | "Inactive"
  | "Archived";

export interface ListingStatusBody {
  reason: string | null;
}

export interface LoginBody {
  email: string;
  password: string;
}

export interface ManifestResponse {
  id: string;
  reference: string;
  courier: string;
  vendorId: string | null;
  pickupLocationId: string | null;
  shipmentCount: number;
  totalWeightGrams: number;
  fileId: string | null;
  generatedAt: string;
}

export interface MeResponse {
  user: AuthenticatedUserResponse;
  profile: null | CustomerProfileResponse;
}

export interface MediaBody {
  media: MediaPayload[];
}

export interface MediaFileResponse {
  id: string;
  fileName: string;
  contentType: string;
  byteSize: number;
  width: number | null;
  height: number | null;
  visibility: string;
  status: string;
  scanState: string;
  ownerType: string | null;
  url: string | null;
  variants: MediaVariant[];
  uploadedAt: string;
}

export interface MediaLinkResponse {
  url: string;
  expiresAt: string;
}

export interface MediaPayload {
  fileId: string;
  kind: CatalogMediaKind;
  altText: string | null;
  position: number;
}

export interface MediaResponse {
  id: string;
  fileId: string;
  kind: CatalogMediaKind;
  altText: string | null;
  position: number;
  url: string | null;
}

/** One rendition of an image, as the storefront asks for it. */
export interface MediaVariant {
  /** The rendition key — `thumb`, `small`, `medium`, `large`. */
  name: string;
  /** Target width in CSS pixels; the height follows the source aspect ratio. */
  width: number;
  /** A ready-to-use URL, signed if the deployment signs imgproxy URLs. */
  url: string;
}

export interface MenuItemBody {
  id: string | null;
  parentId: string | null;
  label: string | null;
  linkType: MenuLinkType;
  targetId: string | null;
  url: string | null;
  isVisible: boolean;
  opensInNewTab: boolean;
  iconFileId: string | null;
  badge: string | null;
}

export interface MenuItemResponse {
  id: string;
  parentId: string | null;
  label: string;
  linkType: MenuLinkType;
  targetId: string | null;
  url: string | null;
  position: number;
  depth: number;
  isVisible: boolean;
  opensInNewTab: boolean;
  iconFileId: string | null;
  badge: string | null;
}

export type MenuLinkType =
  | "None"
  | "Page"
  | "Category"
  | "Collection"
  | "Url";

export interface MenuResponse {
  id: string;
  code: string;
  name: string;
  placement: string | null;
  isActive: boolean;
  items: MenuItemResponse[];
  createdAt: string;
  updatedAt: string | null;
}

export interface MenuSummaryResponse {
  id: string;
  code: string;
  name: string;
  placement: string | null;
  isActive: boolean;
  itemCount: number;
  updatedAt: string | null;
}

export interface MetaResponse {
  apiVersion: string;
  buildVersion: string;
  environment: string;
  serverTimeUtc: string;
}

export interface ModeratedAnswerResponse {
  id: string;
  body: string;
  authorType: string;
  authorName: string | null;
  vendorId: string | null;
  status: string;
  reportCount: number;
  createdAt: string;
}

export interface ModeratedQuestionResponse {
  id: string;
  productId: string;
  customerId: string;
  body: string;
  authorName: string | null;
  status: string;
  reportCount: number;
  answers: ModeratedAnswerResponse[];
  createdAt: string;
}

export interface ModeratedReviewResponse {
  id: string;
  productId: string;
  variantId: string;
  vendorId: string;
  customerId: string;
  orderLineId: string;
  rating: number;
  title: string | null;
  body: string | null;
  authorName: string | null;
  status: string;
  moderatedBy: string | null;
  moderatedAt: string | null;
  moderationNote: string | null;
  images: ReviewImageResponse[];
  helpfulCount: number;
  notHelpfulCount: number;
  reportCount: number;
  vendorReply: string | null;
  publishedAt: string | null;
  createdAt: string;
}

export interface ModerationBody {
  notes: string | null;
}

export interface ModerationResponse {
  id: string;
  productId: string;
  productName: string;
  vendorId: string | null;
  status: ModerationStatus;
  submittedBy: string | null;
  submittedAt: string;
  reviewerId: string | null;
  reviewedAt: string | null;
  notes: string | null;
}

export type ModerationStatus =
  | "Pending"
  | "Approved"
  | "Rejected"
  | "Withdrawn";

export interface MyPaymentResponse {
  orderId: string;
  orderNumber: string;
  status: string;
  method: string;
  amount: number;
  amountCaptured: number;
  amountRefunded: number;
  currencyCode: string;
  isPaid: boolean;
  canRetry: boolean;
  failureReason: string | null;
}

export type NdrAction =
  | "Pending"
  | "Reattempt"
  | "Rescheduled"
  | "AddressUpdated"
  | "ReturnToOrigin"
  | "Resolved";

export interface NdrActionBody {
  action: NdrAction;
  remark: string | null;
  rescheduledFor: string | null;
}

export interface NdrResponse {
  id: string;
  shipmentId: string;
  orderNumber: string;
  subOrderId: string;
  vendorId: string | null;
  customerId: string;
  awb: string | null;
  attemptNumber: number;
  reasonCode: string;
  reason: string | null;
  action: NdrAction;
  actionRemark: string | null;
  rescheduledFor: string | null;
  raisedAt: string;
  resolvedAt: string | null;
}

/** How a notification reaches a person. */
export type NotificationChannel =
  | "Email"
  | "Sms"
  | "WhatsApp"
  | "InApp";

export interface NotificationLogResponse {
  id: string;
  eventKey: string;
  channel: string;
  recipient: string;
  subject: string | null;
  status: string;
  suppression: string | null;
  attempts: number;
  error: string | null;
  createdAt: string;
  sentAt: string | null;
  nextAttemptAt: string | null;
}

/** What became of a queued message. */
export type NotificationStatus =
  | "Queued"
  | "Sending"
  | "Sent"
  | "Delivered"
  | "Failed"
  | "Bounced"
  | "Suppressed";

/** Why a message was never sent. */
export type NotificationSuppression =
  | "None"
  | "NoProvider"
  | "ChannelDisabled"
  | "OptedOut"
  | "NoRecipient"
  | "NoTemplate";

export interface OpenStockItemBody {
  listingId: string;
  warehouseId: string;
}

export interface OrderAddressResponse {
  recipientName: string;
  mobile: string;
  line1: string;
  line2: string | null;
  landmark: string | null;
  city: string;
  stateId: string;
  stateName: string | null;
  stateCode: string | null;
  pincode: string;
  gstin: string | null;
}

export interface OrderEventResponse {
  id: string;
  subOrderId: string | null;
  type: string;
  fromStatus: string | null;
  toStatus: string | null;
  actorType: string;
  message: string | null;
  isCustomerVisible: boolean;
  occurredAt: string;
}

export interface OrderLineResponse {
  id: string;
  listingId: string;
  variantId: string;
  productId: string;
  sku: string;
  name: string;
  imageFileId: string | null;
  hsnCode: string | null;
  quantity: number;
  quantityCancelled: number;
  quantityReturned: number;
  unitMrp: number;
  unitPrice: number;
  discountAmount: number;
  taxableValue: number;
  gstRate: number;
  cgst: number;
  sgst: number;
  igst: number;
  cess: number;
  lineTotal: number;
  status: string;
  isReturnable: boolean;
}

export interface OrderNoteBody {
  message: string;
  isCustomerVisible: boolean;
}

export interface OrderResponse {
  id: string;
  orderNumber: string;
  customerId: string;
  customerName: string;
  customerEmail: string | null;
  customerMobile: string | null;
  status: string;
  paymentMethod: string;
  paymentStatus: string;
  itemsTotal: number;
  discountTotal: number;
  shippingTotal: number;
  taxTotal: number;
  codFee: number;
  walletApplied: number;
  roundingAdjustment: number;
  grandTotal: number;
  amountPayable: number;
  cancelledTotal: number;
  netTotal: number;
  currencyCode: string;
  couponCode: string | null;
  channel: string;
  shippingAddress: OrderAddressResponse;
  billingAddress: OrderAddressResponse;
  placedAt: string;
  completedAt: string | null;
  cancelledAt: string | null;
  cancellationReason: string | null;
  notes: string | null;
  subOrders: SubOrderResponse[];
  timeline: OrderEventResponse[];
}

export interface OrderSummaryResponse {
  id: string;
  orderNumber: string;
  customerId: string;
  customerName: string;
  status: string;
  paymentMethod: string;
  paymentStatus: string;
  grandTotal: number;
  netTotal: number;
  currencyCode: string;
  itemCount: number;
  vendorCount: number;
  subOrderStatuses: string[];
  placedAt: string;
}

export type OtpChannel =
  | "Sms"
  | "Email"
  | "WhatsApp";

export interface OtpRequestBody {
  mobile: string;
}

export interface OtpRequestedResponse {
  expiresInSeconds: number;
  codeLength: number;
}

export interface OtpVerifyBody {
  mobile: string;
  code: string;
}

export interface PackBody {
  lines: PackedLine[] | null;
}

export interface PackedLine {
  orderLineId: string;
  quantity: number;
}

/** Pagination metadata. */
export interface PageInfo {
  /** How many items were requested for this page. */
  size: number;
  /**
   * Opaque token for the following page, or null when this is the last one. Keyset rather than
   * offset: a page that is fetched while rows are being inserted must not repeat or skip a row.
   */
  nextCursor: string | null;
  /**
   * Opaque token for the preceding page, or null. Null on forward-only endpoints; the UI keeps the
   * cursors it has already seen rather than asking the server to walk backwards.
   */
  prevCursor?: string | null;
  /**
   * Total matching rows, or null where counting is too expensive to do on every page. The UI must
   * not depend on it (docs/04-api-specification.md §1.1).
   */
  total?: number | null;
}

export interface PageResponse {
  id: string;
  slug: string;
  type: PageType;
  title: string;
  summary: string | null;
  status: PageStatus;
  publishedAt: string | null;
  scheduledAt: string | null;
  contentChangedAt: string | null;
  version: number;
  seo: SeoResponse;
  coverImage: null | ContentImageResponse;
  author: string | null;
  tags: string[];
  blocks: BlockResponse[];
  allowedTransitions: PageStatus[];
  createdAt: string;
  updatedAt: string | null;
}

export type PageStatus =
  | "Draft"
  | "InReview"
  | "Scheduled"
  | "Published"
  | "Unpublished"
  | "Archived";

export interface PageSummaryResponse {
  id: string;
  slug: string;
  type: PageType;
  title: string;
  status: PageStatus;
  publishedAt: string | null;
  scheduledAt: string | null;
  contentChangedAt: string | null;
  version: number;
  blockCount: number;
  updatedAt: string | null;
}

export type PageType =
  | "Home"
  | "Landing"
  | "Static"
  | "Legal"
  | "Blog";

export interface PageVersionResponse {
  version: number;
  title: string;
  seo: SeoResponse;
  blocks: BlockResponse[];
  note: string | null;
  restoredFrom: number | null;
  createdAt: string;
  createdBy: string | null;
}

export interface PageVersionSummaryResponse {
  version: number;
  title: string;
  note: string | null;
  restoredFrom: number | null;
  blockCount: number;
  createdAt: string;
  createdBy: string | null;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfAbuseReportResponse {
  /** The page of results, in the endpoint's declared order. */
  items: AbuseReportResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfAdminCartSummary {
  /** The page of results, in the endpoint's declared order. */
  items: AdminCartSummary[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfAdminUserResponse {
  /** The page of results, in the endpoint's declared order. */
  items: AdminUserResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfAuditLogResponse {
  /** The page of results, in the endpoint's declared order. */
  items: AuditLogResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfBannerResponse {
  /** The page of results, in the endpoint's declared order. */
  items: BannerResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfBlogCardResponse {
  /** The page of results, in the endpoint's declared order. */
  items: BlogCardResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfBrandResponse {
  /** The page of results, in the endpoint's declared order. */
  items: BrandResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfCatalogJobResponse {
  /** The page of results, in the endpoint's declared order. */
  items: CatalogJobResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfCodCollectionResponse {
  /** The page of results, in the endpoint's declared order. */
  items: CodCollectionResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfCollectionSummaryResponse {
  /** The page of results, in the endpoint's declared order. */
  items: CollectionSummaryResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfCommissionInvoiceResponse {
  /** The page of results, in the endpoint's declared order. */
  items: CommissionInvoiceResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfCourierEventResponse {
  /** The page of results, in the endpoint's declared order. */
  items: CourierEventResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfCreditNoteResponse {
  /** The page of results, in the endpoint's declared order. */
  items: CreditNoteResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfGatewayEventSummaryResponse {
  /** The page of results, in the endpoint's declared order. */
  items: GatewayEventSummaryResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfGoodsReceiptResponse {
  /** The page of results, in the endpoint's declared order. */
  items: GoodsReceiptResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfLedgerEntryResponse {
  /** The page of results, in the endpoint's declared order. */
  items: LedgerEntryResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfListingResponse {
  /** The page of results, in the endpoint's declared order. */
  items: ListingResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfManifestResponse {
  /** The page of results, in the endpoint's declared order. */
  items: ManifestResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfMediaFileResponse {
  /** The page of results, in the endpoint's declared order. */
  items: MediaFileResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfModeratedQuestionResponse {
  /** The page of results, in the endpoint's declared order. */
  items: ModeratedQuestionResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfModeratedReviewResponse {
  /** The page of results, in the endpoint's declared order. */
  items: ModeratedReviewResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfModerationResponse {
  /** The page of results, in the endpoint's declared order. */
  items: ModerationResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfNdrResponse {
  /** The page of results, in the endpoint's declared order. */
  items: NdrResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfNotificationLogResponse {
  /** The page of results, in the endpoint's declared order. */
  items: NotificationLogResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfOrderSummaryResponse {
  /** The page of results, in the endpoint's declared order. */
  items: OrderSummaryResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfPageSummaryResponse {
  /** The page of results, in the endpoint's declared order. */
  items: PageSummaryResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfPaymentSummaryResponse {
  /** The page of results, in the endpoint's declared order. */
  items: PaymentSummaryResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfPayoutBatchResponse {
  /** The page of results, in the endpoint's declared order. */
  items: PayoutBatchResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfPriceListItemResponse {
  /** The page of results, in the endpoint's declared order. */
  items: PriceListItemResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfPriceListResponse {
  /** The page of results, in the endpoint's declared order. */
  items: PriceListResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfProductCardResponse {
  /** The page of results, in the endpoint's declared order. */
  items: ProductCardResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfProductListItem {
  /** The page of results, in the endpoint's declared order. */
  items: ProductListItem[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfPromotionRedemptionResponse {
  /** The page of results, in the endpoint's declared order. */
  items: PromotionRedemptionResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfPromotionResponse {
  /** The page of results, in the endpoint's declared order. */
  items: PromotionResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfPurchaseOrderResponse {
  /** The page of results, in the endpoint's declared order. */
  items: PurchaseOrderResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfQuestionResponse {
  /** The page of results, in the endpoint's declared order. */
  items: QuestionResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfRedirectResponse {
  /** The page of results, in the endpoint's declared order. */
  items: RedirectResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfRefundResponse {
  /** The page of results, in the endpoint's declared order. */
  items: RefundResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfReportRunResponse {
  /** The page of results, in the endpoint's declared order. */
  items: ReportRunResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfReturnSummaryResponse {
  /** The page of results, in the endpoint's declared order. */
  items: ReturnSummaryResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfReviewResponse {
  /** The page of results, in the endpoint's declared order. */
  items: ReviewResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfSettlementCycleResponse {
  /** The page of results, in the endpoint's declared order. */
  items: SettlementCycleResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfSettlementEntryResponse {
  /** The page of results, in the endpoint's declared order. */
  items: SettlementEntryResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfSettlementResponse {
  /** The page of results, in the endpoint's declared order. */
  items: SettlementResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfShipmentSummaryResponse {
  /** The page of results, in the endpoint's declared order. */
  items: ShipmentSummaryResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfStockItemResponse {
  /** The page of results, in the endpoint's declared order. */
  items: StockItemResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfStockLedgerEntryResponse {
  /** The page of results, in the endpoint's declared order. */
  items: StockLedgerEntryResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfStockReservationResponse {
  /** The page of results, in the endpoint's declared order. */
  items: StockReservationResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfStockTakeResponse {
  /** The page of results, in the endpoint's declared order. */
  items: StockTakeResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfStopWordResponse {
  /** The page of results, in the endpoint's declared order. */
  items: StopWordResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfSubOrderResponse {
  /** The page of results, in the endpoint's declared order. */
  items: SubOrderResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfSupplierResponse {
  /** The page of results, in the endpoint's declared order. */
  items: SupplierResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfSynonymResponse {
  /** The page of results, in the endpoint's declared order. */
  items: SynonymResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfTaxRateResponse {
  /** The page of results, in the endpoint's declared order. */
  items: TaxRateResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfVendorListItem {
  /** The page of results, in the endpoint's declared order. */
  items: VendorListItem[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfWalletResponse {
  /** The page of results, in the endpoint's declared order. */
  items: WalletResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfWalletTransactionResponse {
  /** The page of results, in the endpoint's declared order. */
  items: WalletTransactionResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

/**
 * The collection envelope every list endpoint returns (docs/04-api-specification.md §1.1).
 * Defined once so no two endpoints invent their own shape and no client has to special-case one.
 */
export interface PagedResultOfWarehouseResponse {
  /** The page of results, in the endpoint's declared order. */
  items: WarehouseResponse[];
  /** Where this page sits and how to ask for the next one. */
  page: PageInfo;
}

export interface ParcelDimensions {
  lengthCm: number;
  widthCm: number;
  heightCm: number;
}

export interface PartyPayload {
  name: string | null;
  address: string | null;
  contact: string | null;
}

export interface PaymentInstructionResponse {
  orderId: string;
  orderNumber: string;
  provider: string;
  providerOrderId: string;
  publicKey: string;
  amount: number;
  currencyCode: string;
  customerName: string | null;
  customerEmail: string | null;
  customerMobile: string | null;
}

export interface PaymentMethodResponse {
  method: string;
  name: string;
  isAvailable: boolean;
  fee: number;
  reason: string | null;
}

export interface PaymentResponse {
  provider: string;
  providerOrderId: string;
  publicKey: string;
  amount: number;
  currencyCode: string;
}

export interface PaymentSummaryResponse {
  id: string;
  orderId: string;
  orderNumber: string;
  customerId: string;
  provider: string;
  method: string;
  status: string;
  amount: number;
  amountCaptured: number;
  amountRefunded: number;
  currencyCode: string;
  reference: string | null;
  openedAt: string;
  capturedAt: string | null;
}

export interface PayoutBatchResponse {
  id: string;
  reference: string;
  status: PayoutBatchStatus;
  totalAmount: number;
  settledAmount: number;
  vendorCount: number;
  currencyCode: string;
  provider: string | null;
  providerBatchId: string | null;
  requestedBy: string | null;
  requestedAt: string;
  approvedBy: string | null;
  approvedAt: string | null;
  processedAt: string | null;
  completedAt: string | null;
  cancelledReason: string | null;
  nextStatuses: string[];
  items: PayoutItemResponse[];
}

export type PayoutBatchStatus =
  | "Draft"
  | "Approved"
  | "Processing"
  | "Completed"
  | "PartiallyFailed"
  | "Failed"
  | "Cancelled";

export interface PayoutItemResponse {
  id: string;
  vendorId: string;
  vendorCode: string | null;
  vendorName: string | null;
  settlementCycleId: string | null;
  amount: number;
  currencyCode: string;
  status: PayoutItemStatus;
  destinationLast4: string | null;
  providerPayoutId: string | null;
  utr: string | null;
  failureReason: string | null;
  sentAt: string | null;
  settledAt: string | null;
}

export type PayoutItemStatus =
  | "Pending"
  | "Processing"
  | "Completed"
  | "Failed"
  | "Skipped";

export interface PermissionGroupResponse {
  group: string;
  permissions: PermissionResponse[];
}

export interface PermissionResponse {
  code: string;
  description: string;
}

export interface PickListLineResponse {
  shipmentId: string;
  orderNumber: string;
  subOrderNumber: string;
  sku: string;
  name: string;
  quantity: number;
  destinationPincode: string;
  dispatchDueAt: string | null;
  warehouseId: string | null;
  warehouseName: string | null;
}

export interface PickupLocationBody {
  label: string;
  contactName: string;
  contactPhone: string;
  line1: string;
  line2: string | null;
  landmark: string | null;
  city: string;
  stateId: string;
  pincode: string;
  isActive: boolean;
  makeDefault: boolean;
}

export interface PickupLocationResponse {
  id: string;
  vendorId: string | null;
  label: string;
  contactName: string;
  contactPhone: string;
  line1: string;
  line2: string | null;
  landmark: string | null;
  city: string;
  stateId: string;
  pincode: string;
  isDefault: boolean;
  isActive: boolean;
  courierLocationCode: string | null;
}

export interface PincodeRangeModel {
  from: string;
  to: string;
}

export interface PincodeResponse {
  pincode: string;
  city: string;
  district: string;
  stateCode: string;
  stateName: string;
  zone: string;
}

export interface PlaceOrderResponse {
  orderId: string;
  orderNumber: string;
  status: string;
  payment: null | PaymentResponse;
}

export interface PlatformRevenueResponse {
  from: string;
  to: string;
  grossMerchandiseValue: number;
  commission: number;
  platformFee: number;
  paymentFee: number;
  shippingFee: number;
  tax: number;
  refunds: number;
  chargesReversed: number;
  netRevenue: number;
  tcs: number;
  tds: number;
  paidOut: number;
  currencyCode: string;
}

export interface PreferenceResponse {
  category: string;
  description: string;
  email: boolean;
  sms: boolean;
  whatsApp: boolean;
  inApp: boolean;
}

export interface PreferencesResponse {
  categories: PreferenceResponse[];
  availableChannels: string[];
}

export interface PriceListItemPayload {
  listingId: string;
  price: number;
  minQuantity: number;
}

export interface PriceListItemResponse {
  id: string;
  priceListId: string;
  listingId: string;
  price: number;
  minQuantity: number;
}

export interface PriceListResponse {
  id: string;
  vendorId: string | null;
  code: string;
  name: string;
  type: PriceListType;
  currencyCode: string;
  priority: number;
  startsAt: string | null;
  endsAt: string | null;
  isActive: boolean;
  itemCount: number;
  createdAt: string;
}

export type PriceListType =
  | "Base"
  | "Sale"
  | "Scheduled";

export interface ProductBody {
  name: string;
  slug: string | null;
  categoryId: string;
  brandId: string | null;
  vendorId: string | null;
  shortDescription: string | null;
  description: string | null;
  hsnCode: string | null;
  gstRate: number;
  countryOfOrigin: string | null;
  manufacturer: null | PartyPayload;
  packer: null | PartyPayload;
  importer: null | PartyPayload;
  isReturnable: boolean;
  returnWindowDays: number | null;
  warranty: string | null;
  specifications: SpecificationPayload[] | null;
  seo: null | SeoPayload;
  attributes: AttributeValuePayload[] | null;
}

export interface ProductCardResponse {
  productId: string;
  variantId: string;
  listingId: string;
  name: string;
  slug: string;
  brandName: string | null;
  mrp: number;
  price: number;
  currencyCode: string;
  discountPercent: number;
  ratingAverage: number | null;
  ratingCount: number;
  image: null | ContentImageResponse;
  isPurchasable: boolean;
}

export interface ProductImportTemplateResponse {
  fileName: string;
  columns: string[];
}

export interface ProductListItem {
  id: string;
  name: string;
  slug: string;
  status: ProductStatus;
  categoryId: string;
  brandId: string | null;
  vendorId: string | null;
  variantCount: number;
  listingCount: number;
  primaryImageFileId: string | null;
  ratingAverage: number | null;
  ratingCount: number;
  publishedAt: string | null;
  createdAt: string;
}

export interface ProductResponse {
  id: string;
  name: string;
  slug: string;
  status: ProductStatus;
  categoryId: string;
  brandId: string | null;
  vendorId: string | null;
  shortDescription: string | null;
  description: string | null;
  hsnCode: string | null;
  gstRate: number;
  countryOfOrigin: string | null;
  manufacturer: PartyPayload;
  packer: PartyPayload;
  importer: PartyPayload;
  isReturnable: boolean;
  returnWindowDays: number | null;
  warranty: string | null;
  specifications: SpecificationPayload[];
  seo: SeoPayload;
  attributes: AttributeValueResponse[];
  media: MediaResponse[];
  variants: VariantResponse[];
  ratingAverage: number | null;
  ratingCount: number;
  complianceGaps: string[];
  publishedAt: string | null;
  createdAt: string;
}

export interface ProductSearchItem {
  variantId: string;
  productId: string;
  listingId: string;
  slug: string;
  sku: string;
  name: string;
  brandId: string | null;
  brandName: string | null;
  categoryId: string;
  categoryName: string;
  vendorId: string;
  vendorName: string;
  mrp: number;
  price: number;
  currencyCode: string;
  discountPercent: number;
  ratingAverage: number | null;
  ratingCount: number;
  isAvailable: boolean;
  isCodAllowed: boolean;
  offerCount: number;
  imageFileId: string | null;
}

export interface ProductSearchResponse {
  items: ProductSearchItem[];
  total: number;
  nextCursor: string | null;
  size: number;
  facets: FacetGroup[];
  query: string | null;
  corrected: boolean;
  queryToken: string | null;
}

export type ProductStatus =
  | "Draft"
  | "PendingApproval"
  | "Active"
  | "Inactive"
  | "Archived";

export type PromotionApplication =
  | "Line"
  | "Order"
  | "Shipping";

export interface PromotionBody {
  code: string | null;
  name: string;
  description: string | null;
  type: PromotionType;
  appliesTo: PromotionApplication;
  value: number;
  scope: null | PromotionScopePayload;
  conditions: null | PromotionConditionsPayload;
  stacking: StackingMode;
  priority: number;
  startsAt: string;
  endsAt: string | null;
  usageLimitTotal: number | null;
  usageLimitPerCustomer: number | null;
  minOrderValue: number;
  maxDiscount: number | null;
}

export interface PromotionConditionsPayload {
  minQuantity: number | null;
  firstOrderOnly: boolean | null;
  paymentMethods: QuotePaymentMethod[] | null;
  maxQuantityPerOrder: number | null;
  buyQuantity: number | null;
  getQuantity: number | null;
  getDiscountPercent: number | null;
  bundleListingIds: string[] | null;
  bundlePrice: number | null;
  tiers: PromotionTierPayload[] | null;
  tiersArePercentage: boolean | null;
}

export interface PromotionRedemptionResponse {
  id: string;
  promotionId: string;
  code: string | null;
  customerId: string | null;
  orderId: string;
  discountAmount: number;
  status: string;
  redeemedAt: string;
  reversedAt: string | null;
}

export interface PromotionResponse {
  id: string;
  code: string | null;
  name: string;
  description: string | null;
  type: PromotionType;
  appliesTo: PromotionApplication;
  value: number;
  scope: PromotionScopePayload;
  conditions: PromotionConditionsPayload;
  stacking: StackingMode;
  priority: number;
  startsAt: string;
  endsAt: string | null;
  usageLimitTotal: number | null;
  usageLimitPerCustomer: number | null;
  usageCount: number;
  minOrderValue: number;
  maxDiscount: number | null;
  isActive: boolean;
  createdAt: string;
}

export interface PromotionScopePayload {
  categoryIds: string[] | null;
  brandIds: string[] | null;
  vendorIds: string[] | null;
  listingIds: string[] | null;
  excludedListingIds: string[] | null;
  segments: string[] | null;
}

export interface PromotionTierPayload {
  minAmount: number;
  value: number;
}

export type PromotionType =
  | "Percentage"
  | "Fixed"
  | "FreeShipping"
  | "Bogo"
  | "Bundle"
  | "Tiered";

export interface PurchaseOrderLinePayload {
  listingId: string;
  description: string | null;
  quantityOrdered: number;
  unitCost: number;
  taxRate: number;
}

export interface PurchaseOrderLineResponse {
  id: string;
  listingId: string;
  sku: string;
  description: string | null;
  quantityOrdered: number;
  quantityReceived: number;
  unitCost: number;
  taxRate: number;
  lineTotal: number;
}

export interface PurchaseOrderResponse {
  id: string;
  number: string;
  supplierId: string;
  warehouseId: string;
  vendorId: string | null;
  status: PurchaseOrderStatus;
  expectedAt: string | null;
  submittedAt: string | null;
  subtotal: number;
  taxTotal: number;
  total: number;
  notes: string | null;
  lines: PurchaseOrderLineResponse[];
  createdAt: string;
}

export type PurchaseOrderStatus =
  | "Draft"
  | "Submitted"
  | "PartiallyReceived"
  | "Received"
  | "Cancelled";

export interface QcBody {
  result: string;
  disposition: null | ReturnDisposition;
  notes: string | null;
  lines: QcLineRequest[] | null;
}

export interface QcLineRequest {
  returnLineId: string;
  quantityAccepted: number;
  disposition: null | ReturnDisposition;
  note: string | null;
}

export interface QuestionBody {
  body: string | null;
}

export interface QuestionResponse {
  id: string;
  productId: string;
  body: string;
  authorName: string | null;
  answerCount: number;
  answers: AnswerResponse[];
  publishedAt: string | null;
}

/** One message that the request produced. */
export interface QueuedNotification {
  /** Its id in the delivery log. */
  messageId: string;
  /** The channel it was queued on. */
  channel: NotificationChannel;
  /** Queued, or already suppressed. */
  status: NotificationStatus;
  /** Why it was suppressed, when it was. */
  suppression: NotificationSuppression;
}

/**
 * One line of a quote, itemised the way an Indian tax invoice has to be
 * (docs/03-database-design.md §4.6).
 */
export interface QuoteLine {
  /** The caller's identifier for the line. */
  lineId: string;
  /** The offer. */
  listingId: string;
  /** The seller, so a caller can group into sub-orders without asking Catalog. */
  vendorId: string;
  /** The stock-keeping unit, frozen onto the line. */
  sku: string;
  /** The offer's display name, frozen onto the line. */
  name: string;
  /** Units. */
  quantity: number;
  /** Maximum retail price per unit. */
  unitMrp: number;
  /** Selling price per unit, inclusive of GST, before any discount. */
  unitPrice: number;
  /** Unit price multiplied by quantity. */
  gross: number;
  /** Discount from promotions that applied to this line specifically. */
  lineDiscount: number;
  /** This line's pro-rata share of an order-level discount. */
  orderDiscountAllocated: number;
  /** The gross less both discounts, with the tax taken back out of it. */
  taxableValue: number;
  /** The HSN the rate was resolved from. */
  hsnCode: string | null;
  /** The GST percentage applied. */
  gstRate: number;
  /** The cess percentage applied. */
  cessRate: number;
  /** Central GST. Zero on an inter-state supply. */
  cgst: number;
  /** State GST. Zero on an inter-state supply. */
  sgst: number;
  /** Integrated GST. Zero on an intra-state supply. */
  igst: number;
  /** Compensation cess. */
  cess: number;
  /** What the shopper pays for this line: gross less both discounts. */
  lineTotal: number;
  /** The promotions that touched this line, in the order they applied. */
  appliedPromotions: string[];
}

export interface QuoteLinePayload {
  lineId: string | null;
  listingId: string;
  quantity: number;
}

/** How a basket is being paid for, because some promotions and fees depend on it. */
export type QuotePaymentMethod =
  | "Prepaid"
  | "CashOnDelivery";

/** A promotion the engine considered, and what it decided. */
export interface QuotePromotion {
  /** The promotion. */
  promotionId: string;
  /** Its coupon code, or null for an automatic rule. */
  code: string | null;
  /** Its name, for the cart's "you saved" line. */
  name: string;
  /** Its type, verbatim from the promotion. */
  type: string;
  /** Whether it actually reduced anything. */
  applied: boolean;
  /** How much it took off. Zero when it did not apply. */
  discountAmount: number;
  /**
   * Why it did not apply, or null when it did. This is what a cart shows a shopper who typed a code
   * that turned out to be for somebody else's first order.
   */
  reason: string | null;
}

/**
 * A fully itemised, auditable price for a basket. Snapshotted by Orders and re-rendered on the
 * invoice; nothing downstream recomputes any of it.
 */
export interface QuoteResult {
  /** The lines, itemised. */
  lines: QuoteLine[];
  /** The same lines grouped by seller, which is how they will be ordered. */
  vendorGroups: QuoteVendorGroup[];
  /** Every promotion considered, applied or not, with the reason. */
  promotions: QuotePromotion[];
  /** ISO 4217 code every amount is in. */
  currencyCode: string;
  /**
   * Whether the <em>store's own</em> supply - shipping and the COD fee - is intra-state. A line's
   * split is decided against its own seller's registration and may differ from this, which is why
   * the CGST/SGST/IGST figures are on the line rather than inferred from this flag.
   */
  isIntraState: boolean;
  /** The GST state code every split was decided against. */
  placeOfSupplyStateCode: string | null;
  /** The lines' gross, before any discount. */
  subtotal: number;
  /** Discount attributed to specific lines. */
  lineDiscountTotal: number;
  /** Discount applied to the basket as a whole. */
  orderDiscountTotal: number;
  /** The two above, together. */
  discountTotal: number;
  /** What the tax was computed on, after discount. */
  taxableValue: number;
  /** Central GST across the basket. */
  cgstTotal: number;
  /** State GST across the basket. */
  sgstTotal: number;
  /** Integrated GST across the basket. */
  igstTotal: number;
  /** Compensation cess across the basket. */
  cessTotal: number;
  /** Every tax figure above, together. */
  taxTotal: number;
  /** Shipping charged, inclusive of its tax, after any free-shipping promotion. */
  shipping: number;
  /** Shipping taken off by a promotion. */
  shippingDiscount: number;
  /** The tax inside the shipping figure. */
  shippingTax: number;
  /** The cash-on-delivery handling fee, inclusive of its tax. Zero when prepaid. */
  codFee: number;
  /** Store credit actually redeemed. Never more than the balance or the amount due. */
  walletApplied: number;
  /**
   * What was added or taken off to land the total on a whole rupee. Reported rather than hidden,
   * because it is a line on the invoice.
   */
  roundingAdjustment: number;
  /** What the shopper pays. */
  grandTotal: number;
  /** The grand total less store credit: what the gateway is asked for. */
  amountPayable: number;
  /**
   * Why the code the shopper typed did nothing, or null when it worked or none was typed. It is on
   * the quote rather than raised as an error because a basket with a bad coupon on it still has a
   * price, and refusing to quote one would leave the cart with nothing to render.
   */
  couponRejection: string | null;
}

/** One seller's share of a basket, which is the sub-order Orders will create. */
export interface QuoteVendorGroup {
  /** The seller. */
  vendorId: string;
  /** The caller's line identifiers that belong to them. */
  lineIds: string[];
  /** Their lines' gross. */
  subtotal: number;
  /** Their lines' discount, line-level and allocated. */
  discount: number;
  /** Their lines' taxable value. */
  taxableValue: number;
  /** Their lines' CGST + SGST + IGST + cess. */
  taxTotal: number;
  /** Their share of shipping, inclusive of its tax. */
  shipping: number;
  /** The tax inside the shipping figure. */
  shippingTax: number;
  /** What the shopper pays for this seller's part. */
  total: number;
}

export interface RaiseReturnBody {
  subOrderId: string;
  type: string | null;
  reasonCode: string;
  reasonNote: string | null;
  lines: ReturnLineRequest[] | null;
  evidenceFileIds: string[] | null;
  refundMode: string | null;
}

export interface RateBody {
  zoneId: string;
  method: string | null;
  vendorId: string | null;
  terms: RateTerms;
  isActive?: boolean;
}

export interface RateTerms {
  minWeightGrams: number;
  maxWeightGrams: number;
  minOrderValue: number;
  maxOrderValue: number | null;
  baseRate: number;
  perKgRate: number;
  freeAbove: number | null;
  codFee: number;
  isCodAllowed: boolean;
  etaMinDays: number;
  etaMaxDays: number;
}

export interface RatingSummaryResponse {
  productId: string;
  average: number | null;
  count: number;
  oneStar: number;
  twoStar: number;
  threeStar: number;
  fourStar: number;
  fiveStar: number;
}

export interface RebuildIndexBody {
  afterVariantId: string | null;
  maxVariants: number | null;
  variantIds: string[] | null;
}

export interface ReceivePurchaseOrderBody {
  notes: string | null;
  lines: GoodsReceiptLinePayload[];
}

export interface ReceiveReturnBody {
  note: string | null;
}

export interface ReconciliationSummary {
  examined: number;
  recovered: number;
  failed: number;
  mismatched: number;
}

export interface RecordTrackingBody {
  status: string;
  remark: string | null;
  occurredAt: string | null;
}

export interface RedirectResolutionResponse {
  statusCode: number;
  location: string | null;
}

export interface RedirectResponse {
  id: string;
  fromPath: string;
  toPath: string | null;
  statusCode: number;
  hitCount: number;
  lastHitAt: string | null;
  isActive: boolean;
  note: string | null;
  createdAt: string;
}

export interface RefundBody {
  amount: number;
  reason: string;
  subOrderId: string | null;
  speed: string | null;
}

export interface RefundResponse {
  id: string;
  paymentId: string;
  orderId: string;
  subOrderId: string | null;
  returnId: string | null;
  amount: number;
  currencyCode: string;
  reason: string;
  status: string;
  speed: string;
  requiresApproval: boolean;
  reference: string | null;
  initiatedBy: string | null;
  initiatedAt: string;
  approvedBy: string | null;
  approvedAt: string | null;
  completedAt: string | null;
  failureReason: string | null;
}

export interface RefundReturnBody {
  mode: string | null;
  amount: number | null;
}

export interface RegisterBody {
  email: string;
  password: string;
  mobile: string | null;
  marketingConsent: boolean;
}

export interface ReindexResponse {
  variantsWalked: number;
  rowsWritten: number;
  rowsRetired: number;
  isComplete: boolean;
  resumeAfterVariantId: string | null;
}

export interface RejectRefundBody {
  reason: string | null;
}

export interface RejectReturnBody {
  reason: string | null;
}

export interface RemitCashBody {
  ids: string[];
  reference: string;
  amount: number | null;
  remittedAt: string | null;
}

export interface ReplaceReturnBody {
  replacementOrderId: string | null;
  note: string | null;
}

export interface ReplyBody {
  reply: string | null;
}

export interface ReportBody {
  target: string | null;
  targetId: string;
  reason: string | null;
  note: string | null;
}

export interface ReportColumn {
  key: string;
  label: string;
  kind: ReportColumnKind;
}

export type ReportColumnKind =
  | "Text"
  | "Count"
  | "Money"
  | "Percent"
  | "Date";

export interface ReportDefinition {
  key: string;
  name: string;
  description: string;
  group: string;
  columns: ReportColumn[];
  groupBy: string[];
  isVendorScoped: boolean;
}

export interface ReportDownloadResponse {
  url: string;
  expiresAt: string;
  fileName: string;
}

export interface ReportResult {
  key: string;
  name: string;
  columns: ReportColumn[];
  from: string;
  to: string;
  groupBy: string | null;
  currencyCode: string;
  rows: Record<string, unknown>[];
  totals: Record<string, unknown> | null;
  truncated: boolean;
}

export interface ReportRunResponse {
  id: string;
  scheduleId: string | null;
  reportKey: string;
  periodStart: string;
  periodEnd: string;
  status: string;
  format: string;
  rowCount: number | null;
  byteSize: number | null;
  error: string | null;
  startedAt: string;
  completedAt: string | null;
}

export interface ReportScheduleResponse {
  id: string;
  reportKey: string;
  reportName: string | null;
  name: string;
  frequency: string;
  hourUtc: number;
  dayOfWeek: number | null;
  dayOfMonth: number | null;
  recipients: string[];
  format: string;
  isActive: boolean;
  lastRunAt: string | null;
  nextRunAt: string;
}

export type ReservationStatus =
  | "Held"
  | "Committed"
  | "Released"
  | "Expired";

export interface ResetPasswordBody {
  email: string;
  token: string;
  newPassword: string;
}

export interface ResolveReportBody {
  uphold: boolean;
  resolution: string | null;
}

export type ReturnDisposition =
  | "Pending"
  | "Restock"
  | "Scrap"
  | "Quarantine";

export interface ReturnEligibilityResponse {
  subOrderId: string;
  subOrderNumber: string;
  orderNumber: string;
  isEligible: boolean;
  windowClosesAt: string | null;
  windowSource: string;
  reason: string | null;
  currencyCode: string;
  lines: ReturnableLineResponse[];
  reasons: ReturnReasonOption[];
}

export interface ReturnLineRequest {
  orderLineId: string;
  quantity: number;
}

export interface ReturnLineResponse {
  id: string;
  orderLineId: string;
  listingId: string;
  sku: string;
  name: string;
  imageFileId: string | null;
  quantity: number;
  quantityAccepted: number;
  refundAmount: number;
  acceptedRefund: number;
  disposition: ReturnDisposition;
  qcNote: string | null;
}

export interface ReturnPolicyPayload {
  acceptsReturns: boolean;
  windowDays: number;
  acceptsExchanges: boolean;
  customerPaysReturnShipping: boolean;
  notes: string | null;
}

export interface ReturnReasonBody {
  code: string | null;
  label: string;
  description: string | null;
  sortOrder: number;
  isActive?: boolean;
  requiresEvidence?: boolean;
  isPickupRequired?: boolean;
  requiresQc?: boolean;
  isAutoApproved?: boolean;
  shippingPayer?: string | null;
  isVendorFault?: boolean;
  allowsReplacement?: boolean;
}

export interface ReturnReasonOption {
  code: string;
  label: string;
  description: string | null;
  requiresEvidence: boolean;
  isPickupRequired: boolean;
  allowsReplacement: boolean;
}

export interface ReturnReasonResponse {
  id: string;
  code: string;
  label: string;
  description: string | null;
  isActive: boolean;
  sortOrder: number;
  requiresEvidence: boolean;
  isPickupRequired: boolean;
  requiresQc: boolean;
  isAutoApproved: boolean;
  shippingPayer: string;
  isVendorFault: boolean;
  allowsReplacement: boolean;
}

export interface ReturnResponse {
  id: string;
  returnNumber: string;
  orderId: string;
  orderNumber: string;
  subOrderId: string;
  subOrderNumber: string;
  vendorId: string | null;
  type: string;
  status: string;
  reasonCode: string;
  reasonNote: string | null;
  evidenceFileIds: string[];
  estimatedRefund: number;
  approvedAmount: number;
  refundAmount: number;
  returnShippingFee: number;
  shippingRefundAmount: number;
  refundMode: string | null;
  currencyCode: string;
  isPickupRequired: boolean;
  pickupAwb: string | null;
  pickupScheduledFor: string | null;
  qcPassed: boolean | null;
  qcNotes: string | null;
  rejectedReason: string | null;
  customerId: string;
  creditNoteId: string | null;
  replacementOrderId: string | null;
  requestedAt: string;
  approvedAt: string | null;
  receivedAt: string | null;
  refundedAt: string | null;
  closedAt: string | null;
  lines: ReturnLineResponse[];
  nextStatuses: string[];
}

export interface ReturnSummaryResponse {
  id: string;
  returnNumber: string;
  orderNumber: string;
  subOrderNumber: string;
  vendorId: string | null;
  type: string;
  status: string;
  reasonCode: string;
  estimatedRefund: number;
  refundAmount: number;
  currencyCode: string;
  units: number;
  requestedAt: string;
}

export interface ReturnableLineResponse {
  orderLineId: string;
  sku: string;
  name: string;
  imageFileId: string | null;
  quantityReturnable: number;
  unitPrice: number;
  estimatedRefund: number;
  isReturnable: boolean;
  reason: string | null;
}

export interface ReviewBody {
  orderLineId: string;
  rating: number;
  title: string | null;
  body: string | null;
  images: ReviewImageInput[] | null;
}

export interface ReviewEligibilityResponse {
  canReview: boolean;
  eligible: EligiblePurchaseResponse[];
}

export interface ReviewImageInput {
  fileId: string;
  caption: string | null;
}

export interface ReviewImageResponse {
  fileId: string;
  url: string | null;
  caption: string | null;
}

export interface ReviewResponse {
  id: string;
  productId: string;
  variantId: string;
  rating: number;
  title: string | null;
  body: string | null;
  authorName: string | null;
  isVerifiedPurchase: boolean;
  images: ReviewImageResponse[];
  helpfulCount: number;
  notHelpfulCount: number;
  vendorReply: string | null;
  vendorRepliedAt: string | null;
  publishedAt: string | null;
}

export interface RevokedSessionsResponse {
  revoked: number;
}

export interface RoleResponse {
  id: string;
  code: string;
  name: string;
  scope: RoleScope;
  description: string;
  isSystem: boolean;
  permissions: string[];
}

export type RoleScope =
  | "Platform"
  | "Vendor"
  | "Customer";

export interface RolloutModel {
  percentage: number;
  userIds: string[];
  segments: string[];
}

export interface RuleConditionBody {
  field: RuleField;
  key: string | null;
  operator: RuleOperator;
  values: string[] | null;
}

export interface RuleConditionResponse {
  field: RuleField;
  key: string | null;
  operator: RuleOperator;
  values: string[];
}

export type RuleField =
  | "Category"
  | "Brand"
  | "Vendor"
  | "Price"
  | "DiscountPercent"
  | "Rating"
  | "PublishedWithinDays"
  | "Attribute";

export type RuleOperator =
  | "In"
  | "NotIn"
  | "GreaterThan"
  | "AtLeast"
  | "LessThan"
  | "AtMost";

export interface SaveItemBody {
  variantId: string;
  wishlistId: string | null;
  note: string | null;
  priority: number;
}

export interface ScheduleBody {
  reportKey: string | null;
  name: string | null;
  frequency: string | null;
  hourUtc: number;
  dayOfWeek: number | null;
  dayOfMonth: number | null;
  recipients: string[] | null;
  isActive: boolean;
}

export interface SchedulePickupBody {
  pickupAt: string | null;
}

export interface SearchClickBody {
  queryToken: string | null;
  position: number;
  variantId: string;
}

export interface SearchIndexStatus {
  engine: string;
  isExternalEngineEnabled: boolean;
  documentCount: number;
  activeCount: number;
  availableCount: number;
  staleCount: number;
  oldestIndexedAt: string | null;
  synonymCount: number;
  stopWordCount: number;
}

export interface SearchQueryReportRow {
  query: string;
  normalisedQuery: string;
  count: number;
  zeroResultCount: number;
  averageResultCount: number;
  clickThroughRate: number;
  averageClickPosition: number | null;
  lastSearchedAt: string;
}

export interface SearchSuggestion {
  kind: string;
  text: string;
  slug: string | null;
  id: string | null;
  imageFileId: string | null;
  price: number | null;
}

export interface SendTestRequest {
  channel: string;
  to: string;
}

export interface SeoBody {
  metaTitle: string | null;
  metaDescription: string | null;
  metaKeywords: string | null;
  canonicalUrl: string | null;
  ogTitle: string | null;
  ogDescription: string | null;
  ogImageFileId: string | null;
  ogType: string | null;
  noIndex: boolean;
  noFollow: boolean;
  sitemapPriority: number | null;
}

export interface SeoConfigResponse {
  canonicalBaseUrl: string;
  allowIndexing: boolean;
  titleTemplate: string;
  defaultMetaDescription: string;
  twitterCardType: string;
  robotsUrl: string;
  sitemapUrl: string;
}

export interface SeoPayload {
  metaTitle: string | null;
  metaDescription: string | null;
  metaKeywords: string | null;
  canonicalUrl: string | null;
  noIndex: boolean;
}

export interface SeoResponse {
  metaTitle: string | null;
  metaDescription: string | null;
  metaKeywords: string | null;
  canonicalUrl: string | null;
  ogTitle: string | null;
  ogDescription: string | null;
  ogImage: null | ContentImageResponse;
  ogType: string | null;
  noIndex: boolean;
  noFollow: boolean;
  sitemapPriority: number | null;
}

export type SerialStatus =
  | "InStock"
  | "Reserved"
  | "Sold"
  | "Returned"
  | "Damaged";

export interface ServiceabilityResponse {
  pincode: string;
  deliverable: boolean;
  covered: boolean;
  isServiceable: boolean;
  prepaidOk: boolean;
  codOk: boolean;
  etaDays: number | null;
  courier: string | null;
  city: string | null;
  state: string | null;
  reason: string | null;
  message: string | null;
  checkedAt: string | null;
}

export interface ServiceableRegionPayload {
  scope: ServiceableRegionScope;
  stateId: string | null;
  pincodePrefix: string | null;
  isExcluded: boolean;
}

export interface ServiceableRegionResponse {
  id: string;
  scope: ServiceableRegionScope;
  stateId: string | null;
  pincodePrefix: string | null;
  isExcluded: boolean;
}

export type ServiceableRegionScope =
  | "State"
  | "PincodePrefix";

export interface ServiceableRegionsResponse {
  servesAllIndia: boolean;
  regions: ServiceableRegionResponse[];
}

export interface SessionResponse {
  id: string;
  device: string | null;
  ipAddress: string | null;
  startedAt: string;
  lastSeenAt: string;
  isCurrent: boolean;
}

export interface SetBannerActiveBody {
  isActive: boolean;
}

export interface SetCollectionItemsBody {
  items: CollectionItemBody[] | null;
}

export interface SetCollectionRuleBody {
  rule: null | CollectionRuleBody;
}

export interface SetServiceableRegionsBody {
  servesAllIndia: boolean;
  regions: ServiceableRegionPayload[];
}

export interface SetStopWordActiveBody {
  isActive: boolean;
}

export interface SetTemporaryPasswordBody {
  temporaryPassword: string;
}

export interface SetUserRolesBody {
  roleCodes: string[];
}

export interface SetUserStatusBody {
  status: UserStatus;
}

export interface SettingsFieldSchema {
  name: string;
  kind: string;
  isRequired: boolean;
  isList: boolean;
  minimum: number | null;
  maximum: number | null;
  maxLength: number | null;
  pattern: string | null;
  choices: string[] | null;
}

export interface SettingsSchemaResponse {
  sections: SettingsSectionSchema[];
}

export interface SettingsSectionResponse {
  key: string;
  isPublic: boolean;
  value: unknown;
}

export interface SettingsSectionSchema {
  key: string;
  isPublic: boolean;
  fields: SettingsFieldSchema[];
}

export interface SettlementCycleResponse {
  id: string;
  vendorId: string;
  vendorCode: string | null;
  vendorName: string | null;
  periodStart: string;
  periodEnd: string;
  status: SettlementCycleStatus;
  openingBalance: number;
  grossSales: number;
  taxableSales: number;
  totalCommission: number;
  totalFees: number;
  totalRefunds: number;
  totalAdjustments: number;
  totalPayouts: number;
  tcs: number;
  tds: number;
  netPayable: number;
  currencyCode: string;
  entryCount: number;
  closedAt: string | null;
  paidAt: string | null;
  payoutBatchId: string | null;
}

export type SettlementCycleStatus =
  | "Open"
  | "Closed"
  | "Paid";

export interface SettlementEntryResponse {
  id: string;
  entryType: string;
  reference: string | null;
  paymentId: string | null;
  amount: number;
  fee: number;
  tax: number;
  credit: number;
  debit: number;
  matchStatus: string;
  mismatchReason: string | null;
  occurredAt: string | null;
}

export interface SettlementIngestionSummary {
  imported: number;
  skipped: number;
  entries: number;
  matched: number;
  mismatched: number;
}

export interface SettlementResponse {
  id: string;
  provider: string;
  providerSettlementId: string;
  amount: number;
  fees: number;
  tax: number;
  currencyCode: string;
  utr: string | null;
  status: string;
  settledAt: string | null;
  importedAt: string;
  entryCount: number;
  matchedCount: number;
  mismatchCount: number;
}

export interface ShareBody {
  share: boolean;
}

export interface SharedWishlistResponse {
  name: string;
  itemCount: number;
  items: WishlistItemResponse[];
}

export interface ShipmentLabelResponse {
  shipmentId: string;
  url: string;
  expiresAt: string;
  fileName: string;
}

export interface ShipmentLineResponse {
  orderLineId: string;
  sku: string;
  name: string;
  quantity: number;
  unitWeightGrams: number;
}

export interface ShipmentResponse {
  id: string;
  orderId: string;
  orderNumber: string;
  subOrderId: string;
  subOrderNumber: string;
  vendorId: string | null;
  status: string;
  statusReason: string | null;
  provider: string;
  courier: string | null;
  serviceName: string | null;
  awb: string | null;
  trackingUrl: string | null;
  labelFileId: string | null;
  manifestId: string | null;
  weightGrams: number;
  chargedWeightGrams: number | null;
  lengthCm: number;
  widthCm: number;
  heightCm: number;
  pickupPincode: string | null;
  destinationPincode: string;
  codAmount: number | null;
  declaredValue: number;
  freightCharged: number;
  freightCost: number | null;
  currencyCode: string;
  isReturn: boolean;
  pickupScheduledAt: string | null;
  pickedUpAt: string | null;
  expectedDeliveryAt: string | null;
  deliveredAt: string | null;
  lastTrackedAt: string | null;
  deliveryAttempts: number;
  lines: ShipmentLineResponse[];
  tracking: TrackingEventResponse[];
}

export interface ShipmentSummaryResponse {
  id: string;
  orderNumber: string;
  subOrderNumber: string;
  vendorId: string | null;
  status: string;
  courier: string | null;
  awb: string | null;
  destinationPincode: string;
  weightGrams: number;
  codAmount: number | null;
  currencyCode: string;
  expectedDeliveryAt: string | null;
  createdAt: string;
}

export interface ShippingOptionResponse {
  code: string;
  name: string;
  carrier: string | null;
  amount: number;
  dispatchSlaHours: number;
  promisedMinDays: number;
  promisedMaxDays: number;
  isCodAvailable: boolean;
}

export interface ShippingRateResponse {
  id: string;
  zoneId: string;
  vendorId: string | null;
  method: string;
  minWeightGrams: number;
  maxWeightGrams: number;
  minOrderValue: number;
  maxOrderValue: number | null;
  baseRate: number;
  perKgRate: number;
  freeAbove: number | null;
  codFee: number;
  isCodAllowed: boolean;
  currencyCode: string;
  etaMinDays: number;
  etaMaxDays: number;
  isActive: boolean;
}

export interface ShippingZoneResponse {
  id: string;
  code: string;
  name: string;
  states: string[];
  pincodeRanges: PincodeRangeModel[];
  priority: number;
  isActive: boolean;
  isCatchAll: boolean;
}

export interface SignInResponse {
  accessToken: string | null;
  expiresAt: string | null;
  user: null | AuthenticatedUserResponse;
  challenge: null | TwoFactorChallengeResponse;
}

export interface SimulatePromotionBody {
  lines: QuoteLinePayload[];
  customerId: string | null;
  stateId: string | null;
  couponCode: string | null;
  paymentMethod: null | QuotePaymentMethod;
  isFirstOrder: boolean;
  shippingAmount: number;
}

export interface SitemapIndexEntryResponse {
  section: string;
  page: number;
  loc: string;
  urlCount: number;
  lastModified: string | null;
}

export interface SitemapIndexResponse {
  entries: SitemapIndexEntryResponse[];
}

export interface SitemapPageResponse {
  section: string;
  page: number;
  urls: SitemapUrlResponse[];
}

export interface SitemapUrlResponse {
  loc: string;
  lastModified: string | null;
  changeFrequency: string;
  priority: number;
}

export interface SpecificationPayload {
  label: string;
  value: string;
  group: string | null;
}

export type StackingMode =
  | "Exclusive"
  | "Stackable";

export type StateKind =
  | "State"
  | "UnionTerritory";

export interface StateResponse {
  id: string;
  code: string;
  name: string;
  kind: StateKind;
}

export interface StatutoryExtractResponse {
  from: string;
  to: string;
  totalTcs: number;
  totalTds: number;
  currencyCode: string;
  rows: StatutoryExtractRow[];
}

export interface StatutoryExtractRow {
  vendorId: string;
  vendorCode: string | null;
  vendorName: string | null;
  gstin: string | null;
  pan: string | null;
  periodStart: string;
  periodEnd: string;
  grossSales: number;
  refunds: number;
  netTaxableSupplies: number;
  tcs: number;
  netGrossSales: number;
  tds: number;
  currencyCode: string;
}

export interface StockAdjustmentBody {
  stockItemId: string;
  change: number;
  reason: StockMovementReason;
  note: string | null;
}

export interface StockBatchBody {
  batchCode: string;
  quantity: number;
  manufacturedOn: string | null;
  expiresOn: string | null;
  supplierId: string | null;
}

export interface StockBatchResponse {
  id: string;
  stockItemId: string;
  batchCode: string;
  quantity: number;
  manufacturedOn: string | null;
  expiresOn: string | null;
  supplierId: string | null;
}

export interface StockItemResponse {
  id: string;
  listingId: string;
  warehouseId: string;
  warehouseCode: string;
  vendorId: string | null;
  sku: string;
  quantityOnHand: number;
  quantityReserved: number;
  quantityAvailable: number;
  reorderLevel: number;
  reorderQuantity: number;
  allowBackorder: boolean;
  allowPreorder: boolean;
  preorderAvailableAt: string | null;
  trackingMode: StockTrackingMode;
  isLow: boolean;
  updatedAt: string | null;
}

export interface StockLedgerEntryResponse {
  id: string;
  stockItemId: string;
  change: number;
  balanceAfter: number;
  reservedChange: number;
  reservedAfter: number;
  reason: StockMovementReason;
  referenceType: string | null;
  referenceId: string | null;
  note: string | null;
  actorId: string | null;
  occurredAt: string;
}

export type StockMovementReason =
  | "Purchase"
  | "Sale"
  | "Reservation"
  | "Release"
  | "Return"
  | "Adjustment"
  | "Damage"
  | "TransferIn"
  | "TransferOut"
  | "Correction";

export interface StockReservationResponse {
  id: string;
  stockItemId: string;
  listingId: string;
  quantity: number;
  referenceType: string;
  referenceId: string;
  status: ReservationStatus;
  expiresAt: string;
  settledAt: string | null;
  createdAt: string;
}

export interface StockSerialResponse {
  id: string;
  stockItemId: string;
  serialNumber: string;
  batchId: string | null;
  status: SerialStatus;
  referenceId: string | null;
}

export interface StockSerialsBody {
  serialNumbers: string[];
  batchId: string | null;
}

export interface StockSettingsBody {
  reorderLevel: number;
  reorderQuantity: number;
  allowBackorder: boolean;
  allowPreorder: boolean;
  preorderAvailableAt: string | null;
  trackingMode: StockTrackingMode;
}

export interface StockSubscriptionResponse {
  id: string;
  kind: string;
  variantId: string;
  productId: string;
  status: string;
  targetPrice: number | null;
  priceAtSubscription: number | null;
  notifiedAt: string | null;
  expiresAt: string | null;
  createdAt: string;
}

export interface StockTakeCountPayload {
  stockItemId: string;
  countedQuantity: number;
  note: string | null;
}

export interface StockTakeCountsBody {
  lines: StockTakeCountPayload[];
}

export interface StockTakeLineResponse {
  id: string;
  stockItemId: string;
  sku: string;
  expectedQuantity: number;
  countedQuantity: number | null;
  variance: number | null;
  note: string | null;
}

export interface StockTakeResponse {
  id: string;
  number: string;
  warehouseId: string;
  status: StockTakeStatus;
  scheduledFor: string | null;
  submittedAt: string | null;
  submittedBy: string | null;
  notes: string | null;
  lines: StockTakeLineResponse[];
  createdAt: string;
}

export type StockTakeStatus =
  | "Draft"
  | "Counting"
  | "Submitted"
  | "Cancelled";

export type StockTrackingMode =
  | "None"
  | "Batch"
  | "Serial";

export interface StockTransferBody {
  listingId: string;
  fromWarehouseId: string;
  toWarehouseId: string;
  quantity: number;
  note: string | null;
}

export interface StopWordResponse {
  id: string;
  word: string;
  isActive: boolean;
  createdAt: string;
}

export interface StoreBannerResponse {
  id: string;
  placement: BannerPlacement;
  image: null | ContentImageResponse;
  mobileImage: null | ContentImageResponse;
  message: string | null;
  altText: string | null;
  link: string | null;
  ctaLabel: string | null;
  priority: number;
}

export interface StoreBlockResponse {
  id: string;
  type: string;
  position: number;
  config: unknown;
  images: ContentImageResponse[];
  products: ProductCardResponse[];
  categories: CategoryTileResponse[];
}

export interface StoreCollectionResponse {
  slug: string;
  name: string;
  description: string | null;
  seo: SeoResponse;
  heroImage: null | ContentImageResponse;
  itemCount: number;
  products: ProductCardResponse[];
  nextCursor: string | null;
}

export interface StoreConfigResponse {
  tenantCode: string;
  settings: Record<string, unknown>;
  features: Record<string, boolean>;
}

export interface StoreMenuItemResponse {
  label: string;
  href: string | null;
  opensInNewTab: boolean;
  icon: null | ContentImageResponse;
  badge: string | null;
  children: StoreMenuItemResponse[];
}

export interface StoreMenuResponse {
  code: string;
  name: string;
  placement: string | null;
  items: StoreMenuItemResponse[];
}

export interface StorePageResponse {
  id: string;
  slug: string;
  type: PageType;
  title: string;
  summary: string | null;
  seo: SeoResponse;
  coverImage: null | ContentImageResponse;
  author: string | null;
  tags: string[];
  publishedAt: string | null;
  updatedAt: string | null;
  blocks: StoreBlockResponse[];
}

export interface StoreQuoteBody {
  lines: QuoteLinePayload[];
  stateId: string | null;
  couponCode: string | null;
  paymentMethod: null | QuotePaymentMethod;
  shippingAmount: number;
  walletRedeemRequested: number;
}

export interface StoreSettingsResponse {
  sections: SettingsSectionResponse[];
}

export interface StorefrontOffer {
  listingId: string;
  vendorId: string;
  vendorName: string;
  vendorSlug: string;
  vendorRating: number | null;
  mrp: number;
  sellingPrice: number;
  discountPercent: number;
  isCodAllowed: boolean;
  maxOrderQuantity: number | null;
  dispatchHours: number;
  isBuyBox: boolean;
}

export interface StorefrontProduct {
  id: string;
  name: string;
  slug: string;
  categoryId: string;
  brandId: string | null;
  shortDescription: string | null;
  description: string | null;
  specifications: SpecificationPayload[];
  attributes: AttributeValueResponse[];
  media: MediaResponse[];
  variants: StorefrontVariant[];
  hsnCode: string | null;
  gstRate: number;
  countryOfOrigin: string | null;
  manufacturer: PartyPayload;
  packer: PartyPayload;
  importer: PartyPayload;
  isReturnable: boolean;
  returnWindowDays: number | null;
  warranty: string | null;
  seo: SeoPayload;
  ratingAverage: number | null;
  ratingCount: number;
}

export interface StorefrontVariant {
  id: string;
  sku: string;
  nameSuffix: string | null;
  netQuantity: string | null;
  mrp: number;
  isDefault: boolean;
  options: AttributeValueResponse[];
  media: MediaResponse[];
  buyBox: null | StorefrontOffer;
  offerCount: number;
}

export interface StorefrontVendorResponse {
  id: string;
  displayName: string;
  slug: string;
  about: string | null;
  logoFileId: string | null;
  bannerFileId: string | null;
  rating: number | null;
  dispatchSlaHours: number;
  returnPolicy: ReturnPolicyPayload;
  onboardedAt: string | null;
}

export interface StructuredDataResponse {
  path: string;
  graph: unknown;
}

export interface SubOrderResponse {
  id: string;
  subOrderNumber: string;
  vendorId: string;
  vendorName: string | null;
  status: string;
  itemsTotal: number;
  discountTotal: number;
  shippingTotal: number;
  taxTotal: number;
  total: number;
  cancelledTotal: number;
  netTotal: number;
  currencyCode: string;
  shippingOptionCode: string | null;
  carrier: string | null;
  promisedMinDays: number;
  promisedMaxDays: number;
  dispatchDueAt: string | null;
  confirmedAt: string | null;
  shippedAt: string | null;
  deliveredAt: string | null;
  returnWindowEndsAt: string | null;
  cancelledAt: string | null;
  cancelledBy: string | null;
  cancellationReason: string | null;
  isCancellable: boolean;
  lines: OrderLineResponse[];
  invoice: null | InvoiceResponse;
  nextStatuses: string[];
}

export interface SubmitKycBody {
  documentType: KycDocumentType;
  fileId: string;
  number: string | null;
}

export interface SubscriptionBody {
  variantId: string;
  kind: string | null;
  targetPrice: number | null;
  email: string | null;
}

export interface SuggestionResponse {
  query: string;
  suggestions: SearchSuggestion[];
}

export interface SupplierResponse {
  id: string;
  vendorId: string | null;
  code: string;
  name: string;
  contactName: string | null;
  email: string | null;
  phone: string | null;
  gstin: string | null;
  address: null | AddressPayload;
  paymentTermsDays: number;
  isActive: boolean;
  createdAt: string;
}

export interface SynonymResponse {
  id: string;
  term: string;
  expansions: string[];
  isBidirectional: boolean;
  isActive: boolean;
  note: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface TaxRateResolutionResponse {
  hsnCode: string;
  asOf: string;
  rate: number;
  cessRate: number;
  taxRateId: string | null;
}

export interface TaxRateResponse {
  id: string;
  hsnCode: string;
  description: string | null;
  rate: number;
  cessRate: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface TemplateResponse {
  id: string;
  eventKey: string;
  channel: string;
  locale: string;
  subject: string;
  body: string;
  providerTemplateId: string | null;
  category: string;
  isSensitive: boolean;
  isTransactional: boolean;
  isActive: boolean;
  version: number;
  placeholders: string[];
  dltViolation: string | null;
}

export interface TrackingEventResponse {
  status: string;
  courierStatus: string | null;
  location: string | null;
  remark: string | null;
  isApplied: boolean;
  occurredAt: string;
}

export interface TransitionBody {
  status: string;
  reason: string | null;
}

export interface TransitionPageBody {
  status: PageStatus;
  scheduledAt: string | null;
  note: string | null;
}

export interface TwoFactorChallengeResponse {
  type: string;
  challengeToken: string;
}

export interface TwoFactorEnrolBody {
  challengeToken: string;
}

export interface TwoFactorSetupResponse {
  secret: string;
  otpAuthUri: string;
}

export interface TwoFactorVerifyBody {
  challengeToken: string;
  code: string;
}

export interface UpdateCartItemBody {
  quantity: number | null;
  savedForLater: boolean | null;
}

export interface UpdateCollectionBody {
  slug: string | null;
  name: string | null;
  description: string | null;
  seo: null | SeoBody;
  heroImageFileId: string | null;
  isActive: boolean;
  isListed: boolean;
}

export interface UpdateCommissionPlanBody {
  name: string;
  description: string | null;
  planType: CommissionPlanType;
  defaultRate: number;
  defaultFixedFee: number;
  isActive: boolean;
  isDefault: boolean;
  rules: CommissionRulePayload[];
}

export interface UpdateFeatureFlagRequest {
  enabled: boolean;
  rollout: null | RolloutModel;
  description: string | null;
}

export interface UpdateListingBody {
  mrp: number;
  sellingPrice: number;
  vendorSku: string | null;
  handlingTimeHours: number;
  isCodAllowed: boolean;
  maxOrderQuantity: number | null;
}

export interface UpdateMeBody {
  firstName: string | null;
  lastName: string | null;
  dateOfBirth: string | null;
  gender: string | null;
  gstin: string | null;
  marketingConsent: boolean;
}

export interface UpdateMenuBody {
  name: string | null;
  placement: string | null;
  isActive: boolean;
  items: MenuItemBody[] | null;
}

export interface UpdatePageBody {
  slug: string | null;
  title: string | null;
  summary: string | null;
  seo: null | SeoBody;
  coverImageFileId: string | null;
  author: string | null;
  tags: string[] | null;
  blocks: BlockBody[] | null;
}

export interface UpdatePreferenceRequest {
  category: string;
  email: boolean;
  sms: boolean;
  whatsApp: boolean;
  inApp: boolean;
}

export interface UpdatePriceListBody {
  name: string;
  type: PriceListType;
  priority: number;
  startsAt: string | null;
  endsAt: string | null;
}

export interface UpdatePurchaseOrderBody {
  expectedAt: string | null;
  notes: string | null;
  lines: PurchaseOrderLinePayload[];
}

export interface UpdateRedirectBody {
  toPath: string | null;
  statusCode: number;
  isActive: boolean;
  note: string | null;
}

export interface UpdateRoleBody {
  name: string;
  description: string;
  permissions: string[];
}

export interface UpdateSupplierBody {
  name: string;
  contactName: string | null;
  email: string | null;
  phone: string | null;
  gstin: string | null;
  address: null | AddressPayload;
  paymentTermsDays: number;
  isActive: boolean;
}

export interface UpdateSynonymBody {
  expansions: string[] | null;
  isBidirectional: boolean;
  isActive: boolean;
  note: string | null;
}

export interface UpdateTaxRateBody {
  description: string | null;
  rate: number;
  cessRate: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  isActive: boolean;
}

export interface UpdateTemplateRequest {
  subject: string;
  body: string;
  providerTemplateId: string | null;
  isActive: boolean;
}

export interface UpdateVendorBody {
  legalName: string;
  businessType: VendorBusinessType;
  pan: string | null;
  gstin: string | null;
  registeredAddress: AddressPayload;
}

export interface UpdateVendorOperationsBody {
  dispatchSlaHours: number;
  returnPolicy: ReturnPolicyPayload;
  servesAllIndia: boolean;
}

export interface UpdateVendorProfileBody {
  displayName: string;
  about: string | null;
  logoFileId: string | null;
  bannerFileId: string | null;
  supportEmail: string | null;
  supportPhone: string | null;
}

export interface UpdateWarehouseBody {
  name: string;
  pincode: string;
  address: null | AddressPayload;
  priority: number;
  isActive: boolean;
}

export interface UpsertPriceListItemsBody {
  items: PriceListItemPayload[];
}

export type UserStatus =
  | "Active"
  | "Suspended"
  | "Disabled";

export type UserType =
  | "Customer"
  | "Vendor"
  | "Staff";

export interface VariantBody {
  sku: string | null;
  barcode: string | null;
  nameSuffix: string | null;
  mrp: number;
  netQuantity: string | null;
  shelfLifeDays: number | null;
  expiresOn: string | null;
  weightGrams: number;
  lengthMm: number;
  widthMm: number;
  heightMm: number;
  position: number;
  isDefault: boolean;
  options: VariantOptionPayload[] | null;
  media: MediaPayload[] | null;
}

export interface VariantOptionPayload {
  attributeId: string;
  optionId: string;
}

export interface VariantResponse {
  id: string;
  productId: string;
  sku: string;
  barcode: string | null;
  nameSuffix: string | null;
  status: VariantStatus;
  mrp: number;
  netQuantity: string | null;
  shelfLifeDays: number | null;
  expiresOn: string | null;
  weightGrams: number;
  lengthMm: number;
  widthMm: number;
  heightMm: number;
  position: number;
  isDefault: boolean;
  options: AttributeValueResponse[];
  media: MediaResponse[];
}

export type VariantStatus =
  | "Draft"
  | "Active"
  | "Inactive"
  | "Archived";

export interface VendorBalanceResponse {
  vendorId: string;
  currentBalance: number;
  unsettledBalance: number;
  awaitingPayout: number;
  currencyCode: string;
  lastCycleClosedAt: string | null;
  lastPaidAt: string | null;
}

export type VendorBusinessType =
  | "Individual"
  | "SoleProprietorship"
  | "Partnership"
  | "LimitedLiabilityPartnership"
  | "PrivateLimited"
  | "PublicLimited"
  | "HinduUndividedFamily"
  | "Trust";

export interface VendorListItem {
  id: string;
  code: string;
  legalName: string;
  displayName: string;
  slug: string;
  status: VendorStatus;
  rating: number | null;
  commissionPlanId: string | null;
  onboardedAt: string | null;
  createdAt: string;
}

export interface VendorReadiness {
  isReady: boolean;
  blockers: string[];
}

export interface VendorResponse {
  id: string;
  code: string;
  legalName: string;
  displayName: string;
  slug: string;
  status: VendorStatus;
  statusReason: string | null;
  businessType: VendorBusinessType;
  pan: string | null;
  gstin: string | null;
  registeredAddress: AddressPayload;
  supportEmail: string | null;
  supportPhone: string | null;
  about: string | null;
  logoFileId: string | null;
  bannerFileId: string | null;
  commissionPlanId: string | null;
  dispatchSlaHours: number;
  returnPolicy: ReturnPolicyPayload;
  servesAllIndia: boolean;
  rating: number | null;
  gatewayAccountId: string | null;
  onboardedAt: string | null;
  createdAt: string;
  nextStatuses: string[];
}

export interface VendorShippingChoiceBody {
  vendorId: string;
  optionCode: string;
}

export interface VendorShippingOptionsResponse {
  vendorId: string;
  vendorName: string;
  selectedCode: string | null;
  options: ShippingOptionResponse[];
}

export interface VendorStaffBody {
  userId: string;
  isOwner: boolean;
  jobTitle: string | null;
}

export interface VendorStaffResponse {
  id: string;
  vendorId: string | null;
  userId: string;
  isOwner: boolean;
  jobTitle: string | null;
  addedAt: string;
}

export type VendorStatus =
  | "Applied"
  | "UnderReview"
  | "Approved"
  | "Active"
  | "Suspended"
  | "Offboarded";

export interface VendorStatusBody {
  reason: string | null;
}

export interface VerificationConfirmBody {
  channel: OtpChannel;
  code: string;
}

export interface VerificationRequestBody {
  channel: OtpChannel;
}

export interface VerifyBankAccountBody {
  verified: boolean;
  note: string | null;
}

export interface VerifyCheckoutBody {
  providerOrderId: string;
  providerPaymentId: string;
  signature: string;
}

export interface VerifyKycBody {
  approve: boolean;
  rejectionReason: string | null;
}

export interface VoteBody {
  isHelpful: boolean;
}

export interface WalletResponse {
  customerId: string;
  balance: number;
  currencyCode: string;
  isActive: boolean;
  updatedAt: string | null;
}

export interface WalletTransactionResponse {
  id: string;
  type: string;
  amount: number;
  balanceAfter: number;
  reason: string;
  referenceType: string | null;
  referenceId: string | null;
  occurredAt: string;
  expiresAt: string | null;
  note: string | null;
}

export interface WarehouseResponse {
  id: string;
  vendorId: string | null;
  code: string;
  name: string;
  pincode: string;
  address: null | AddressPayload;
  isActive: boolean;
  priority: number;
  stockItemCount: number;
  createdAt: string;
}

export interface WeighBody {
  weight: number;
  dimensions: null | ParcelDimensions;
}

export interface WishlistBody {
  name: string | null;
}

export interface WishlistItemResponse {
  variantId: string;
  productId: string;
  listingId: string | null;
  name: string | null;
  slug: string | null;
  sku: string | null;
  imageFileId: string | null;
  imageUrl: string | null;
  mrp: number | null;
  price: number | null;
  currencyCode: string | null;
  isPurchasable: boolean;
  ratingAverage: number | null;
  ratingCount: number;
  note: string | null;
  priority: number;
  addedAt: string;
}

export interface WishlistResponse {
  id: string;
  name: string;
  isDefault: boolean;
  itemCount: number;
  shareToken: string | null;
  items: WishlistItemResponse[];
}

export interface ZoneBody {
  code: string | null;
  name: string;
  priority: number;
  states: string[] | null;
  pincodeRanges: PincodeRangeModel[] | null;
  isActive?: boolean;
}
