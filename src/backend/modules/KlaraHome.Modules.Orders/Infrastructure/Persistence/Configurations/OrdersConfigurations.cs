using System.Text.Json;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Orders.Infrastructure.Persistence.Configurations;

/// <summary>How the open-shaped columns in this schema are written.</summary>
internal static class OrdersJson
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
}

/// <summary>The <c>CHECK</c> lists, written once so a column and its constraint cannot drift apart.</summary>
internal static class OrdersCheckConstraints
{
    /// <summary>The values <c>orders.status</c> accepts.</summary>
    public const string OrderStatuses = "status IN ('PendingPayment', 'InProgress', 'Completed', 'Cancelled')";

    /// <summary>The values <c>orders.payment_status</c> accepts.</summary>
    public const string PaymentStatuses =
        "payment_status IN ('Pending', 'Authorized', 'Paid', 'Failed', 'PartiallyRefunded', 'Refunded')";

    /// <summary>The values <c>orders.payment_method</c> accepts.</summary>
    public const string PaymentMethods = "payment_method IN ('Prepaid', 'CashOnDelivery')";

    /// <summary>The values <c>orders.channel</c> accepts.</summary>
    public const string Channels = "channel IN ('web', 'app', 'admin')";

    /// <summary>
    /// The values <c>sub_orders.status</c> accepts. Every state of the machine, written out.
    /// </summary>
    /// <remarks>
    /// A constraint rather than trust, because this column is the one thing the whole platform
    /// switches on. A typo written by a future handler that bypassed the domain would otherwise
    /// produce a sub-order in a state nothing can move and nothing can read.
    /// </remarks>
    public const string SubOrderStatuses =
        "status IN ('PendingPayment', 'PaymentFailed', 'Confirmed', 'Processing', 'Packed', 'Shipped', "
        + "'OutForDelivery', 'DeliveryFailed', 'RtoInitiated', 'RtoDelivered', 'Delivered', "
        + "'ReturnRequested', 'ReturnInProgress', 'Returned', 'Completed', 'Cancelled')";

    /// <summary>The values <c>order_lines.status</c> accepts.</summary>
    public const string LineStatuses =
        "status IN ('Active', 'PartiallyCancelled', 'Cancelled', 'PartiallyReturned', 'Returned')";

    /// <summary>The values <c>sub_orders.cancelled_by</c> accepts.</summary>
    public const string CancellationInitiators =
        "cancelled_by IS NULL OR cancelled_by IN ('Customer', 'Vendor', 'Platform', 'System')";

    /// <summary>The values <c>invoices.status</c> accepts.</summary>
    public const string InvoiceStatuses = "status IN ('Issued', 'Cancelled')";

    /// <summary>The values <c>order_events.type</c> accepts.</summary>
    public const string EventTypes =
        "type IN ('placed', 'status-changed', 'cancelled', 'invoiced', 'payment', 'note')";

    /// <summary>The values <c>number_sequences.kind</c> accepts.</summary>
    public const string SequenceKinds = "kind IN ('order', 'invoice')";

    /// <summary>
    /// A line can never have more units cancelled or returned than were ordered.
    /// </summary>
    /// <remarks>
    /// The database's own answer to the one arithmetic that costs money if it drifts. The domain
    /// clamps both counters, so this constraint should be unreachable — which is exactly why it is
    /// worth having: the day it fires, something has written a line without going through the domain.
    /// </remarks>
    public const string LineQuantities =
        "quantity >= 1 AND quantity_cancelled >= 0 AND quantity_returned >= 0 "
        + "AND quantity_cancelled <= quantity AND quantity_returned <= quantity";
}

