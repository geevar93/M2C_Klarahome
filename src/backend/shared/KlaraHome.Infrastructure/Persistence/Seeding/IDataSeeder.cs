using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Infrastructure.Persistence.Seeding;

/// <summary>
/// Bootstrap data a deployment cannot function without: roles and permissions, default store
/// settings, tax rates, states and PIN-code reference data (docs/03-database-design.md §7).
/// </summary>
/// <remarks>
/// <para>
/// The contract is <b>idempotent and re-runnable</b>. A seeder runs on every deploy, against a
/// database that may be empty or may be three years old, and must leave it in the same state
/// either way. That means upserting by natural key — never "insert if the table is empty", which
/// silently does nothing after the first row is added by hand.
/// </para>
/// <para>
/// Seeders are <em>not</em> for demo or test data. Anything a business could reasonably delete
/// does not belong here.
/// </para>
/// </remarks>
public interface IDataSeeder
{
    /// <summary>Name used in logs, and in the failure message when this seeder throws.</summary>
    string Name { get; }

    /// <summary>Relative order. Lower runs first; use it only where data genuinely depends on data.</summary>
    int Order => 100;

    /// <summary>Applies the seeder's data. Must be safe to call repeatedly.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SeedAsync(CancellationToken cancellationToken);
}

/// <summary>Runs every registered seeder, in order, once.</summary>
public interface IDataSeedRunner
{
    /// <summary>Runs the seeders and returns how many ran.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> RunAsync(CancellationToken cancellationToken);
}

/// <param name="seeders">Every registered seeder.</param>
/// <param name="logger">Reports what ran, so a deploy log answers "was the data applied".</param>
internal sealed class DataSeedRunner(IEnumerable<IDataSeeder> seeders, ILogger<DataSeedRunner> logger)
    : IDataSeedRunner
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var ordered = seeders
            .OrderBy(seeder => seeder.Order)
            .ThenBy(seeder => seeder.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var seeder in ordered)
        {
            try
            {
                await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
                PersistenceLog.SeederApplied(logger, seeder.Name);
            }
            catch (Exception exception)
            {
                // Named rather than rethrown bare: "seeder X failed" is diagnosable from a deploy
                // log; a raw Npgsql exception with no context is not.
                throw new InvalidOperationException(
                    $"Data seeder '{seeder.Name}' failed. Seeders must be idempotent and re-runnable.",
                    exception);
            }
        }

        return ordered.Count;
    }
}

/// <summary>Registration helper, so a module adds a seeder in one line.</summary>
public static class SeedingExtensions
{
    /// <summary>Registers a seeder to run after migrations.</summary>
    /// <typeparam name="TSeeder">The seeder implementation.</typeparam>
    /// <param name="services">The container.</param>
    public static IServiceCollection AddDataSeeder<TSeeder>(this IServiceCollection services)
        where TSeeder : class, IDataSeeder
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IDataSeeder, TSeeder>();
        return services;
    }
}
