using KlaraHome.Infrastructure.Authorization;

namespace KlaraHome.Modules.Shipping.Infrastructure;

/// <summary>
/// Answers "who is asking, and whose parcels may they see".
/// </summary>
/// <remarks>
/// <para>
/// Unlike Payments, this module does have a seller surface, and it is the one that matters most in
/// daily use: a seller packs their own parcels, prints their own labels and works their own failed
/// deliveries. The vendor query filter does the confining, exactly as it does in Catalog, Inventory
/// and Orders; this type is what a handler asks when it needs to know whether a caller is a seller
/// at all.
/// </para>
/// <para>
/// There is no shopper surface here beyond serviceability, which is anonymous. A customer tracks
/// their parcel through their order, where the timeline already carries every courier scan — a
/// second tracking screen reading this schema would be a second answer to the same question.
/// </para>
/// </remarks>
/// <param name="caller">The signed-in caller.</param>
internal sealed class ShippingScope(ICallerContext caller)
{
    /// <summary>The seller the caller acts for, or null for platform staff.</summary>
    public Guid? VendorId => caller.VendorId;

    /// <summary>Whether the caller is acting for one seller rather than for the platform.</summary>
    public bool IsVendor => caller.VendorId is not null;

    /// <summary>The user to attribute a packing decision or an NDR action to.</summary>
    public Guid? ActorId => caller.UserId;
}
