using KlaraHome.Modules.Reporting.Application;
using KlaraHome.Modules.Reporting.Domain;
using KlaraHome.Modules.Reporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Reporting.Infrastructure.Query;

/// <summary>What a report is being asked for.</summary>
/// <param name="From">The start of the period, inclusive.</param>
/// <param name="To">The end of the period, exclusive.</param>
/// <param name="GroupBy">Which grouping, where the report offers a choice.</param>
/// <param name="VendorId">The seller to confine the figures to, or null for the whole platform.</param>
internal sealed record ReportRequest(DateTimeOffset From, DateTimeOffset To, string? GroupBy, Guid? VendorId);

/// <summary>
/// Runs a declared report against this module's own facts.
/// </summary>
/// <remarks>
/// <para>
/// Thirteen methods, one per report, and every one of them is a filtered aggregation over exactly
/// one table. There is no join anywhere in this class, and that is not restraint — it is the module
/// boundary: everything a report groups by was denormalised onto the fact row when the event that
/// produced it arrived, precisely so this code never has to reach for a second table.
/// </para>
/// <para>
/// A switch rather than a dictionary of delegates or a small expression language. Thirteen is a
/// number a person can read, each query is a few lines of LINQ that Postgres turns into one grouped
/// index scan, and the alternative — a query builder general enough to express all of them — would
/// be a language nobody outside this file understands and an optimiser nobody can reason about.
/// </para>
/// <para>
/// Vendor confinement is applied twice, and deliberately. The global query filter on the context
/// already confines a seller's own token to their own rows; passing the seller explicitly as well
/// means a report run by <em>staff</em> on a named seller's behalf produces the same figures, and it
/// means the confinement is visible in the code a reviewer reads rather than only in a convention.
/// </para>
/// <para>
/// Every result is capped. A report is a table somebody reads or a CSV somebody opens, and neither
/// has a use for four hundred thousand rows; the cap is reported back so the admin app can say the
/// table is partial rather than letting it be read as complete.
/// </para>
/// </remarks>
/// <param name="context">The Reporting data context.</param>
/// <param name="options">The row ceiling and the store's currency.</param>
internal sealed class ReportQueryEngine(ReportingDbContext context, IOptionsMonitor<ReportingOptions> options)
{
    /// <summary>Runs one report.</summary>
    /// <param name="definition">The declared report.</param>
    /// <param name="request">The period, the grouping and the seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ReportResult> RunAsync(
        ReportDefinition definition,
        ReportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(request);

        var settings = options.CurrentValue;
        var limit = settings.MaxReportRows;

        var (rows, totals) = definition.Key switch
        {
            ReportCatalog.SalesByDay => await SalesByDayAsync(request, limit, cancellationToken)
                .ConfigureAwait(false),
            ReportCatalog.SalesByCategory => await SalesByCategoryAsync(request, limit, cancellationToken)
                .ConfigureAwait(false),
            ReportCatalog.SalesByVendor => await SalesByVendorAsync(request, limit, cancellationToken)
                .ConfigureAwait(false),
            ReportCatalog.GmvVsNetRevenue => await GmvVsNetRevenueAsync(request, limit, cancellationToken)
                .ConfigureAwait(false),
            ReportCatalog.AverageOrderValue => await AverageOrderValueAsync(request, limit, cancellationToken)
                .ConfigureAwait(false),
            ReportCatalog.ConversionFunnel => await ConversionFunnelAsync(request, limit, cancellationToken)
                .ConfigureAwait(false),
            ReportCatalog.CartAbandonment => await CartAbandonmentAsync(request, limit, cancellationToken)
                .ConfigureAwait(false),
            ReportCatalog.TopSkus => await SkuPerformanceAsync(request, limit, best: true, cancellationToken)
                .ConfigureAwait(false),
            ReportCatalog.SlowSkus => await SkuPerformanceAsync(request, limit, best: false, cancellationToken)
                .ConfigureAwait(false),
            ReportCatalog.StockAgeing => await StockAgeingAsync(request, limit, cancellationToken)
                .ConfigureAwait(false),
            ReportCatalog.ReturnRateByReason => await ReturnRateByReasonAsync(request, limit, cancellationToken)
                .ConfigureAwait(false),
            ReportCatalog.SettlementSummary => await SettlementSummaryAsync(request, limit, cancellationToken)
                .ConfigureAwait(false),
            _ => await CodVsPrepaidAsync(request, limit, cancellationToken).ConfigureAwait(false),
        };

