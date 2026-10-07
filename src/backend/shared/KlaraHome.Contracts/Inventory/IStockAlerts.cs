namespace KlaraHome.Contracts.Inventory;

/// <summary>
/// Counts stock that has fallen to its reorder level, from outside the Inventory module
/// (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// Added for the admin dashboard's "low stock" figure (ADMIN_UX_GAPS H2). Whether a stock line is
/// low depends on its reorder level, which is a setting on the Inventory row and not a fact any
/// integration event carries, so Reporting - which keeps only the facts events deliver (ADR-021) -
/// cannot work it out and may not read the table. Inventory answers it.
/// </para>
/// <para>
/// A count and nothing else. A caller that wants the rows asks the stock list, which has the filter.
/// For a seller's own token the count is confined to their own stock by the same query filter every
/// other Inventory read goes through.
/// </para>
/// </remarks>
public interface IStockAlerts
{
    /// <summary>
    /// How many stock lines are at or below a reorder level that is switched on.
    /// </summary>
    /// <remarks>
    /// The same definition as the stock list's low-stock filter: a reorder level above zero and
    /// <c>on hand - reserved</c> at or below it. A line with the alert disabled (level zero) is never low.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountLowStockAsync(CancellationToken cancellationToken = default);
}
