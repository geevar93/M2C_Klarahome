using KlaraHome.Modules.Orders.Domain;

namespace KlaraHome.Modules.Orders.Application.Orders;

/// <summary>An address as an order response states it.</summary>
/// <param name="RecipientName">Who the courier asks for.</param>
/// <param name="Mobile">The delivery contact number.</param>
/// <param name="Line1">House or flat number and building.</param>
/// <param name="Line2">Street, area or locality.</param>
/// <param name="Landmark">A nearby landmark.</param>
/// <param name="City">City or town.</param>
/// <param name="StateId">The <c>platform.states</c> row.</param>
/// <param name="StateName">Its name, as it was at the time.</param>
/// <param name="StateCode">The GST state code the tax was decided against.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="Gstin">The GSTIN it is billed to, for a B2B invoice.</param>
internal sealed record OrderAddressResponse(
    string RecipientName,
    string Mobile,
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    Guid StateId,
    string? StateName,
    string? StateCode,
    string Pincode,
    string? Gstin);

/// <summary>One item on an order.</summary>
/// <param name="Id">The line.</param>
/// <param name="ListingId">The offer bought.</param>
/// <param name="VariantId">The sellable thing.</param>
/// <param name="ProductId">The product it belongs to.</param>
/// <param name="Sku">The stock-keeping unit, as it was.</param>
/// <param name="Name">What it was called, as it was.</param>
/// <param name="ImageFileId">The image to render. Resolved by the client through the media library.</param>
/// <param name="HsnCode">The HSN the tax rate came from.</param>
/// <param name="Quantity">Units ordered.</param>
/// <param name="QuantityCancelled">Units cancelled.</param>
/// <param name="QuantityReturned">Units returned.</param>
/// <param name="UnitMrp">Maximum retail price per unit.</param>
/// <param name="UnitPrice">Selling price per unit, inclusive of GST.</param>
/// <param name="DiscountAmount">Every discount on the line.</param>
/// <param name="TaxableValue">What the tax was computed on.</param>
/// <param name="GstRate">The GST percentage applied.</param>
/// <param name="Cgst">Central GST.</param>
/// <param name="Sgst">State GST.</param>
/// <param name="Igst">Integrated GST.</param>
/// <param name="Cess">Compensation cess.</param>
/// <param name="LineTotal">What the shopper pays for the line.</param>
/// <param name="Status">Where the line stands.</param>
/// <param name="IsReturnable">Whether the product could be returned at all.</param>
internal sealed record OrderLineResponse(
    Guid Id,
    Guid ListingId,
    Guid VariantId,
    Guid ProductId,
    string Sku,
    string Name,
    Guid? ImageFileId,
    string? HsnCode,
    int Quantity,
    int QuantityCancelled,
    int QuantityReturned,
    decimal UnitMrp,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxableValue,
    decimal GstRate,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal Cess,
    decimal LineTotal,
    string Status,
    bool IsReturnable);

/// <summary>What the platform charges a seller on one line. Never shown to a shopper.</summary>
/// <param name="OrderLineId">The line.</param>
/// <param name="CommissionRate">The rate applied.</param>
/// <param name="CommissionAmount">What it came to.</param>
/// <param name="CommissionPlanId">The plan that decided it.</param>
internal sealed record OrderLineCommissionResponse(
    Guid OrderLineId,
    decimal CommissionRate,
    decimal CommissionAmount,
    Guid? CommissionPlanId);

