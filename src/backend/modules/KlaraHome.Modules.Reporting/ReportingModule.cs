using KlaraHome.Contracts.Carts;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Returns;
using KlaraHome.Contracts.Settlements;
using KlaraHome.Contracts.Shipping;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Reporting.Endpoints;
using KlaraHome.Modules.Reporting.Infrastructure;
using KlaraHome.Modules.Reporting.Infrastructure.Export;
using KlaraHome.Modules.Reporting.Infrastructure.Features;
using KlaraHome.Modules.Reporting.Infrastructure.Ingest;
using KlaraHome.Modules.Reporting.Infrastructure.Jobs;
using KlaraHome.Modules.Reporting.Infrastructure.Persistence;
using KlaraHome.Modules.Reporting.Infrastructure.Query;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Reporting;

/// <summary>
/// The numbers the business is run on.
/// </summary>
/// <remarks>
/// <para>
/// The module that owns no business rule and may change nothing. Everything it holds is a copy of
/// something another module decided, and everything it answers is a sum over those copies — which is
/// why it is the last module in the registration order and the only one nothing else consumes.
/// </para>
/// <para>
/// It keeps its own facts because docs/01-architecture.md §2.1 forbids a cross-module table read
/// outright, and a reporting module that quietly ignored that would be the one place in the system
/// where the boundaries were decoration. So thirteen integration-event subscriptions write one row
/// per transactional row, denormalised at the moment they land with the category, the seller and the
/// payment method already on them. A report is then a filtered aggregation over a single table with
/// no join anywhere in the module, the numbers reconcile against the transactional data because each
/// row corresponds to one transactional row, and there is no staleness window to explain to somebody
/// asking why yesterday's figure moved.
/// </para>
/// <para>
/// There are deliberately no rollup tables and no materialised views, which is a departure from what
/// docs/03-database-design.md §4.18 originally described and is recorded as ADR-021. A materialised
/// view over another module's schema is a cross-schema read with a different name on it; a rollup
/// over these facts would buy precomputation this workload does not need and cost a second set of
/// numbers to explain when the two disagreed.
/// </para>
/// <para>
/// The one thing no event carries is how long the stock currently on a shelf has been there —
/// <c>StockLevelChanged</c> says what the balance is, and no sequence of those says when the units
/// making it up arrived. That is a daily snapshot taken through <c>IInventoryAgeing</c>, a read-only
/// seam added here and implemented by Inventory over its own ledger, and it is the only scheduled
/// write of a fact in the module.
/// </para>
/// </remarks>
public sealed class ReportingModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "reporting";

    /// <inheritdoc />
    public string Name => "Reporting";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// Last. It consumes from six modules and is consumed by none, so it registers after everything
    /// it reads and the order is a statement of that.
    /// </summary>
    public int Order => 200;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<ReportingDbContext>(configuration, this);
        services.AddValidatedOptions<ReportingOptions>(configuration, ReportingOptions.SectionName);

        // Who is asking, and whose figures they are entitled to. The whole of authorisation here.
        services.AddScoped<ReportingScope>();

        // The single place a declared report becomes a table, and the single place one becomes a file.
        services.AddScoped<ReportQueryEngine>();
        services.AddScoped<ReportExporter>();

        AddEventHandlers(services);

        // Declared in code and seeded into platform.feature_flags, exactly as every module's are.
        services.AddSingleton<IFeatureFlagSource, ReportingFeatureFlagSource>();

        // Off in the API and on in the worker, exactly as every sweeper before them.
        services.AddHostedService<InventorySnapshotWorker>();
        services.AddHostedService<ReportScheduleWorker>();
    }

    /// <inheritdoc />
    /// <remarks>
    /// There is no storefront surface and there never will be. Every number this module holds is
    /// commercial, and none of it is anybody's business but the people running the platform and the
    /// sellers reading their own figures.
    /// </remarks>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminReportingEndpoints();
    }

    /// <summary>
    /// Subscribes to everything that is a fact about the business.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Thirteen subscriptions across six modules, which is more than any other module in the platform
    /// has and is exactly what a reporting module should look like: the events it does <em>not</em>
    /// subscribe to are the ones nothing is measured about.
    /// </para>
    /// <para>
    /// One event is deliberately absent and somebody will try to add it.
    /// <c>Inventory.StockLevelChanged</c> is the highest-volume message on the platform and it would
    /// tell this module nothing it can report on — a balance is a state, and every stock question in
    /// the thirteen reports is either about a movement, which arrives as a sale or a return, or about
    /// age, which the daily snapshot answers.
    /// </para>
    /// </remarks>
    private static void AddEventHandlers(IServiceCollection services)
    {
        services.AddScoped<CommerceFactHandlers>();

        // Written out rather than looped, exactly as every other module writes its subscriptions.
        // A generic helper would need a cast through `object` to satisfy the compiler, which is a
        // worse thing to leave in a registration than fourteen readable lines.
        services.AddScoped<IIntegrationEventHandler<OrderPlaced>>(Handlers);
        services.AddScoped<IIntegrationEventHandler<SubOrderConfirmed>>(Handlers);
        services.AddScoped<IIntegrationEventHandler<SubOrderCancelled>>(Handlers);
        services.AddScoped<IIntegrationEventHandler<SubOrderStatusChanged>>(Handlers);
        services.AddScoped<IIntegrationEventHandler<OrderCompleted>>(Handlers);
        services.AddScoped<IIntegrationEventHandler<PaymentCaptured>>(Handlers);
        services.AddScoped<IIntegrationEventHandler<RefundProcessed>>(Handlers);
        services.AddScoped<IIntegrationEventHandler<CodCashRecorded>>(Handlers);
        services.AddScoped<IIntegrationEventHandler<CartAbandoned>>(Handlers);
        services.AddScoped<IIntegrationEventHandler<CartConverted>>(Handlers);
        services.AddScoped<IIntegrationEventHandler<ShipmentDelivered>>(Handlers);
        services.AddScoped<IIntegrationEventHandler<ReturnRequested>>(Handlers);
        services.AddScoped<IIntegrationEventHandler<ReturnClosed>>(Handlers);
        services.AddScoped<IIntegrationEventHandler<SettlementCycleClosed>>(Handlers);
    }

    /// <summary>Resolves the one class that implements all fourteen subscriptions.</summary>
    /// <param name="provider">The scope being served.</param>
    private static CommerceFactHandlers Handlers(IServiceProvider provider)
        => provider.GetRequiredService<CommerceFactHandlers>();
}
