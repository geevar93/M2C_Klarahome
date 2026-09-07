namespace KlaraHome.Contracts.Settlements;

/// <summary>
/// Tells the module that owns payouts that one of them may have moved
/// (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The gateway's webhook endpoint belongs to Payments — one signing secret, one replay-protected
/// event table, one URL registered with the gateway — but a <c>transfer.processed</c> event is about
/// a seller payout, which is Settlements'. This is the one line between them, and it is deliberately
/// as thin as it can be: an identifier and nothing else.
/// </para>
/// <para>
/// It says "look again", not "it succeeded". The implementation re-fetches the transfer from the
/// gateway and applies whatever the gateway says, which is the same rule every payment webhook here
/// already follows: a webhook body is an unauthenticated claim that happens to be signed, and the
/// signature proves who sent it rather than that its contents are current. Passing the payload
/// across would make the claim itself the thing that moved money.
/// </para>
/// <para>
/// Without it a payout's outcome is learnt only by the fifteen-minute reconciliation sweep — which
/// still runs, and is still what covers a webhook that is never delivered.
/// </para>
/// </remarks>
public interface IPayoutOutcomes
{
    /// <summary>
    /// Re-reads one transfer from the gateway and applies its current state. An id that names no
    /// in-flight payout is a no-op.
    /// </summary>
    /// <param name="providerPayoutId">The gateway's id for the transfer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether a payout item actually moved as a result.</returns>
    Task<bool> RefreshAsync(string providerPayoutId, CancellationToken cancellationToken = default);
}
