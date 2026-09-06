using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Pricing.Infrastructure.Persistence.Configurations;

/// <summary>The <c>CHECK</c> lists, written once so a column and its constraint cannot drift apart.</summary>
internal static class PricingCheckConstraints
{
    /// <summary>The values <c>price_lists.type</c> accepts.</summary>
    public const string PriceListTypes = "type IN ('Base', 'Sale', 'Scheduled')";

    /// <summary>The values <c>promotions.type</c> accepts.</summary>
    public const string PromotionTypes =
        "type IN ('Percentage', 'Fixed', 'FreeShipping', 'Bogo', 'Bundle', 'Tiered')";

    /// <summary>The values <c>promotions.applies_to</c> accepts.</summary>
    public const string PromotionApplications = "applies_to IN ('Line', 'Order', 'Shipping')";

    /// <summary>The values <c>promotions.stacking</c> accepts.</summary>
    public const string StackingModes = "stacking IN ('Exclusive', 'Stackable')";

    /// <summary>The values <c>promotion_redemptions.status</c> accepts.</summary>
    public const string RedemptionStatuses = "status IN ('Redeemed', 'Reversed')";

    /// <summary>The values <c>wallet_transactions.type</c> accepts.</summary>
    public const string WalletTransactionTypes = "type IN ('Credit', 'Debit', 'Expiry', 'Reversal')";
}

/// <summary>Maps <see cref="PriceList"/> to <c>pricing.price_lists</c>.</summary>
internal sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("price_lists", table =>
        {
            table.HasCheckConstraint("ck_price_lists_type", PricingCheckConstraints.PriceListTypes);

            table.HasCheckConstraint(
                "ck_price_lists_priority",
                $"priority >= 0 AND priority <= {PriceList.MaxPriority}");

            // A window that closes before it opens is a list that can never apply, and the operator
            // who typed it will spend an afternoon wondering why their sale did nothing.
            table.HasCheckConstraint(
                "ck_price_lists_window",
                "ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at");
        });

        builder.HasKey(list => list.Id);
        builder.Property(list => list.Id).ValueGeneratedNever();

        builder.Property(list => list.Code).HasMaxLength(48);
        builder.Property(list => list.Name).HasMaxLength(160);
        builder.Property(list => list.Type).HasConversion<string>().HasMaxLength(16);

        builder.Property(list => list.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(KlaraHome.SharedKernel.Primitives.Money.Inr);

        builder.HasMany(list => list.Items)
            .WithOne()
            .HasForeignKey(item => item.PriceListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(list => list.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The code is what an operator quotes and what an import keys on. Hard-deleted rather than
        // soft, so no filter is needed.
        builder.HasIndex(list => new { list.TenantId, list.Code }).IsUnique();

        // The resolution walk: the lists that could apply, cheapest rank first. Everything else in
        // the walk — the window, the seller — is decided in memory over this handful of rows.
        builder.HasIndex(list => new { list.TenantId, list.IsActive, list.Priority });

        builder.Ignore(list => list.DomainEvents);
    }
}

/// <summary>Maps <see cref="PriceListItem"/> to <c>pricing.price_list_items</c>.</summary>
internal sealed class PriceListItemConfiguration : IEntityTypeConfiguration<PriceListItem>
{
    public void Configure(EntityTypeBuilder<PriceListItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("price_list_items", table =>
        {
            table.HasCheckConstraint("ck_price_list_items_price", "price_amount >= 0");
            table.HasCheckConstraint("ck_price_list_items_min_quantity", "min_quantity >= 1");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.HasMoney(item => item.Price);

        // One price per offer per tier. Without it a list could hold two prices for the same offer
        // at the same quantity, and which one a shopper saw would depend on the query plan.
        builder
            .HasIndex(item => new { item.TenantId, item.PriceListId, item.ListingId, item.MinQuantity })
            .IsUnique();

        // "What does this offer cost" reads every applicable list at once, so the offer leads.
        builder.HasIndex(item => new { item.TenantId, item.ListingId });

        builder.Ignore(item => item.DomainEvents);
    }
}

/// <summary>Maps <see cref="TaxRate"/> to <c>pricing.tax_rates</c>.</summary>
internal sealed class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tax_rates", table =>
        {
            table.HasCheckConstraint("ck_tax_rates_rate", $"rate >= 0 AND rate <= {TaxRate.MaxRate}");
            table.HasCheckConstraint("ck_tax_rates_cess", $"cess_rate >= 0 AND cess_rate <= {TaxRate.MaxRate}");

            table.HasCheckConstraint(
                "ck_tax_rates_window",
                "effective_to IS NULL OR effective_to >= effective_from");

            // Four to eight digits. An HSN is a numeric code, and a letter in one is a typo that
            // would silently resolve to nothing and fall back to the product's own rate.
            table.HasCheckConstraint("ck_tax_rates_hsn", "hsn_code ~ '^[0-9]{4,8}$'");
        });

        builder.HasKey(rate => rate.Id);
        builder.Property(rate => rate.Id).ValueGeneratedNever();

        builder.Property(rate => rate.HsnCode).HasMaxLength(8);
        builder.Property(rate => rate.Description).HasMaxLength(300);

        builder.Property(rate => rate.Rate).HasColumnType("numeric(7,4)");
        builder.Property(rate => rate.CessRate).HasColumnType("numeric(7,4)");

        // One rate per code per start date. A second row with the same start is two answers to what
        // a customer owed that day, which is the one thing a tax table may never have.
        builder.HasIndex(rate => new { rate.TenantId, rate.HsnCode, rate.EffectiveFrom }).IsUnique();

        // Resolution: the rows for one code, newest window first.
        builder.HasIndex(rate => new { rate.TenantId, rate.HsnCode, rate.IsActive });

        builder.Ignore(rate => rate.DomainEvents);
    }
}

