using System.Text.Json;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Payments.Infrastructure.Persistence.Configurations;

/// <summary>How the open-shaped columns in this schema are written.</summary>
internal static class PaymentsJson
{
    /// <summary>
    /// camelCase, matching the API payloads these documents are handed to and received from. A
    /// <c>jsonb</c> column read by a support screen is part of the API surface, and a casing
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
internal static class PaymentsCheckConstraints
{
    /// <summary>The values <c>payments.status</c> accepts.</summary>
    public const string PaymentStatuses =
        "status IN ('Created', 'Authorized', 'Captured', 'PartiallyRefunded', 'Refunded', 'Failed', 'Cancelled')";

    /// <summary>The values <c>payments.method</c> and <c>payment_attempts.method</c> accept.</summary>
    public const string PaymentMethods =
        "method IN ('Unknown', 'Upi', 'Card', 'NetBanking', 'Wallet', 'Emi', 'Cod')";

    /// <summary>The values <c>payments.provider</c> accepts.</summary>
    public const string Providers = "provider IN ('razorpay', 'internal_cod')";

    /// <summary>
    /// The money on a collection can only ever move one way.
    /// </summary>
    /// <remarks>
    /// The database's own answer to the arithmetic that costs real money when it drifts. The domain
    /// clamps all three figures, so this should be unreachable — which is exactly why it is worth
    /// having: the day it fires, something has written a payment without going through the domain.
    /// </remarks>
    public const string PaymentAmounts =
        "amount >= 0 AND amount_captured >= 0 AND amount_refunded >= 0 "
        + "AND amount_refunded <= amount_captured + 1";

    /// <summary>The values <c>payment_attempts.source</c> accepts.</summary>
    public const string AttemptSources = "source IN ('Checkout', 'Webhook', 'Reconciliation', 'Admin')";

    /// <summary>The values <c>refunds.status</c> accepts.</summary>
    public const string RefundStatuses =
        "status IN ('Requested', 'Approved', 'Processing', 'Processed', 'Failed', 'Rejected')";

    /// <summary>The values <c>refunds.speed</c> accepts.</summary>
    public const string RefundSpeeds = "speed IN ('Normal', 'Optimum')";

    /// <summary>
    /// Nobody signs off their own refund.
    /// </summary>
    /// <remarks>
    /// The maker-checker control of docs/07-security-compliance.md §4, in the database as well as in
    /// the handler. A control that lives in only one of those two places is one a future handler can
    /// forget about, and this is the one operation in the platform where forgetting it means money
    /// leaves without a second pair of eyes.
    /// </remarks>
    public const string RefundApprover =
        "approved_by IS NULL OR initiated_by IS NULL OR approved_by <> initiated_by";

    /// <summary>The values <c>gateway_events.status</c> accepts.</summary>
    public const string GatewayEventStatuses =
        "status IN ('Pending', 'Processed', 'Ignored', 'Failed', 'DeadLettered')";

    /// <summary>The values <c>cod_collections.status</c> accepts.</summary>
    public const string CodStatuses =
        "status IN ('Pending', 'Collected', 'Remitted', 'Waived', 'WrittenOff')";

    /// <summary>The values <c>gateway_settlement_entries.match_status</c> accepts.</summary>
    public const string MatchStatuses = "match_status IN ('Unmatched', 'Matched', 'Mismatched')";

