using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Contracts.Payments;

/// <summary>Cash owed at one door, as the module that put a parcel on a courier sees it.</summary>
/// <param name="CollectionId">The cash record.</param>
/// <param name="OrderId">The order.</param>
/// <param name="SubOrderId">The seller's part the cash is owed against.</param>
/// <param name="ShipmentId">The consignment carrying it, once one has been attached.</param>
/// <param name="Amount">What is to be collected at the door.</param>
/// <param name="CollectedAmount">What was actually taken, once it has been.</param>
/// <param name="RemittedAmount">What the courier handed over, once they have.</param>
/// <param name="CurrencyCode">ISO 4217 code every amount is in.</param>
/// <param name="Status">Where the cash stands.</param>
public sealed record CodCollectionView(
    Guid CollectionId,
    Guid OrderId,
    Guid SubOrderId,
    Guid? ShipmentId,
    decimal Amount,
    decimal? CollectedAmount,
    decimal? RemittedAmount,
    string CurrencyCode,
    string Status);

/// <summary>
/// The seam Shipping reaches cash on delivery through (docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// Payments owns the record of money, including the money a courier is carrying, and Shipping owns
/// the parcel it is carried in. Neither reads the other's schema, so the two facts are joined here:
/// Shipping says which consignment the cash travels on and what became of it, and Payments does the
/// arithmetic and keeps the ledger.
/// </para>
/// <para>
/// It exists rather than a second cash table in the shipping schema because a marketplace with two
/// answers to "what has the courier not yet remitted" is a marketplace that will one day pay a
/// seller out of money nobody collected. Payments already keeps the answer; this hands it the facts
/// only Shipping knows.
/// </para>
/// <para>
/// Every method is idempotent, because the delivery scan that drives them is delivered at least
/// once. Cash already recorded as collected is a success rather than a conflict.
/// </para>
/// </remarks>
public interface ICodCollections
{
    /// <summary>The cash record for a seller's part, or null when the parcel is prepaid.</summary>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CodCollectionView?> FindBySubOrderAsync(
        Guid subOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records which consignment the cash is travelling on.
    /// </summary>
    /// <remarks>
    /// Called when the AWB is booked, which is the first moment the two facts exist together. Until
    /// it happens, a courier remittance can only be matched by sub-order — which is exactly the
    /// manual reconciliation this call removes.
    /// </remarks>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="shipmentId">The consignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> AttachShipmentAsync(
        Guid subOrderId,
        Guid shipmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the courier took the money at the door.
    /// </summary>
    /// <remarks>
    /// The delivery scan on a cash parcel is the platform learning that cash changed hands. It is
    /// not the platform receiving it — that is the remittance, days later — and the two instants stay
    /// separate on the record precisely so a seller is never settled out of money nobody holds.
    /// </remarks>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="amount">What the courier reports taking.</param>
    /// <param name="collectedAt">When the parcel was delivered.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> RecordCollectedAsync(
        Guid subOrderId,
        decimal amount,
        DateTimeOffset collectedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that nothing will be collected, because the parcel never arrived.
    /// </summary>
    /// <remarks>
    /// A return to origin, a refusal at the door, a cancellation in transit. Marking it collected
    /// and then refunded would invent two movements of money where none happened.
    /// </remarks>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="reason">Why nothing is owed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> WaiveAsync(
        Guid subOrderId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a courier remittance against the consignments it covers.
    /// </summary>
    /// <remarks>
    /// By consignment rather than by order, because that is the shape of the file a courier sends:
    /// one transfer, and a list of air waybills. A total, where one is given, is apportioned across
    /// the records in proportion to what each was for, so a courier who deducts their fee from the
    /// batch leaves every record short by its share.
    /// </remarks>
    /// <param name="shipmentIds">The consignments the remittance covers.</param>
    /// <param name="reference">The courier's reference — a UTR or a batch number.</param>
    /// <param name="amount">What arrived in total, or null to take each record at its face value.</param>
    /// <param name="remittedAt">When it arrived.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<int>> RecordRemittanceAsync(
        IReadOnlyCollection<Guid> shipmentIds,
        string reference,
        decimal? amount,
        DateTimeOffset remittedAt,
        CancellationToken cancellationToken = default);
}
