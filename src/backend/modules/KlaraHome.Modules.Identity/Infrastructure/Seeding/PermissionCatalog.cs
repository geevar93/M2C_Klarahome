using KlaraHome.Modules.Identity.Domain;

namespace KlaraHome.Modules.Identity.Infrastructure.Seeding;

/// <summary>One permission, as the catalogue declares it.</summary>
/// <param name="Code">The dotted lowercase code an endpoint asks for.</param>
/// <param name="Group">The heading the admin UI files it under.</param>
/// <param name="Description">What holding it lets a user do.</param>
internal sealed record PermissionDescriptor(string Code, string Group, string Description);

/// <summary>
/// Every permission this platform enforces (docs/07-security-compliance.md §2).
/// </summary>
/// <remarks>
/// <para>
/// Declared in code, reconciled into <c>identity.permissions</c> on every deploy. The table is a
/// projection for the admin UI, never the authority: a permission exists because an endpoint
/// declares it with <c>RequirePermission</c>, and an integration test asserts that every permission
/// an endpoint asks for appears here. A row that no endpoint asks for is a role checkbox that does
/// nothing, which is worse than a missing one because it looks like a grant.
/// </para>
/// <para>
/// Permissions for modules that do not exist yet are deliberately absent. They are added by the
/// step that builds the endpoints asking for them, so this list and the enforced surface can never
/// drift apart.
/// </para>
/// </remarks>
internal static class PermissionCatalog
{
    /// <summary>Read the settings, branding and feature flags; change them.</summary>
    public const string PlatformSettingsManage = "platform.settings.manage";

    /// <summary>Read the audit trail.</summary>
    public const string PlatformAuditRead = "platform.audit.read";

    /// <summary>List and view user accounts.</summary>
    public const string IdentityUserRead = "identity.user.read";

    /// <summary>Create accounts, change their details, lock and unlock them.</summary>
    public const string IdentityUserManage = "identity.user.manage";

    /// <summary>Grant and revoke roles.</summary>
    public const string IdentityRoleAssign = "identity.role.assign";

    /// <summary>List roles and the permission catalogue.</summary>
    public const string IdentityRoleRead = "identity.role.read";

    /// <summary>Create roles and change what they grant.</summary>
    public const string IdentityRoleManage = "identity.role.manage";

    /// <summary>Browse the media library and read one file's entry.</summary>
    public const string MediaFileRead = "media.file.read";

    /// <summary>Upload and delete files.</summary>
    public const string MediaFileManage = "media.file.manage";

    /// <summary>Read and rewrite notification templates, and send a test message.</summary>
    public const string NotificationTemplateManage = "notifications.template.manage";

    /// <summary>Read the delivery log and re-queue a message.</summary>
    public const string NotificationLogRead = "notifications.log.read";

    /// <summary>List sellers and read one, with their documents, accounts and locations.</summary>
    public const string VendorRead = "vendors.vendor.read";

    /// <summary>
    /// Create a seller and edit their details, profile, settings, staff, bank accounts and pickup
    /// locations. Held by a vendor owner too, who is confined to their own seller.
    /// </summary>
    public const string VendorManage = "vendors.vendor.manage";

    /// <summary>
    /// Move a seller through the onboarding life cycle. Separate from
    /// <see cref="VendorManage"/> precisely so that a seller who can edit their own record cannot
    /// approve it.
    /// </summary>
    public const string VendorApprove = "vendors.vendor.approve";

    /// <summary>Accept or refuse a KYC document, and record a bank-account check.</summary>
    public const string VendorKycVerify = "vendors.kyc.verify";

    /// <summary>Create and edit commission plans, and put a seller on one.</summary>
    public const string VendorCommissionManage = "vendors.commission.manage";

    /// <summary>Read the taxonomy: categories, brands, attributes and attribute sets.</summary>
    public const string CatalogTaxonomyRead = "catalog.taxonomy.read";

    /// <summary>
    /// Change the taxonomy. Platform staff only — the tree and the attribute vocabulary are shared
    /// by every seller, so one reshaping them would reshape everybody's products.
    /// </summary>
    public const string CatalogTaxonomyManage = "catalog.taxonomy.manage";

    /// <summary>List products and variants, and read one.</summary>
    public const string CatalogProductRead = "catalog.product.read";

    /// <summary>Create and edit products and variants. A seller is confined to their own.</summary>
    public const string CatalogProductManage = "catalog.product.manage";

    /// <summary>
    /// Approve, reject, publish or withdraw a product. Platform staff only — a seller holding this
    /// could approve their own listing, which is the whole point of moderation.
    /// </summary>
    public const string CatalogProductModerate = "catalog.product.moderate";

    /// <summary>List offers and read one.</summary>
    public const string CatalogListingRead = "catalog.listing.read";

    /// <summary>Open an offer, change its price and terms, and pause or resume it.</summary>
    public const string CatalogListingManage = "catalog.listing.manage";

    /// <summary>Queue a bulk catalogue import or export, and read its report.</summary>
    public const string CatalogImportRun = "catalog.import.run";

    /// <summary>List stock rows and read one, with its ledger, holds, lots and serials.</summary>
    public const string InventoryStockRead = "inventory.stock.read";

