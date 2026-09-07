export * from './lib/admin-session.service';
export * from './lib/audit-log.service';
export * from './lib/catalog-admin.service';
export * from './lib/content-admin.service';
export * from './lib/cursor-list';
export * from './lib/dashboard.service';
export * from './lib/document-print.service';
export * from './lib/fulfilment.service';
export * from './lib/global-search.service';
export * from './lib/identity-admin.service';
export * from './lib/impersonation.store';
export * from './lib/inventory-admin.service';
export * from './lib/media-library.service';
export * from './lib/notification-centre.service';
export * from './lib/orders-admin.service';
export * from './lib/platform-settings.service';
export * from './lib/pricing-admin.service';
export * from './lib/reference-data.service';
export * from './lib/reporting-admin.service';
export * from './lib/returns-admin.service';
export * from './lib/settlements-admin.service';
export * from './lib/shipping-zones.service';
export * from './lib/vendors-admin.service';

/**
 * The response shapes the admin screens render, re-exported.
 *
 * The apps may not import `@klarahome/data-access-api` — the boundary that keeps the generated
 * client behind a hand-written seam — but an admin page still has to be able to name the type of
 * the row it is drawing. The same reasoning as `data-access-auth`'s re-export of `ApiError`: the
 * boundary is about the *transport*, not about the shape of an answer.
 *
 * The list grew at Step 27 from eight names to the operations vocabulary, and the growth is the
 * point rather than a smell: an admin screen renders the API's own record, so every response it
 * draws has to be nameable. What is *not* re-exported is any client class — a page still cannot
 * make a request except through a service in this library.
 */
