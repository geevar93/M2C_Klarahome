using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Reporting.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Reporting.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>CHECK</c> lists, written once so a column and the constraint on it cannot drift apart.
/// </summary>
/// <remarks>
/// The quantity and amount bounds matter more here than the enum lists do. A negative unit count in
/// a fact table is a number that silently subtracts from a total nobody is checking, and it would
/// surface as a business question — "why did last Tuesday go down?" — rather than as an error.
/// </remarks>
internal static class ReportingCheckConstraints
{
    /// <summary>The values <c>fact_payments.kind</c> accepts.</summary>
    public const string PaymentKinds = "kind IN ('Captured', 'Refunded', 'CodCollected', 'Failed')";

    /// <summary>The values <c>fact_funnel_events.step</c> accepts.</summary>
    public const string FunnelSteps =
        "step IN ('CartAbandoned', 'CartConverted', 'OrderPlaced', 'OrderPaid', 'OrderConfirmed')";

    /// <summary>The values <c>report_schedules.frequency</c> accepts.</summary>
    public const string Frequencies = "frequency IN ('Daily', 'Weekly', 'Monthly')";

    /// <summary>The values a format column accepts.</summary>
    public const string Formats = "format IN ('Csv')";

    /// <summary>The values <c>report_runs.status</c> accepts.</summary>
    public const string RunStatuses = "status IN ('Running', 'Completed', 'Failed')";

    /// <summary>A schedule's hour is an hour.</summary>
    public const string ScheduleHour = "hour_utc BETWEEN 0 AND 23";

    /// <summary>Its day fields are days, and only the relevant one is set.</summary>
    /// <remarks>
    /// The second half is what stops a schedule carrying both a weekday and a day of the month, which
    /// would leave "when does this run" answerable two ways depending on which the code read first.
    /// </remarks>
    public const string ScheduleDays =
        "(day_of_week IS NULL OR day_of_week BETWEEN 1 AND 7) "
        + "AND (day_of_month IS NULL OR day_of_month BETWEEN 1 AND 31) "
        + "AND (frequency <> 'Weekly' OR day_of_month IS NULL) "
        + "AND (frequency <> 'Monthly' OR day_of_week IS NULL) "
        + "AND (frequency <> 'Daily' OR (day_of_week IS NULL AND day_of_month IS NULL))";

    /// <summary>A period ends after it starts.</summary>
    public const string Period = "period_end > period_start";

    /// <summary>Quantities on an order line are counts, and what came back is not more than went out.</summary>
    public const string OrderLineQuantities =
        "quantity >= 0 AND cancelled_quantity >= 0 AND returned_quantity >= 0 "
        + "AND cancelled_quantity + returned_quantity <= quantity";

    /// <summary>Amounts on an order line are not negative, and what came back is not more than it cost.</summary>
    public const string OrderLineAmounts =
        "line_total >= 0 AND cancelled_amount >= 0 AND returned_amount >= 0 "
        + "AND cancelled_amount + returned_amount <= line_total";

    /// <summary>A movement of money is a positive amount; its direction is the kind.</summary>
    public const string PaymentAmount = "amount >= 0";

    /// <summary>A returned quantity is a count and its value is not negative.</summary>
    public const string ReturnAmounts = "quantity > 0 AND amount >= 0";

    /// <summary>Stock counts are counts and an age is not negative.</summary>
    public const string StockCounts =
        "quantity_on_hand >= 0 AND quantity_reserved >= 0 AND (age_days IS NULL OR age_days >= 0)";

    /// <summary>A completed run says how many rows it holds and where the file is.</summary>
    /// <remarks>
    /// The invariant the download endpoint depends on. A run marked complete with no key is one the
    /// admin app would offer a download button for that answers 500.
    /// </remarks>
    public const string RunCompleteness =
        "status <> 'Completed' OR (storage_key IS NOT NULL AND row_count IS NOT NULL)";
}