    /// <summary>
    /// Move stock by hand and set a row's replenishment policy. Separate from
    /// <see cref="InventoryStockRead"/> because a correction is how stock appears from nowhere.
    /// </summary>
    public const string InventoryStockAdjust = "inventory.stock.adjust";

    /// <summary>Open, rename and close stock locations.</summary>
    public const string InventoryWarehouseManage = "inventory.warehouse.manage";

    /// <summary>Keep suppliers, raise purchase orders and book goods in.</summary>
    public const string InventoryPurchasingManage = "inventory.purchasing.manage";

    /// <summary>Schedule a stock take, enter counts and submit the variances.</summary>
    public const string InventoryStockTakeManage = "inventory.stock-take.manage";

    /// <summary>List price lists, read one, and read the prices in it.</summary>
    public const string PricingPriceListRead = "pricing.price-list.read";

    /// <summary>Open price lists, set prices in them, and switch them on and off.</summary>
    public const string PricingPriceListManage = "pricing.price-list.manage";

    /// <summary>Read the GST rates and resolve one for a date.</summary>
    public const string PricingTaxRateRead = "pricing.tax-rate.read";

    /// <summary>
    /// Record and amend GST rates. Deliberately narrow: the rate table decides what every customer
    /// is charged and what the business remits.
    /// </summary>
    public const string PricingTaxRateManage = "pricing.tax-rate.manage";

    /// <summary>List promotions, read one, and read its redemptions.</summary>
    public const string PricingPromotionRead = "pricing.promotion.read";

    /// <summary>Create and edit promotions, and switch them on and off.</summary>
    public const string PricingPromotionManage = "pricing.promotion.manage";

    /// <summary>Read a customer's store-credit balance and statement.</summary>
    public const string PricingWalletRead = "pricing.wallet.read";

    /// <summary>
    /// Adjust a customer's store credit by hand. The only permission on this platform that creates
    /// money out of nothing, which is why it is its own grant.
    /// </summary>
    public const string PricingWalletAdjust = "pricing.wallet.adjust";

    /// <summary>
    /// List baskets and checkout sessions, and read one. A read of personal data — what somebody is
    /// about to buy — so it is a grant rather than something every operator has.
    /// </summary>
    public const string CartRead = "carts.cart.read";

    /// <summary>
    /// Retire a basket. The only write an operator has over somebody else's basket: there is no
    /// endpoint that adds, removes or reprices a line on a shopper's behalf.
    /// </summary>
    public const string CartManage = "carts.cart.manage";

    /// <summary>
    /// List orders and sub-orders, and read one in full. A read of personal data — what somebody
    /// bought, where it went and what they paid — so it is a grant rather than something every
    /// operator has.
    /// </summary>
    public const string OrderRead = "orders.order.read";

    /// <summary>
    /// Move a sub-order along the lifecycle, and write internal notes. The seller's daily work, and
    /// Operations doing it on their behalf.
    /// </summary>
    public const string OrderTransition = "orders.order.transition";

    /// <summary>
    /// Cancel a sub-order, in whole or in part. Separate from moving one because it is the
    /// transition that moves money and, after dispatch, recalls a parcel from a courier.
    /// </summary>
    public const string OrderCancel = "orders.order.cancel";

    /// <summary>
    /// Raise a tax invoice by hand, and read any invoice. A statutory document with a gapless
    /// number; the automatic issue at dispatch needs no permission because nobody asked for it.
    /// </summary>
    public const string InvoiceManage = "orders.invoice.manage";

    /// <summary>
    /// List payments, refunds and settlement reports, and read one in full. A read of financial
    /// data about a named person, and what support needs to answer "did my payment go through".
    /// </summary>
    public const string PaymentRead = "payments.payment.read";

    /// <summary>
    /// Re-read a payment from the gateway and apply what it says, and capture an authorised one.
    /// The repair surface, and deliberately the only way a payment moves by hand: nothing anywhere
    /// sets a payment's status directly.
    /// </summary>
    public const string PaymentManage = "payments.payment.manage";

    /// <summary>
    /// Raise a refund. Separate from reading because it is money leaving; holding it does not send
    /// anything above the approval threshold.
    /// </summary>
    public const string RefundInitiate = "payments.refund.initiate";

    /// <summary>
    /// Approve or refuse a refund above the threshold. The checker half of maker-checker
    /// (docs/07-security-compliance.md 4), and separate precisely so it can be held by different
    /// people from <see cref="RefundInitiate"/>.
    /// </summary>
    public const string RefundApprove = "payments.refund.approve";

    /// <summary>
    /// Read the webhook log and its dead-letter queue, replay an event, import settlement reports
    /// and run reconciliation. The plumbing, and a different job from support or finance.
    /// </summary>
    public const string PaymentGatewayManage = "payments.gateway.manage";

    /// <summary>
    /// Record cash taken at a door and a courier's remittance. Operations work, and a different kind
    /// of money from the gateway's: nobody is reconciling an API here.
    /// </summary>
    public const string CodManage = "payments.cod.manage";

    /// <summary>
    /// List parcels and read one, with everywhere it has been. A read of a delivery address and a
    /// telephone number, and what support needs beyond what the order timeline already says.
    /// </summary>
    public const string ShipmentRead = "shipping.shipment.read";

