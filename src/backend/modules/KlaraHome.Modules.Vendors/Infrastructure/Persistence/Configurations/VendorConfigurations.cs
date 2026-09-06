using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Vendors.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Vendors.Infrastructure.Persistence.Configurations;

/// <summary>The <c>CHECK</c> lists, written once so a column and its constraint cannot drift apart.</summary>
internal static class VendorCheckConstraints
{
    /// <summary>The values <c>vendors.status</c> accepts.</summary>
    public const string VendorStatuses =
        "status IN ('Applied', 'UnderReview', 'Approved', 'Active', 'Suspended', 'Offboarded')";

    /// <summary>The values <c>vendors.business_type</c> accepts.</summary>
    public const string BusinessTypes =
        "business_type IN ('Individual', 'SoleProprietorship', 'Partnership', 'LimitedLiabilityPartnership', "
        + "'PrivateLimited', 'PublicLimited', 'HinduUndividedFamily', 'Trust')";

    /// <summary>The values <c>vendor_kyc_documents.document_type</c> accepts.</summary>
    public const string KycDocumentTypes =
        "document_type IN ('Pan', 'Gstin', 'CancelledCheque', 'AddressProof', 'IdentityProof', "
        + "'IncorporationCertificate')";

    /// <summary>The values <c>vendor_kyc_documents.status</c> accepts.</summary>
    public const string KycStatuses = "status IN ('Pending', 'Verified', 'Rejected')";

    /// <summary>The values <c>vendor_bank_accounts.verification_status</c> accepts.</summary>
    public const string BankVerificationStatuses = "verification_status IN ('Unverified', 'Verified', 'Failed')";

    /// <summary>The values <c>vendor_serviceable_regions.scope</c> accepts.</summary>
    public const string RegionScopes = "scope IN ('State', 'PincodePrefix')";

    /// <summary>The values <c>commission_plans.plan_type</c> accepts.</summary>
    public const string PlanTypes = "plan_type IN ('Flat', 'Percentage', 'Tiered')";
}

/// <summary>Maps <see cref="Vendor"/> to <c>vendors.vendors</c>.</summary>
internal sealed class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("vendors", table =>
        {
            table.HasCheckConstraint("ck_vendors_status", VendorCheckConstraints.VendorStatuses);
            table.HasCheckConstraint("ck_vendors_business_type", VendorCheckConstraints.BusinessTypes);

            // The SLA is what fulfilment measures a seller against and what the storefront promises
            // a shopper. Zero would promise same-second dispatch; a year is a typo.
            table.HasCheckConstraint(
                "ck_vendors_dispatch_sla",
                $"dispatch_sla_hours > 0 AND dispatch_sla_hours <= {Vendor.MaxDispatchSlaHours}");

            table.HasCheckConstraint("ck_vendors_rating", "rating IS NULL OR (rating >= 0 AND rating <= 5)");

            // A GSTIN is fifteen characters and a PAN is ten, always. Length is the cheapest half
            // of the check; the format is validated in the application layer, where the message
            // can say which character is wrong.
            table.HasCheckConstraint("ck_vendors_pan", "pan IS NULL OR char_length(pan) = 10");
            table.HasCheckConstraint("ck_vendors_gstin", "gstin IS NULL OR char_length(gstin) = 15");
        });

        builder.HasKey(vendor => vendor.Id);
        builder.Property(vendor => vendor.Id).ValueGeneratedNever();

        builder.Property(vendor => vendor.Code).HasMaxLength(32);
        builder.Property(vendor => vendor.LegalName).HasMaxLength(200);
        builder.Property(vendor => vendor.DisplayName).HasMaxLength(120);
        builder.Property(vendor => vendor.Slug).HasMaxLength(140);
        builder.Property(vendor => vendor.StatusReason).HasMaxLength(500);
        builder.Property(vendor => vendor.Pan).HasMaxLength(10).IsFixedLength();
        builder.Property(vendor => vendor.Gstin).HasMaxLength(15).IsFixedLength();
        builder.Property(vendor => vendor.SupportEmail).HasMaxLength(320);
        builder.Property(vendor => vendor.SupportPhone).HasMaxLength(20);
        builder.Property(vendor => vendor.About).HasMaxLength(4000);
        builder.Property(vendor => vendor.GatewayAccountId).HasMaxLength(64);
        builder.Property(vendor => vendor.Rating).HasColumnType("numeric(3,2)");

        // text + CHECK rather than a native enum (docs/03-database-design.md §1).
        builder.Property(vendor => vendor.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(vendor => vendor.BusinessType).HasConversion<string>().HasMaxLength(32);

        // jsonb, and read whole. Neither of these is ever filtered on: the registered address is
        // printed on an invoice, and the return policy is shown on a product page.
        builder.OwnsOne(vendor => vendor.RegisteredAddress, address => address.ToJson());
        builder.OwnsOne(vendor => vendor.ReturnPolicy, policy => policy.ToJson());

        // The code is quoted in support calls and printed on invoices; the slug is a URL. Both are
        // how somebody outside this system names a seller, so both have to be unique.
        builder.HasIndex(vendor => new { vendor.TenantId, vendor.Code }).IsUnique();
        builder.HasIndex(vendor => new { vendor.TenantId, vendor.Slug }).IsUnique();

        // The admin list filters by status far more often than by anything else — "show me who is
        // waiting for review" is the screen this module exists to serve.
        builder.HasIndex(vendor => new { vendor.TenantId, vendor.Status });

        // Which sellers are on a plan, asked whenever a plan is edited or retired.
        builder.HasIndex(vendor => new { vendor.TenantId, vendor.CommissionPlanId });

        builder.Ignore(vendor => vendor.DomainEvents);
        builder.Ignore(vendor => vendor.IsTrading);
    }
}

