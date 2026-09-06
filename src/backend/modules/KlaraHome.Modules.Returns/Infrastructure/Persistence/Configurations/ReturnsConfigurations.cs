using System.Text.Json;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Returns.Infrastructure.Persistence.Configurations;

/// <summary>How the open-shaped columns in this schema are written.</summary>
internal static class ReturnsJson
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
internal static class ReturnsCheckConstraints
{
    /// <summary>The values <c>returns.status</c> accepts.</summary>
    public const string ReturnStatuses =
        "status IN ('Requested', 'Approved', 'Rejected', 'PickupScheduled', 'Picked', 'InTransit', "
        + "'Received', 'QcPassed', 'QcFailed', 'Refunded', 'Replaced', 'Closed', 'Cancelled')";

    /// <summary>The values <c>returns.type</c> accepts.</summary>
    public const string ReturnTypes = "type IN ('Return', 'Replacement')";

    /// <summary>The values <c>returns.refund_mode</c> accepts, or nothing at all.</summary>
    public const string RefundModes = "refund_mode IS NULL OR refund_mode IN ('Original', 'Wallet')";

    /// <summary>The values <c>return_lines.disposition</c> accepts.</summary>
    public const string Dispositions =
        "disposition IN ('Pending', 'Restock', 'Scrap', 'Quarantine')";

    /// <summary>The values <c>return_reasons.shipping_payer</c> accepts.</summary>
    public const string ShippingPayers = "shipping_payer IN ('Platform', 'Vendor', 'Customer')";

    /// <summary>The values <c>number_sequences.kind</c> accepts.</summary>
    public const string SequenceKinds = "kind IN ('return', 'credit-note')";

    /// <summary>Money on a return is never negative, whichever of the three amounts it is.</summary>
    public const string ReturnAmounts =
        "estimated_refund >= 0 AND approved_amount >= 0 AND refund_amount >= 0 "
        + "AND return_shipping_fee >= 0";

    /// <summary>
    /// A refund that has been paid says where it went, and one that has not says nothing.
    /// </summary>
    /// <remarks>
    /// The database's own answer to the inconsistency that would be expensive: a return recorded as
    /// refunded with no mode is money nobody can trace to an instrument. The workflow refuses it too,
    /// which is exactly why this is worth having — the day it fires, something wrote a return without
    /// going through the aggregate.
    /// </remarks>
    public const string RefundSettled =
        "(refund_amount = 0 AND refund_mode IS NULL) OR (refund_amount > 0 AND refund_mode IS NOT NULL)";

    /// <summary>A line cannot accept more units than were sent, nor a negative number of them.</summary>
    public const string LineQuantities =
        "quantity > 0 AND quantity_accepted >= 0 AND quantity_accepted <= quantity";

    /// <summary>Money on a return line is never negative.</summary>
    public const string LineAmounts =
        "taxable_value >= 0 AND cgst >= 0 AND sgst >= 0 AND igst >= 0 AND cess >= 0 "
        + "AND refund_amount >= 0";

    /// <summary>Money on a credit note is never negative.</summary>
    public const string CreditNoteAmounts =
        "taxable_value >= 0 AND cgst >= 0 AND sgst >= 0 AND igst >= 0 AND cess >= 0 AND total >= 0";

    /// <summary>
    /// CGST and SGST are the intra-state pair and IGST is the inter-state one; a credit note
    /// carrying both is a tax return that will not add up.
    /// </summary>
    public const string CreditNoteTaxSplit = "(igst = 0) OR (cgst = 0 AND sgst = 0)";

    /// <summary>A credit note is always a seller's, because it is raised under their GSTIN.</summary>
    public const string CreditNoteVendor = "vendor_id IS NOT NULL";
}

