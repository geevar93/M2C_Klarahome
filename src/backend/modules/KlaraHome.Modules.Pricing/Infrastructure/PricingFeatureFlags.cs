using KlaraHome.Contracts.Platform;

namespace KlaraHome.Modules.Pricing.Infrastructure;

/// <summary>
/// The feature flags this module reads (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// Declared through <see cref="IFeatureFlagSource"/> rather than written to
/// <c>platform.feature_flags</c> directly, because no module may write to another's schema
/// (docs/01-architecture.md §2.1). Declaring them is what makes the admin UI list every switch that
/// exists rather than only the ones somebody has already touched — the same arrangement the
/// Identity module uses for its four.
/// </remarks>
internal sealed class PricingFeatureFlags : IFeatureFlagSource
{
    /// <summary>
    /// Whether the store-credit wallet is available at all.
    /// </summary>
    /// <remarks>
    /// Off by shipping default. The tables exist in every deployment because a loyalty programme
    /// designed later is a migration on live data; the switch exists because most stores will not
    /// want one on day one, and a wallet nobody has decided the rules for is a liability with no
    /// owner. Both halves of that are deliberate (Step 12 deliverables).
    /// </remarks>
    public const string StoreCredit = "pricing.store-credit";

    /// <summary>
    /// Whether coupon codes may be applied at all.
    /// </summary>
    /// <remarks>
    /// On. It exists as a switch rather than a setting because the case it is for is an incident —
    /// a leaked code being shared publicly — and an operator needs to stop it without a deploy and
    /// without deactivating campaigns one at a time.
    /// </remarks>
    public const string Coupons = "pricing.coupons";

    /// <inheritdoc />
    public string Module => "Pricing";

    /// <inheritdoc />
    public IReadOnlyList<FeatureFlagDeclaration> Flags { get; } =
    [
        new(
            StoreCredit,
            Enabled: false,
            "Store credit and loyalty. Off until the business has decided the accrual rules."),
        new(
            Coupons,
            Enabled: true,
            "Coupon codes. Turn off to stop every code at once during an abuse incident."),
    ];
}
