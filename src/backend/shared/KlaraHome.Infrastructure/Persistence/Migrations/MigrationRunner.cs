using KlaraHome.Infrastructure.Configuration;
using KlaraHome.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Infrastructure.Persistence.Migrations;

/// <summary>The outcome of a migration run, for the caller's exit code and log line.</summary>
/// <param name="ContextsInspected">How many module contexts were considered.</param>
/// <param name="MigrationsApplied">How many migrations were applied across all of them.</param>
/// <param name="SeedersRun">How many seeders ran afterwards.</param>
public sealed record MigrationRunResult(int ContextsInspected, int MigrationsApplied, int SeedersRun);

/// <summary>
/// Applies pending migrations for every registered module context, in module order, and then runs
/// the seeders.
/// </summary>
/// <remarks>
/// <para>
/// Used by the <c>migrator</c> job container. In production the API never migrates on startup: a
/// schema change and a rolling restart at the same moment is how two versions of the code end up
/// disagreeing about the shape of a table (docs/03-database-design.md §7).
/// </para>
/// <para>
/// Each module migrates independently, not all of them in one transaction. Postgres supports
/// transactional DDL and EF wraps each migration in its own transaction, so a module either
/// arrives complete or not at all. Wrapping every module together would instead make a failure in
/// the last one discard the work of the first, and the modules share no schema to be consistent
/// about.
/// </para>
/// </remarks>
/// <param name="services">Resolves each context in its own scope.</param>
/// <param name="descriptors">Every registered module context.</param>
/// <param name="databaseOptions">Controls whether seeders run.</param>
/// <param name="logger">Reports progress; a deploy log must say what was applied.</param>
public sealed class MigrationRunner(
    IServiceProvider services,
    IEnumerable<ModuleDbContextDescriptor> descriptors,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<MigrationRunner> logger)
{
    /// <summary>Applies everything pending. Throws on the first failure; the caller exits non-zero.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<MigrationRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        // The migrator does not run hosted services, so the check the API gets at boot is made
        // here explicitly. A module whose context disagrees about its schema would otherwise
        // migrate its tables into someone else's.
        PersistenceExtensions.ValidateRegistrations(services);

        var ordered = descriptors
            .DistinctBy(descriptor => descriptor.ContextType)
            .OrderBy(descriptor => descriptor.Order)
            .ThenBy(descriptor => descriptor.ModuleName, StringComparer.Ordinal)
            .ToList();

        var applied = 0;

        foreach (var descriptor in ordered)
        {
            applied += await MigrateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        }

        var seeded = 0;
        if (databaseOptions.Value.RunSeeders)
        {
            using var scope = services.CreateScope();
            seeded = await scope.ServiceProvider
                .GetRequiredService<IDataSeedRunner>()
                .RunAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            PersistenceLog.SeedersSkipped(logger);
        }

        return new MigrationRunResult(ordered.Count, applied, seeded);
    }

    private async Task<int> MigrateAsync(ModuleDbContextDescriptor descriptor, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var context = (KlaraHomeDbContext)scope.ServiceProvider.GetRequiredService(descriptor.ContextType);

        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false))
            .ToList();

        if (pending.Count == 0)
        {
            PersistenceLog.ModuleUpToDate(logger, descriptor.ModuleName, descriptor.Schema);
            return 0;
        }

        PersistenceLog.ApplyingMigrations(logger, descriptor.ModuleName, descriptor.Schema, pending.Count);

        // No transaction is opened here, deliberately. MigrateAsync manages its own — and takes an
        // advisory lock so two migrator containers started at once cannot both apply the same
        // migration. Wrapping it in an outer transaction suppresses that lock and, because the
        // provider probes for the history table before it exists, aborts the transaction on the
        // first run. EF says so out loud when you try; this comment is so nobody tries twice.
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        PersistenceLog.MigrationsApplied(logger, descriptor.ModuleName, pending.Count);
        return pending.Count;
    }
}