    /// <summary>
    /// Pack a parcel, weigh it, book it with a courier, print its label and hand it over. The
    /// seller's daily work, and Operations doing it for them; one permission because it is one job.
    /// </summary>
    public const string ShipmentManage = "shipping.shipment.manage";

    /// <summary>
    /// Work the failed-delivery queue: reattempt, reschedule, correct an address, or send the goods
    /// back. A different decision from dispatching, and usually a different person.
    /// </summary>
    public const string NdrManage = "shipping.ndr.manage";

    /// <summary>
    /// Edit the delivery map and the rate card. What delivery costs is a commercial decision and is
    /// deliberately not the packer's.
    /// </summary>
    public const string ShippingRateManage = "shipping.rate.manage";

    /// <summary>
    /// Read the courier webhook log and its dead-letter queue, replay an event, refresh
    /// serviceability, and record a courier's cash remittance. The plumbing.
    /// </summary>
    public const string ShippingCourierManage = "shipping.courier.manage";

    /// <summary>
    /// List returns and read one, with its evidence. A read of a shopper's photographs and their
    /// words about a product, so it is a grant rather than something every operator has.
    /// </summary>
    public const string ReturnRead = "returns.return.read";

    /// <summary>
    /// Approve a return, refuse it, book its collection and close it. The daily work of the returns
    /// queue, and the permission a seller holds for their own goods.
    /// </summary>
    public const string ReturnManage = "returns.return.manage";

    /// <summary>
    /// Book a returned parcel in and grade what is in it. Deliberately separate and deliberately not
    /// a seller's: grading decides whether a shopper is refunded and whether a seller is charged.
    /// </summary>
    public const string ReturnQc = "returns.qc.manage";

    /// <summary>
    /// Pay a refund out of a return and raise the credit note that goes with it. Finance's rather
    /// than the queue's, and it sits on top of the refund approval threshold rather than replacing
    /// it.
    /// </summary>
    public const string ReturnRefund = "returns.refund.manage";

    /// <summary>
    /// Edit the return reason codes and the policy each carries. A commercial decision — who pays
    /// the freight, what is inspected, what is auto-approved — and staff-only.
    /// </summary>
    public const string ReturnReasonManage = "returns.reason.manage";

    /// <summary>
    /// Read settlement periods, ledger statements, payout batches and the statutory extracts. A
    /// seller holds it for their own account, confined by the vendor query filter.
    /// </summary>
    public const string SettlementRead = "settlements.settlement.read";

    /// <summary>
    /// Close a settlement period by hand and post an adjustment to a seller's ledger. Finance's, and
    /// deliberately not a seller's: an adjustment is a correction to somebody's money.
    /// </summary>
    public const string SettlementManage = "settlements.settlement.manage";

    /// <summary>
    /// Build a payout batch from closed periods, and abandon one nothing has left. The maker's half
    /// of the control — a large amount of power and no money, because an unapproved batch sends
    /// nothing.
    /// </summary>
    public const string PayoutManage = "settlements.payout.manage";

    /// <summary>
    /// Approve a payout batch and send it. The checker's half, and the one permission that moves
    /// money out of the platform. Holding it is necessary and not sufficient: the batch itself
    /// refuses an approval by the person who raised it.
    /// </summary>
    public const string PayoutApprove = "settlements.payout.approve";

    /// <summary>
    /// Read what shoppers have searched for, including the queries that found nothing. A buying
    /// decision rather than a technical one, and support's second-most-used report.
    /// </summary>
    public const string SearchQueryRead = "search.query.read";

    /// <summary>
    /// Edit the store's search synonyms and stop words. A large amount of power over what shoppers
    /// find, and deliberately a merchandiser's rather than an engineer's.
    /// </summary>
    public const string SearchVocabularyManage = "search.vocabulary.manage";

    /// <summary>
    /// Rebuild the search index and read its state. The one action here with a real operational
    /// cost, so it sits with the people who run the catalogue.
    /// </summary>
    public const string SearchIndexManage = "search.index.manage";

    /// <summary>
    /// Write pages, menus, banners and collections, and read any of them, published or not.
    /// </summary>
    /// <remarks>
    /// The merchandiser's permission. It covers everything about the storefront except putting a page
    /// live, which is deliberately somebody else's.
    /// </remarks>
    public const string ContentManage = "content.content.manage";

    /// <summary>
    /// Publish, schedule, unpublish, archive and roll back a page.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ContentManage"/>, and the only reason the editorial workflow means
    /// anything: an agency or a seasonal hire can be given the first without the second, and a page
    /// then cannot reach a shopper without somebody holding this looking at it.
    /// </remarks>
    public const string ContentPublish = "content.page.publish";

    /// <summary>
    /// Write a custom-HTML block.
    /// </summary>
    /// <remarks>
    /// Its own permission because it is its own risk: arbitrary markup executed in every shopper's
    /// browser is a stored cross-site-scripting vector, and the person who writes the store's copy is
    /// not automatically the person who may embed a script.
    /// </remarks>
    public const string ContentCustomHtmlWrite = "content.custom-html.write";

