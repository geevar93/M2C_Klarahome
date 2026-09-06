using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Configuration;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence.Interceptors;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KlaraHome.Infrastructure.Persistence;

/// <summary>
/// What a module context registration leaves behind, so the migration runner can find every
/// context without referencing any module. The migrator is a host: it composes, it does not know
/// what a Catalog is.
/// </summary>
/// <param name="ModuleName">The owning module's name, for logs and ordering.</param>
/// <param name="Schema">The Postgres schema the module declared.</param>
/// <param name="Order">The module's registration order; migrations follow it.</param>
/// <param name="ContextType">The concrete context type to resolve.</param>
public sealed record ModuleDbContextDescriptor(string ModuleName, string Schema, int Order, Type ContextType);

/// <summary>
/// Data-access composition: the connection, the conventions, the interceptors, and the registry
/// the migrator reads.
/// </summary>
public static class PersistenceExtensions
{
    /// <summary>The connection string name every host and container uses.</summary>
    public const string ConnectionStringName = "Postgres";

    /// <summary>
    /// Registers the services a module context depends on. Called once per host, before any
    /// module registers its context.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Configuration to bind <see cref="DatabaseOptions"/> from.</param>
    /// <param name="httpContextAvailable">
    /// Whether the host serves HTTP. The API attributes writes to the request principal; the
    /// worker and the migrator act as the system and have no principal to attribute to.
    /// </param>
    public static IServiceCollection AddKlaraHomePersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        bool httpContextAvailable)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<DatabaseOptions>(
            configuration,
            DatabaseOptions.SectionName,
            options => options.MaxRetryDelaySeconds >= 1,
            "Database:MaxRetryDelaySeconds must be at least 1 second.");

        // Bound here rather than only in the API's cross-cutting registration, because the ambient
        // tenant decides what tenant_id every row is written under - and the worker and the migrator
        // write rows without ever calling AddKlaraHomeInfrastructure. Unbound, they would fall back
        // to the built-in defaults and seed a deployment under a different tenant than the one the
        // API serves, silently.
        services.AddValidatedOptions<TenantOptions>(configuration, TenantOptions.SectionName);

        services.TryAddSingleton<ITenantContext, ConfiguredTenantContext>();

        // Anything that writes carries the correlation id of whatever caused it: the outbox stamps
        // it on every queued event and the audit trail on every entry. That makes it a persistence
        // dependency rather than an HTTP one - the worker and the migrator write too, and outside a
        // request the context simply mints a fresh id, which is the right answer for a job.
        services.TryAddScoped<Correlation.ICorrelationContext, Correlation.CorrelationContext>();

        // Registered here alongside the tenant for the same reason: the vendor query filter is a
        // persistence concern, and a host that writes rows without ever calling
        // AddKlaraHomeInfrastructure still has to know that it has no vendor scope.
        services.AddKlaraHomeAuthorizationContext(httpContextAvailable);

        if (httpContextAvailable)
        {
            services.AddHttpContextAccessor();
            services.TryAddScoped<IUserContext, ClaimsUserContext>();
        }
        else
        {
            services.TryAddSingleton<IUserContext, SystemUserContext>();
        }

        services.TryAddScoped<AuditingInterceptor>();
        services.TryAddScoped<IDataSeedRunner, DataSeedRunner>();

        return services;
    }

    /// <summary>
    /// Turns on outbox dispatch in this host. Called by the worker only: dispatching from every
    /// API replica would multiply the work and have replicas contending for the same rows for no
    /// gain (docs/01-architecture.md §3.1).
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Supplies <see cref="OutboxOptions"/>.</param>
    /// <param name="contractAssemblies">Assemblies scanned for integration-event contracts.</param>
    public static IServiceCollection AddOutboxDispatcher(
        this IServiceCollection services,
        IConfiguration configuration,
        params System.Reflection.Assembly[] contractAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<OutboxOptions>(configuration, OutboxOptions.SectionName);
        services.TryAddSingleton(new IntegrationEventTypeMap(contractAssemblies));
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }

    /// <summary>
    /// Registers one module's context: the Npgsql connection, the shared interceptors, the
    /// snake_case naming convention, and the descriptor the migrator reads.
    /// </summary>
    /// <typeparam name="TContext">The module's context.</typeparam>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Supplies the connection string.</param>
    /// <param name="module">The owning module; its schema and order are recorded on the descriptor.</param>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        IModule module)
        where TContext : KlaraHomeDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(module);

        // The connection string is read from the container rather than captured here. A host whose
        // configuration is completed after registration - WebApplicationFactory does exactly that -
        // would otherwise register every context against whatever the string was at startup, which
        // is usually nothing.
        services.AddDbContext<TContext>((provider, builder) =>
            builder.ConfigureKlaraHome(
                provider,
                provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName),
                module.Schema));

        services.AddSingleton(new ModuleDbContextDescriptor(
            module.Name,
            module.Schema,
            module.Order,
            typeof(TContext)));

        // Resolved per context: an outbox write must join the transaction of the context the
        // caller is already using, so there is one IOutbox per context, not one per host.
        //
        // The unkeyed registration is first-wins, which means it belongs to whichever module
        // registered first — the Platform module, in this solution. That is correct for exactly one
        // module and silently wrong for every other: a handler in another module that enqueued
        // through it would add the row to a *different* context's change tracker, its own
        // SaveChangesAsync would not write it, and the event would be lost with no error anywhere.
        // So the keyed registration below is the one a module must ask for by name, and the unkeyed
        // one is kept only for callers that hold the first-registered context anyway.
        services.TryAddScoped<IOutbox>(provider => new DbContextOutbox(
            provider.GetRequiredService<TContext>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<Correlation.ICorrelationContext>(),
            provider.GetRequiredService<SharedKernel.Time.IClock>()));

        // Keyed by the context type, so a module publishes into its own transaction and says so:
        //     [FromKeyedServices(typeof(MyDbContext))] IOutbox outbox
        services.AddKeyedScoped<IOutbox>(typeof(TContext), (provider, _) => new DbContextOutbox(
            provider.GetRequiredService<TContext>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<Correlation.ICorrelationContext>(),
            provider.GetRequiredService<SharedKernel.Time.IClock>()));

        services.AddHostedService<PersistenceStartupValidator>();

        return services;
    }

    /// <summary>
    /// The provider configuration shared by runtime registration and the design-time factory, so
    /// the model <c>dotnet ef</c> builds is the model the application runs.
    /// </summary>
    /// <param name="builder">The options being built.</param>
    /// <param name="provider">Supplies options and interceptors; null at design time.</param>
    /// <param name="connectionString">The Postgres connection string.</param>
    /// <param name="schema">The schema whose migrations history table this context owns.</param>
    public static DbContextOptionsBuilder ConfigureKlaraHome(
        this DbContextOptionsBuilder builder,
        IServiceProvider? provider,
        string? connectionString,
        string schema)
    {
        var options = provider?.GetService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>()?.Value
                      ?? new DatabaseOptions();

        builder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.CommandTimeout(options.CommandTimeoutSeconds);
            npgsql.EnableRetryOnFailure(
                options.MaxRetryCount,
                TimeSpan.FromSeconds(options.MaxRetryDelaySeconds),
                errorCodesToAdd: null);

            // Each context keeps its migration history in its own schema. Without this every
            // module would share one history table and the first migrator to run would decide
            // that every other module was already up to date.
            npgsql.MigrationsHistoryTable(HistoryTableName, schema);
        });

        builder.UseSnakeCaseNamingConvention();

        if (provider is not null)
        {
            builder.AddInterceptors(provider.GetRequiredService<AuditingInterceptor>());
        }

        if (options.EnableSensitiveDataLogging)
        {
            builder.EnableSensitiveDataLogging();
            builder.EnableDetailedErrors();
        }

        return builder;
    }

    /// <summary>The per-schema EF migrations history table, named to match our own conventions.</summary>
    public const string HistoryTableName = "__ef_migrations_history";

    /// <summary>
    /// Refuses to start a host whose persistence registration is inconsistent — a context whose
    /// schema disagrees with its module's, or a solution where the messaging tables are owned by
    /// none or by several contexts. Both are silent-corruption bugs at runtime and a one-line
    /// failure at boot, so they are made into the latter.
    /// </summary>
    /// <summary>
    /// Checks that every registered context agrees with its module about the schema it owns, and
    /// that exactly one of them carries the messaging tables. Called at host start and again by
    /// the migration runner, which does not run hosted services.
    /// </summary>
    /// <param name="services">A provider to resolve the contexts from.</param>
    public static void ValidateRegistrations(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();

        var descriptors = scope.ServiceProvider
            .GetServices<ModuleDbContextDescriptor>()
            .DistinctBy(descriptor => descriptor.ContextType)
            .ToList();

        var owners = new List<string>();

        foreach (var descriptor in descriptors)
        {
            var context = (KlaraHomeDbContext)scope.ServiceProvider.GetRequiredService(descriptor.ContextType);

            if (!string.Equals(context.Schema, descriptor.Schema, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{descriptor.ContextType.Name} owns schema '{context.Schema}' but module "
                    + $"'{descriptor.ModuleName}' declares '{descriptor.Schema}'. A module and its "
                    + "context must agree, or its tables land outside its own boundary.");
            }

            if (context.OwnsMessagingTables)
            {
                owners.Add(descriptor.ContextType.Name);
            }
        }

        if (descriptors.Count > 0 && owners.Count != 1)
        {
            throw new InvalidOperationException(
                "Exactly one DbContext must set OwnsMessagingTables so the outbox and inbox are "
                + $"created once. Found {owners.Count}: {string.Join(", ", owners)}.");
        }
    }

    private sealed class PersistenceStartupValidator(IServiceProvider services, IHostEnvironment environment)
        : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = services.CreateScope();

            var database = scope.ServiceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;

            if (environment.IsProduction() && database.MigrateOnStartup)
            {
                throw new InvalidOperationException(
                    "Database:MigrateOnStartup must be false in Production. Migrations are applied by the "
                    + "migrator job before the new version starts, so a rolling restart never has two "
                    + "versions of the code disagreeing about the shape of a table "
                    + "(docs/03-database-design.md §7).");
            }

            if (environment.IsProduction() && database.EnableSensitiveDataLogging)
            {
                throw new InvalidOperationException(
                    "Database:EnableSensitiveDataLogging must be false in Production: it writes parameter "
                    + "values, including personal data, into the logs.");
            }

            ValidateRegistrations(services);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
