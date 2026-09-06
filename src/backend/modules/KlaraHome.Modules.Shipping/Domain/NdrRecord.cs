using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Shipping.Domain;

/// <summary>
/// A delivery attempt that failed, and what was decided about it
/// (docs/03-database-design.md §4.10, docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// One row per attempt, not one per parcel. Three failed attempts are three rows, because the
/// decision taken after the first is evidence about the second — a shopper who asked to reschedule
/// and was out again is a different conversation from one who has been out three times.
/// </para>
/// <para>
/// It exists as a record rather than as a shipment status because it needs a human. Every other
/// courier scan is a fact to be relayed; this one is a question — try again, change the address, or
/// send it back — and a queue is the only honest shape for a question nobody has answered yet.
/// </para>
/// <para>
/// Cash on delivery is where these concentrate. A shopper who does not have the money at the door is
/// the single commonest NDR reason in India, which is why <see cref="NdrReasonCode.CodNotReady"/> is
/// its own category rather than free text: the action for it is different, and it is worth counting.
/// </para>
/// </remarks>
internal sealed class NdrRecord : AggregateRoot<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private NdrRecord(Guid id, Guid shipmentId, int attemptNumber, DateTimeOffset raisedAt)
        : base(id)
    {
        ShipmentId = Guard.NotEmpty(shipmentId);
        AttemptNumber = Guard.Positive(attemptNumber);
        RaisedAt = raisedAt;
        Action = NdrAction.Pending;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private NdrRecord()
    {
    }

    /// <summary>The parcel.</summary>
    public Guid ShipmentId { get; private set; }

    /// <summary>The order, carried here so the queue can be worked without a join.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Its number, which is what an operator quotes to the shopper.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>The seller's part.</summary>
    public Guid SubOrderId { get; private set; }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>The shopper, who is who somebody has to ring.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The air waybill, so the queue is workable against the courier's own portal.</summary>
    public string? Awb { get; private set; }

    /// <summary>Which attempt this was, counting from one.</summary>
    public int AttemptNumber { get; private set; }

    /// <summary>What this platform made of the courier's reason.</summary>
    public NdrReasonCode ReasonCode { get; private set; }

    /// <summary>The courier's reason, verbatim. Often the only useful part.</summary>
    public string? Reason { get; private set; }

    /// <summary>What was decided. <c>Pending</c> is the queue.</summary>
    public NdrAction Action { get; private set; }

    /// <summary>What the operator wrote when they decided.</summary>
    public string? ActionRemark { get; private set; }

    /// <summary>The date the shopper asked for, when they asked for one.</summary>
    public DateTimeOffset? RescheduledFor { get; private set; }

    /// <summary>Who decided.</summary>
    public Guid? ActionedBy { get; private set; }

    /// <summary>When they decided.</summary>
    public DateTimeOffset? ActionedAt { get; private set; }

    /// <summary>When the attempt failed.</summary>
    public DateTimeOffset RaisedAt { get; private set; }

    /// <summary>When the report was closed, by a decision or by the parcel arriving.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

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

    /// <summary>Whether the report is still waiting for somebody.</summary>
    public bool IsOpen => Action == NdrAction.Pending && ResolvedAt is null;

    /// <summary>Opens a report against a failed attempt.</summary>
    /// <param name="shipment">The parcel that was not delivered.</param>
    /// <param name="attemptNumber">Which attempt this was.</param>
    /// <param name="reasonCode">What this platform made of the courier's reason.</param>
    /// <param name="reason">The courier's reason, verbatim.</param>
    /// <param name="raisedAt">When the attempt failed.</param>
    public static NdrRecord Raise(
        Shipment shipment,
        int attemptNumber,
        NdrReasonCode reasonCode,
        string? reason,
        DateTimeOffset raisedAt)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        return new NdrRecord(UuidV7.New(), shipment.Id, attemptNumber, raisedAt)
        {
            OrderId = shipment.OrderId,
            OrderNumber = shipment.OrderNumber,
            SubOrderId = shipment.SubOrderId,
            VendorId = shipment.VendorId,
            CustomerId = shipment.CustomerId,
            Awb = shipment.Awb,
            ReasonCode = reasonCode,
            Reason = Clip(reason, 500),
        };
    }

    /// <summary>
    /// Records what was decided.
    /// </summary>
    /// <remarks>
    /// Refused once the report is closed. A second decision on a report somebody already worked would
    /// overwrite the first without trace, and the reattempt it asked for may already be on a van.
    /// </remarks>
    /// <param name="action">What to do.</param>
    /// <param name="remark">What the operator wrote.</param>
    /// <param name="rescheduledFor">The date the shopper asked for, when they asked for one.</param>
    /// <param name="actionedBy">Who decided.</param>
    /// <param name="at">When.</param>
    /// <returns>Whether anything changed.</returns>
    public bool Decide(
        NdrAction action,
        string? remark,
        DateTimeOffset? rescheduledFor,
        Guid? actionedBy,
        DateTimeOffset at)
    {
        if (!IsOpen || action == NdrAction.Pending)
        {
            return false;
        }

        Action = action;
        ActionRemark = Clip(remark, 500);
        RescheduledFor = rescheduledFor;
        ActionedBy = actionedBy;
        ActionedAt = at;
        ResolvedAt = at;

        return true;
    }

    /// <summary>
    /// Closes the report because the parcel arrived after all.
    /// </summary>
    /// <remarks>
    /// Common enough to be worth its own path: a courier reports a failed attempt in the evening and
    /// delivers the next morning, and an operator should not have to work a queue of questions that
    /// have already answered themselves.
    /// </remarks>
    /// <param name="at">When it arrived.</param>
    /// <returns>Whether anything changed.</returns>
    public bool CloseOnDelivery(DateTimeOffset at)
    {
        if (!IsOpen)
        {
            return false;
        }

        Action = NdrAction.Resolved;
        ActionRemark = "The parcel was delivered on a later attempt.";
        ResolvedAt = at;

        return true;
    }

    private static string? Clip(string? value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= max ? value : value[..max];
}
