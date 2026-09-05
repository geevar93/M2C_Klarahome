using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Health;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Platform.Endpoints;
using KlaraHome.Modules.Platform.Infrastructure;
using KlaraHome.Modules.Platform.Infrastructure.Auditing;
using KlaraHome.Modules.Platform.Infrastructure.FeatureFlags;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using KlaraHome.Modules.Platform.Infrastructure.Seeding;
using KlaraHome.Modules.Platform.Infrastructure.Settings;
using KlaraHome.Modules.Platform.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Platform;

/// <summary>
/// Tenancy, platform settings, branding, feature flags, audit and reference data.
/// </summary>
/// <remarks>
/// This is the module that makes the product re-distributable: nothing about the business it is
/// deployed for is compiled in. The tenant comes from configuration, everything a business would
/// want to change comes from <c>platform.store_settings</c>, and every change to it is recorded in
/// <c>platform.audit_logs</c>.
/// </remarks>
public sealed class PlatformModule : IModule
{
    /// <summary>
    /// The Postgres schema this module owns, as a constant so the context and its design-time
    /// factory name it without constructing the module.
    /// </summary>
    public const string SchemaName = "platform";

    /// <inheritdoc />
    public string Name => "Platform";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// Registered first: tenancy and settings are what every other module reads its
    /// configuration from, and this context carries the outbox every other module writes to.
    /// </summary>
    public int Order => 10;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<PlatformDbContext>(configuration, this);
        services.AddValidatedOptions<PlatformOptions>(configuration, PlatformOptions.SectionName);

        // Settings and flags are read on nearly every request and written a few times a year, so
        // both sit behind the process cache rather than the table.
        services.AddMemoryCache();

        // The concrete service is registered and the contract forwarded to it, so the module's own
        // code can use the write and invalidate methods while every other module sees only the
        // published read contract.
        services.AddScoped<StoreSettingsService>();
        services.AddScoped<IStoreSettings>(provider => provider.GetRequiredService<StoreSettingsService>());

        services.AddScoped<FeatureFlagService>();
        services.AddScoped<IFeatureFlags>(provider => provider.GetRequiredService<FeatureFlagService>());

        services.AddScoped<IAuditLogger, AuditLogger>();

        services.AddDataSeeder<TenantSeeder>();
        services.AddDataSeeder<ReferenceDataSeeder>();
        services.AddDataSeeder<StoreSettingsSeeder>();
        services.AddDataSeeder<FeatureFlagSeeder>();
        services.AddDataSeeder<PincodeSeeder>();

        // Only where there is a database to ask. A host configured without one — the API in the
        // integration tests, for instance — reports the checks it can run rather than a failure it
        // cannot act on, which is the same rule AddKlaraHomeHealthChecks follows.
        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString(PersistenceExtensions.ConnectionStringName)))
        {
            services
                .AddHealthChecks()
                .AddCheck<TenantHealthCheck>(TenantHealthCheck.Name, tags: [HealthCheckExtensions.ReadyTag]);
        }
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapStorePlatformEndpoints();
        endpoints.MapAdminPlatformEndpoints();
    }
}
