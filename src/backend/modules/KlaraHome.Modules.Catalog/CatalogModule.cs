using KlaraHome.Contracts.Catalog;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.Modules.Catalog.Application.Products;
using KlaraHome.Modules.Catalog.Endpoints;
using KlaraHome.Modules.Catalog.Infrastructure;
using KlaraHome.Modules.Catalog.Infrastructure.Directory;
using KlaraHome.Modules.Catalog.Infrastructure.Events;
using KlaraHome.Modules.Catalog.Infrastructure.Import;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Catalog;

/// <summary>
/// The product information model: taxonomy, products, variants and the per-vendor offers that make
/// them purchasable.
/// </summary>
/// <remarks>
/// <para>
/// This is the module every commerce module downstream of it points at. Inventory keys its stock on
/// a <c>listing_id</c>, a cart line holds one, an order line freezes a snapshot of one, and a
/// settlement is computed against the commission category of the product behind it. None of them
/// may join to this schema, so <see cref="IProductCatalog"/> is how those ids become facts.
/// </para>
/// <para>
/// It owns <em>what</em> a thing is. How many there are is Inventory's, what it costs after a
/// promotion is Pricing's, and who is selling it is the Vendors module's — Catalog holds a plain
/// <c>vendor_id</c> and asks that module over <c>IVendorDirectory</c> whether the seller may trade.
/// </para>
/// </remarks>
public sealed class CatalogModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "catalog";

    /// <inheritdoc />
    public string Name => "Catalog";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// After Vendors, whose sellers own the listings this module stores, and before Inventory,
    /// Pricing, Cart and Orders, all of which hold a listing id.
    /// </summary>
    public int Order => 60;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<CatalogDbContext>(configuration, this);
        services.AddValidatedOptions<CatalogOptions>(configuration, CatalogOptions.SectionName);

        // Bulk import and export put their files in object storage rather than in a table. The
        // registration is idempotent, and the Media module has already made it in a host that has
        // both.
        services.AddKlaraHomeStorage(configuration);

        services.AddScoped<CatalogScope>();
        services.AddScoped<ProductReader>();
        services.AddScoped<ProductWriter>();
        services.AddScoped<CatalogEventPublisher>();
        services.AddScoped<ProductImportRunner>();

        // The published contract: a read-only projection of the listings table, so Inventory, Cart
        // and Orders can resolve an offer without a query that crosses a schema.
        services.AddScoped<IProductCatalog, ProductCatalogDirectory>();

        // Its wider sibling, added at Step 19. A read-model needs the words a shopper searches by
        // and the names a facet is labelled with, none of which a cart line has any use for — and
        // it needs the buy box resolved by the rule this module owns, so that a search result and
        // the product page it links to can never name two different sellers.
        services.AddScoped<IProductProjectionSource, ProductProjectionSource>();

        // The narrowest of the three, added at Step 20. The CMS has to list every browsable URL the
        // store has and to name the ancestors above a category, and neither is answerable from a
        // projection that holds a leaf's name and a path of ids but none of the names along it.
        services.AddScoped<ICatalogTaxonomy, CatalogTaxonomyDirectory>();

        // Catalog reacts to a seller's life cycle: activation lets their offers go live again, and
        // a suspension takes every one of them out of the storefront.
        services.AddScoped<VendorLifecycleHandlers>();
        services.AddScoped<
            KlaraHome.Infrastructure.Persistence.Outbox.IIntegrationEventHandler<Contracts.Vendors.VendorActivated>>(
            provider => provider.GetRequiredService<VendorLifecycleHandlers>());
        services.AddScoped<
            KlaraHome.Infrastructure.Persistence.Outbox.IIntegrationEventHandler<Contracts.Vendors.VendorSuspended>>(
            provider => provider.GetRequiredService<VendorLifecycleHandlers>());
        services.AddScoped<
            KlaraHome.Infrastructure.Persistence.Outbox.IIntegrationEventHandler<Contracts.Vendors.VendorOffboarded>>(
            provider => provider.GetRequiredService<VendorLifecycleHandlers>());

        services.AddHostedService<CatalogJobDispatcher>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminTaxonomyEndpoints();
        admin.MapAdminProductEndpoints();
        admin.MapAdminListingEndpoints();
        admin.MapAdminCatalogJobEndpoints();

        var store = endpoints.MapGroup("/store");
        store.MapStoreCatalogEndpoints();
    }
}
