using KlaraHome.Infrastructure.Caching;
using KlaraHome.Infrastructure.Features;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Content.Application;
using KlaraHome.Modules.Content.Application.Redirects;
using KlaraHome.Modules.Content.Application.Seo;
using KlaraHome.Modules.Content.Application.Storefront;
using KlaraHome.Modules.Content.Infrastructure.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Content.Endpoints;

/// <summary>
/// What a shopper's browser reads (docs/04-api-specification.md §3.7).
/// </summary>
/// <remarks>
/// <para>
/// Anonymous and cacheable at the edge, because nothing here is scoped to a caller — with one
/// deliberate exception. The banner read varies by whether the visitor is signed in, which is the
/// coarsest possible targeting and the only one a welcome offer needs; it is therefore cached with the
/// short-lived public policy rather than the long one, and the storefront must not cache it further
/// per user.
/// </para>
/// <para>
/// Every read here is published-only. There is no <c>?preview=</c> parameter and there will not be
/// one: an unpublished page is reached through the admin preview, with the caller's own token, rather
/// than through a query parameter one guess away from serving a draft to anybody.
/// </para>
/// <para>
/// The redirect resolver is the odd one out and is worth its place. It is called on every 404 the
/// storefront produces, answers from one indexed lookup, and returns a status code and a location
/// rather than performing the redirect itself — because the browser has to see the redirect come from
/// the storefront's own origin for the address bar to end up right.
/// </para>
/// </remarks>
internal static class StoreContentEndpoints
{
    /// <summary>Maps the storefront content surface beneath <c>/store</c>.</summary>
    /// <param name="store">The <c>/store</c> group.</param>
    public static IEndpointRouteBuilder MapStoreContentEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var group = store
            .MapGroup("/content")
            .WithTags("Content")
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead);

        MapPages(group);
        MapNavigation(group);
        MapBlog(group);
        MapSeo(group);

        // The collection landing page sits at /store/collections/{slug}, where the API specification
        // puts it (§3.2), rather than under /content — it is a shopping surface rather than a CMS one,
        // and a storefront route should not have to know which module answers it.
        store.MapGet("/collections/{slug}", async (
                string slug,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStoreCollectionQuery(slug, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetCollection")
            .WithTags("Content")
            .WithSummary("A curated collection's landing page and a page of its products.")
            .AllowAnonymous()
            .RequireFeature(ContentFeatures.Storefront)
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead)
            .CachePublicRead()
            .Produces<StoreCollectionResponse>();

        return store;
    }

    /// <summary>The composed pages.</summary>
    private static void MapPages(IEndpointRouteBuilder group)
    {
        group.MapGet("/home", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetHomePageQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetHomePage")
            .WithSummary("The published home page, blocks resolved and windowed to now.")
            .AllowAnonymous()
            .RequireFeature(ContentFeatures.Storefront)
            .CachePublicRead()
            .Produces<StorePageResponse>();

        group.MapGet("/pages/{slug}", async (string slug, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStorePageQuery(slug), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetPage")
            .WithSummary("One published page, blocks resolved and windowed to now.")
            .AllowAnonymous()
            .RequireFeature(ContentFeatures.Storefront)
            .CachePublicRead()
            .Produces<StorePageResponse>();
    }

    /// <summary>The menus, the banners and the redirect resolver.</summary>
    private static void MapNavigation(IEndpointRouteBuilder group)
    {
        group.MapGet("/menus/{code}", async (string code, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStoreMenuQuery(code), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetMenu")
            .WithSummary("One menu, nested, with every target resolved into a path.")
            .AllowAnonymous()
            .RequireFeature(ContentFeatures.Storefront)
            .CacheReferenceData()
            .Produces<StoreMenuResponse>();

        group.MapGet("/banners", async (string? placement, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStoreBannersQuery(placement), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetBanners")
            .WithSummary("The banners live in a placement right now, best first.")
            .AllowAnonymous()
            .RequireFeature(ContentFeatures.Banners)
            .CachePublicRead()
            .Produces<IReadOnlyList<StoreBannerResponse>>();

        group.MapGet("/redirects/resolve", async (string? path, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ResolveRedirectQuery(path), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeResolveRedirect")
            .WithSummary("What to do with a path nothing else claims: 301, 302 or 410.")
            .AllowAnonymous()
            .RequireFeature(ContentFeatures.Redirects)
            .CachePublicRead()
            .Produces<RedirectResolutionResponse>();
    }

    /// <summary>The blog, behind its own flag.</summary>
    private static void MapBlog(IEndpointRouteBuilder group)
    {
        group.MapGet("/blog", async (
                string? tag,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListBlogPostsQuery(tag, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeListBlogPosts")
            .WithSummary("The published posts, newest first.")
            .AllowAnonymous()
            .RequireFeature(ContentFeatures.Blog)
            .CachePublicRead()
            .Produces<PagedResult<BlogCardResponse>>();

        group.MapGet("/blog/{slug}", async (string slug, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetBlogPostQuery(slug), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetBlogPost")
            .WithSummary("One published post.")
            .AllowAnonymous()
            .RequireFeature(ContentFeatures.Blog)
            .CachePublicRead()
            .Produces<StorePageResponse>();
    }

    /// <summary>What the storefront needs to serve robots.txt, the sitemap and JSON-LD.</summary>
    /// <remarks>
    /// Deliberately not gated by <c>content.storefront</c>. That flag exists so a store can be served
    /// without the CMS while somebody fixes a page, and a store with no <c>robots.txt</c> is a far
    /// worse outcome than one with no home-page blocks — particularly on a deployment whose
    /// <c>AllowIndexing</c> is off and whose robots document is the only thing keeping it out of an
    /// index.
    /// </remarks>
    private static void MapSeo(IEndpointRouteBuilder group)
    {
        group.MapGet("/seo/config", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetSeoConfigQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetSeoConfig")
            .WithSummary("The canonical origin, the title template and the indexing switch.")
            .AllowAnonymous()
            .CacheReferenceData()
            .Produces<SeoConfigResponse>();

        group.MapGet("/seo/robots", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetRobotsQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                // text/plain rather than the JSON envelope: the storefront writes this straight into
                // the body of /robots.txt, and wrapping it would mean the storefront unwrapping it.
                return result.IsSuccess
                    ? Results.Text(result.Value, "text/plain; charset=utf-8")
                    : result.ToOk(context);
            })
            .WithName("storeGetRobots")
            .WithSummary("The robots document, ready to serve as text/plain.")
            .AllowAnonymous()
            .CacheReferenceData()
            .Produces<string>(StatusCodes.Status200OK, "text/plain");

        group.MapGet("/seo/sitemap", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetSitemapIndexQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetSitemapIndex")
            .WithSummary("The sitemap index: which sitemaps exist, and where.")
            .AllowAnonymous()
            .CacheReferenceData()
            .Produces<SitemapIndexResponse>();

        group.MapGet("/seo/sitemap/{section}", async (
                string section,
                int? page,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetSitemapSectionQuery(section, page), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetSitemapSection")
            .WithSummary("One page of one sitemap section.")
            .AllowAnonymous()
            .CacheReferenceData()
            .Produces<SitemapPageResponse>();

        group.MapGet("/seo/structured-data", async (string? path, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStructuredDataQuery(path), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetStructuredData")
            .WithSummary("The schema.org graph for one path, ready for a script tag.")
            .AllowAnonymous()
            .CachePublicRead()
            .Produces<StructuredDataResponse>();
    }
}
