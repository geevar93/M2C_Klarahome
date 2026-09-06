namespace KlaraHome.Modules.Orders.Endpoints;

/// <summary>
/// The permissions this module's endpoints declare.
/// </summary>
/// <remarks>
/// <para>
/// Declared here and mirrored in the Identity module's permission catalogue, which is what the admin
/// UI lists and what a role grants. The duplication is deliberate and is what the module boundary
/// costs: a module may not reference another module, so the two lists are kept in step by a test
/// that asserts every permission an endpoint asks for appears in the catalogue — the same
/// arrangement Media, Vendors, Catalog, Inventory, Pricing and Cart use.
/// </para>
/// <para>
/// The storefront surface takes none. A shopper's authority over their own order comes from the
/// token that names them, and every storefront handler looks an order up by <em>(order, customer)</em>
/// — a permission on <c>GET /store/orders</c> would be a permission every customer would have to be
/// given.
/// </para>
/// <para>
/// Four permissions, and the split between them is the split between seeing, moving, stopping and
/// invoicing. They are deliberately not one: reading an order is what support does all day, moving
/// one is a seller's daily work, cancelling after dispatch takes a parcel back off a courier, and
/// raising a tax invoice by hand creates a statutory document.
/// </para>
/// </remarks>
internal static class OrdersPermissions
{
    /// <summary>List orders and sub-orders, and read one in full.</summary>
    /// <remarks>
    /// A read of personal data — what somebody bought, where it went and what they paid — so it is a
    /// grant rather than something every operator has. Vendor callers hold it too, and the vendor
    /// query filter is what confines them to their own sub-orders.
    /// </remarks>
    public const string OrderRead = "orders.order.read";

    /// <summary>Move a sub-order along the lifecycle, and write internal notes.</summary>
    /// <remarks>
    /// The seller's daily work — accept, pick, pack, hand over — and Operations doing it on their
    /// behalf. Holding it is also what makes a platform caller <c>Platform</c> rather than
    /// <c>Customer</c> to the transition table, so the edges reserved for Operations are unreachable
    /// without it.
    /// </remarks>
    public const string OrderTransition = "orders.order.transition";

    /// <summary>Cancel a sub-order, in whole or in part.</summary>
    /// <remarks>
    /// Separate from moving one because it is the transition that moves money: it reverses a coupon,
    /// gives back store credit and puts units back on sale, and after dispatch it recalls a parcel
    /// from a courier. A seller needs it for what they cannot fulfil; not every operator does.
    /// </remarks>
    public const string OrderCancel = "orders.order.cancel";

    /// <summary>Raise a tax invoice by hand, and read any invoice.</summary>
    /// <remarks>
    /// A statutory document with a gapless number. The automatic issue at dispatch needs no
    /// permission because nobody asked for it; this is for the operator who has to raise one early
    /// or repair one that was missed.
    /// </remarks>
    public const string InvoiceManage = "orders.invoice.manage";
}
