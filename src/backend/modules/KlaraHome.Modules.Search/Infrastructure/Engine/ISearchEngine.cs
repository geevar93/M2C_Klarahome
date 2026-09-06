using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Search.Application;
using KlaraHome.Modules.Search.Infrastructure.Query;

namespace KlaraHome.Modules.Search.Infrastructure.Engine;

/// <summary>One attribute the shopper has narrowed by.</summary>
/// <param name="Code">The attribute's code, as <c>color</c>.</param>
/// <param name="Values">The values they chose. Any of them matches.</param>
internal sealed record AttributeFilter(string Code, IReadOnlyList<string> Values);

/// <summary>
/// Where a page of results starts.
/// </summary>
/// <remarks>
/// Keyset rather than offset, as every paged endpoint on this platform is: a page fetched while the
/// index is being rebuilt must not repeat or skip a row, and <c>OFFSET 4800</c> on the fiftieth page
/// of a search is a sort of the whole result set to throw nearly all of it away.
/// </remarks>
/// <param name="SortValue">The sort key of the last row of the previous page.</param>
/// <param name="Id">Its id, which breaks the tie when two rows sort equally.</param>
internal sealed record SearchCursor(decimal SortValue, Guid Id);

/// <summary>
/// A parsed search, with everything an engine needs and nothing it has to look up.
/// </summary>
/// <remarks>
/// Deliberately complete. The engine does not read settings, does not load the vocabulary and does
/// not know what a permission is — which is what makes swapping PostgreSQL for a dedicated engine a
/// matter of writing one class rather than reproducing half a module.
/// </remarks>
/// <param name="Query">The query after the store's stop words and synonyms.</param>
/// <param name="CategoryId">Browse this category and everything beneath it, or null.</param>
/// <param name="BrandIds">Only these brands, or empty for all.</param>
/// <param name="VendorIds">Only these sellers, or empty for all.</param>
/// <param name="MinPrice">At or above this price.</param>
/// <param name="MaxPrice">At or below it.</param>
/// <param name="MinRating">At or above this review score.</param>
/// <param name="MinDiscount">At or above this discount percentage.</param>
/// <param name="InStockOnly">Only what can be bought right now.</param>
/// <param name="Attributes">The catalogue attributes narrowed by.</param>
/// <param name="Sort">The order, one of <see cref="SearchSorts"/>.</param>
/// <param name="Cursor">Where the page starts, or null for the first.</param>
/// <param name="Size">How many results to return.</param>
/// <param name="IncludeFacets">
/// Whether to compute facet counts. False for the second page onwards: the facets do not change as a
/// shopper pages, and recomputing eight aggregates per page is the most expensive thing this module
/// does.
/// </param>
/// <param name="Fuzzy">Whether this is the trigram fallback pass rather than the exact one.</param>
/// <param name="Settings">The store's ranking policy.</param>
internal sealed record SearchRequest(
    NormalizedQuery Query,
    Guid? CategoryId,
    IReadOnlyList<Guid> BrandIds,
    IReadOnlyList<Guid> VendorIds,
    decimal? MinPrice,
    decimal? MaxPrice,
    decimal? MinRating,
    int? MinDiscount,
    bool InStockOnly,
    IReadOnlyList<AttributeFilter> Attributes,
    string Sort,
    SearchCursor? Cursor,
    int Size,
    bool IncludeFacets,
    bool Fuzzy,
    SearchSettings Settings)
{
    /// <summary>Whether any narrowing at all has been applied.</summary>
    /// <remarks>
    /// Read by the query log: a search that found nothing with three filters applied is a filter
    /// combination nothing satisfies, and a search that found nothing with none is a product the
    /// store does not carry. They are different problems and a buying team acts on them differently.
    /// </remarks>
    public bool HasFilters
        => CategoryId is not null
           || BrandIds.Count > 0
           || VendorIds.Count > 0
           || MinPrice is not null
           || MaxPrice is not null
           || MinRating is not null
           || MinDiscount is not null
           || InStockOnly
           || Attributes.Count > 0;
}

/// <summary>What an engine answered with.</summary>
/// <param name="Items">The page of results, in the requested order.</param>
/// <param name="Total">How many matched in total.</param>
/// <param name="Facets">The dimensions the result set can be narrowed by. Empty when not asked for.</param>
/// <param name="NextCursor">Where the following page starts, or null when this was the last.</param>
internal sealed record SearchResults(
    IReadOnlyList<ProductSearchItem> Items,
    long Total,
    IReadOnlyList<FacetGroup> Facets,
    SearchCursor? NextCursor);

/// <summary>
/// What answers a query.
/// </summary>
/// <remarks>
/// <para>
/// The seam ADR-007 promised and ADR-019 defines. PostgreSQL full text is the engine this platform
/// ships with and is enough for a catalogue of the size this business plans; a dedicated engine is a
/// second implementation of this interface, a configuration value and a feature flag, and nothing
/// outside this module would know it had happened.
/// </para>
/// <para>
/// Deliberately shaped around what a storefront asks rather than around what an engine offers. There
/// is no method here that takes a raw query string in an engine's own dialect, because the moment
/// one exists the storefront starts sending Lucene syntax and the seam stops being a seam.
/// </para>
/// <para>
/// Nothing here writes. Keeping an engine's index current is the projection's job and it happens
/// through the event pipeline, so an engine that fell behind can be rebuilt without a query path
/// that knows how to do it.
/// </para>
/// </remarks>
internal interface ISearchEngine
{
    /// <summary>
    /// The key that selects this engine, matched against <c>Search:Provider</c>.
    /// </summary>
    /// <remarks>
    /// Adapters are keyed by the engine they are, exactly as the courier adapters are (ADR-018), so
    /// that switching engine is a configuration value rather than a deployment of different code.
    /// </remarks>
    string Key { get; }

    /// <summary>
    /// Whether this engine has everything it needs to answer.
    /// </summary>
    /// <remarks>
    /// An engine that is named but not configured says so here rather than failing on the first
    /// query, and the registry falls back to PostgreSQL. A store whose search returns a 502 because
    /// somebody typed a provider name into an environment file is a worse outcome than a store whose
    /// search is merely not as fast as it could be.
    /// </remarks>
    bool IsConfigured { get; }

    /// <summary>Answers a search.</summary>
    /// <param name="request">The parsed query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SearchResults> SearchAsync(SearchRequest request, CancellationToken cancellationToken);

    /// <summary>Answers the autocomplete box.</summary>
    /// <param name="query">The partial query, parsed with the last term prefix-matched.</param>
    /// <param name="limit">The most suggestions to return.</param>
    /// <param name="settings">The store's ranking policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(
        NormalizedQuery query,
        int limit,
        SearchSettings settings,
        CancellationToken cancellationToken);
}
