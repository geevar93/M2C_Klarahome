namespace KlaraHome.SharedKernel.Domain;

/// <summary>
/// Identity and domain-event bookkeeping for a domain object. Deliberately free of any
/// persistence concern: auditing columns, soft delete, concurrency tokens and UUIDv7 key
/// generation are added by the EF Core layer in Step 4, not here.
/// </summary>
public abstract class Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity(TId id) => Id = id;

    /// <summary>Required by EF Core's materialiser; not for application use.</summary>
    protected Entity() => Id = default!;

    public TId Id { get; protected init; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public override bool Equals(object? obj)
        => obj is Entity<TId> other
           && other.GetType() == GetType()
           && EqualityComparer<TId>.Default.Equals(other.Id, Id);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

/// <summary>
/// The consistency boundary. Only aggregate roots are loaded, saved and locked; everything else
/// is reached through one.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    protected AggregateRoot()
    {
    }
}
