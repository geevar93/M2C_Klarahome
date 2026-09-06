using System.Text.Json;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Shipping.Infrastructure.Persistence.Configurations;

/// <summary>How the open-shaped columns in this schema are written.</summary>
internal static class ShippingJson
{
    /// <summary>
    /// camelCase, matching the API payloads these documents are handed to and received from. A
    /// <c>jsonb</c> column read by an admin screen is part of the API surface, and a casing
    /// convention applied on one side and not the other is a class of bug worth designing out.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <summary>The Postgres type every open-shaped column in this schema uses.</summary>
    public const string ColumnType = "jsonb";

    /// <summary>
    /// A document is replaced wholesale rather than edited, so reference equality is the honest
    /// comparison and the deep copy is simply the same instance.
    /// </summary>
    public static ValueComparer<TDocument> Comparer<TDocument>()
        where TDocument : class
        => new(
            (left, right) => ReferenceEquals(left, right),
            document => document == null ? 0 : document.GetHashCode(),
            document => document);
}

/// <summary>The <c>CHECK</c> lists, written once so a column and its constraint cannot drift apart.</summary>
internal static class ShippingCheckConstraints
{
    /// <summary>The values <c>shipments.status</c> and <c>tracking_events.status</c> accept.</summary>
    public const string ShipmentStatuses =
        "status IN ('Draft', 'Created', 'LabelGenerated', 'PickupScheduled', 'PickedUp', 'InTransit', "
        + "'OutForDelivery', 'Delivered', 'Exception', 'RtoInitiated', 'RtoDelivered', 'Cancelled')";

    /// <summary>The values <c>shipping_rates.method</c> accepts.</summary>
    public const string ShippingMethods = "method IN ('Standard', 'Express')";

    /// <summary>The values <c>ndr_records.reason_code</c> accepts.</summary>
    public const string NdrReasons =
        "reason_code IN ('Other', 'CustomerUnavailable', 'AddressIncorrect', 'Refused', "
        + "'RescheduleRequested', 'CodNotReady', 'Unreachable')";

    /// <summary>The values <c>ndr_records.action</c> accepts.</summary>
    public const string NdrActions =
        "action IN ('Pending', 'Reattempt', 'Rescheduled', 'AddressUpdated', 'ReturnToOrigin', 'Resolved')";

    /// <summary>The values <c>courier_events.status</c> accepts.</summary>
    public const string CourierEventStatuses =
        "status IN ('Pending', 'Processed', 'Ignored', 'Failed', 'DeadLettered')";

    /// <summary>Six digits, no leading zero. The same shape the Vendors and Platform schemas enforce.</summary>
    public const string DestinationPincode = "destination_pincode ~ '^[1-9][0-9]{5}$'";

    /// <summary>The same, for the serviceability cache.</summary>
    public const string ServiceabilityPincode = "pincode ~ '^[1-9][0-9]{5}$'";

    /// <summary>
    /// A weight band that goes backwards prices nothing, and a value band that does prices nothing
    /// either. Both are refused at the database as well as in the domain.
    /// </summary>
    public const string RateBands =
        "min_weight_grams >= 0 AND max_weight_grams >= min_weight_grams "
        + "AND min_order_value >= 0 AND (max_order_value IS NULL OR max_order_value >= min_order_value)";

    /// <summary>Money on a rate rule can only ever be positive.</summary>
    public const string RateAmounts =
        "base_rate >= 0 AND per_kg_rate >= 0 AND cod_fee >= 0 AND (free_above IS NULL OR free_above >= 0)";

    /// <summary>A promise that ends before it starts is not a promise.</summary>
    public const string RateEta = "eta_min_days >= 0 AND eta_max_days >= eta_min_days";

    /// <summary>Weights and money on a consignment are never negative.</summary>
    public const string ShipmentAmounts =
        "weight_grams >= 0 AND (charged_weight_grams IS NULL OR charged_weight_grams >= 0) "
        + "AND declared_value >= 0 AND freight_charged >= 0 "
        + "AND (freight_cost IS NULL OR freight_cost >= 0) AND (cod_amount IS NULL OR cod_amount >= 0)";

