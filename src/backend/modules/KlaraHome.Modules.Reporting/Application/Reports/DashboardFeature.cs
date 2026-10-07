using KlaraHome.Contracts.Inventory;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Reporting.Domain;
using KlaraHome.Modules.Reporting.Infrastructure;
using KlaraHome.Modules.Reporting.Infrastructure.Persistence;
using KlaraHome.Modules.Reporting.Infrastructure.Query;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Reporting.Application.Reports;

/// <summary>
/// The commercial figures at the top of the admin dashboard.
/// </summary>
/// <param name="Day">The reporting day the "today" figures are for (UTC date).</param>
/// <param name="CurrencyCode">ISO 4217 code the money figures are in.</param>
/// <param name="OrdersToday">
/// Orders with a line confirmed today - the <c>orders</c> total of the sales-by-day report for today,
/// not a count of baskets placed, so an unpaid prepaid order is not counted until its money lands.
/// </param>
/// <param name="RevenueToday">
/// What those lines are worth after cancellations and returns - the <c>netValue</c> total of the
/// same report, so the tile and the report it opens can never disagree.
/// </param>
/// <param name="CodPendingOrders">
/// Open cash-on-delivery orders for which no cash has been recorded yet. Null for a seller, because
/// the order facts it reads are the platform's, not any one seller's.
/// </param>
/// <param name="CodPendingAmount">
/// What is still to be collected on them (the amount payable at the door). Null for a seller.
/// </param>
/// <param name="LowStockCount">
/// Stock lines at or below a switched-on reorder level, the same definition as the stock list's
/// low-stock filter. A seller's count is confined to their own stock.
/// </param>
internal sealed record DashboardSummaryResponse(
    DateOnly Day,
    string CurrencyCode,
    int OrdersToday,
    decimal RevenueToday,
    int? CodPendingOrders,
    decimal? CodPendingAmount,
    int LowStockCount);

/// <summary>Reads the dashboard's commercial figures.</summary>
internal sealed record GetDashboardSummaryQuery : IQuery<DashboardSummaryResponse>;

/// <summary>
/// Answers the dashboard's four commercial questions from the reporting facts.
/// </summary>
/// <remarks>
/// <para>
/// "Today" is not recomputed here. The orders and revenue are the totals row of the
/// <c>sales-by-day</c> report run for today's window through the same <see cref="ReportQueryEngine"/>
/// every report goes through, so the dashboard tile and the report it opens are one calculation
/// rather than two that have to be kept in step (ADR-021: a report is a filtered aggregation over
/// one fact table).
/// </para>
/// <para>
/// Cash on delivery pending is the open COD orders that have no <c>CodCollected</c> payment fact: the
/// money the platform is owed and has not yet seen. Cancelled orders are not owed anything. It reads
/// two fact tables of this module with a correlated <c>NOT EXISTS</c> and no join, which keeps the
/// "no cross-module read" rule intact and the module's own convention nearly so.
/// </para>
/// <para>
/// Low stock is the one figure Reporting cannot derive: the reorder level is a setting on the
/// Inventory row, not a fact an event carries. It comes through <see cref="IStockAlerts"/>.
/// </para>
/// </remarks>
/// <param name="engine">Runs sales-by-day for today.</param>
/// <param name="context">The Reporting data context.</param>
/// <param name="alerts">Counts low stock, which only Inventory can.</param>
/// <param name="scope">Who is asking; a seller is confined to their own figures.</param>
/// <param name="options">The store's currency.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class GetDashboardSummaryQueryHandler(
    ReportQueryEngine engine,
    ReportingDbContext context,
    IStockAlerts alerts,
    ReportingScope scope,
    IOptionsMonitor<ReportingOptions> options,
    IClock clock) : IQueryHandler<GetDashboardSummaryQuery, DashboardSummaryResponse>
{
    /// <summary>The status an order's fact carries once it has been cancelled.</summary>
    private const string CancelledStatus = "Cancelled";

    public async Task<Result<DashboardSummaryResponse>> HandleAsync(
        GetDashboardSummaryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var today = new DateTimeOffset(clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var definition = ReportCatalog.Find(ReportCatalog.SalesByDay)!;

        var sales = await engine
            .RunAsync(definition, new ReportRequest(today, today.AddDays(1), null, scope.VendorId), cancellationToken)
            .ConfigureAwait(false);

        var totals = sales.Totals;
        var orders = totals is not null && totals.TryGetValue("orders", out var o) ? Convert.ToInt32(o) : 0;
        var revenue = totals is not null && totals.TryGetValue("netValue", out var n) ? Convert.ToDecimal(n) : 0m;

        int? codOrders = null;
        decimal? codAmount = null;

        if (scope.VendorId is null)
        {
            var pending = context.Orders
                .AsNoTracking()
                .Where(order => order.IsCod
                                && order.Status != CancelledStatus
                                && !context.Payments.Any(payment =>
                                    payment.OrderId == order.OrderId
                                    && payment.Kind == PaymentFactKind.CodCollected));

            codOrders = await pending.CountAsync(cancellationToken).ConfigureAwait(false);
            codAmount = await pending.SumAsync(order => (decimal?)order.AmountPayable, cancellationToken)
                .ConfigureAwait(false) ?? 0m;
        }

        var low = await alerts.CountLowStockAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new DashboardSummaryResponse(
            DateOnly.FromDateTime(today.UtcDateTime),
            options.CurrentValue.CurrencyCode,
            orders,
            revenue,
            codOrders,
            codAmount,
            low));
    }
}
