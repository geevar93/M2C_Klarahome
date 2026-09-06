using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Shipping.Infrastructure.Persistence;

/// <summary>
/// The Shipping module's data access.
/// </summary>
/// <remarks>
/// <para>
/// Two halves with different scoping, and the split is deliberate. The rate card — zones, rates,
/// serviceability — is the platform's, read by everybody and written by nobody but staff. The
/// operational half — consignments, failed deliveries, handover sheets — belongs to a seller, and
/// carries the vendor scope so a seller working their own dispatch queue cannot see another's.
/// </para>
/// <para>
/// A rate rule is the one row that spans both. It has a nullable <c>vendor_id</c> because a seller
/// may have negotiated their own pricing, and the vendor query filter therefore has to admit rows
/// with no seller at all — which is exactly what the shared convention does for a platform-owned
/// row.
/// </para>
/// <para>
/// The courier event log has no vendor scope, because a webhook has no caller. It is worked by whoever
/// holds the plumbing permission, exactly as the payments gateway log is.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller.</param>
internal sealed class ShippingDbContext(
    DbContextOptions<ShippingDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => ShippingModule.SchemaName;

    /// <summary>The delivery map.</summary>
    public DbSet<ShippingZone> Zones => Set<ShippingZone>();

    /// <summary>What the customer pays, by zone, service, weight and basket value.</summary>
    public DbSet<ShippingRate> Rates => Set<ShippingRate>();

    /// <summary>What a courier last said about a PIN code.</summary>
    public DbSet<ServiceabilityEntry> Serviceability => Set<ServiceabilityEntry>();

    /// <summary>Parcels.</summary>
    public DbSet<Shipment> Shipments => Set<Shipment>();

    /// <summary>What is in them, which is what makes a partial shipment expressible.</summary>
    public DbSet<ShipmentLine> ShipmentLines => Set<ShipmentLine>();

    /// <summary>Every scan. Append-only, and partitioned monthly.</summary>
    public DbSet<TrackingEvent> TrackingEvents => Set<TrackingEvent>();

    /// <summary>Failed delivery attempts and what was decided about them.</summary>
    public DbSet<NdrRecord> NdrRecords => Set<NdrRecord>();

    /// <summary>Handover sheets.</summary>
    public DbSet<ShippingManifest> Manifests => Set<ShippingManifest>();

    /// <summary>Every courier webhook, exactly as it arrived. Replay index and dead-letter queue in one.</summary>
    public DbSet<CourierEvent> CourierEvents => Set<CourierEvent>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new ShippingZoneConfiguration());
        modelBuilder.ApplyConfiguration(new ShippingRateConfiguration());
        modelBuilder.ApplyConfiguration(new ServiceabilityEntryConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentLineConfiguration());
        modelBuilder.ApplyConfiguration(new TrackingEventConfiguration());
        modelBuilder.ApplyConfiguration(new NdrRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ShippingManifestConfiguration());
        modelBuilder.ApplyConfiguration(new CourierEventConfiguration());
    }
}
