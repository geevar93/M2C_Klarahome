using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Reporting.Domain;

/// <summary>
/// One order, as reporting keeps it.
/// </summary>
/// <remarks>
/// <para>
/// Written when the order is placed and updated only by the two facts that change what it was worth
/// — completion and cancellation. It is the grain the order-level measures are computed at: how many
/// orders there were, what the average was, and how the split between cash and prepaid moved.
/// </para>
/// <para>
/// It is not append-only, and it is the only fact table here that is not. An order is a thing with a
/// life, and a reporting row that could not follow it would mean every question about outcomes —
/// what fraction of orders complete, how many are cancelled — needing a second table to answer.
/// </para>
/// </remarks>
internal sealed class OrderFact : AggregateRoot<Guid>, ITenantScoped
{
    private OrderFact(Guid id, Guid orderId)
        : base(id)
        => OrderId = orderId;

    /// <summary>Required by EF Core's materialiser.</summary>
    private OrderFact() => OrderNumber = string.Empty;

    /// <summary>The order. Unique per tenant.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Its human-readable number, so a figure can be traced to a sale.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>Who bought.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>How they paid, as the Payments module named it.</summary>
    public string PaymentMethod { get; private set; } = string.Empty;

    /// <summary>Whether it is cash on delivery, which is the split this market is run on.</summary>
    public bool IsCod { get; private set; }

    /// <summary>What the shopper agreed to pay, inclusive of tax and delivery.</summary>
    public decimal GrandTotal { get; private set; }

    /// <summary>What was left to collect after store credit and prepaid discounts.</summary>
    public decimal AmountPayable { get; private set; }

    /// <summary>ISO 4217 code both amounts are in.</summary>
    public string CurrencyCode { get; private set; } = "INR";

    /// <summary>How many sellers were in the basket.</summary>
    public int VendorCount { get; private set; }

    /// <summary>Where the order stands.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>When it was placed, in UTC. Every date filter in this module is against this.</summary>
    public DateTimeOffset PlacedAt { get; private set; }

    /// <summary>The date part, in the store's reporting day. Grouped on rather than computed.</summary>
    /// <remarks>
    /// Stored rather than derived because grouping on <c>date(placed_at)</c> cannot use an index on
    /// the timestamp, and "sales by day" is the most-run query in the module.
    /// </remarks>
    public DateOnly PlacedOn { get; private set; }

    /// <summary>When it completed, in UTC. Null until it does.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Records a placed order.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="orderNumber">Its number.</param>
    /// <param name="customerId">Who bought.</param>
    /// <param name="paymentMethod">How they paid.</param>
    /// <param name="grandTotal">What they agreed to pay.</param>
    /// <param name="amountPayable">What was left to collect.</param>
    /// <param name="currencyCode">What both amounts are in.</param>
    /// <param name="vendorCount">How many sellers were in the basket.</param>
    /// <param name="status">Where it stood when placed.</param>
    /// <param name="placedAt">When.</param>
    /// <param name="reportingDay">The date it falls on in the store's reporting timezone.</param>
    public static OrderFact Record(
        Guid orderId,
        string orderNumber,
        Guid customerId,
        string paymentMethod,
        decimal grandTotal,
        decimal amountPayable,
        string currencyCode,
        int vendorCount,
        string status,
        DateTimeOffset placedAt,
        DateOnly reportingDay)
        => new(UuidV7.NewAt(placedAt), orderId)
        {
            OrderNumber = orderNumber,
            CustomerId = customerId,
            PaymentMethod = paymentMethod,
            IsCod = string.Equals(paymentMethod, "Cod", StringComparison.OrdinalIgnoreCase),
            GrandTotal = grandTotal,
            AmountPayable = amountPayable,
            CurrencyCode = currencyCode,
            VendorCount = vendorCount,
            Status = status,
            PlacedAt = placedAt,
            PlacedOn = reportingDay,
        };

    /// <summary>Follows the order's status.</summary>
    /// <param name="status">Where it stands now.</param>
    /// <param name="completedAt">When it completed, if it has.</param>
    public void Advance(string status, DateTimeOffset? completedAt = null)
    {
        Status = status;
        CompletedAt ??= completedAt;
    }
}

