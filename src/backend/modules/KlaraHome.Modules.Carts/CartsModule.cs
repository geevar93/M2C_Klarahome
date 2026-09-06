using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Shipping;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Carts.Application.Carts;
using KlaraHome.Modules.Carts.Endpoints;
using KlaraHome.Modules.Carts.Infrastructure;
using KlaraHome.Modules.Carts.Infrastructure.Carts;
using KlaraHome.Modules.Carts.Infrastructure.Checkout;
using KlaraHome.Modules.Carts.Infrastructure.Events;
using KlaraHome.Modules.Carts.Infrastructure.Jobs;
using KlaraHome.Modules.Carts.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KlaraHome.Modules.Carts;

/// <summary>
/// From "add to cart" to a validated, priced, ready-to-pay checkout.
/// </summary>
/// <remarks>
/// <para>
/// This module owns <em>the shopper's intent</em>: what they have chosen, where they want it, how
/// they mean to pay for it, and whether any of that has stopped being possible since they chose it.
/// What a thing is belongs to Catalog, how many there are to Inventory, what it costs to Pricing,
/// and what was finally agreed to Orders — this module holds plain ids and asks those modules over
/// their contracts.
/// </para>
/// <para>
/// It computes no money. Every figure on a cart, a checkout and an order comes from
/// <c>IPriceQuoteEngine</c>, and the snapshot this module takes at placement is what Orders copies
/// onto the order — which is what makes the confirmation the same number the shopper agreed to.
/// </para>
/// <para>
/// It holds no stock either, until the moment of placement. Reserving at add-to-cart would make
/// every browsing shopper a denial of service against every buying one; the hold is taken in
/// <c>place-order</c>, against the cart, and Inventory's sweeper releases it if the order never
/// happens (docs/02-domain-model.md §4.2).
/// </para>
/// <para>
/// It declares two seams that later steps fill: <see cref="IShippingOptions"/>, implemented here by
/// a degenerate free-standard-delivery quoter until Step 16, and <see cref="IOrderPlacement"/>,
/// which refuses politely until Step 14. Both are registered with <c>TryAdd</c>, so the module that
/// owns them simply replaces them.
/// </para>
/// </remarks>
public sealed class CartsModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "carts";

    /// <inheritdoc />
    public string Name => "Cart";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// After Pricing, whose engine prices every basket, and before Orders, Payments and Shipping,
    /// each of which replaces one of the seams this module declares.
    /// </summary>
    public int Order => 90;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<CartsDbContext>(configuration, this);
        services.AddValidatedOptions<CartsOptions>(configuration, CartsOptions.SectionName);

        services.AddScoped<CartsScope>();
        services.AddScoped<CartsEventPublisher>();

        // Finding the caller's basket, and merging the guest one into it on login. Every storefront
        // cart endpoint starts here, and none of them takes a cart id.
        services.AddScoped<CartCookie>();
        services.AddScoped<CartResolver>();

        // The single place cart validation and pricing live. One renderer serves the cart page, the
        // checkout review and the operator's view, which is what stops them disagreeing.
        services.AddScoped<CartRenderer>();
        services.AddScoped<CartWorkflow>();
        services.AddScoped<CheckoutWorkflow>();

        // The two seams. TryAdd, so Step 16 and Step 14 replace them by registering their own.
        services.TryAddScoped<IShippingOptions, StandardShippingOptions>();
        services.TryAddScoped<IOrderPlacement, UnavailableOrderPlacement>();

        // Off in the API and on in the worker, exactly as the catalogue job runner, the notification
        // dispatcher and the reservation sweeper are configured.
        services.AddHostedService<AbandonedCartSweeper>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var store = endpoints.MapGroup("/store");
        store.MapStoreCartEndpoints();
        store.MapStoreCheckoutEndpoints();

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminCartEndpoints();
    }
}
