using KlaraHome.Infrastructure.Caching;
using KlaraHome.Infrastructure.Features;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Search.Application;
using KlaraHome.Modules.Search.Application.Products;
using KlaraHome.Modules.Search.Infrastructure.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Search.Endpoints;

/// <summary>The body of a click report.</summary>
/// <param name="QueryToken">The handle the search answered with.</param>
/// <param name="Position">Which result was clicked, one-based.</param>
/// <param name="VariantId">What was clicked.</param>
internal sealed record SearchClickBody(string? QueryToken, int Position, Guid VariantId);

/// <summary>
/// What a shopper can search (docs/04-api-specification.md §3.2).
/// </summary>
/// <remarks>
/// <para>
/// Anonymous, and cacheable at the edge because nothing here is scoped to a caller. <c>GET
/// /store/products</c> is the faceted listing page the API specification assigns to this module
/// rather than to the catalogue: it reads a denormalised projection with its facet counts, and
/// serving it from the catalogue schema would be a five-table join per card and no facets at all.
/// </para>
/// <para>
/// The attribute filters do not appear as named parameters, and cannot: they are
/// <c>?attr.color=beige&amp;attr.size=m</c>, and which attributes exist is a merchandising decision
/// rather than a compile-time one. They are read off the query string by prefix, which is the one
/// place in this API where that is the right answer.
/// </para>
/// <para>
/// The click report is a <c>POST</c> that writes and answers 204. It is deliberately fire-and-forget
/// from the storefront's point of view: a shopper navigating to a product page must not wait for an
/// analytics write, and a handle the log no longer holds succeeds silently.
/// </para>
/// </remarks>
internal static class StoreSearchEndpoints
{
    /// <summary>The query-string prefix an attribute filter carries.</summary>
    private const string AttributePrefix = "attr.";

    /// <summary>Maps the storefront search surface beneath <c>/store</c>.</summary>
    /// <param name="store">The <c>/store</c> group.</param>
    public static IEndpointRouteBuilder MapStoreSearchEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var group = store
            .MapGroup(string.Empty)
            .WithTags("Search")
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead);

        group.MapGet("/products", async (HttpContext context, IDispatcher dispatcher) =>
            {
                var result = await dispatcher
                    .QueryAsync(ReadQuery(context), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeSearchProducts")
            .WithSummary("The faceted product listing: search, browse, filter, sort and page.")
            .AllowAnonymous()
            .RequireFeature(SearchFeatures.FacetedBrowse)
            .CachePublicRead()
            .Produces<ProductSearchResponse>();

        group.MapGet("/search/suggest", async (
                string? q,
                int? limit,
                HttpContext context,
                IDispatcher dispatcher) =>
            {
                var result = await dispatcher
                    .QueryAsync(new SuggestQuery(q, limit), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeSearchSuggest")
            .WithSummary("Autocomplete: products, popular searches, brands and categories.")
            .AllowAnonymous()
            .RequireFeature(SearchFeatures.Suggestions)
            .CachePublicRead()
            .Produces<SuggestionResponse>();

        group.MapPost("/search/click", async (
                SearchClickBody body,
                HttpContext context,
                IDispatcher dispatcher) =>
            {
                var command = new RecordSearchClickCommand(body.QueryToken, body.Position, body.VariantId);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("storeSearchClick")
            .WithSummary("Records which result a shopper opened, so ranking can be measured.")
            .AllowAnonymous()
            .RequireFeature(SearchFeatures.QueryLogging)
            .Produces(StatusCodes.Status204NoContent);

        return store;
    }

    /// <summary>
    /// Reads the listing page's filters off the query string.
    /// </summary>
    /// <remarks>
    /// Hand-read rather than bound with <c>[AsParameters]</c> because of the attribute filters. Their
    /// names are data — a store that sells fabric has <c>attr.gsm</c> and one that sells lamps does
    /// not — and no record type can declare a parameter whose name a merchandiser invents.
    /// Everything else is read here too, rather than half-bound and half-read, so there is one place
    /// that says what this endpoint accepts.
    /// </remarks>
    /// <param name="context">The request.</param>
    private static SearchProductsQuery ReadQuery(HttpContext context)
    {
        var query = context.Request.Query;
        var attributes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in query)
        {
            if (!parameter.Key.StartsWith(AttributePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var code = parameter.Key[AttributePrefix.Length..];

            // Both spellings a client might use: repeated parameters, and one parameter with commas.
            // Accepting only the first would make a filter URL that works in one HTTP client and not
            // in another, which is the sort of difference nobody finds until a customer reports it.
            var values = parameter.Value
                .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToList();

            if (code.Length > 0 && values.Count > 0)
            {
                attributes[code] = values;
            }
        }

        return new SearchProductsQuery(
            query["q"],
            Guid.TryParse(query["category"], out var category) ? category : null,
            Guids(query["brand"]),
            Guids(query["vendor"]),
            Decimal(query["minPrice"]),
            Decimal(query["maxPrice"]),
            Decimal(query["rating"]),
            Integer(query["discount"]),
            Boolean(query["inStock"]),
            attributes,
            query["sort"],
            query["cursor"],
            Integer(query["size"]));
    }

    /// <summary>Reads a repeated or comma-separated list of identifiers.</summary>
    /// <param name="values">The raw query-string values.</param>
    private static List<Guid> Guids(Microsoft.Extensions.Primitives.StringValues values)
    {
        var ids = new List<Guid>();

        foreach (var value in values)
        {
            foreach (var candidate in (value ?? string.Empty)
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // A value that is not an identifier is dropped rather than refused. A filter URL
                // somebody hand-edited should return a slightly wider result set, not a 400 on a page
                // the shopper is already looking at.
                if (Guid.TryParse(candidate, out var id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    /// <summary>Reads an optional decimal.</summary>
    /// <param name="value">The raw query-string value.</param>
    private static decimal? Decimal(string? value)
        => decimal.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>Reads an optional integer.</summary>
    /// <param name="value">The raw query-string value.</param>
    private static int? Integer(string? value)
        => int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>Reads an optional boolean.</summary>
    /// <param name="value">The raw query-string value.</param>
    private static bool? Boolean(string? value)
        => bool.TryParse(value, out var parsed) ? parsed : null;
}