/// <summary>
/// One line of one order, as reporting keeps it.
/// </summary>
/// <remarks>
/// <para>
/// The workhorse of the module: sales by day, by category and by seller, the top and slow SKUs, and
/// gross merchandise value are all aggregations over this one table. Everything a report groups by
/// is a column on the row — the category, the brand, the seller, the payment method — written once
/// when the line is confirmed rather than joined at read time, because a join is exactly what this
/// module may not do.
/// </para>
/// <para>
/// Recorded on <em>confirmation</em> rather than placement. A placed order that is never paid for is
/// not a sale, and counting it would overstate every revenue figure by the abandonment rate. The
/// order-level row exists from placement precisely so the funnel can still see the difference.
/// </para>
/// <para>
/// The cancelled and returned columns are subtracted rather than deleted. Net revenue is gross less
/// what came back, and a store that removed the row would lose the ability to say how much of its
/// gross it gives back — which for a fashion catalogue is the number that decides whether the
/// business works.
/// </para>
/// </remarks>
internal sealed class SaleLineFact : AggregateRoot<Guid>, ITenantScoped, IVendorScoped
{
    private SaleLineFact(Guid id, Guid orderLineId)
        : base(id)
        => OrderLineId = orderLineId;

    /// <summary>Required by EF Core's materialiser.</summary>
    private SaleLineFact()
    {
    }

    /// <summary>The line. Unique per tenant, and what makes redelivery harmless.</summary>
    public Guid OrderLineId { get; private set; }

    /// <summary>The order it belongs to.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Its number, so a figure can be traced without a join.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>The seller's part of the order.</summary>
    public Guid SubOrderId { get; private set; }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>Who bought.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The offer sold.</summary>
    public Guid ListingId { get; private set; }

    /// <summary>The sellable thing sold, where the catalogue could still name it.</summary>
    public Guid? VariantId { get; private set; }

    /// <summary>Its product.</summary>
    public Guid? ProductId { get; private set; }

    /// <summary>Its category, denormalised so "sales by category" needs no join.</summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>The category's name, for the report a person reads.</summary>
    public string? CategoryName { get; private set; }

    /// <summary>Its brand.</summary>
    public Guid? BrandId { get; private set; }

    /// <summary>The brand's name.</summary>
    public string? BrandName { get; private set; }

    /// <summary>The stock-keeping unit, frozen at placement.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>What the line was called on the order.</summary>
    public string ProductName { get; private set; } = string.Empty;

    /// <summary>How many units.</summary>
    public int Quantity { get; private set; }

    /// <summary>What the shopper paid for them, inclusive of tax.</summary>
    public decimal LineTotal { get; private set; }

    /// <summary>How they paid.</summary>
    public string PaymentMethod { get; private set; } = string.Empty;

    /// <summary>Whether it is cash on delivery.</summary>
    public bool IsCod { get; private set; }

    /// <summary>How many units were cancelled.</summary>
    public int CancelledQuantity { get; private set; }

    /// <summary>What those units were worth.</summary>
    public decimal CancelledAmount { get; private set; }

    /// <summary>How many units came back.</summary>
    public int ReturnedQuantity { get; private set; }

    /// <summary>What those units were worth.</summary>
    public decimal ReturnedAmount { get; private set; }

    /// <summary>What the platform charged the seller, once Settlements has said.</summary>
    public decimal CommissionAmount { get; private set; }

    /// <summary>When the sale was confirmed, in UTC.</summary>
    public DateTimeOffset ConfirmedAt { get; private set; }

    /// <summary>The reporting day it falls on.</summary>
    public DateOnly ConfirmedOn { get; private set; }

    /// <summary>When the seller's parcel was delivered, in UTC. Null until it is.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>What the line is worth after everything that came back.</summary>
    public decimal NetAmount => LineTotal - CancelledAmount - ReturnedAmount;