/// <summary>A tax invoice, as a response states it.</summary>
/// <param name="Id">The invoice.</param>
/// <param name="InvoiceNumber">Its gapless number.</param>
/// <param name="Series">The series it came from.</param>
/// <param name="FinancialYear">The financial year.</param>
/// <param name="TaxableValue">What the tax was computed on.</param>
/// <param name="Cgst">Central GST.</param>
/// <param name="Sgst">State GST.</param>
/// <param name="Igst">Integrated GST.</param>
/// <param name="Cess">Compensation cess.</param>
/// <param name="Total">The invoice total.</param>
/// <param name="CurrencyCode">ISO 4217 code the figures are in.</param>
/// <param name="Status">Whether it still stands.</param>
/// <param name="FileId">The stored PDF, or null when it has not been rendered.</param>
/// <param name="Irn">The Invoice Reference Number, once e-invoicing is switched on.</param>
/// <param name="IssuedAt">When it was raised.</param>
internal sealed record InvoiceResponse(
    Guid Id,
    string InvoiceNumber,
    string Series,
    string FinancialYear,
    decimal TaxableValue,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal Cess,
    decimal Total,
    string CurrencyCode,
    string Status,
    Guid? FileId,
    string? Irn,
    DateTimeOffset IssuedAt);

/// <summary>One seller's part of an order.</summary>
/// <param name="Id">The sub-order.</param>
/// <param name="SubOrderNumber">The number on the seller's worklist and the parcel.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="VendorName">Their trading name, as it was.</param>
/// <param name="Status">Where it is in its life.</param>
/// <param name="ItemsTotal">The lines' gross.</param>
/// <param name="DiscountTotal">Their discount.</param>
/// <param name="ShippingTotal">Their share of delivery.</param>
/// <param name="TaxTotal">Their tax.</param>
/// <param name="Total">What the shopper pays for their part.</param>
/// <param name="CancelledTotal">What has come off it through cancellation.</param>
/// <param name="NetTotal">What is still owed on it.</param>
/// <param name="CurrencyCode">ISO 4217 code the figures are in.</param>
/// <param name="ShippingOptionCode">The delivery service the shopper chose.</param>
/// <param name="Carrier">The courier, when one has been decided.</param>
/// <param name="PromisedMinDays">Earliest delivery, in days from dispatch.</param>
/// <param name="PromisedMaxDays">Latest delivery, in days from dispatch.</param>
/// <param name="DispatchDueAt">When the seller must have handed it over.</param>
/// <param name="ConfirmedAt">When it was confirmed.</param>
/// <param name="ShippedAt">When it was handed to a courier.</param>
/// <param name="DeliveredAt">When it was delivered.</param>
/// <param name="ReturnWindowEndsAt">When the right to return it lapses.</param>
/// <param name="CancelledAt">When it was cancelled.</param>
/// <param name="CancelledBy">Who cancelled it.</param>
/// <param name="CancellationReason">Why.</param>
/// <param name="IsCancellable">Whether the shopper may still cancel it themselves.</param>
/// <param name="Lines">What is in it.</param>
/// <param name="Invoice">Its tax invoice, when one has been raised.</param>
/// <param name="NextStatuses">Where it may go next, for the caller who is asking.</param>
internal sealed record SubOrderResponse(
    Guid Id,
    string SubOrderNumber,
    Guid VendorId,
    string? VendorName,
    string Status,
    decimal ItemsTotal,
    decimal DiscountTotal,
    decimal ShippingTotal,
    decimal TaxTotal,
    decimal Total,
    decimal CancelledTotal,
    decimal NetTotal,
    string CurrencyCode,
    string? ShippingOptionCode,
    string? Carrier,
    int PromisedMinDays,
    int PromisedMaxDays,
    DateTimeOffset? DispatchDueAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReturnWindowEndsAt,
    DateTimeOffset? CancelledAt,
    string? CancelledBy,
    string? CancellationReason,
    bool IsCancellable,
    IReadOnlyList<OrderLineResponse> Lines,
    InvoiceResponse? Invoice,
    IReadOnlyList<string> NextStatuses);

