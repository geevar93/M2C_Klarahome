using KlaraHome.Contracts.Shipping;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Returns.Application.Reasons;
using KlaraHome.Modules.Returns.Application.Returns;
using KlaraHome.Modules.Returns.Endpoints;
using KlaraHome.Modules.Returns.Infrastructure;
using KlaraHome.Modules.Returns.Infrastructure.Documents;
using KlaraHome.Modules.Returns.Infrastructure.Events;
using KlaraHome.Modules.Returns.Infrastructure.Jobs;
using KlaraHome.Modules.Returns.Infrastructure.Numbering;
using KlaraHome.Modules.Returns.Infrastructure.Persistence;
using KlaraHome.Modules.Returns.Infrastructure.Policy;
using KlaraHome.Modules.Returns.Infrastructure.Processing;
using KlaraHome.Modules.Returns.Infrastructure.Refunds;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Returns;

/// <summary>
/// What happens after a parcel arrives and the shopper does not want it.
/// </summary>
/// <remarks>
/// <para>
/// This module owns the decision that goods are coming back and what that is worth. It does not own
/// the money, the stock or the courier: a refund goes through the Payments module's own maker–checker
/// control, units move through the Inventory module's append-only ledger, and a reverse pickup is a
/// shipment like any other. Four seams, four modules, and this one calculates.
/// </para>
/// <para>
/// Four properties hold whatever else changes. <b>Nothing is refunded before somebody has looked at
/// what came back</b>, unless the reason code says the goods need not come back at all — which is a
/// decision a business takes deliberately and not one a handler takes for it. <b>The tax credited is
/// the tax that was charged</b>, apportioned from the frozen order line and never recomputed, so a
/// credit note agrees with the invoice it credits. <b>The credit note is raised whether or not money
/// moves</b>, because section 34 of the CGST Act is about the supply and not about the card. And
/// <b>every movement of a return goes through one transition table</b>, so a courier scan, an
/// operator's click and a shopper's withdrawal all do exactly the same things.
/// </para>
/// <para>
/// It is the second module — after Shipping — that works with no third-party credentials at all. A
/// deployment with no gateway refunds to store credit; a deployment with no logistics account takes
/// a waybill an operator typed in. Both paths are the ordinary ones with a different adapter behind
/// them, not a degraded mode.
/// </para>
/// </remarks>
public sealed class ReturnsModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "returns";

    /// <inheritdoc />
    public string Name => "Returns";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// After Shipping, whose reverse pickups it books and whose scans it listens to, and before
    /// Settlements, which reverses a seller's earning on the credit notes it raises.
    /// </summary>
    public int Order => 130;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<ReturnsDbContext>(configuration, this);
        services.AddValidatedOptions<ReturnsOptions>(configuration, ReturnsOptions.SectionName);

        services.AddScoped<ReturnsScope>();
        services.AddScoped<ReturnsEventPublisher>();
        services.AddScoped<ReturnNumbering>();
        services.AddScoped<ReturnLoader>();

        // The single place a return moves, the single place one is inspected, the single place one is
        // paid out, and the single place a courier is asked. Every route — an endpoint, a courier
        // scan, a sweep — goes through them, so none of the four can drift about what "received"
        // means or what a refund is worth.
        services.AddScoped<ReturnWorkflow>();
        services.AddScoped<ReturnInspectionService>();
        services.AddScoped<ReversePickupCoordinator>();
        services.AddScoped<ReturnRefundService>();
        services.AddScoped<CreditNoteService>();
        services.AddScoped<ReturnPolicyService>();

        AddEventHandlers(services);

        services.AddDataSeeder<ReturnReasonSeeder>();

        // Off in the API and on in the worker, exactly as every sweeper before it.
        services.AddHostedService<StaleReturnWorker>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var store = endpoints.MapGroup("/store");
        store.MapStoreReturnEndpoints();

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminReturnEndpoints();
    }

    /// <summary>
    /// Subscribes to the one fact about a parcel that concerns returned goods.
    /// </summary>
    /// <remarks>
    /// A single subscription carries both halves of the problem. A scan on a reverse consignment
    /// moves the return riding on it; a scan saying a parcel reached the seller again is goods coming
    /// back that nobody asked to return, and the units go back on supply. Subscribing once rather
    /// than to two narrower events is what makes them visibly the same mechanism.
    /// </remarks>
    private static void AddEventHandlers(IServiceCollection services)
    {
        services.AddScoped<ShippingLifecycleHandlers>();

        services.AddScoped<IIntegrationEventHandler<ShipmentTrackingUpdated>>(
            provider => provider.GetRequiredService<ShippingLifecycleHandlers>());
    }
}
