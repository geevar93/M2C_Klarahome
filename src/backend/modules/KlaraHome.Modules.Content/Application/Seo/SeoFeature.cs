using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Content.Application.Validation;
using KlaraHome.Modules.Content.Infrastructure.Seo;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Content.Application.Seo;

/// <summary>Reads the public SEO configuration the storefront needs before it renders anything.</summary>
internal sealed record GetSeoConfigQuery : IQuery<SeoConfigResponse>;

/// <summary>Builds the <c>robots.txt</c> body.</summary>
internal sealed record GetRobotsQuery : IQuery<string>;

/// <summary>Reads the sitemap index: which sitemaps exist, and where.</summary>
internal sealed record GetSitemapIndexQuery : IQuery<SitemapIndexResponse>;

/// <summary>Reads one page of one sitemap section.</summary>
/// <param name="Section">Which section.</param>
/// <param name="Page">Which page of it, one-based.</param>
internal sealed record GetSitemapSectionQuery(string? Section, int? Page) : IQuery<SitemapPageResponse>;

/// <summary>Builds the structured-data graph for one storefront path.</summary>
/// <param name="Path">The path, as a crawler asked for it.</param>
internal sealed record GetStructuredDataQuery(string? Path) : IQuery<StructuredDataResponse>;

/// <summary>
/// Hands the storefront the SEO facts it must have before its first byte.
/// </summary>
/// <remarks>
/// Server-side rendering writes the <c>&lt;head&gt;</c> before anything else, and the canonical
/// origin, the title template and the indexing switch all belong in it. Every one of them is public,
/// which is why this endpoint is anonymous — a crawler's first request is not authenticated either.
/// </remarks>
/// <param name="settings">The store's settings.</param>
internal sealed class GetSeoConfigQueryHandler(IStoreSettings settings) : IQueryHandler<GetSeoConfigQuery, SeoConfigResponse>
{
    public async Task<Result<SeoConfigResponse>> HandleAsync(
        GetSeoConfigQuery query,
        CancellationToken cancellationToken)
    {
        var seo = await settings.GetAsync<SeoSettings>(cancellationToken).ConfigureAwait(false);

        var origin = seo.CanonicalBaseUrl.TrimEnd('/');

        return Result.Success(new SeoConfigResponse(
            origin,
            seo.AllowIndexing,
            seo.TitleTemplate,
            seo.DefaultMetaDescription,
            seo.TwitterCardType,
            origin.Length == 0 ? StorefrontRoutes.RobotsPath : StorefrontRoutes.Absolute(origin, StorefrontRoutes.RobotsPath),
            origin.Length == 0 ? StorefrontRoutes.SitemapPath : StorefrontRoutes.Absolute(origin, StorefrontRoutes.SitemapPath)));
    }
}

/// <summary>Builds the <c>robots.txt</c> body.</summary>
/// <param name="settings">The store's settings.</param>
internal sealed class GetRobotsQueryHandler(IStoreSettings settings) : IQueryHandler<GetRobotsQuery, string>
{
    public async Task<Result<string>> HandleAsync(GetRobotsQuery query, CancellationToken cancellationToken)
    {
        var seo = await settings.GetAsync<SeoSettings>(cancellationToken).ConfigureAwait(false);

        return Result.Success(RobotsBuilder.Build(seo));
    }
}

/// <summary>Reads the sitemap index.</summary>
/// <remarks>
/// Refused outright when no canonical origin is configured, rather than answered with relative URLs.
/// The sitemap protocol requires <c>loc</c> to be absolute, and a file of relative paths is one every
/// crawler rejects whole — so an error naming the missing setting is far more useful than a file
/// nobody can use.
/// </remarks>
/// <param name="builder">Assembles the sections.</param>
/// <param name="settings">The store's settings.</param>
internal sealed class GetSitemapIndexQueryHandler(SitemapBuilder builder, IStoreSettings settings)
    : IQueryHandler<GetSitemapIndexQuery, SitemapIndexResponse>
{
    public async Task<Result<SitemapIndexResponse>> HandleAsync(
        GetSitemapIndexQuery query,
        CancellationToken cancellationToken)
    {
        var seo = await settings.GetAsync<SeoSettings>(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(seo.CanonicalBaseUrl))
        {
            return Result.Failure<SitemapIndexResponse>(ContentErrors.CanonicalUrlNotConfigured);
        }

        var index = await builder.BuildIndexAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(index);
    }
}

/// <summary>Reads one page of one sitemap section.</summary>
/// <param name="builder">Assembles the section.</param>
/// <param name="settings">The store's settings.</param>
internal sealed class GetSitemapSectionQueryHandler(SitemapBuilder builder, IStoreSettings settings)
    : IQueryHandler<GetSitemapSectionQuery, SitemapPageResponse>
{
    public async Task<Result<SitemapPageResponse>> HandleAsync(
        GetSitemapSectionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!SitemapSections.Contains(query.Section))
        {
            return Result.Failure<SitemapPageResponse>(ContentErrors.NotFound("sitemap section"));
        }

        var seo = await settings.GetAsync<SeoSettings>(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(seo.CanonicalBaseUrl))
        {
            return Result.Failure<SitemapPageResponse>(ContentErrors.CanonicalUrlNotConfigured);
        }

        var section = await builder
            .BuildSectionAsync(query.Section!.ToLowerInvariant(), query.Page ?? 1, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(section);
    }
}

/// <summary>Builds the structured-data graph for one storefront path.</summary>
/// <remarks>
/// The path is normalised the same way a redirect's is, so that <c>/Pages/Returns/</c> and
/// <c>/pages/returns</c> produce the same graph. A crawler arriving on either has arrived at one page,
/// and telling it otherwise is how a site ends up competing with itself.
/// </remarks>
/// <param name="builder">Assembles the graph.</param>
/// <param name="settings">The store's settings.</param>
internal sealed class GetStructuredDataQueryHandler(StructuredDataBuilder builder, IStoreSettings settings)
    : IQueryHandler<GetStructuredDataQuery, StructuredDataResponse>
{
    public async Task<Result<StructuredDataResponse>> HandleAsync(
        GetStructuredDataQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var path = ContentFormats.NormalizePath(query.Path ?? "/");

        if (path is null)
        {
            return Result.Failure<StructuredDataResponse>(ContentErrors.InvalidPath);
        }

        var seo = await settings.GetAsync<SeoSettings>(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(seo.CanonicalBaseUrl))
        {
            return Result.Failure<StructuredDataResponse>(ContentErrors.CanonicalUrlNotConfigured);
        }

        var graph = await builder.BuildAsync(path, cancellationToken).ConfigureAwait(false);

        return Result.Success(new StructuredDataResponse(path, graph));
    }
}