/// <summary>Maps <see cref="Promotion"/> to <c>pricing.promotions</c>.</summary>
internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("promotions", table =>
        {
            table.HasCheckConstraint("ck_promotions_type", PricingCheckConstraints.PromotionTypes);
            table.HasCheckConstraint("ck_promotions_applies_to", PricingCheckConstraints.PromotionApplications);
            table.HasCheckConstraint("ck_promotions_stacking", PricingCheckConstraints.StackingModes);
            table.HasCheckConstraint("ck_promotions_value", "value >= 0");
            table.HasCheckConstraint("ck_promotions_min_order_value", "min_order_value >= 0");
            table.HasCheckConstraint("ck_promotions_max_discount", "max_discount IS NULL OR max_discount > 0");
            table.HasCheckConstraint("ck_promotions_window", "ends_at IS NULL OR ends_at > starts_at");

            table.HasCheckConstraint(
                "ck_promotions_priority",
                $"priority >= 0 AND priority <= {Promotion.MaxPriority}");

            // The usage counter is the derived cache the conditional update reads. Letting it go
            // negative or past its own limit would make the race it exists to win unwinnable.
            table.HasCheckConstraint("ck_promotions_usage_count", "usage_count >= 0");

            table.HasCheckConstraint(
                "ck_promotions_usage_limits",
                "(usage_limit_total IS NULL OR usage_limit_total > 0) "
                + "AND (usage_limit_per_customer IS NULL OR usage_limit_per_customer > 0)");
        });

        builder.HasKey(promotion => promotion.Id);
        builder.Property(promotion => promotion.Id).ValueGeneratedNever();

        builder.Property(promotion => promotion.Code).HasMaxLength(48);
        builder.Property(promotion => promotion.Name).HasMaxLength(160);
        builder.Property(promotion => promotion.Description).HasMaxLength(1000);
        builder.Property(promotion => promotion.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(promotion => promotion.AppliesTo).HasConversion<string>().HasMaxLength(16);
        builder.Property(promotion => promotion.Stacking).HasConversion<string>().HasMaxLength(16);

        builder.OwnsOne(promotion => promotion.Scope, scope => scope.ToJson());

        builder.OwnsOne(promotion => promotion.Conditions, conditions =>
        {
            conditions.ToJson();

            // The tier ladder is a collection of complex values inside the JSON document, and EF
            // will not infer that on its own: without this it reads List<PromotionTier> as a
            // relationship to a table that does not exist and refuses to build the model.
            conditions.OwnsMany(condition => condition.Tiers);
        });

        // Unique where there is a code at all. Automatic rules carry no code and there may be any
        // number of them, so the filter is what makes both true at once.
        builder
            .HasIndex(promotion => new { promotion.TenantId, promotion.Code })
            .IsUnique()
            .HasFilter("code IS NOT NULL");

        // The candidate query on every cart render: the live ones, in the order they are walked.
        builder.HasIndex(promotion => new { promotion.TenantId, promotion.IsActive, promotion.Priority });

        builder.Ignore(promotion => promotion.DomainEvents);
        builder.Ignore(promotion => promotion.HasUsesLeft);
    }
}

