using DotNet.Testcontainers.Builders;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Migrations;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace KlaraHome.IntegrationTests.Persistence;

/// <summary>
/// Drives the real migration runner against a real database — the same code path the
/// <c>migrator</c> job container runs on every deploy.
/// </summary>
/// <remarks>
/// Each test gets its own database rather than sharing the suite's, because a migration test that
/// starts from "whatever the previous test left" proves nothing about a first deploy.
/// </remarks>
public sealed class MigrationPipelineTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string? _skipReason;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder(PostgresFixture.Image)
                .WithDatabase("klarahome_migrations")
                .WithUsername("klarahome")
                .WithPassword("klarahome_tests")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "klarahome"))
                .Build();

            await _container.StartAsync();
        }
        catch (Exception exception)
        {
            _container = null;
            _skipReason = $"Docker is not available for the database tests: {exception.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Builds the provider the migrator builds: persistence services, the modules discovered by
    /// the same registry the API uses, and the runner. Nothing is stubbed.
    /// </summary>
    private ServiceProvider BuildMigratorServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Postgres"] = _container!.GetConnectionString(),
                ["Tenant:Code"] = "klarahome",
                ["Database:RunSeeders"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<KlaraHome.SharedKernel.Time.IClock>(KlaraHome.SharedKernel.Time.SystemClock.Instance);
        services.AddKlaraHomePersistence(configuration, httpContextAvailable: false);
        services.AddModules(configuration, [typeof(PlatformModule).Assembly]);
        services.AddSingleton<MigrationRunner>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_migration_creates_its_schema_and_applies_cleanly_to_an_empty_database()
    {
        Assert.SkipWhen(_skipReason is not null, _skipReason ?? string.Empty);

        await using var provider = BuildMigratorServices();

        var result = await provider.GetRequiredService<MigrationRunner>()
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ContextsInspected);
        Assert.Equal(1, result.MigrationsApplied);

        var tables = await ReadAsync(
            provider,
            $"""
             SELECT table_name AS "Value" FROM information_schema.tables
             WHERE table_schema = {PlatformModule.SchemaName}
             """);

        Assert.Contains("outbox_messages", tables);
        Assert.Contains("inbox_messages", tables);
        Assert.Contains(PersistenceExtensions.HistoryTableName, tables);
    }

    [Fact]
    public async Task Running_the_migrator_twice_applies_nothing_the_second_time()
    {
        Assert.SkipWhen(_skipReason is not null, _skipReason ?? string.Empty);

        await using var provider = BuildMigratorServices();
        var runner = provider.GetRequiredService<MigrationRunner>();

        await runner.RunAsync(TestContext.Current.CancellationToken);
        var second = await runner.RunAsync(TestContext.Current.CancellationToken);

        // A deploy that redeploys the same version must be a no-op, not a failure and not a
        // re-application.
        Assert.Equal(0, second.MigrationsApplied);
    }

    [Fact]
    public async Task The_extensions_the_design_requires_are_installed()
    {
        Assert.SkipWhen(_skipReason is not null, _skipReason ?? string.Empty);

        await using var provider = BuildMigratorServices();
        await provider.GetRequiredService<MigrationRunner>().RunAsync(TestContext.Current.CancellationToken);

        var extensions = await ReadAsync(provider, $"""SELECT extname AS "Value" FROM pg_extension""");

        // Named individually rather than as a set: a missing one is a specific later failure —
        // pg_trgm absent means fuzzy search silently degrades, not that a test list is stale.
        Assert.Contains("pgcrypto", extensions);
        Assert.Contains("pg_trgm", extensions);
        Assert.Contains("unaccent", extensions);
        Assert.Contains("btree_gin", extensions);
    }

    [Fact]
    public async Task The_migration_history_lives_in_the_module_schema()
    {
        Assert.SkipWhen(_skipReason is not null, _skipReason ?? string.Empty);

        await using var provider = BuildMigratorServices();
        await provider.GetRequiredService<MigrationRunner>().RunAsync(TestContext.Current.CancellationToken);

        // Sharing one public history table across modules would let the first module to migrate
        // convince every later one that it was already up to date.
        var schemas = await ReadAsync(
            provider,
            $"""
             SELECT table_schema AS "Value" FROM information_schema.tables
             WHERE table_name = {PersistenceExtensions.HistoryTableName}
             """);

        Assert.Equal(PlatformModule.SchemaName, Assert.Single(schemas));
    }

    [Fact]
    public async Task Seeders_run_after_the_migrations()
    {
        Assert.SkipWhen(_skipReason is not null, _skipReason ?? string.Empty);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Postgres"] = _container!.GetConnectionString(),
                ["Database:RunSeeders"] = "true",
            })
            .Build();

        var seeder = new CountingSeeder();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<KlaraHome.SharedKernel.Time.IClock>(KlaraHome.SharedKernel.Time.SystemClock.Instance);
        services.AddKlaraHomePersistence(configuration, httpContextAvailable: false);
        services.AddModules(configuration, [typeof(PlatformModule).Assembly]);
        services.AddSingleton<IDataSeeder>(seeder);
        services.AddSingleton<MigrationRunner>();

        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<MigrationRunner>()
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.SeedersRun);
        Assert.Equal(1, seeder.Runs);
    }

    [Fact]
    public async Task Seeders_are_skipped_when_the_deployment_asks_for_schema_only()
    {
        Assert.SkipWhen(_skipReason is not null, _skipReason ?? string.Empty);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Postgres"] = _container!.GetConnectionString(),
                ["Database:RunSeeders"] = "false",
            })
            .Build();

        var seeder = new CountingSeeder();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<KlaraHome.SharedKernel.Time.IClock>(KlaraHome.SharedKernel.Time.SystemClock.Instance);
        services.AddKlaraHomePersistence(configuration, httpContextAvailable: false);
        services.AddModules(configuration, [typeof(PlatformModule).Assembly]);
        services.AddSingleton<IDataSeeder>(seeder);
        services.AddSingleton<MigrationRunner>();

        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<MigrationRunner>()
            .RunAsync(TestContext.Current.CancellationToken);

        // A restore drill migrates the schema without touching data; that is what this switch is
        // for, and it has to actually hold the seeders back.
        Assert.Equal(0, result.SeedersRun);
        Assert.Equal(0, seeder.Runs);
    }

    private static async Task<List<string>> ReadAsync(IServiceProvider provider, FormattableString sql)
    {
        using var scope = provider.CreateScope();

        var descriptor = scope.ServiceProvider.GetServices<ModuleDbContextDescriptor>().First();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(descriptor.ContextType);

        return await context.Database.SqlQuery<string>(sql).ToListAsync(TestContext.Current.CancellationToken);
    }

    private sealed class CountingSeeder : IDataSeeder
    {
        public int Runs { get; private set; }

        public string Name => "counting-seeder";

        public Task SeedAsync(CancellationToken cancellationToken)
        {
            Runs++;
            return Task.CompletedTask;
        }
    }
}
