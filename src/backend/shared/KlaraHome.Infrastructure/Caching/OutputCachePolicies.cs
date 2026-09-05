namespace KlaraHome.Infrastructure.Caching;

/// <summary>
/// Named output-cache policies. Only genuinely public, anonymous reads are cacheable; anything
/// authenticated is <c>no-store</c> (docs/04-api-specification.md §1).
/// </summary>
public static class OutputCachePolicies
{
    /// <summary>Catalog and content reads: short TTL, varied by the query string.</summary>
    public const string PublicRead = "public-read";

    /// <summary>Rarely-changing reference data (categories, PIN serviceability tables).</summary>
    public const string ReferenceData = "reference-data";
}