/// <summary>Maps <see cref="ReturnRequest"/> to <c>returns.returns</c>.</summary>
internal sealed class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("returns", table =>
        {
            table.HasCheckConstraint("ck_returns_status", ReturnsCheckConstraints.ReturnStatuses);
            table.HasCheckConstraint("ck_returns_type", ReturnsCheckConstraints.ReturnTypes);
            table.HasCheckConstraint("ck_returns_refund_mode", ReturnsCheckConstraints.RefundModes);
            table.HasCheckConstraint("ck_returns_amounts", ReturnsCheckConstraints.ReturnAmounts);
            table.HasCheckConstraint("ck_returns_refund_settled", ReturnsCheckConstraints.RefundSettled);
        });

        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).ValueGeneratedNever();

        builder.Property(request => request.ReturnNumber).HasMaxLength(32).IsRequired();
        builder.Property(request => request.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(request => request.SubOrderNumber).HasMaxLength(40).IsRequired();
        builder.Property(request => request.ReasonCode).HasMaxLength(64).IsRequired();
        builder.Property(request => request.ReasonNote).HasMaxLength(2000);
        builder.Property(request => request.RejectedReason).HasMaxLength(500);
        builder.Property(request => request.QcNotes).HasMaxLength(2000);
        builder.Property(request => request.PickupAwb).HasMaxLength(64);

        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(request => request.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(request => request.RefundMode).HasConversion<string>().HasMaxLength(16);

        builder.Property(request => request.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        foreach (var money in new[]
                 {
                     nameof(ReturnRequest.EstimatedRefund),
                     nameof(ReturnRequest.ApprovedAmount),
                     nameof(ReturnRequest.RefundAmount),
                     nameof(ReturnRequest.ReturnShippingFee),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        // A document rather than a child table. The evidence is a handful of file ids, read whole
        // every time the return is read and never joined to or filtered on in SQL — a second table
        // would buy nothing but a join on the one screen support opens most.
        builder.Property(request => request.EvidenceFileIds)
            .HasColumnType(ReturnsJson.ColumnType)
            .HasConversion(
                ids => JsonSerializer.Serialize(ids, ReturnsJson.Options),
                json => JsonSerializer.Deserialize<List<Guid>>(json, ReturnsJson.Options)!,
                ReturnsJson.Comparer<IReadOnlyList<Guid>>());

        builder.HasMany(request => request.Lines)
            .WithOne()
            .HasForeignKey(line => line.ReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(request => request.Lines).AutoInclude();

        // One RMA per number. It is what a shopper quotes and what an operator searches by.
        builder.HasIndex(request => new { request.TenantId, request.ReturnNumber }).IsUnique();

        // The two worklists: the platform's queue by state, and one seller's queue by state.
        builder.HasIndex(request => new { request.TenantId, request.Status, request.RequestedAt });
        builder.HasIndex(request => new { request.TenantId, request.VendorId, request.Status });

        // The shopper's own list, and the "is there already one open against this part" check that
        // stops a double request.
        builder.HasIndex(request => new { request.TenantId, request.CustomerId, request.RequestedAt });
        builder.HasIndex(request => new { request.TenantId, request.SubOrderId });
        builder.HasIndex(request => new { request.TenantId, request.OrderId });

        builder.Ignore(request => request.DomainEvents);
        builder.Ignore(request => request.TotalQuantity);
        builder.Ignore(request => request.TotalAccepted);
        builder.Ignore(request => request.AcceptedValue);
        builder.Ignore(request => request.IsTerminal);
        builder.Ignore(request => request.IsBeforeCollection);
    }
}

/// <summary>Maps <see cref="ReturnLine"/> to <c>returns.return_lines</c>.</summary>
internal sealed class ReturnLineConfiguration : IEntityTypeConfiguration<ReturnLine>
{
    public void Configure(EntityTypeBuilder<ReturnLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("return_lines", table =>
        {
            table.HasCheckConstraint("ck_return_lines_quantities", ReturnsCheckConstraints.LineQuantities);
            table.HasCheckConstraint("ck_return_lines_amounts", ReturnsCheckConstraints.LineAmounts);
            table.HasCheckConstraint("ck_return_lines_disposition", ReturnsCheckConstraints.Dispositions);
        });

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.Sku).HasMaxLength(64).IsRequired();
        builder.Property(line => line.QcNote).HasMaxLength(500);
        builder.Property(line => line.Disposition).HasConversion<string>().HasMaxLength(16);

        foreach (var money in new[]
                 {
                     nameof(ReturnLine.TaxableValue),
                     nameof(ReturnLine.Cgst),
                     nameof(ReturnLine.Sgst),
                     nameof(ReturnLine.Igst),
                     nameof(ReturnLine.Cess),
                     nameof(ReturnLine.RefundAmount),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        // What was bought, frozen. Written whole and read whole, exactly as the order line's own
        // snapshot is, and for the same reason: the catalogue is edited and the box is not.
        builder.Property(line => line.Snapshot)
            .HasColumnType(ReturnsJson.ColumnType)
            .HasConversion(
                snapshot => JsonSerializer.Serialize(snapshot, ReturnsJson.Options),
                json => JsonSerializer.Deserialize<ReturnLineSnapshot>(json, ReturnsJson.Options)!,
                ReturnsJson.Comparer<ReturnLineSnapshot>());

        // One row per order line per return. A shopper adding the same line twice to one request is
        // asking for a quantity, not for two rows, and the index says so.
        builder.HasIndex(line => new { line.TenantId, line.ReturnId, line.OrderLineId }).IsUnique();

        // "What has been returned against this order line" — the check that stops a second RMA
        // claiming units a first one already has in flight.
        builder.HasIndex(line => new { line.TenantId, line.OrderLineId });

        builder.Ignore(line => line.DomainEvents);
        builder.Ignore(line => line.TaxAmount);
        builder.Ignore(line => line.AcceptedRefund);
        builder.Ignore(line => line.AcceptedTaxableValue);
    }
}

/// <summary>Maps <see cref="ReturnReason"/> to <c>returns.return_reasons</c>.</summary>
internal sealed class ReturnReasonConfiguration : IEntityTypeConfiguration<ReturnReason>
{
    public void Configure(EntityTypeBuilder<ReturnReason> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("return_reasons", table => table.HasCheckConstraint(
            "ck_return_reasons_shipping_payer",
            ReturnsCheckConstraints.ShippingPayers));

        builder.HasKey(reason => reason.Id);
        builder.Property(reason => reason.Id).ValueGeneratedNever();

        builder.Property(reason => reason.Code).HasMaxLength(64).IsRequired();
        builder.Property(reason => reason.Label).HasMaxLength(160).IsRequired();
        builder.Property(reason => reason.Description).HasMaxLength(500);
        builder.Property(reason => reason.ShippingPayer).HasConversion<string>().HasMaxLength(16);

        // One reason per code. The code is what a return names for the rest of its life, so it is
        // never reused even after the reason is withdrawn.
        builder.HasIndex(reason => new { reason.TenantId, reason.Code }).IsUnique();

        // The shopper's dropdown: the active reasons, in the order somebody chose.
        builder.HasIndex(reason => new { reason.TenantId, reason.IsActive, reason.SortOrder });

        builder.Ignore(reason => reason.DomainEvents);
    }
}

/// <summary>Maps <see cref="CreditNote"/> to <c>returns.credit_notes</c>.</summary>
internal sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("credit_notes", table =>
        {
            table.HasCheckConstraint("ck_credit_notes_amounts", ReturnsCheckConstraints.CreditNoteAmounts);
            table.HasCheckConstraint("ck_credit_notes_tax_split", ReturnsCheckConstraints.CreditNoteTaxSplit);
            table.HasCheckConstraint("ck_credit_notes_vendor", ReturnsCheckConstraints.CreditNoteVendor);
        });

        builder.HasKey(note => note.Id);
        builder.Property(note => note.Id).ValueGeneratedNever();

        builder.Property(note => note.CreditNoteNumber).HasMaxLength(48).IsRequired();
        builder.Property(note => note.Series).HasMaxLength(32).IsRequired();
        builder.Property(note => note.FinancialYear).HasMaxLength(9).IsRequired();
        builder.Property(note => note.InvoiceNumber).HasMaxLength(48);
        builder.Property(note => note.PlaceOfSupplyStateCode).HasMaxLength(2);

        builder.Property(note => note.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        foreach (var money in new[]
                 {
                     nameof(CreditNote.TaxableValue),
                     nameof(CreditNote.Cgst),
                     nameof(CreditNote.Sgst),
                     nameof(CreditNote.Igst),
                     nameof(CreditNote.Cess),
                     nameof(CreditNote.Total),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        // A return is credited once. The second note against one return is the thing the gapless
        // series exists to prevent, and this index is what actually prevents it.
        builder.HasIndex(note => new { note.TenantId, note.ReturnId }).IsUnique();

        // Gapless per seller per financial year (rule 53 of the CGST Rules). The uniqueness is the
        // enforceable half; the gaplessness is the counter row's.
        builder
            .HasIndex(note => new { note.TenantId, note.VendorId, note.FinancialYear, note.CreditNoteNumber })
            .IsUnique();

        // The GST return: everything a seller credited in a period.
        builder.HasIndex(note => new { note.TenantId, note.VendorId, note.IssuedAt });

        builder.HasIndex(note => new { note.TenantId, note.OrderId });

        builder.Ignore(note => note.DomainEvents);
        builder.Ignore(note => note.TaxAmount);
    }
}

/// <summary>Maps <see cref="NumberSequence"/> to <c>returns.number_sequences</c>.</summary>
internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("number_sequences", table =>
        {
            table.HasCheckConstraint("ck_number_sequences_kind", ReturnsCheckConstraints.SequenceKinds);
            table.HasCheckConstraint("ck_number_sequences_next", "next_value >= 1");
        });

        builder.HasKey(sequence => sequence.Id);
        builder.Property(sequence => sequence.Id).ValueGeneratedNever();

        builder.Property(sequence => sequence.Kind).HasMaxLength(16).IsRequired();
        builder.Property(sequence => sequence.ScopeKey).HasMaxLength(64).IsRequired();
        builder.Property(sequence => sequence.FinancialYear).HasMaxLength(9).IsRequired();

        // One counter per series. The unique index is what makes the "insert if missing, then lock"
        // allocation safe: two requests opening the same counter at once race for it, and the loser
        // reads the winner's row rather than creating a second series.
        builder
            .HasIndex(sequence => new
            {
                sequence.TenantId,
                sequence.Kind,
                sequence.ScopeKey,
                sequence.FinancialYear,
            })
            .IsUnique();

        builder.Ignore(sequence => sequence.DomainEvents);
    }
}