    /// <summary>How many units the customer kept.</summary>
    public int NetQuantity => Math.Max(0, Quantity - CancelledQuantity - ReturnedQuantity);

    /// <summary>Records a confirmed line.</summary>
    /// <param name="line">Everything about it, assembled by the handler that saw the event.</param>
    public static SaleLineFact Record(SaleLineFactValues line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new SaleLineFact(UuidV7.NewAt(line.ConfirmedAt), line.OrderLineId)
        {
            OrderId = line.OrderId,
            OrderNumber = line.OrderNumber,
            SubOrderId = line.SubOrderId,
            VendorId = line.VendorId,
            CustomerId = line.CustomerId,
            ListingId = line.ListingId,
            VariantId = line.VariantId,
            ProductId = line.ProductId,
            CategoryId = line.CategoryId,
            CategoryName = line.CategoryName,
            BrandId = line.BrandId,
            BrandName = line.BrandName,
            Sku = line.Sku,
            ProductName = line.ProductName,
            Quantity = line.Quantity,
            LineTotal = line.LineTotal,
            PaymentMethod = line.PaymentMethod,
            IsCod = string.Equals(line.PaymentMethod, "Cod", StringComparison.OrdinalIgnoreCase),
            ConfirmedAt = line.ConfirmedAt,
            ConfirmedOn = line.ConfirmedOn,
        };
    }

    /// <summary>Records units the customer never received.</summary>
    /// <param name="quantity">How many.</param>
    /// <param name="amount">What they were worth.</param>
    public void RecordCancellation(int quantity, decimal amount)
    {
        CancelledQuantity = Math.Min(Quantity, CancelledQuantity + Math.Max(0, quantity));
        CancelledAmount = Math.Min(LineTotal, CancelledAmount + Math.Max(0m, amount));
    }

    /// <summary>Records units that came back.</summary>
    /// <param name="quantity">How many.</param>
    /// <param name="amount">What they were worth.</param>
    public void RecordReturn(int quantity, decimal amount)
    {
        ReturnedQuantity = Math.Min(Quantity, ReturnedQuantity + Math.Max(0, quantity));
        ReturnedAmount = Math.Min(LineTotal, ReturnedAmount + Math.Max(0m, amount));
    }

    /// <summary>Records what the platform charged for the sale.</summary>
    /// <param name="commission">The commission, as Settlements computed it.</param>
    public void RecordCommission(decimal commission) => CommissionAmount = Math.Max(0m, commission);

    /// <summary>Records when the parcel arrived.</summary>
    /// <param name="deliveredAt">When.</param>
    public void RecordDelivery(DateTimeOffset deliveredAt) => DeliveredAt ??= deliveredAt;
}

/// <summary>
/// Everything a confirmed line's fact row needs, assembled by the handler that saw the event.
/// </summary>
/// <remarks>
/// A parameter object rather than fourteen positional arguments, because the alternative is a
/// factory call nobody can read and two adjacent <c>Guid</c>s that will eventually be swapped.
/// </remarks>
/// <param name="OrderLineId">The line.</param>
/// <param name="OrderId">Its order.</param>
/// <param name="OrderNumber">The order's number.</param>
/// <param name="SubOrderId">The seller's part of it.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CustomerId">Who bought.</param>
/// <param name="ListingId">The offer sold.</param>
/// <param name="VariantId">The sellable thing, where the catalogue could name it.</param>
/// <param name="ProductId">Its product.</param>
/// <param name="CategoryId">Its category.</param>
/// <param name="CategoryName">The category's name.</param>
/// <param name="BrandId">Its brand.</param>
/// <param name="BrandName">The brand's name.</param>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="ProductName">What it was called on the order.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="LineTotal">What they cost.</param>
/// <param name="PaymentMethod">How the order was paid for.</param>
/// <param name="ConfirmedAt">When the sale was confirmed.</param>
/// <param name="ConfirmedOn">The reporting day that falls on.</param>
internal sealed record SaleLineFactValues(
    Guid OrderLineId,
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    Guid VendorId,
    Guid CustomerId,
    Guid ListingId,
    Guid? VariantId,
    Guid? ProductId,
    Guid? CategoryId,
    string? CategoryName,
    Guid? BrandId,
    string? BrandName,
    string Sku,
    string ProductName,
    int Quantity,
    decimal LineTotal,
    string PaymentMethod,
    DateTimeOffset ConfirmedAt,
    DateOnly ConfirmedOn);

