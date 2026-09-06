using KlaraHome.Infrastructure.Caching;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Catalog.Application.Storefront;
using KlaraHome.Modules.Catalog.Application.Taxonomy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Catalog.Endpoints;

/// <summary>Query-string filters for the storefront brand list.</summary>
/// <param name="Search">A fragment of the name.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record StoreBrandFilter(string? Search, string? Cursor, int? Size);

/// <summary>
/// What a shopper can read of the catalogue (docs/04-api-specification.md §3.2).
/// </summary>
/// <remarks>
/// <para>
/// Anonymous, and everything here is either public already or a mandatory disclosure the law
/// requires to be published. Nothing is scoped to a caller, which is what makes these the only
/// catalogue reads that can be cached at the edge.
/// </para>
/// <para>
/// <c>GET /store/products</c> — the faceted product listing — is deliberately <b>not</b> here. It
/// reads a denormalised projection with its facet counts, and that projection is the Search
/// module's at Step 19. Serving it from this schema would mean a join across five tables per page
/// and no facets at all.
/// </para>
/// </remarks>
internal static class StoreCatalogEndpoints
{
    /// <summary>Maps the storefront catalogue surface beneath <c>/store</c>.</summary>
    /// <param name="store">The <c>/store</c> group.</param>
    public static IEndpointRouteBuilder MapStoreCatalogEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var group = store
            .MapGroup(string.Empty)
            .WithTags("Catalog")
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead);

        group.MapGet("/categories", async (
                Guid? parentId,
                int? depth,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                // activeOnly is not a parameter here: a shopper never sees a hidden category, and
                // making it optional would be a query string away from publishing the merchandising
                // team's unfinished tree.
                var query = new GetCategoryTreeQuery(parentId, depth, ActiveOnly: true);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeCategories")
            .WithSummary("The browse tree, or the subtree beneath one node.")
            .AllowAnonymous()
            .CacheReferenceData()
            .Produces<IReadOnlyList<CategoryNode>>();

        group.MapGet("/categories/{slug}", async (string slug, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetCategoryBySlugQuery(slug), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeCategoryBySlug")
            .WithSummary("One category page. A hidden category answers 404.")
            .AllowAnonymous()
            .CacheReferenceData()
            .Produces<CategoryResponse>();

        group.MapGet("/brands", async (
                [AsParameters] StoreBrandFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListBrandsQuery(filter.Search, ActiveOnly: true, filter.Cursor, filter.Size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeBrands")
            .WithSummary("The brands this store carries, alphabetically.")
            .AllowAnonymous()
            .CachePublicRead()
            .Produces<PagedResult<BrandResponse>>();

        group.MapGet("/products/{slug}", async (string slug, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStorefrontProductQuery(slug), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeProductBySlug")
            .WithSummary("A product page: its variants, its mandatory disclosures, and each variant's buy box.")
            .AllowAnonymous()
            .Produces<StorefrontProduct>();

        group.MapGet("/products/{slug}/offers", async (
                string slug,
                Guid? variantId,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStorefrontOffersQuery(slug, variantId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeProductOffers")
            .WithSummary("Every seller's offer for one variant, in buy-box order.")
            .AllowAnonymous()
            .Produces<IReadOnlyList<StorefrontOffer>>();

        return store;
    }
}
