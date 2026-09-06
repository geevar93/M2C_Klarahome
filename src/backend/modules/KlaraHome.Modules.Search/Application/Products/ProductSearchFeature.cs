using System.Diagnostics;
using System.Globalization;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Search.Domain;
using KlaraHome.Modules.Search.Infrastructure;
using KlaraHome.Modules.Search.Infrastructure.Engine;
using KlaraHome.Modules.Search.Infrastructure.Logging;
using KlaraHome.Modules.Search.Infrastructure.Query;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Search.Application.Products;

/// <summary>
/// A faceted product search (docs/04-api-specification.md §3.2).
/// </summary>
/// <remarks>
/// One query object rather than a dozen parameters, because this is the widest read surface the
/// storefront has and every one of these is optional. A request with none of them set is a browse of
/// the whole catalogue in popularity order, which is exactly what a home page wants.
/// </remarks>
/// <param name="Query">What the shopper typed, or null for a browse.</param>
/// <param name="CategoryId">Browse this category and everything beneath it.</param>
/// <param name="BrandIds">Only these brands.</param>
/// <param name="VendorIds">Only these sellers.</param>
/// <param name="MinPrice">At or above this price.</param>
/// <param name="MaxPrice">At or below it.</param>
/// <param name="MinRating">At or above this review score.</param>
/// <param name="MinDiscount">At or above this discount percentage.</param>
/// <param name="InStock">Only what can be bought right now.</param>
/// <param name="Attributes">Catalogue attributes to narrow by, keyed by attribute code.</param>
/// <param name="Sort">The order, one of <see cref="SearchSorts"/>.</param>
/// <param name="Cursor">The page to return, from a previous response.</param>
/// <param name="Size">How many results.</param>
internal sealed record SearchProductsQuery(
    string? Query,
    Guid? CategoryId,
    IReadOnlyList<Guid>? BrandIds,
    IReadOnlyList<Guid>? VendorIds,
    decimal? MinPrice,
    decimal? MaxPrice,
    decimal? MinRating,
    int? MinDiscount,
    bool? InStock,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Attributes,
    string? Sort,
    string? Cursor,
    int? Size) : IQuery<ProductSearchResponse>;

