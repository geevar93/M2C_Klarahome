using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace KlaraHome.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the Identity model without starting the application.
/// </summary>
/// <remarks>
/// It reuses <c>ConfigureKlaraHome</c> — the same call the runtime registration makes — so the
/// model the tooling generates a migration from is the model the application runs. There is no
/// caller at design time, and none is passed: the vendor filter appears in the model only as a
/// query parameter, never in the DDL.
/// </remarks>
internal sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    private const string PlaceholderConnection =
        "Host=localhost;Port=5432;Database=klarahome;Username=klarahome;Password=design-time";

    public IdentityDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration["KLARAHOME_DESIGN_CONNECTION"]
                               ?? configuration.GetConnectionString(PersistenceExtensions.ConnectionStringName)
                               ?? PlaceholderConnection;

        var builder = new DbContextOptionsBuilder<IdentityDbContext>();
        builder.ConfigureKlaraHome(provider: null, connectionString, IdentityModule.SchemaName);

        return new IdentityDbContext(builder.Options, new DesignTimeTenantContext());
    }

    /// <summary>A fixed id keeps the generated model deterministic.</summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;

        public string Code => "design-time";
    }
}
