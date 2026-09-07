using DotNet.Testcontainers.Builders;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Migrations;
using KlaraHome.Modules.Carts;
using KlaraHome.Modules.Catalog;
using KlaraHome.Modules.Content;
using KlaraHome.Modules.Identity;
using KlaraHome.Modules.Inventory;
using KlaraHome.Modules.Media;
using KlaraHome.Modules.Notifications;
using KlaraHome.Modules.Orders;
using KlaraHome.Modules.Payments;
using KlaraHome.Modules.Platform;
using KlaraHome.Modules.Pricing;
using KlaraHome.Modules.Reporting;
using KlaraHome.Modules.Returns;
using KlaraHome.Modules.Reviews;
using KlaraHome.Modules.Search;
using KlaraHome.Modules.Settlements;
using KlaraHome.Modules.Shipping;
using KlaraHome.Modules.Vendors;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace KlaraHome.IntegrationTests.Database;

/// <summary>
/// A PostgreSQL container with every module's schema migrated and seeded, exactly as the
/// <c>migrator</c> job leaves it on a first deploy.
/// </summary>
/// <remarks>
/// <para>
/// The tests in this collection assert behaviour that only exists in the database — range
/// partitioning, the append-only trigger, the unique index behind a settings section, whether EF's
/// keyset predicate translates to SQL at all, and whether the vendor query filter actually keeps
/// one seller out of another's rows. None of that can be proved without the real engine and the
/// real migrations.
/// </para>
/// <para>
/// Every module, not just one: the API host these tests point at composes all of them, and a
/// database carrying only some of their schemas is not a database the product ever runs against.
/// </para>
/// </remarks>
public sealed class KlaraHomeSchemaFixture : IAsyncLifetime
{
    /// <summary>
    /// Every module assembly, exactly as the three hosts compose them.
    /// </summary>
    /// <remarks>
    /// Listed once rather than at each call site, because a module missing here is a module whose
    /// schema is never migrated — and the failure would surface as a "relation does not exist" in
    /// whichever test happened to touch it first.
    /// </remarks>
    private static readonly System.Reflection.Assembly[] ModuleAssemblies =
    [
        typeof(PlatformModule).Assembly,
        typeof(IdentityModule).Assembly,
        typeof(MediaModule).Assembly,
        typeof(NotificationsModule).Assembly,
        typeof(VendorsModule).Assembly,
        typeof(CatalogModule).Assembly,
        typeof(InventoryModule).Assembly,
        typeof(PricingModule).Assembly,
        typeof(CartsModule).Assembly,
        typeof(OrdersModule).Assembly,
        typeof(PaymentsModule).Assembly,
        typeof(ShippingModule).Assembly,
        typeof(ReturnsModule).Assembly,
        typeof(SettlementsModule).Assembly,
        typeof(SearchModule).Assembly,
        typeof(ContentModule).Assembly,
        typeof(ReviewsModule).Assembly,
        typeof(ReportingModule).Assembly,
    ];

    /// <summary>The tenant code every test in this collection runs as.</summary>
    public const string TenantCode = "klarahome-tests";

    /// <summary>The display name configuration supplies, so branding can be asserted against it.</summary>
    public const string TenantName = "Klara Home Test Store";

    /// <summary>The first administrator this deployment is configured to create.</summary>
    public const string BootstrapEmail = "founder@klarahome.test";

    /// <summary>Their initial password. Long, and not one of the documented examples.</summary>
    public const string BootstrapPassword = "correct-horse-battery-staple-27";

    /// <summary>The id of the key TOTP secrets are encrypted under in these tests.</summary>
    public const string EncryptionKeyId = "test";

    /// <summary>
    /// A fixed AES key, 32 bytes. Fixed rather than generated, so the two hosts in these tests —
    /// the fixture's own provider and the API under test — can read each other's protected values.
    /// </summary>
    private const string TestEncryptionKey = "a2xhcmFob21lLWludGVncmF0aW9uLXRlc3RzLWtleSE=";

    /// <summary>
    /// Configuration every host in these tests shares: the tenant, the bootstrap administrator and
    /// the key their authenticator secret is protected with.
    /// </summary>
    /// <param name="connectionString">The migrated database.</param>
    public static Dictionary<string, string?> SharedSettings(string connectionString) => new(StringComparer.Ordinal)
    {
        ["ConnectionStrings:Postgres"] = connectionString,
        ["Tenant:Code"] = TenantCode,
        ["Tenant:Name"] = TenantName,
        ["Auth:Bootstrap:Email"] = BootstrapEmail,
        ["Auth:Bootstrap:Password"] = BootstrapPassword,
        // The test host serves plain http, and a cookie marked Secure is one a client correctly
        // refuses to send back over it. This is the single setting that exists for that case; it
        // is never false in a deployed environment.
        ["Auth:Tokens:RefreshCookieSecure"] = "false",
        ["Auth:Encryption:CurrentKeyId"] = EncryptionKeyId,
        [$"Auth:Encryption:Keys:{EncryptionKeyId}"] = TestEncryptionKey,
    };

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
            _container = new PostgreSqlBuilder(KlaraHome.IntegrationTests.Persistence.PostgresFixture.Image)
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
        var settings = SharedSettings(connectionString);
        settings["Database:RunSeeders"] = "true";

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // The seeders include one that behaves differently outside Development, so the container
        // has to answer "which environment is this" the way a real host does.
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment
            {
                EnvironmentName = Microsoft.Extensions.Hosting.Environments.Development,
                ApplicationName = "KlaraHome.Tests",
                ContentRootPath = AppContext.BaseDirectory,
            });
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IClock>(SystemClock.Instance);

        // The dispatcher and the validators, so the tests drive the same handlers the API does
        // rather than calling into the services underneath them.
        services.AddMessaging(ModuleAssemblies);

        services.AddKlaraHomePersistence(configuration, httpContextAvailable: false);
        services.AddModules(configuration, ModuleAssemblies);
        services.AddSingleton<MigrationRunner>();

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Shares one migrated database across the database-backed test classes. Migrating a fresh schema
/// per class would multiply a real cost for no isolation gain, and a collection runs serially, so
/// two classes never write at the same time.
/// </summary>
[CollectionDefinition(CollectionName)]
public sealed class KlaraHomeSchema : ICollectionFixture<KlaraHomeSchemaFixture>
{
    /// <summary>The xUnit collection name the database-backed tests join.</summary>
    public const string CollectionName = "klarahome-schema";
}
