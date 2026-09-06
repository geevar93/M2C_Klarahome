namespace KlaraHome.Modules.Catalog.Endpoints;

/// <summary>
/// The permissions this module's endpoints declare.
/// </summary>
/// <remarks>
/// Declared here and mirrored in the Identity module's permission catalogue, which is what the
/// admin UI lists and what a role grants. The duplication is deliberate and is what the module
/// boundary costs: a module may not reference another module, so the two lists are kept in step by
/// a test that asserts every permission an endpoint asks for appears in the catalogue — the same
/// arrangement the Media and Vendors modules use.
/// </remarks>
internal static class CatalogPermissions
{
    /// <summary>Read the taxonomy: categories, brands, attributes and attribute sets.</summary>
    public const string TaxonomyRead = "catalog.taxonomy.read";

    /// <summary>
    /// Change the taxonomy. Platform staff only — the tree, the brands and the attribute
    /// vocabulary are the platform's, and a seller reshaping them would reshape every other
    /// seller's products too.
    /// </summary>
    public const string TaxonomyManage = "catalog.taxonomy.manage";

    /// <summary>List products and variants, and read one.</summary>
    public const string ProductRead = "catalog.product.read";

    /// <summary>
    /// Create and edit products and variants. Held by platform staff and by a seller, who is
    /// confined to their own by the vendor scope.
    /// </summary>
    public const string ProductManage = "catalog.product.manage";

    /// <summary>
    /// Approve or reject a submitted product, and publish or withdraw one. Platform staff only — a
    /// seller holding this could approve their own listing, which is the whole point of moderation.
    /// </summary>
    public const string ProductModerate = "catalog.product.moderate";

    /// <summary>List offers and read one.</summary>
    public const string ListingRead = "catalog.listing.read";

    /// <summary>Open an offer, change its price and terms, and pause or resume it.</summary>
    public const string ListingManage = "catalog.listing.manage";

    /// <summary>Queue a bulk import or export, and read its report.</summary>
    public const string ImportRun = "catalog.import.run";
}
