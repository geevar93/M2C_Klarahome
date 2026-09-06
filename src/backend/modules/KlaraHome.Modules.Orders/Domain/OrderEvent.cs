using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Orders.Domain;

/// <summary>
/// The kinds of entry that appear on an order's timeline.
/// </summary>
/// <remarks>
/// Deliberately short. Every status change is one <see cref="StatusChanged"/> row carrying the two
/// states, rather than a type per transition — a machine with sixteen states and thirty edges would
/// otherwise need thirty constants here and a new one for every edge added.
/// </remarks>
internal static class OrderEventTypes
{
    /// <summary>The order was created.</summary>
    public const string Placed = "placed";

    /// <summary>A sub-order moved from one state to another.</summary>
    public const string StatusChanged = "status-changed";

    /// <summary>Units were cancelled, in whole or in part.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>A tax invoice was raised.</summary>
    public const string Invoiced = "invoiced";

    /// <summary>Payment moved — authorised, captured, refused, refunded.</summary>
    public const string Payment = "payment";

    /// <summary>Somebody wrote something down. Internal unless explicitly marked otherwise.</summary>
    public const string Note = "note";

    /// <summary>Every type this platform writes.</summary>
    public static readonly IReadOnlyList<string> All = [Placed, StatusChanged, Cancelled, Invoiced, Payment, Note];
}

/// <summary>
/// One entry on an order's timeline (docs/03-database-design.md §4.8). Append-only.
/// </summary>
/// <remarks>
/// <para>
/// Two jobs in one table, and they are the same job: it is what the shopper sees on "track my
/// order", and it is what support reads when the shopper says the tracking is wrong.
/// <see cref="IsCustomerVisible"/> is the only thing separating them, which is what keeps the two
/// accounts of an order from drifting apart — an internal note and a delivery update are entries in
/// one sequence, not two systems.
/// </para>
/// <para>
/// Append-only and marked as such, so the optimistic-concurrency convention does not apply: there is
/// no lost update to detect on a row nothing ever updates. Corrections are new entries, never edits,
/// which is what makes the timeline evidence rather than a summary.
/// </para>
/// <para>
/// Internal notes live here rather than in a table of their own. They are things that happened to
/// the order, in the order they happened, and a separate table would mean an operator reading two
/// lists and interleaving them by eye.
/// </para>
/// </remarks>
internal sealed class OrderEvent : Entity<Guid>, ITenantScoped, IAppendOnly
{
    private OrderEvent(Guid id, Guid orderId, string type, DateTimeOffset occurredAt)
        : base(id)
    {
        OrderId = orderId;
        Type = type;
        OccurredAt = occurredAt;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private OrderEvent() => Type = string.Empty;

    /// <summary>The order it belongs to.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The seller's part it concerns, or null for an order-level entry.</summary>
    public Guid? SubOrderId { get; private set; }

    /// <summary>One of <see cref="OrderEventTypes"/>.</summary>
    public string Type { get; private set; }

    /// <summary>Where the sub-order was, for a status change.</summary>
    public string? FromStatus { get; private set; }

    /// <summary>Where it went.</summary>
    public string? ToStatus { get; private set; }

    /// <summary>What class of actor did it.</summary>
    public OrderActor ActorType { get; private set; } = OrderActor.System;

    /// <summary>Who, when there was somebody.</summary>
    public Guid? ActorId { get; private set; }

    /// <summary>What the entry says, in words. Shown to the shopper when it is visible to them.</summary>
    public string? Message { get; private set; }

    /// <summary>Anything structured worth keeping — an AWB, a gateway reference. Stored as <c>jsonb</c>.</summary>
    public string? Payload { get; private set; }

    /// <summary>Whether the shopper sees it on "track my order".</summary>
    public bool IsCustomerVisible { get; private set; }

    /// <summary>When it happened.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Records a sub-order moving.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="from">Where it was.</param>
    /// <param name="to">Where it went.</param>
    /// <param name="actor">Who moved it.</param>
    /// <param name="actorId">Which user, when there was one.</param>
    /// <param name="message">What to tell the shopper, when they may see it.</param>
    /// <param name="visible">Whether the shopper sees it.</param>
    /// <param name="at">When it happened.</param>
    public static OrderEvent StatusChange(
        Guid orderId,
        Guid subOrderId,
        SubOrderStatus from,
        SubOrderStatus to,
        OrderActor actor,
        Guid? actorId,
        string? message,
        bool visible,
        DateTimeOffset at)
        => new(UuidV7.New(), Guard.NotEmpty(orderId), OrderEventTypes.StatusChanged, at)
        {
            SubOrderId = subOrderId,
            FromStatus = from.ToString(),
            ToStatus = to.ToString(),
            ActorType = actor,
            ActorId = actorId,
            Message = message,
            IsCustomerVisible = visible,
        };

    /// <summary>Records anything else.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="subOrderId">The seller's part, or null for an order-level entry.</param>
    /// <param name="type">One of <see cref="OrderEventTypes"/>.</param>
    /// <param name="actor">Who did it.</param>
    /// <param name="actorId">Which user, when there was one.</param>
    /// <param name="message">What it says.</param>
    /// <param name="payload">Anything structured worth keeping, already serialised.</param>
    /// <param name="visible">Whether the shopper sees it.</param>
    /// <param name="at">When it happened.</param>
    public static OrderEvent Record(
        Guid orderId,
        Guid? subOrderId,
        string type,
        OrderActor actor,
        Guid? actorId,
        string? message,
        string? payload,
        bool visible,
        DateTimeOffset at)
        => new(UuidV7.New(), Guard.NotEmpty(orderId), Guard.NotNullOrWhiteSpace(type), at)
        {
            SubOrderId = subOrderId,
            ActorType = actor,
            ActorId = actorId,
            Message = message,
            Payload = payload,
            IsCustomerVisible = visible,
        };
}