/// <summary>Maps <see cref="PromotionRedemption"/> to <c>pricing.promotion_redemptions</c>.</summary>
internal sealed class PromotionRedemptionConfiguration : IEntityTypeConfiguration<PromotionRedemption>
{
    public void Configure(EntityTypeBuilder<PromotionRedemption> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("promotion_redemptions", table =>
        {
            table.HasCheckConstraint("ck_promotion_redemptions_status", PricingCheckConstraints.RedemptionStatuses);
            table.HasCheckConstraint("ck_promotion_redemptions_amount", "discount_amount >= 0");
        });

        builder.HasKey(redemption => redemption.Id);
        builder.Property(redemption => redemption.Id).ValueGeneratedNever();

        builder.Property(redemption => redemption.Code).HasMaxLength(48);
        builder.Property(redemption => redemption.DiscountAmount).HasColumnType(ModelConventions.MoneyColumnType);
        builder.Property(redemption => redemption.Status).HasConversion<string>().HasMaxLength(16);

        // One row per promotion per order. This is what makes redemption idempotent under
        // at-least-once delivery: an OrderPlaced seen twice inserts once and the second insert is a
        // conflict the ledger reads as "already done" rather than an error.
        builder
            .HasIndex(redemption => new { redemption.TenantId, redemption.PromotionId, redemption.OrderId })
            .IsUnique();

        // The per-customer limit check, and the "offers you have used" screen.
        builder.HasIndex(redemption => new
        {
            redemption.TenantId,
            redemption.CustomerId,
            redemption.PromotionId,
        });

        // Reversal on cancellation reads every redemption of one order.
        builder.HasIndex(redemption => new { redemption.TenantId, redemption.OrderId });

        builder.Ignore(redemption => redemption.DomainEvents);
    }
}

/// <summary>Maps <see cref="Wallet"/> to <c>pricing.wallets</c>.</summary>
internal sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "wallets",
            table => table.HasCheckConstraint("ck_wallets_balance", "balance_amount >= 0"));

        builder.HasKey(wallet => wallet.Id);
        builder.Property(wallet => wallet.Id).ValueGeneratedNever();

        builder.HasMoney(wallet => wallet.Balance);

        // One wallet per shopper. Two would split a balance, and whichever half a redemption found
        // would be the one the shopper was told they had.
        builder.HasIndex(wallet => new { wallet.TenantId, wallet.CustomerId }).IsUnique();

        builder.Ignore(wallet => wallet.DomainEvents);
    }
}

/// <summary>Maps <see cref="WalletTransaction"/> to <c>pricing.wallet_transactions</c>.</summary>
internal sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("wallet_transactions", table =>
        {
            table.HasCheckConstraint("ck_wallet_transactions_type", PricingCheckConstraints.WalletTransactionTypes);

            // The amount is always positive and the type carries the sign. A signed amount plus a
            // type is two places to read the direction from, and they eventually disagree.
            table.HasCheckConstraint("ck_wallet_transactions_amount", "amount_amount > 0");
            table.HasCheckConstraint("ck_wallet_transactions_balance", "balance_after_amount >= 0");
        });

        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.Id).ValueGeneratedNever();

        builder.Property(transaction => transaction.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(transaction => transaction.Reason).HasMaxLength(48);
        builder.Property(transaction => transaction.ReferenceType).HasMaxLength(32);
        builder.Property(transaction => transaction.Note).HasMaxLength(500);

        builder.HasMoney(transaction => transaction.Amount);
        builder.HasMoney(transaction => transaction.BalanceAfter);

        builder.HasOne<Wallet>()
            .WithMany()
            .HasForeignKey(transaction => transaction.WalletId)
            .OnDelete(DeleteBehavior.Cascade);

        // The idempotency key. A refund event delivered twice credits once, because the second
        // insert violates this and the service reads that as "already done".
        builder
            .HasIndex(transaction => new
            {
                transaction.TenantId,
                transaction.WalletId,
                transaction.ReferenceType,
                transaction.ReferenceId,
                transaction.Type,
            })
            .IsUnique()
            .HasFilter("reference_id IS NOT NULL");

        // The statement: one wallet's movements, newest first.
        builder.HasIndex(transaction => new
        {
            transaction.TenantId,
            transaction.WalletId,
            transaction.OccurredAt,
        });

        builder.Ignore(transaction => transaction.DomainEvents);
    }
}
