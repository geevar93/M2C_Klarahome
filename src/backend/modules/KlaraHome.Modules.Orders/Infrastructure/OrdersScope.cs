using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Endpoints;

namespace KlaraHome.Modules.Orders.Infrastructure;

/// <summary>
/// Answers "who is asking, and what may they do to this order".
/// </summary>
/// <remarks>
/// <para>
/// Three callers reach this module and they see three different things. A shopper sees their own
/// orders, found by <em>(order, customer)</em> so an id belonging to somebody else simply does not
/// resolve. A seller sees their own sub-orders, and the vendor query filter — not this class — is
/// what enforces that: a query that forgot to say so returns nothing rather than everyone's. Platform
/// staff see all of it, gated by permission.
/// </para>
/// <para>
/// <see cref="Actor"/> is the half that matters to the state machine. It is derived from the caller's
/// claims and never from the request, so nobody can nominate themselves as
/// <see cref="OrderActor.System"/> — the states only the platform's own webhooks and sweepers may
/// drive are unreachable from any endpoint.
/// </para>
/// </remarks>
/// <param name="caller">The signed-in caller.</param>
internal sealed class OrdersScope(ICallerContext caller)
{
    /// <summary>The signed-in shopper, or null when nobody is signed in.</summary>
    public Guid? CustomerId => caller.UserId;

    /// <summary>The seller this caller acts for, or null for a shopper or platform staff.</summary>
    public Guid? VendorId => caller.VendorId;

    /// <summary>Whether the caller has signed in at all.</summary>
    public bool IsSignedIn => caller.IsAuthenticated && caller.UserId is not null;

    /// <summary>Whether the caller acts for a seller.</summary>
    public bool IsVendor => caller.VendorId is not null;

    /// <summary>
    /// What class of actor the caller is, for the transition table.
    /// </summary>
    /// <remarks>
    /// A vendor caller is a vendor even when they hold a platform permission — the vendor scope in
    /// the token is what decides which rows exist for them at all, and letting the permission promote
    /// them would let a seller take an edge reserved for Operations on their own sub-order.
    /// </remarks>
    public OrderActor Actor
        => caller.VendorId is not null
            ? OrderActor.Vendor
            : caller.HasPermission(OrdersPermissions.OrderTransition)
                ? OrderActor.Platform
                : OrderActor.Customer;

    /// <summary>The user to attribute a timeline entry to.</summary>
    public Guid? ActorId => caller.UserId;
}
