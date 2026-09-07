using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Settlements.Infrastructure.Persistence.Configurations;

/// <summary>The <c>CHECK</c> lists, written once so a column and its constraint cannot drift apart.</summary>
/// <remarks>
/// There are more of them in this schema than in any other, and that is deliberate. Every other
/// module's constraints stop a row being wrong; these stop money being wrong, and the day one of
/// them fires is the day something wrote to a seller's account without going through the aggregate
/// that guards it.
/// </remarks>
internal static class SettlementsCheckConstraints
{
    /// <summary>The values <c>ledger_entries.entry_type</c> accepts.</summary>
    public const string EntryTypes =
        "entry_type IN ('sale', 'commission', 'platform_tax', 'platform_fee', 'payment_fee', "
        + "'shipping_fee', 'refund', 'refund_commission_reversal', 'tcs', 'tds', 'adjustment', "
        + "'payout')";

    /// <summary>The values <c>ledger_entries.direction</c> accepts.</summary>
    public const string Directions = "direction IN ('Credit', 'Debit')";

    /// <summary>The values <c>ledger_entries.reference_type</c> accepts.</summary>
    public const string ReferenceTypes =
        "reference_type IN ('sub-order', 'credit-note', 'cycle', 'payout', 'manual')";

    /// <summary>
    /// An entry's amount is never negative.
    /// </summary>
    /// <remarks>
    /// The direction carries the sign, so a negative amount would be a double negative that reads as
    /// a credit on half the reports and a debit on the other half.
    /// </remarks>
    public const string EntryAmounts = "amount >= 0 AND taxable_value >= 0";

    /// <summary>The values <c>settlement_cycles.status</c> accepts.</summary>
    public const string CycleStatuses = "status IN ('Open', 'Closed', 'Paid')";

    /// <summary>A period ends after it starts.</summary>
    public const string CyclePeriod = "period_end > period_start";

    /// <summary>
    /// Every figure a cycle totals is a magnitude, and none of them may be negative.
    /// </summary>
    /// <remarks>
    /// <c>net_payable</c> and <c>opening_balance</c> are deliberately absent from this list: a seller
    /// whose returns exceeded their sales is genuinely in deficit, and a constraint that refused to
    /// record it would force the arithmetic to lie rather than the ledger to be corrected.
    /// </remarks>
    public const string CycleAmounts =
        "gross_sales >= 0 AND taxable_sales >= 0 AND total_commission >= 0 AND total_fees >= 0 "
        + "AND total_refunds >= 0 AND taxable_refunds >= 0 AND tcs >= 0 AND tds >= 0";

    /// <summary>A closed period says when it closed, and an open one does not.</summary>
    public const string CycleClosed =
        "(status = 'Open' AND closed_at IS NULL) OR (status <> 'Open' AND closed_at IS NOT NULL)";

    /// <summary>The values <c>payout_batches.status</c> accepts.</summary>
    public const string BatchStatuses =
        "status IN ('Draft', 'Approved', 'Processing', 'Completed', 'PartiallyFailed', 'Failed', "
        + "'Cancelled')";

    /// <summary>
    /// Nobody signs off their own payment.
    /// </summary>
    /// <remarks>
    /// The database's own copy of the maker–checker rule, alongside the aggregate's and the
    /// handler's. Three places for one rule is more than anything else in this platform gets; it is
    /// the rule whose failure sends money, so it gets the belt, the braces and the second belt.
    /// </remarks>
    public const string BatchSelfApproval =
        "approved_by IS NULL OR requested_by IS NULL OR approved_by <> requested_by";

    /// <summary>An approved batch says who approved it and when.</summary>
    public const string BatchApproval =
        "(approved_by IS NULL AND approved_at IS NULL) OR (approved_by IS NOT NULL AND approved_at IS NOT NULL)";

    /// <summary>A batch's headline figures are magnitudes.</summary>
    public const string BatchAmounts = "total_amount >= 0 AND vendor_count >= 0";

    /// <summary>The values <c>payout_items.status</c> accepts.</summary>
    public const string ItemStatuses =
        "status IN ('Pending', 'Processing', 'Completed', 'Failed', 'Skipped')";

    /// <summary>A transfer never sends a negative amount.</summary>
    public const string ItemAmounts = "amount >= 0";

    /// <summary>
    /// A completed transfer names the gateway payment that made it.
    /// </summary>
    /// <remarks>
    /// The one inconsistency that would be expensive: a payout item recorded as completed with no
    /// provider id is money that left with nothing to trace it by, and a reconciliation against a
    /// bank statement would have nowhere to start.
    /// </remarks>
    public const string ItemCompleted =
        "status <> 'Completed' OR provider_payout_id IS NOT NULL";