    /// <summary>
    /// A booked parcel has a courier and an air waybill; an unbooked one has neither.
    /// </summary>
    /// <remarks>
    /// The database's own answer to the one inconsistency that would be expensive: a consignment that
    /// says it is in transit with nothing to track it by. The domain refuses it too, which is exactly
    /// why this is worth having — the day it fires, something has written a shipment without going
    /// through the aggregate.
    /// </remarks>
    public const string ShipmentBooking =
        "(status = 'Draft') OR (status = 'Cancelled') OR (awb IS NOT NULL AND courier IS NOT NULL)";
}

/// <summary>Maps <see cref="ShippingZone"/> to <c>shipping.shipping_zones</c>.</summary>
internal sealed class ShippingZoneConfiguration : IEntityTypeConfiguration<ShippingZone>
{
    public void Configure(EntityTypeBuilder<ShippingZone> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("shipping_zones");

        builder.HasKey(zone => zone.Id);
        builder.Property(zone => zone.Id).ValueGeneratedNever();

        builder.Property(zone => zone.Code).HasMaxLength(32).IsRequired();
        builder.Property(zone => zone.Name).HasMaxLength(128).IsRequired();

        // Documents rather than child tables. A zone's geography is read whole every time a rate is
        // looked up and is never joined to or filtered on in SQL — the matching happens in memory
        // against a handful of zones, and two extra tables would buy nothing but joins.
        builder.Property(zone => zone.States)
            .HasColumnType(ShippingJson.ColumnType)
            .HasConversion(
                states => JsonSerializer.Serialize(states, ShippingJson.Options),
                json => JsonSerializer.Deserialize<IReadOnlyList<Guid>>(json, ShippingJson.Options)!,
                ShippingJson.Comparer<IReadOnlyList<Guid>>());

        builder.Property(zone => zone.PincodeRanges)
            .HasColumnType(ShippingJson.ColumnType)
            .HasConversion(
                ranges => JsonSerializer.Serialize(ranges, ShippingJson.Options),
                json => JsonSerializer.Deserialize<IReadOnlyList<PincodeRange>>(json, ShippingJson.Options)!,
                ShippingJson.Comparer<IReadOnlyList<PincodeRange>>());

        // One zone per code per tenant. The code is what a rate rule and an operator both name.
        builder.HasIndex(zone => new { zone.TenantId, zone.Code }).IsUnique();

        // The lookup order: every rate resolution walks the active zones by priority.
        builder.HasIndex(zone => new { zone.TenantId, zone.IsActive, zone.Priority });

        builder.Ignore(zone => zone.DomainEvents);
        builder.Ignore(zone => zone.IsCatchAll);
    }
}

