using KlaraHome.Infrastructure.Authorization;

namespace KlaraHome.Modules.Carts.Infrastructure;

/// <summary>
/// Answers "whose basket is this request about".
/// </summary>
/// <remarks>
/// <para>
/// The one rule this module enforces above all others: a shopper reads and edits their own basket
/// and nobody else's, and the identity comes from the token or the cookie rather than from
/// anything in the request. There is no route on the storefront that takes a cart id, and that is
/// not an oversight — an id in a path is an id a caller can change.
/// </para>
/// <para>
/// The sibling of <c>PricingScope</c>, <c>CatalogScope</c> and <c>InventoryScope</c>. This one has
/// no vendor half, because a basket is never a seller's.
/// </para>
/// </remarks>
/// <param name="caller">The signed-in caller, when there is one.</param>
internal sealed class CartsScope(ICallerContext caller)
{
    /// <summary>The signed-in shopper, or null for a browser that has not signed in.</summary>
    public Guid? CustomerId => caller.UserId;

    /// <summary>Whether the caller has signed in at all.</summary>
    public bool IsSignedIn => caller.IsAuthenticated && caller.UserId is not null;

    /// <summary>The cohort the caller belongs to, for a segment-scoped campaign on their basket.</summary>
    public string? Segment => caller.Segment;
}
