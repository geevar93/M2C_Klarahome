using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Pricing.Endpoints;
using KlaraHome.Modules.Pricing.Infrastructure;
using KlaraHome.Modules.Pricing.Infrastructure.Calculation;
using KlaraHome.Modules.Pricing.Infrastructure.Events;
using KlaraHome.Modules.Pricing.Infrastructure.Persistence;
using KlaraHome.Modules.Pricing.Infrastructure.Promotions;
using KlaraHome.Modules.Pricing.Infrastructure.Wallets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Pricing;

/// <summary>
/// What a basket costs, and why: price lists, the GST engine, promotions and store credit.
/// </summary>
/// <remarks>
/// <para>
/// This module owns <em>money</em>. It is the only place in the platform permitted to compute a
/// price, a discount or a tax figure, and <see cref="IPriceQuoteEngine"/> is how cart, checkout,
/// orders and invoices all get one. That single-implementation rule is the whole point: a
/// marketplace with two pieces of code that both compute GST has two answers to what a customer
/// owes, and the discrepancy surfaces at a tax return rather than at a code review.
/// </para>
/// <para>
/// What a thing is belongs to Catalog, how many there are to Inventory, and who sells it to
/// Vendors — this module holds a plain <c>listing_id</c> and asks those modules over their
/// contracts. It publishes <c>Pricing.PriceChanged</c>, which Search and the price-drop
/// subscribers reproject from (docs/02-domain-model.md §6).
/// </para>
/// </remarks>
public sealed class PricingModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "pricing";

    /// <inheritdoc />
    public string Name => "Pricing";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// After Catalog and Inventory, whose offers it prices and whose stock a cart checks alongside
    /// the price, and before Cart, Orders, Payments and Settlements, every one of which reads a
    /// quote rather than computing one.
    /// </summary>
    public int Order => 80;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<PricingDbContext>(configuration, this);
        services.AddValidatedOptions<PricingOptions>(configuration, PricingOptions.SectionName);

        services.AddScoped<PricingScope>();
        services.AddScoped<PricingEventPublisher>();

        // The two resolvers behind the engine. Registered separately because the admin surface
        // explains their answers directly — "which list won" and "which rate was in force" are
        // questions an operator asks without wanting a whole quote.
        services.AddScoped<PriceResolver>();
        services.AddScoped<TaxRateResolver>();

        // The published contracts. IPriceCatalog is the narrow read Search and the storefront use;
        // IPriceQuoteEngine is the itemised calculation everything downstream of the cart uses.
        services.AddScoped<IPriceCatalog>(provider => provider.GetRequiredService<PriceResolver>());
        services.AddScoped<IPriceQuoteEngine, QuoteEngine>();

        // Committing what a quote merely evaluated. Both are called when an order is placed or
        // cancelled, never when a cart is rendered.
        services.AddScoped<IPromotionLedger, PromotionLedger>();
        services.AddScoped<StoreCreditService>();
        services.AddScoped<IStoreCredit>(provider => provider.GetRequiredService<StoreCreditService>());

        // Two flags this module reads: the wallet, off until the business has decided its loyalty
        // rules, and the coupon switch, which exists so a leaked code can be stopped without a
        // deploy.
        services.AddSingleton<IFeatureFlagSource, PricingFeatureFlags>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminPriceListEndpoints();
        admin.MapAdminTaxRateEndpoints();
        admin.MapAdminPromotionEndpoints();
        admin.MapAdminWalletEndpoints();

        var store = endpoints.MapGroup("/store");
        store.MapStorePricingEndpoints();
    }
}
