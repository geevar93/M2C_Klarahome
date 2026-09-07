using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KlaraHome.Infrastructure.Persistence.Seeding;

/// <summary>
/// The switch that puts a small demonstration catalogue into a non-production database.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IDataSeeder"/> is explicit that seeders are for bootstrap data a deployment cannot
/// function without, and that demo data does not belong there. That rule is not being relaxed here;
/// it is being given the one exception it needs, and the exception is fenced on three sides:
/// </para>
/// <list type="number">
/// <item>
/// A demo seeder is registered only when <see cref="DemoDataExtensions.AddDemoDataSeeder{TSeeder}"/>
/// finds the switch on, which needs somebody to
/// have written <c>DemoData:SeedCatalog=true</c> into configuration. It is off by default, and
/// nothing turns it on implicitly.
/// </item>
/// <item>
/// It is refused outright in Production, at registration and again inside the seeder, whatever
/// configuration says. A production database gets this data only if somebody edits this file.
/// </item>
/// <item>
/// Everything it writes is keyed on a <c>DEMO-</c> prefix or a <c>demo-</c> slug, so it is
/// idempotent on a re-run and an operator can see at a glance which rows are not real.
/// </item>
/// </list>
/// <para>
/// What it exists for: a developer, a designer or a client looking at a freshly migrated database
/// sees an empty storefront, and an empty storefront cannot be reviewed. Every page that matters —
/// home, category, search, product, cart — needs a catalogue behind it before anybody can say
/// whether it works.
/// </para>
/// </remarks>
public sealed class DemoDataOptions
{
    /// <summary>The configuration section these bind to.</summary>
    public const string SectionName = "DemoData";

    /// <summary>
    /// Whether to seed the demonstration catalogue. Off by default, and ignored in Production.
    /// </summary>
    public bool SeedCatalog { get; set; }
}

/// <summary>Registration helper for seeders that must never reach a production database.</summary>
public static class DemoDataExtensions
{
    /// <summary>
    /// Whether this host should register its demonstration seeders.
    /// </summary>
    /// <remarks>
    /// Read straight from configuration rather than from bound options, because module registration
    /// runs before the options pipeline is available to it — and because a decision this consequential
    /// should be visible at the call site rather than resolved from a container three layers down.
    /// </remarks>
    /// <param name="configuration">Root configuration.</param>
    /// <param name="environment">The host environment. Production always answers false.</param>
    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsProduction())
        {
            return false;
        }

        return configuration.GetValue<bool>($"{DemoDataOptions.SectionName}:{nameof(DemoDataOptions.SeedCatalog)}");
    }

    /// <summary>
    /// Registers a demonstration seeder, but only outside Production and only when the switch is on.
    /// </summary>
    /// <typeparam name="TSeeder">The seeder implementation.</typeparam>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Root configuration.</param>
    public static IServiceCollection AddDemoDataSeeder<TSeeder>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TSeeder : class, IDataSeeder
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // The environment is not injectable here, so it is read the same way the host itself
        // determines it. A host that has not set it is not Production.
        var environmentName = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? Environments.Development;

        if (string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase))
        {
            return services;
        }

        if (!configuration.GetValue<bool>($"{DemoDataOptions.SectionName}:{nameof(DemoDataOptions.SeedCatalog)}"))
        {
            return services;
        }

        services.AddScoped<IDataSeeder, TSeeder>();
        return services;
    }
}
