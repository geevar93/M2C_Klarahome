using KlaraHome.Contracts.Platform;

namespace KlaraHome.Modules.Reviews.Infrastructure.Features;

/// <summary>
/// The feature flags the Reviews module owns.
/// </summary>
/// <remarks>
/// Declared in code and seeded into <c>platform.feature_flags</c>, exactly as every module's are, so
/// the admin UI lists every switch that exists rather than only the ones somebody has already
/// touched. Four surfaces, four flags, and they are separate because they fail separately: a store
/// that has to take Q&amp;A down because somebody is posting phone numbers into it has no reason to
/// lose its star ratings at the same time.
/// </remarks>
internal static class ReviewFeatures
{
    /// <summary>
    /// Gates reading and writing reviews.
    /// </summary>
    /// <remarks>
    /// On. Turning it off hides every review from the storefront and refuses new ones, which is the
    /// switch to reach for on the afternoon a moderation problem is discovered and nobody is
    /// available to work the queue. The rows stay, so nothing is lost by using it.
    /// </remarks>
    public const string Reviews = "reviews.reviews";

    /// <summary>
    /// Gates product Q&amp;A.
    /// </summary>
    /// <remarks>
    /// Off. It is the only surface on the platform where somebody with no purchase behind them can
    /// publish text on a product page, and a store with nobody watching it should not have one. An
    /// operator turns it on when they have decided who answers.
    /// </remarks>
    public const string Questions = "reviews.questions";

    /// <summary>Gates the wishlist.</summary>
    /// <remarks>
    /// On. It is the one surface here with no moderation dimension at all — a saved item is private
    /// to the shopper who saved it — and the flag exists for the deployment that does not want the
    /// feature rather than for the emergency that needs it withdrawn.
    /// </remarks>
    public const string Wishlist = "reviews.wishlist";

    /// <summary>
    /// Gates back-in-stock and price-drop alerts.
    /// </summary>
    /// <remarks>
    /// Off, and it is the flag most worth being careful with. Turning it on means the store starts
    /// sending unsolicited email on stock and price movements it does not control the timing of, and
    /// a deployment with no email provider configured would collect subscriptions for months and
    /// deliver none of them. It is on only when somebody has decided both of those are true.
    /// </remarks>
    public const string StockAlerts = "reviews.stock-alerts";

    /// <summary>
    /// Gates sharing a wishlist by link.
    /// </summary>
    /// <remarks>
    /// Off, and separate from the wishlist itself because it is the only part of this module that
    /// publishes anything about a shopper to anybody else. A share token is an unauthenticated URL
    /// that reveals what one named person wants, and a store should have decided it wants that
    /// before it exists.
    /// </remarks>
    public const string WishlistSharing = "reviews.wishlist-sharing";

    /// <summary>Every flag this module declares, seeded on each deploy.</summary>
    public static IReadOnlyList<FeatureFlagDeclaration> All { get; } =
    [
        new(Reviews, true, "Show product reviews and accept new ones."),
        new(Questions, false, "Show product questions and answers, and accept new ones."),
        new(Wishlist, true, "Let shoppers save items for later."),
        new(StockAlerts, false, "Send back-in-stock and price-drop alerts."),
        new(WishlistSharing, false, "Let a shopper share a wishlist by link."),
    ];
}

/// <summary>Publishes this module's flags to the seeder, like every other module.</summary>
internal sealed class ReviewFeatureFlagSource : IFeatureFlagSource
{
    /// <inheritdoc />
    public string Module => "Reviews";

    /// <inheritdoc />
    public IReadOnlyList<FeatureFlagDeclaration> Flags => ReviewFeatures.All;
}
