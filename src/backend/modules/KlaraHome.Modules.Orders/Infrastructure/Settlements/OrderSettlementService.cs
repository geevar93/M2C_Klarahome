using KlaraHome.Contracts.Orders;
using KlaraHome.Modules.Orders.Application;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Orders.Infrastructure.Settlements;

/// <summary>
/// Answers <see cref="IOrderSettlement"/> from this module's own tables.
/// </summary>
/// <remarks>
/// <para>
/// The fourth of ordering's outward seams and the only one that cannot write. Settlements reads what
/// was sold, what it was worth and what the platform charged for it, and does everything else in its
/// own schema. A seam that could write would be a seam through which a settlement run could change
/// the sale it is settling — which is the one thing an auditor would ask about first.
/// </para>
/// <para>
/// The commission is read off the frozen line rather than resolved. It was decided from the seller's
/// plan when the order was placed and written onto <c>order_lines</c>; handing back today's rate would
/// let a plan changed this morning re-price last month's sales, and a seller's statement would stop
/// agreeing with the invoice it came from.
/// </para>
/// <para>
/// Deliberately not confined to a customer, unlike <see cref="IOrderReturns"/>. A settlement run
/// covers every shopper at once and there is no customer to confine it to; what confines it is that
/// it is only ever called from a background job and an admin surface behind a finance permission.
/// </para>
/// </remarks>
/// <param name="context">The Ordering data context.</param>
internal sealed class OrderSettlementService(OrdersDbContext context) : IOrderSettlement
{
    /// <inheritdoc />
    public async Task<Result<SubOrderSettlementView>> GetAsync(
        Guid subOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await ReadQuery()
            .FirstOrDefaultAsync(
                candidate => candidate.SubOrders.Any(subOrder => subOrder.Id == subOrderId),
                cancellationToken)
            .ConfigureAwait(false);

        var found = order?.SubOrders.FirstOrDefault(subOrder => subOrder.Id == subOrderId);

        if (order is null || found is null)
        {
            return Result.Failure<SubOrderSettlementView>(OrdersErrors.NotFound("sub-order"));
        }

        var invoice = await InvoiceAsync(found.Id, cancellationToken).ConfigureAwait(false);

        return Result.Success(Project(order, found, invoice));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubOrderSettlementView>> GetManyAsync(
        IReadOnlyCollection<Guid> subOrderIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subOrderIds);

        if (subOrderIds.Count == 0)
        {
            return [];
        }

        var wanted = subOrderIds.Distinct().ToArray();

        var orders = await ReadQuery()
            .Where(order => order.SubOrders.Any(subOrder => wanted.Contains(subOrder.Id)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var invoices = await context.Invoices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(invoice => wanted.Contains(invoice.SubOrderId))
            .Select(invoice => new
            {
                invoice.SubOrderId,
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.FinancialYear,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var bySubOrder = invoices.ToDictionary(
            invoice => invoice.SubOrderId,
            invoice => new InvoiceFacts(invoice.Id, invoice.InvoiceNumber, invoice.FinancialYear));

        return
        [
            .. orders.SelectMany(
                order => order.SubOrders.Where(subOrder => wanted.Contains(subOrder.Id)),
                (order, subOrder) => Project(order, subOrder, bySubOrder.GetValueOrDefault(subOrder.Id))),
        ];
    }

    /// <summary>
    /// The read query.
    /// </summary>
    /// <remarks>
    /// Filters are ignored for the same reason every other outward seam ignores them: this is called
    /// from a background job with no caller and no vendor scope, and a query that silently returned
    /// nothing would silently stop paying sellers. The tenant is applied explicitly instead.
    /// </remarks>
    private IQueryable<Order> ReadQuery()
        => context.Orders
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(order => order.TenantId == context.TenantId)
            .Include(order => order.SubOrders)
            .ThenInclude(subOrder => subOrder.Lines);

    /// <summary>The invoice raised against one seller's part, where one was.</summary>
    private async Task<InvoiceFacts?> InvoiceAsync(Guid subOrderId, CancellationToken cancellationToken)
    {
        var invoice = await context.Invoices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(candidate => candidate.TenantId == context.TenantId && candidate.SubOrderId == subOrderId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.InvoiceNumber,
                candidate.FinancialYear,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return invoice is null
            ? null
            : new InvoiceFacts(invoice.Id, invoice.InvoiceNumber, invoice.FinancialYear);
    }

    /// <summary>Projects one seller's part into the shape a settlement run reads.</summary>
    private static SubOrderSettlementView Project(Order order, SubOrder subOrder, InvoiceFacts? invoice)
        => new(
            order.Id,
            order.OrderNumber,
            subOrder.Id,
            subOrder.SubOrderNumber,
            subOrder.VendorId ?? Guid.Empty,
            subOrder.VendorCode,
            subOrder.VendorName,
            subOrder.VendorGstin,
            order.CustomerId,
            subOrder.Status.ToString(),
            order.PaymentMethod.ToString(),
            order.PaymentStatus is OrderPaymentStatus.Paid or OrderPaymentStatus.PartiallyRefunded,
            subOrder.ItemsTotal,
            subOrder.DiscountTotal,
            subOrder.ShippingTotal,
            subOrder.ShippingTax,
            subOrder.TaxableValue,
            subOrder.TaxTotal,
            subOrder.Total,
            subOrder.CurrencyCode,
            order.PlacedAt,
            subOrder.DeliveredAt,
            subOrder.ReturnWindowEndsAt,
            invoice?.InvoiceId,
            invoice?.InvoiceNumber,
            invoice?.FinancialYear,
            [.. subOrder.Lines.Select(Project)]);

    /// <summary>Projects one line, carrying the commission exactly as it was frozen.</summary>
    private static SettleableLine Project(OrderLine line)
        => new(
            line.Id,
            line.ListingId,
            line.Snapshot.CategoryId,
            line.Sku,
            line.Snapshot.Name,
            line.Quantity,
            line.QuantityCancelled,
            line.QuantityReturned,
            line.UnitPrice,
            line.TaxableValue,
            line.Cgst + line.Sgst + line.Igst + line.Cess,
            line.LineTotal,
            line.CommissionRate,
            line.CommissionAmount,
            line.CommissionPlanId);

    /// <summary>The few invoice facts a statement quotes.</summary>
    /// <param name="InvoiceId">The invoice.</param>
    /// <param name="InvoiceNumber">Its gapless number.</param>
    /// <param name="FinancialYear">The financial year it belongs to.</param>
    private sealed record InvoiceFacts(Guid InvoiceId, string InvoiceNumber, string FinancialYear);
}
