using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Content.Application;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Features;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Content.Infrastructure.Seo;

/// <summary>The sections a sitemap is divided into.</summary>
/// <remarks>
/// Strings rather than an enum because they appear in a URL a crawler fetches and remembers —
/// <c>/sitemap/pages/1.xml</c> — and a renamed section is a sitemap a crawler has to rediscover.
/// </remarks>
internal static class SitemapSections
{
    /// <summary>The CMS pages: the home page, landing pages, static and legal pages.</summary>
    public const string Pages = "pages";

    /// <summary>The blog, when the flag is on.</summary>
    public const string Blog = "blog";

    /// <summary>The curated collections that are listed.</summary>
    public const string Collections = "collections";

    /// <summary>The category tree.</summary>
    public const string Categories = "categories";

    /// <summary>The products.</summary>
    public const string Products = "products";

    /// <summary>Every section, in the order the index lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Pages, Blog, Collections, Categories, Products];

    /// <summary>Whether a value names a section.</summary>
    /// <param name="section">The candidate.</param>
    public static bool Contains(string? section)
        => section is not null && All.Contains(section, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Builds the store's sitemap.
/// </summary>
/// <remarks>
/// <para>
/// An index of paginated sections rather than one file, because the protocol caps a sitemap at fifty
/// thousand URLs and a store with a real catalogue passes that. Sectioning also means the pages
/// section does not change when a product is added, so a crawler's conditional fetches stay cheap —
/// which is most of what a sitemap is for.
/// </para>
/// <para>
/// The entries come from this module's own tables for pages and collections, from the catalogue's
/// taxonomy seam for categories, and from the projection seam for products. Only the last is
/// expensive: it walks the catalogue variant by variant, because there is no seam that counts
/// products and no schema this module may query for one. That cost is bounded by
/// <c>MaxWalkedVariants</c>, is paid on a response the storefront caches for fifteen minutes, and is
/// the reason <see cref="SeoSettings.SitemapIncludeProducts"/> exists as a switch.
/// </para>
/// <para>
/// Nothing here is XML. The API answers in JSON and the storefront writes the file, which is what
/// docs/05-frontend-architecture.md §3.5 asks for — the sitemap is served from the storefront's own
/// origin, because a sitemap on a different host than the URLs it lists is one a crawler is entitled
/// to ignore.
/// </para>
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="taxonomy">The category tree.</param>
/// <param name="catalogue">The catalogue walk, for the products section.</param>
/// <param name="settings">The store's SEO settings.</param>
/// <param name="flags">Decides whether the blog section exists at all.</param>
/// <param name="options">The walk's bounds.</param>
internal sealed class SitemapBuilder(
    ContentDbContext context,
    ICatalogTaxonomy taxonomy,
    IProductProjectionSource catalogue,
    IStoreSettings settings,
    IFeatureFlags flags,
    IOptionsMonitor<ContentOptions> options)
{
    /// <summary>Which sitemaps exist, and where.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SitemapIndexResponse> BuildIndexAsync(CancellationToken cancellationToken)
    {
        var seo = await settings.GetAsync<SeoSettings>(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(seo.CanonicalBaseUrl))
        {
            return new SitemapIndexResponse([]);
        }

        var entries = new List<SitemapIndexEntryResponse>();
        var size = Math.Max(1, seo.SitemapPageSize);

        foreach (var section in SitemapSections.All)
        {
            if (!await IncludesAsync(section, seo, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var urls = await CollectAsync(section, seo, cancellationToken).ConfigureAwait(false);

            if (urls.Count == 0)
            {
                // A section with nothing in it is left out entirely. An empty sitemap is a file a
                // crawler fetches, parses and learns nothing from, on every pass, for ever.
                continue;
            }

            var pages = (urls.Count + size - 1) / size;

            for (var page = 1; page <= pages; page++)
            {
                var slice = urls.Skip((page - 1) * size).Take(size).ToList();

                entries.Add(new SitemapIndexEntryResponse(
                    section,
                    page,
                    StorefrontRoutes.Absolute(seo.CanonicalBaseUrl, $"/sitemap/{section}/{page}.xml"),
                    slice.Count,
                    slice.Max(url => url.LastModified)));
            }
        }

        return new SitemapIndexResponse(entries);
    }

    /// <summary>One page of one section.</summary>
    /// <param name="section">Which section.</param>
    /// <param name="page">Which page of it, one-based.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SitemapPageResponse> BuildSectionAsync(
        string section,
        int page,
        CancellationToken cancellationToken)
    {
        var seo = await settings.GetAsync<SeoSettings>(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(seo.CanonicalBaseUrl)
            || !await IncludesAsync(section, seo, cancellationToken).ConfigureAwait(false))
        {
            return new SitemapPageResponse(section, page, []);
        }

        var size = Math.Max(1, seo.SitemapPageSize);
        var urls = await CollectAsync(section, seo, cancellationToken).ConfigureAwait(false);

        return new SitemapPageResponse(
            section,
            page,
            [.. urls.Skip((Math.Max(1, page) - 1) * size).Take(size)]);
    }

    /// <summary>Whether a section is served at all in this deployment.</summary>
    private async ValueTask<bool> IncludesAsync(
        string section,
        SeoSettings seo,
        CancellationToken cancellationToken)
        => section switch
        {
            SitemapSections.Products => seo.SitemapIncludeProducts,
            SitemapSections.Categories => seo.SitemapIncludeCategories,
            SitemapSections.Blog => await flags
                .IsEnabledAsync(ContentFeatures.Blog, cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            _ => true,
        };

    private Task<List<SitemapUrlResponse>> CollectAsync(
        string section,
        SeoSettings seo,
        CancellationToken cancellationToken)
        => section switch
        {
            SitemapSections.Blog => CollectPagesAsync(seo, blog: true, cancellationToken),
            SitemapSections.Collections => CollectCollectionsAsync(seo, cancellationToken),
            SitemapSections.Categories => CollectCategoriesAsync(seo, cancellationToken),
            SitemapSections.Products => CollectProductsAsync(seo, cancellationToken),
            _ => CollectPagesAsync(seo, blog: false, cancellationToken),
        };

    /// <summary>The CMS pages that are published and not asked to be left out of the index.</summary>
    private async Task<List<SitemapUrlResponse>> CollectPagesAsync(
        SeoSettings seo,
        bool blog,
        CancellationToken cancellationToken)
    {
        var pages = await context.Pages
            .AsNoTracking()
            .Where(page => page.Status == PageStatus.Published)
            .Where(page => blog ? page.Type == PageType.Blog : page.Type != PageType.Blog)
            .OrderBy(page => page.Slug)
            .Select(page => new
            {
                page.Type,
                page.Slug,
                page.ContentChangedAt,
                page.PublishedAt,
                page.UpdatedAt,
                page.CreatedAt,
                page.Seo.NoIndex,
                page.Seo.SitemapPriority,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. pages
                .Where(page => !page.NoIndex)
                .Select(page => new SitemapUrlResponse(
                    StorefrontRoutes.Absolute(seo.CanonicalBaseUrl, StorefrontRoutes.Page(page.Type, page.Slug)),
                    page.ContentChangedAt ?? page.UpdatedAt ?? page.PublishedAt ?? page.CreatedAt,
                    ChangeFrequency(page.Type),
                    page.SitemapPriority ?? DefaultPriority(page.Type))),
        ];
    }

    /// <summary>The collections that are live and listed.</summary>
    private async Task<List<SitemapUrlResponse>> CollectCollectionsAsync(
        SeoSettings seo,
        CancellationToken cancellationToken)
    {
        var collections = await context.Collections
            .AsNoTracking()
            .Where(collection => collection.IsActive && collection.IsListed)
            .OrderBy(collection => collection.Slug)
            .Select(collection => new
            {
                collection.Slug,
                collection.ContentChangedAt,
                collection.UpdatedAt,
                collection.CreatedAt,
                collection.Seo.NoIndex,
                collection.Seo.SitemapPriority,
                collection.ItemCount,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. collections
                // An empty collection is a page that says "nothing here". Offering it to a crawler is
                // offering a thin page, which is the one thing a sitemap should never do on purpose.
                .Where(collection => !collection.NoIndex && collection.ItemCount > 0)
                .Select(collection => new SitemapUrlResponse(
                    StorefrontRoutes.Absolute(seo.CanonicalBaseUrl, StorefrontRoutes.Collection(collection.Slug)),
                    collection.ContentChangedAt ?? collection.UpdatedAt ?? collection.CreatedAt,
                    "weekly",
                    collection.SitemapPriority ?? 0.7m)),
        ];
    }

    /// <summary>The active category tree.</summary>
    private async Task<List<SitemapUrlResponse>> CollectCategoriesAsync(
        SeoSettings seo,
        CancellationToken cancellationToken)
    {
        var categories = await taxonomy
            .ListCategoriesAsync(activeOnly: true, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. categories.Select(category => new SitemapUrlResponse(
                StorefrontRoutes.Absolute(seo.CanonicalBaseUrl, StorefrontRoutes.Category(category.Slug)),
                category.UpdatedAt,
                "weekly",
                // A top-level category is a more important page than a leaf three levels down, and
                // saying so is most of what sitemap priority is good for.
                category.Level switch { 0 => 0.9m, 1 => 0.8m, _ => 0.6m })),
        ];
    }

    /// <summary>
    /// The products, one URL per product, gathered by walking the catalogue.
    /// </summary>
    /// <remarks>
    /// The expensive section, and the only one that is. There is no seam that counts or pages
    /// products by slug — <c>IProductProjectionSource</c> walks variants, because that is what a
    /// read-model needs — so this reads the catalogue forwards, keeps one URL per product, and stops
    /// at the configured ceiling. A store larger than that ceiling gets a truncated products section
    /// rather than a slow one, which is the right trade for a file a crawler reads at its leisure.
    /// </remarks>
    private async Task<List<SitemapUrlResponse>> CollectProductsAsync(
        SeoSettings seo,
        CancellationToken cancellationToken)
    {
        var bounds = options.CurrentValue;
        var urls = new Dictionary<Guid, SitemapUrlResponse>();
        var walked = 0;
        Guid? cursor = null;

        while (walked < bounds.MaxWalkedVariants && !cancellationToken.IsCancellationRequested)
        {
            var page = await catalogue
                .EnumerateAsync(cursor, bounds.CatalogWalkPageSize, cancellationToken)
                .ConfigureAwait(false);

            walked += page.VariantIds.Count;

            foreach (var product in page.Items.Where(row => row is { IsBuyBox: true, IsPurchasable: true }))
            {
                // Keyed on the product, not the variant: a product with nine colours is one page, and
                // nine URLs pointing at it is the definition of duplicate content.
                urls.TryAdd(
                    product.ProductId,
                    new SitemapUrlResponse(
                        StorefrontRoutes.Absolute(seo.CanonicalBaseUrl, StorefrontRoutes.Product(product.ProductSlug)),
                        product.PublishedAt,
                        "daily",
                        0.8m));
            }

            if (page.NextVariantCursor is null)
            {
                break;
            }

            cursor = page.NextVariantCursor;
        }

        return [.. urls.Values.OrderBy(url => url.Loc, StringComparer.Ordinal)];
    }

    /// <summary>How often a crawler is told a page of this kind changes.</summary>
    /// <remarks>
    /// A hint rather than an instruction — crawlers weigh it lightly and some ignore it — but the
    /// hints are honest: a home page really does change more often than a privacy policy.
    /// </remarks>
    private static string ChangeFrequency(PageType type)
        => type switch
        {
            PageType.Home => "daily",
            PageType.Landing => "weekly",
            PageType.Blog => "monthly",
            PageType.Legal => "yearly",
            _ => "monthly",
        };

    private static decimal DefaultPriority(PageType type)
        => type switch
        {
            PageType.Home => 1.0m,
            PageType.Landing => 0.8m,
            PageType.Blog => 0.6m,
            PageType.Legal => 0.3m,
            _ => 0.5m,
        };
}
