using KlaraHome.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Identity.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="User"/> to <c>identity.users</c>.</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users", table =>
        {
            table.HasCheckConstraint(
                "ck_users_status",
                "status IN ('Active', 'Suspended', 'Disabled')");
            table.HasCheckConstraint(
                "ck_users_user_type",
                "user_type IN ('Customer', 'Vendor', 'Staff')");

            // A row with neither identifier could never sign in and could never be found; the only
            // way to create one is a bug, and this is where it stops.
            table.HasCheckConstraint(
                "ck_users_has_identifier",
                "mobile IS NOT NULL OR email IS NOT NULL");
        });

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.UserType).HasConversion<string>().HasMaxLength(16);
        builder.Property(user => user.Status).HasConversion<string>().HasMaxLength(16);

        // E.164 is at most 15 digits and a leading '+'.
        builder.Property(user => user.Mobile).HasMaxLength(16);
        builder.Property(user => user.Email).HasMaxLength(320);
        builder.Property(user => user.PasswordHash).HasMaxLength(256);
        builder.Property(user => user.TotpSecretEncrypted).HasMaxLength(512);

        // Unique per tenant, and only among live rows: a retired account must not hold its mobile
        // number hostage against the person coming back with the same one.
        builder
            .HasIndex(user => new { user.TenantId, user.Mobile })
            .IsUnique()
            .HasFilter("mobile IS NOT NULL AND deleted_at IS NULL");

        builder
            .HasIndex(user => new { user.TenantId, user.Email })
            .IsUnique()
            .HasFilter("email IS NOT NULL AND deleted_at IS NULL");

        builder
            .HasMany(user => user.Roles)
            .WithOne()
            .HasForeignKey(role => role.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(user => user.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(user => user.DomainEvents);
    }
}

/// <summary>Maps <see cref="Role"/> to <c>identity.roles</c>.</summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("roles", table => table.HasCheckConstraint(
            "ck_roles_scope",
            "scope IN ('Platform', 'Vendor', 'Customer')"));

        builder.HasKey(role => role.Id);
        builder.Property(role => role.Id).ValueGeneratedNever();

        builder.Property(role => role.Code).HasMaxLength(64);
        builder.Property(role => role.Name).HasMaxLength(128);
        builder.Property(role => role.Description).HasMaxLength(512);
        builder.Property(role => role.Scope).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(role => new { role.TenantId, role.Code }).IsUnique();

        builder
            .HasMany(role => role.Permissions)
            .WithOne()
            .HasForeignKey(permission => permission.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(role => role.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(role => role.DomainEvents);
    }
}

/// <summary>Maps <see cref="Permission"/> to <c>identity.permissions</c>.</summary>
internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("permissions");

        builder.HasKey(permission => permission.Id);
        builder.Property(permission => permission.Id).ValueGeneratedNever();

        builder.Property(permission => permission.Code).HasMaxLength(128);
        builder.Property(permission => permission.Group).HasMaxLength(64);
        builder.Property(permission => permission.Description).HasMaxLength(512);

        builder.HasIndex(permission => new { permission.TenantId, permission.Code }).IsUnique();

        builder.Ignore(permission => permission.DomainEvents);
    }
}

/// <summary>Maps <see cref="RolePermission"/> to <c>identity.role_permissions</c>.</summary>
internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("role_permissions");

        builder.HasKey(grant => grant.Id);
        builder.Property(grant => grant.Id).ValueGeneratedNever();

        builder.Property(grant => grant.PermissionCode).HasMaxLength(128);

        builder.HasIndex(grant => new { grant.RoleId, grant.PermissionCode }).IsUnique();

        builder.Ignore(grant => grant.DomainEvents);
    }
}

/// <summary>Maps <see cref="UserRole"/> to <c>identity.user_roles</c>.</summary>
internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_roles");

        builder.HasKey(grant => grant.Id);
        builder.Property(grant => grant.Id).ValueGeneratedNever();

        // Two indexes rather than one, because Postgres treats NULLs as distinct in a unique
        // index: without the partial pair, the same platform-wide role could be granted to the
        // same user any number of times.
        builder
            .HasIndex(grant => new { grant.UserId, grant.RoleId, grant.VendorId })
            .IsUnique()
            .HasFilter("vendor_id IS NOT NULL");

        builder
            .HasIndex(grant => new { grant.UserId, grant.RoleId })
            .IsUnique()
            .HasFilter("vendor_id IS NULL");

        builder.HasIndex(grant => grant.RoleId);

        builder.Ignore(grant => grant.DomainEvents);
    }
}

/// <summary>Maps <see cref="UserSession"/> to <c>identity.user_sessions</c>.</summary>
internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_sessions", table => table.HasCheckConstraint(
            "ck_user_sessions_revoked_reason",
            "revoked_reason IS NULL OR revoked_reason IN "
            + "('SignedOut', 'SignedOutEverywhere', 'TokenReuseDetected', 'CredentialChanged', 'AccountClosed')"));

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();

        builder.Property(session => session.Device).HasMaxLength(200);
        builder.Property(session => session.IpAddress).HasMaxLength(45);
        builder.Property(session => session.RevokedReason).HasConversion<string>().HasMaxLength(32);

        // The session list is "my live devices, newest first", which is exactly this index.
        builder
            .HasIndex(session => new { session.UserId, session.StartedAt })
            .IsDescending(false, true);

        builder.Ignore(session => session.DomainEvents);
    }
}

