using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Shipping;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Shipping.Application.Rates;
using KlaraHome.Modules.Shipping.Endpoints;
using KlaraHome.Modules.Shipping.Infrastructure;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Courier.Shiprocket;
using KlaraHome.Modules.Shipping.Infrastructure.Events;
using KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;
using KlaraHome.Modules.Shipping.Infrastructure.Jobs;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.Modules.Shipping.Infrastructure.Processing;
using KlaraHome.Modules.Shipping.Infrastructure.Quoting;
using KlaraHome.Modules.Shipping.Infrastructure.Rating;
using KlaraHome.Modules.Shipping.Infrastructure.Serviceability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Shipping;

/// <summary>
/// Getting parcels moving, and keeping everybody informed about them.
/// </summary>
/// <remarks>
/// <para>
/// This module owns the conversation with couriers and the map of what delivery costs. It knows a
/// destination, a weight and a cash figure; it does not know what a promotion was, what a shopper
/// paid in total, or what a seller is charged in commission. That narrowness is the same discipline
/// Payments keeps, and for the same reason — a shipping schema that grew a copy of the sale would be
/// a second answer to what an order contains.
/// </para>
/// <para>
/// Four properties hold whatever else changes. <b>The customer's price and the platform's cost are
/// separate figures</b>, from the rate card and the aggregator respectively, and both land on the
/// parcel so the margin on delivery is a query. <b>Serviceability is never asked on a request
/// path</b>: a product page and a checkout read a cached table, and a nightly job is what keeps it
/// current. <b>Tracking is webhook-first with a polling fallback</b>, because a webhook is a
/// courtesy and not a guarantee. And <b>every movement of a sub-order goes back through the ordering
/// state machine</b> over <see cref="IOrderFulfilment"/>, so a courier scan and an operator's click
/// do exactly the same things.
/// </para>
/// <para>
/// It fills the seam the Cart module declared at Step 13 by registering <see cref="IShippingOptions"/>,
/// so a checkout now offers real services at real prices instead of one free standard option.
/// </para>
/// <para>
/// The aggregator credentials are blank by default and that is the shippable state. An unconfigured
/// deployment registers every adapter, books through the manual one — an operator types the air
/// waybill their courier gave them — and everything downstream works unchanged: the label prints,
/// the order ships, the timeline fills in, and the cash reconciles. Filling three values into
/// configuration is the whole of turning an aggregator on.
/// </para>
/// </remarks>
public sealed class ShippingModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "shipping";

    /// <inheritdoc />
    public string Name => "Shipping";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// After Payments, whose cash-on-delivery records it settles, and before Returns, which books
    /// reverse pickups through the same adapters.
    /// </summary>
    public int Order => 120;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<ShippingDbContext>(configuration, this);
        services.AddValidatedOptions<ShippingOptions>(configuration, ShippingOptions.SectionName);

        services.AddScoped<ShippingScope>();
        services.AddScoped<ShippingEventPublisher>();

        AddProviders(services);

        // The single place a parcel moves, the single place one is booked, the single place a
        // courier's word is applied. Every route — a webhook, the polling fallback, an operator's
        // click — goes through them, so the three cannot drift about what "delivered" means.
        services.AddScoped<ShipmentWorkflow>();
        services.AddScoped<ShipmentBooker>();
        services.AddScoped<CourierEventProcessor>();
        services.AddScoped<TrackingSynchroniser>();
        services.AddScoped<RateResolver>();
        services.AddScoped<ServiceabilityService>();

        // Where this store is willing to deliver, which is a different question from what a courier
        // will carry and is answered from a settings row rather than a cache (ADR-018).
        services.AddScoped<DeliveryCoverageService>();

        // The seam Cart declared at Step 13. Registered unconditionally, so it replaces the
        // free-standard-delivery quoter that module registers with TryAdd.
        services.AddScoped<IShippingOptions, RatedShippingOptions>();

        // The seam Returns books a collection through, added at Step 17. A reverse pickup is a
        // shipment like any other with its two addresses the other way round — booked by the same
        // adapters, tracked by the same webhook receiver, and visible in the same parcel list.
        services.AddScoped<IReversePickup, ReversePickupService>();

        AddEventHandlers(services);

        services.AddDataSeeder<ShippingRateCardSeeder>();

        // All three off in the API and on in the worker, exactly as the outbox dispatcher, the
        // notification dispatcher and every sweeper before them.
        services.AddHostedService<CourierEventWorker>();
        services.AddHostedService<TrackingPollWorker>();
        services.AddHostedService<ServiceabilityRefreshWorker>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var store = endpoints.MapGroup("/store");
        store.MapStoreShippingEndpoints();

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminShippingEndpoints();

        // Not under /store or /admin. A webhook is a third surface with its own authentication — a
        // signature rather than a token — and docs/04-api-specification.md §5 gives it its own prefix
        // so no policy meant for a human caller can ever be applied to it by accident.
        endpoints.MapShippingWebhookEndpoints();
    }

    /// <summary>
    /// Registers the courier adapters and the outbound client the aggregator shares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both adapters are registered whether or not either is configured. An unconfigured aggregator
    /// reports <c>IsConfigured == false</c> and the registry falls through to the manual adapter,
    /// which is always usable — which is what a fresh deployment with no logistics account should
    /// look like, and is why this module can ship before its credentials exist.
    /// </para>
    /// <para>
    /// The client carries a bounded timeout and an allow-list derived from the configured base URL
    /// (docs/07-security-compliance.md §3). Every request it makes carries the aggregator's bearer
    /// token, so a misdirected one would be a credential disclosure rather than a failed call.
    /// </para>
    /// </remarks>
    private static void AddProviders(IServiceCollection services)
    {
        services.AddTransient<ShiprocketAllowedHostHandler>();

        services
            .AddHttpClient(ShiprocketHttp.ClientName)
            .AddHttpMessageHandler<ShiprocketAllowedHostHandler>();

        // Every adapter this build has, registered together and told apart by their keys. The
        // registry picks the one `Shipping:Provider` names and falls through to `manual` when it
        // names nothing this build knows (ADR-018) - so adding a courier is one line here.
        //
        // Singletons, because an adapter caches a bearer token: one per process rather than one per
        // request is the difference between a login a week and a login a second.
        services.AddSingleton<IShippingProvider, ShiprocketShippingProvider>();
        services.AddSingleton<IShippingProvider, ManualShippingProvider>();
        services.AddSingleton<ShippingProviderRegistry>();
    }

    /// <summary>
    /// Subscribes to the two facts about an order that concern parcels.
    /// </summary>
    /// <remarks>
    /// A confirmed seller's part becomes a parcel waiting to be packed, and a cancellation withdraws
    /// one that has not left. Neither is a booking: nothing is asked of a courier until a human has
    /// put the box on a scale.
    /// </remarks>
    private static void AddEventHandlers(IServiceCollection services)
    {
        services.AddScoped<OrderLifecycleHandlers>();

        services.AddScoped<IIntegrationEventHandler<SubOrderConfirmed>>(
            provider => provider.GetRequiredService<OrderLifecycleHandlers>());

        services.AddScoped<IIntegrationEventHandler<SubOrderCancelled>>(
            provider => provider.GetRequiredService<OrderLifecycleHandlers>());
    }
}