    /// <summary>The values <c>number_sequences.kind</c> accepts.</summary>
    public const string SequenceKinds = "kind IN ('payout', 'commission-invoice')";
}

/// <summary>Maps <see cref="LedgerEntry"/> to <c>settlements.ledger_entries</c>.</summary>
internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ledger_entries", table =>
        {
            table.HasCheckConstraint("ck_ledger_entries_type", SettlementsCheckConstraints.EntryTypes);
            table.HasCheckConstraint("ck_ledger_entries_direction", SettlementsCheckConstraints.Directions);
            table.HasCheckConstraint("ck_ledger_entries_reference", SettlementsCheckConstraints.ReferenceTypes);
            table.HasCheckConstraint("ck_ledger_entries_amounts", SettlementsCheckConstraints.EntryAmounts);
        });

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.EntryType).HasMaxLength(32).IsRequired();
        builder.Property(entry => entry.ReferenceType).HasMaxLength(24).IsRequired();
        builder.Property(entry => entry.SourceKey).HasMaxLength(160).IsRequired();
        builder.Property(entry => entry.Note).HasMaxLength(500);

        builder.Property(entry => entry.Direction).HasConversion<string>().HasMaxLength(8);

        builder.Property(entry => entry.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        builder.Property(entry => entry.Amount).HasColumnType(ModelConventions.MoneyColumnType);
        builder.Property(entry => entry.TaxableValue).HasColumnType(ModelConventions.MoneyColumnType);

        // The whole point of the source key. A redelivered integration event derives the same key,
        // collides here, and posts nothing — which is what makes at-least-once delivery safe for a
        // table that moves money.
        builder.HasIndex(entry => new { entry.TenantId, entry.SourceKey }).IsUnique();

        // The statement: one seller's movements in date order. The first index this schema needs and
        // the one nearly every read uses.
        builder.HasIndex(entry => new { entry.TenantId, entry.VendorId, entry.OccurredAt });

        // What a cycle draws in, and what is still free to be drawn into one. Filtered, because the
        // unsettled entries are a small and shrinking slice of a table that only ever grows.
        builder
            .HasIndex(entry => new { entry.TenantId, entry.VendorId, entry.OccurredAt })
            .HasDatabaseName("ix_ledger_entries_unsettled")
            .HasFilter("settlement_cycle_id IS NULL");

        builder.HasIndex(entry => new { entry.TenantId, entry.SettlementCycleId });
        builder.HasIndex(entry => new { entry.TenantId, entry.PayoutBatchId });

        // "Why was I charged this against that order" — the second question every seller asks.
        builder.HasIndex(entry => new { entry.TenantId, entry.SubOrderId });

        builder.Ignore(entry => entry.DomainEvents);
        builder.Ignore(entry => entry.SignedAmount);
        builder.Ignore(entry => entry.IsSettled);
    }
}

