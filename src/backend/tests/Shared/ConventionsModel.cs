using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Testing.Persistence;

/// <summary>
/// A context that exercises every convention at once, so the model tests assert against a shape
/// that has all of them applied together rather than one contrived entity per rule.
/// </summary>
/// <remarks>
/// <para>
/// It lives in the test tree on purpose. Proving the conventions does not require a throwaway
/// table in a module's production schema, and a fixture that pretends to be a feature is worse
/// than one that admits what it is.
/// </para>
/// <para>
/// One file, linked into both test projects: the unit tests assert the <em>shape</em> of the model
/// this produces without a database, and the integration tests assert the <em>behaviour</em> the
/// same model produces against a real one. Two copies of the fixture would let those two halves
/// drift, and the drift would look like a passing suite.
/// </para>
/// </remarks>
public sealed class ConventionsDbContext(DbContextOptions<ConventionsDbContext> options, ITenantContext tenant)
    : KlaraHomeDbContext(options, tenant)
{
    public const string SchemaName = "conventions_test";

    public override string Schema => SchemaName;

    /// <summary>The test context carries the messaging DDL; nothing else in this model does.</summary>
    public override bool OwnsMessagingTables => true;

    public DbSet<AuditedThing> AuditedThings => Set<AuditedThing>();

    public DbSet<PlainThing> PlainThings => Set<PlainThing>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditedThing>(entity =>
        {
            entity.HasKey(thing => thing.Id);
            entity.Property(thing => thing.Id).ValueGeneratedNever();
            entity.Property(thing => thing.DisplayName).HasMaxLength(200);
            entity.HasMoney(thing => thing.SellingPrice);
        });

        modelBuilder.Entity<PlainThing>(entity =>
        {
            entity.HasKey(thing => thing.Id);
            entity.Property(thing => thing.Id).ValueGeneratedNever();
        });
    }
}

/// <summary>Carries every marker: tenant-scoped, audited, soft-deletable, and holds money.</summary>
public sealed class AuditedThing : ITenantScoped, IAuditable, ISoftDeletable
{
    public Guid Id { get; set; } = UuidV7.New();

    public Guid TenantId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public Money SellingPrice { get; set; } = Money.Rupees(0m);

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}

/// <summary>Carries none of them: proves the conventions are opt-in, not applied to everything.</summary>
public sealed class PlainThing
{
    public Guid Id { get; set; } = UuidV7.New();

    public string? Note { get; set; }
}

/// <summary>
/// Builds a context configured exactly as the application configures one — same provider options,
/// same naming convention, same migrations-history placement.
/// </summary>
public static class ConventionsContextFactory
{
    /// <summary>Options for a <see cref="ConventionsDbContext"/> against the given connection.</summary>
    /// <param name="connectionString">The Postgres connection string; never opened by the model tests.</param>
    /// <param name="interceptors">Interceptors to attach, typically the auditing one.</param>
    public static DbContextOptions<ConventionsDbContext> Options(
        string connectionString,
        params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ConventionsDbContext>();

        builder.ConfigureKlaraHome(provider: null, connectionString, ConventionsDbContext.SchemaName);
        builder.AddInterceptors(interceptors);

        return builder.Options;
    }
}

/// <summary>A tenant context whose id the test chooses.</summary>
public sealed class FixedTenantContext(Guid tenantId, string code = "test") : ITenantContext
{
    public Guid TenantId { get; } = tenantId;

    public string Code { get; } = code;
}

/// <summary>A user context the test can switch between anonymous and attributed.</summary>
public sealed class FixedUserContext(Guid? userId) : IUserContext
{
    public Guid? UserId { get; set; } = userId;
}

/// <summary>A clock the test advances by hand.</summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
