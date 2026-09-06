namespace KlaraHome.Modules.Pricing.Endpoints;

/// <summary>
/// The permissions this module's endpoints declare.
/// </summary>
/// <remarks>
/// Declared here and mirrored in the Identity module's permission catalogue, which is what the admin
/// UI lists and what a role grants. The duplication is deliberate and is what the module boundary
/// costs: a module may not reference another module, so the two lists are kept in step by a test
/// that asserts every permission an endpoint asks for appears in the catalogue — the same
/// arrangement the Media, Vendors, Catalog and Inventory modules use.
/// </remarks>
internal static class PricingPermissions
{
    /// <summary>List price lists, read one, and read the prices in it.</summary>
    public const string PriceListRead = "pricing.price-list.read";

    /// <summary>Open price lists, set prices in them, and switch them on and off.</summary>
    public const string PriceListManage = "pricing.price-list.manage";

    /// <summary>Read the GST rates and resolve one for a date.</summary>
    public const string TaxRateRead = "pricing.tax-rate.read";

    /// <summary>
    /// Record and amend GST rates.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="TaxRateRead"/>, and deliberately narrow: the rate table decides what
    /// every customer is charged and what the business remits, and it is the one thing in this
    /// module that a merchandiser has no business editing.
    /// </remarks>
    public const string TaxRateManage = "pricing.tax-rate.manage";

    /// <summary>List promotions, read one, and read its redemptions.</summary>
    public const string PromotionRead = "pricing.promotion.read";

    /// <summary>Create and edit promotions, and switch them on and off.</summary>
    public const string PromotionManage = "pricing.promotion.manage";

    /// <summary>Read a customer's store-credit balance and statement.</summary>
    public const string WalletRead = "pricing.wallet.read";

    /// <summary>
    /// Adjust a customer's store credit by hand.
    /// </summary>
    /// <remarks>
    /// The only permission on this platform that creates money out of nothing, which is why it is
    /// its own grant rather than part of <see cref="WalletRead"/> and why every use of it is
    /// audited with the actor.
    /// </remarks>
    public const string WalletAdjust = "pricing.wallet.adjust";
}
