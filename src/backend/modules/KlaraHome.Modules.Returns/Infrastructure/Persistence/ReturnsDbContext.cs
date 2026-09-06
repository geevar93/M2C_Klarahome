using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Returns.Infrastructure.Persistence;

/// <summary>
/// The Returns module's data access.
/// </summary>
/// <remarks>
/// <para>
/// Two halves with different scoping, as the shipping schema has. The reason codes are the
/// platform's — read by every shopper, written by staff — and carry no vendor scope at all. The
/// returns and their credit notes belong to a seller, and carry it, so a seller working their own
/// queue cannot see another's.
/// </para>
/// <para>
/// The counter table has no vendor scope either, and that is not an oversight. A credit-note series
/// is scoped to a seller through its <c>scope_key</c>, not through a query filter, because the row
/// is taken with <c>SELECT ... FOR UPDATE</c> and a lock taken inside a filter's subquery is a lock
/// this code would be trusting the provider to preserve.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller.</param>
internal sealed class ReturnsDbContext(
    DbContextOptions<ReturnsDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => ReturnsModule.SchemaName;

    /// <summary>The RMAs.</summary>
    public DbSet<ReturnRequest> Returns => Set<ReturnRequest>();

    /// <summary>What is on them, which is what makes a partial return expressible.</summary>
    public DbSet<ReturnLine> ReturnLines => Set<ReturnLine>();

    /// <summary>The reasons a shopper may give, and the policy that follows from each.</summary>
    public DbSet<ReturnReason> Reasons => Set<ReturnReason>();

    /// <summary>The documents that reduce a seller's output tax.</summary>
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();

    /// <summary>The gapless counters behind RMA numbers and credit-note numbers.</summary>
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new ReturnRequestConfiguration());
        modelBuilder.ApplyConfiguration(new ReturnLineConfiguration());
        modelBuilder.ApplyConfiguration(new ReturnReasonConfiguration());
        modelBuilder.ApplyConfiguration(new CreditNoteConfiguration());
        modelBuilder.ApplyConfiguration(new NumberSequenceConfiguration());
    }
}
