using System.Text.Json;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Platform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Serialisation for the <c>jsonb</c> columns in this schema, shared by every configuration so the
/// document a converter writes is the document the services read.
/// </summary>
internal static class PlatformJson
{
    /// <summary>
    /// camelCase, matching the API payloads these documents are handed to and received from. A
    /// jsonb column that is read by an admin screen is part of the API surface, and a casing
    /// convention applied on one side and not the other is a class of bug worth designing out.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <summary>The Postgres type every open-shaped column in this schema uses.</summary>
    public const string ColumnType = "jsonb";
}

/// <summary>Maps <see cref="Tenant"/> to <c>platform.tenants</c>.</summary>
internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tenants", table => table.HasCheckConstraint(
            "ck_tenants_status",
            "status IN ('Active', 'Suspended')"));

        builder.HasKey(tenant => tenant.Id);
        builder.Property(tenant => tenant.Id).ValueGeneratedNever();

        builder.Property(tenant => tenant.Code).HasMaxLength(32);
        builder.Property(tenant => tenant.Name).HasMaxLength(200);

        // text + CHECK rather than a native enum (docs/03-database-design.md §1): adding a value to
        // a Postgres enum is a migration that cannot run inside a transaction with other DDL.
        builder.Property(tenant => tenant.Status).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(tenant => tenant.Code).IsUnique();

        builder.Ignore(tenant => tenant.DomainEvents);
    }
}

/// <summary>Maps <see cref="StoreSetting"/> to <c>platform.store_settings</c>.</summary>
internal sealed class StoreSettingConfiguration : IEntityTypeConfiguration<StoreSetting>
{
    public void Configure(EntityTypeBuilder<StoreSetting> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("store_settings");

        builder.HasKey(setting => setting.Id);
        builder.Property(setting => setting.Id).ValueGeneratedNever();

        builder.Property(setting => setting.Key).HasMaxLength(64);
        builder.Property(setting => setting.Value).HasColumnType(PlatformJson.ColumnType);

        // The uniqueness that makes "read section X" a single-row lookup rather than a policy about
        // which duplicate wins.
        builder
            .HasIndex(setting => new { setting.TenantId, setting.Key })
            .IsUnique();

        builder.Ignore(setting => setting.DomainEvents);
    }
}

/// <summary>Maps <see cref="FeatureFlag"/> to <c>platform.feature_flags</c>.</summary>
internal sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("feature_flags");

        builder.HasKey(flag => flag.Id);
        builder.Property(flag => flag.Id).ValueGeneratedNever();

        builder.Property(flag => flag.Key).HasMaxLength(128);
        builder.Property(flag => flag.Description).HasMaxLength(500);

        // A converter rather than an owned entity: the rollout is one document, it has no identity,
        // and its shape must be free to grow without a migration.
        builder.Property(flag => flag.Rollout)
            .HasColumnType(PlatformJson.ColumnType)
            .HasConversion(
                rollout => JsonSerializer.Serialize(rollout, PlatformJson.Options),
                json => JsonSerializer.Deserialize<FeatureRollout>(json, PlatformJson.Options)
                        ?? FeatureRollout.Everyone,
                new ValueComparer<FeatureRollout>(
                    (left, right) => left == right,
                    rollout => rollout.GetHashCode(),
                    rollout => rollout));

        builder
            .HasIndex(flag => new { flag.TenantId, flag.Key })
            .IsUnique();

        builder.Ignore(flag => flag.DomainEvents);
    }
}