    /// <summary>
    /// Edit the redirect manager.
    /// </summary>
    /// <remarks>
    /// Routing rather than content. A wrong row here sends every visitor arriving on a good URL
    /// somewhere else, and it does so without anything on screen looking wrong to whoever wrote it.
    /// </remarks>
    public const string ContentRedirectManage = "content.redirect.manage";

    /// <summary>
    /// Read the sitemap index, the robots document and a page's structured data.
    /// </summary>
    /// <remarks>
    /// A read permission, because there is nothing here to write: the SEO settings belong to the
    /// Platform module and its own permission. It exists so the admin app's SEO screen can show what a
    /// crawler will be served without also granting the settings permission.
    /// </remarks>
    public const string ContentSeoRead = "content.seo.read";

    /// <summary>Every declared permission, in the order the admin UI lists them.</summary>
    public static readonly IReadOnlyList<PermissionDescriptor> All =
    [
        new(PlatformSettingsManage, "Platform", "Change store settings, branding and feature flags."),
        new(PlatformAuditRead, "Platform", "Read the audit trail."),
        new(IdentityUserRead, "Users & access", "List and view user accounts."),
        new(IdentityUserManage, "Users & access", "Create accounts, edit them, lock and unlock them."),
        new(IdentityRoleRead, "Users & access", "List roles and the permissions they grant."),
        new(IdentityRoleManage, "Users & access", "Create roles and change what they grant."),
        new(IdentityRoleAssign, "Users & access", "Grant and revoke a user's roles."),
        new(MediaFileRead, "Media", "Browse the media library."),
        new(MediaFileManage, "Media", "Upload files and delete them."),
        new(NotificationTemplateManage, "Notifications", "Edit the wording of transactional messages."),
        new(NotificationLogRead, "Notifications", "Read the delivery log and re-queue a message."),
        new(VendorRead, "Sellers", "List sellers and read one."),
        new(VendorManage, "Sellers", "Create sellers and edit their details, staff and settings."),
        new(VendorApprove, "Sellers", "Approve, activate, suspend and offboard a seller."),
        new(VendorKycVerify, "Sellers", "Accept or refuse KYC documents and bank-account checks."),
        new(VendorCommissionManage, "Sellers", "Edit commission plans and assign them to sellers."),
        new(CatalogTaxonomyRead, "Catalogue", "Browse categories, brands and attributes."),
        new(CatalogTaxonomyManage, "Catalogue", "Edit the category tree, brands and the attribute vocabulary."),
        new(CatalogProductRead, "Catalogue", "List products and variants, and read one."),
        new(CatalogProductManage, "Catalogue", "Create and edit products, variants and their galleries."),
        new(CatalogProductModerate, "Catalogue", "Approve, reject, publish and withdraw products."),
        new(CatalogListingRead, "Catalogue", "List sellers' offers and read one."),
        new(CatalogListingManage, "Catalogue", "Open offers and change their price and terms."),
        new(CatalogImportRun, "Catalogue", "Run a bulk catalogue import or export."),
        new(InventoryStockRead, "Inventory", "List stock levels and read a stock row's history."),
        new(InventoryStockAdjust, "Inventory", "Adjust stock by hand and set reorder levels."),
        new(InventoryWarehouseManage, "Inventory", "Open, rename and close stock locations."),
        new(InventoryPurchasingManage, "Inventory", "Keep suppliers, raise purchase orders and receive goods."),
        new(InventoryStockTakeManage, "Inventory", "Run stock takes and post their variances."),
        new(PricingPriceListRead, "Pricing", "List price lists and the prices in them."),
        new(PricingPriceListManage, "Pricing", "Open price lists, set prices and switch them on and off."),
        new(PricingTaxRateRead, "Pricing", "Read the GST rates and resolve one for a date."),
        new(PricingTaxRateManage, "Pricing", "Record and amend GST rates."),
        new(PricingPromotionRead, "Pricing", "List promotions and read their redemptions."),
        new(PricingPromotionManage, "Pricing", "Create promotions and switch them on and off."),
        new(PricingWalletRead, "Pricing", "Read a customer's store-credit balance and statement."),
        new(PricingWalletAdjust, "Pricing", "Credit or debit a customer's store credit by hand."),
        new(CartRead, "Baskets", "List shoppers' baskets and checkout sessions, and read one."),
        new(CartManage, "Baskets", "Retire a basket."),
        new(OrderRead, "Orders", "List orders and sub-orders, and read one in full."),
        new(OrderTransition, "Orders", "Move a sub-order along the lifecycle and write internal notes."),
        new(OrderCancel, "Orders", "Cancel a sub-order, in whole or in part."),
        new(InvoiceManage, "Orders", "Raise a tax invoice by hand and read any invoice."),
        new(PaymentRead, "Payments", "List payments, refunds and settlements, and read one in full."),
        new(PaymentManage, "Payments", "Re-read a payment from the gateway and capture an authorised one."),
        new(RefundInitiate, "Payments", "Raise a refund."),
        new(RefundApprove, "Payments", "Approve or refuse a refund above the threshold."),
        new(PaymentGatewayManage, "Payments", "Work the webhook log, import settlements and run reconciliation."),
        new(CodManage, "Payments", "Record cash taken at the door and a courier's remittance."),
        new(ShipmentRead, "Shipping", "List parcels and read one, with everywhere it has been."),
        new(ShipmentManage, "Shipping", "Pack, weigh, book, label and hand over parcels."),
        new(NdrManage, "Shipping", "Work the failed-delivery queue."),
        new(ShippingRateManage, "Shipping", "Edit the delivery zones and the rate card."),
        new(ShippingCourierManage, "Shipping", "Work the courier webhook log and record cash remittances."),
        new(ReturnRead, "Returns", "List returns and read one, with its evidence."),
        new(ReturnManage, "Returns", "Approve and refuse returns, book their collection, and close them."),
        new(ReturnQc, "Returns", "Book returned parcels in and grade what is in them."),
        new(ReturnRefund, "Returns", "Refund a return and raise its credit note."),
        new(ReturnReasonManage, "Returns", "Edit the return reason codes and the policy each carries."),
        new(SettlementRead, "Settlements", "Read settlement periods, ledgers, payouts and the tax extracts."),
        new(SettlementManage, "Settlements", "Close a settlement period and adjust a seller's ledger."),
        new(PayoutManage, "Settlements", "Build a payout batch from closed periods, and cancel one."),
        new(PayoutApprove, "Settlements", "Approve a payout batch and send the money."),
        new(SearchQueryRead, "Search", "Read what shoppers searched for, and what they did not find."),
        new(SearchVocabularyManage, "Search", "Edit the store's search synonyms and stop words."),
        new(SearchIndexManage, "Search", "Rebuild the search index and read its state."),
        new(ContentManage, "Content", "Write pages, menus, banners and collections, and read drafts."),
        new(ContentPublish, "Content", "Publish, schedule, unpublish, archive and roll back a page."),
        new(ContentCustomHtmlWrite, "Content", "Write custom HTML blocks."),
        new(ContentRedirectManage, "Content", "Edit the redirect manager."),
        new(ContentSeoRead, "Content", "Read the sitemap, the robots document and a page's structured data."),
    ];