/// <summary>Maps <see cref="OrderFact"/> to <c>reporting.fact_orders</c>.</summary>
internal sealed class OrderFactConfiguration : IEntityTypeConfiguration<OrderFact>
{
    public void Configure(EntityTypeBuilder<OrderFact> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("fact_orders");

        builder.HasKey(fact => fact.Id);
        builder.Property(fact => fact.Id).ValueGeneratedNever();

        builder.Property(fact => fact.OrderNumber).HasMaxLength(40).IsRequired();
        builder.Property(fact => fact.PaymentMethod).HasMaxLength(30).IsRequired();
        builder.Property(fact => fact.Status).HasMaxLength(30).IsRequired();
        builder.Property(fact => fact.CurrencyCode).HasColumnType(ModelConventions.CurrencyColumnType);

        builder.Property(fact => fact.GrandTotal).HasColumnType(ModelConventions.MoneyColumnType);
        builder.Property(fact => fact.AmountPayable).HasColumnType(ModelConventions.MoneyColumnType);

        // One row per order, and the reason a redelivered OrderPlaced does not double the day's count.
        builder.HasIndex(fact => new { fact.TenantId, fact.OrderId })
            .IsUnique()
            .HasDatabaseName("ux_fact_orders_order");

        // Every report over this table filters on the day and groups on it. Stored rather than
        // derived precisely so this index can be used.
        builder.HasIndex(fact => new { fact.TenantId, fact.PlacedOn });

        // The COD-versus-prepaid split, which is the one cut this table is asked for by name.
        builder.HasIndex(fact => new { fact.TenantId, fact.PlacedOn, fact.IsCod });
    }
}

/// <summary>Maps <see cref="SaleLineFact"/> to <c>reporting.fact_order_lines</c>.</summary>
internal sealed class OrderLineFactConfiguration : IEntityTypeConfiguration<SaleLineFact>
{
    public void Configure(EntityTypeBuilder<SaleLineFact> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("fact_order_lines", table =>
        {
            table.HasCheckConstraint("ck_fact_order_lines_quantities", ReportingCheckConstraints.OrderLineQuantities);
            table.HasCheckConstraint("ck_fact_order_lines_amounts", ReportingCheckConstraints.OrderLineAmounts);
        });

        builder.HasKey(fact => fact.Id);
        builder.Property(fact => fact.Id).ValueGeneratedNever();

        builder.Property(fact => fact.OrderNumber).HasMaxLength(40).IsRequired();
        builder.Property(fact => fact.Sku).HasMaxLength(64).IsRequired();
        builder.Property(fact => fact.ProductName).HasMaxLength(300).IsRequired();
        builder.Property(fact => fact.CategoryName).HasMaxLength(200);
        builder.Property(fact => fact.BrandName).HasMaxLength(200);
        builder.Property(fact => fact.PaymentMethod).HasMaxLength(30).IsRequired();

        builder.Property(fact => fact.LineTotal).HasColumnType(ModelConventions.MoneyColumnType);
        builder.Property(fact => fact.CancelledAmount).HasColumnType(ModelConventions.MoneyColumnType);
        builder.Property(fact => fact.ReturnedAmount).HasColumnType(ModelConventions.MoneyColumnType);
        builder.Property(fact => fact.CommissionAmount).HasColumnType(ModelConventions.MoneyColumnType);

        builder.HasIndex(fact => new { fact.TenantId, fact.OrderLineId })
            .IsUnique()
            .HasDatabaseName("ux_fact_order_lines_line");

        // The three cuts this table exists for, each with the day first because every report filters
        // on a period before it groups on anything.
        builder.HasIndex(fact => new { fact.TenantId, fact.ConfirmedOn });
        builder.HasIndex(fact => new { fact.TenantId, fact.ConfirmedOn, fact.CategoryId });
        builder.HasIndex(fact => new { fact.TenantId, fact.ConfirmedOn, fact.VendorId });

        // Top and slow SKUs, which group on the unit rather than on a date.
        builder.HasIndex(fact => new { fact.TenantId, fact.Sku, fact.ConfirmedOn });

        // The lookup the cancellation, return, delivery and commission handlers all make.
        builder.HasIndex(fact => fact.SubOrderId);
    }
}