/// <summary>Maps <see cref="AuditLogEntry"/> to <c>platform.audit_logs</c>.</summary>
/// <remarks>
/// The table is excluded from migrations and its DDL is hand-written, because it is
/// <c>PARTITION BY RANGE (occurred_at)</c> and carries a trigger that rejects any <c>UPDATE</c> or
/// <c>DELETE</c> — neither of which EF can express. The mapping still lives here so the query API
/// is ordinary LINQ rather than raw SQL.
/// </remarks>
internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audit_logs", table => table.ExcludeFromMigrations());

        // PostgreSQL requires the partition key in every unique constraint, so the key is the pair
        // rather than the id alone.
        builder.HasKey(entry => new { entry.OccurredAt, entry.Id });

        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.ActorType).HasConversion<string>().HasMaxLength(32);
        builder.Property(entry => entry.Action).HasMaxLength(128);
        builder.Property(entry => entry.EntityType).HasMaxLength(128);
        builder.Property(entry => entry.EntityId).HasMaxLength(128);
        builder.Property(entry => entry.Before).HasColumnType(PlatformJson.ColumnType);
        builder.Property(entry => entry.After).HasColumnType(PlatformJson.ColumnType);
        builder.Property(entry => entry.Ip).HasMaxLength(45);
        builder.Property(entry => entry.UserAgent).HasMaxLength(512);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(64);
    }
}

/// <summary>Maps <see cref="StateOrUnionTerritory"/> to <c>platform.states</c>.</summary>
internal sealed class StateConfiguration : IEntityTypeConfiguration<StateOrUnionTerritory>
{
    public void Configure(EntityTypeBuilder<StateOrUnionTerritory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("states", table => table.HasCheckConstraint(
            "ck_states_kind",
            "kind IN ('State', 'UnionTerritory')"));

        builder.HasKey(state => state.Id);
        builder.Property(state => state.Id).ValueGeneratedNever();

        builder.Property(state => state.Code).HasColumnType("char(2)");
        builder.Property(state => state.Name).HasMaxLength(100);
        builder.Property(state => state.Kind).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(state => state.Code).IsUnique();
    }
}

/// <summary>Maps <see cref="Pincode"/> to <c>platform.pincodes</c>.</summary>
internal sealed class PincodeConfiguration : IEntityTypeConfiguration<Pincode>
{
    public void Configure(EntityTypeBuilder<Pincode> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("pincodes", table => table.HasCheckConstraint(
            "ck_pincodes_code_is_six_digits",
            "code ~ '^[1-9][0-9]{5}$'"));

        builder.HasKey(pincode => pincode.Id);
        builder.Property(pincode => pincode.Id).ValueGeneratedNever();

        builder.Property(pincode => pincode.Code).HasColumnType("char(6)");
        builder.Property(pincode => pincode.City).HasMaxLength(120);
        builder.Property(pincode => pincode.District).HasMaxLength(120);
        builder.Property(pincode => pincode.Zone).HasMaxLength(32);

        builder.HasIndex(pincode => pincode.Code).IsUnique();
        builder.HasIndex(pincode => pincode.StateId);

        // Same schema, so this is a real foreign key. The no-cross-schema rule
        // (docs/01-architecture.md §2.1) is about module boundaries, and both tables are ours.
        builder
            .HasOne<StateOrUnionTerritory>()
            .WithMany()
            .HasForeignKey(pincode => pincode.StateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <see cref="HsnCode"/> to <c>platform.hsn_codes</c>.</summary>
internal sealed class HsnCodeConfiguration : IEntityTypeConfiguration<HsnCode>
{
    public void Configure(EntityTypeBuilder<HsnCode> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("hsn_codes", table => table.HasCheckConstraint(
            "ck_hsn_codes_rate_is_a_percentage",
            "default_gst_rate IS NULL OR (default_gst_rate >= 0 AND default_gst_rate <= 100)"));

        builder.HasKey(hsn => hsn.Id);
        builder.Property(hsn => hsn.Id).ValueGeneratedNever();

        builder.Property(hsn => hsn.Code).HasMaxLength(8);
        builder.Property(hsn => hsn.Description).HasMaxLength(1000);

        // Percentages are numeric(7,4) throughout (docs/03-database-design.md §1).
        builder.Property(hsn => hsn.DefaultGstRate).HasColumnType("numeric(7,4)");

        builder.HasIndex(hsn => hsn.Code).IsUnique();
    }
}