/// <summary>Maps <see cref="VendorUser"/> to <c>vendors.vendor_users</c>.</summary>
internal sealed class VendorUserConfiguration : IEntityTypeConfiguration<VendorUser>
{
    public void Configure(EntityTypeBuilder<VendorUser> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("vendor_users");

        builder.HasKey(member => member.Id);
        builder.Property(member => member.Id).ValueGeneratedNever();
        builder.Property(member => member.VendorId).IsRequired();
        builder.Property(member => member.JobTitle).HasMaxLength(120);

        // One membership per person per seller. Without this a double-click on "add staff" gives a
        // user two rows, and removing one would leave them still in.
        builder.HasIndex(member => new { member.TenantId, member.VendorId, member.UserId }).IsUnique();

        // "Which seller does this user belong to" — asked by Identity's admin surface for every
        // vendor user it lists.
        builder.HasIndex(member => new { member.TenantId, member.UserId });

        builder.Ignore(member => member.DomainEvents);
    }
}

/// <summary>Maps <see cref="VendorKycDocument"/> to <c>vendors.vendor_kyc_documents</c>.</summary>
internal sealed class VendorKycDocumentConfiguration : IEntityTypeConfiguration<VendorKycDocument>
{
    public void Configure(EntityTypeBuilder<VendorKycDocument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("vendor_kyc_documents", table =>
        {
            table.HasCheckConstraint("ck_vendor_kyc_documents_type", VendorCheckConstraints.KycDocumentTypes);
            table.HasCheckConstraint("ck_vendor_kyc_documents_status", VendorCheckConstraints.KycStatuses);

            // A rejection without a reason is a seller who does not know what to send again.
            table.HasCheckConstraint(
                "ck_vendor_kyc_documents_rejection",
                "status <> 'Rejected' OR rejection_reason IS NOT NULL");
        });

        builder.HasKey(document => document.Id);
        builder.Property(document => document.Id).ValueGeneratedNever();
        builder.Property(document => document.VendorId).IsRequired();
        builder.Property(document => document.NumberMasked).HasMaxLength(64);
        builder.Property(document => document.RejectionReason).HasMaxLength(500);
        builder.Property(document => document.DocumentType).HasConversion<string>().HasMaxLength(32);
        builder.Property(document => document.Status).HasConversion<string>().HasMaxLength(16);

        // One live document of each kind per seller. A second PAN card is a replacement, and
        // Resubmit exists so it replaces rather than accumulates — two PANs in Pending is a queue
        // where a reviewer has to guess which one counts.
        builder.HasIndex(document => new { document.TenantId, document.VendorId, document.DocumentType })
            .IsUnique();

        // The review queue: everything still Pending, oldest first.
        builder.HasIndex(document => new { document.TenantId, document.Status });

        builder.Ignore(document => document.DomainEvents);
    }
}

