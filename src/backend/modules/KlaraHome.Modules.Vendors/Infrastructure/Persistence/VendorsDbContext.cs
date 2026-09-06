using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Vendors.Infrastructure.Persistence;

/// <summary>
/// The Vendors module's data access.
/// </summary>
/// <remarks>
/// This is the first context to take a caller context for anything other than form: five of its
/// seven tables are <see cref="KlaraHome.SharedKernel.Domain.IVendorScoped"/>, so the global vendor
/// filter is doing real work here — a vendor user querying pickup locations gets their own seller's
/// and no others, whatever the handler asked for.
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller, read by the vendor query filter.</param>
internal sealed class VendorsDbContext(
    DbContextOptions<VendorsDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => VendorsModule.SchemaName;

    /// <summary>Every seller this deployment has, in every state.</summary>
    public DbSet<Vendor> Vendors => Set<Vendor>();

    /// <summary>Who operates each seller's account.</summary>
    public DbSet<VendorUser> VendorUsers => Set<VendorUser>();

    /// <summary>The documents sellers submitted, and what became of them.</summary>
    public DbSet<VendorKycDocument> KycDocuments => Set<VendorKycDocument>();

    /// <summary>Where each seller's payouts go.</summary>
    public DbSet<VendorBankAccount> BankAccounts => Set<VendorBankAccount>();

    /// <summary>Where couriers collect from.</summary>
    public DbSet<VendorPickupLocation> PickupLocations => Set<VendorPickupLocation>();

    /// <summary>Where each seller is willing to deliver.</summary>
    public DbSet<VendorServiceableRegion> ServiceableRegions => Set<VendorServiceableRegion>();

    /// <summary>What the platform charges its sellers.</summary>
    public DbSet<CommissionPlan> CommissionPlans => Set<CommissionPlan>();

    /// <summary>The category and price-band overrides within those plans.</summary>
    public DbSet<CommissionPlanRule> CommissionPlanRules => Set<CommissionPlanRule>();

    /// <summary>The sequence behind a generated vendor code.</summary>
    /// <remarks>
    /// A database sequence rather than a count of rows, because a count is a race: two applications
    /// arriving together would both read seventeen and both try to be <c>VND-000017</c>. It is also
    /// not the primary key — the id is a UUIDv7 — so a gap left by a rolled-back transaction costs
    /// nothing, which is exactly why a sequence is safe to use here and would not be for an invoice
    /// number.
    /// </remarks>
    public const string CodeSequenceName = "vendor_code_seq";

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasSequence<long>(CodeSequenceName, VendorsModule.SchemaName).StartsAt(1).IncrementsBy(1);

        modelBuilder.ApplyConfiguration(new VendorConfiguration());
        modelBuilder.ApplyConfiguration(new VendorUserConfiguration());
        modelBuilder.ApplyConfiguration(new VendorKycDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new VendorBankAccountConfiguration());
        modelBuilder.ApplyConfiguration(new VendorPickupLocationConfiguration());
        modelBuilder.ApplyConfiguration(new VendorServiceableRegionConfiguration());
        modelBuilder.ApplyConfiguration(new CommissionPlanConfiguration());
        modelBuilder.ApplyConfiguration(new CommissionPlanRuleConfiguration());
    }
}
