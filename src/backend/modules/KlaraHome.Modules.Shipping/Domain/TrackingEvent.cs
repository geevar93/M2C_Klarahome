using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Shipping.Domain;

/// <summary>
/// One scan on a parcel (docs/03-database-design.md §4.10).
/// </summary>
/// <remarks>
/// <para>
/// The evidence behind every status a shopper is shown. A shipment's status is a summary of the
/// latest of these; the rows themselves are what a support call is answered from, because "where is
/// it" is really "where has it been, and when did it stop moving".
/// </para>
/// <para>
/// Unique on <c>(shipment_id, provider_event_id)</c>, which is the whole of the deduplication.
/// Couriers repeat themselves — a webhook and a poll routinely report the same scan — and the index
/// is what makes reprocessing free rather than a timeline with everything in it twice.
/// </para>
/// <para>
/// Append-only and partitioned monthly by <see cref="OccurredAt"/> (docs/03-database-design.md §8).
/// This is the highest-volume table in the schema by an order of magnitude: every parcel produces
/// six to ten scans, and none of them is ever edited. The table is created by hand for that reason —
/// a partitioned table is created partitioned or not at all.
/// </para>
/// <para>
/// Both vocabularies are kept. <see cref="Status"/> is what this platform decided the scan means and
/// is what everything downstream reads; <see cref="CourierStatus"/> is the courier's own word,
/// verbatim, because an operator on the phone to the courier has to quote it back.
/// </para>
/// </remarks>
internal sealed class TrackingEvent : Entity<Guid>, ITenantScoped, IAppendOnly, IPartitioned
{
    private TrackingEvent(Guid id, Guid shipmentId, ShipmentStatus status, DateTimeOffset occurredAt)
        : base(id)
    {
        ShipmentId = Guard.NotEmpty(shipmentId);
        Status = status;
        OccurredAt = occurredAt;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private TrackingEvent()
    {
    }

    /// <summary>The parcel.</summary>
    public Guid ShipmentId { get; private set; }

    /// <summary>
    /// The courier's own id for the scan, which is what a replay is recognised by.
    /// </summary>
    /// <remarks>
    /// Synthesised from the courier's status and timestamp when they do not supply one, which most
    /// aggregators do not. That is enough: the same scan reported twice produces the same synthetic
    /// id, and two genuinely different scans at the same instant with the same status are the same
    /// scan for every purpose this platform has.
    /// </remarks>
    public string ProviderEventId { get; private set; } = string.Empty;

    /// <summary>What this platform decided the scan means.</summary>
    public ShipmentStatus Status { get; private set; }

    /// <summary>The courier's own word for it, kept verbatim.</summary>
    public string? CourierStatus { get; private set; }

    /// <summary>Where the scan happened, when the courier says.</summary>
    public string? Location { get; private set; }

    /// <summary>What the courier wrote. On a failed attempt this is the reason.</summary>
    public string? Remark { get; private set; }

    /// <summary>Whether this scan moved the shipment, or was only recorded.</summary>
    /// <remarks>
    /// A scan the machine had no edge for is kept and marked. It is the only trace of a courier
    /// reporting something impossible, and losing it would make that class of problem invisible.
    /// </remarks>
    public bool IsApplied { get; private set; }

    /// <summary>When the courier says it happened. The partition key.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>When this platform learned of it.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>The courier's payload for this scan, kept whole.</summary>
    public string? Raw { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Records a scan.</summary>
    /// <param name="shipmentId">The parcel.</param>
    /// <param name="providerEventId">The courier's id for the scan, or a synthesised one.</param>
    /// <param name="status">What this platform decided it means.</param>
    /// <param name="courierStatus">The courier's own word.</param>
    /// <param name="location">Where it happened.</param>
    /// <param name="remark">What the courier wrote.</param>
    /// <param name="occurredAt">When the courier says it happened.</param>
    /// <param name="receivedAt">When this platform learned of it.</param>
    /// <param name="raw">The courier's payload.</param>
    /// <param name="isApplied">Whether it moved the shipment.</param>
    public static TrackingEvent Record(
        Guid shipmentId,
        string providerEventId,
        ShipmentStatus status,
        string? courierStatus,
        string? location,
        string? remark,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        string? raw,
        bool isApplied)
        => new(UuidV7.New(), shipmentId, status, occurredAt)
        {
            ProviderEventId = Guard.MaxLength(Guard.NotNullOrWhiteSpace(providerEventId), 128),
            CourierStatus = Clip(courierStatus, 64),
            Location = Clip(location, 128),
            Remark = Clip(remark, 500),
            ReceivedAt = receivedAt,
            Raw = raw,
            IsApplied = isApplied,
        };

    /// <summary>
    /// The id a scan is deduplicated by when the courier supplies none.
    /// </summary>
    /// <remarks>
    /// Deterministic on purpose: the same scan arriving by webhook and again by the polling fallback
    /// has to collide on the unique index rather than appear twice.
    /// </remarks>
    /// <param name="courierStatus">The courier's own word for the scan.</param>
    /// <param name="occurredAt">When it happened.</param>
    public static string SyntheticId(string? courierStatus, DateTimeOffset occurredAt)
        => $"{occurredAt.ToUniversalTime():yyyyMMddTHHmmssZ}:{courierStatus?.Trim().ToLowerInvariant() ?? "scan"}";

    private static string? Clip(string? value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= max ? value : value[..max];
}