/// <summary>Maps <see cref="VendorBankAccount"/> to <c>vendors.vendor_bank_accounts</c>.</summary>
internal sealed class VendorBankAccountConfiguration : IEntityTypeConfiguration<VendorBankAccount>
{
    public void Configure(EntityTypeBuilder<VendorBankAccount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("vendor_bank_accounts", table =>
        {
            table.HasCheckConstraint(
                "ck_vendor_bank_accounts_verification",
                VendorCheckConstraints.BankVerificationStatuses);

            // An IFSC is eleven characters and its fifth is always '0'. This one is worth putting
            // in the database: a payout to a malformed IFSC fails at the bank, days later, and the
            // money sits in limbo while somebody works out why.
            table.HasCheckConstraint(
                "ck_vendor_bank_accounts_ifsc",
                "char_length(ifsc) = 11 AND substring(ifsc from 5 for 1) = '0'");

            table.HasCheckConstraint(
                "ck_vendor_bank_accounts_last4",
                "char_length(account_number_last4) BETWEEN 1 AND 4");
        });

        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).ValueGeneratedNever();
        builder.Property(account => account.VendorId).IsRequired();
        builder.Property(account => account.AccountName).HasMaxLength(200);

        // The envelope is key id, nonce, ciphertext and tag, base64 and dot-joined. Generous
        // rather than tight: the column must not be what makes a key rotation fail.
        builder.Property(account => account.AccountNumberEncrypted).HasMaxLength(512);

        builder.Property(account => account.AccountNumberLast4).HasMaxLength(4);
        builder.Property(account => account.Ifsc).HasMaxLength(11).IsFixedLength();
        builder.Property(account => account.BankName).HasMaxLength(120);
        builder.Property(account => account.BranchName).HasMaxLength(120);
        builder.Property(account => account.VerificationNote).HasMaxLength(500);
        builder.Property(account => account.VerificationStatus).HasConversion<string>().HasMaxLength(16);

        // At most one primary account per seller, enforced by the database rather than by the
        // handler that sets it. Two primaries is a payout that goes to whichever row sorted first.
        builder.HasIndex(account => new { account.TenantId, account.VendorId })
            .HasDatabaseName("ux_vendor_bank_accounts_primary")
            .IsUnique()
            .HasFilter("is_primary");

        builder.Ignore(account => account.DomainEvents);
    }
}

/// <summary>Maps <see cref="VendorPickupLocation"/> to <c>vendors.vendor_pickup_locations</c>.</summary>
internal sealed class VendorPickupLocationConfiguration : IEntityTypeConfiguration<VendorPickupLocation>
{
    public void Configure(EntityTypeBuilder<VendorPickupLocation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("vendor_pickup_locations", table =>
            table.HasCheckConstraint("ck_vendor_pickup_locations_pincode", "pincode ~ '^[1-9][0-9]{5}$'"));

        builder.HasKey(location => location.Id);
        builder.Property(location => location.Id).ValueGeneratedNever();
        builder.Property(location => location.VendorId).IsRequired();
        builder.Property(location => location.Label).HasMaxLength(80);
        builder.Property(location => location.ContactName).HasMaxLength(120);
        builder.Property(location => location.ContactPhone).HasMaxLength(20);
        builder.Property(location => location.Line1).HasMaxLength(200);
        builder.Property(location => location.Line2).HasMaxLength(200);
        builder.Property(location => location.Landmark).HasMaxLength(200);
        builder.Property(location => location.City).HasMaxLength(120);
        builder.Property(location => location.Pincode).HasMaxLength(6).IsFixedLength();
        builder.Property(location => location.CourierLocationCode).HasMaxLength(64);

        // One default per seller, for the same reason as the primary bank account.
        builder.HasIndex(location => new { location.TenantId, location.VendorId })
            .HasDatabaseName("ux_vendor_pickup_locations_default")
            .IsUnique()
            .HasFilter("is_default");

        // Shipping asks "where does this seller ship from, near here" by PIN code.
        builder.HasIndex(location => new { location.TenantId, location.VendorId, location.Pincode });

        builder.Ignore(location => location.DomainEvents);
    }
}