/// <summary>Maps <see cref="SettlementCycle"/> to <c>settlements.settlement_cycles</c>.</summary>
internal sealed class SettlementCycleConfiguration : IEntityTypeConfiguration<SettlementCycle>
{
    public void Configure(EntityTypeBuilder<SettlementCycle> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("settlement_cycles", table =>
        {
            table.HasCheckConstraint("ck_settlement_cycles_status", SettlementsCheckConstraints.CycleStatuses);
            table.HasCheckConstraint("ck_settlement_cycles_period", SettlementsCheckConstraints.CyclePeriod);
            table.HasCheckConstraint("ck_settlement_cycles_amounts", SettlementsCheckConstraints.CycleAmounts);
            table.HasCheckConstraint("ck_settlement_cycles_closed", SettlementsCheckConstraints.CycleClosed);
        });

        builder.HasKey(cycle => cycle.Id);
        builder.Property(cycle => cycle.Id).ValueGeneratedNever();

        builder.Property(cycle => cycle.Status).HasConversion<string>().HasMaxLength(16);

        builder.Property(cycle => cycle.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        foreach (var money in new[]
                 {
                     nameof(SettlementCycle.OpeningBalance),
                     nameof(SettlementCycle.GrossSales),
                     nameof(SettlementCycle.TaxableSales),
                     nameof(SettlementCycle.TotalCommission),
                     nameof(SettlementCycle.TotalFees),
                     nameof(SettlementCycle.TotalRefunds),
                     nameof(SettlementCycle.TaxableRefunds),
                     nameof(SettlementCycle.TotalAdjustments),
                     nameof(SettlementCycle.TotalPayouts),
                     nameof(SettlementCycle.Tcs),
                     nameof(SettlementCycle.Tds),
                     nameof(SettlementCycle.NetPayable),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        // One period per seller. It is what stops a scheduler running twice from opening a second
        // cycle over the same days and settling the same earnings again.
        builder
            .HasIndex(cycle => new { cycle.TenantId, cycle.VendorId, cycle.PeriodStart })
            .IsUnique();

        // The two worklists: what is due to be closed, and what is closed and waiting to be paid.
        builder.HasIndex(cycle => new { cycle.TenantId, cycle.Status, cycle.PeriodEnd });
        builder.HasIndex(cycle => new { cycle.TenantId, cycle.PayoutBatchId });

        builder.Ignore(cycle => cycle.DomainEvents);
        builder.Ignore(cycle => cycle.TotalDeductions);
        builder.Ignore(cycle => cycle.IsClosed);
    }
}

/// <summary>Maps <see cref="PayoutBatch"/> to <c>settlements.payout_batches</c>.</summary>
internal sealed class PayoutBatchConfiguration : IEntityTypeConfiguration<PayoutBatch>
{
    public void Configure(EntityTypeBuilder<PayoutBatch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payout_batches", table =>
        {
            table.HasCheckConstraint("ck_payout_batches_status", SettlementsCheckConstraints.BatchStatuses);
            table.HasCheckConstraint("ck_payout_batches_self_approval", SettlementsCheckConstraints.BatchSelfApproval);
            table.HasCheckConstraint("ck_payout_batches_approval", SettlementsCheckConstraints.BatchApproval);
            table.HasCheckConstraint("ck_payout_batches_amounts", SettlementsCheckConstraints.BatchAmounts);
        });

        builder.HasKey(batch => batch.Id);
        builder.Property(batch => batch.Id).ValueGeneratedNever();

        builder.Property(batch => batch.Reference).HasMaxLength(32).IsRequired();
        builder.Property(batch => batch.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(batch => batch.ProviderBatchId).HasMaxLength(64);
        builder.Property(batch => batch.Provider).HasMaxLength(32);
        builder.Property(batch => batch.CancelledReason).HasMaxLength(500);

        builder.Property(batch => batch.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        builder.Property(batch => batch.TotalAmount).HasColumnType(ModelConventions.MoneyColumnType);

        builder.HasMany(batch => batch.Items)
            .WithOne()
            .HasForeignKey(item => item.PayoutBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(batch => batch.Items).AutoInclude();

        // One batch per reference. It is what finance quotes at a bank and what a support call
        // starts with.
        builder.HasIndex(batch => new { batch.TenantId, batch.Reference }).IsUnique();

        // The queue: what is waiting for a signature, and what is still in flight.
        builder.HasIndex(batch => new { batch.TenantId, batch.Status, batch.RequestedAt });

        builder.Ignore(batch => batch.DomainEvents);
        builder.Ignore(batch => batch.IsTerminal);
        builder.Ignore(batch => batch.SettledAmount);
    }
}

/// <summary>Maps <see cref="PayoutItem"/> to <c>settlements.payout_items</c>.</summary>
internal sealed class PayoutItemConfiguration : IEntityTypeConfiguration<PayoutItem>
{
    public void Configure(EntityTypeBuilder<PayoutItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payout_items", table =>
        {
            table.HasCheckConstraint("ck_payout_items_status", SettlementsCheckConstraints.ItemStatuses);
            table.HasCheckConstraint("ck_payout_items_amount", SettlementsCheckConstraints.ItemAmounts);
            table.HasCheckConstraint("ck_payout_items_completed", SettlementsCheckConstraints.ItemCompleted);
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.Property(item => item.VendorCode).HasMaxLength(32);
        builder.Property(item => item.VendorName).HasMaxLength(200);
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(item => item.DestinationAccountId).HasMaxLength(64);
        builder.Property(item => item.DestinationLast4).HasMaxLength(4);
        builder.Property(item => item.ProviderPayoutId).HasMaxLength(64);
        builder.Property(item => item.ProviderStatus).HasMaxLength(32);
        builder.Property(item => item.Utr).HasMaxLength(64);
        builder.Property(item => item.FailureReason).HasMaxLength(500);

        builder.Property(item => item.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        builder.Property(item => item.Amount).HasColumnType(ModelConventions.MoneyColumnType);

        // One line per seller per batch. A batch that paid the same seller twice would be a batch
        // whose total disagreed with what left the account.
        builder.HasIndex(item => new { item.TenantId, item.PayoutBatchId, item.VendorId }).IsUnique();

        // A cycle is discharged by exactly one transfer that succeeded. The index is not unique,
        // because a failed attempt and a later successful one both point at the same cycle.
        builder.HasIndex(item => new { item.TenantId, item.SettlementCycleId });

        // The reconciliation sweep's query: what did we hand to the gateway that has not answered.
        builder.HasIndex(item => new { item.TenantId, item.Status, item.SentAt });

        // "Where is my money" — from the seller's side, newest first.
        builder.HasIndex(item => new { item.TenantId, item.VendorId, item.CreatedAt });

        builder.Ignore(item => item.DomainEvents);
        builder.Ignore(item => item.IsSettled);
    }
}

/// <summary>Maps <see cref="CommissionInvoice"/> to <c>settlements.commission_invoices</c>.</summary>
internal sealed class CommissionInvoiceConfiguration : IEntityTypeConfiguration<CommissionInvoice>
{
    public void Configure(EntityTypeBuilder<CommissionInvoice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("commission_invoices", table => table.HasCheckConstraint(
            "ck_commission_invoices_money",
            "commission >= 0 AND platform_fee >= 0 AND payment_fee >= 0 AND taxable_value >= 0 "
            + "AND cgst >= 0 AND sgst >= 0 AND igst >= 0 AND total >= 0"));

        builder.HasKey(invoice => invoice.Id);
        builder.Property(invoice => invoice.Id).ValueGeneratedNever();

        builder.Property(invoice => invoice.InvoiceNumber).HasMaxLength(32).IsRequired();
        builder.Property(invoice => invoice.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(invoice => invoice.SupplierGstin).HasMaxLength(15);
        builder.Property(invoice => invoice.RecipientGstin).HasMaxLength(15);
        builder.Property(invoice => invoice.PlaceOfSupplyStateCode).HasMaxLength(2);

        foreach (var money in new[]
                 {
                     nameof(CommissionInvoice.Commission),
                     nameof(CommissionInvoice.PlatformFee),
                     nameof(CommissionInvoice.PaymentFee),
                     nameof(CommissionInvoice.TaxableValue),
                     nameof(CommissionInvoice.Cgst),
                     nameof(CommissionInvoice.Sgst),
                     nameof(CommissionInvoice.Igst),
                     nameof(CommissionInvoice.Total),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        builder.Property(invoice => invoice.GstRate).HasColumnType("numeric(9,4)");

        // A statutory number is unique or it is not a number. The index is the enforcement, not this
        // code: two closings racing for the same counter both reach the insert and one is rejected.
        builder.HasIndex(invoice => new { invoice.TenantId, invoice.InvoiceNumber }).IsUnique();

        // One invoice per cycle, ever. The same rule a sub-order invoice has, enforced the same way.
        builder.HasIndex(invoice => invoice.SettlementCycleId).IsUnique();

        // "Show me what I was charged", newest first.
        builder.HasIndex(invoice => new { invoice.TenantId, invoice.VendorId, invoice.IssuedAt });

        builder.Ignore(invoice => invoice.DomainEvents);
        builder.Ignore(invoice => invoice.TaxTotal);
        builder.Ignore(invoice => invoice.IsInterState);
    }
}

/// <summary>Maps <see cref="NumberSequence"/> to <c>settlements.number_sequences</c>.</summary>
internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("number_sequences", table =>
        {
            table.HasCheckConstraint("ck_number_sequences_kind", SettlementsCheckConstraints.SequenceKinds);
            table.HasCheckConstraint("ck_number_sequences_next", "next_value >= 1");
        });

        builder.HasKey(sequence => sequence.Id);
        builder.Property(sequence => sequence.Id).ValueGeneratedNever();

        builder.Property(sequence => sequence.Kind).HasMaxLength(32).IsRequired();
        builder.Property(sequence => sequence.ScopeKey).HasMaxLength(64).IsRequired();

        // One counter per series. The unique index is what makes the "insert if missing, then lock"
        // allocation safe: two requests opening the same counter at once race for it, and the loser
        // reads the winner's row rather than creating a second series.
        builder
            .HasIndex(sequence => new { sequence.TenantId, sequence.Kind, sequence.ScopeKey })
            .IsUnique();

        builder.Ignore(sequence => sequence.DomainEvents);
    }
}