/// <summary>
/// Runs a search: parses it, asks the engine, retries fuzzily when nothing matched, and records it.
/// </summary>
/// <remarks>
/// <para>
/// The orchestration, deliberately kept out of the engine. Reading the store's policy, applying its
/// vocabulary, deciding to try again more loosely and writing the query log are all the same
/// whichever engine answers — and putting them here is what makes a second engine one class rather
/// than a second copy of this module.
/// </para>
/// <para>
/// Facets are computed on the first page only. They do not change as a shopper pages through a
/// result set, and recomputing eight aggregates for page four would make paging the most expensive
/// thing the storefront does. The second page's response carries the same facets the first one did,
/// because the storefront still has them.
/// </para>
/// <para>
/// The fuzzy retry runs only when the exact pass matched nothing, only when there was text to be
/// wrong about, and only on the first page. It is a correction, not a search mode: a shopper on
/// page three of a result set has already found what they were looking for.
/// </para>
/// </remarks>
/// <param name="engines">Chooses the engine that answers.</param>
/// <param name="settings">Supplies the store's ranking and filtering policy.</param>
/// <param name="vocabulary">Supplies the store's synonyms and stop words.</param>
/// <param name="recorder">Records the query, for merchandising.</param>
/// <param name="options">Supplies the page and filter ceilings.</param>
internal sealed class SearchProductsQueryHandler(
    SearchEngineRegistry engines,
    IStoreSettings settings,
    SearchVocabularyReader vocabulary,
    SearchQueryRecorder recorder,
    IOptions<SearchOptions> options)
    : IQueryHandler<SearchProductsQuery, ProductSearchResponse>
{
    public async Task<Result<ProductSearchResponse>> HandleAsync(
        SearchProductsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var policy = await settings.GetAsync<SearchSettings>(cancellationToken).ConfigureAwait(false);

        // A query below the minimum is refused rather than answered with an empty page. The
        // storefront has to be able to tell "keep typing" from "nothing matched", and an empty
        // result set says the second.
        if (query.Query is { } typed
            && !string.IsNullOrWhiteSpace(typed)
            && typed.Trim().Length < policy.MinimumQueryLength)
        {
            return Result.Failure<ProductSearchResponse>(SearchErrors.QueryTooShort(policy.MinimumQueryLength));
        }

        var sort = query.Sort ?? policy.DefaultSort;

        if (!SearchSorts.Contains(sort))
        {
            return Result.Failure<ProductSearchResponse>(SearchErrors.UnknownSort(SearchSorts.All));
        }

        if (!TryReadCursor(query.Cursor, out var cursor))
        {
            return Result.Failure<ProductSearchResponse>(SearchErrors.InvalidCursor);
        }

        var words = await vocabulary.GetAsync(cancellationToken).ConfigureAwait(false);

        var parsed = SearchTextNormalizer.Parse(
            query.Query,
            words,
            policy.MaxQueryLength,
            prefixLastTerm: false);

        var request = Build(query, parsed, sort, cursor, policy);
        var engine = await engines.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();

        var results = await engine.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        var corrected = false;

        if (results.Items.Count == 0
            && parsed.HasText
            && policy.EnableFuzzyFallback
            && cursor is null)
        {
            var retry = request with { Fuzzy = true };

            results = await engine.SearchAsync(retry, cancellationToken).ConfigureAwait(false);
            corrected = results.Items.Count > 0;
        }

        stopwatch.Stop();

        var token = await recorder
            .RecordAsync(
                parsed,
                SearchQuerySources.Search,
                request,
                results.Total,
                stopwatch.ElapsedMilliseconds,
                policy,
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new ProductSearchResponse(
            results.Items,
            results.Total,
            Encode(results.NextCursor),
            request.Size,
            results.Facets,
            parsed.HasText ? parsed.Normalised : null,
            corrected,
            token));
    }

    /// <summary>Turns the query object into the request an engine understands.</summary>
    /// <param name="query">What was asked.</param>
    /// <param name="parsed">The query after the store's vocabulary.</param>
    /// <param name="sort">The validated sort.</param>
    /// <param name="cursor">Where the page starts.</param>
    /// <param name="policy">The store's ranking and filtering policy.</param>
    private SearchRequest Build(
        SearchProductsQuery query,
        NormalizedQuery parsed,
        string sort,
        SearchCursor? cursor,
        SearchSettings policy)
    {
        var limits = options.Value;

        // Every collection is bounded before it reaches the engine. A filter naming two hundred
        // brands is not a shopper, and each value is an element of an array the database matches
        // against; the extra values are dropped rather than refused, so the search still answers.
        var brands = Bound(query.BrandIds, limits.MaxFilterValues);
        var vendors = Bound(query.VendorIds, limits.MaxFilterValues);

        var attributes = (query.Attributes ?? new Dictionary<string, IReadOnlyList<string>>())
            .Where(pair => pair.Value is { Count: > 0 })
            .Take(limits.MaxAttributeFilters)
            .Select(pair => new AttributeFilter(
                SearchTextNormalizer.Fold(pair.Key),
                Bound(pair.Value, limits.MaxFilterValues)))
            .Where(attribute => attribute.Code.Length > 0)
            .ToList();

        return new SearchRequest(
            parsed,
            query.CategoryId,
            brands,
            vendors,
            query.MinPrice,
            query.MaxPrice,
            query.MinRating,
            query.MinDiscount,
            query.InStock ?? policy.HideUnavailable,
            attributes,
            sort,
            cursor,
            Math.Min(Cursor.NormalizeSize(query.Size), limits.MaxPageSize),

            // The first page only. The facets do not change as a shopper pages through a result set,
            // and the storefront still has the ones it was given.
            IncludeFacets: cursor is null,
            Fuzzy: false,
            policy);
    }

    /// <summary>Trims a filter to the most values the engine will be asked to match.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="values">What the caller named, or null.</param>
    /// <param name="limit">The ceiling.</param>
    private static IReadOnlyList<TValue> Bound<TValue>(IReadOnlyList<TValue>? values, int limit)
        => values is null or { Count: 0 } ? [] : [.. values.Distinct().Take(limit)];

    /// <summary>
    /// Reads a page cursor back into the sort value and id it carries.
    /// </summary>
    /// <remarks>
    /// A malformed cursor is a client error and never an exception: a caller may have kept a stale
    /// one, and the answer is to say so and let them start again from the first page.
    /// </remarks>
    /// <param name="token">The cursor, or null for the first page.</param>
    /// <param name="cursor">Where the page starts.</param>
    private static bool TryReadCursor(string? token, out SearchCursor? cursor)
    {
        cursor = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        if (!Cursor.TryDecode(token, out var key))
        {
            return false;
        }

        var separator = key.IndexOf('|', StringComparison.Ordinal);

        if (separator <= 0
            || !decimal.TryParse(key[..separator], CultureInfo.InvariantCulture, out var sortValue)
            || !Guid.TryParse(key[(separator + 1)..], out var id))
        {
            return false;
        }

        cursor = new SearchCursor(sortValue, id);
        return true;
    }

    /// <summary>Encodes the cursor for the following page, or null when there is none.</summary>
    /// <param name="cursor">Where the following page starts.</param>
    private static string? Encode(SearchCursor? cursor)
        => cursor is null
            ? null
            : Cursor.Encode(string.Create(
                CultureInfo.InvariantCulture,
                $"{cursor.SortValue}|{cursor.Id}"));
}
