using DotNet.Testcontainers.Builders;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Migrations;
using KlaraHome.Modules.Platform;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace KlaraHome.IntegrationTests.Platform;

/// <summary>
/// A PostgreSQL container with the Platform schema migrated and seeded, exactly as the
/// <c>migrator</c> job leaves it on a first deploy.
/// </summary>
/// <remarks>
/// The tests in this collection assert behaviour that only exists in the database — range
/// partitioning, the append-only trigger, the unique index behind a settings section, and whether
/// EF's keyset predicate translates to SQL at all. None of that can be proved without the real
/// engine and the real migrations.
/// </remarks>
public sealed class PlatformSchemaFixture : IAsyncLifetime
{
    /// <summary>The tenant code every test in this collection runs as.</summary>
    public const string TenantCode = "klarahome-tests";

    /// <summary>The display name configuration supplies, so branding can be asserted against it.</summary>
    public const string TenantName = "Klara Home Test Store";

    private PostgreSqlContainer? _container;
    private ServiceProvider? _services;

    /// <summary>Why the fixture could not start, or null if it did.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>The connection string for the migrated database.</summary>
    public string ConnectionString => _container?.GetConnectionString()
                                      ?? throw new InvalidOperationException(SkipReason ?? "Not started.");

    /// <summary>A provider wired the way the migrator wires one, with the module registered.</summary>
    public IServiceProvider Services => _services
                                        ?? throw new InvalidOperationException(SkipReason ?? "Not started.");

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder(Persistence.PostgresFixture.Image)
                .WithDatabase("klarahome_platform")
                .WithUsername("klarahome")
                .WithPassword("klarahome_tests")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "klarahome"))
                .Build();

            await _container.StartAsync();
        }
        catch (Exception exception)
        {
            _container = null;
            SkipReason = $"Docker is not available for the database tests: {exception.Message}";
            return;
        }

        _services = BuildServices(_container.GetConnectionString());

        await _services.GetRequiredService<MigrationRunner>().RunAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Runs every registered seeder again, to prove a redeploy changes nothing.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<int> ReseedAsync(CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<Infrastructure.Persistence.Seeding.IDataSeedRunner>()
            .RunAsync(cancellationToken);
    }

    private static ServiceProvider BuildServices(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Postgres"] = connectionString,
                ["Tenant:Code"] = TenantCode,
                ["Tenant:Name"] = TenantName,
                ["Database:RunSeeders"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IClock>(SystemClock.Instance);

        // The dispatcher and the validators, so the tests drive the same handlers the API does
        // rather than calling into the services underneath them.
        services.AddMessaging(typeof(PlatformModule).Assembly);

        services.AddKlaraHomePersistence(configuration, httpContextAvailable: false);
        services.AddModules(configuration, [typeof(PlatformModule).Assembly]);
        services.AddSingleton<MigrationRunner>();

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Shares one migrated database across the Platform test classes. Migrating a fresh schema per
/// class would multiply a real cost for no isolation gain — these tests write disjoint rows.
/// </summary>
[CollectionDefinition(CollectionName)]
public sealed class PlatformSchema : ICollectionFixture<PlatformSchemaFixture>
{
    /// <summary>The xUnit collection name the Platform database tests join.</summary>
    public const string CollectionName = "platform-schema";
}
