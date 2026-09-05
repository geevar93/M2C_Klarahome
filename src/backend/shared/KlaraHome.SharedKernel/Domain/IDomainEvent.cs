namespace KlaraHome.SharedKernel.Domain;

/// <summary>
/// Something that happened inside one aggregate, raised and handled entirely within the owning
/// module. Cross-module facts are integration events instead (KlaraHome.Contracts) and travel
/// through the outbox — see docs/01-architecture.md §2.1.
/// </summary>
public interface IDomainEvent
{
    /// <summary>When the fact occurred, in UTC.</summary>
    DateTimeOffset OccurredAtUtc { get; }
}
