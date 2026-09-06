namespace KlaraHome.Contracts.Inventory;

/// <summary>What is happening to units that have come back.</summary>
/// <remarks>
/// Three outcomes rather than two, because "we have it and it is saleable" and "we have it and it is
/// not" are different facts about the same box, and a platform that recorded only the first would
/// have no way to explain where the missing units went at a stock take.
/// </remarks>
public enum RestockDisposition
{
    /// <summary>Saleable. The units go back on supply and can be sold again.</summary>
    Restock = 0,

    /// <summary>Not saleable and not worth keeping. The units are written off supply.</summary>
    Scrap = 1,

    /// <summary>
    /// Held pending a decision — a warranty claim, a supplier credit, a second opinion.
    /// </summary>
    /// <remarks>
    /// Deliberately moves no stock at all. Quarantined goods are physically present and commercially
    /// undecided, and putting them back on sale or writing them off would both be a lie.
    /// </remarks>
    Quarantine = 2,
}

/// <summary>Units of one offer coming back, and what is to become of them.</summary>
/// <param name="ListingId">The offer. It is what a stock row is keyed by.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="Disposition">What is happening to them.</param>
public sealed record RestockUnits(Guid ListingId, int Quantity, RestockDisposition Disposition);

/// <summary>
/// Puts units back on supply, or writes them off (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The seam Returns reaches inventory through. It exists because stock is only ever moved by the
/// module that owns the ledger: <c>inventory.stock_ledger_entries</c> is append-only and every
/// movement in it names its source document, and a second writer would be a second, unreconcilable
/// account of what this platform has.
/// </para>
/// <para>
/// It is deliberately narrow — one direction, one caller's worth of vocabulary. There is no
/// "take stock out" method here, because taking stock out is what a sale does and a sale already has
/// its own path through <see cref="IStockAvailability"/>.
/// </para>
/// <para>
/// Idempotent on <c>(referenceType, referenceId)</c>. A redelivered QC event, a retried request and
/// an operator clicking twice must all move the units once, so the implementation records the
/// reference on the ledger entry and refuses a second movement under the same one.
/// </para>
/// </remarks>
public interface IStockRestock
{
    /// <summary>
    /// Moves units back onto supply, off it, or neither.
    /// </summary>
    /// <param name="units">The offers, quantities and dispositions.</param>
    /// <param name="referenceType">What is causing the movement — <c>return</c>, <c>rto</c>.</param>
    /// <param name="referenceId">The document causing it, which makes the movement idempotent.</param>
    /// <param name="note">What the ledger entry should say.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many units were actually moved. Zero means it had already happened.</returns>
    ValueTask<int> RestockAsync(
        IReadOnlyCollection<RestockUnits> units,
        string referenceType,
        Guid referenceId,
        string? note = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The documents that cause stock to come back.</summary>
public static class RestockReferenceTypes
{
    /// <summary>A return that passed or failed quality control.</summary>
    public const string Return = "return";

    /// <summary>A parcel that came back to the seller because it could not be delivered.</summary>
    public const string ReturnToOrigin = "rto";
}
