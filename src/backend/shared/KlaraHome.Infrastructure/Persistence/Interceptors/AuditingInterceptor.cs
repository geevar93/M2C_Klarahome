using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KlaraHome.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Fills <c>tenant_id</c>, the audit columns and the soft-delete columns on every save, for every
/// context, without a single handler having to remember to.
/// </summary>
/// <remarks>
/// <para>
/// An interceptor rather than an override on the base context, so it applies uniformly, is
/// testable in isolation, and cannot be bypassed by a context that forgets to call
/// <c>base.SaveChangesAsync</c>.
/// </para>
/// <para>
/// It also converts a <see cref="EntityState.Deleted"/> on an <see cref="ISoftDeletable"/> into an
/// update. That means <c>Remove()</c> does the right thing by default: nobody has to know which
/// entities are soft-deleted, and no accidental <c>DELETE</c> can escape through a cascade.
/// </para>
/// </remarks>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="userContext">The acting user, when there is one.</param>
/// <param name="clock">The sanctioned clock; UTC everywhere.</param>
internal sealed class AuditingInterceptor(
    ITenantContext tenantContext,
    IUserContext userContext,
    IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;
        var actor = userContext.UserId;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged)
            {
                continue;
            }

            StampTenant(entry);
            StampSoftDelete(entry, now, actor);
            StampAudit(entry, now, actor);
        }
    }

    /// <summary>
    /// A new row belongs to the ambient tenant. An existing row's tenant is immutable — moving a
    /// row between tenants is not an update, it is a data leak.
    /// </summary>
    private void StampTenant(EntityEntry entry)
    {
        if (entry.Entity is not ITenantScoped)
        {
            return;
        }

        var property = entry.Property(nameof(ITenantScoped.TenantId));

        if (entry.State == EntityState.Added)
        {
            if (Equals(property.CurrentValue, Guid.Empty))
            {
                property.CurrentValue = tenantContext.TenantId;
            }

            return;
        }

        property.IsModified = false;
    }

    /// <summary>
    /// Turns a delete of a soft-deletable row into the update the design intends
    /// (docs/03-database-design.md §1), so history survives an ordinary <c>Remove()</c>.
    /// </summary>
    private static void StampSoftDelete(EntityEntry entry, DateTimeOffset now, Guid? actor)
    {
        if (entry is { State: EntityState.Deleted, Entity: ISoftDeletable })
        {
            entry.State = EntityState.Modified;
            entry.Property(nameof(ISoftDeletable.DeletedAt)).CurrentValue = now;
            entry.Property(nameof(ISoftDeletable.DeletedBy)).CurrentValue = actor;
        }
    }

    private static void StampAudit(EntityEntry entry, DateTimeOffset now, Guid? actor)
    {
        if (entry.Entity is not IAuditable)
        {
            return;
        }

        if (entry.State == EntityState.Added)
        {
            entry.Property(nameof(IAuditable.CreatedAt)).CurrentValue = now;
            entry.Property(nameof(IAuditable.CreatedBy)).CurrentValue = actor;
            return;
        }

        if (entry.State != EntityState.Modified)
        {
            return;
        }

        entry.Property(nameof(IAuditable.UpdatedAt)).CurrentValue = now;
        entry.Property(nameof(IAuditable.UpdatedBy)).CurrentValue = actor;

        // Creation is a fact about the past. An update that rewrites it is a bug, whether it came
        // from a stale entity graph or from a client that sent the field back.
        entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
        entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
    }
}
