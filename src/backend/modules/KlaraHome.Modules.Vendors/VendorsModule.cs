using KlaraHome.Contracts.Vendors;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Infrastructure.Security;
using KlaraHome.Modules.Vendors.Application;
using KlaraHome.Modules.Vendors.Endpoints;
using KlaraHome.Modules.Vendors.Infrastructure;
using KlaraHome.Modules.Vendors.Infrastructure.Commission;
using KlaraHome.Modules.Vendors.Infrastructure.Directory;
using KlaraHome.Modules.Vendors.Infrastructure.Payments;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using KlaraHome.Modules.Vendors.Infrastructure.Seeding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Vendors;

/// <summary>
/// Sellers: onboarding and its state machine, KYC, bank accounts, commission plans, staff, pickup
/// points and serviceability.
/// </summary>
/// <remarks>
/// <para>
/// This is the module that makes the platform a marketplace rather than a shop. Everything
/// downstream of it holds a <c>vendor_id</c> — a listing, a sub-order, an invoice, a ledger entry —
/// and asks this module, over <see cref="IVendorDirectory"/> and
/// <see cref="ICommissionResolver"/>, the two questions it needs answered: may this seller trade,
/// and what do we charge them.
/// </para>
/// <para>
/// It publishes the first integration events this platform raises. Activation, suspension and
/// offboarding are announced through the outbox in the transaction that caused them, so a consumer
/// acting on one is acting on something that certainly happened (ADR-003).
/// </para>
/// </remarks>
public sealed class VendorsModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "vendors";

    /// <inheritdoc />
    public string Name => "Vendors";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// After Platform (settings, reference data, audit), Identity (the caller and their vendor
    /// scope), Media (KYC scans) and Notifications, and before every commerce module, all of which
    /// hold a seller's id.
    /// </summary>
    public int Order => 50;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<VendorsDbContext>(configuration, this);
        services.AddValidatedOptions<VendorOptions>(configuration, VendorOptions.SectionName);

        // Column encryption for the bank account number. Idempotent, and registered here because
        // this is the first module that stores a value it must be able to read back but must not
        // store in the clear (docs/07-security-compliance.md §5).
        services.AddKlaraHomeFieldProtection(configuration);

        services.AddScoped<VendorScope>();
        services.AddScoped<VendorReadinessService>();
        services.AddScoped<VendorEventPublisher>();
        services.AddScoped<VendorPayoutProvisioner>();

        // The published contracts. Both are read-only projections of this module's tables, so other
        // modules can ask their two questions without a query that crosses a schema.
        services.AddScoped<IVendorDirectory, VendorDirectory>();
        services.AddScoped<ICommissionResolver, CommissionResolver>();

        // The Razorpay Route seam, with the implementation that is honest about creating nothing.
        // Step 18 replaces this registration and nothing else.
        services.AddScoped<IVendorPayoutAccounts, UnprovisionedPayoutAccounts>();

        services.AddDataSeeder<CommissionPlanSeeder>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminVendorEndpoints();
        admin.MapAdminCommissionPlanEndpoints();

        var store = endpoints.MapGroup("/store");
        store.MapStoreVendorEndpoints();
    }
}