    /// <summary>The values <c>gateway_settlement_entries.entry_type</c> accepts.</summary>
    public const string EntryTypes =
        "entry_type IN ('payment', 'refund', 'adjustment', 'transfer')";
}

/// <summary>Maps <see cref="Payment"/> to <c>payments.payments</c>.</summary>
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payments", table =>
        {
            table.HasCheckConstraint("ck_payments_status", PaymentsCheckConstraints.PaymentStatuses);
            table.HasCheckConstraint("ck_payments_method", PaymentsCheckConstraints.PaymentMethods);
            table.HasCheckConstraint("ck_payments_provider", PaymentsCheckConstraints.Providers);
            table.HasCheckConstraint("ck_payments_amounts", PaymentsCheckConstraints.PaymentAmounts);
        });

        builder.HasKey(payment => payment.Id);
        builder.Property(payment => payment.Id).ValueGeneratedNever();

        builder.Property(payment => payment.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(payment => payment.Provider).HasMaxLength(32).IsRequired();
        builder.Property(payment => payment.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(payment => payment.Method).HasConversion<string>().HasMaxLength(16);
        builder.Property(payment => payment.ProviderOrderId).HasMaxLength(64);
        builder.Property(payment => payment.ProviderPaymentId).HasMaxLength(64);
        builder.Property(payment => payment.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(payment => payment.Receipt).HasMaxLength(64).IsRequired();
        builder.Property(payment => payment.Notes).HasMaxLength(2000);
        builder.Property(payment => payment.FailureCode).HasMaxLength(64);
        builder.Property(payment => payment.FailureReason).HasMaxLength(500);

        builder.Property(payment => payment.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        foreach (var money in new[]
                 {
                     nameof(Payment.Amount),
                     nameof(Payment.AmountCaptured),
                     nameof(Payment.AmountRefunded),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        builder.HasMany(payment => payment.Attempts)
            .WithOne()
            .HasForeignKey(attempt => attempt.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(payment => payment.Refunds)
            .WithOne()
            .HasForeignKey(refund => refund.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(payment => payment.Attempts).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(payment => payment.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Idempotency, half one. A replayed placement finds the collection it already opened.
        builder.HasIndex(payment => new { payment.TenantId, payment.IdempotencyKey }).IsUnique();

        // Idempotency, half two — and the interesting half. An order may have at most one *open*
        // collection, which stops a double-tap opening two, while still allowing the new collection
        // a retry after failure needs. A full unique index here would make a retry impossible.
        builder.HasIndex(payment => new { payment.TenantId, payment.OrderId })
            .IsUnique()
            .HasFilter("status IN ('Created', 'Authorized')")
            .HasDatabaseName("ix_payments_open_per_order");

        // A webhook arrives naming a gateway id and nothing else. Both lookups are on the hot path
        // of every event, so both are indexed rather than one being a scan.
        builder.HasIndex(payment => new { payment.TenantId, payment.ProviderOrderId });
        builder.HasIndex(payment => new { payment.TenantId, payment.ProviderPaymentId });

        // The shopper's "where is my payment", and the support screen's search by order number.
        builder.HasIndex(payment => new { payment.TenantId, payment.OrderId, payment.OpenedAt });
        builder.HasIndex(payment => new { payment.TenantId, payment.OrderNumber });

        // The reconciliation sweep: collections that have been open too long, oldest first.
        builder.HasIndex(payment => new { payment.TenantId, payment.Status, payment.OpenedAt });

        builder.Ignore(payment => payment.DomainEvents);
        builder.Ignore(payment => payment.AmountOutstanding);
        builder.Ignore(payment => payment.AmountRefundable);
    }
}

/// <summary>Maps <see cref="PaymentAttempt"/> to <c>payments.payment_attempts</c>.</summary>
internal sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payment_attempts", table =>
        {
            table.HasCheckConstraint("ck_payment_attempts_method", PaymentsCheckConstraints.PaymentMethods);
            table.HasCheckConstraint("ck_payment_attempts_source", PaymentsCheckConstraints.AttemptSources);
        });

        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Id).ValueGeneratedNever();

        builder.Property(attempt => attempt.ProviderPaymentId).HasMaxLength(64);
        builder.Property(attempt => attempt.Status).HasMaxLength(32).IsRequired();
        builder.Property(attempt => attempt.Method).HasConversion<string>().HasMaxLength(16);
        builder.Property(attempt => attempt.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(attempt => attempt.ErrorCode).HasMaxLength(64);
        builder.Property(attempt => attempt.ErrorDescription).HasMaxLength(500);
        builder.Property(attempt => attempt.Amount).HasColumnType(ModelConventions.MoneyColumnType);

        // A document rather than columns: every rail describes itself differently and the set grows
        // whenever the gateway adds one. It is a closed type all the same, so a future adapter
        // cannot quietly start storing a card number in it.
        builder.Property(attempt => attempt.Detail)
            .HasColumnName("method_detail")
            .HasColumnType(PaymentsJson.ColumnType)
            .HasConversion(
                detail => JsonSerializer.Serialize(detail, PaymentsJson.Options),
                json => JsonSerializer.Deserialize<PaymentMethodDetail>(json, PaymentsJson.Options)!,
                PaymentsJson.Comparer<PaymentMethodDetail>());

        // The support question: every try against one collection, oldest first.
        builder.HasIndex(attempt => new { attempt.TenantId, attempt.PaymentId, attempt.AttemptedAt });

        // "The customer quotes this gateway id" — the second commonest payment support call.
        builder.HasIndex(attempt => new { attempt.TenantId, attempt.ProviderPaymentId });

        builder.Ignore(attempt => attempt.DomainEvents);
    }
}

/// <summary>Maps <see cref="Refund"/> to <c>payments.refunds</c>.</summary>
internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refunds", table =>
        {
            table.HasCheckConstraint("ck_refunds_status", PaymentsCheckConstraints.RefundStatuses);
            table.HasCheckConstraint("ck_refunds_speed", PaymentsCheckConstraints.RefundSpeeds);
            table.HasCheckConstraint("ck_refunds_approver", PaymentsCheckConstraints.RefundApprover);
            table.HasCheckConstraint("ck_refunds_amount", "amount >= 0");
        });

        builder.HasKey(refund => refund.Id);
        builder.Property(refund => refund.Id).ValueGeneratedNever();

        builder.Property(refund => refund.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(refund => refund.Speed).HasConversion<string>().HasMaxLength(16);
        builder.Property(refund => refund.Reason).HasMaxLength(500).IsRequired();
        builder.Property(refund => refund.ProviderRefundId).HasMaxLength(64);
        builder.Property(refund => refund.RejectedReason).HasMaxLength(500);
        builder.Property(refund => refund.FailureReason).HasMaxLength(500);
        builder.Property(refund => refund.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(refund => refund.Amount).HasColumnType(ModelConventions.MoneyColumnType);

        builder.Property(refund => refund.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        // The one operation where a duplicate is money out of the door twice, so the guarantee is an
        // index rather than a caller remembering to send a key.
        builder.HasIndex(refund => new { refund.TenantId, refund.IdempotencyKey }).IsUnique();

        builder.HasIndex(refund => new { refund.TenantId, refund.PaymentId });
        builder.HasIndex(refund => new { refund.TenantId, refund.OrderId });
        builder.HasIndex(refund => new { refund.TenantId, refund.ProviderRefundId });

        // The approvals queue, and the finance report on what went back and when.
        builder.HasIndex(refund => new { refund.TenantId, refund.Status, refund.InitiatedAt });

        builder.Ignore(refund => refund.DomainEvents);
        builder.Ignore(refund => refund.IsOpen);
    }
}

/// <summary>Maps <see cref="GatewayEvent"/> to <c>payments.gateway_events</c>.</summary>
internal sealed class GatewayEventConfiguration : IEntityTypeConfiguration<GatewayEvent>
{
    public void Configure(EntityTypeBuilder<GatewayEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("gateway_events", table =>
            table.HasCheckConstraint("ck_gateway_events_status", PaymentsCheckConstraints.GatewayEventStatuses));

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.Provider).HasMaxLength(32).IsRequired();
        builder.Property(entry => entry.ProviderEventId).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.EventType).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(entry => entry.ProcessError).HasMaxLength(1000);

        builder.Property(entry => entry.Payload)
            .HasColumnType(PaymentsJson.ColumnType)
            .IsRequired();

        // Replay protection, and the reason this table exists at all. A redelivered webhook collides
        // here and becomes a no-op, which is the guarantee docs/04-api-specification.md §5 requires.
        builder.HasIndex(entry => new { entry.TenantId, entry.Provider, entry.ProviderEventId })
            .IsUnique();

        // The processor's claim query: pending or retry-due, oldest first.
        builder.HasIndex(entry => new { entry.TenantId, entry.Status, entry.NextAttemptAt });

        // The operator's view: the dead-letter queue, and the log of everything about one payment.
        builder.HasIndex(entry => new { entry.TenantId, entry.PaymentId });
        builder.HasIndex(entry => new { entry.TenantId, entry.ReceivedAt });

        builder.Ignore(entry => entry.DomainEvents);
    }
}

/// <summary>Maps <see cref="CodCollection"/> to <c>payments.cod_collections</c>.</summary>
internal sealed class CodCollectionConfiguration : IEntityTypeConfiguration<CodCollection>
{
    public void Configure(EntityTypeBuilder<CodCollection> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("cod_collections", table =>
        {
            table.HasCheckConstraint("ck_cod_collections_status", PaymentsCheckConstraints.CodStatuses);
            table.HasCheckConstraint("ck_cod_collections_amount", "amount >= 0");
        });

        builder.HasKey(collection => collection.Id);
        builder.Property(collection => collection.Id).ValueGeneratedNever();

        builder.Property(collection => collection.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(collection => collection.RemittanceReference).HasMaxLength(128);
        builder.Property(collection => collection.Note).HasMaxLength(500);

        builder.Property(collection => collection.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        foreach (var money in new[]
                 {
                     nameof(CodCollection.Amount),
                     nameof(CodCollection.CollectedAmount),
                     nameof(CodCollection.RemittedAmount),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        // Cash is collected once, at one door. Two rows against one parcel would be two people
        // expecting the same money.
        builder.HasIndex(collection => new { collection.TenantId, collection.SubOrderId }).IsUnique();

        builder.HasIndex(collection => new { collection.TenantId, collection.OrderId });

        // The remittance worklist: what has been collected and not yet handed over, by seller.
        builder.HasIndex(collection => new { collection.TenantId, collection.Status, collection.CollectedAt });
        builder.HasIndex(collection => new { collection.TenantId, collection.VendorId, collection.Status });

        builder.Ignore(collection => collection.DomainEvents);
    }
}

/// <summary>Maps <see cref="GatewaySettlement"/> to <c>payments.gateway_settlements</c>.</summary>
internal sealed class GatewaySettlementConfiguration : IEntityTypeConfiguration<GatewaySettlement>
{
    public void Configure(EntityTypeBuilder<GatewaySettlement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("gateway_settlements");

        builder.HasKey(settlement => settlement.Id);
        builder.Property(settlement => settlement.Id).ValueGeneratedNever();

        builder.Property(settlement => settlement.Provider).HasMaxLength(32).IsRequired();
        builder.Property(settlement => settlement.ProviderSettlementId).HasMaxLength(64).IsRequired();
        builder.Property(settlement => settlement.Utr).HasMaxLength(64);
        builder.Property(settlement => settlement.Status).HasMaxLength(32).IsRequired();
        builder.Property(settlement => settlement.Raw).HasColumnType(PaymentsJson.ColumnType).IsRequired();

        builder.Property(settlement => settlement.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        foreach (var money in new[]
                 {
                     nameof(GatewaySettlement.Amount),
                     nameof(GatewaySettlement.Fees),
                     nameof(GatewaySettlement.Tax),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        builder.HasMany(settlement => settlement.Entries)
            .WithOne()
            .HasForeignKey(entry => entry.SettlementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(settlement => settlement.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Imports are idempotent: a lookback window that re-reads yesterday's report collides here
        // rather than importing it twice.
        builder.HasIndex(settlement => new { settlement.TenantId, settlement.Provider, settlement.ProviderSettlementId })
            .IsUnique();

        builder.HasIndex(settlement => new { settlement.TenantId, settlement.SettledAt });

        builder.Ignore(settlement => settlement.DomainEvents);
    }
}

/// <summary>Maps <see cref="GatewaySettlementEntry"/> to <c>payments.gateway_settlement_entries</c>.</summary>
internal sealed class GatewaySettlementEntryConfiguration : IEntityTypeConfiguration<GatewaySettlementEntry>
{
    public void Configure(EntityTypeBuilder<GatewaySettlementEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("gateway_settlement_entries", table =>
        {
            table.HasCheckConstraint("ck_settlement_entries_match", PaymentsCheckConstraints.MatchStatuses);
            table.HasCheckConstraint("ck_settlement_entries_type", PaymentsCheckConstraints.EntryTypes);
        });

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.EntryType).HasMaxLength(24).IsRequired();
        builder.Property(entry => entry.ProviderEntryId).HasMaxLength(64);
        builder.Property(entry => entry.ProviderPaymentId).HasMaxLength(64);
        builder.Property(entry => entry.MatchStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(entry => entry.MismatchReason).HasMaxLength(500);

        foreach (var money in new[]
                 {
                     nameof(GatewaySettlementEntry.Amount),
                     nameof(GatewaySettlementEntry.Fee),
                     nameof(GatewaySettlementEntry.Tax),
                     nameof(GatewaySettlementEntry.Debit),
                     nameof(GatewaySettlementEntry.Credit),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        // "Show me this report's mismatches" — the only screen these rows exist for.
        builder.HasIndex(entry => new { entry.TenantId, entry.SettlementId, entry.MatchStatus });

        // Matching walks from the gateway's payment id to ours.
        builder.HasIndex(entry => new { entry.TenantId, entry.ProviderPaymentId });
        builder.HasIndex(entry => new { entry.TenantId, entry.PaymentId });

        builder.Ignore(entry => entry.DomainEvents);
    }
}
