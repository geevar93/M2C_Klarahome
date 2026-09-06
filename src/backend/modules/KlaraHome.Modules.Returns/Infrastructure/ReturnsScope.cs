using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Endpoints;

namespace KlaraHome.Modules.Returns.Infrastructure;

/// <summary>
/// Answers "who is asking, and what may they do about a return".
/// </summary>
/// <remarks>
/// <para>
/// This module has all three surfaces, which is unusual and is the shape of the problem: a shopper
/// raises a return and can withdraw it, a seller sees the ones against their own goods and can
/// approve or refuse them, and platform staff do everything including the one thing neither of the
/// others may — grade what came back.
/// </para>
/// <para>
/// <see cref="Actor"/> is what the transition table is asked with, and it resolves in the order that
/// gives the least power: a caller carrying a vendor id is a seller even if they also hold a
/// platform permission, because a seller acting on their own returns must never be able to grade
/// goods they are about to be charged for.
/// </para>
/// </remarks>
/// <param name="caller">The signed-in caller.</param>
internal sealed class ReturnsScope(ICallerContext caller)
{
    /// <summary>The shopper, when the caller is one.</summary>
    public Guid? CustomerId => caller.UserId;

    /// <summary>The seller the caller acts for, or null for platform staff and shoppers.</summary>
    public Guid? VendorId => caller.VendorId;

    /// <summary>Whether the caller is signed in at all.</summary>
    public bool IsSignedIn => caller.IsAuthenticated && caller.UserId is not null;

    /// <summary>Whether the caller is acting for one seller rather than for the platform.</summary>
    public bool IsVendor => caller.VendorId is not null;

    /// <summary>The user to attribute an approval, an inspection or a refusal to.</summary>
    public Guid? ActorId => caller.UserId;

    /// <summary>
    /// Who the transition table should treat this caller as.
    /// </summary>
    /// <remarks>
    /// A seller first, then anybody holding the permission that governs the return queue, then the
    /// shopper. <see cref="ReturnActor.System"/> is deliberately unreachable from here — it is what
    /// a courier scan and a background job transition as, and no HTTP caller may claim it.
    /// </remarks>
    public ReturnActor Actor
        => caller.VendorId is not null
            ? ReturnActor.Vendor
            : caller.HasPermission(ReturnsPermissions.ReturnManage)
                ? ReturnActor.Platform
                : ReturnActor.Customer;
}
