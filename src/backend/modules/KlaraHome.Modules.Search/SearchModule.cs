using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Inventory;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Search.Endpoints;
using KlaraHome.Modules.Search.Infrastructure;
using KlaraHome.Modules.Search.Infrastructure.Engine;
using KlaraHome.Modules.Search.Infrastructure.Events;
using KlaraHome.Modules.Search.Infrastructure.Features;
using KlaraHome.Modules.Search.Infrastructure.Jobs;
using KlaraHome.Modules.Search.Infrastructure.Logging;
using KlaraHome.Modules.Search.Infrastructure.Persistence;
using KlaraHome.Modules.Search.Infrastructure.Projection;
using KlaraHome.Modules.Search.Infrastructure.Query;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Search;

/// <summary>
/// How a shopper finds a thing they cannot name.
/// </summary>
/// <remarks>
/// <para>
/// This module owns no truth at all, and that is the most important thing about it. Every fact in
/// the index belongs to Catalog, Pricing, Inventory or Vendors; it arrives here as an integration
/// event and can be rebuilt from those modules at any time. A wrong row is therefore an operational
/// nuisance rather than a loss — which is a very different risk profile from every module before it,
/// and it is why this one can afford a denormalised copy of half the platform.
/// </para>
/// <para>
/// The exception is the query log, and it is the only original record here: what a shopper typed
/// exists nowhere else. The queries that returned nothing are the most valuable rows in the schema —
/// they are a buying team's most direct evidence of what the store does not stock — and the whole
/// log is behind a switch, because a search term is personal data under the DPDP Act.
/// </para>
/// <para>
/// Four properties hold whatever else changes. <b>One row per variant, carrying the offer that won
/// its buy box</b>, resolved by the module that owns the rule, so a search result and the page it
/// links to can never name two different sellers. <b>The search vector is a generated column</b>, so
/// there is no path by which a row is written and its text is not — the bulk rebuild included.
/// <b>Facet counts are computed with each facet's own filter lifted</b>, which is the only definition
/// that agrees with what happens when a shopper clicks one. And <b>every write is an upsert keyed on
/// the variant</b>, which is what makes at-least-once event delivery and a concurrent rebuild safe on
/// the same table.
/// </para>
/// <para>
/// PostgreSQL full text is the engine, behind an interface this module owns (ADR-007, ADR-019). A
/// dedicated engine is a class, a registration line, a configuration value and a feature flag away,
/// and nothing outside this module would know it had happened. It needs no credentials and no second
/// container, so unlike the payment, courier and payout adapters before it there is no degraded mode
/// here at all: search works in every deployment, on the day it is installed.
/// </para>
/// </remarks>
public sealed class SearchModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "search";

    /// <inheritdoc />
    public string Name => "Search";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// First of Phase E, after Settlements. It consumes from four modules and is consumed by none,
    /// so it registers last among those it reads and the order is a statement of that.
    /// </summary>
    public int Order => 150;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<SearchDbContext>(configuration, this);
        services.AddValidatedOptions<SearchOptions>(configuration, SearchOptions.SectionName);

        // The store's own words. The cache is a singleton with a time to live because every search
        // parses a query and every keystroke in the suggestion box is a search — reading two tables
        // on each one would make the vocabulary the most-read data in the platform.
        services.AddSingleton<SearchVocabularyCache>();
        services.AddScoped<SearchVocabularyReader>();

        // The single place an index row is written, and the two operations that drive it.
        services.AddScoped<SearchProjectionWriter>();
        services.AddScoped<SearchIndexService>();
        services.AddScoped<SearchQueryRecorder>();

        AddEngines(services);
        AddEventHandlers(services);

        // Declared in code and seeded into platform.feature_flags, exactly as every module's are.
        services.AddSingleton<IFeatureFlagSource, SearchFeatureFlagSource>();

        // Off in the API and on in the worker, exactly as every sweeper before it.
        services.AddHostedService<SearchReindexWorker>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var store = endpoints.MapGroup("/store");
        store.MapStoreSearchEndpoints();

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminSearchEndpoints();
    }

    /// <summary>
    /// Registers every engine this build has, told apart by their keys.
    /// </summary>
    /// <remarks>
    /// One today. The registry picks the one <c>Search:Provider</c> names, and falls through to
    /// PostgreSQL when it names nothing, names something this build has no adapter for, or names one
    /// whose credentials are blank — the arrangement ADR-018 settled on for couriers, applied to
    /// search. Adding a dedicated engine is one class and one line here (ADR-019).
    /// </remarks>
    private static void AddEngines(IServiceCollection services)
    {
        services.AddScoped<ISearchEngine, PostgresSearchEngine>();
        services.AddScoped<SearchEngineRegistry>();
    }

    /// <summary>
    /// Subscribes to the six facts that between them are everything a shopper's results depend on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An offer going live, its terms changing, its withdrawal, its price moving, its stock moving,
    /// and its being bought. Three of the six re-resolve the buy box because they can change which
    /// offer wins; stock deliberately does not, which is what keeps the highest-volume event in the
    /// platform costing one <c>UPDATE</c>.
    /// </para>
    /// <para>
    /// The seller life-cycle events are deliberately absent, and it is the subscription somebody will
    /// try to add. Catalog already reacts to a suspension by withdrawing that seller's listings, and
    /// each withdrawal publishes <c>ListingDeactivated</c> — so subscribing here as well would do the
    /// same work twice and, worse, would do it from a module that cannot see whether the listings
    /// were actually withdrawn.
    /// </para>
    /// </remarks>
    private static void AddEventHandlers(IServiceCollection services)
    {
        services.AddScoped<SearchProjectionHandlers>();

        services.AddScoped<IIntegrationEventHandler<ListingPublished>>(
            provider => provider.GetRequiredService<SearchProjectionHandlers>());

        services.AddScoped<IIntegrationEventHandler<ListingUpdated>>(
            provider => provider.GetRequiredService<SearchProjectionHandlers>());

        services.AddScoped<IIntegrationEventHandler<ListingDeactivated>>(
            provider => provider.GetRequiredService<SearchProjectionHandlers>());

        services.AddScoped<IIntegrationEventHandler<PriceChanged>>(
            provider => provider.GetRequiredService<SearchProjectionHandlers>());

        services.AddScoped<IIntegrationEventHandler<StockLevelChanged>>(
            provider => provider.GetRequiredService<SearchProjectionHandlers>());

        services.AddScoped<IIntegrationEventHandler<SubOrderConfirmed>>(
            provider => provider.GetRequiredService<SearchProjectionHandlers>());
    }
}
