using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Orders.Endpoints;
using KlaraHome.Modules.Orders.Infrastructure;
using KlaraHome.Modules.Orders.Infrastructure.Events;
using KlaraHome.Modules.Orders.Infrastructure.Fulfilment;
using KlaraHome.Modules.Orders.Infrastructure.Invoicing;
using KlaraHome.Modules.Orders.Infrastructure.Jobs;
using KlaraHome.Modules.Orders.Infrastructure.Lifecycle;
using KlaraHome.Modules.Orders.Infrastructure.Numbering;
using KlaraHome.Modules.Orders.Infrastructure.Payments;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using KlaraHome.Modules.Orders.Infrastructure.Placement;
using KlaraHome.Modules.Orders.Infrastructure.Returns;
using KlaraHome.Modules.Orders.Infrastructure.Reviews;
using KlaraHome.Modules.Orders.Infrastructure.Settlements;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KlaraHome.Modules.Orders;

/// <summary>
/// The record of what was agreed, and its life from payment to settlement.
/// </summary>
/// <remarks>
/// <para>
/// This module owns <em>the sale</em>. Cart owns the shopper's intent and hands it over priced and
/// agreed; from that moment the order is the authority on what was bought, at what price, under what
/// tax, and where it stands. Nothing here re-prices, re-taxes or re-resolves anything: every figure
/// is copied from the quote and frozen, because a second calculation is how an invoice and a
/// settlement come to disagree about what a customer paid.
/// </para>
/// <para>
/// The shape is the marketplace's. An order belongs to a shopper and splits into one sub-order per
/// seller, and the sub-order is what actually lives: it carries the status, it is what a seller
/// packs, what a courier collects, what a tax invoice is raised against under that seller's GSTIN,
/// and what Settlements pays out on. The order's own status is derived from its parts and never set
/// (docs/02-domain-model.md §5.2) — two sellers in one basket move independently, and a single
/// stored status could only ever describe one of them.
/// </para>
/// <para>
/// It fills the seam Cart declared at Step 13 by registering <see cref="IOrderPlacement"/>, and
/// declares one of its own: <see cref="IPaymentInitiation"/>, implemented here by a refusal that
/// names the reason until Step 15. Cash on delivery does not go through it and works today.
/// </para>
/// </remarks>
public sealed class OrdersModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "orders";

    /// <inheritdoc />
    public string Name => "Orders";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// After Cart, whose seam it fills, and before Payments, Shipping, Returns and Settlements, each
    /// of which reacts to what this module publishes.
    /// </summary>
    public int Order => 100;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<OrdersDbContext>(configuration, this);
        services.AddValidatedOptions<OrdersOptions>(configuration, OrdersOptions.SectionName);

        services.AddScoped<OrdersScope>();
        services.AddScoped<OrdersEventPublisher>();

        // Gapless numbering, and the transaction-scoped lock it depends on.
        services.AddScoped<OrderNumbering>();

        // The single place a sub-order moves, and the single place an invoice is raised. Every
        // handler goes through them, so the storefront's cancel and the admin's cancel cannot drift.
        services.AddScoped<InvoiceService>();
        services.AddScoped<SubOrderWorkflow>();

        // The seam Cart declared at Step 13. Registered unconditionally, so it replaces the polite
        // refusal Cart registers with TryAdd for a deployment that has no Ordering module.
        services.AddScoped<IOrderPlacement, OrderPlacementService>();

        // The seam this module declares. TryAdd, so Step 15 replaces it by registering its own.
        services.TryAddScoped<IPaymentInitiation, UnavailablePaymentInitiation>();

        // The return leg of it, added at Step 15: how Payments confirms, fails and mirrors refunds
        // against an order. Registered unconditionally, because this module owns the state machine
        // and there is no other implementation of it — a deployment without Payments simply never
        // calls it.
        services.AddScoped<IOrderPaymentSync, OrderPaymentSyncService>();

        // The same arrangement for logistics, added at Step 16: how Shipping reads what a parcel is
        // booked from and relays a courier's word back into the state machine. One implementation,
        // registered unconditionally, for the same reason.
        services.AddScoped<IOrderFulfilment, OrderFulfilmentService>();

        // And the same again for the post-delivery leg, added at Step 17: what a line is still worth
        // and how much of it is left, the three return states the machine has, and a place to record
        // what actually came back. It is deliberately narrower than the other two — no price may be
        // changed through it and nothing may be cancelled — because a returns queue is not a place
        // that should be able to alter a sale.
        services.AddScoped<IOrderReturns, OrderReturnsService>();

        // Added at Step 18, and the only one of the four that cannot write. Settlements reads what
        // was sold, what it was worth and what the platform charged for it — the commission frozen on
        // the line, never a rate resolved today — and does everything else in its own schema. A seam
        // through which a settlement run could change the sale it is settling is the first thing an
        // auditor would object to.
        services.AddScoped<IOrderSettlement, OrderSettlementService>();

        // Added at Step 21, and narrower still: it answers whether a given person received a given
        // line, and nothing else. It exists because "a review can only be posted against a delivered
        // purchase" has to be decided by the module that owns the state machine deciding what
        // delivered means, rather than by a second opinion in the module that stores the stars.
        services.AddScoped<IOrderPurchases, OrderPurchasesService>();

        // Off in the API and on in the worker, exactly as the catalogue job runner, the notification
        // dispatcher, the reservation sweeper and the abandoned-cart sweeper are configured.
        services.AddHostedService<OrderLifecycleSweeper>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var store = endpoints.MapGroup("/store");
        store.MapStoreOrderEndpoints();

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminOrderEndpoints();
    }
}
