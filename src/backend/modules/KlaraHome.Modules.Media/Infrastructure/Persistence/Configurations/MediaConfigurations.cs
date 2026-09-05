using KlaraHome.Modules.Media.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Media.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="StoredFile"/> to <c>media.files</c>.</summary>
internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("files", table =>
        {
            table.HasCheckConstraint("ck_files_status", "status IN ('Ready', 'Quarantined', 'Deleted')");
            table.HasCheckConstraint("ck_files_visibility", "visibility IN ('Public', 'Private')");
            table.HasCheckConstraint(
                "ck_files_scan_state",
                "scan_state IN ('Skipped', 'Pending', 'Clean', 'Infected')");
            table.HasCheckConstraint("ck_files_byte_size", "byte_size > 0");

            // Dimensions are both present or both absent. A width with no height is a half-read
            // header, and the caller that consumes one would compute a nonsensical aspect ratio.
            table.HasCheckConstraint(
                "ck_files_dimensions",
                "(width IS NULL AND height IS NULL) OR (width > 0 AND height > 0)");
        });

        builder.HasKey(file => file.Id);
        builder.Property(file => file.Id).ValueGeneratedNever();

        // 1024 is the S3 key limit; the column matches it so a key that storage would accept is
        // never rejected by the registry instead.
        builder.Property(file => file.StorageKey).HasMaxLength(1024);
        builder.Property(file => file.FileName).HasMaxLength(255);
        builder.Property(file => file.ContentType).HasMaxLength(128);

        // Lowercase hex SHA-256: always exactly 64 characters.
        builder.Property(file => file.ChecksumSha256).HasMaxLength(64).IsFixedLength();

        builder.Property(file => file.OwnerType).HasMaxLength(64);

        // text + CHECK rather than a native enum (docs/03-database-design.md §1).
        builder.Property(file => file.Visibility).HasConversion<string>().HasMaxLength(16);
        builder.Property(file => file.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(file => file.ScanState).HasConversion<string>().HasMaxLength(16);

        // One object per key per bucket. Keys are generated, so a collision would mean two
        // registrations pointing at bytes only one of them wrote.
        builder
            .HasIndex(file => new { file.TenantId, file.Visibility, file.StorageKey })
            .IsUnique();

        // The admin media library lists newest first, which is the only listing this table has.
        builder.HasIndex(file => new { file.TenantId, file.CreatedAt })
            .HasDatabaseName("ix_files_tenant_created_at")
            .IsDescending(false, true);

        // The orphan sweep at Step 31 asks "what points at this owner", and the reference is soft,
        // so there is no foreign key to plan the query for it.
        builder.HasIndex(file => new { file.TenantId, file.OwnerType, file.OwnerId });

        builder.Ignore(file => file.DomainEvents);
        builder.Ignore(file => file.IsServable);
    }
}
