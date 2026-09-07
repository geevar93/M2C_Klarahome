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
export * from './lib/inventory-admin.service';
export * from './lib/media-library.service';
export * from './lib/notification-centre.service';
export * from './lib/orders-admin.service';
export * from './lib/platform-settings.service';
export * from './lib/pricing-admin.service';
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
  // Platform, identity and notifications — Step 26.
  AuditLogResponse,
  MeResponse,
  NotificationLogResponse,
  PageInfo,
  PermissionGroupResponse,
  RoleResponse,
  SessionResponse,
  TwoFactorSetupResponse,

  // Catalogue — Step 27.
  AttributeBody,
  AttributeDataType,
  AttributeOptionPayload,
  AttributeResponse,
  AttributeSetResponse,
  AttributeValuePayload,
  BrandBody,
  BrandResponse,
  CatalogJobResponse,
  CatalogMediaKind,
  CategoryBody,
  CategoryNode,
  CategoryResponse,
  ImportErrorResponse,
  ListingResponse,
  MediaFileResponse,
  MediaPayload,
  MediaResponse,
  ModerationResponse,
  ProductBody,
  ProductListItem,
  ProductResponse,
  ProductStatus,
  SeoPayload,
  SpecificationPayload,
  VariantBody,
  VariantResponse,

  // Inventory — Step 27.
  GoodsReceiptLinePayload,
  GoodsReceiptResponse,
  PurchaseOrderLinePayload,
  PurchaseOrderResponse,
  StockItemResponse,
  StockLedgerEntryResponse,
  StockMovementReason,
  StockTakeResponse,
  SupplierResponse,
  WarehouseResponse,

  // Orders — Step 27.
  CancelLineBody,
  InvoiceResponse,
  OrderEventResponse,
  OrderLineResponse,
  OrderResponse,
  OrderSummaryResponse,
  SubOrderResponse,

  // Fulfilment — Step 27.
  CourierEventResponse,
  ManifestResponse,
  NdrResponse,
  PickListLineResponse,
  ShipmentResponse,
  ShipmentSummaryResponse,
  TrackingEventResponse,

  // Returns — Step 27.
  CreditNoteResponse,
  QcLineRequest,
  ReturnLineResponse,
  ReturnReasonResponse,
  ReturnResponse,
  ReturnSummaryResponse,

  // Pricing — Step 28.
  CreatePriceListBody,
  CreateTaxRateBody,
  EffectivePrice,
  PriceListItemPayload,
  PriceListItemResponse,
  PriceListResponse,
  PromotionBody,
  PromotionConditionsPayload,
  PromotionRedemptionResponse,
  PromotionResponse,
  PromotionScopePayload,
  PromotionTierPayload,
  QuoteLine,
  QuoteLinePayload,
  QuotePromotion,
  QuoteResult,
  QuoteVendorGroup,
  SimulatePromotionBody,
  TaxRateResolutionResponse,
  TaxRateResponse,
  UpdatePriceListBody,
  UpdateTaxRateBody,

  // Content — Step 28.
  BannerBody,
  BannerResponse,
  BlockBody,
  BlockFieldResponse,
  BlockResponse,
  BlockTypeResponse,
  CollectionItemBody,
  CollectionResponse,
  CollectionRuleBody,
  CollectionRuleResponse,
  CollectionSummaryResponse,
  ContentImageResponse,
  CreateCollectionBody,
  CreateMenuBody,
  CreatePageBody,
  CreateRedirectBody,
  MenuItemBody,
  MenuItemResponse,
  MenuResponse,
  MenuSummaryResponse,
  PageResponse,
  PageSummaryResponse,
  PageVersionResponse,
  PageVersionSummaryResponse,
  ProductCardResponse,
  RedirectResponse,
  RuleConditionBody,
  RuleConditionResponse,
  SeoBody,
  SeoResponse,
  StorePageResponse,
  TransitionPageBody,
  UpdateCollectionBody,
  UpdateMenuBody,
  UpdatePageBody,
  UpdateRedirectBody,

  // Sellers and commission — Step 28.
  AddBankAccountBody,
  AddressPayload,
  BankAccountResponse,
  BankVerificationStatus,
  CommissionPlanResponse,
  CommissionPlanType,
  CommissionQuote,
  CommissionRulePayload,
  CreateCommissionPlanBody,
  CreateVendorBody,
  KycDocumentResponse,
  KycDocumentType,
  KycSummaryResponse,
  KycVerificationStatus,
  PickupLocationBody,
  PickupLocationResponse,
  ReturnPolicyPayload,
  ServiceableRegionPayload,
  ServiceableRegionResponse,
  ServiceableRegionScope,
  ServiceableRegionsResponse,
  SetServiceableRegionsBody,
  SubmitKycBody,
  UpdateCommissionPlanBody,
  UpdateVendorBody,
  UpdateVendorOperationsBody,
  UpdateVendorProfileBody,
  VendorBusinessType,
  VendorListItem,
  VendorReadiness,
  VendorResponse,
  VendorStaffBody,
  VendorStaffResponse,
  VendorStatus,

  // Settlements — Step 28.
  AdjustmentBody,
  LedgerEntryResponse,
  LedgerStatementResponse,
  LedgerTotalResponse,
  PayoutBatchResponse,
  PayoutItemResponse,
  PlatformRevenueResponse,
  SettlementCycleResponse,
  StatutoryExtractResponse,
  StatutoryExtractRow,
  VendorBalanceResponse,

  // Reporting — Step 28.
  ReportColumn,
  ReportColumnKind,
  ReportDefinition,
  ReportDownloadResponse,
  ReportRunResponse,
  ReportScheduleResponse,
  ScheduleBody,

  // Identity, platform and shipping — Step 28.
  AdminUserResponse,
  CreateRoleBody,
  CreateUserBody,
  DeliveryCoverageResponse,
  FeatureFlagResponse,
  PermissionResponse,
  PincodeRangeModel,
  RateBody,
  RateTerms,
  RolloutModel,
  RoleScope,
  ServiceabilityResponse,
  SettingsSectionResponse,
  ShippingRateResponse,
  ShippingZoneResponse,
  StoreSettingsResponse,
  UpdateRoleBody,
  UserStatus,
  UserType,
  ZoneBody,
} from '@klarahome/data-access-api';
