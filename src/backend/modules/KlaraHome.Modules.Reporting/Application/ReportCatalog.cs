namespace KlaraHome.Modules.Reporting.Application;

/// <summary>How a column is rendered, and how it aligns.</summary>
/// <remarks>
/// Carried so the admin app and the CSV writer format a value the same way without either of them
/// guessing from the runtime type. A rupee amount and a count are both numbers and are not read the
/// same way, and a percentage that arrived as <c>0.0834</c> has to be shown as <c>8.34%</c> by
/// somebody.
/// </remarks>
internal enum ReportColumnKind
{
    /// <summary>Free text — a name, a code, a status.</summary>
    Text = 0,

    /// <summary>A whole number — a count of orders, units or rows.</summary>
    Count = 1,

    /// <summary>An amount of money, in the store's currency.</summary>
    Money = 2,

    /// <summary>A ratio between nought and one, rendered as a percentage.</summary>
    Percent = 3,

    /// <summary>A calendar date.</summary>
    Date = 4,
}

/// <summary>One column of a report.</summary>
/// <param name="Key">The property name in the row object, and the CSV header.</param>
/// <param name="Label">What a person reads at the top of the column.</param>
/// <param name="Kind">How to render it.</param>
internal sealed record ReportColumn(string Key, string Label, ReportColumnKind Kind);

/// <summary>
/// One report this platform can produce.
/// </summary>
/// <remarks>
/// The declaration and the query are kept apart on purpose. This record is data the admin app reads
/// back over the API so that the report picker, the parameter form and the column headings come off
/// the same declaration the CSV writer uses — the arrangement the CMS block schemas use at Step 20,
/// and for the same reason: a form and the thing it drives cannot drift when there is only one of
/// them.
/// </remarks>
/// <param name="Key">The stable key a caller asks for, as <c>sales-by-day</c>.</param>
/// <param name="Name">What a person calls it.</param>
/// <param name="Description">What it answers, in a sentence.</param>
/// <param name="Group">The heading the admin app files it under.</param>
/// <param name="Columns">Its columns, in the order they are rendered.</param>
/// <param name="GroupBy">
/// The groupings it accepts, or empty where the shape is fixed. The first is the default.
/// </param>
/// <param name="IsVendorScoped">
/// Whether a seller may run it for their own figures. False for the reports that are about the
/// platform rather than about a seller — a conversion funnel is the store's, and a seller has no
/// business reading how many baskets were abandoned across it.
/// </param>
internal sealed record ReportDefinition(
    string Key,
    string Name,
    string Description,
    string Group,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<string> GroupBy,
    bool IsVendorScoped);

/// <summary>
/// Every report this platform produces, declared in code.
/// </summary>
/// <remarks>
/// <para>
/// Thirteen, and they are the thirteen the step card asks for. Each one is a filtered aggregation
/// over exactly one fact table, which is what the fact tables were denormalised for: there is no
/// join anywhere in this module, and the numbers reconcile against the transactional data because
/// every fact row corresponds to one transactional row.
/// </para>
/// <para>
/// The catalogue is served over the API. An admin screen that hard-coded these would be a screen
/// that had to be redeployed to add a report, and a CSV header that was written twice would
/// eventually be written differently in the two places.
/// </para>
/// </remarks>
internal static class ReportCatalog
{
    /// <summary>Sales by day: what was sold, and what it was worth.</summary>
    public const string SalesByDay = "sales-by-day";

    /// <summary>The same, cut by category.</summary>
    public const string SalesByCategory = "sales-by-category";

    /// <summary>The same, cut by seller.</summary>
    public const string SalesByVendor = "sales-by-vendor";

    /// <summary>Gross merchandise value against what the platform actually earned.</summary>
    public const string GmvVsNetRevenue = "gmv-vs-net-revenue";

    /// <summary>What an order is worth on average, by day.</summary>
    public const string AverageOrderValue = "average-order-value";

    /// <summary>How many baskets became orders, and how many did not.</summary>
    public const string ConversionFunnel = "conversion-funnel";

    /// <summary>What was left in baskets and never bought.</summary>
    public const string CartAbandonment = "cart-abandonment";

    /// <summary>The best-selling stock-keeping units.</summary>
    public const string TopSkus = "top-skus";

    /// <summary>The ones that barely move.</summary>
    public const string SlowSkus = "slow-skus";

