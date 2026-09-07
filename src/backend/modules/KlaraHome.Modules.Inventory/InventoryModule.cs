using KlaraHome.Contracts.Inventory;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Inventory.Endpoints;
using KlaraHome.Modules.Inventory.Infrastructure;
using KlaraHome.Modules.Inventory.Infrastructure.Ageing;
using KlaraHome.Modules.Inventory.Infrastructure.Events;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using KlaraHome.Modules.Inventory.Infrastructure.Stock;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Inventory;

/// <summary>
/// How many there are, and where: warehouses, an append-only stock ledger, time-limited
/// reservations, and the replenishment paperwork behind them.
/// </summary>
/// <remarks>
/// <para>
/// It owns <em>quantity</em>. What a thing is belongs to Catalog, what it costs after a promotion
/// belongs to Pricing, and who sells it belongs to Vendors — this module holds a plain
/// <c>listing_id</c> and a plain <c>vendor_id</c> and asks those modules over their contracts.
/// </para>
/// <para>
/// Its own contract, <see cref="IStockAvailability"/>, is what Cart, Checkout and Orders use: read
/// what is available, hold it, and settle the hold. That interface is the oversell boundary of the
/// platform, and everything behind it exists to make one promise keepable — two shoppers racing for
/// the last unit produce one sale and one refusal (docs/02-domain-model.md §4.2).
/// </para>
/// </remarks>
public sealed class InventoryModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "inventory";

    /// <inheritdoc />
    public string Name => "Inventory";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// After Catalog, whose listings its stock rows are keyed on, and before Pricing, Cart and
    /// Orders, all of which ask it what is available before they commit to anything.
    /// </summary>
    public int Order => 70;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<InventoryDbContext>(configuration, this);
        services.AddValidatedOptions<InventoryOptions>(configuration, InventoryOptions.SectionName);

        services.AddScoped<InventoryScope>();
        services.AddScoped<InventoryEventPublisher>();

        // The only way stock moves. Everything that changes a quantity goes through it, so that the
        // conditional update, the ledger entry and the events are written together or not at all.
        services.AddScoped<StockLedgerService>();

        // The published contract: availability and holds, so Cart and Orders can reason about stock
        // without a query that crosses a schema.
        services.AddScoped<IStockAvailability, StockAvailabilityService>();

        // Step 28B. Where the units behind a hold actually are, for the two callers that must know:
        // the order that records it and the pick list that sends somebody to a shelf.
        services.AddScoped<IStockAllocation, StockAllocationService>();

        // The seam goods come back through, added at Step 17. It is one direction only: taking stock
        // out is what a sale does and already has its own path, and a returns queue must not be able
        // to reach it.
        services.AddScoped<IStockRestock, StockRestockService>();

        // A read-only seam over the ledger, added at Step 21. Reporting needs to know how long the
        // stock currently on a shelf has been there, and that is the one inventory fact no
        // integration event carries: StockLevelChanged says what the balance is, never when the units
        // making it up arrived.
        services.AddScoped<IInventoryAgeing, InventoryAgeingService>();

        // Inventory reacts to an offer's life cycle: a listing that goes live gets a stock row, and
        // one whose SKU changes gets its label refreshed.
        services.AddScoped<ListingLifecycleHandlers>();
        services.AddScoped<IIntegrationEventHandler<Contracts.Catalog.ListingPublished>>(
            provider => provider.GetRequiredService<ListingLifecycleHandlers>());
        services.AddScoped<IIntegrationEventHandler<Contracts.Catalog.ListingUpdated>>(
            provider => provider.GetRequiredService<ListingLifecycleHandlers>());
        services.AddScoped<IIntegrationEventHandler<Contracts.Catalog.ListingDeactivated>>(
            provider => provider.GetRequiredService<ListingLifecycleHandlers>());

        // Both loops are off in the API and on in the worker, exactly as the catalogue job runner
        // and the notification dispatcher are configured.
        // Stock behind the demonstration offers, without which every demo product renders as
        // unavailable. Registered only outside Production and only when DemoData:SeedCatalog is on.
        services.AddDemoDataSeeder<Infrastructure.Seeding.DemoStockSeeder>(configuration);

        services.AddHostedService<ReservationSweeper>();
        services.AddHostedService<StockReconciliationJob>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminWarehouseEndpoints();
        admin.MapAdminStockEndpoints();
        admin.MapAdminPurchasingEndpoints();
        admin.MapAdminStockTakeEndpoints();
    }
}
