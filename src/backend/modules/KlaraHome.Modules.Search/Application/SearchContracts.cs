namespace KlaraHome.Modules.Search.Application;

/// <summary>One result, as a card on a listing page renders it.</summary>
/// <remarks>
/// Everything here comes off one row of the projection. That is the whole reason the projection
/// exists: assembling this from the catalogue, the price lists, the stock table and the seller
/// directory would be a five-schema join per card, forty-eight times a page.
/// </remarks>
/// <param name="VariantId">The sellable thing. This is what a click reports.</param>
/// <param name="ProductId">The product it belongs to.</param>
/// <param name="ListingId">The offer that won the buy box. This is what add-to-cart takes.</param>
/// <param name="Slug">The product's URL segment.</param>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="Name">The full display name.</param>
/// <param name="BrandId">The brand, or null.</param>
/// <param name="BrandName">Its name.</param>
/// <param name="CategoryId">The category it browses under.</param>
/// <param name="CategoryName">Its name.</param>
/// <param name="VendorId">The winning seller.</param>
/// <param name="VendorName">Their public name.</param>
/// <param name="Mrp">The declared MRP, always displayed. Statutory in India.</param>
/// <param name="Price">What the shopper pays per unit, inclusive of GST.</param>
/// <param name="CurrencyCode">ISO 4217 code both amounts are in.</param>
/// <param name="DiscountPercent">How far below MRP that is, for the badge.</param>
/// <param name="RatingAverage">The product's average review score, or null.</param>
/// <param name="RatingCount">How many reviews that is over.</param>
/// <param name="IsAvailable">Whether it can be bought right now.</param>
/// <param name="IsCodAllowed">Whether the winning seller accepts cash on delivery.</param>
/// <param name="OfferCount">How many sellers offer it, for the "N offers" line.</param>
/// <param name="ImageFileId">The picture.</param>
internal sealed record ProductSearchItem(
    Guid VariantId,
    Guid ProductId,
    Guid ListingId,
    string Slug,
    string Sku,
    string Name,
    Guid? BrandId,
    string? BrandName,
    Guid CategoryId,
    string CategoryName,
    Guid VendorId,
    string VendorName,
    decimal Mrp,
    decimal Price,
    string CurrencyCode,
    int DiscountPercent,
    decimal? RatingAverage,
    int RatingCount,
    bool IsAvailable,
    bool IsCodAllowed,
    int OfferCount,
    Guid? ImageFileId);

/// <summary>One value of one facet, and how many results carry it.</summary>
/// <param name="Value">The machine value a filter is expressed in.</param>
/// <param name="Label">What a shopper reads.</param>
/// <param name="Count">How many results have it.</param>
/// <param name="From">The lower bound, for a banded facet such as price. Null otherwise.</param>
/// <param name="To">The upper bound, exclusive, or null for the open-ended last band.</param>
internal sealed record FacetValue(
    string Value,
    string Label,
    long Count,
    decimal? From = null,
    decimal? To = null);

/// <summary>One dimension a result set can be narrowed by.</summary>
/// <param name="Key">
/// The filter key, as it appears in the query string: <c>brand</c>, <c>price</c>, or
/// <c>attr.color</c> for a catalogue attribute.
/// </param>
/// <param name="Label">The dimension's shopper-facing name.</param>
/// <param name="Values">Its values, most-populated first.</param>
internal sealed record FacetGroup(string Key, string Label, IReadOnlyList<FacetValue> Values);

/// <summary>A page of results, with the facets that narrow it.</summary>
/// <remarks>
/// <para>
/// <paramref name="QueryToken"/> is what makes click-through measurable. It names the query-log row
/// this page was recorded as, and it carries the row's partition key as well as its id — a click
/// arriving an hour later has to find one row in a partitioned table, and a bare id would mean
/// searching every month the log holds.
/// </para>
/// <para>
/// <paramref name="Corrected"/> says the exact search found nothing and these results came from the
/// fuzzy pass. The storefront renders "showing results for…", which is the difference between a
/// helpful correction and results that look wrong.
/// </para>
/// </remarks>
/// <param name="Items">The results, in the requested order.</param>
/// <param name="Total">How many matched in total.</param>
/// <param name="NextCursor">The token for the following page, or null when this is the last.</param>
/// <param name="Size">How many were asked for.</param>
/// <param name="Facets">The dimensions this result set can be narrowed by.</param>
/// <param name="Query">The query as it was interpreted, after stop words and synonyms.</param>
/// <param name="Corrected">Whether these results came from the fuzzy fallback.</param>
/// <param name="QueryToken">The handle a click reports against, or null when nothing was logged.</param>
internal sealed record ProductSearchResponse(
    IReadOnlyList<ProductSearchItem> Items,
    long Total,
    string? NextCursor,
    int Size,
    IReadOnlyList<FacetGroup> Facets,
    string? Query,
    bool Corrected,
    string? QueryToken);

/// <summary>What kind of thing a suggestion points at.</summary>
internal static class SuggestionKinds
{
    /// <summary>A product. Clicking it goes straight to the product page.</summary>
    public const string Product = "product";

