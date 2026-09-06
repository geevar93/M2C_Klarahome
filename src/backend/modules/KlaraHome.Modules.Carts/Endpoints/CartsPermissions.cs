namespace KlaraHome.Modules.Carts.Endpoints;

/// <summary>
/// The permissions this module's endpoints declare.
/// </summary>
/// <remarks>
/// <para>
/// Declared here and mirrored in the Identity module's permission catalogue, which is what the
/// admin UI lists and what a role grants. The duplication is deliberate and is what the module
/// boundary costs: a module may not reference another module, so the two lists are kept in step by
/// a test that asserts every permission an endpoint asks for appears in the catalogue — the same
/// arrangement Media, Vendors, Catalog, Inventory and Pricing use.
/// </para>
/// <para>
/// There are only two, and both are platform-side. The storefront surface takes none: a shopper's
/// authority over their own basket comes from holding the token or the cookie that names it, not
/// from a grant, and a permission on <c>GET /store/cart</c> would be a permission every customer
/// would have to be given.
/// </para>
/// </remarks>
internal static class CartsPermissions
{
    /// <summary>List baskets and checkout sessions, and read one.</summary>
    /// <remarks>
    /// A read of a shopper's basket is a read of personal data — what somebody is about to buy —
    /// so it is a grant rather than something every operator has. It is what support needs to
    /// answer "my basket says the wrong thing", and what merchandising needs to see what is being
    /// abandoned.
    /// </remarks>
    public const string CartRead = "carts.cart.read";

    /// <summary>
    /// Retire a basket.
    /// </summary>
    /// <remarks>
    /// The only write an operator has over somebody else's basket, and deliberately the only one:
    /// there is no endpoint that adds, removes or reprices a line on a shopper's behalf. An
    /// operator who could edit a basket could change what a customer is about to pay, and no
    /// support question is worth that.
    /// </remarks>
    public const string CartManage = "carts.cart.manage";
}