        return new ReportResult(
            definition.Key,
            definition.Name,
            definition.Columns,
            request.From,
            request.To,
            request.GroupBy,
            settings.CurrencyCode,
            rows,
            totals,
            rows.Count >= limit);
    }

    /// <summary>The order lines confirmed in the period, confined to a seller where one is named.</summary>
    private IQueryable<SaleLineFact> Lines(ReportRequest request)
    {
        var from = DateOnly.FromDateTime(request.From.UtcDateTime);
        var to = DateOnly.FromDateTime(request.To.UtcDateTime);

        var lines = context.OrderLines
            .AsNoTracking()
            .Where(line => line.ConfirmedOn >= from && line.ConfirmedOn < to);

        return request.VendorId is { } vendorId
            ? lines.Where(line => line.VendorId == vendorId)
            : lines;
    }

    /// <summary>The orders placed in the period.</summary>
    private IQueryable<OrderFact> Orders(ReportRequest request)
    {
        var from = DateOnly.FromDateTime(request.From.UtcDateTime);
        var to = DateOnly.FromDateTime(request.To.UtcDateTime);

        return context.Orders
            .AsNoTracking()
            .Where(order => order.PlacedOn >= from && order.PlacedOn < to);
    }

    /// <summary>Orders, units and value for each day.</summary>
    /// <remarks>
    /// The order count is a distinct count over the lines rather than a join to the order table, so
    /// that a day filtered to one seller reports the orders <em>that seller</em> was in rather than
    /// every order placed. Those are different numbers and a seller's dashboard wants the first.
    /// </remarks>
    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?>? Totals)>
        SalesByDayAsync(ReportRequest request, int limit, CancellationToken cancellationToken)
    {
        var grouped = await Lines(request)
            .GroupBy(line => line.ConfirmedOn)
            .Select(group => new
            {
                Day = group.Key,
                Orders = group.Select(line => line.OrderId).Distinct().Count(),
                Units = group.Sum(line => line.Quantity),
                Gross = group.Sum(line => line.LineTotal),
                Cancelled = group.Sum(line => line.CancelledAmount),
                Returned = group.Sum(line => line.ReturnedAmount),
            })
            .OrderBy(row => row.Day)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = grouped
            .Select(row => Row(
                ("day", row.Day),
                ("orders", row.Orders),
                ("units", row.Units),
                ("grossValue", row.Gross),
                ("cancelledValue", row.Cancelled),
                ("returnedValue", row.Returned),
                ("netValue", row.Gross - row.Cancelled - row.Returned)))
            .ToList();

        var totals = Row(
            ("day", null),
            ("orders", grouped.Sum(row => row.Orders)),
            ("units", grouped.Sum(row => row.Units)),
            ("grossValue", grouped.Sum(row => row.Gross)),
            ("cancelledValue", grouped.Sum(row => row.Cancelled)),
            ("returnedValue", grouped.Sum(row => row.Returned)),
            ("netValue", grouped.Sum(row => row.Gross - row.Cancelled - row.Returned)));

        return (rows, totals);
    }

    /// <summary>Units and value by category, best first.</summary>
    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?>? Totals)>
        SalesByCategoryAsync(ReportRequest request, int limit, CancellationToken cancellationToken)
    {
        var grouped = await Lines(request)
            .GroupBy(line => new { line.CategoryId, line.CategoryName })
            .Select(group => new
            {
                group.Key.CategoryId,
                group.Key.CategoryName,
                Orders = group.Select(line => line.OrderId).Distinct().Count(),
                Units = group.Sum(line => line.Quantity),
                Gross = group.Sum(line => line.LineTotal),
                Net = group.Sum(line => line.LineTotal - line.CancelledAmount - line.ReturnedAmount),
            })
            .OrderByDescending(row => row.Net)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = grouped
            .Select(row => Row(
                ("categoryId", row.CategoryId),

                // A line whose category the catalogue could not name at the time still counts; it is
                // shown as uncategorised rather than dropped, because dropping it would make the
                // report's total disagree with the sales-by-day total for the same period.
                ("categoryName", row.CategoryName ?? "Uncategorised"),
                ("orders", row.Orders),
                ("units", row.Units),
                ("grossValue", row.Gross),
                ("netValue", row.Net)))
            .ToList();

        var totals = Row(
            ("categoryId", null),
            ("categoryName", null),
            ("orders", grouped.Sum(row => row.Orders)),
            ("units", grouped.Sum(row => row.Units)),
            ("grossValue", grouped.Sum(row => row.Gross)),
            ("netValue", grouped.Sum(row => row.Net)));

        return (rows, totals);
    }

    /// <summary>Units, value, commission and return rate by seller.</summary>
    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?>? Totals)>
        SalesByVendorAsync(ReportRequest request, int limit, CancellationToken cancellationToken)
    {
        var grouped = await Lines(request)
            .GroupBy(line => line.VendorId)
            .Select(group => new
            {
                VendorId = group.Key,
                Orders = group.Select(line => line.OrderId).Distinct().Count(),
                Units = group.Sum(line => line.Quantity),
                Gross = group.Sum(line => line.LineTotal),
                Net = group.Sum(line => line.LineTotal - line.CancelledAmount - line.ReturnedAmount),
                Commission = group.Sum(line => line.CommissionAmount),
                ReturnedUnits = group.Sum(line => line.ReturnedQuantity),
            })
            .OrderByDescending(row => row.Net)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = grouped
            .Select(row => Row(
                ("vendorId", row.VendorId),
                ("orders", row.Orders),
                ("units", row.Units),
                ("grossValue", row.Gross),
                ("netValue", row.Net),
                ("commission", row.Commission),
                ("returnRate", Rate(row.ReturnedUnits, row.Units))))
            .ToList();

        var units = grouped.Sum(row => row.Units);

        var totals = Row(
            ("vendorId", null),
            ("orders", grouped.Sum(row => row.Orders)),
            ("units", units),
            ("grossValue", grouped.Sum(row => row.Gross)),
            ("netValue", grouped.Sum(row => row.Net)),
            ("commission", grouped.Sum(row => row.Commission)),
            ("returnRate", Rate(grouped.Sum(row => row.ReturnedUnits), units)));

        return (rows, totals);
    }

    /// <summary>
    /// What went through the platform against what the platform earned.
    /// </summary>
    /// <remarks>
    /// Gross merchandise value is what shoppers agreed to pay; net merchandise value is that less
    /// everything cancelled and returned; and revenue is the commission. Keeping the three apart is
    /// the point of the report — a marketplace whose GMV is growing while its take rate falls is a
    /// marketplace discounting its way to a number that does not pay any bills.
    /// </remarks>
    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?>? Totals)>
        GmvVsNetRevenueAsync(ReportRequest request, int limit, CancellationToken cancellationToken)
    {
        var grouped = await Lines(request)
            .GroupBy(line => line.ConfirmedOn)
            .Select(group => new
            {
                Day = group.Key,
                Gmv = group.Sum(line => line.LineTotal),
                Net = group.Sum(line => line.LineTotal - line.CancelledAmount - line.ReturnedAmount),
                Commission = group.Sum(line => line.CommissionAmount),
            })
            .OrderBy(row => row.Day)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = grouped
            .Select(row => Row(
                ("day", row.Day),
                ("gmv", row.Gmv),
                ("netMerchandiseValue", row.Net),
                ("commission", row.Commission),

                // The take rate is against net rather than gross, because commission is not charged
                // on goods that came back. Against gross it would fall every time returns rose, which
                // reads as the platform earning less per sale when nothing of the sort happened.
                ("takeRate", Rate(row.Commission, row.Net))))
            .ToList();

        var net = grouped.Sum(row => row.Net);

        var totals = Row(
            ("day", null),
            ("gmv", grouped.Sum(row => row.Gmv)),
            ("netMerchandiseValue", net),
            ("commission", grouped.Sum(row => row.Commission)),
            ("takeRate", Rate(grouped.Sum(row => row.Commission), net)));

        return (rows, totals);
    }

    /// <summary>What an order was worth on average, by day.</summary>
    /// <remarks>
    /// Over the order table rather than the lines, because the question is about orders. Computing it
    /// from lines would divide by the number of <em>lines</em> in a multi-seller basket and quietly
    /// report an average basket a third of its real size.
    /// </remarks>
    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?>? Totals)>
        AverageOrderValueAsync(ReportRequest request, int limit, CancellationToken cancellationToken)
    {
        var grouped = await Orders(request)
            .GroupBy(order => order.PlacedOn)
            .Select(group => new
            {
                Day = group.Key,
                Orders = group.Count(),
                Value = group.Sum(order => order.GrandTotal),
            })
            .OrderBy(row => row.Day)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = grouped
            .Select(row => Row(
                ("day", row.Day),
                ("orders", row.Orders),
                ("value", row.Value),
                ("averageOrderValue", Divide(row.Value, row.Orders))))
            .ToList();

        var orders = grouped.Sum(row => row.Orders);
        var value = grouped.Sum(row => row.Value);

        var totals = Row(
            ("day", null),
            ("orders", orders),
            ("value", value),
            ("averageOrderValue", Divide(value, orders)));

        return (rows, totals);
    }

    /// <summary>Baskets, orders and payments by day, with the rate between each pair.</summary>
    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?>? Totals)>
        ConversionFunnelAsync(ReportRequest request, int limit, CancellationToken cancellationToken)
    {
        var steps = await FunnelStepsAsync(request, limit, cancellationToken).ConfigureAwait(false);

        var rows = steps
            .Select(day => Row(
                ("day", day.Day),
                ("cartsAbandoned", day.Abandoned),
                ("cartsConverted", day.Converted),
                ("ordersPlaced", day.Placed),
                ("ordersPaid", day.Paid),

                // Basket conversion is against every basket that reached an outcome, which is the
                // only denominator this platform can observe: a basket that is neither abandoned nor
                // converted is one somebody is still shopping in.
                ("basketConversion", Rate(day.Converted, day.Converted + day.Abandoned)),
                ("paymentConversion", Rate(day.Paid, day.Placed))))
            .ToList();

        var totalConverted = steps.Sum(day => day.Converted);
        var totalPlaced = steps.Sum(day => day.Placed);

        var totals = Row(
            ("day", null),
            ("cartsAbandoned", steps.Sum(day => day.Abandoned)),
            ("cartsConverted", totalConverted),
            ("ordersPlaced", totalPlaced),
            ("ordersPaid", steps.Sum(day => day.Paid)),
            ("basketConversion", Rate(totalConverted, totalConverted + steps.Sum(day => day.Abandoned))),
            ("paymentConversion", Rate(steps.Sum(day => day.Paid), totalPlaced)));

        return (rows, totals);
    }

    /// <summary>What was left in baskets and never bought.</summary>
    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?>? Totals)>
        CartAbandonmentAsync(ReportRequest request, int limit, CancellationToken cancellationToken)
    {
        var from = DateOnly.FromDateTime(request.From.UtcDateTime);
        var to = DateOnly.FromDateTime(request.To.UtcDateTime);

        var grouped = await context.FunnelEvents
            .AsNoTracking()
            .Where(fact => fact.OccurredOn >= from && fact.OccurredOn < to)
            .Where(fact => fact.Step == FunnelStep.CartAbandoned || fact.Step == FunnelStep.CartConverted)
            .GroupBy(fact => fact.OccurredOn)
            .Select(group => new
            {
                Day = group.Key,
                Abandoned = group.Count(fact => fact.Step == FunnelStep.CartAbandoned),
                Converted = group.Count(fact => fact.Step == FunnelStep.CartConverted),
                AbandonedValue = group
                    .Where(fact => fact.Step == FunnelStep.CartAbandoned)
                    .Sum(fact => fact.Value),
            })
            .OrderBy(row => row.Day)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = grouped
            .Select(row => Row(
                ("day", row.Day),
                ("abandoned", row.Abandoned),
                ("converted", row.Converted),
                ("abandonedValue", row.AbandonedValue),
                ("abandonmentRate", Rate(row.Abandoned, row.Abandoned + row.Converted))))
            .ToList();

        var abandoned = grouped.Sum(row => row.Abandoned);
        var converted = grouped.Sum(row => row.Converted);

        var totals = Row(
            ("day", null),
            ("abandoned", abandoned),
            ("converted", converted),
            ("abandonedValue", grouped.Sum(row => row.AbandonedValue)),
            ("abandonmentRate", Rate(abandoned, abandoned + converted)));

        return (rows, totals);
    }

    /// <summary>The best- or worst-selling stock-keeping units.</summary>
    /// <remarks>
    /// One method for both reports, because they are the same query in two directions and writing it
    /// twice would be two places for the definition of "units" to drift. A slow SKU is only slow
    /// among the ones that sold at all: something with no sales in the period has no row here and is
    /// found in the stock-ageing report instead, which is the report that can see it.
    /// </remarks>
    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?>? Totals)>
        SkuPerformanceAsync(ReportRequest request, int limit, bool best, CancellationToken cancellationToken)
    {
        var query = Lines(request)
            .GroupBy(line => new { line.Sku, line.ProductName, line.CategoryName })
            .Select(group => new
            {
                group.Key.Sku,
                group.Key.ProductName,
                group.Key.CategoryName,
                Units = group.Sum(line => line.NetQuantity),
                Net = group.Sum(line => line.LineTotal - line.CancelledAmount - line.ReturnedAmount),
                ReturnedUnits = group.Sum(line => line.ReturnedQuantity),
            });

        var ordered = best
            ? query.OrderByDescending(row => row.Units).ThenByDescending(row => row.Net)
            : query.OrderBy(row => row.Units).ThenBy(row => row.Net);

        var results = await ordered.Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);

        var rows = results
            .Select(row => best
                ? Row(
                    ("sku", row.Sku),
                    ("productName", row.ProductName),
                    ("categoryName", row.CategoryName ?? "Uncategorised"),
                    ("units", row.Units),
                    ("netValue", row.Net),
                    ("returnedUnits", row.ReturnedUnits))
                : Row(
                    ("sku", row.Sku),
                    ("productName", row.ProductName),
                    ("categoryName", row.CategoryName ?? "Uncategorised"),
                    ("units", row.Units),
                    ("netValue", row.Net)))
            .ToList();

        return (rows, null);
    }

    /// <summary>
    /// How much stock is sitting, and how long it has been there.
    /// </summary>
    /// <remarks>
    /// Reads the most recent snapshot rather than the whole series, because "how much stock is old"
    /// is a question about today. Where there is no snapshot at all — a deployment whose worker has
    /// not run yet — the report is empty rather than wrong, and the admin app says so.
    /// </remarks>
    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?>? Totals)>
        StockAgeingAsync(ReportRequest request, int limit, CancellationToken cancellationToken)
    {
        var to = DateOnly.FromDateTime(request.To.UtcDateTime);

        var snapshots = context.InventoryAgeing.AsNoTracking().Where(fact => fact.SnapshotOn < to);

        if (request.VendorId is { } vendorId)
        {
            snapshots = snapshots.Where(fact => fact.VendorId == vendorId);
        }

        var latest = await snapshots
            .Select(fact => (DateOnly?)fact.SnapshotOn)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);

        if (latest is not { } snapshotOn)
        {
            return ([], null);
        }

        var onTheDay = snapshots.Where(fact => fact.SnapshotOn == snapshotOn);

        // "sku" gives the buyer the individual lines to act on; the default groups into bands, which
        // is the shape somebody reads to decide whether there is a problem at all.
        var bySku = string.Equals(request.GroupBy, "sku", StringComparison.OrdinalIgnoreCase);

        if (bySku)
        {
            var lines = await onTheDay
                .OrderByDescending(fact => fact.AgeDays ?? int.MaxValue)
                .ThenByDescending(fact => fact.QuantityOnHand)
                .Take(limit)
                .Select(fact => new
                {
                    fact.Sku,
                    fact.AgeBucket,
                    fact.QuantityOnHand,
                    fact.QuantityReserved,
                    fact.SnapshotOn,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var skuRows = lines
                .Select(line => Row(
                    ("ageBucket", line.AgeBucket),
                    ("lines", 1),
                    ("units", line.QuantityOnHand),
                    ("reserved", line.QuantityReserved),
                    ("snapshotOn", line.SnapshotOn)))
                .ToList();

            return (skuRows, null);
        }

        var grouped = await onTheDay
            .GroupBy(fact => fact.AgeBucket)
            .Select(group => new
            {
                Bucket = group.Key,
                Lines = group.Count(),
                Units = group.Sum(fact => fact.QuantityOnHand),
                Reserved = group.Sum(fact => fact.QuantityReserved),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Ordered here rather than in the database: the bands are strings and their alphabetical
        // order is not their age order — "0-29" would sort before "180+".
        var order = new[] { "0-29", "30-59", "60-89", "90-179", "180+", "Unknown" };

        var rows = grouped
            .OrderBy(row => Array.IndexOf(order, row.Bucket))
            .Select(row => Row(
                ("ageBucket", row.Bucket),
                ("lines", row.Lines),
                ("units", row.Units),
                ("reserved", row.Reserved),
                ("snapshotOn", snapshotOn)))
            .ToList();

        var totals = Row(
            ("ageBucket", null),
            ("lines", grouped.Sum(row => row.Lines)),
            ("units", grouped.Sum(row => row.Units)),
            ("reserved", grouped.Sum(row => row.Reserved)),
            ("snapshotOn", snapshotOn));

        return (rows, totals);
    }

    /// <summary>What came back, why, and what fraction of units sold that is.</summary>
    /// <remarks>
    /// The denominator for the rate is units sold in the same window, read once. That is not the
    /// perfectly correct denominator — a return in March is usually against a sale in February — and
    /// it is the one every retail team uses, because the alternative needs a cohort analysis nobody
    /// reads. Naming it "rate of units sold" rather than "return rate" is the honesty tax on that.
    /// </remarks>
    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?>? Totals)>
        ReturnRateByReasonAsync(ReportRequest request, int limit, CancellationToken cancellationToken)
    {
        var from = DateOnly.FromDateTime(request.From.UtcDateTime);
        var to = DateOnly.FromDateTime(request.To.UtcDateTime);

        var returns = context.ReturnLines
            .AsNoTracking()
            .Where(fact => fact.RequestedOn >= from && fact.RequestedOn < to);

        if (request.VendorId is { } vendorId)
        {
            returns = returns.Where(fact => fact.VendorId == vendorId);
        }

        var grouped = await returns
            .GroupBy(fact => fact.ReasonCode)
            .Select(group => new
            {
                ReasonCode = group.Key,
                Returns = group.Count(),
                Units = group.Sum(fact => fact.Quantity),
                Value = group.Sum(fact => fact.Amount),
            })
            .OrderByDescending(row => row.Units)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var unitsSold = await Lines(request)
            .SumAsync(line => (int?)line.Quantity, cancellationToken)
            .ConfigureAwait(false) ?? 0;

        var unitsReturned = grouped.Sum(row => row.Units);

        var rows = grouped
            .Select(row => Row(
                ("reasonCode", row.ReasonCode),
                ("returns", row.Returns),
                ("units", row.Units),
                ("value", row.Value),
                ("shareOfReturns", Rate(row.Units, unitsReturned)),
                ("rateOfSales", Rate(row.Units, unitsSold))))
            .ToList();

        var totals = Row(
            ("reasonCode", null),
            ("returns", grouped.Sum(row => row.Returns)),
            ("units", unitsReturned),
            ("value", grouped.Sum(row => row.Value)),
            ("shareOfReturns", unitsReturned > 0 ? 1m : 0m),
            ("rateOfSales", Rate(unitsReturned, unitsSold)));

        return (rows, totals);
    }

    /// <summary>Every settlement period closed in the window, with each deduction shown.</summary>
    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?>? Totals)>
        SettlementSummaryAsync(ReportRequest request, int limit, CancellationToken cancellationToken)
    {
        var from = DateOnly.FromDateTime(request.From.UtcDateTime);
        var to = DateOnly.FromDateTime(request.To.UtcDateTime);

        var settlements = context.Settlements
            .AsNoTracking()
            .Where(fact => fact.ClosedOn >= from && fact.ClosedOn < to);

        if (request.VendorId is { } vendorId)
        {
            settlements = settlements.Where(fact => fact.VendorId == vendorId);
        }

        var grouped = await settlements
            .GroupBy(fact => fact.VendorId)
            .Select(group => new
            {
                VendorId = group.Key,
                Periods = group.Count(),
                Gross = group.Sum(fact => fact.GrossSales),
                Commission = group.Sum(fact => fact.Commission),
                Fees = group.Sum(fact => fact.Fees),
                Tcs = group.Sum(fact => fact.Tcs),
                Tds = group.Sum(fact => fact.Tds),
                Refunds = group.Sum(fact => fact.Refunds),
                Net = group.Sum(fact => fact.NetPayable),
            })
            .OrderByDescending(row => row.Net)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = grouped
            .Select(row => Row(
                ("vendorId", row.VendorId),
                ("periods", row.Periods),
                ("grossSales", row.Gross),
                ("commission", row.Commission),
                ("fees", row.Fees),
                ("tcs", row.Tcs),
                ("tds", row.Tds),
                ("refunds", row.Refunds),
                ("netPayable", row.Net)))
            .ToList();

        var totals = Row(
            ("vendorId", null),
            ("periods", grouped.Sum(row => row.Periods)),
            ("grossSales", grouped.Sum(row => row.Gross)),
            ("commission", grouped.Sum(row => row.Commission)),
            ("fees", grouped.Sum(row => row.Fees)),
            ("tcs", grouped.Sum(row => row.Tcs)),
            ("tds", grouped.Sum(row => row.Tds)),
            ("refunds", grouped.Sum(row => row.Refunds)),
            ("netPayable", grouped.Sum(row => row.Net)));

        return (rows, totals);
    }

    /// <summary>How the split between cash on delivery and prepaid is moving.</summary>
    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?>? Totals)>
        CodVsPrepaidAsync(ReportRequest request, int limit, CancellationToken cancellationToken)
    {
        var grouped = await Orders(request)
            .GroupBy(order => order.PlacedOn)
            .Select(group => new
            {
                Day = group.Key,
                CodOrders = group.Count(order => order.IsCod),
                PrepaidOrders = group.Count(order => !order.IsCod),
                CodValue = group.Where(order => order.IsCod).Sum(order => order.GrandTotal),
                PrepaidValue = group.Where(order => !order.IsCod).Sum(order => order.GrandTotal),
            })
            .OrderBy(row => row.Day)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = grouped
            .Select(row => Row(
                ("day", row.Day),
                ("codOrders", row.CodOrders),
                ("prepaidOrders", row.PrepaidOrders),
                ("codValue", row.CodValue),
                ("prepaidValue", row.PrepaidValue),

                // By value rather than by count, because that is the number a finance team is
                // exposed to: a store where a third of orders are cash but two thirds of the money is
                // has a very different working-capital problem from one where it is the other way.
                ("codShare", Rate(row.CodValue, row.CodValue + row.PrepaidValue))))
            .ToList();

        var codValue = grouped.Sum(row => row.CodValue);
        var prepaidValue = grouped.Sum(row => row.PrepaidValue);

        var totals = Row(
            ("day", null),
            ("codOrders", grouped.Sum(row => row.CodOrders)),
            ("prepaidOrders", grouped.Sum(row => row.PrepaidOrders)),
            ("codValue", codValue),
            ("prepaidValue", prepaidValue),
            ("codShare", Rate(codValue, codValue + prepaidValue)));

        return (rows, totals);
    }

    /// <summary>The funnel's four counts per day, read in one pass.</summary>
    private async Task<List<FunnelDay>> FunnelStepsAsync(
        ReportRequest request,
        int limit,
        CancellationToken cancellationToken)
    {
        var from = DateOnly.FromDateTime(request.From.UtcDateTime);
        var to = DateOnly.FromDateTime(request.To.UtcDateTime);

        var grouped = await context.FunnelEvents
            .AsNoTracking()
            .Where(fact => fact.OccurredOn >= from && fact.OccurredOn < to)
            .GroupBy(fact => fact.OccurredOn)
            .Select(group => new FunnelDay(
                group.Key,
                group.Count(fact => fact.Step == FunnelStep.CartAbandoned),
                group.Count(fact => fact.Step == FunnelStep.CartConverted),
                group.Count(fact => fact.Step == FunnelStep.OrderPlaced),
                group.Count(fact => fact.Step == FunnelStep.OrderPaid)))
            .OrderBy(row => row.Day)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return grouped;
    }

    /// <summary>One day of the funnel.</summary>
    /// <param name="Day">The day.</param>
    /// <param name="Abandoned">Baskets left.</param>
    /// <param name="Converted">Baskets that became orders.</param>
    /// <param name="Placed">Orders placed.</param>
    /// <param name="Paid">Orders paid for.</param>
    private sealed record FunnelDay(DateOnly Day, int Abandoned, int Converted, int Placed, int Paid);

    /// <summary>Builds one row from its column values, in the report's declared order.</summary>
    /// <remarks>
    /// Ordinal comparison and insertion order preserved, because the CSV writer walks the declared
    /// columns and looks each one up by key — a case-insensitive dictionary would hide a typo in a
    /// column key until the day two of them collided.
    /// </remarks>
    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] values)
    {
        var row = new Dictionary<string, object?>(values.Length, StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            row[key] = value;
        }

        return row;
    }

    /// <summary>A ratio, or nought when there is nothing to divide by.</summary>
    /// <remarks>
    /// Nought rather than null for a rate whose denominator is empty. A day with no baskets has a
    /// conversion rate of nothing happened, and rendering an empty cell in the middle of a trend line
    /// reads as missing data rather than as a quiet day.
    /// </remarks>
    private static decimal Rate(decimal numerator, decimal denominator)
        => denominator == 0m ? 0m : Math.Round(numerator / denominator, 4, MidpointRounding.AwayFromZero);

    /// <summary>An average, or nought when there is nothing to average.</summary>
    private static decimal Divide(decimal total, int count)
        => count == 0 ? 0m : Math.Round(total / count, 2, MidpointRounding.AwayFromZero);
}