/// <summary>Maps <see cref="RefreshToken"/> to <c>identity.refresh_tokens</c>.</summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refresh_tokens");

        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();

        builder.Property(token => token.TokenHash).HasMaxLength(64);

        // Every refresh is a lookup by hash, and the hash must be unique or a rotation chain could
        // fork.
        builder.HasIndex(token => new { token.TenantId, token.TokenHash }).IsUnique();
        builder.HasIndex(token => token.SessionId);
        builder.HasIndex(token => token.UserId);

        builder.Ignore(token => token.DomainEvents);
    }
}

/// <summary>Maps <see cref="OtpChallenge"/> to <c>identity.otp_challenges</c>.</summary>
internal sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("otp_challenges", table =>
        {
            table.HasCheckConstraint("ck_otp_challenges_channel", "channel IN ('Sms', 'Email', 'WhatsApp')");
            table.HasCheckConstraint(
                "ck_otp_challenges_purpose",
                "purpose IN ('Login', 'VerifyMobile', 'VerifyEmail', 'PasswordReset')");
        });

        builder.HasKey(challenge => challenge.Id);
        builder.Property(challenge => challenge.Id).ValueGeneratedNever();

        builder.Property(challenge => challenge.Destination).HasMaxLength(320);
        builder.Property(challenge => challenge.CodeHash).HasMaxLength(64);
        builder.Property(challenge => challenge.Channel).HasConversion<string>().HasMaxLength(16);
        builder.Property(challenge => challenge.Purpose).HasConversion<string>().HasMaxLength(32);

        // Both hot paths: the throttle counts recent requests to one destination, and verification
        // finds the newest open challenge for one destination and purpose.
        builder
            .HasIndex(challenge => new { challenge.TenantId, challenge.Destination, challenge.Purpose, challenge.RequestedAt })
            .IsDescending(false, false, false, true);

        builder.Ignore(challenge => challenge.DomainEvents);
    }
}

/// <summary>Maps <see cref="CustomerProfile"/> to <c>identity.customer_profiles</c>.</summary>
internal sealed class CustomerProfileConfiguration : IEntityTypeConfiguration<CustomerProfile>
{
    public void Configure(EntityTypeBuilder<CustomerProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("customer_profiles");

        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).ValueGeneratedNever();

        builder.Property(profile => profile.FirstName).HasMaxLength(100);
        builder.Property(profile => profile.LastName).HasMaxLength(100);
        builder.Property(profile => profile.Gender).HasMaxLength(32);
        builder.Property(profile => profile.Gstin).HasMaxLength(15);
        builder.Property(profile => profile.ReferralCode).HasMaxLength(16);

        builder.HasIndex(profile => profile.UserId).IsUnique();
        builder.HasIndex(profile => new { profile.TenantId, profile.ReferralCode }).IsUnique();

        builder.Ignore(profile => profile.DomainEvents);
    }
}

/// <summary>Maps <see cref="Address"/> to <c>identity.addresses</c>.</summary>
internal sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("addresses", table =>
        {
            table.HasCheckConstraint("ck_addresses_type", "type IN ('Home', 'Office')");
            table.HasCheckConstraint("ck_addresses_pincode", "pincode ~ '^[1-9][0-9]{5}$'");
        });

        builder.HasKey(address => address.Id);
        builder.Property(address => address.Id).ValueGeneratedNever();

        builder.Property(address => address.Label).HasMaxLength(40);
        builder.Property(address => address.RecipientName).HasMaxLength(120);
        builder.Property(address => address.Mobile).HasMaxLength(16);
        builder.Property(address => address.Line1).HasMaxLength(200);
        builder.Property(address => address.Line2).HasMaxLength(200);
        builder.Property(address => address.Landmark).HasMaxLength(200);
        builder.Property(address => address.City).HasMaxLength(100);
        builder.Property(address => address.Pincode).HasMaxLength(6);
        builder.Property(address => address.Gstin).HasMaxLength(15);
        builder.Property(address => address.Type).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(address => address.UserId);

        // At most one default of each kind per customer, enforced by the database rather than by
        // the handler that clears the previous one: a race between two tabs would otherwise leave
        // a customer with two default addresses and checkout with no way to choose.
        //
        // Named twice on purpose. The model name keeps EF from treating each call as a
        // reconfiguration of the previous index — all three cover the same column — and the
        // database name stops the snake_case convention from renaming them to user_id1 and
        // user_id2, which say nothing to whoever reads the schema next.
        builder
            .HasIndex(address => address.UserId, "ix_addresses_default_shipping")
            .IsUnique()
            .HasFilter("is_default_shipping AND deleted_at IS NULL")
            .HasDatabaseName("ix_addresses_default_shipping");

        builder
            .HasIndex(address => address.UserId, "ix_addresses_default_billing")
            .IsUnique()
            .HasFilter("is_default_billing AND deleted_at IS NULL")
            .HasDatabaseName("ix_addresses_default_billing");

        builder.Ignore(address => address.DomainEvents);
    }
}
