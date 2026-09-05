using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;

namespace KlaraHome.Modules.Platform.Domain;

/// <summary>
/// The business this deployment belongs to. One row per deployment in v1: the redistribution model
/// is a separate installation per client, not shared hosting (IMPLEMENTATION_PLAN §6).
/// </summary>
/// <remarks>
/// <para>
/// The row is not the source of the tenant's identity — configuration is, through
/// <c>Tenant:Id</c> and <c>Tenant:Code</c>, because the id has to exist before any table can be
/// written to and because onboarding a second business must be a configuration change. The row is
/// the database's record of that configured tenant, and the thing that makes <c>tenant_id</c> on
/// every other table mean something a human can read.
/// </para>
/// <para>
/// It is deliberately not <see cref="ITenantScoped"/>. Filtering the tenant table by the ambient
/// tenant would work, and would also mean a misconfigured deployment saw an empty table rather
/// than a mismatch it could report.
/// </para>
/// </remarks>
internal sealed class Tenant : AggregateRoot<Guid>, IAuditable
{
    private Tenant(Guid id, string code, string name, TenantStatus status)
        : base(id)
    {
        Code = Guard.NotNullOrWhiteSpace(code);
        Name = Guard.NotNullOrWhiteSpace(name);
        Status = status;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Tenant()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Short lowercase code identifying the deployment (ADR-006). Unique, and never reused.</summary>
    public string Code { get; private set; }

    /// <summary>Display name of the business, used wherever a human reads which deployment this is.</summary>
    public string Name { get; private set; }

    /// <summary>Whether the deployment is serving. A suspended tenant is refused at the door.</summary>
    public TenantStatus Status { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Registers a tenant with the id the deployment is configured to write.</summary>
    /// <param name="id">The configured tenant id, as written into <c>tenant_id</c> everywhere.</param>
    /// <param name="code">The configured tenant code.</param>
    /// <param name="name">Display name of the business.</param>
    public static Tenant Register(Guid id, string code, string name)
        => new(id, code, name, TenantStatus.Active);

    /// <summary>Renames the tenant. The code is immutable; every row already written carries the id.</summary>
    /// <param name="name">The new display name.</param>
    public void Rename(string name) => Name = Guard.NotNullOrWhiteSpace(name);

    /// <summary>Moves the tenant to a new status.</summary>
    /// <param name="status">The new status.</param>
    public void ChangeStatus(TenantStatus status) => Status = status;
}

/// <summary>Whether a tenant's deployment is serving traffic.</summary>
internal enum TenantStatus
{
    /// <summary>Serving normally.</summary>
    Active = 0,

    /// <summary>Not serving. Kept so its data stays readable to an operator.</summary>
    Suspended = 1,
}
