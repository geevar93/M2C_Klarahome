using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Contracts.Returns;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Settlements.Endpoints;
using KlaraHome.Modules.Settlements.Infrastructure;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;
using KlaraHome.Modules.Settlements.Infrastructure.Events;
using KlaraHome.Modules.Settlements.Infrastructure.Jobs;
using KlaraHome.Modules.Settlements.Infrastructure.Numbering;
using KlaraHome.Modules.Settlements.Infrastructure.Payouts;
using KlaraHome.Modules.Settlements.Infrastructure.Payouts.Razorpay;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Settlements;

/// <summary>
/// What each seller is owed, and the money that discharges it.
/// </summary>
/// <remarks>
/// <para>
/// This module owns one number per seller and everything that moves it. A delivered parcel earns; a
/// commission, a marketplace fee, a gateway's cut and a courier's bill come off it; a cancellation or
/// a credit note reverses part of it; the state takes its two deductions at the end of a period; and
/// a payout discharges what is left. Nothing else in the platform is allowed to have an opinion about
/// what a seller is owed.
/// </para>
/// <para>
/// Four properties hold whatever else changes. <b>The ledger is append-only and a balance is a sum</b>
/// — never a column, so it cannot drift from its own history, and a correction is a reversing entry
/// rather than an edit. <b>Nothing is earned before the money is the platform's</b>: a prepaid sale on
/// delivery, a cash sale when the courier remits, and never a day earlier. <b>What was charged was
/// decided when the order was placed</b> and is read off the frozen order line, so a plan that
/// changes today does not re-price last month. And <b>every posting is idempotent by a unique index
/// on a key derived from the fact</b>, which is what makes at-least-once event delivery safe on a
/// table that moves money.
/// </para>
/// <para>
/// It reads the sale and it never writes to it. The seam it reaches ordering through is read-only, so
/// a settlement run cannot change its own inputs — which is a different guarantee from being careful,
/// and the one an auditor can check.
/// </para>
/// <para>
/// Like Shipping and Returns before it, it works with no third-party credentials at all. A deployment
/// with no payout account still earns, still deducts, still closes periods and still builds and
/// approves batches; the transfer refuses with a named error instead of pretending money moved, and
/// the ledger goes on saying to the paisa what every seller is owed.
/// </para>
/// </remarks>
public sealed class SettlementsModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "settlements";

    /// <inheritdoc />
    public string Name => "Settlements";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// Last of Phase D, after Returns — whose credit notes it reverses a seller's supply on, and
    /// whose refunds it deliberately does not.
    /// </summary>
    public int Order => 140;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<SettlementsDbContext>(configuration, this);
        services.AddValidatedOptions<SettlementsOptions>(configuration, SettlementsOptions.SectionName);
        services.AddValidatedOptions<PayoutOptions>(configuration, PayoutOptions.SectionName);
        services.AddValidatedOptions<RazorpayPayoutOptions>(configuration, RazorpayPayoutOptions.SectionName);

        services.AddScoped<SettlementsScope>();
        services.AddScoped<SettlementsEventPublisher>();
        services.AddScoped<PayoutNumbering>();

        // The single place the ledger is written to, the single place a period is closed, and the
        // single place a batch is built, signed off and sent. Every route — an endpoint, an
        // integration event, a scheduler, a reconciliation sweep — goes through them, so none of the
        // three can drift about what an earning is worth or what a reversal gives back.
        services.AddScoped<SettlementPoster>();
        services.AddScoped<SettlementCycleService>();
        services.AddScoped<PayoutWorkflow>();

        AddPayoutProviders(services);
        AddEventHandlers(services);

        // Off in the API and on in the worker, exactly as every scheduler and sweeper before them.
        services.AddHostedService<SettlementCycleWorker>();
        services.AddHostedService<PayoutReconciliationWorker>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminSettlementEndpoints();
    }

    /// <summary>
    /// Registers every payout rail this build has, told apart by their keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registry picks the one <c>Payouts:Provider</c> names and falls through to the adapter that
    /// sends nothing when it names nothing this build knows or nothing whose credentials are filled
    /// in — the arrangement ADR-018 settled on for couriers, applied to money. Adding a rail is one
    /// line here.
    /// </para>
    /// <para>
    /// Scoped rather than singleton, unlike the courier adapters. Those cache a bearer token across
    /// requests; these authenticate with a Basic credential on every call, so there is nothing for a
    /// singleton to hold and a scoped lifetime keeps them beside the workflow that uses them.
    /// </para>
    /// </remarks>
    private static void AddPayoutProviders(IServiceCollection services)
    {
        services.AddTransient<RazorpayPayoutAllowedHostHandler>();

        services
            .AddHttpClient(RazorpayPayoutHttp.ClientName)
            .AddHttpMessageHandler<RazorpayPayoutAllowedHostHandler>();

        services.AddScoped<IPayoutProvider, RazorpayRoutePayoutProvider>();
        services.AddScoped<IPayoutProvider, RazorpayXPayoutProvider>();
        services.AddScoped<IPayoutProvider, UnconfiguredPayoutProvider>();
        services.AddScoped<PayoutProviderRegistry>();
    }

    /// <summary>
    /// Subscribes to the four facts that between them are the whole of a seller's earnings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A parcel arriving and a courier remitting are the same fact for two payment methods, and both
    /// earn. A cancellation and a credit note are the two documents that undo a supply, and both
    /// reverse.
    /// </para>
    /// <para>
    /// <c>RefundProcessed</c> is deliberately absent, and it is the one subscription somebody will
    /// try to add. This ledger reverses supplies, not payments: a refund is money moving in response
    /// to one of the two documents above, and subscribing to all three would reverse the same sale
    /// twice.
    /// </para>
    /// </remarks>
    private static void AddEventHandlers(IServiceCollection services)
    {
        services.AddScoped<SettlementLifecycleHandlers>();

        services.AddScoped<IIntegrationEventHandler<SubOrderStatusChanged>>(
            provider => provider.GetRequiredService<SettlementLifecycleHandlers>());

        services.AddScoped<IIntegrationEventHandler<SubOrderCancelled>>(
            provider => provider.GetRequiredService<SettlementLifecycleHandlers>());

        services.AddScoped<IIntegrationEventHandler<CodCashRecorded>>(
            provider => provider.GetRequiredService<SettlementLifecycleHandlers>());

        services.AddScoped<IIntegrationEventHandler<CreditNoteIssued>>(
            provider => provider.GetRequiredService<SettlementLifecycleHandlers>());
    }
}