    /// <summary>A brand. Clicking it browses that brand.</summary>
    public const string Brand = "brand";

    /// <summary>A category. Clicking it browses that category.</summary>
    public const string Category = "category";

    /// <summary>Something other shoppers searched for. Clicking it runs that search.</summary>
    public const string Query = "query";
}

/// <summary>One line of the autocomplete box.</summary>
/// <param name="Kind">What it points at: one of <see cref="SuggestionKinds"/>.</param>
/// <param name="Text">What is shown, and what a query suggestion searches for.</param>
/// <param name="Slug">The URL segment to navigate to, for a product, brand or category.</param>
/// <param name="Id">The thing's id, where it has one.</param>
/// <param name="ImageFileId">A thumbnail, for a product suggestion.</param>
/// <param name="Price">The product's price, so the box can show it without a second request.</param>
internal sealed record SearchSuggestion(
    string Kind,
    string Text,
    string? Slug,
    Guid? Id,
    Guid? ImageFileId,
    decimal? Price);

/// <summary>The autocomplete box's answer.</summary>
/// <param name="Query">What was asked, as interpreted.</param>
/// <param name="Suggestions">What to show, best first.</param>
internal sealed record SuggestionResponse(string Query, IReadOnlyList<SearchSuggestion> Suggestions);

/// <summary>A synonym rule, as the admin screen lists it.</summary>
/// <param name="Id">The rule.</param>
/// <param name="Term">The word a shopper types.</param>
/// <param name="Expansions">What it is also taken to mean.</param>
/// <param name="IsBidirectional">Whether the expansions expand back to the term.</param>
/// <param name="IsActive">Whether it is applied.</param>
/// <param name="Note">Why it exists.</param>
/// <param name="CreatedAt">When it was added.</param>
/// <param name="UpdatedAt">When it was last edited.</param>
internal sealed record SynonymResponse(
    Guid Id,
    string Term,
    IReadOnlyList<string> Expansions,
    bool IsBidirectional,
    bool IsActive,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A stop word, as the admin screen lists it.</summary>
/// <param name="Id">The rule.</param>
/// <param name="Word">The word this store ignores.</param>
/// <param name="IsActive">Whether it is applied.</param>
/// <param name="CreatedAt">When it was added.</param>
internal sealed record StopWordResponse(Guid Id, string Word, bool IsActive, DateTimeOffset CreatedAt);

/// <summary>One recorded query, as the merchandising report lists it.</summary>
/// <param name="Query">What was typed.</param>
/// <param name="NormalisedQuery">What it became.</param>
/// <param name="Count">How many times it was asked over the window.</param>
/// <param name="ZeroResultCount">How many of those returned nothing.</param>
/// <param name="AverageResultCount">The mean number of results.</param>
/// <param name="ClickThroughRate">
/// The share of those searches that ended in a click, from 0 to 1. The number that says whether the
/// results were any good, as distinct from whether there were any.
/// </param>
/// <param name="AverageClickPosition">
/// Where the clicks landed, on average. A query whose clicks are all on the eighth result is a
/// query whose ranking is wrong, and nothing but this number says so.
/// </param>
/// <param name="LastSearchedAt">When it was last asked.</param>
internal sealed record SearchQueryReportRow(
    string Query,
    string NormalisedQuery,
    long Count,
    long ZeroResultCount,
    double AverageResultCount,
    double ClickThroughRate,
    double? AverageClickPosition,
    DateTimeOffset LastSearchedAt);

/// <summary>What the index looks like right now.</summary>
/// <param name="Engine">Which engine is answering queries.</param>
/// <param name="IsExternalEngineEnabled">Whether the dedicated-engine flag is on.</param>
/// <param name="DocumentCount">How many variants are indexed.</param>
/// <param name="ActiveCount">How many of those may appear in results.</param>
/// <param name="AvailableCount">How many are in stock.</param>
/// <param name="StaleCount">How many have not been rebuilt within the configured window.</param>
/// <param name="OldestIndexedAt">The least recently rebuilt row, or null when the index is empty.</param>
/// <param name="SynonymCount">How many synonym rules are in force.</param>
/// <param name="StopWordCount">How many stop words are in force.</param>
internal sealed record SearchIndexStatus(
    string Engine,
    bool IsExternalEngineEnabled,
    long DocumentCount,
    long ActiveCount,
    long AvailableCount,
    long StaleCount,
    DateTimeOffset? OldestIndexedAt,
    int SynonymCount,
    int StopWordCount);

/// <summary>What a rebuild did.</summary>
/// <param name="VariantsWalked">How many variants were read from the catalogue.</param>
/// <param name="RowsWritten">How many index rows were created or refreshed.</param>
/// <param name="RowsRetired">How many were taken out of results because no offer survived.</param>
/// <param name="IsComplete">Whether the walk reached the end of the catalogue.</param>
/// <param name="ResumeAfterVariantId">Where to resume, when it did not.</param>
internal sealed record ReindexResponse(
    int VariantsWalked,
    int RowsWritten,
    int RowsRetired,
    bool IsComplete,
    Guid? ResumeAfterVariantId);