/// <summary>One entry on an order's timeline.</summary>
/// <param name="Id">The entry.</param>
/// <param name="SubOrderId">The seller's part it concerns, or null for an order-level entry.</param>
/// <param name="Type">What kind of entry it is.</param>
/// <param name="FromStatus">Where the sub-order was, for a status change.</param>
/// <param name="ToStatus">Where it went.</param>
/// <param name="ActorType">What class of actor did it.</param>
/// <param name="Message">What it says.</param>
/// <param name="IsCustomerVisible">Whether the shopper sees it.</param>
/// <param name="OccurredAt">When it happened.</param>
internal sealed record OrderEventResponse(
    Guid Id,
    Guid? SubOrderId,
    string Type,
    string? FromStatus,
    string? ToStatus,
    string ActorType,
    string? Message,
    bool IsCustomerVisible,
    DateTimeOffset OccurredAt);

/// <summary>An order in a list.</summary>
/// <param name="Id">The order.</param>
/// <param name="OrderNumber">The number a shopper quotes.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="CustomerName">Their name, as it was.</param>
/// <param name="Status">Where the order stands, derived from its parts.</param>
/// <param name="PaymentMethod">Prepaid or cash on delivery.</param>
/// <param name="PaymentStatus">Where the money stands.</param>
/// <param name="GrandTotal">What was agreed.</param>
/// <param name="NetTotal">What is still owed, after cancellations.</param>
/// <param name="CurrencyCode">ISO 4217 code the figures are in.</param>
/// <param name="ItemCount">How many units are on it.</param>
/// <param name="VendorCount">How many sellers it split across.</param>
/// <param name="SubOrderStatuses">Each seller's state, so a list can show the per-vendor breakdown.</param>
/// <param name="PlacedAt">When it was placed.</param>
internal sealed record OrderSummaryResponse(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string CustomerName,
    string Status,
    string PaymentMethod,
    string PaymentStatus,
    decimal GrandTotal,
    decimal NetTotal,
    string CurrencyCode,
    int ItemCount,
    int VendorCount,
    IReadOnlyList<string> SubOrderStatuses,
    DateTimeOffset PlacedAt);

/// <summary>An order in full.</summary>
/// <param name="Id">The order.</param>
/// <param name="OrderNumber">The number a shopper quotes.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="CustomerName">Their name, as it was.</param>
/// <param name="CustomerEmail">Their email, as it was.</param>
/// <param name="CustomerMobile">Their mobile, as it was.</param>
/// <param name="Status">Where the order stands.</param>
/// <param name="PaymentMethod">Prepaid or cash on delivery.</param>
/// <param name="PaymentStatus">Where the money stands.</param>
/// <param name="ItemsTotal">The lines' gross.</param>
/// <param name="DiscountTotal">Every discount.</param>
/// <param name="ShippingTotal">Delivery, inclusive of tax.</param>
/// <param name="TaxTotal">Every tax figure.</param>
/// <param name="CodFee">The cash-on-delivery handling fee.</param>
/// <param name="WalletApplied">Store credit redeemed.</param>
/// <param name="RoundingAdjustment">What was rounded off.</param>
/// <param name="GrandTotal">What was agreed.</param>
/// <param name="AmountPayable">What the gateway was asked for.</param>
/// <param name="CancelledTotal">What has come off through cancellation.</param>
/// <param name="NetTotal">What is still owed.</param>
/// <param name="CurrencyCode">ISO 4217 code every figure is in.</param>
/// <param name="CouponCode">The code applied, if any.</param>
/// <param name="Channel">Where the order came from.</param>
/// <param name="ShippingAddress">Where it goes.</param>
/// <param name="BillingAddress">Who it is billed to.</param>
/// <param name="PlacedAt">When it was placed.</param>
/// <param name="CompletedAt">When it completed.</param>
/// <param name="CancelledAt">When it was cancelled.</param>
/// <param name="CancellationReason">Why.</param>
/// <param name="Notes">The operator's note. Null on the storefront.</param>
/// <param name="SubOrders">One per seller.</param>
/// <param name="Timeline">What has happened to it, oldest first.</param>
internal sealed record OrderResponse(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string CustomerName,
    string? CustomerEmail,
    string? CustomerMobile,
    string Status,
    string PaymentMethod,
    string PaymentStatus,
    decimal ItemsTotal,
    decimal DiscountTotal,
    decimal ShippingTotal,
    decimal TaxTotal,
    decimal CodFee,
    decimal WalletApplied,
    decimal RoundingAdjustment,
    decimal GrandTotal,
    decimal AmountPayable,
    decimal CancelledTotal,
    decimal NetTotal,
    string CurrencyCode,
    string? CouponCode,
    string Channel,
    OrderAddressResponse ShippingAddress,
    OrderAddressResponse BillingAddress,
    DateTimeOffset PlacedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    string? Notes,
    IReadOnlyList<SubOrderResponse> SubOrders,
    IReadOnlyList<OrderEventResponse> Timeline);