    /// <summary>How long the stock on the shelves has been there.</summary>
    public const string StockAgeing = "stock-ageing";

    /// <summary>What fraction of sales came back, and why.</summary>
    public const string ReturnRateByReason = "return-rate-by-reason";

    /// <summary>What each seller was paid, and everything deducted on the way.</summary>
    public const string SettlementSummary = "settlement-summary";

    /// <summary>How the split between cash on delivery and prepaid is moving.</summary>
    public const string CodVsPrepaid = "cod-vs-prepaid";

    /// <summary>Every report, in the order the admin app lists them.</summary>
    public static IReadOnlyList<ReportDefinition> All { get; } =
    [
        new(
            SalesByDay,
            "Sales by day",
            "Orders, units and value for each day in the period, net of cancellations and returns.",
            "Sales",
            [
                new("day", "Day", ReportColumnKind.Date),
                new("orders", "Orders", ReportColumnKind.Count),
                new("units", "Units", ReportColumnKind.Count),
                new("grossValue", "Gross", ReportColumnKind.Money),
                new("cancelledValue", "Cancelled", ReportColumnKind.Money),
                new("returnedValue", "Returned", ReportColumnKind.Money),
                new("netValue", "Net", ReportColumnKind.Money),
            ],
            ["day"],
            IsVendorScoped: true),

        new(
            SalesByCategory,
            "Sales by category",
            "Units and value by category for the period, best first.",
            "Sales",
            [
                new("categoryId", "Category id", ReportColumnKind.Text),
                new("categoryName", "Category", ReportColumnKind.Text),
                new("orders", "Orders", ReportColumnKind.Count),
                new("units", "Units", ReportColumnKind.Count),
                new("grossValue", "Gross", ReportColumnKind.Money),
                new("netValue", "Net", ReportColumnKind.Money),
            ],
            [],
            IsVendorScoped: true),

        new(
            SalesByVendor,
            "Sales by seller",
            "Units, value and commission by seller for the period, best first.",
            "Sales",
            [
                new("vendorId", "Seller id", ReportColumnKind.Text),
                new("orders", "Orders", ReportColumnKind.Count),
                new("units", "Units", ReportColumnKind.Count),
                new("grossValue", "Gross", ReportColumnKind.Money),
                new("netValue", "Net", ReportColumnKind.Money),
                new("commission", "Commission", ReportColumnKind.Money),
                new("returnRate", "Return rate", ReportColumnKind.Percent),
            ],
            [],
            IsVendorScoped: true),

        new(
            GmvVsNetRevenue,
            "GMV vs net revenue",
            "What went through the platform against what the platform earned, by day.",
            "Sales",
            [
                new("day", "Day", ReportColumnKind.Date),
                new("gmv", "GMV", ReportColumnKind.Money),
                new("netMerchandiseValue", "Net merchandise value", ReportColumnKind.Money),
                new("commission", "Commission earned", ReportColumnKind.Money),
                new("takeRate", "Take rate", ReportColumnKind.Percent),
            ],
            [],
            IsVendorScoped: false),

        new(
            AverageOrderValue,
            "Average order value",
            "What an order was worth on average, by day, and how many there were.",
            "Sales",
            [
                new("day", "Day", ReportColumnKind.Date),
                new("orders", "Orders", ReportColumnKind.Count),
                new("value", "Value", ReportColumnKind.Money),
                new("averageOrderValue", "AOV", ReportColumnKind.Money),
            ],
            [],
            IsVendorScoped: false),

        new(
            ConversionFunnel,
            "Conversion funnel",
            "Baskets, orders and payments by day, and the rate between each pair.",
            "Behaviour",
            [
                new("day", "Day", ReportColumnKind.Date),
                new("cartsAbandoned", "Baskets abandoned", ReportColumnKind.Count),
                new("cartsConverted", "Baskets converted", ReportColumnKind.Count),
                new("ordersPlaced", "Orders placed", ReportColumnKind.Count),
                new("ordersPaid", "Orders paid", ReportColumnKind.Count),
                new("basketConversion", "Basket conversion", ReportColumnKind.Percent),
                new("paymentConversion", "Payment conversion", ReportColumnKind.Percent),
            ],
            [],
            IsVendorScoped: false),

        new(
            CartAbandonment,
            "Cart abandonment",
            "How many baskets were left, what they were worth, and what fraction of baskets that is.",
            "Behaviour",
            [
                new("day", "Day", ReportColumnKind.Date),
                new("abandoned", "Abandoned", ReportColumnKind.Count),
                new("converted", "Converted", ReportColumnKind.Count),
                new("abandonedValue", "Value abandoned", ReportColumnKind.Money),
                new("abandonmentRate", "Abandonment rate", ReportColumnKind.Percent),
            ],
            [],
            IsVendorScoped: false),

        new(
            TopSkus,
            "Top SKUs",
            "The stock-keeping units that sold most in the period, by units.",
            "Catalogue",
            [
                new("sku", "SKU", ReportColumnKind.Text),
                new("productName", "Product", ReportColumnKind.Text),
                new("categoryName", "Category", ReportColumnKind.Text),
                new("units", "Units", ReportColumnKind.Count),
                new("netValue", "Net", ReportColumnKind.Money),
                new("returnedUnits", "Returned", ReportColumnKind.Count),
            ],
            [],
            IsVendorScoped: true),

        new(
            SlowSkus,
            "Slow SKUs",
            "The stock-keeping units that sold least in the period, by units, worst first.",
            "Catalogue",
            [
                new("sku", "SKU", ReportColumnKind.Text),
                new("productName", "Product", ReportColumnKind.Text),
                new("categoryName", "Category", ReportColumnKind.Text),
                new("units", "Units", ReportColumnKind.Count),
                new("netValue", "Net", ReportColumnKind.Money),
            ],
            [],
            IsVendorScoped: true),

        new(
            StockAgeing,
            "Stock ageing",
            "How much stock is sitting, and how long it has been there, as of the latest snapshot.",
            "Inventory",
            [
                new("ageBucket", "Age", ReportColumnKind.Text),
                new("lines", "Stock lines", ReportColumnKind.Count),
                new("units", "Units", ReportColumnKind.Count),
                new("reserved", "Reserved", ReportColumnKind.Count),
                new("snapshotOn", "As of", ReportColumnKind.Date),
            ],
            ["bucket", "sku"],
            IsVendorScoped: true),

        new(
            ReturnRateByReason,
            "Return rate by reason",
            "What came back in the period, why, and what fraction of units sold that is.",
            "Returns",
            [
                new("reasonCode", "Reason", ReportColumnKind.Text),
                new("returns", "Returns", ReportColumnKind.Count),
                new("units", "Units", ReportColumnKind.Count),
                new("value", "Value", ReportColumnKind.Money),
                new("shareOfReturns", "Share of returns", ReportColumnKind.Percent),
                new("rateOfSales", "Rate of units sold", ReportColumnKind.Percent),
            ],
            [],
            IsVendorScoped: true),

        new(
            SettlementSummary,
            "Settlement summary",
            "Every period closed in the window, with each deduction shown separately.",
            "Money",
            [
                new("vendorId", "Seller id", ReportColumnKind.Text),
                new("periods", "Periods", ReportColumnKind.Count),
                new("grossSales", "Gross sales", ReportColumnKind.Money),
                new("commission", "Commission", ReportColumnKind.Money),
                new("fees", "Fees", ReportColumnKind.Money),
                new("tcs", "TCS", ReportColumnKind.Money),
                new("tds", "TDS", ReportColumnKind.Money),
                new("refunds", "Refunds", ReportColumnKind.Money),
                new("netPayable", "Net payable", ReportColumnKind.Money),
            ],
            [],
            IsVendorScoped: true),

        new(
            CodVsPrepaid,
            "COD vs prepaid",
            "How the split between cash on delivery and prepaid is moving, by day.",
            "Money",
            [
                new("day", "Day", ReportColumnKind.Date),
                new("codOrders", "COD orders", ReportColumnKind.Count),
                new("prepaidOrders", "Prepaid orders", ReportColumnKind.Count),
                new("codValue", "COD value", ReportColumnKind.Money),
                new("prepaidValue", "Prepaid value", ReportColumnKind.Money),
                new("codShare", "COD share", ReportColumnKind.Percent),
            ],
            [],
            IsVendorScoped: false),
    ];

    /// <summary>The report with this key, or null.</summary>
    /// <param name="key">The report key.</param>
    public static ReportDefinition? Find(string? key)
        => key is null
            ? null
            : All.FirstOrDefault(report => string.Equals(report.Key, key, StringComparison.OrdinalIgnoreCase));
}