    /// <summary>Whether a code is one this platform declares.</summary>
    /// <param name="code">The permission code.</param>
    public static bool Contains(string code)
        => All.Any(permission => string.Equals(permission.Code, code, StringComparison.Ordinal));
}

/// <summary>One role, as the platform defines it.</summary>
/// <param name="Code">The stable lowercase code.</param>
/// <param name="Name">The display name.</param>
/// <param name="Scope">Platform-wide, vendor-scoped, or a shopper.</param>
/// <param name="Description">What the role is for.</param>
/// <param name="Permissions">What it grants.</param>
internal sealed record RoleDescriptor(
    string Code,
    string Name,
    RoleScope Scope,
    string Description,
    IReadOnlyList<string> Permissions);

/// <summary>
/// The roles every deployment starts with, one per actor in docs/02-domain-model.md §1.
/// </summary>
/// <remarks>
/// <para>
/// System roles: their permission set is reasserted on every deploy, so editing one in the admin
/// UI would be silently undone. A deployment that wants a different bundle creates its own role —
/// which is exactly what "roles are data, not code" is for, and why the code path that creates
/// them is a first-class endpoint rather than a migration.
/// </para>
/// <para>
/// Several roles start empty. Operations, finance, merchandising and catalog management have no
/// permissions yet because the modules whose endpoints they would grant do not exist; the steps
/// that build those endpoints fill these bundles in. They are seeded now so that the users an
/// operator creates at Step 7 can already be filed under the right role.
/// </para>
/// </remarks>
internal static class SystemRoles
{
    /// <summary>Full control, including users, roles and settings.</summary>
    public const string PlatformAdmin = "platform-admin";

    /// <summary>Read and limited action, with audited impersonation.</summary>
    public const string Support = "support";

    /// <summary>Taxonomy and moderation.</summary>
    public const string CatalogManager = "catalog-manager";

    /// <summary>Orders, fulfilment and returns.</summary>
    public const string Operations = "operations";

    /// <summary>CMS, promotions and collections.</summary>
    public const string Merchandiser = "merchandiser";

    /// <summary>Settlements, payouts and reconciliation.</summary>
    public const string Finance = "finance";

    /// <summary>The legal owner of a seller account.</summary>
    public const string VendorOwner = "vendor-owner";

    /// <summary>A seller's operating staff.</summary>
    public const string VendorStaff = "vendor-staff";

    /// <summary>A registered shopper.</summary>
    public const string Customer = "customer";

