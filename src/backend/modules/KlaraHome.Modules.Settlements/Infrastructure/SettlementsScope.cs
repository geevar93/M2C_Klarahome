using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Endpoints;

namespace KlaraHome.Modules.Settlements.Infrastructure;

/// <summary>
/// Answers "who is asking, and what may they do about money owed to a seller".
/// </summary>
/// <remarks>
/// <para>
/// Two surfaces rather than the three the Returns module has, because a shopper has no place in a
/// settlement. A seller reads their own account; platform finance reads everybody's and is the only
/// one who moves anything.
/// </para>
/// <para>
/// <see cref="VendorId"/> is not used to filter a query anywhere in this module, and that is the
/// point of it: the vendor query filter in the data layer already confines a seller to their own
/// rows, so this exists to answer the questions the filter cannot — whether a vendor id in a request
/// may be honoured, and which buttons an admin screen should draw.
/// </para>
/// </remarks>
/// <param name="caller">The signed-in caller.</param>
internal sealed class SettlementsScope(ICallerContext caller)
{
    /// <summary>The seller the caller acts for, or null for platform staff.</summary>
    public Guid? VendorId => caller.VendorId;

    /// <summary>Whether the caller is acting for one seller rather than for the platform.</summary>
    public bool IsVendor => caller.VendorId is not null;

    /// <summary>The user to attribute a closing, an adjustment or an approval to.</summary>
    public Guid? ActorId => caller.UserId;

    /// <summary>
    /// The seller a request is about: the caller's own where they have one, the asked-for one
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// A seller's own id always wins over a parameter, so a vendor caller who names somebody else's
    /// id gets their own account rather than a refusal. It is the same rule the vendor query filter
    /// enforces underneath, stated once here so a list endpoint's filter and the filter that
    /// actually confines it cannot disagree.
    /// </remarks>
    /// <param name="requested">The vendor id in the request, if any.</param>
    public Guid? VendorFilter(Guid? requested) => caller.VendorId ?? requested;

    /// <summary>
    /// Who the payout transition table should treat this caller as.
    /// </summary>
    /// <remarks>
    /// Both halves are granted where both permissions are held, and the aggregate still refuses a
    /// self-approval — which is what makes granting both to the finance role safe: two people in
    /// that role are needed, rather than two roles. <see cref="PayoutActor.System"/> is deliberately
    /// unreachable from here; it is what a gateway's answer and a background job move a batch as, and
    /// no HTTP caller may claim it.
    /// </remarks>
    public PayoutActor Actor
    {
        get
        {
            var actor = PayoutActor.None;

            if (caller.HasPermission(SettlementsPermissions.PayoutManage))
            {
                actor |= PayoutActor.Maker;
            }

            if (caller.HasPermission(SettlementsPermissions.PayoutApprove))
            {
                actor |= PayoutActor.Checker;
            }

            return actor;
        }
    }
}