export type {
  // Catalogue — Step 27.
  // compile error, and the vocabulary files that held them now hold only labels.
  // Content — Step 28.
  // Fulfilment — Step 27.
  // Identity, platform and shipping — Step 28.
  // Inventory — Step 27.
  // nothing checked; a value renamed on the server produced a refused request rather than a
  // Orders — Step 27.
  // Platform, identity and notifications — Step 26.
  // Pricing — Step 28.
  // Reporting — Step 28.
  // Returns — Step 27.
  // Sellers and commission — Step 28.
  // Settlements — Step 28.
  // Step 28B (deliverable 11). Every one of them was a `readonly Choice[]` of bare strings that
  // The enumerations the back office used to keep its own copy of, typed in the contract at
  AddBankAccountBody,
  AddressPayload,
  AdjustmentBody,
  AdminUserResponse,
  AttributeBody,
  AttributeDataType,
  AttributeOptionPayload,
  AttributeResponse,
  AttributeSetResponse,
  AttributeValuePayload,
  AuditLogResponse,
  BankAccountResponse,
  BankVerificationStatus,
  BannerAudience,
  BannerBody,
  BannerPlacement,
  BannerResponse,
  BlockBody,
  BlockFieldResponse,
  BlockResponse,
  BlockTypeResponse,
  BrandBody,
  BrandResponse,
  CancelLineBody,
  CatalogJobResponse,
  CatalogMediaKind,
  CategoryBody,
  CategoryNode,
  CategoryResponse,
  CollectionItemBody,
  CollectionKind,
  CollectionResponse,
  CollectionRuleBody,
  CollectionRuleResponse,
  CollectionSort,
  CollectionSummaryResponse,
  CommissionPlanResponse,
  CommissionPlanType,
  CommissionQuote,
  CommissionRulePayload,
  ContentImageResponse,
  CourierEventResponse,
  CreateCollectionBody,
  CreateCommissionPlanBody,
  CreateMenuBody,
  CreatePageBody,
  CreatePriceListBody,
  CreateRedirectBody,
  CreateRoleBody,
  CreateTaxRateBody,
  CreateUserBody,
  CreateVendorBody,
  CreditNoteResponse,
  DeliveryCoverageResponse,
  EffectivePrice,
  FeatureFlagResponse,
  GoodsReceiptLinePayload,
  GoodsReceiptResponse,
  ImpersonationResponse,
  ImportErrorResponse,
  InvoiceResponse,
  KycDocumentResponse,
  KycDocumentType,
  KycSummaryResponse,
  KycVerificationStatus,
  LedgerEntryResponse,
  LedgerEntryType,
  LedgerStatementResponse,
  LedgerTotalResponse,
  ListingResponse,
  ManifestResponse,
  MediaFileResponse,
  MediaPayload,
  MediaResponse,
  MenuItemBody,
  MenuItemResponse,
  MenuLinkType,
  MenuResponse,
  MenuSummaryResponse,
  MeResponse,
  ModerationResponse,
  NdrAction,
  NdrResponse,
  NotificationLogResponse,
  OrderEventResponse,
  OrderLineResponse,
  OrderResponse,
  OrderSummaryResponse,
  PageInfo,
  PageResponse,
  PageStatus,
  PageSummaryResponse,
  PageType,
  PageVersionResponse,
  PageVersionSummaryResponse,
  PayoutBatchResponse,
  PayoutBatchStatus,
  PayoutItemResponse,
  PayoutItemStatus,
  PermissionGroupResponse,
  PermissionResponse,
  PickListLineResponse,
  PickupLocationBody,
  PickupLocationResponse,
  PincodeRangeModel,
  PincodeResponse,
  PlatformRevenueResponse,
  PriceListItemPayload,
  PriceListItemResponse,
  PriceListResponse,
  PriceListType,
  ProductBody,
  ProductCardResponse,
  ProductListItem,
  ProductResponse,
  ProductStatus,
  PromotionApplication,
  PromotionBody,
  PromotionConditionsPayload,
  PromotionRedemptionResponse,
  PromotionResponse,
  PromotionScopePayload,
  PromotionTierPayload,
  PromotionType,
  PurchaseOrderLinePayload,
  PurchaseOrderResponse,
  QcLineRequest,
  QuoteLine,
  QuoteLinePayload,
  QuotePaymentMethod,
  QuotePromotion,
  QuoteResult,
  QuoteVendorGroup,
  RateBody,
  RateTerms,
  RedirectResponse,
  ReportColumn,
  ReportColumnKind,
  ReportDefinition,
  ReportDownloadResponse,
  ReportRunResponse,
  ReportScheduleResponse,
  ReturnDisposition,
  ReturnLineResponse,
  ReturnPolicyPayload,
  ReturnReasonResponse,
  ReturnResponse,
  ReturnSummaryResponse,
  RoleResponse,
  RoleScope,
  RolloutModel,
  RuleConditionBody,
  RuleConditionResponse,
  RuleField,
  RuleOperator,
  ScheduleBody,
  SeoBody,
  SeoPayload,
  SeoResponse,
  ServiceabilityResponse,
  ServiceableRegionPayload,
  ServiceableRegionResponse,
  ServiceableRegionScope,
  ServiceableRegionsResponse,
  SessionResponse,
  SetServiceableRegionsBody,
  SettingsFieldSchema,
  SettingsSchemaResponse,
  SettingsSectionResponse,
  SettingsSectionSchema,
  SettlementCycleResponse,
  SettlementCycleStatus,
  ShipmentResponse,
  ShipmentSummaryResponse,
  ShippingRateResponse,
  ShippingZoneResponse,
  SimulatePromotionBody,
  SpecificationPayload,
  StackingMode,
  StateResponse,
  StatutoryExtractResponse,
  StatutoryExtractRow,
  StockItemResponse,
  StockLedgerEntryResponse,
  StockMovementReason,
  StockTakeResponse,
  StorePageResponse,
  StoreSettingsResponse,
  SubmitKycBody,
  SubOrderResponse,
  SupplierResponse,
  TaxRateResolutionResponse,
  TaxRateResponse,
  TrackingEventResponse,
  TransitionPageBody,
  TwoFactorSetupResponse,
  UpdateCollectionBody,
  UpdateCommissionPlanBody,
  UpdateMenuBody,
  UpdatePageBody,
  UpdatePriceListBody,
  UpdateRedirectBody,
  UpdateRoleBody,
  UpdateTaxRateBody,
  UpdateVendorBody,
  UpdateVendorOperationsBody,
  UpdateVendorProfileBody,
  UserStatus,
  UserType,
  VariantBody,
  VariantResponse,
  VendorBalanceResponse,
  VendorBusinessType,
  VendorListItem,
  VendorReadiness,
  VendorResponse,
  VendorStaffBody,
  VendorStaffResponse,
  VendorStatus,
  WarehouseResponse,
  ZoneBody,
} from '@klarahome/data-access-api';
