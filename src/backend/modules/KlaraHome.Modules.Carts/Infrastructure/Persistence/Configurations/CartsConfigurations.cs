using System.Text.Json;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Carts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Carts.Infrastructure.Persistence.Configurations;

/// <summary>How the open-shaped columns in this schema are written.</summary>
internal static class CartsJson
{
    /// <summary>
    /// camelCase, matching the API payloads these documents are handed to and received from. A
    /// <c>jsonb</c> column that is read by a support screen is part of the API surface, and a
    /// casing convention applied on one side and not the other is a class of bug worth designing
    /// out.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <summary>The Postgres type every open-shaped column in this schema uses.</summary>
    public const string ColumnType = "jsonb";
}

/// <summary>The <c>CHECK</c> lists, written once so a column and its constraint cannot drift apart.</summary>
internal static class CartsCheckConstraints
{
    /// <summary>The values <c>carts.status</c> accepts.</summary>
    public const string CartStatuses = "status IN ('Active', 'Converted', 'Abandoned', 'Expired')";

    /// <summary>The values <c>checkout_sessions.status</c> accepts.</summary>
    public const string CheckoutStatuses =
        "status IN ('Draft', 'AddressSet', 'ShippingSet', 'PaymentSet', 'Placing', 'Placed', "
        + "'Abandoned', 'Expired')";

    /// <summary>The values <c>checkout_sessions.payment_method</c> accepts.</summary>
    public const string PaymentMethods = "payment_method IN ('Prepaid', 'CashOnDelivery')";

    /// <summary>The values <c>checkout_placements.status</c> accepts.</summary>
    public const string PlacementStatuses = "status IN ('InProgress', 'Succeeded', 'Failed')";

    /// <summary>
    /// A basket belongs to a shopper or to a browser, and never to neither. Without this a row
    /// nobody can reach is insertable, and the only symptom is a cart that quietly disappears.
    /// </summary>
    public const string CartHasOwner = "customer_id IS NOT NULL OR anonymous_token_hash IS NOT NULL";
}

/// <summary>Maps <see cref="Cart"/> to <c>carts.carts</c>.</summary>
internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("carts", table =>
        {
            table.HasCheckConstraint("ck_carts_status", CartsCheckConstraints.CartStatuses);
            table.HasCheckConstraint("ck_carts_owner", CartsCheckConstraints.CartHasOwner);
            table.HasCheckConstraint("ck_carts_line_count", "line_count >= 0");
            table.HasCheckConstraint("ck_carts_reminder_count", "reminder_count >= 0");
        });

        builder.HasKey(cart => cart.Id);
        builder.Property(cart => cart.Id).ValueGeneratedNever();

        builder.Property(cart => cart.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(cart => cart.AnonymousTokenHash).HasMaxLength(64);
        builder.Property(cart => cart.CouponCode).HasMaxLength(48);

        builder.Property(cart => cart.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(KlaraHome.SharedKernel.Primitives.Money.Inr);

        builder.HasMany(cart => cart.Lines)
            .WithOne()
            .HasForeignKey(line => line.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(cart => cart.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // One live basket per shopper. The filter is what makes it "one *live* basket": a converted
        // cart stays in the table for conversion analysis, and without the filter the shopper's
        // second order could never be started.
        builder
            .HasIndex(cart => new { cart.TenantId, cart.CustomerId })
            .IsUnique()
            .HasFilter("customer_id IS NOT NULL AND status = 'Active'");

        // The cookie lookup. Unique because the token identifies exactly one basket, and a
        // duplicate would mean two browsers sharing one.
        builder
            .HasIndex(cart => new { cart.TenantId, cart.AnonymousTokenHash })
            .IsUnique()
            .HasFilter("anonymous_token_hash IS NOT NULL");

        // The sweeper's query: the stale live baskets, oldest first.
        builder.HasIndex(cart => new { cart.TenantId, cart.Status, cart.LastActivityAt });

        // The marketing worklist, which reads abandoned baskets by when they were given up on.
        builder.HasIndex(cart => new { cart.TenantId, cart.Status, cart.AbandonedAt });

        builder.Ignore(cart => cart.DomainEvents);
    }
}

/// <summary>Maps <see cref="CartLine"/> to <c>carts.cart_lines</c>.</summary>
internal sealed class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
    public void Configure(EntityTypeBuilder<CartLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("cart_lines", table =>
        {
            table.HasCheckConstraint("ck_cart_lines_quantity", "quantity >= 1");
            table.HasCheckConstraint("ck_cart_lines_unit_price", "unit_price_at_add >= 0");
        });

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.UnitPriceAtAdd).HasColumnType(ModelConventions.MoneyColumnType);

        // One line per offer. Adding something that is already in the basket increases the line
        // that is there; two rows for one offer would give the shopper two prices for one thing.
        builder.HasIndex(line => new { line.TenantId, line.CartId, line.ListingId }).IsUnique();

        // Grouping by seller is what a multi-vendor cart renders and what the sub-order split
        // keys on, so the seller leads its own index.
        builder.HasIndex(line => new { line.TenantId, line.VendorId });

        builder.Ignore(line => line.DomainEvents);
    }
}

