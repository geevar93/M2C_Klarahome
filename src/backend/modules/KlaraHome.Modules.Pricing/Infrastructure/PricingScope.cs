using KlaraHome.Infrastructure.Authorization;

namespace KlaraHome.Modules.Pricing.Infrastructure;

/// <summary>
/// Answers "may this caller write to this row".
/// </summary>
/// <remarks>
/// <para>
/// Reads of price lists are already handled: <c>PriceList</c> is
/// <see cref="KlaraHome.SharedKernel.Domain.IVendorScoped"/>, so the global query filter shows a
/// vendor caller their own lists and the platform's shared ones and nobody else's. Writes need one
/// more rule a query filter cannot express — a seller may edit their own list but must not edit a
/// <em>platform</em> list, even though they can see it — and that rule lives here, once, rather
/// than in each handler that takes an id from a route.
/// </para>
/// <para>
/// The same arrangement as <c>CatalogScope</c> and <c>InventoryScope</c>, and deliberately so: two
/// modules answering the same question two different ways is how a seller ends up able to do in one
/// what they cannot do in the other.
/// </para>
/// </remarks>
/// <param name="caller">The signed-in caller, for their vendor scope.</param>
internal sealed class PricingScope(ICallerContext caller)
{
    /// <summary>Whether the caller is confined to one seller.</summary>
    public bool IsVendorCaller => caller.VendorId is not null;

    /// <summary>The seller the caller is confined to, or null for platform staff.</summary>
    public Guid? CallerVendorId => caller.VendorId;

    /// <summary>The signed-in shopper, when the caller is one.</summary>
    public Guid? CustomerId => caller.UserId;

    /// <summary>The cohort the caller belongs to, for a segment-scoped campaign.</summary>
    public string? Segment => caller.Segment;

    /// <summary>Whether the caller may write to a row owned by <paramref name="ownerVendorId"/>.</summary>
    /// <param name="ownerVendorId">The row's owning seller, or null for a platform-owned row.</param>
    public bool CanWrite(Guid? ownerVendorId)
        => caller.VendorId is not { } scoped || ownerVendorId == scoped;

    /// <summary>The seller a newly created row belongs to: the caller's own, or the one staff named.</summary>
    /// <param name="requested">The seller named in the request, if any.</param>
    public Guid? OwnerFor(Guid? requested) => caller.VendorId ?? requested;
}
