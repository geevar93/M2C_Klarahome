using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Orders.Infrastructure;

/// <summary>
/// The platform's own limits on orders.
/// </summary>
/// <remarks>
/// Configuration rather than store settings, on the same split every module here draws: number
/// formats, sweeper cadences and resource limits live where a shopkeeper cannot change them. The
/// commercial levers a shopper feels — the return window, the cancellation window, whether cash on
/// delivery is offered — are in the <c>commerce</c> settings section, because they are business
/// decisions and change more often than the product is deployed.
/// </remarks>
internal sealed class OrdersOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Orders";

    /// <summary>
    /// What every order number starts with, as <c>KH-2609-000184</c>.
    /// </summary>
    /// <remarks>
    /// Configuration rather than a constant because it is the tenant's brand on every parcel, every
    /// invoice and every support call, and a second business onboarded onto this product must not
    /// have to quote Klara Home's initials (docs/01-architecture.md §8).
    /// </remarks>
    [Required]
    [StringLength(8, MinimumLength = 1)]
    public string OrderNumberPrefix { get; set; } = "KH";

    /// <summary>
    /// How many digits the sequence part of an order number is padded to.
    /// </summary>
    /// <remarks>
    /// Six, and the sequence restarts every month, so it accommodates a million orders in a month
    /// before it widens. Widening it is harmless — the numbers stay unique and stay sortable within
    /// a month — but it would make two months of numbers look different, so it is set once.
    /// </remarks>
    [Range(4, 10)]
    public int OrderNumberDigits { get; set; } = 6;

    /// <summary>How many digits an invoice number is padded to, within a seller's financial year.</summary>
    [Range(4, 10)]
    public int InvoiceNumberDigits { get; set; } = 5;

    /// <summary>
    /// The return window used when neither the product nor the store settings name one, in days.
    /// </summary>
    /// <remarks>
    /// A floor, not the answer. The window actually applied is the product's own when it has one and
    /// the store's <c>commerce.returnWindowDays</c> otherwise; this is what is used when a settings
    /// read fails, so an order still gets a defensible window rather than none.
    /// </remarks>
    [Range(0, 90)]
    public int FallbackReturnWindowDays { get; set; } = 7;

    /// <summary>
    /// How long an unpaid order is left before it is cancelled, in minutes.
    /// </summary>
    /// <remarks>
    /// Longer than the stock hold it inherits from the checkout, and deliberately: the hold lapsing
    /// puts the units back on sale, and cancelling the order immediately afterwards would deny a
    /// shopper whose bank took four minutes to send an OTP. Short enough that a dead order does not
    /// sit in somebody's account for a day.
    /// </remarks>
    [Range(5, 1440)]
    public int UnpaidOrderTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// Whether an invoice is raised automatically when a sub-order is packed.
    /// </summary>
    /// <remarks>
    /// On. A tax invoice accompanies the goods, so the moment it is needed is the moment the parcel
    /// is closed — and raising it any earlier, at confirmation, would mean a credit note for every
    /// seller who later finds they cannot fulfil.
    /// </remarks>
    public bool AutoInvoiceOnPacked { get; set; } = true;

    /// <summary>The largest page any order list will serve.</summary>
    [Range(1, 200)]
    public int MaxPageSize { get; set; } = 50;

    /// <summary>Whether the lifecycle sweeper runs in this host. Off in the API, on in the worker.</summary>
    public bool SweeperEnabled { get; set; }

    /// <summary>How often the sweeper polls, in seconds.</summary>
    [Range(30, 3600)]
    public int SweepIntervalSeconds { get; set; } = 300;

    /// <summary>How many sub-orders one sweep claims.</summary>
    [Range(1, 1000)]
    public int SweepBatchSize { get; set; } = 200;
}