/// <summary>Maps <see cref="Order"/> to <c>orders.orders</c>.</summary>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("orders", table =>
        {
            table.HasCheckConstraint("ck_orders_status", OrdersCheckConstraints.OrderStatuses);
            table.HasCheckConstraint("ck_orders_payment_status", OrdersCheckConstraints.PaymentStatuses);
            table.HasCheckConstraint("ck_orders_payment_method", OrdersCheckConstraints.PaymentMethods);
            table.HasCheckConstraint("ck_orders_channel", OrdersCheckConstraints.Channels);
            table.HasCheckConstraint(
                "ck_orders_totals",
                "items_total >= 0 AND discount_total >= 0 AND shipping_total >= 0 AND tax_total >= 0 "
                + "AND cod_fee >= 0 AND wallet_applied >= 0 AND grand_total >= 0 AND amount_payable >= 0");
        });

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();

        builder.Property(order => order.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(order => order.PaymentStatus).HasConversion<string>().HasMaxLength(24);
        builder.Property(order => order.PaymentMethod).HasConversion<string>().HasMaxLength(16);
        builder.Property(order => order.Channel).HasMaxLength(16).IsRequired();
        builder.Property(order => order.CouponCode).HasMaxLength(48);
        builder.Property(order => order.CancellationReason).HasMaxLength(500);
        builder.Property(order => order.Notes).HasMaxLength(4000);

        builder.Property(order => order.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        foreach (var money in new[]
                 {
                     nameof(Order.ItemsTotal),
                     nameof(Order.DiscountTotal),
                     nameof(Order.ShippingTotal),
                     nameof(Order.TaxTotal),
                     nameof(Order.CodFee),
                     nameof(Order.WalletApplied),
                     nameof(Order.RoundingAdjustment),
                     nameof(Order.GrandTotal),
                     nameof(Order.AmountPayable),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        // Snapshots, not references, and therefore documents rather than owned entities: they are
        // read whole, never joined to and never filtered on, and freezing them is the whole point.
        builder.Property(order => order.CustomerSnapshot)
            .HasColumnType(OrdersJson.ColumnType)
            .HasConversion(
                snapshot => JsonSerializer.Serialize(snapshot, OrdersJson.Options),
                json => JsonSerializer.Deserialize<OrderCustomerSnapshot>(json, OrdersJson.Options)!,
                SnapshotComparer<OrderCustomerSnapshot>());

        builder.Property(order => order.ShippingAddress)
            .HasColumnType(OrdersJson.ColumnType)
            .HasConversion(
                address => JsonSerializer.Serialize(address, OrdersJson.Options),
                json => JsonSerializer.Deserialize<OrderAddressSnapshot>(json, OrdersJson.Options)!,
                SnapshotComparer<OrderAddressSnapshot>());

        builder.Property(order => order.BillingAddress)
            .HasColumnType(OrdersJson.ColumnType)
            .HasConversion(
                address => JsonSerializer.Serialize(address, OrdersJson.Options),
                json => JsonSerializer.Deserialize<OrderAddressSnapshot>(json, OrdersJson.Options)!,
                SnapshotComparer<OrderAddressSnapshot>());

        builder.HasMany(order => order.SubOrders)
            .WithOne()
            .HasForeignKey(subOrder => subOrder.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(order => order.Events)
            .WithOne()
            .HasForeignKey(entry => entry.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(order => order.SubOrders).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(order => order.Events).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The number a shopper quotes to support. Unique per tenant, because two orders answering to
        // one number is the fastest way to refund the wrong person.
        builder.HasIndex(order => new { order.TenantId, order.OrderNumber }).IsUnique();

        // "My orders", newest first — the single most-served authenticated query on the storefront.
        builder.HasIndex(order => new { order.TenantId, order.CustomerId, order.PlacedAt });

        // The operations worklist, and the finance report.
        builder.HasIndex(order => new { order.TenantId, order.Status, order.PlacedAt });

        // The place-order path re-reads by cart to answer a replayed request, and the reservation
        // settlement finds the order the same way.
        builder.HasIndex(order => new { order.TenantId, order.CartId });

        builder.Ignore(order => order.DomainEvents);
        builder.Ignore(order => order.NetTotal);
        builder.Ignore(order => order.CancelledTotal);
    }

    /// <summary>
    /// A snapshot is replaced wholesale rather than edited, so reference equality is the honest
    /// comparison and the deep copy is simply the same instance.
    /// </summary>
    private static ValueComparer<TSnapshot> SnapshotComparer<TSnapshot>()
        where TSnapshot : class
        => new(
            (left, right) => ReferenceEquals(left, right),
            snapshot => snapshot == null ? 0 : snapshot.GetHashCode(),
            snapshot => snapshot);
}

/// <summary>Maps <see cref="SubOrder"/> to <c>orders.sub_orders</c>.</summary>
internal sealed class SubOrderConfiguration : IEntityTypeConfiguration<SubOrder>
{
    public void Configure(EntityTypeBuilder<SubOrder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sub_orders", table =>
        {
            table.HasCheckConstraint("ck_sub_orders_status", OrdersCheckConstraints.SubOrderStatuses);
            table.HasCheckConstraint("ck_sub_orders_cancelled_by", OrdersCheckConstraints.CancellationInitiators);
            table.HasCheckConstraint(
                "ck_sub_orders_totals",
                "items_total >= 0 AND discount_total >= 0 AND shipping_total >= 0 AND tax_total >= 0 "
                + "AND taxable_value >= 0 AND total >= 0");
            table.HasCheckConstraint(
                "ck_sub_orders_promise",
                "promised_min_days >= 0 AND promised_max_days >= promised_min_days AND dispatch_sla_hours >= 0");

            // A sub-order without a seller could not be invoiced, dispatched or settled by anybody.
            // The property is nullable only because IVendorScoped allows platform-owned rows.
            table.HasCheckConstraint("ck_sub_orders_vendor", "vendor_id IS NOT NULL");
        });

        builder.HasKey(subOrder => subOrder.Id);
        builder.Property(subOrder => subOrder.Id).ValueGeneratedNever();

        builder.Property(subOrder => subOrder.SubOrderNumber).HasMaxLength(40).IsRequired();
        builder.Property(subOrder => subOrder.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(subOrder => subOrder.CancelledBy).HasConversion<string>().HasMaxLength(16);
        builder.Property(subOrder => subOrder.CancellationReason).HasMaxLength(500);
        builder.Property(subOrder => subOrder.VendorCode).HasMaxLength(32);
        builder.Property(subOrder => subOrder.VendorName).HasMaxLength(200);
        builder.Property(subOrder => subOrder.VendorGstin).HasMaxLength(15);
        builder.Property(subOrder => subOrder.ShippingOptionCode).HasMaxLength(48);
        builder.Property(subOrder => subOrder.Carrier).HasMaxLength(80);

        builder.Property(subOrder => subOrder.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        foreach (var money in new[]
                 {
                     nameof(SubOrder.ItemsTotal),
                     nameof(SubOrder.DiscountTotal),
                     nameof(SubOrder.ShippingTotal),
                     nameof(SubOrder.ShippingTax),
                     nameof(SubOrder.TaxTotal),
                     nameof(SubOrder.TaxableValue),
                     nameof(SubOrder.Total),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        builder.HasMany(subOrder => subOrder.Lines)
            .WithOne()
            .HasForeignKey(line => line.SubOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(subOrder => subOrder.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The number on the seller's worklist and on the parcel.
        builder.HasIndex(subOrder => new { subOrder.TenantId, subOrder.SubOrderNumber }).IsUnique();

        // One sub-order per seller per order. Two would split one seller's parcel in half and
        // invoice it twice.
        builder.HasIndex(subOrder => new { subOrder.TenantId, subOrder.OrderId, subOrder.VendorId }).IsUnique();

        // The vendor dashboard: this seller's work, by state, newest first.
        builder.HasIndex(subOrder => new { subOrder.TenantId, subOrder.VendorId, subOrder.Status, subOrder.CreatedAt });

        // The operations queue, and the dispatch-SLA breach report.
        builder.HasIndex(subOrder => new { subOrder.TenantId, subOrder.Status, subOrder.DispatchDueAt });

        // The completion sweeper's query: delivered sub-orders whose return window has closed.
        builder.HasIndex(subOrder => new { subOrder.TenantId, subOrder.Status, subOrder.ReturnWindowEndsAt });

        builder.Ignore(subOrder => subOrder.DomainEvents);
        builder.Ignore(subOrder => subOrder.CancelledTotal);
        builder.Ignore(subOrder => subOrder.NetTotal);
        builder.Ignore(subOrder => subOrder.IsFullyCancelled);
    }
}

/// <summary>Maps <see cref="OrderLine"/> to <c>orders.order_lines</c>.</summary>
internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("order_lines", table =>
        {
            table.HasCheckConstraint("ck_order_lines_status", OrdersCheckConstraints.LineStatuses);
            table.HasCheckConstraint("ck_order_lines_quantities", OrdersCheckConstraints.LineQuantities);
            table.HasCheckConstraint(
                "ck_order_lines_money",
                "unit_mrp >= 0 AND unit_price >= 0 AND discount_amount >= 0 AND taxable_value >= 0 "
                + "AND line_total >= 0 AND commission_amount >= 0");
            table.HasCheckConstraint("ck_order_lines_vendor", "vendor_id IS NOT NULL");
        });

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.Sku).HasMaxLength(64).IsRequired();
        builder.Property(line => line.Status).HasConversion<string>().HasMaxLength(24);

        foreach (var money in new[]
                 {
                     nameof(OrderLine.UnitMrp),
                     nameof(OrderLine.UnitPrice),
                     nameof(OrderLine.DiscountAmount),
                     nameof(OrderLine.TaxableValue),
                     nameof(OrderLine.Cgst),
                     nameof(OrderLine.Sgst),
                     nameof(OrderLine.Igst),
                     nameof(OrderLine.Cess),
                     nameof(OrderLine.LineTotal),
                     nameof(OrderLine.CommissionAmount),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        // Rates rather than amounts: a percentage with four decimals, which is what a GST rate and a
        // commission rate both are.
        builder.Property(line => line.GstRate).HasColumnType("numeric(9,4)");
        builder.Property(line => line.CommissionRate).HasColumnType("numeric(9,4)");

        builder.Property(line => line.Snapshot)
            .HasColumnType(OrdersJson.ColumnType)
            .HasConversion(
                snapshot => JsonSerializer.Serialize(snapshot, OrdersJson.Options),
                json => JsonSerializer.Deserialize<ProductSnapshot>(json, OrdersJson.Options)!,
                new ValueComparer<ProductSnapshot>(
                    (left, right) => ReferenceEquals(left, right),
                    snapshot => snapshot == null ? 0 : snapshot.GetHashCode(),
                    snapshot => snapshot));

        builder.HasIndex(line => line.SubOrderId);

        // "How much of this offer have we sold" — the product sales report, and the question Returns
        // asks when it needs the line a returned unit came from.
        builder.HasIndex(line => new { line.TenantId, line.ListingId });

        builder.Ignore(line => line.DomainEvents);
        builder.Ignore(line => line.QuantityLive);
        builder.Ignore(line => line.IsFullyCancelled);
        builder.Ignore(line => line.CancelledValue);
    }
}

/// <summary>Maps <see cref="OrderEvent"/> to <c>orders.order_events</c>.</summary>
internal sealed class OrderEventConfiguration : IEntityTypeConfiguration<OrderEvent>
{
    public void Configure(EntityTypeBuilder<OrderEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("order_events", table =>
            table.HasCheckConstraint("ck_order_events_type", OrdersCheckConstraints.EventTypes));

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.Type).HasMaxLength(24).IsRequired();
        builder.Property(entry => entry.FromStatus).HasMaxLength(24);
        builder.Property(entry => entry.ToStatus).HasMaxLength(24);
        builder.Property(entry => entry.ActorType).HasConversion<string>().HasMaxLength(16);
        builder.Property(entry => entry.Message).HasMaxLength(1000);
        builder.Property(entry => entry.Payload).HasColumnType(OrdersJson.ColumnType);

        // The timeline, in the order it happened. Both the customer's view and the operator's read
        // it this way, and the visibility flag is a filter on the same index rather than a second one.
        builder.HasIndex(entry => new { entry.TenantId, entry.OrderId, entry.OccurredAt });
        builder.HasIndex(entry => new { entry.TenantId, entry.SubOrderId, entry.OccurredAt });

        builder.Ignore(entry => entry.DomainEvents);
    }
}

/// <summary>Maps <see cref="Invoice"/> to <c>orders.invoices</c>.</summary>
internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("invoices", table =>
        {
            table.HasCheckConstraint("ck_invoices_status", OrdersCheckConstraints.InvoiceStatuses);
            table.HasCheckConstraint(
                "ck_invoices_amounts",
                "taxable_value >= 0 AND cgst >= 0 AND sgst >= 0 AND igst >= 0 AND cess >= 0 AND total >= 0");

            // CGST and SGST are the intra-state pair and IGST is the inter-state one; a row carrying
            // both is a tax return that will not add up.
            table.HasCheckConstraint(
                "ck_invoices_tax_split",
                "(igst = 0) OR (cgst = 0 AND sgst = 0)");
            table.HasCheckConstraint("ck_invoices_vendor", "vendor_id IS NOT NULL");
        });

        builder.HasKey(invoice => invoice.Id);
        builder.Property(invoice => invoice.Id).ValueGeneratedNever();

        builder.Property(invoice => invoice.InvoiceNumber).HasMaxLength(48).IsRequired();
        builder.Property(invoice => invoice.Series).HasMaxLength(32).IsRequired();
        builder.Property(invoice => invoice.FinancialYear).HasMaxLength(9).IsRequired();
        builder.Property(invoice => invoice.PlaceOfSupplyStateCode).HasMaxLength(2);
        builder.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(invoice => invoice.Irn).HasMaxLength(64);
        builder.Property(invoice => invoice.QrPayload).HasMaxLength(4000);

        builder.Property(invoice => invoice.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        foreach (var money in new[]
                 {
                     nameof(Invoice.TaxableValue),
                     nameof(Invoice.Cgst),
                     nameof(Invoice.Sgst),
                     nameof(Invoice.Igst),
                     nameof(Invoice.Cess),
                     nameof(Invoice.Total),
                 })
        {
            builder.Property(money).HasColumnType(ModelConventions.MoneyColumnType);
        }

        // A sub-order is invoiced once. The second invoice for one supply is the thing the whole
        // gapless-numbering apparatus exists to prevent, and this index is what actually prevents it.
        builder.HasIndex(invoice => new { invoice.TenantId, invoice.SubOrderId }).IsUnique();

        // Gapless per seller per financial year (docs/02-domain-model.md §7.2). The uniqueness is
        // the enforceable half; the gaplessness is the counter row's.
        builder
            .HasIndex(invoice => new { invoice.TenantId, invoice.VendorId, invoice.FinancialYear, invoice.InvoiceNumber })
            .IsUnique();

        // The GST return: everything a seller supplied in a period.
        builder.HasIndex(invoice => new { invoice.TenantId, invoice.VendorId, invoice.IssuedAt });

        builder.HasIndex(invoice => new { invoice.TenantId, invoice.OrderId });

        builder.Ignore(invoice => invoice.DomainEvents);
    }
}

/// <summary>Maps <see cref="NumberSequence"/> to <c>orders.number_sequences</c>.</summary>
internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("number_sequences", table =>
        {
            table.HasCheckConstraint("ck_number_sequences_kind", OrdersCheckConstraints.SequenceKinds);
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