/// <summary>Maps <see cref="VendorServiceableRegion"/> to <c>vendors.vendor_serviceable_regions</c>.</summary>
internal sealed class VendorServiceableRegionConfiguration : IEntityTypeConfiguration<VendorServiceableRegion>
{
    public void Configure(EntityTypeBuilder<VendorServiceableRegion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("vendor_serviceable_regions", table =>
        {
            table.HasCheckConstraint("ck_vendor_serviceable_regions_scope", VendorCheckConstraints.RegionScopes);

            // Exactly one of the two columns carries the rule's subject. A row with both is
            // ambiguous and a row with neither matches everything, which is what ServesAllIndia is
            // for.
            table.HasCheckConstraint(
                "ck_vendor_serviceable_regions_subject",
                "(scope = 'State' AND state_id IS NOT NULL AND pincode_prefix IS NULL) OR "
                + "(scope = 'PincodePrefix' AND pincode_prefix IS NOT NULL AND state_id IS NULL)");

            table.HasCheckConstraint(
                "ck_vendor_serviceable_regions_prefix",
                "pincode_prefix IS NULL OR pincode_prefix ~ '^[1-9][0-9]{1,5}$'");
        });

        builder.HasKey(region => region.Id);
        builder.Property(region => region.Id).ValueGeneratedNever();
        builder.Property(region => region.VendorId).IsRequired();
        builder.Property(region => region.PincodePrefix).HasMaxLength(6);
        builder.Property(region => region.Scope).HasConversion<string>().HasMaxLength(16);

        // The whole rule set for one seller is read at once, on every serviceability check.
        builder.HasIndex(region => new { region.TenantId, region.VendorId });

        builder.Ignore(region => region.DomainEvents);
    }
}

/// <summary>Maps <see cref="CommissionPlan"/> to <c>vendors.commission_plans</c>.</summary>
internal sealed class CommissionPlanConfiguration : IEntityTypeConfiguration<CommissionPlan>
{
    public void Configure(EntityTypeBuilder<CommissionPlan> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("commission_plans", table =>
        {
            table.HasCheckConstraint("ck_commission_plans_type", VendorCheckConstraints.PlanTypes);
            table.HasCheckConstraint(
                "ck_commission_plans_default_rate",
                $"default_rate >= 0 AND default_rate <= {CommissionPlan.MaxRatePercent}");
        });

        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.Id).ValueGeneratedNever();
        builder.Property(plan => plan.Code).HasMaxLength(64);
        builder.Property(plan => plan.Name).HasMaxLength(120);
        builder.Property(plan => plan.Description).HasMaxLength(500);
        builder.Property(plan => plan.PlanType).HasConversion<string>().HasMaxLength(16);

        // numeric(7,4): 12.5% is stored as 12.5000 (docs/03-database-design.md §1).
        builder.Property(plan => plan.DefaultRate).HasColumnType("numeric(7,4)");
        builder.HasMoney(plan => plan.DefaultFixedFee);

        builder.HasIndex(plan => new { plan.TenantId, plan.Code }).IsUnique();

        // At most one default plan. Two would make "which plan does a new seller get" depend on
        // row order.
        builder.HasIndex(plan => plan.TenantId)
            .HasDatabaseName("ux_commission_plans_default")
            .IsUnique()
            .HasFilter("is_default");

        // The rules are the plan. They are always loaded with it and never queried alone, so they
        // are a backing-field collection rather than a navigation somebody might forget to include.
        builder.HasMany(plan => plan.Rules)
            .WithOne()
            .HasForeignKey(rule => rule.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(CommissionPlan.Rules))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(plan => plan.DomainEvents);
    }
}

/// <summary>Maps <see cref="CommissionPlanRule"/> to <c>vendors.commission_plan_rules</c>.</summary>
internal sealed class CommissionPlanRuleConfiguration : IEntityTypeConfiguration<CommissionPlanRule>
{
    public void Configure(EntityTypeBuilder<CommissionPlanRule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("commission_plan_rules", table =>
        {
            table.HasCheckConstraint(
                "ck_commission_plan_rules_rate",
                $"rate >= 0 AND rate <= {CommissionPlan.MaxRatePercent}");

            // A band whose floor is above its ceiling matches nothing, silently, for ever.
            table.HasCheckConstraint(
                "ck_commission_plan_rules_band",
                "min_price IS NULL OR max_price IS NULL OR min_price < max_price");

            table.HasCheckConstraint(
                "ck_commission_plan_rules_prices",
                "(min_price IS NULL OR min_price >= 0) AND (max_price IS NULL OR max_price > 0)");
        });

        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.Id).ValueGeneratedNever();
        builder.Property(rule => rule.Rate).HasColumnType("numeric(7,4)");
        builder.Property(rule => rule.MinPrice).HasColumnType(ModelConventions.MoneyColumnType);
        builder.Property(rule => rule.MaxPrice).HasColumnType(ModelConventions.MoneyColumnType);
        builder.HasMoney(rule => rule.FixedFee);

        // Resolution loads every rule of one plan and picks among them in memory: the rule set is
        // a handful of rows, and the specificity comparison is not something SQL should be asked
        // to express.
        builder.HasIndex(rule => new { rule.TenantId, rule.PlanId });

        builder.Ignore(rule => rule.DomainEvents);
    }
}