/// <summary>Maps <see cref="PaymentFact"/> to <c>reporting.fact_payments</c>.</summary>
internal sealed class PaymentFactConfiguration : IEntityTypeConfiguration<PaymentFact>
{
    public void Configure(EntityTypeBuilder<PaymentFact> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("fact_payments", table =>
        {
            table.HasCheckConstraint("ck_fact_payments_kind", ReportingCheckConstraints.PaymentKinds);
            table.HasCheckConstraint("ck_fact_payments_amount", ReportingCheckConstraints.PaymentAmount);
        });

        builder.HasKey(fact => fact.Id);
        builder.Property(fact => fact.Id).ValueGeneratedNever();

        builder.Property(fact => fact.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(fact => fact.Method).HasMaxLength(30).IsRequired();
        builder.Property(fact => fact.Amount).HasColumnType(ModelConventions.MoneyColumnType);
        builder.Property(fact => fact.CurrencyCode).HasColumnType(ModelConventions.CurrencyColumnType);

        // Keyed on the event rather than on the payment: one payment produces a capture and possibly
        // several refunds, and each of those is its own row.
        builder.HasIndex(fact => new { fact.TenantId, fact.SourceEventId })
            .IsUnique()
            .HasDatabaseName("ux_fact_payments_event");

        builder.HasIndex(fact => new { fact.TenantId, fact.OccurredOn, fact.Kind });
        builder.HasIndex(fact => fact.OrderId);
    }
}

/// <summary>Maps <see cref="ReturnedLineFact"/> to <c>reporting.fact_return_lines</c>.</summary>
internal sealed class ReturnLineFactConfiguration : IEntityTypeConfiguration<ReturnedLineFact>
{
    public void Configure(EntityTypeBuilder<ReturnedLineFact> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("fact_return_lines", table =>
            table.HasCheckConstraint("ck_fact_return_lines_amounts", ReportingCheckConstraints.ReturnAmounts));

        builder.HasKey(fact => fact.Id);
        builder.Property(fact => fact.Id).ValueGeneratedNever();

        builder.Property(fact => fact.Sku).HasMaxLength(64).IsRequired();
        builder.Property(fact => fact.CategoryName).HasMaxLength(200);
        builder.Property(fact => fact.ReasonCode).HasMaxLength(60).IsRequired();
        builder.Property(fact => fact.Status).HasMaxLength(30).IsRequired();
        builder.Property(fact => fact.Disposition).HasMaxLength(30);

        builder.Property(fact => fact.Amount).HasColumnType(ModelConventions.MoneyColumnType);

        builder.HasIndex(fact => new { fact.TenantId, fact.ReturnLineId })
            .IsUnique()
            .HasDatabaseName("ux_fact_return_lines_line");

        // The report this table exists for: what came back, when, and why.
        builder.HasIndex(fact => new { fact.TenantId, fact.RequestedOn, fact.ReasonCode });
        builder.HasIndex(fact => fact.ReturnId);
    }
}

/// <summary>Maps <see cref="SettlementFact"/> to <c>reporting.fact_settlements</c>.</summary>
internal sealed class SettlementFactConfiguration : IEntityTypeConfiguration<SettlementFact>
{
    public void Configure(EntityTypeBuilder<SettlementFact> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("fact_settlements", table =>
            table.HasCheckConstraint("ck_fact_settlements_period", ReportingCheckConstraints.Period));

        builder.HasKey(fact => fact.Id);
        builder.Property(fact => fact.Id).ValueGeneratedNever();

        builder.Property(fact => fact.CurrencyCode).HasColumnType(ModelConventions.CurrencyColumnType);

        foreach (var money in new[] { "GrossSales", "Commission", "Fees", "Tcs", "Tds", "Refunds", "NetPayable" })
        {
            builder.Property<decimal>(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        builder.HasIndex(fact => new { fact.TenantId, fact.PeriodId })
            .IsUnique()
            .HasDatabaseName("ux_fact_settlements_period");

        builder.HasIndex(fact => new { fact.TenantId, fact.ClosedOn, fact.VendorId });
    }
}

/// <summary>Maps <see cref="FunnelFact"/> to <c>reporting.fact_funnel_events</c>.</summary>
internal sealed class FunnelFactConfiguration : IEntityTypeConfiguration<FunnelFact>
{
    public void Configure(EntityTypeBuilder<FunnelFact> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("fact_funnel_events", table =>
            table.HasCheckConstraint("ck_fact_funnel_events_step", ReportingCheckConstraints.FunnelSteps));

        builder.HasKey(fact => fact.Id);
        builder.Property(fact => fact.Id).ValueGeneratedNever();

        builder.Property(fact => fact.Step).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(fact => fact.Value).HasColumnType(ModelConventions.MoneyColumnType);

        // The step is part of the uniqueness because one event can be two steps: a converted basket
        // is both the basket's outcome and the order's beginning, and both are worth counting.
        builder.HasIndex(fact => new { fact.TenantId, fact.SourceEventId, fact.Step })
            .IsUnique()
            .HasDatabaseName("ux_fact_funnel_events_event");

        builder.HasIndex(fact => new { fact.TenantId, fact.OccurredOn, fact.Step });
    }
}

/// <summary>Maps <see cref="InventoryAgeFact"/> to <c>reporting.fact_inventory_ageing</c>.</summary>
internal sealed class InventoryAgeFactConfiguration : IEntityTypeConfiguration<InventoryAgeFact>
{
    public void Configure(EntityTypeBuilder<InventoryAgeFact> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("fact_inventory_ageing", table =>
            table.HasCheckConstraint("ck_fact_inventory_ageing_counts", ReportingCheckConstraints.StockCounts));

        builder.HasKey(fact => fact.Id);
        builder.Property(fact => fact.Id).ValueGeneratedNever();

        builder.Property(fact => fact.Sku).HasMaxLength(64).IsRequired();
        builder.Property(fact => fact.WarehouseName).HasMaxLength(200).IsRequired();
        builder.Property(fact => fact.AgeBucket).HasMaxLength(20).IsRequired();

        // One row per stock line per day. Unique so a snapshot that is re-run after a failure
        // replaces rather than doubles the day — which for a series matters more than for a total,
        // because a doubled day makes the trend line wrong in both directions around it.
        builder.HasIndex(fact => new { fact.TenantId, fact.SnapshotOn, fact.ListingId, fact.WarehouseId })
            .IsUnique()
            .HasDatabaseName("ux_fact_inventory_ageing_line");

        builder.HasIndex(fact => new { fact.TenantId, fact.SnapshotOn, fact.AgeBucket });
    }
}

/// <summary>Maps <see cref="ReportSchedule"/> to <c>reporting.report_schedules</c>.</summary>
internal sealed class ReportScheduleConfiguration : IEntityTypeConfiguration<ReportSchedule>
{
    public void Configure(EntityTypeBuilder<ReportSchedule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("report_schedules", table =>
        {
            table.HasCheckConstraint("ck_report_schedules_frequency", ReportingCheckConstraints.Frequencies);
            table.HasCheckConstraint("ck_report_schedules_format", ReportingCheckConstraints.Formats);
            table.HasCheckConstraint("ck_report_schedules_hour", ReportingCheckConstraints.ScheduleHour);
            table.HasCheckConstraint("ck_report_schedules_days", ReportingCheckConstraints.ScheduleDays);
        });

        builder.HasKey(schedule => schedule.Id);
        builder.Property(schedule => schedule.Id).ValueGeneratedNever();

        builder.Property(schedule => schedule.ReportKey).HasMaxLength(60).IsRequired();
        builder.Property(schedule => schedule.Name).HasMaxLength(ReportSchedule.MaxNameLength).IsRequired();

        builder.Property(schedule => schedule.Frequency).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(schedule => schedule.Format).HasConversion<string>().HasMaxLength(10).IsRequired();

        // A text array rather than a child table. The list is short, it is always read whole, and
        // nothing ever joins to an address — which is the case docs/03-database-design.md §1 allows
        // an array for.
        builder.Property(schedule => schedule.Recipients).HasColumnType("text[]").IsRequired();

        // The worker's sweep, which runs every few minutes for ever and must never be a table scan.
        builder.HasIndex(schedule => schedule.NextRunAt)
            .HasDatabaseName("ix_report_schedules_due")
            .HasFilter("is_active");

        builder.HasIndex(schedule => new { schedule.TenantId, schedule.ReportKey });
    }
}

/// <summary>Maps <see cref="ReportRun"/> to <c>reporting.report_runs</c>.</summary>
internal sealed class ReportRunConfiguration : IEntityTypeConfiguration<ReportRun>
{
    public void Configure(EntityTypeBuilder<ReportRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("report_runs", table =>
        {
            table.HasCheckConstraint("ck_report_runs_status", ReportingCheckConstraints.RunStatuses);
            table.HasCheckConstraint("ck_report_runs_format", ReportingCheckConstraints.Formats);
            table.HasCheckConstraint("ck_report_runs_period", ReportingCheckConstraints.Period);
            table.HasCheckConstraint("ck_report_runs_completeness", ReportingCheckConstraints.RunCompleteness);
        });

        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();

        builder.Property(run => run.ReportKey).HasMaxLength(60).IsRequired();
        builder.Property(run => run.StorageKey).HasMaxLength(512);
        builder.Property(run => run.Error).HasMaxLength(ReportRun.MaxErrorLength);

        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(run => run.Format).HasConversion<string>().HasMaxLength(10).IsRequired();

        // The history list, newest first, and the "has this schedule already run today" check.
        builder.HasIndex(run => new { run.TenantId, run.StartedAt });
        builder.HasIndex(run => new { run.ScheduleId, run.StartedAt });
    }
}
