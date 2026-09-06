using KlaraHome.Infrastructure.Authorization;

namespace KlaraHome.Modules.Payments.Infrastructure;

/// <summary>
/// Answers "who is asking, and what may they see of the money".
/// </summary>
/// <remarks>
/// <para>
/// Two callers reach this module and they see two different things. A shopper sees the payment
/// against their own order, found by <em>(order, customer)</em> so an id belonging to somebody else
/// simply does not resolve. Platform staff see all of it, gated by permission.
/// </para>
/// <para>
/// There is deliberately no third. A seller has no vendor scope here because a payment has no seller
/// (see <c>PaymentsDbContext</c>): the money is collected against an order that may span two of
/// them. What a seller may see about money is their settlement, which is Step 18's and derived from
/// these rows rather than joined to them.
/// </para>
/// </remarks>
/// <param name="caller">The signed-in caller.</param>
internal sealed class PaymentsScope(ICallerContext caller)
{
    /// <summary>The signed-in shopper, or null when nobody is signed in.</summary>
    public Guid? CustomerId => caller.UserId;

    /// <summary>Whether the caller has signed in at all.</summary>
    public bool IsSignedIn => caller.IsAuthenticated && caller.UserId is not null;

    /// <summary>The user to attribute a refund or a cash record to.</summary>
    public Guid? ActorId => caller.UserId;
}
