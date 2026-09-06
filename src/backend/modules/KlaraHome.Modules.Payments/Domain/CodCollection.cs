using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Payments.Domain;

/// <summary>
/// Cash owed at one door, and its journey back to the platform
/// (docs/03-database-design.md §4.9, docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// Per sub-order rather than per order, because cash is collected per parcel: two sellers in one
/// basket are two deliveries, two couriers and two amounts, and a single row against the order could
/// only ever describe one of them.
/// </para>
/// <para>
/// The two instants are deliberately separate. <see cref="CollectedAt"/> is when the courier took
/// the money and it became <em>theirs</em>; <see cref="RemittedAt"/> is when they handed it over and
/// it became the platform's. Settling a seller on the first would be paying them out of the
/// platform's own pocket for money it does not yet hold, which is the classic cash-on-delivery
/// marketplace failure.
/// </para>
/// <para>
/// <see cref="CodCollectionStatus.Waived"/> is the honest end for a parcel that never arrived — a
/// return to origin, a refusal at the door, a cancellation in transit. Nothing is owed, and marking
/// it collected-then-refunded would invent two movements where none happened.
/// </para>
/// </remarks>
internal sealed class CodCollection : Entity<Guid>, ITenantScoped, IAuditable
{
    private CodCollection(
        Guid id,
        Guid orderId,
        Guid subOrderId,
        decimal amount,
        string currencyCode)
        : base(id)
    {
        OrderId = orderId;
        SubOrderId = subOrderId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = CodCollectionStatus.Pending;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CodCollection() => CurrencyCode = Money.Inr;

    /// <summary>The order.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The seller's part the cash is owed against. One row per parcel.</summary>
    public Guid SubOrderId { get; private set; }

    /// <summary>The seller, so a vendor can be shown what is outstanding at their doors.</summary>
    public Guid? VendorId { get; private set; }

    /// <summary>The shipment, once Step 16 has created one.</summary>
    public Guid? ShipmentId { get; private set; }

    /// <summary>What is to be collected at the door.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO 4217 code every amount here is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>Where the cash stands.</summary>
    public CodCollectionStatus Status { get; private set; }

    /// <summary>When the courier took it.</summary>
    public DateTimeOffset? CollectedAt { get; private set; }

    /// <summary>What was actually taken, which is not always what was owed.</summary>
    public decimal? CollectedAmount { get; private set; }

    /// <summary>Who recorded the collection, when a person did.</summary>
    public Guid? CollectedBy { get; private set; }

    /// <summary>When the courier handed it over.</summary>
    public DateTimeOffset? RemittedAt { get; private set; }

    /// <summary>What the courier actually remitted, net of whatever they deduct.</summary>
    public decimal? RemittedAmount { get; private set; }

    /// <summary>The courier's remittance reference — a UTR or a batch number.</summary>
    public string? RemittanceReference { get; private set; }

    /// <summary>Why nothing is owed, when nothing is.</summary>
    public string? Note { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Opens the expectation of cash at a door.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="vendorId">The seller.</param>
    /// <param name="amount">What is owed at the door.</param>
    /// <param name="currencyCode">ISO 4217 code the amount is in.</param>
    public static CodCollection Expect(
        Guid orderId,
        Guid subOrderId,
        Guid? vendorId,
        decimal amount,
        string currencyCode)
        => new(UuidV7.New(), orderId, subOrderId, Guard.NotNegative(amount), Guard.NotNullOrWhiteSpace(currencyCode))
        {
            VendorId = vendorId,
        };

    /// <summary>Attaches the shipment the cash will travel with.</summary>
    /// <param name="shipmentId">The shipment.</param>
    public void AttachShipment(Guid shipmentId) => ShipmentId = shipmentId;

    /// <summary>Records that the courier took the money.</summary>
    /// <param name="amount">What was actually taken.</param>
    /// <param name="collectedAt">When.</param>
    /// <param name="collectedBy">Who recorded it.</param>
    /// <returns>Whether anything changed.</returns>
    public bool Collect(decimal amount, DateTimeOffset collectedAt, Guid? collectedBy)
    {
        if (Status is not CodCollectionStatus.Pending)
        {
            return false;
        }

        Status = CodCollectionStatus.Collected;
        CollectedAmount = amount;
        CollectedAt = collectedAt;
        CollectedBy = collectedBy;

        return true;
    }

    /// <summary>
    /// Records the courier's remittance.
    /// </summary>
    /// <remarks>
    /// Remitting money nobody recorded as collected is allowed, and stamps both instants. It happens:
    /// a courier's remittance file is often the first this platform hears of a delivery whose status
    /// update was lost, and refusing the money would be refusing the only evidence there is of it.
    /// </remarks>
    /// <param name="amount">What the courier handed over.</param>
    /// <param name="reference">Their reference for it.</param>
    /// <param name="remittedAt">When.</param>
    /// <returns>Whether anything changed.</returns>
    public bool Remit(decimal amount, string? reference, DateTimeOffset remittedAt)
    {
        if (Status is CodCollectionStatus.Remitted or CodCollectionStatus.Waived)
        {
            return false;
        }

        CollectedAt ??= remittedAt;
        CollectedAmount ??= amount;
        Status = CodCollectionStatus.Remitted;
        RemittedAmount = amount;
        RemittanceReference = Clip(reference, 128);
        RemittedAt = remittedAt;

        return true;
    }

    /// <summary>Records that nothing is owed, because the parcel never arrived.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>Whether anything changed.</returns>
    public bool Waive(string? reason)
    {
        if (Status is CodCollectionStatus.Remitted or CodCollectionStatus.Waived)
        {
            return false;
        }

        Status = CodCollectionStatus.Waived;
        Note = Clip(reason, 500);

        return true;
    }

    /// <summary>Records cash that was taken and will never be remitted.</summary>
    /// <param name="reason">Why it is unrecoverable.</param>
    /// <returns>Whether anything changed.</returns>
    public bool WriteOff(string? reason)
    {
        if (Status is not CodCollectionStatus.Collected)
        {
            return false;
        }

        Status = CodCollectionStatus.WrittenOff;
        Note = Clip(reason, 500);

        return true;
    }

    private static string? Clip(string? value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= max ? value : value[..max];
}