/// <summary>Maps <see cref="ShippingRate"/> to <c>shipping.shipping_rates</c>.</summary>
internal sealed class ShippingRateConfiguration : IEntityTypeConfiguration<ShippingRate>
{
    public void Configure(EntityTypeBuilder<ShippingRate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("shipping_rates", table =>
        {
            table.HasCheckConstraint("ck_shipping_rates_method", ShippingCheckConstraints.ShippingMethods);
            table.HasCheckConstraint("ck_shipping_rates_bands", ShippingCheckConstraints.RateBands);
            table.HasCheckConstraint("ck_shipping_rates_amounts", ShippingCheckConstraints.RateAmounts);
            table.HasCheckConstraint("ck_shipping_rates_eta", ShippingCheckConstraints.RateEta);
        });

        builder.HasKey(rate => rate.Id);
        builder.Property(rate => rate.Id).ValueGeneratedNever();

        builder.Property(rate => rate.Method).HasConversion<string>().HasMaxLength(16);

        builder.Property(rate => rate.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        foreach (var money in new[]
                 {
                     nameof(ShippingRate.MinOrderValue),
                     nameof(ShippingRate.MaxOrderValue),
                     nameof(ShippingRate.BaseRate),
                     nameof(ShippingRate.PerKgRate),
                     nameof(ShippingRate.FreeAbove),
                     nameof(ShippingRate.CodFee),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        // The resolution query: the active rules for a zone and a service, seller's own first.
        builder.HasIndex(rate => new { rate.TenantId, rate.ZoneId, rate.Method, rate.IsActive });
        builder.HasIndex(rate => new { rate.TenantId, rate.VendorId });

        builder.Ignore(rate => rate.DomainEvents);
        builder.Ignore(rate => rate.WeightBandWidth);
    }
}

/// <summary>Maps <see cref="ServiceabilityEntry"/> to <c>shipping.serviceability_cache</c>.</summary>
internal sealed class ServiceabilityEntryConfiguration : IEntityTypeConfiguration<ServiceabilityEntry>
{
    public void Configure(EntityTypeBuilder<ServiceabilityEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("serviceability_cache", table => table.HasCheckConstraint(
            "ck_serviceability_cache_pincode",
            ShippingCheckConstraints.ServiceabilityPincode));

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.Pincode).HasMaxLength(6).IsRequired();
        builder.Property(entry => entry.Courier).HasMaxLength(64).IsRequired();

        // One answer per courier per PIN code. This is what makes the refresh an upsert rather than
        // an append, and it is why a nightly job can run twice without doubling the table.
        builder.HasIndex(entry => new { entry.TenantId, entry.Pincode, entry.Courier }).IsUnique();

        // The refresh order: oldest first, so a job that is interrupted resumes where it stopped.
        builder.HasIndex(entry => new { entry.TenantId, entry.RefreshedAt });

        builder.Ignore(entry => entry.DomainEvents);
    }
}

/// <summary>Maps <see cref="Shipment"/> to <c>shipping.shipments</c>.</summary>
internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("shipments", table =>
        {
            table.HasCheckConstraint("ck_shipments_status", ShippingCheckConstraints.ShipmentStatuses);
            table.HasCheckConstraint("ck_shipments_pincode", ShippingCheckConstraints.DestinationPincode);
            table.HasCheckConstraint("ck_shipments_amounts", ShippingCheckConstraints.ShipmentAmounts);
            table.HasCheckConstraint("ck_shipments_booking", ShippingCheckConstraints.ShipmentBooking);
        });

        builder.HasKey(shipment => shipment.Id);
        builder.Property(shipment => shipment.Id).ValueGeneratedNever();

        builder.Property(shipment => shipment.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(shipment => shipment.SubOrderNumber).HasMaxLength(40).IsRequired();
        builder.Property(shipment => shipment.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(shipment => shipment.StatusReason).HasMaxLength(500);
        builder.Property(shipment => shipment.Provider).HasMaxLength(32).IsRequired();
        builder.Property(shipment => shipment.Courier).HasMaxLength(64);
        builder.Property(shipment => shipment.ServiceName).HasMaxLength(64);
        builder.Property(shipment => shipment.Awb).HasMaxLength(64);
        builder.Property(shipment => shipment.ProviderShipmentId).HasMaxLength(64);
        builder.Property(shipment => shipment.TrackingUrl).HasMaxLength(512);
        builder.Property(shipment => shipment.LabelObjectKey).HasMaxLength(512);
        builder.Property(shipment => shipment.PickupPincode).HasMaxLength(6);
        builder.Property(shipment => shipment.DestinationPincode).HasMaxLength(6).IsRequired();

        builder.Property(shipment => shipment.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        foreach (var money in new[]
                 {
                     nameof(Shipment.CodAmount),
                     nameof(Shipment.DeclaredValue),
                     nameof(Shipment.FreightCharged),
                     nameof(Shipment.FreightCost),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        // A document rather than three columns: the box is read whole, written whole, and only ever
        // used to derive one number.
        builder.Property(shipment => shipment.Dimensions)
            .HasColumnType(ShippingJson.ColumnType)
            .HasConversion(
                dimensions => JsonSerializer.Serialize(dimensions, ShippingJson.Options),
                json => JsonSerializer.Deserialize<ShipmentDimensions>(json, ShippingJson.Options)!,
                ShippingJson.Comparer<ShipmentDimensions>());

        builder.HasMany(shipment => shipment.Lines)
            .WithOne()
            .HasForeignKey(line => line.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(shipment => shipment.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The lookup a courier webhook makes, and the reason a tracking update is a single indexed
        // read rather than a scan of every parcel in flight. Filtered, because a draft has no waybill
        // and there may be thousands of those.
        builder.HasIndex(shipment => new { shipment.TenantId, shipment.Awb })
            .IsUnique()
            .HasFilter("awb IS NOT NULL");

        // The seller's dispatch queue, and the shape every admin list uses.
        builder.HasIndex(shipment => new { shipment.TenantId, shipment.VendorId, shipment.Status });

        // "Where is the parcel for this order" — asked by support on every second call.
        builder.HasIndex(shipment => new { shipment.TenantId, shipment.SubOrderId });
        builder.HasIndex(shipment => new { shipment.TenantId, shipment.OrderId });

        // The polling fallback's own query: booked, unfinished, and quiet for too long.
        builder.HasIndex(shipment => new { shipment.TenantId, shipment.LastTrackedAt });

        builder.Ignore(shipment => shipment.DomainEvents);
        builder.Ignore(shipment => shipment.DeadWeightGrams);
        builder.Ignore(shipment => shipment.IsBooked);
        builder.Ignore(shipment => shipment.IsCod);
        builder.Ignore(shipment => shipment.HasLabel);
    }
}

/// <summary>Maps <see cref="ShipmentLine"/> to <c>shipping.shipment_lines</c>.</summary>
internal sealed class ShipmentLineConfiguration : IEntityTypeConfiguration<ShipmentLine>
{
    public void Configure(EntityTypeBuilder<ShipmentLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("shipment_lines", table => table.HasCheckConstraint(
            "ck_shipment_lines_quantity",
            "quantity > 0 AND unit_weight_grams >= 0 AND declared_value >= 0"));

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.Sku).HasMaxLength(64).IsRequired();
        builder.Property(line => line.Name).HasMaxLength(256).IsRequired();

        builder.Property(line => line.DeclaredValue).HasColumnType(ModelConventions.MoneyColumnType);

        // "How many of this order line have actually been sent" — the sum over this index, and the
        // check every partial shipment is validated against.
        builder.HasIndex(line => new { line.TenantId, line.OrderLineId });

        builder.Ignore(line => line.DomainEvents);
        builder.Ignore(line => line.WeightGrams);
    }
}

/// <summary>
/// Maps <see cref="TrackingEvent"/> to <c>shipping.tracking_events</c>.
/// </summary>
/// <remarks>
/// The table is created by hand and excluded from migrations: it is
/// <c>PARTITION BY RANGE (occurred_at)</c> (docs/03-database-design.md §8), and a partitioned table
/// is created partitioned or not at all. The mapping still describes every column, because this is
/// what the tracking service and the admin surface query through.
/// </remarks>
internal sealed class TrackingEventConfiguration : IEntityTypeConfiguration<TrackingEvent>
{
    public void Configure(EntityTypeBuilder<TrackingEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tracking_events", table => table.ExcludeFromMigrations());

        // Composite, and in this order: PostgreSQL requires the partition key to be part of every
        // unique constraint on a partitioned table.
        builder.HasKey(entry => new { entry.OccurredAt, entry.Id });

        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.ProviderEventId).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(entry => entry.CourierStatus).HasMaxLength(64);
        builder.Property(entry => entry.Location).HasMaxLength(128);
        builder.Property(entry => entry.Remark).HasMaxLength(500);
        builder.Property(entry => entry.Raw).HasColumnType(ShippingJson.ColumnType);

        builder.Ignore(entry => entry.DomainEvents);
    }
}

/// <summary>Maps <see cref="NdrRecord"/> to <c>shipping.ndr_records</c>.</summary>
internal sealed class NdrRecordConfiguration : IEntityTypeConfiguration<NdrRecord>
{
    public void Configure(EntityTypeBuilder<NdrRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ndr_records", table =>
        {
            table.HasCheckConstraint("ck_ndr_records_reason", ShippingCheckConstraints.NdrReasons);
            table.HasCheckConstraint("ck_ndr_records_action", ShippingCheckConstraints.NdrActions);
            table.HasCheckConstraint("ck_ndr_records_attempt", "attempt_number > 0");

            // A worked report has a decision and a closing time; an open one has neither. Without
            // this the two states blur and the queue stops being a queue.
            table.HasCheckConstraint(
                "ck_ndr_records_resolution",
                "(action = 'Pending') = (resolved_at IS NULL)");
        });

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).ValueGeneratedNever();

        builder.Property(record => record.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(record => record.Awb).HasMaxLength(64);
        builder.Property(record => record.ReasonCode).HasConversion<string>().HasMaxLength(32);
        builder.Property(record => record.Reason).HasMaxLength(500);
        builder.Property(record => record.Action).HasConversion<string>().HasMaxLength(24);
        builder.Property(record => record.ActionRemark).HasMaxLength(500);

        // One report per attempt per parcel. A courier that repeats a failed-attempt scan produces
        // one row, which is what stops a queue filling with the same question.
        builder.HasIndex(record => new { record.TenantId, record.ShipmentId, record.AttemptNumber })
            .IsUnique();

        // The queue itself: open reports, oldest first, optionally for one seller.
        builder.HasIndex(record => new { record.TenantId, record.VendorId, record.Action, record.RaisedAt });

        builder.Ignore(record => record.DomainEvents);
        builder.Ignore(record => record.IsOpen);
    }
}

/// <summary>Maps <see cref="ShippingManifest"/> to <c>shipping.manifests</c>.</summary>
internal sealed class ShippingManifestConfiguration : IEntityTypeConfiguration<ShippingManifest>
{
    public void Configure(EntityTypeBuilder<ShippingManifest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("manifests", table => table.HasCheckConstraint(
            "ck_manifests_counts",
            "shipment_count >= 0 AND total_weight_grams >= 0"));

        builder.HasKey(manifest => manifest.Id);
        builder.Property(manifest => manifest.Id).ValueGeneratedNever();

        builder.Property(manifest => manifest.Reference).HasMaxLength(32).IsRequired();
        builder.Property(manifest => manifest.Courier).HasMaxLength(64).IsRequired();
        builder.Property(manifest => manifest.ProviderManifestId).HasMaxLength(64);

        // One sheet per reference per tenant: the number on the paper the driver signed.
        builder.HasIndex(manifest => new { manifest.TenantId, manifest.Reference }).IsUnique();
        builder.HasIndex(manifest => new { manifest.TenantId, manifest.VendorId, manifest.GeneratedAt });

        builder.Ignore(manifest => manifest.DomainEvents);
    }
}

/// <summary>Maps <see cref="CourierEvent"/> to <c>shipping.courier_events</c>.</summary>
internal sealed class CourierEventConfiguration : IEntityTypeConfiguration<CourierEvent>
{
    public void Configure(EntityTypeBuilder<CourierEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("courier_events", table => table.HasCheckConstraint(
            "ck_courier_events_status",
            ShippingCheckConstraints.CourierEventStatuses));

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.Provider).HasMaxLength(32).IsRequired();
        builder.Property(entry => entry.ProviderEventId).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.EventType).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.Awb).HasMaxLength(64);
        builder.Property(entry => entry.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(entry => entry.ProcessError).HasMaxLength(1000);
        builder.Property(entry => entry.Payload).HasColumnType(ShippingJson.ColumnType).IsRequired();

        // The replay protection, and the whole reason a redelivered webhook is free.
        builder.HasIndex(entry => new { entry.TenantId, entry.Provider, entry.ProviderEventId }).IsUnique();

        // The worker's claim: pending or due, oldest first.
        builder.HasIndex(entry => new { entry.TenantId, entry.Status, entry.NextAttemptAt });

        // The operator's queue: everything about one parcel, however it arrived.
        builder.HasIndex(entry => new { entry.TenantId, entry.Awb });

        builder.Ignore(entry => entry.DomainEvents);
    }
}
