using KlaraHome.Contracts.Platform;

namespace KlaraHome.Modules.Search.Infrastructure.Features;

/// <summary>
/// The feature flags the Search module owns.
/// </summary>
/// <remarks>
/// Declared in code and seeded into <c>platform.feature_flags</c>, exactly as every module's are, so
/// the admin UI lists every switch that exists rather than only the ones somebody has already
/// touched. Three of the four ship on: they gate features that work, and the switches exist for the
/// afternoon when one of them does not.
/// </remarks>
internal static class SearchFeatures
{
    /// <summary>
    /// Gates <c>GET /api/v1/store/products</c> — the faceted listing page.
    /// </summary>
    /// <remarks>
    /// The switch to reach for while a full rebuild is running against an empty index, when the
    /// storefront serving 404 for a browse page is a better answer than serving an empty one and
    /// telling every shopper the store carries nothing.
    /// </remarks>
    public const string FacetedBrowse = "search.faceted-browse";

    /// <summary>
    /// Gates <c>GET /api/v1/store/search/suggest</c>.
    /// </summary>
    /// <remarks>
    /// Separate from the browse flag because it fails separately: the suggestion box fires on every
    /// keystroke and is by far the highest-rate endpoint this platform has, so it is the first thing
    /// to switch off if the database is struggling and the last thing a shopper misses.
    /// </remarks>
    public const string Suggestions = "search.suggestions";

    /// <summary>
    /// Gates writing to the query log.
    /// </summary>
    /// <remarks>
    /// A shopper's search terms are personal data under the DPDP Act. The switch is here so a
    /// deployment with a stricter reading of its own privacy notice — or one working through a
    /// subject request — can stop recording without stopping searching.
    /// </remarks>
    public const string QueryLogging = "search.query-logging";

    /// <summary>
    /// Allows a dedicated search engine to answer, when one is configured.
    /// </summary>
    /// <remarks>
    /// Off, and it is off in every deployment that ships today because no dedicated engine is
    /// configured in any of them. It is the operator's half of the pair ADR-019 describes:
    /// <c>Search:Provider</c> is what a deployment <em>has</em>, and this is whether to use it. An
    /// engine that starts returning stale results is switched off here in seconds, and PostgreSQL
    /// answers the next query.
    /// </remarks>
    public const string ExternalEngine = "search.external-engine";

    /// <summary>Every flag this module declares, seeded on each deploy.</summary>
    public static IReadOnlyList<FeatureFlagDeclaration> All { get; } =
    [
        new(FacetedBrowse, true, "Serve the faceted product listing page."),
        new(Suggestions, true, "Serve search suggestions as a shopper types."),
        new(QueryLogging, true, "Record what shoppers search for, for merchandising."),
        new(ExternalEngine, false, "Let a configured dedicated search engine answer queries instead of PostgreSQL."),
    ];
}

/// <summary>Publishes this module's flags to the seeder, like every other module.</summary>
internal sealed class SearchFeatureFlagSource : IFeatureFlagSource
{
    /// <inheritdoc />
    public string Module => "Search";

    /// <inheritdoc />
    public IReadOnlyList<FeatureFlagDeclaration> Flags => SearchFeatures.All;
}