    /// <summary>Every system role.</summary>
    public static readonly IReadOnlyList<RoleDescriptor> All =
    [
        new(
            PlatformAdmin,
            "Platform administrator",
            RoleScope.Platform,
            "Full control, including users, roles and store settings.",
            [.. PermissionCatalog.All.Select(permission => permission.Code)]),
        new(
            Support,
            "Support",
            RoleScope.Platform,
            "Reads customer and order data to answer queries; acts only where explicitly permitted.",
            [
                PermissionCatalog.IdentityUserRead,

                // "Did they get the email" is the second question of nearly every support
                // conversation, and it is a read of masked data.
                PermissionCatalog.NotificationLogRead,

                // Step 12. "Where did my store credit go" is the third. Reading a balance is a
                // read; changing one is deliberately not here, and belongs to Finance.
                PermissionCatalog.PricingWalletRead,

                // Step 13. "My basket says the wrong thing" is the first question of all, and it
                // cannot be answered without seeing the basket. Reading one is a read; editing it
                // is nobody's, and retiring it belongs to Operations.
                PermissionCatalog.CartRead,

                // Step 14. "Where is my order" is the question support exists to answer, and it
                // cannot be answered without reading the order and its timeline. Moving one is
                // Operations', and cancelling one is deliberately not support's at all.
                PermissionCatalog.OrderRead,

                // Step 15. "Did my payment go through" is asked as often as "where is my order",
                // and reading the payment is the only way to answer it. Repairing one is
                // Operations', raising a refund is Finance's, and neither is support's.
                PermissionCatalog.PaymentRead,

                // Step 16. "Where is my parcel" is the same call as "where is my order" and cannot
                // be answered from the order alone once a courier has it. Reading a parcel is a
                // read; packing, booking and working failed deliveries are all Operations'.
                PermissionCatalog.ShipmentRead,

                // Step 19. "Why can't I find X on your site" is a support call, and the query log is
                // the only thing that answers it. A read of what was typed and what came back.
                PermissionCatalog.SearchQueryRead,
            ]),
        new(
            CatalogManager,
            "Catalog manager",
            RoleScope.Platform,
            "Owns the taxonomy and moderates listings.",
            [
                PermissionCatalog.MediaFileRead,
                PermissionCatalog.MediaFileManage,

                // A listing belongs to a seller, so moderating one means being able to see which.
                PermissionCatalog.VendorRead,

                // Step 10 filled this bundle in. It is the whole catalogue surface: the taxonomy
                // is theirs to shape, and moderation is the decision this role exists to take.
                PermissionCatalog.CatalogTaxonomyRead,
                PermissionCatalog.CatalogTaxonomyManage,
                PermissionCatalog.CatalogProductRead,
                PermissionCatalog.CatalogProductManage,
                PermissionCatalog.CatalogProductModerate,
                PermissionCatalog.CatalogListingRead,
                PermissionCatalog.CatalogListingManage,
                PermissionCatalog.CatalogImportRun,

                // Their own stock, confined to their own locations by the vendor scope. A seller
                // runs their own warehouse and buys their own stock; the platform does not do it
                // for them.
                PermissionCatalog.InventoryStockRead,
                PermissionCatalog.InventoryStockAdjust,
                PermissionCatalog.InventoryWarehouseManage,
                PermissionCatalog.InventoryPurchasingManage,
                PermissionCatalog.InventoryStockTakeManage,

                // Step 19. The search index is a projection of the catalogue this role owns, so
                // rebuilding it after a bulk import — and reading whether it has fallen behind — is
                // their job. Editing the synonyms is the merchandiser's.
                PermissionCatalog.SearchIndexManage,
                PermissionCatalog.SearchQueryRead,
            ]),
        new(
            Operations,
            "Operations",
            RoleScope.Platform,
            "Runs orders, fulfilment and returns, and onboards sellers.",
            [
                // Onboarding is operations work: the queue of applications, the documents to read,
                // and the decision to let a seller trade. It deliberately does not include the
                // commission plan — what a seller is charged is Finance's.
                PermissionCatalog.VendorRead,
                PermissionCatalog.VendorManage,
                PermissionCatalog.VendorApprove,
                PermissionCatalog.VendorKycVerify,

                // Step 11. Stock is operations work end to end: the levels, the corrections, the
                // locations, the purchase orders and the recounts are all this team's, and none of
                // them belongs to the people who write the product copy.
                PermissionCatalog.InventoryStockRead,
                PermissionCatalog.InventoryStockAdjust,
                PermissionCatalog.InventoryWarehouseManage,
                PermissionCatalog.InventoryPurchasingManage,
                PermissionCatalog.InventoryStockTakeManage,

                // Step 13. Diagnosing a stuck basket and retiring one are operations work. Editing
                // a shopper's basket is not a permission that exists — for anybody.
                PermissionCatalog.CartRead,
                PermissionCatalog.CartManage,

                // Step 14. Orders are what this team runs: the queue, the transitions a seller has
                // not taken, the cancellation after dispatch that only they may make, and the
                // invoice that was missed. The whole surface, because there is no half of it they
                // can be asked to work without.
                PermissionCatalog.OrderRead,
                PermissionCatalog.OrderTransition,
                PermissionCatalog.OrderCancel,
                PermissionCatalog.InvoiceManage,

                // Step 15. The repair half of payments is operations work: re-reading a stuck
                // payment from the gateway, working the webhook dead-letter queue, and agreeing
                // cash with a courier. Raising and approving refunds is deliberately Finance's -
                // this team fixes plumbing, it does not decide to give money back.
                PermissionCatalog.PaymentRead,
                PermissionCatalog.PaymentManage,
                PermissionCatalog.PaymentGatewayManage,
                PermissionCatalog.CodManage,

                // Step 16. Fulfilment is what this team runs: the parcels a seller has not packed,
                // the failed deliveries somebody has to decide about, and the courier plumbing when
                // this platform and an aggregator disagree. The rate card is deliberately absent -
                // what delivery costs is a commercial decision and it is Finance's.
                PermissionCatalog.ShipmentRead,
                PermissionCatalog.ShipmentManage,
                PermissionCatalog.NdrManage,
                PermissionCatalog.ShippingCourierManage,
            ]),
        new(
            Merchandiser,
            "Merchandiser",
            RoleScope.Platform,
            "Owns content, promotions and collections.",
            [
                // Step 12 filled the promotion half of this bundle in. Campaigns are what this role
                // exists for: the codes, the cart rules, the windows, and the simulator that shows
                // what a basket would cost before the campaign goes live.
                PermissionCatalog.PricingPromotionRead,
                PermissionCatalog.PricingPromotionManage,

                // Platform-wide price lists, which is how a marketplace runs a sale that is not a
                // coupon. The GST rates are read-only to them: what a supply is taxed at is the
                // law's answer, not a merchandising decision.
                PermissionCatalog.PricingPriceListRead,
                PermissionCatalog.PricingPriceListManage,
                PermissionCatalog.PricingTaxRateRead,

                // A campaign is built out of the catalogue, so the taxonomy and the offers have to
                // be visible. Editing them is the catalog manager's.
                PermissionCatalog.CatalogTaxonomyRead,
                PermissionCatalog.CatalogProductRead,
                PermissionCatalog.CatalogListingRead,

                // Step 13. The abandoned-cart worklist is a merchandising instrument: what people
                // put down before paying is the most direct evidence a campaign has.
                PermissionCatalog.CartRead,

                // Step 19. What shoppers searched for is the other half of that evidence, and the
                // zero-result report is the most actionable list this platform produces. Editing the
                // vocabulary comes with it: the person who notices that nobody finds "settee" is this
                // role, and making them raise a ticket is how a store ends up with a search nobody
                // trusts. Rebuilding the index is deliberately absent — it has an operational cost
                // and belongs with the catalogue.
                PermissionCatalog.SearchQueryRead,
                PermissionCatalog.SearchVocabularyManage,

                // Step 20. The storefront itself: pages, menus, banners, collections and the SEO
                // surface. Publishing comes with it, because the role's description is "owns
                // content" and a merchandiser who could write a campaign page but not put it live
                // would be a merchandiser who needs somebody else present to run a sale. The
                // editorial split is still real — the permission exists and can be withheld from a
                // custom role built for an agency — it is simply not the split this role wants.
                PermissionCatalog.ContentManage,
                PermissionCatalog.ContentPublish,
                PermissionCatalog.ContentRedirectManage,
                PermissionCatalog.ContentSeoRead,

                // Custom HTML is deliberately absent. It is arbitrary markup in every shopper's
                // browser, and the person who writes the store's copy is not automatically the
                // person who may embed a script (docs/07-security-compliance.md §3). A deployment
                // that wants it grants it explicitly, to named people.
            ]),
        new(
            Finance,
            "Finance",
            RoleScope.Platform,
            "Owns settlements, payouts and reconciliation.",
            [
                PermissionCatalog.VendorRead,

                // What the platform charges a seller is a commercial decision, and it belongs to
                // the people who reconcile the money rather than the people who onboard.
                PermissionCatalog.VendorCommissionManage,

                // A payout goes to a bank account, so whoever owns payouts owns the check on it.
                PermissionCatalog.VendorKycVerify,

                // Step 12. The GST rate table is finance's: it decides what is collected and what
                // is remitted, and a wrong row is a filing problem rather than a display problem.
                PermissionCatalog.PricingTaxRateRead,
                PermissionCatalog.PricingTaxRateManage,

                // Store credit is a liability the business owes a customer, so issuing it is the
                // decision of the people who reconcile the money.
                PermissionCatalog.PricingWalletRead,
                PermissionCatalog.PricingWalletAdjust,

                // Prices are read-only to finance: they need to see what was charged, and changing
                // it is a merchandising or a seller decision.
                PermissionCatalog.PricingPriceListRead,
                PermissionCatalog.PricingPromotionRead,

                // Step 14. Reconciliation reads orders and the tax invoices raised against them —
                // what was supplied, under whose GSTIN, at what tax. Moving or cancelling one is
                // operations work and deliberately absent.
                PermissionCatalog.OrderRead,
                PermissionCatalog.InvoiceManage,

                // Step 15. Money going back out is finance's decision, and the settlement reports
                // are what they reconcile a bank statement against. Both halves of maker-checker
                // are granted to the role and the control still holds: the handler and a check
                // constraint both refuse a refund approved by the person who raised it, so two
                // people in this role are needed rather than two roles.
                PermissionCatalog.PaymentRead,
                PermissionCatalog.RefundInitiate,
                PermissionCatalog.RefundApprove,

                // Step 16. What delivery costs a customer is a commercial decision, and it belongs
                // with the people who reconcile what it costs the platform. Reading parcels comes
                // with it: the freight charged and the freight billed are both on the parcel, and
                // the difference is the margin they are answerable for.
                PermissionCatalog.ShippingRateManage,
                PermissionCatalog.ShipmentRead,

                // Step 17. A refund out of a return is money going back, and it is the same decision
                // as any other refund; reading the return is what makes it possible to check the
                // credit note against the invoice it credits. Approving and grading returns is
                // Operations' work and deliberately absent.
                PermissionCatalog.ReturnRead,
                PermissionCatalog.ReturnRefund,

                // Step 18. The role's whole description. Both halves of maker-checker are granted and
                // the control still holds: the aggregate and a check constraint both refuse a batch
                // approved by the person who raised it, so two people in this role are needed rather
                // than two roles - exactly the arrangement the refund threshold already uses.
                PermissionCatalog.SettlementRead,
                PermissionCatalog.SettlementManage,
                PermissionCatalog.PayoutManage,
                PermissionCatalog.PayoutApprove,
            ]),
        new(
            VendorOwner,
            "Vendor owner",
            RoleScope.Vendor,
            "The legal owner of a seller account. Manages that seller's staff and its listings.",
            [
                PermissionCatalog.IdentityUserRead,
                PermissionCatalog.IdentityUserManage,
                PermissionCatalog.IdentityRoleAssign,

                // Their own record, and only their own: the vendor scope in their token decides
                // which seller these apply to, and no route takes a seller id they could change.
                // Notably absent is vendors.vendor.approve — a seller does not approve themselves.
                PermissionCatalog.VendorRead,
                PermissionCatalog.VendorManage,

                // Their own catalogue. The taxonomy is read-only to them — it is shared with every
                // other seller — and catalog.product.moderate is deliberately absent: a seller does
                // not approve their own listing.
                PermissionCatalog.CatalogTaxonomyRead,
                PermissionCatalog.CatalogProductRead,
                PermissionCatalog.CatalogProductManage,
                PermissionCatalog.CatalogListingRead,
                PermissionCatalog.CatalogListingManage,
                PermissionCatalog.CatalogImportRun,

                // Step 12. Their own price lists, confined to their own seller by the vendor scope.
                // A seller discounts their own goods with a price list; a marketplace-wide coupon
                // is the platform's, and pricing.promotion.* is deliberately absent.
                PermissionCatalog.PricingPriceListRead,
                PermissionCatalog.PricingPriceListManage,

                // Step 14. Their own orders, confined to their own sub-orders by the vendor scope.
                // They fulfil, they cancel what they cannot fulfil, and they read their own tax
                // invoices — which are raised under their GSTIN and are their statutory record.
                PermissionCatalog.OrderRead,
                PermissionCatalog.OrderTransition,
                PermissionCatalog.OrderCancel,
                PermissionCatalog.InvoiceManage,

                // Step 16. Their own parcels, confined by the vendor scope, and their own rate
                // overrides - a seller who has negotiated their own delivery pricing sets it here.
                // The platform-wide card is not theirs, and the vendor filter is what enforces that.
                PermissionCatalog.ShipmentRead,
                PermissionCatalog.ShipmentManage,
                PermissionCatalog.NdrManage,
                PermissionCatalog.ShippingRateManage,

                // Step 17. Returns against their own goods: they see them, approve them and refuse
                // them. Grading what came back is deliberately absent - a seller may not decide the
                // condition of goods they are about to be charged for - and so is the refund.
                PermissionCatalog.ReturnRead,
                PermissionCatalog.ReturnManage,

                // Step 18. Their own statement and their own payouts, confined by the vendor query
                // filter in the data layer. Reading only: closing a period, adjusting a ledger and
                // approving a payout are all the platform's, and a seller who could do any of them
                // would be deciding what they are paid.
                PermissionCatalog.SettlementRead,
            ]),
        new(
            VendorStaff,
            "Vendor staff",
            RoleScope.Vendor,
            "Operates one seller's listings and orders.",
            [
                PermissionCatalog.VendorRead,
                PermissionCatalog.CatalogTaxonomyRead,
                PermissionCatalog.CatalogProductRead,
                PermissionCatalog.CatalogProductManage,
                PermissionCatalog.CatalogListingRead,
                PermissionCatalog.CatalogListingManage,

                // Staff pick, pack and count. Notably absent are the location list and the
                // purchasing surface: opening a warehouse and committing the seller's money to a
                // supplier are the owner's decisions.
                PermissionCatalog.InventoryStockRead,
                PermissionCatalog.InventoryStockAdjust,
                PermissionCatalog.InventoryStockTakeManage,

                // Staff see what their seller charges but do not set it. Changing a price is a
                // commercial decision and it is the owner's.
                PermissionCatalog.PricingPriceListRead,

                // Step 14. Staff pick, pack and hand over. Cancelling an order the seller has
                // accepted is a commercial decision that costs the seller a performance mark, so it
                // is the owner's — as is raising a tax invoice by hand.
                PermissionCatalog.OrderRead,
                PermissionCatalog.OrderTransition,

                // Step 16. Staff pack, weigh, book and hand over - that is the whole of the job this
                // role exists for. Working a failed delivery is a conversation with the customer and
                // is the owner's, as is anything to do with what delivery costs.
                PermissionCatalog.ShipmentRead,
                PermissionCatalog.ShipmentManage,
            ]),
        new(
            Customer,
            "Customer",
            RoleScope.Customer,
            "A registered shopper. Carries no administrative permission at all.",
            []),
    ];
}
