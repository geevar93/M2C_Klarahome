using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Payments.Endpoints;
using KlaraHome.Modules.Payments.Infrastructure;
using KlaraHome.Modules.Payments.Infrastructure.Events;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.Modules.Payments.Infrastructure.Gateway.Razorpay;
using KlaraHome.Modules.Payments.Infrastructure.Initiation;
using KlaraHome.Modules.Payments.Infrastructure.Jobs;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.Modules.Payments.Infrastructure.Processing;
using KlaraHome.Modules.Payments.Infrastructure.Refunds;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Payments;

/// <summary>
/// Taking money, and being able to account for every rupee of it afterwards.
/// </summary>
/// <remarks>
/// <para>
/// This module owns the conversation with the gateway and nothing else. It knows an order id, an
/// amount and a currency; it does not know what was bought, from whom, or where it is going. That
/// narrowness is deliberate — a payments schema that grew a copy of the sale would be a second
/// answer to what a customer owes, and the whole design of Steps 12 to 14 exists to make sure there
/// is only one.
/// </para>
/// <para>
/// Three properties hold whatever else changes. <b>Card data never reaches these servers</b>: the
/// instrument is collected by the gateway's own hosted widget, which is what keeps this merchant in
/// PCI-DSS SAQ-A scope (ADR-008). <b>Order truth comes from the webhook plus an API re-fetch</b>,
/// never from the browser and never from a webhook body alone — the signature proves who sent a
/// claim, not that the claim is current. And <b>every route into the module reduces to one
/// workflow</b>, so a webhook, a reconciliation sweep, an operator's <em>sync</em> and a manual
/// capture all confirm an order in exactly the same way.
/// </para>
/// <para>
/// It fills the seam Orders declared at Step 14 by registering <see cref="IPaymentInitiation"/>, so
/// a prepaid placement now returns a real gateway order rather than a polite 503. Cash on delivery
/// goes through the same interface as a second provider with nothing behind it, because cash is
/// still money that has to be expected at a door, collected, remitted and reconciled.
/// </para>
/// <para>
/// The gateway credentials are blank by default and that is the shippable state. An unconfigured
/// deployment registers every adapter, reports the gateway unusable, and refuses a collection with a
/// named 503 — filling three values into configuration is the whole of turning payments on.
/// </para>
/// </remarks>
public sealed class PaymentsModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "payments";

    /// <inheritdoc />
    public string Name => "Payments";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// After Orders, whose seam it fills and whose events it consumes, and before Settlements, which
    /// pays sellers out of what this module collected.
    /// </summary>
    public int Order => 110;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<PaymentsDbContext>(configuration, this);
        services.AddValidatedOptions<PaymentsOptions>(configuration, PaymentsOptions.SectionName);

        // A top-level section rather than a child of Payments, so the environment variables are the
        // ones docs/06-infrastructure-devops.md already publishes: Razorpay__KeyId and its siblings.
        services.AddValidatedOptions<RazorpayOptions>(configuration, RazorpayOptions.SectionName);

        services.AddScoped<PaymentsScope>();
        services.AddScoped<PaymentsEventPublisher>();

        AddProviders(services);

        // The single place a payment moves, the single place a refund is sent, and the single place
        // a webhook becomes a change. Every route goes through them, so the webhook's idea of
        // "captured" and the reconciliation sweep's cannot drift.
        services.AddScoped<PaymentWorkflow>();
        services.AddScoped<RefundDispatcher>();
        services.AddScoped<GatewayEventProcessor>();
        services.AddScoped<PaymentReconciliationService>();
        services.AddScoped<SettlementIngestionService>();

        // The seam Orders declared at Step 14. Registered unconditionally, so it replaces the polite
        // refusal Orders registers with TryAdd for a deployment that has no Payments module.
        services.AddScoped<IPaymentInitiation, PaymentInitiationService>();

        // The seam Shipping reaches cash on delivery through, added at Step 16. Only the module that
        // books couriers knows which consignment the cash is on and what became of it; the ledger
        // stays here, because two answers to "what has the courier not remitted" is how a seller
        // ends up settled out of money nobody collected.
        services.AddScoped<ICodCollections, Infrastructure.Cod.CodCollections>();

        // The seam Returns sends money back through, added at Step 17. It resolves the collection
        // itself rather than taking one, so a returns queue never has to know what an order was paid
        // with — and it goes through the same threshold every other refund does, so a large one still
        // waits for a second pair of eyes.
        services.AddScoped<IRefundInitiation, RefundInitiationService>();

        AddEventHandlers(services);

        // All three off in the API and on in the worker, exactly as the outbox dispatcher, the
        // notification dispatcher, the catalogue job runner and every sweeper before them.
        services.AddHostedService<GatewayEventWorker>();
        services.AddHostedService<PaymentReconciliationWorker>();
        services.AddHostedService<SettlementIngestionWorker>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var store = endpoints.MapGroup("/store");
        store.MapStorePaymentEndpoints();

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminPaymentEndpoints();

        // Not under /store or /admin. A webhook is a third surface with its own authentication —
        // a signature rather than a token — and docs/04-api-specification.md §2 gives it its own
        // prefix so no policy meant for a human caller can ever be applied to it by accident.
        endpoints.MapPaymentWebhookEndpoints();
    }

    /// <summary>
    /// Registers the gateway adapters and the outbound client they share.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both adapters are registered whether or not they are configured. An unconfigured one reports
    /// <c>IsConfigured == false</c> and every path that needs it answers a named 503 — which is what
    /// a fresh deployment with no merchant account should look like, and is why this module can ship
    /// before its credentials exist.
    /// </para>
    /// <para>
    /// The client carries a bounded timeout and the host allow-list docs/07-security-compliance.md §3
    /// requires. Every request it makes carries the merchant's API secret in a header, so a
    /// misdirected one would be a credential disclosure rather than a failed call.
    /// </para>
    /// </remarks>
    private static void AddProviders(IServiceCollection services)
    {
        services.AddTransient<RazorpayAllowedHostHandler>();

        services
            .AddHttpClient(RazorpayHttp.ClientName)
            .AddHttpMessageHandler<RazorpayAllowedHostHandler>();

        services.AddSingleton<IPaymentProvider, RazorpayPaymentProvider>();
        services.AddSingleton<IPaymentProvider, InternalCodPaymentProvider>();
        services.AddSingleton<PaymentProviderRegistry>();
    }

    /// <summary>
    /// Subscribes to the two facts about an order that concern money.
    /// </summary>
    /// <remarks>
    /// A confirmed cash-on-delivery parcel opens a cash record, and a cancellation of something
    /// already paid for raises the refund without waiting for anybody to notice. Neither is the
    /// payment itself — that arrives from the gateway, not from an event.
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
