using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Content.Endpoints;
using KlaraHome.Modules.Content.Infrastructure;
using KlaraHome.Modules.Content.Infrastructure.Blocks;
using KlaraHome.Modules.Content.Infrastructure.Collections;
using KlaraHome.Modules.Content.Infrastructure.Events;
using KlaraHome.Modules.Content.Infrastructure.Features;
using KlaraHome.Modules.Content.Infrastructure.Jobs;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.Modules.Content.Infrastructure.Rendering;
using KlaraHome.Modules.Content.Infrastructure.Seo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Content;

/// <summary>
/// The storefront a merchandiser can change without a deployment.
/// </summary>
/// <remarks>
/// <para>
/// A page is an ordered list of typed blocks, and everything else in this module follows from that
/// one sentence. A block type is a closed declaration — this codebase and an Angular component agree
/// on it — with a schema its configuration is validated against on the way in, so a malformed block is
/// an editor's error message rather than a shopper's blank page. Menus, banners, curated collections
/// and redirects are the other four things a business wants to change on a Tuesday afternoon, and none
/// of them is a deployment either.
/// </para>
/// <para>
/// It owns everything in its schema, which makes it the opposite of the module before it. Search can
/// be rebuilt from the catalogue at any time; a page nobody backed up is gone. That is why every
/// publish snapshots the whole page into an append-only version row — preview and rollback both fall
/// out of that one decision — and why deletion is soft wherever it exists here at all.
/// </para>
/// <para>
/// What it does not own is products. A collection holds product ids, a carousel names them, and every
/// fact about them is resolved through the catalogue's contracts at read time with the buy box the
/// Catalog module itself picked — so a merchandising tile and the product page it links to cannot name
/// two sellers. Two seams were added for it: <c>ICatalogTaxonomy</c>, for the category tree a sitemap
/// and a breadcrumb need, and two methods on <c>IProductProjectionSource</c>, for resolving products
/// by id and by slug.
/// </para>
/// <para>
/// The SEO surface lives here rather than in the storefront because the facts do. The canonical host
/// and the organisation's identity are settings, the URLs a sitemap lists come from these tables and
/// the catalogue, a <c>301</c> is a row a merchandiser writes when they rename a page, and the price
/// in a <c>schema.org</c> <c>Offer</c> has to be the price the product page is showing. The storefront
/// renders what this module computes.
/// </para>
/// </remarks>
public sealed class ContentModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "content";

    /// <inheritdoc />
    public string Name => "Content";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// Second of Phase E, after Search. It consumes from Catalog and Media and is consumed by none,
    /// so it registers after everything it reads and the order is a statement of that.
    /// </summary>
    public int Order => 155;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<ContentDbContext>(configuration, this);
        services.AddValidatedOptions<ContentOptions>(configuration, ContentOptions.SectionName);

        // Who is asking, and whether they may put something in front of a shopper.
        services.AddScoped<ContentScope>();

        // The single place a block is validated, and the single place one is resolved for rendering.
        services.AddScoped<PageBlockBinder>();
        services.AddScoped<ContentRenderer>();
        services.AddScoped<PageComposer>();
        services.AddScoped<MenuComposer>();

        // Rules into rows.
        services.AddScoped<CollectionMaterializer>();

        // What a crawler is served.
        services.AddScoped<SitemapBuilder>();
        services.AddScoped<StructuredDataBuilder>();

        AddEventHandlers(services);

        // Declared in code and seeded into platform.feature_flags, exactly as every module's are.
        services.AddSingleton<IFeatureFlagSource, ContentFeatureFlagSource>();

        // Off in the API and on in the worker, exactly as every sweeper before them.
        services.AddHostedService<ContentSchedulerWorker>();
        services.AddHostedService<CollectionRefreshWorker>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var store = endpoints.MapGroup("/store");
        store.MapStoreContentEndpoints();

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminContentEndpoints();
    }

    /// <summary>
    /// Subscribes to the three catalogue facts that can change whether a product belongs in a rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An offer going live, its terms changing — which moves the price and the discount a rule may be
    /// about — and its withdrawal. Between them they cover everything a rule can be written against
    /// except the passage of time, and the periodic sweep covers that.
    /// </para>
    /// <para>
    /// Stock is deliberately absent, and it is the subscription somebody will try to add. A collection
    /// includes out-of-stock products by default, precisely so a curated page does not silently shrink
    /// because a supplier is late; subscribing to the highest-volume event in the platform in order to
    /// remove and re-add rows nobody wanted removed would be the most expensive way to make a page
    /// worse.
    /// </para>
    /// </remarks>
    private static void AddEventHandlers(IServiceCollection services)
    {
        services.AddScoped<CatalogContentHandlers>();

        services.AddScoped<IIntegrationEventHandler<ListingPublished>>(
            provider => provider.GetRequiredService<CatalogContentHandlers>());

        services.AddScoped<IIntegrationEventHandler<ListingUpdated>>(
            provider => provider.GetRequiredService<CatalogContentHandlers>());

        services.AddScoped<IIntegrationEventHandler<ListingDeactivated>>(
            provider => provider.GetRequiredService<CatalogContentHandlers>());
    }
}