/// <summary>Maps <see cref="CheckoutSession"/> to <c>carts.checkout_sessions</c>.</summary>
internal sealed class CheckoutSessionConfiguration : IEntityTypeConfiguration<CheckoutSession>
{
    public void Configure(EntityTypeBuilder<CheckoutSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("checkout_sessions", table =>
        {
            table.HasCheckConstraint("ck_checkout_sessions_status", CartsCheckConstraints.CheckoutStatuses);
            table.HasCheckConstraint("ck_checkout_sessions_payment", CartsCheckConstraints.PaymentMethods);
            table.HasCheckConstraint("ck_checkout_sessions_totals", "shipping_total >= 0 AND grand_total >= 0");
        });

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();

        builder.Property(session => session.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(session => session.PaymentMethod).HasConversion<string>().HasMaxLength(16);
        builder.Property(session => session.Gstin).HasMaxLength(15);
        builder.Property(session => session.OrderNumber).HasMaxLength(32);

        builder.Property(session => session.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(KlaraHome.SharedKernel.Primitives.Money.Inr);

        builder.Property(session => session.ShippingTotal).HasColumnType(ModelConventions.MoneyColumnType);
        builder.Property(session => session.GrandTotal).HasColumnType(ModelConventions.MoneyColumnType);

        // Snapshots, not references, and therefore documents rather than owned entities: they are
        // read whole, never joined to and never filtered on, and freezing them is the whole point.
        builder.Property(session => session.ShippingAddress)
            .HasColumnType(CartsJson.ColumnType)
            .HasConversion(
                address => JsonSerializer.Serialize(address, CartsJson.Options),
                json => JsonSerializer.Deserialize<AddressSnapshot>(json, CartsJson.Options),
                AddressComparer);

        builder.Property(session => session.BillingAddress)
            .HasColumnType(CartsJson.ColumnType)
            .HasConversion(
                address => JsonSerializer.Serialize(address, CartsJson.Options),
                json => JsonSerializer.Deserialize<AddressSnapshot>(json, CartsJson.Options),
                AddressComparer);

        // The agreed price, verbatim. A converter rather than EF's owned-JSON mapping because the
        // quote is a contract type belonging to another module: it must be free to gain a field
        // without this schema needing a migration, and it must never become an entity graph this
        // module can be tempted to query into.
        builder.Property(session => session.QuoteSnapshot)
            .HasColumnType(CartsJson.ColumnType)
            .HasConversion(
                quote => JsonSerializer.Serialize(quote, CartsJson.Options),
                json => JsonSerializer.Deserialize<QuoteResult>(json, CartsJson.Options),
                QuoteComparer);

        builder.HasMany(session => session.Shipments)
            .WithOne()
            .HasForeignKey(shipment => shipment.CheckoutSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(session => session.Placements)
            .WithOne()
            .HasForeignKey(placement => placement.CheckoutSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(session => session.Shipments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(session => session.Placements).UsePropertyAccessMode(PropertyAccessMode.Field);

        // One open attempt per basket. A shopper who opens checkout twice in two tabs must not end
        // up with two sessions holding two different addresses for one basket.
        builder
            .HasIndex(session => new { session.TenantId, session.CartId })
            .IsUnique()
            .HasFilter("status IN ('Draft', 'AddressSet', 'ShippingSet', 'PaymentSet', 'Placing')");

        // "What is this shopper in the middle of" — the support question, and the sweeper's.
        builder.HasIndex(session => new { session.TenantId, session.CustomerId, session.Status });
        builder.HasIndex(session => new { session.TenantId, session.Status, session.ExpiresAt });

        builder.Ignore(session => session.DomainEvents);
    }

    /// <summary>
    /// A snapshot is replaced wholesale rather than edited, so reference equality is the honest
    /// comparison and the deep copy is simply the same immutable-in-practice instance.
    /// </summary>
    private static ValueComparer<AddressSnapshot?> AddressComparer { get; } = new(
        (left, right) => ReferenceEquals(left, right),
        address => address == null ? 0 : address.GetHashCode(),
        address => address);

    /// <summary>The same, for the quote.</summary>
    private static ValueComparer<QuoteResult?> QuoteComparer { get; } = new(
        (left, right) => ReferenceEquals(left, right),
        quote => quote == null ? 0 : quote.GetHashCode(),
        quote => quote);
}

/// <summary>Maps <see cref="CheckoutShipment"/> to <c>carts.checkout_shipments</c>.</summary>
internal sealed class CheckoutShipmentConfiguration : IEntityTypeConfiguration<CheckoutShipment>
{
    public void Configure(EntityTypeBuilder<CheckoutShipment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("checkout_shipments", table =>
        {
            table.HasCheckConstraint("ck_checkout_shipments_amount", "amount >= 0 AND tax_amount >= 0");
            table.HasCheckConstraint(
                "ck_checkout_shipments_promise",
                "promised_min_days >= 0 AND promised_max_days >= promised_min_days");
        });

        builder.HasKey(shipment => shipment.Id);
        builder.Property(shipment => shipment.Id).ValueGeneratedNever();

        builder.Property(shipment => shipment.OptionCode).HasMaxLength(48);
        builder.Property(shipment => shipment.ServiceName).HasMaxLength(120);
        builder.Property(shipment => shipment.Carrier).HasMaxLength(80);

        builder.Property(shipment => shipment.Amount).HasColumnType(ModelConventions.MoneyColumnType);
        builder.Property(shipment => shipment.TaxAmount).HasColumnType(ModelConventions.MoneyColumnType);

        // One choice per seller. Two rows would mean two delivery charges for one parcel.
        builder
            .HasIndex(shipment => new { shipment.TenantId, shipment.CheckoutSessionId, shipment.VendorId })
            .IsUnique();

        builder.Ignore(shipment => shipment.DomainEvents);
    }
}

/// <summary>Maps <see cref="CheckoutPlacement"/> to <c>carts.checkout_placements</c>.</summary>
internal sealed class CheckoutPlacementConfiguration : IEntityTypeConfiguration<CheckoutPlacement>
{
    public void Configure(EntityTypeBuilder<CheckoutPlacement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("checkout_placements", table =>
            table.HasCheckConstraint("ck_checkout_placements_status", CartsCheckConstraints.PlacementStatuses));

        builder.HasKey(placement => placement.Id);
        builder.Property(placement => placement.Id).ValueGeneratedNever();

        builder.Property(placement => placement.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(placement => placement.IdempotencyKey).HasMaxLength(128);
        builder.Property(placement => placement.RequestHash).HasMaxLength(64);
        builder.Property(placement => placement.OrderNumber).HasMaxLength(32);
        builder.Property(placement => placement.FailureCode).HasMaxLength(64);
        builder.Property(placement => placement.Response).HasColumnType(CartsJson.ColumnType);

        // The whole of the idempotency guarantee. Two concurrent requests carrying one key race for
        // this index; one inserts and creates the order, the other loses and replays the winner's
        // response. Everything else in the place-order path is arranged around that fact.
        builder.HasIndex(placement => new { placement.TenantId, placement.IdempotencyKey }).IsUnique();

        builder.Ignore(placement => placement.DomainEvents);
    }
}
