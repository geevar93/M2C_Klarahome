using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace KlaraHome.Modules.Media.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the application.
/// </summary>
/// <remarks>
/// <para>
/// It reuses <c>ConfigureKlaraHome</c> — the same call the runtime registration makes — so the
/// model the tooling generates a migration from is the model the application runs. A design-time
/// factory that configures the provider its own way is how a migration comes to describe a schema
/// nothing actually uses.
/// </para>
/// <para>
/// No connection is opened to generate a migration, so the placeholder connection string is
/// sufficient; <c>KLARAHOME_DESIGN_CONNECTION</c> overrides it for the commands that do connect,
/// such as <c>database update</c> and <c>dbcontext script</c> against a live database.
/// </para>
/// </remarks>
internal sealed class MediaDbContextFactory : IDesignTimeDbContextFactory<MediaDbContext>
{
    private const string PlaceholderConnection =
        "Host=localhost;Port=5432;Database=klarahome;Username=klarahome;Password=design-time";

    public MediaDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration["KLARAHOME_DESIGN_CONNECTION"]
                               ?? configuration.GetConnectionString(PersistenceExtensions.ConnectionStringName)
                               ?? PlaceholderConnection;

        var builder = new DbContextOptionsBuilder<MediaDbContext>();
        builder.ConfigureKlaraHome(provider: null, connectionString, MediaModule.SchemaName);

        return new MediaDbContext(builder.Options, new DesignTimeTenantContext());
    }

    /// <summary>
    /// There is no ambient tenant at design time. A fixed id keeps the generated model
    /// deterministic — the tenant appears in the model only as a query-filter parameter, never in
    /// the DDL, so its value cannot leak into a migration.
    /// </summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;

        public string Code => "design-time";
    }
}