/// <summary>
/// Turns the aggregate into the shapes the API returns.
/// </summary>
/// <remarks>
/// <para>
/// One projector for three audiences, with the differences passed in rather than duplicated. A
/// shopper sees the visible half of the timeline and no operator note; a seller sees their own
/// sub-orders; platform staff see all of it. Writing three projectors would mean three places to
/// forget a field.
/// </para>
/// <para>
/// <see cref="SubOrderResponse.NextStatuses"/> is computed for the <em>asking</em> actor, which is
/// what lets an admin screen offer exactly the buttons that will work rather than offering all of
/// them and reporting a conflict on the ones that will not.
/// </para>
/// </remarks>
internal static class OrderProjection
{
    /// <summary>Projects one order in full.</summary>
    /// <param name="order">The order, with sub-orders, lines and timeline loaded.</param>
    /// <param name="invoices">Its invoices, keyed by sub-order.</param>
    /// <param name="actor">Who is asking; decides the offered next states.</param>
    /// <param name="includeInternal">Whether to include operator notes and invisible timeline entries.</param>
    public static OrderResponse ToResponse(
        Order order,
        IReadOnlyDictionary<Guid, Invoice> invoices,
        OrderActor actor,
        bool includeInternal)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(invoices);

        return new OrderResponse(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.CustomerSnapshot.DisplayName,
            order.CustomerSnapshot.Email,
            order.CustomerSnapshot.Mobile,
            order.Status.ToString(),
            order.PaymentMethod.ToString(),
            order.PaymentStatus.ToString(),
            order.ItemsTotal,
            order.DiscountTotal,
            order.ShippingTotal,
            order.TaxTotal,
            order.CodFee,
            order.WalletApplied,
            order.RoundingAdjustment,
            order.GrandTotal,
            order.AmountPayable,
            order.CancelledTotal,
            order.NetTotal,
            order.CurrencyCode,
            order.CouponCode,
            order.Channel,
            ToResponse(order.ShippingAddress),
            ToResponse(order.BillingAddress),
            order.PlacedAt,
            order.CompletedAt,
            order.CancelledAt,
            order.CancellationReason,
            includeInternal ? order.Notes : null,
            [
                .. order.SubOrders
                    .OrderBy(subOrder => subOrder.SubOrderNumber, StringComparer.Ordinal)
                    .Select(subOrder => ToResponse(
                        subOrder,
                        invoices.GetValueOrDefault(subOrder.Id),
                        actor)),
            ],
            [
                .. order.Events
                    .Where(entry => includeInternal || entry.IsCustomerVisible)
                    .OrderBy(entry => entry.OccurredAt)
                    .Select(ToResponse),
            ]);
    }

    /// <summary>Projects one order into a list row.</summary>
    /// <param name="order">The order, with sub-orders and lines loaded.</param>
    public static OrderSummaryResponse ToSummary(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderSummaryResponse(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.CustomerSnapshot.DisplayName,
            order.Status.ToString(),
            order.PaymentMethod.ToString(),
            order.PaymentStatus.ToString(),
            order.GrandTotal,
            order.NetTotal,
            order.CurrencyCode,
            order.SubOrders.Sum(subOrder => subOrder.Lines.Sum(line => line.Quantity)),
            order.SubOrders.Count,
            [.. order.SubOrders.Select(subOrder => subOrder.Status.ToString())],
            order.PlacedAt);
    }

    /// <summary>Projects one seller's part.</summary>
    /// <param name="subOrder">The sub-order.</param>
    /// <param name="invoice">Its invoice, when one has been raised.</param>
    /// <param name="actor">Who is asking; decides the offered next states.</param>
    public static SubOrderResponse ToResponse(SubOrder subOrder, Invoice? invoice, OrderActor actor)
    {
        ArgumentNullException.ThrowIfNull(subOrder);

        return new SubOrderResponse(
            subOrder.Id,
            subOrder.SubOrderNumber,
            subOrder.VendorId ?? Guid.Empty,
            subOrder.VendorName,
            subOrder.Status.ToString(),
            subOrder.ItemsTotal,
            subOrder.DiscountTotal,
            subOrder.ShippingTotal,
            subOrder.TaxTotal,
            subOrder.Total,
            subOrder.CancelledTotal,
            subOrder.NetTotal,
            subOrder.CurrencyCode,
            subOrder.ShippingOptionCode,
            subOrder.Carrier,
            subOrder.PromisedMinDays,
            subOrder.PromisedMaxDays,
            subOrder.DispatchDueAt,
            subOrder.ConfirmedAt,
            subOrder.ShippedAt,
            subOrder.DeliveredAt,
            subOrder.ReturnWindowEndsAt,
            subOrder.CancelledAt,
            subOrder.CancelledBy?.ToString(),
            subOrder.CancellationReason,
            SubOrderLifecycle.IsCustomerCancellable(subOrder.Status),
            [.. subOrder.Lines.Select(ToResponse)],
            invoice is null ? null : ToResponse(invoice),
            [.. SubOrderLifecycle.NextFrom(subOrder.Status, actor).Select(status => status.ToString())]);
    }

    /// <summary>Projects one line.</summary>
    /// <param name="line">The line.</param>
    public static OrderLineResponse ToResponse(OrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new OrderLineResponse(
            line.Id,
            line.ListingId,
            line.VariantId,
            line.Snapshot.ProductId,
            line.Sku,
            line.Snapshot.Name,
            line.Snapshot.ImageFileId,
            line.Snapshot.HsnCode,
            line.Quantity,
            line.QuantityCancelled,
            line.QuantityReturned,
            line.UnitMrp,
            line.UnitPrice,
            line.DiscountAmount,
            line.TaxableValue,
            line.GstRate,
            line.Cgst,
            line.Sgst,
            line.Igst,
            line.Cess,
            line.LineTotal,
            line.Status.ToString(),
            line.Snapshot.IsReturnable);
    }

    /// <summary>Projects one invoice.</summary>
    /// <param name="invoice">The invoice.</param>
    public static InvoiceResponse ToResponse(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return new InvoiceResponse(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.Series,
            invoice.FinancialYear,
            invoice.TaxableValue,
            invoice.Cgst,
            invoice.Sgst,
            invoice.Igst,
            invoice.Cess,
            invoice.Total,
            invoice.CurrencyCode,
            invoice.Status.ToString(),
            invoice.FileId,
            invoice.Irn,
            invoice.IssuedAt);
    }

    /// <summary>Projects one timeline entry.</summary>
    /// <param name="entry">The entry.</param>
    public static OrderEventResponse ToResponse(OrderEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new OrderEventResponse(
            entry.Id,
            entry.SubOrderId,
            entry.Type,
            entry.FromStatus,
            entry.ToStatus,
            entry.ActorType.ToString(),
            entry.Message,
            entry.IsCustomerVisible,
            entry.OccurredAt);
    }

    /// <summary>Projects an address snapshot.</summary>
    /// <param name="address">The snapshot.</param>
    public static OrderAddressResponse ToResponse(OrderAddressSnapshot address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return new OrderAddressResponse(
            address.RecipientName,
            address.Mobile,
            address.Line1,
            address.Line2,
            address.Landmark,
            address.City,
            address.StateId,
            address.StateName,
            address.StateCode,
            address.Pincode,
            address.Gstin);
    }
}
