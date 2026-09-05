namespace KlaraHome.Contracts.IntegrationEvents;

/// <summary>
/// Base record for integration events. Derive with a positional record so the payload stays a
/// flat, serialisable contract:
/// <code>
/// public sealed record OrderConfirmed(Guid OrderId, string OrderNumber) : IntegrationEvent;
/// </code>
/// </summary>
public abstract record IntegrationEvent : IIntegrationEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public string? CorrelationId { get; init; }
}
