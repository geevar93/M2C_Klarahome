using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Carts.Infrastructure;

/// <summary>
/// The platform's own limits on baskets and checkouts.
/// </summary>
/// <remarks>
/// Configuration rather than store settings, on the same split every module here draws: resource
/// limits, lifetimes and safety valves live where a shopkeeper cannot change them. The commercial
/// levers a shopper feels — whether cash on delivery is offered, up to what value, and how many
/// units one line may hold — are in the <c>commerce</c> settings section, because they are a
/// business decision and change more often than the product is deployed.
/// </remarks>
internal sealed class CartsOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Carts";

    /// <summary>The name of the cookie an anonymous basket is identified by.</summary>
    public string CartCookieName { get; set; } = "kh_cart";

    /// <summary>
    /// Whether the cart cookie carries <c>Secure</c>. Off only in local development, where the
    /// storefront is served over plain HTTP.
    /// </summary>
    public bool CartCookieSecure { get; set; } = true;

    /// <summary>
    /// How long an untouched basket lives, in days. Refreshed on every edit, so this is the gap
    /// after which a shopper who never came back loses it.
    /// </summary>
    [Range(1, 365)]
    public int CartLifetimeDays { get; set; } = 30;

    /// <summary>
    /// How long a basket sits untouched before it is treated as abandoned, in hours.
    /// </summary>
    /// <remarks>
    /// Well short of <see cref="CartLifetimeDays"/> on purpose: abandonment is a marketing signal
    /// and it is worthless once the shopper has forgotten what was in the basket, while expiry is
    /// housekeeping. Four hours is the usual first-reminder window in Indian retail.
    /// </remarks>
    [Range(1, 720)]
    public int AbandonAfterHours { get; set; } = 4;

    /// <summary>How long an abandoned basket is kept before it is retired, in days.</summary>
    [Range(1, 365)]
    public int AbandonedRetentionDays { get; set; } = 60;

    /// <summary>The most lines one basket may hold.</summary>
    /// <remarks>
    /// A cart render prices every line through the quote engine, which reads the catalogue, the
    /// price lists and every live promotion. Without a ceiling, a basket is a denial-of-service
    /// primitive with a friendly name.
    /// </remarks>
    [Range(1, 500)]
    public int MaxLines { get; set; } = 50;

    /// <summary>How long an unfinished checkout session lives, in minutes.</summary>
    /// <remarks>
    /// Short, because a session that is placing an order holds stock the moment it succeeds and
    /// because a stale address is worse than no address. It is refreshed on every step.
    /// </remarks>
    [Range(5, 1440)]
    public int CheckoutSessionMinutes { get; set; } = 60;

    /// <summary>
    /// How long stock is held between the reservation and the order, in minutes.
    /// </summary>
    /// <remarks>
    /// This is the window in which a gateway has to answer. It is deliberately short: units held
    /// for a checkout nobody completes are units nobody can buy, and Inventory's sweeper is what
    /// puts them back (docs/03-database-design.md §4.5).
    /// </remarks>
    [Range(1, 120)]
    public int PlacementHoldMinutes { get; set; } = 20;

    /// <summary>Whether the abandoned-cart sweeper runs in this host. Off in the API, on in the worker.</summary>
    public bool SweeperEnabled { get; set; }

    /// <summary>How often the sweeper polls, in seconds.</summary>
    [Range(10, 3600)]
    public int SweepIntervalSeconds { get; set; } = 300;

    /// <summary>How many baskets one sweep claims.</summary>
    [Range(1, 1000)]
    public int SweepBatchSize { get; set; } = 200;
}