/// <summary>What kind of money movement a payment fact records.</summary>
internal enum PaymentFactKind
{
    /// <summary>Money taken by the gateway.</summary>
    Captured = 0,

    /// <summary>Money given back.</summary>
    Refunded = 1,

    /// <summary>Cash taken at the door.</summary>
    CodCollected = 2,

    /// <summary>An attempt that did not work. Counted for the funnel, never for revenue.</summary>
    Failed = 3,
}

/// <summary>
/// One movement of money, as reporting keeps it.
/// </summary>
/// <remarks>
/// Append-only and keyed on the event that produced it, which is what makes redelivery harmless: a
/// second copy of the same capture fails the unique index rather than doubling the day's takings.
/// Failures are kept alongside successes because the ratio between them is the only measure of
/// whether a payment gateway is working that a business can read.
/// </remarks>
internal sealed class PaymentFact : AggregateRoot<Guid>, ITenantScoped, IAppendOnly
{
    private PaymentFact(Guid id, Guid eventId, PaymentFactKind kind)
        : base(id)
    {
        SourceEventId = eventId;
        Kind = kind;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private PaymentFact()
    {
    }

    /// <summary>The integration event that produced this row. Unique, and the idempotency key.</summary>
    public Guid SourceEventId { get; private set; }

    /// <summary>What kind of movement it was.</summary>
    public PaymentFactKind Kind { get; private set; }

    /// <summary>The order it belongs to, where there is one.</summary>
    public Guid? OrderId { get; private set; }

    /// <summary>The payment or refund it came from.</summary>
    public Guid? PaymentId { get; private set; }

    /// <summary>How the money moved, as the Payments module named it.</summary>
    public string Method { get; private set; } = string.Empty;

    /// <summary>How much. Always positive; the kind says which direction.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO 4217 code.</summary>
    public string CurrencyCode { get; private set; } = "INR";

    /// <summary>When, in UTC.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>The reporting day it falls on.</summary>
    public DateOnly OccurredOn { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Records a movement of money.</summary>
    /// <param name="eventId">The integration event that produced it.</param>
    /// <param name="kind">Which direction.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="paymentId">The payment or refund.</param>
    /// <param name="method">How the money moved.</param>
    /// <param name="amount">How much.</param>
    /// <param name="currencyCode">In what.</param>
    /// <param name="occurredAt">When.</param>
    /// <param name="reportingDay">The reporting day that falls on.</param>
    public static PaymentFact Record(
        Guid eventId,
        PaymentFactKind kind,
        Guid? orderId,
        Guid? paymentId,
        string method,
        decimal amount,
        string currencyCode,
        DateTimeOffset occurredAt,
        DateOnly reportingDay)
        => new(UuidV7.NewAt(occurredAt), eventId, kind)
        {
            OrderId = orderId,
            PaymentId = paymentId,
            Method = method,
            Amount = Math.Abs(amount),
            CurrencyCode = currencyCode,
            OccurredAt = occurredAt,
            OccurredOn = reportingDay,
        };
}

/// <summary>
/// One returned line, as reporting keeps it.
/// </summary>
/// <remarks>
/// Keyed on the return line so a redelivered event updates rather than duplicates. The reason code
/// is on the row because "return rate by reason" is the report a buying team acts on: a rate driven
/// by <c>SizeIssue</c> is a sizing chart to fix, and the same rate driven by <c>DamagedInTransit</c>
/// is a packing problem, and a single number cannot tell them apart.
/// </remarks>
internal sealed class ReturnedLineFact : AggregateRoot<Guid>, ITenantScoped, IVendorScoped
{
    private ReturnedLineFact(Guid id, Guid returnLineId)
        : base(id)
        => ReturnLineId = returnLineId;

    /// <summary>Required by EF Core's materialiser.</summary>
    private ReturnedLineFact()
    {
    }

    /// <summary>The return line. Unique per tenant.</summary>
    public Guid ReturnLineId { get; private set; }

    /// <summary>The RMA it belongs to.</summary>
    public Guid ReturnId { get; private set; }

    /// <summary>The order line that came back.</summary>
    public Guid OrderLineId { get; private set; }

    /// <summary>The order.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The seller's part of it.</summary>
    public Guid SubOrderId { get; private set; }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>The sellable thing, where the catalogue could name it.</summary>
    public Guid? VariantId { get; private set; }

    /// <summary>Its product.</summary>
    public Guid? ProductId { get; private set; }

    /// <summary>Its category.</summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>The category's name.</summary>
    public string? CategoryName { get; private set; }

    /// <summary>The stock-keeping unit.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>The reason code the shopper chose.</summary>
    public string ReasonCode { get; private set; } = string.Empty;

    /// <summary>How many units.</summary>
    public int Quantity { get; private set; }

    /// <summary>What they were worth.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Where the return stands.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>What the receiving bay decided, once it has.</summary>
    public string? Disposition { get; private set; }

    /// <summary>When it was asked for, in UTC.</summary>
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>The reporting day that falls on.</summary>
    public DateOnly RequestedOn { get; private set; }

    /// <summary>When it closed, in UTC.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Records a requested return.</summary>
    /// <param name="line">Everything about it, assembled by the handler that saw the event.</param>
    public static ReturnedLineFact Record(ReturnedLineFactValues line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new ReturnedLineFact(UuidV7.NewAt(line.RequestedAt), line.ReturnLineId)
        {
            ReturnId = line.ReturnId,
            OrderLineId = line.OrderLineId,
            OrderId = line.OrderId,
            SubOrderId = line.SubOrderId,
            VendorId = line.VendorId,
            VariantId = line.VariantId,
            ProductId = line.ProductId,
            CategoryId = line.CategoryId,
            CategoryName = line.CategoryName,
            Sku = line.Sku,
            ReasonCode = line.ReasonCode,
            Quantity = line.Quantity,
            Amount = line.Amount,
            Status = line.Status,
            RequestedAt = line.RequestedAt,
            RequestedOn = line.RequestedOn,
        };
    }

    /// <summary>Follows the return's life.</summary>
    /// <param name="status">Where it stands.</param>
    /// <param name="disposition">What the receiving bay decided, if it has.</param>
    /// <param name="closedAt">When it closed, if it has.</param>
    public void Advance(string status, string? disposition = null, DateTimeOffset? closedAt = null)
    {
        Status = status;
        Disposition = disposition ?? Disposition;
        ClosedAt ??= closedAt;
    }
}

/// <summary>Everything a return line's fact row needs.</summary>
/// <param name="ReturnLineId">The return line.</param>
/// <param name="ReturnId">The RMA.</param>
/// <param name="OrderLineId">The order line that came back.</param>
/// <param name="OrderId">The order.</param>
/// <param name="SubOrderId">The seller's part of it.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="VariantId">The sellable thing.</param>
/// <param name="ProductId">Its product.</param>
/// <param name="CategoryId">Its category.</param>
/// <param name="CategoryName">The category's name.</param>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="ReasonCode">Why it came back.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="Amount">What they were worth.</param>
/// <param name="Status">Where the return stands.</param>
/// <param name="RequestedAt">When it was asked for.</param>
/// <param name="RequestedOn">The reporting day that falls on.</param>
internal sealed record ReturnedLineFactValues(
    Guid ReturnLineId,
    Guid ReturnId,
    Guid OrderLineId,
    Guid OrderId,
    Guid SubOrderId,
    Guid VendorId,
    Guid? VariantId,
    Guid? ProductId,
    Guid? CategoryId,
    string? CategoryName,
    string Sku,
    string ReasonCode,
    int Quantity,
    decimal Amount,
    string Status,
    DateTimeOffset RequestedAt,
    DateOnly RequestedOn);

/// <summary>
/// One closed settlement period for one seller, as reporting keeps it.
/// </summary>
/// <remarks>
/// The settlement summary is the one report whose numbers a seller will check against their own
/// books, so every deduction is its own column rather than a net. A statement that showed only "we
/// paid you ₹84,120" would be a statement that generates a support ticket every cycle.
/// </remarks>
internal sealed class SettlementFact : AggregateRoot<Guid>, ITenantScoped, IVendorScoped, IAppendOnly
{
    private SettlementFact(Guid id, Guid periodId)
        : base(id)
        => PeriodId = periodId;

    /// <summary>Required by EF Core's materialiser.</summary>
    private SettlementFact()
    {
    }

    /// <summary>The settlement period. Unique per tenant.</summary>
    public Guid PeriodId { get; private set; }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>When the period opened, in UTC.</summary>
    public DateTimeOffset PeriodStart { get; private set; }

    /// <summary>When it closed, in UTC. Half-open, as Settlements defines it.</summary>
    public DateTimeOffset PeriodEnd { get; private set; }

    /// <summary>The reporting day the close falls on.</summary>
    public DateOnly ClosedOn { get; private set; }

    /// <summary>What the seller sold in the period.</summary>
    public decimal GrossSales { get; private set; }

    /// <summary>What the platform charged.</summary>
    public decimal Commission { get; private set; }

    /// <summary>Other fees.</summary>
    public decimal Fees { get; private set; }

    /// <summary>Tax collected at source under CGST s.52.</summary>
    public decimal Tcs { get; private set; }

    /// <summary>Tax deducted at source under s.194-O.</summary>
    public decimal Tds { get; private set; }

    /// <summary>What came back in the period.</summary>
    public decimal Refunds { get; private set; }

    /// <summary>What is owed after all of it.</summary>
    public decimal NetPayable { get; private set; }

    /// <summary>ISO 4217 code.</summary>
    public string CurrencyCode { get; private set; } = "INR";

    /// <summary>How many orders it covers.</summary>
    public int OrderCount { get; private set; }

    /// <summary>When the period was closed, in UTC.</summary>
    public DateTimeOffset ClosedAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Records a closed period.</summary>
    /// <param name="values">Everything about it.</param>
    public static SettlementFact Record(SettlementFactValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return new SettlementFact(UuidV7.NewAt(values.ClosedAt), values.PeriodId)
        {
            VendorId = values.VendorId,
            PeriodStart = values.PeriodStart,
            PeriodEnd = values.PeriodEnd,
            ClosedOn = values.ClosedOn,
            GrossSales = values.GrossSales,
            Commission = values.Commission,
            Fees = values.Fees,
            Tcs = values.Tcs,
            Tds = values.Tds,
            Refunds = values.Refunds,
            NetPayable = values.NetPayable,
            CurrencyCode = values.CurrencyCode,
            OrderCount = values.OrderCount,
            ClosedAt = values.ClosedAt,
        };
    }
}

/// <summary>Everything a settlement fact row needs.</summary>
/// <param name="PeriodId">The settlement period.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="PeriodStart">When it opened.</param>
/// <param name="PeriodEnd">When it closed.</param>
/// <param name="ClosedOn">The reporting day the close falls on.</param>
/// <param name="GrossSales">What was sold.</param>
/// <param name="Commission">What the platform charged.</param>
/// <param name="Fees">Other fees.</param>
/// <param name="Tcs">Tax collected at source.</param>
/// <param name="Tds">Tax deducted at source.</param>
/// <param name="Refunds">What came back.</param>
/// <param name="NetPayable">What is owed.</param>
/// <param name="CurrencyCode">In what.</param>
/// <param name="OrderCount">How many orders it covers.</param>
/// <param name="ClosedAt">When it was closed.</param>
internal sealed record SettlementFactValues(
    Guid PeriodId,
    Guid VendorId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateOnly ClosedOn,
    decimal GrossSales,
    decimal Commission,
    decimal Fees,
    decimal Tcs,
    decimal Tds,
    decimal Refunds,
    decimal NetPayable,
    string CurrencyCode,
    int OrderCount,
    DateTimeOffset ClosedAt);

/// <summary>A step of the buying journey.</summary>
/// <remarks>
/// Five steps, and they are the five this platform can actually observe. A conversion funnel with
/// "viewed a product" in it would need a client-side analytics pipeline this system does not have,
/// and inventing a number for it would be worse than admitting the funnel starts at the basket.
/// </remarks>
internal enum FunnelStep
{
    /// <summary>A basket was abandoned — items in it, and no order.</summary>
    CartAbandoned = 0,

    /// <summary>A basket became an order.</summary>
    CartConverted = 1,

    /// <summary>An order was placed.</summary>
    OrderPlaced = 2,

    /// <summary>An order was paid for.</summary>
    OrderPaid = 3,

    /// <summary>A seller's part of an order was confirmed and is being fulfilled.</summary>
    OrderConfirmed = 4,
}

/// <summary>
/// One step of one shopper's journey, as reporting keeps it.
/// </summary>
/// <remarks>
/// Append-only and keyed on the event, because a funnel is a count of things that happened and a
/// redelivery must not make one of them happen twice. The basket's own value is carried so
/// abandonment can be reported in money as well as in count — "we lost eleven baskets" and "we lost
/// ₹94,000 of baskets" are different conversations.
/// </remarks>
internal sealed class FunnelFact : AggregateRoot<Guid>, ITenantScoped, IAppendOnly
{
    private FunnelFact(Guid id, Guid eventId, FunnelStep step)
        : base(id)
    {
        SourceEventId = eventId;
        Step = step;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private FunnelFact()
    {
    }

    /// <summary>The integration event that produced this row. Unique, and the idempotency key.</summary>
    public Guid SourceEventId { get; private set; }

    /// <summary>Which step.</summary>
    public FunnelStep Step { get; private set; }

    /// <summary>The basket, where the step had one.</summary>
    public Guid? CartId { get; private set; }

    /// <summary>The order, where the step had one.</summary>
    public Guid? OrderId { get; private set; }

    /// <summary>The shopper, when they were signed in.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>What was in play, so abandonment can be reported in money.</summary>
    public decimal Value { get; private set; }

    /// <summary>How many lines were in it.</summary>
    public int ItemCount { get; private set; }

    /// <summary>When, in UTC.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>The reporting day that falls on.</summary>
    public DateOnly OccurredOn { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Records a step.</summary>
    /// <param name="eventId">The integration event that produced it.</param>
    /// <param name="step">Which step.</param>
    /// <param name="cartId">The basket.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="customerId">The shopper.</param>
    /// <param name="value">What was in play.</param>
    /// <param name="itemCount">How many lines.</param>
    /// <param name="occurredAt">When.</param>
    /// <param name="reportingDay">The reporting day that falls on.</param>
    public static FunnelFact Record(
        Guid eventId,
        FunnelStep step,
        Guid? cartId,
        Guid? orderId,
        Guid? customerId,
        decimal value,
        int itemCount,
        DateTimeOffset occurredAt,
        DateOnly reportingDay)
        => new(UuidV7.NewAt(occurredAt), eventId, step)
        {
            CartId = cartId,
            OrderId = orderId,
            CustomerId = customerId,
            Value = value,
            ItemCount = itemCount,
            OccurredAt = occurredAt,
            OccurredOn = reportingDay,
        };
}

/// <summary>
/// How much stock was sitting where, and how old it was, on one day.
/// </summary>
/// <remarks>
/// <para>
/// The only fact in this module that is not written by an integration event, and the only scheduled
/// write it makes. Ageing is not observable from messages: <c>StockLevelChanged</c> says what the
/// balance is, and no sequence of those says when the units currently on the shelf arrived. It is
/// therefore snapshotted daily through <c>IInventoryAgeing</c>, which is a seam Inventory implements
/// over its own ledger.
/// </para>
/// <para>
/// A series rather than a state, and that is the point of storing it at all. "How much stock is over
/// ninety days old" is a number; "is that getting better or worse" is the question a buying team
/// actually asks, and only a snapshot per day can answer it.
/// </para>
/// </remarks>
internal sealed class InventoryAgeFact : AggregateRoot<Guid>, ITenantScoped, IVendorScoped, IAppendOnly
{
    private InventoryAgeFact(Guid id, DateOnly snapshotOn, Guid listingId, Guid warehouseId)
        : base(id)
    {
        SnapshotOn = snapshotOn;
        ListingId = listingId;
        WarehouseId = warehouseId;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private InventoryAgeFact()
    {
    }

    /// <summary>The day the snapshot was taken. Unique with the stock line.</summary>
    public DateOnly SnapshotOn { get; private set; }

    /// <summary>The offer the stock is held against.</summary>
    public Guid ListingId { get; private set; }

    /// <summary>Where it is.</summary>
    public Guid WarehouseId { get; private set; }

    /// <summary>What that place is called.</summary>
    public string WarehouseName { get; private set; } = string.Empty;

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>The stock-keeping unit.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>How much is physically there.</summary>
    public int QuantityOnHand { get; private set; }

    /// <summary>How much of it is promised to an order.</summary>
    public int QuantityReserved { get; private set; }

    /// <summary>The last time stock arrived, in UTC.</summary>
    public DateTimeOffset? LastInboundAt { get; private set; }

    /// <summary>The last time stock left, in UTC.</summary>
    public DateTimeOffset? LastOutboundAt { get; private set; }

    /// <summary>Days since the last arrival, or null when there has never been one.</summary>
    public int? AgeDays { get; private set; }

    /// <summary>Which ageing band it falls in, so a report can group without a CASE expression.</summary>
    public string AgeBucket { get; private set; } = string.Empty;

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Records one stock line on one day.</summary>
    /// <param name="snapshotOn">The day.</param>
    /// <param name="listingId">The offer.</param>
    /// <param name="warehouseId">Where it is.</param>
    /// <param name="warehouseName">What that place is called.</param>
    /// <param name="vendorId">Whose stock it is.</param>
    /// <param name="sku">The stock-keeping unit.</param>
    /// <param name="quantityOnHand">How much is there.</param>
    /// <param name="quantityReserved">How much is promised.</param>
    /// <param name="lastInboundAt">The last arrival.</param>
    /// <param name="lastOutboundAt">The last departure.</param>
    /// <param name="ageDays">Days since the last arrival.</param>
    public static InventoryAgeFact Record(
        DateOnly snapshotOn,
        Guid listingId,
        Guid warehouseId,
        string warehouseName,
        Guid? vendorId,
        string sku,
        int quantityOnHand,
        int quantityReserved,
        DateTimeOffset? lastInboundAt,
        DateTimeOffset? lastOutboundAt,
        int? ageDays)
        => new(UuidV7.New(), snapshotOn, listingId, warehouseId)
        {
            WarehouseName = warehouseName,
            VendorId = vendorId,
            Sku = sku,
            QuantityOnHand = quantityOnHand,
            QuantityReserved = quantityReserved,
            LastInboundAt = lastInboundAt,
            LastOutboundAt = lastOutboundAt,
            AgeDays = ageDays,
            AgeBucket = BucketFor(ageDays),
        };

    /// <summary>
    /// Which band an age falls in.
    /// </summary>
    /// <remarks>
    /// The bands are the ones a retail buyer thinks in — a month, a quarter, half a year — and they
    /// are computed once at write time rather than in every query. <c>Unknown</c> is a band and not a
    /// zero: a stock line whose balance was opened by an adjustment has no arrival date, and putting
    /// it in the freshest band would hide the oldest goods in the store.
    /// </remarks>
    /// <param name="ageDays">Days since the last arrival, or null.</param>
    public static string BucketFor(int? ageDays)
        => ageDays switch
        {
            null => "Unknown",
            < 30 => "0-29",
            < 60 => "30-59",
            < 90 => "60-89",
            < 180 => "90-179",
            _ => "180+",
        };
}
