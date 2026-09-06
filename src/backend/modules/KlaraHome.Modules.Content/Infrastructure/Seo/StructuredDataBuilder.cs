using System.Globalization;
using System.Text.Json;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Media;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Content.Infrastructure.Seo;

/// <summary>
/// Builds the <c>schema.org</c> JSON-LD graph for one storefront URL.
/// </summary>
/// <remarks>
/// <para>
/// One <c>@graph</c> rather than several script tags, and that is the substantive decision here. A
/// page needs to say four things at once — who the store is, that this is that store's website, where
/// this page sits in the site's hierarchy, and what is on it — and a crawler is only entitled to
/// treat the organisation named by a product's <c>seller</c> and the organisation named by the site
/// as the same one when they share an <c>@id</c>. Four separate documents are four separate
/// assertions about four possibly different companies.
/// </para>
/// <para>
/// Built here rather than in the storefront because every fact in it is a fact this backend owns: the
/// canonical origin and the organisation's identity are settings, the breadcrumb comes from the
/// category tree, and the price and availability on an <c>Offer</c> come from the same buy-box
/// resolution the product page renders. A storefront assembling it from what it happened to have
/// loaded would eventually publish a price it was not showing, which is a penalty rather than a bug.
/// </para>
/// <para>
/// A path nothing claims produces the organisation and website nodes and nothing else, rather than an
/// error. Every page of the store should carry those two, and a 404 page is still a page of the
/// store.
/// </para>
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="taxonomy">The category tree, for a breadcrumb.</param>
/// <param name="catalogue">The catalogue, for a product's offer.</param>
/// <param name="settings">The store's settings.</param>
/// <param name="media">Resolves the logo and the images a graph names.</param>
internal sealed class StructuredDataBuilder(
    ContentDbContext context,
    ICatalogTaxonomy taxonomy,
    IProductProjectionSource catalogue,
    IStoreSettings settings,
    IMediaLibrary media)
{
    /// <summary>The graph for one path.</summary>
    /// <param name="path">The storefront path, as a crawler asked for it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonElement> BuildAsync(string path, CancellationToken cancellationToken)
    {
        var seo = await settings.GetAsync<SeoSettings>(cancellationToken).ConfigureAwait(false);
        var branding = await settings.GetAsync<BrandingSettings>(cancellationToken).ConfigureAwait(false);
        var support = await settings.GetAsync<SupportSettings>(cancellationToken).ConfigureAwait(false);

        var origin = seo.CanonicalBaseUrl.TrimEnd('/');
        var nodes = new List<Dictionary<string, object?>>();

        var organisation = await BuildOrganizationAsync(seo, branding, support, origin, cancellationToken)
            .ConfigureAwait(false);

        nodes.Add(organisation);
        nodes.Add(BuildWebSite(seo, branding, origin));

        var segments = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        var page = segments switch
        {
            [] => await BuildCmsPageAsync(PageType.Home, slug: null, origin, path, cancellationToken)
                .ConfigureAwait(false),
            ["pages", var slug] => await BuildCmsPageAsync(PageType.Static, slug, origin, path, cancellationToken)
                .ConfigureAwait(false),
            ["blog", var slug] => await BuildCmsPageAsync(PageType.Blog, slug, origin, path, cancellationToken)
                .ConfigureAwait(false),
            ["collections", var slug] => await BuildCollectionAsync(slug, origin, path, cancellationToken)
                .ConfigureAwait(false),
            ["c", var slug] => await BuildCategoryAsync(slug, origin, path, cancellationToken)
                .ConfigureAwait(false),
            ["p", var slug] => await BuildProductAsync(slug, origin, path, cancellationToken)
                .ConfigureAwait(false),
            _ => [],
        };

        nodes.AddRange(page);

        var graph = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = nodes,
        };

        return Serialize(graph);
    }

    /// <summary>Who the store is.</summary>
    private async Task<Dictionary<string, object?>> BuildOrganizationAsync(
        SeoSettings seo,
        BrandingSettings branding,
        SupportSettings support,
        string origin,
        CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(seo.OrganizationName)
            ? branding.StoreName
            : seo.OrganizationName;

        var logoRef = string.IsNullOrWhiteSpace(seo.OrganizationLogoRef)
            ? branding.LogoRef
            : seo.OrganizationLogoRef;

        var node = new Dictionary<string, object?>
        {
            ["@type"] = "Organization",
            ["@id"] = $"{origin}/#organization",
            ["name"] = name,
            ["url"] = origin + "/",
        };

        if (Guid.TryParse(logoRef, out var logoId))
        {
            var logo = await media.GetAsync(logoId, cancellationToken).ConfigureAwait(false);

            if (logo?.Url is { Length: > 0 })
            {
                node["logo"] = new Dictionary<string, object?>
                {
                    ["@type"] = "ImageObject",
                    ["url"] = logo.Url,
                    ["width"] = logo.Width,
                    ["height"] = logo.Height,
                };
            }
        }

        if (seo.SocialProfileUrls.Count > 0)
        {
            node["sameAs"] = seo.SocialProfileUrls;
        }

        // The contact point is the support desk, which is a fact the store already publishes on
        // every page under the Consumer Protection (E-Commerce) Rules 2020. Saying it in a form a
        // crawler understands costs nothing and is the difference between a knowledge panel with a
        // phone number and one without.
        if (!string.IsNullOrWhiteSpace(support.Email) || !string.IsNullOrWhiteSpace(support.Phone))
        {
            node["contactPoint"] = new Dictionary<string, object?>
            {
                ["@type"] = "ContactPoint",
                ["contactType"] = "customer support",
                ["email"] = Blank(support.Email),
                ["telephone"] = Blank(support.Phone),
                ["areaServed"] = "IN",
            };
        }

        return node;
    }

    /// <summary>That this is that organisation's website, and that it has a search box.</summary>
    private static Dictionary<string, object?> BuildWebSite(
        SeoSettings seo,
        BrandingSettings branding,
        string origin)
        => new()
        {
            ["@type"] = "WebSite",
            ["@id"] = $"{origin}/#website",
            ["url"] = origin + "/",
            ["name"] = branding.StoreName,
            ["description"] = Blank(seo.DefaultMetaDescription) ?? Blank(branding.Tagline),
            ["publisher"] = Reference($"{origin}/#organization"),
            ["potentialAction"] = new Dictionary<string, object?>
            {
                ["@type"] = "SearchAction",
                ["target"] = new Dictionary<string, object?>
                {
                    ["@type"] = "EntryPoint",
                    ["urlTemplate"] = $"{origin}/search?q={{search_term_string}}",
                },
                ["query-input"] = "required name=search_term_string",
            },
        };

    /// <summary>A CMS page, and the trail to it.</summary>
    private async Task<List<Dictionary<string, object?>>> BuildCmsPageAsync(
        PageType type,
        string? slug,
        string origin,
        string path,
        CancellationToken cancellationToken)
    {
        var query = context.Pages
            .AsNoTracking()
            .Where(page => page.Status == PageStatus.Published);

        query = slug is null
            ? query.Where(page => page.Type == PageType.Home)
            : query.Where(page => page.Slug == slug && page.Type != PageType.Home);

        var found = await query
            .Select(page => new
            {
                page.Title,
                page.Summary,
                page.Slug,
                page.Type,
                page.PublishedAt,
                page.ContentChangedAt,
                page.Author,
                page.Seo.MetaDescription,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (found is null)
        {
            return [];
        }

        var url = StorefrontRoutes.Absolute(origin, path);

        // A blog post is an Article and everything else is a WebPage. The distinction is worth making
        // because an Article is eligible for rich results a WebPage is not, and claiming one for a
        // returns policy would be a claim a crawler eventually punishes.
        var isArticle = found.Type == PageType.Blog && type == PageType.Blog;

        var node = new Dictionary<string, object?>
        {
            ["@type"] = isArticle ? "Article" : "WebPage",
            ["@id"] = $"{url}#page",
            ["url"] = url,
            ["name"] = found.Title,
            ["headline"] = isArticle ? found.Title : null,
            ["description"] = Blank(found.MetaDescription) ?? Blank(found.Summary),
            ["datePublished"] = Iso(found.PublishedAt),
            ["dateModified"] = Iso(found.ContentChangedAt ?? found.PublishedAt),
            ["author"] = isArticle && !string.IsNullOrWhiteSpace(found.Author)
                ? new Dictionary<string, object?> { ["@type"] = "Person", ["name"] = found.Author }
                : null,
            ["isPartOf"] = Reference($"{origin}/#website"),
            ["publisher"] = Reference($"{origin}/#organization"),
        };

        var crumbs = new List<(string Name, string Path)> { ("Home", "/") };

        if (found.Type != PageType.Home)
        {
            crumbs.Add((found.Title, path));
        }

        return [node, BuildBreadcrumb(origin, url, crumbs)];
    }

    /// <summary>A curated collection, and the products on it.</summary>
    private async Task<List<Dictionary<string, object?>>> BuildCollectionAsync(
        string slug,
        string origin,
        string path,
        CancellationToken cancellationToken)
    {
        var collection = await context.Collections
            .AsNoTracking()
            .Where(row => row.Slug == slug && row.IsActive)
            .Select(row => new { row.Id, row.Name, row.Description, row.ItemCount, row.ContentChangedAt })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return [];
        }

        var url = StorefrontRoutes.Absolute(origin, path);

        var node = new Dictionary<string, object?>
        {
            ["@type"] = "CollectionPage",
            ["@id"] = $"{url}#page",
            ["url"] = url,
            ["name"] = collection.Name,
            ["description"] = Blank(collection.Description),
            ["dateModified"] = Iso(collection.ContentChangedAt),
            ["isPartOf"] = Reference($"{origin}/#website"),
            // The count rather than the items. An ItemList of five hundred products is a payload
            // bigger than the page it describes, and a crawler follows the links on the page anyway.
            ["mainEntity"] = new Dictionary<string, object?>
            {
                ["@type"] = "ItemList",
                ["numberOfItems"] = collection.ItemCount,
            },
        };

        return
        [
            node,
            BuildBreadcrumb(origin, url, [("Home", "/"), (collection.Name, path)]),
        ];
    }

    /// <summary>A category listing, and the trail of categories above it.</summary>
    private async Task<List<Dictionary<string, object?>>> BuildCategoryAsync(
        string slug,
        string origin,
        string path,
        CancellationToken cancellationToken)
    {
        var categories = await taxonomy
            .ListCategoriesAsync(activeOnly: true, cancellationToken)
            .ConfigureAwait(false);

        var category = categories.FirstOrDefault(node =>
            string.Equals(node.Slug, slug, StringComparison.OrdinalIgnoreCase));

        if (category is null)
        {
            return [];
        }

        var ancestry = await taxonomy.FindAncestryAsync(category.Id, cancellationToken).ConfigureAwait(false);
        var url = StorefrontRoutes.Absolute(origin, path);

        var crumbs = new List<(string Name, string Path)> { ("Home", "/") };
        crumbs.AddRange(ancestry.Select(node => (node.Name, StorefrontRoutes.Category(node.Slug))));

        var node = new Dictionary<string, object?>
        {
            ["@type"] = "CollectionPage",
            ["@id"] = $"{url}#page",
            ["url"] = url,
            ["name"] = category.Name,
            ["dateModified"] = Iso(category.UpdatedAt),
            ["isPartOf"] = Reference($"{origin}/#website"),
        };

        return [node, BuildBreadcrumb(origin, url, crumbs)];
    }

    /// <summary>
    /// A product, its winning offer, and the trail of categories above it.
    /// </summary>
    /// <remarks>
    /// The offer's price and availability come from the same buy-box resolution the product page
    /// renders, which is the whole reason this is built on the projection seam rather than on
    /// anything this module stores. A structured-data price that disagrees with the page's price is
    /// the one SEO mistake with a direct commercial penalty attached.
    /// </remarks>
    private async Task<List<Dictionary<string, object?>>> BuildProductAsync(
        string slug,
        string origin,
        string path,
        CancellationToken cancellationToken)
    {
        var offers = await catalogue.FindBySlugAsync(slug, cancellationToken).ConfigureAwait(false);

        var winner = offers
            .Where(row => row.IsBuyBox)
            .OrderByDescending(row => row.IsPurchasable)
            .ThenBy(row => row.SellingPrice)
            .FirstOrDefault();

        if (winner is null)
        {
            return [];
        }

        var url = StorefrontRoutes.Absolute(origin, path);
        var image = await ResolveImageUrlAsync(winner.PrimaryImageFileId, cancellationToken).ConfigureAwait(false);

        var node = new Dictionary<string, object?>
        {
            ["@type"] = "Product",
            ["@id"] = $"{url}#product",
            ["url"] = url,
            ["name"] = winner.ProductName,
            ["description"] = Blank(winner.ShortDescription),
            ["sku"] = winner.Sku,
            ["image"] = image is null ? null : new[] { image },
            ["brand"] = string.IsNullOrWhiteSpace(winner.BrandName)
                ? null
                : new Dictionary<string, object?> { ["@type"] = "Brand", ["name"] = winner.BrandName },
            ["offers"] = new Dictionary<string, object?>
            {
                ["@type"] = "Offer",
                ["@id"] = $"{url}#offer",
                ["url"] = url,
                ["price"] = winner.SellingPrice.ToString("0.00", CultureInfo.InvariantCulture),
                ["priceCurrency"] = winner.CurrencyCode,
                ["availability"] = winner.IsPurchasable
                    ? "https://schema.org/InStock"
                    : "https://schema.org/OutOfStock",
                ["itemCondition"] = "https://schema.org/NewCondition",
                // The seller is the marketplace's vendor, named rather than referenced: it is a
                // different organisation from the one that publishes the site, and conflating the two
                // would tell a crawler the platform manufactured everything it sells.
                ["seller"] = new Dictionary<string, object?>
                {
                    ["@type"] = "Organization",
                    ["name"] = winner.VendorName,
                },
            },
        };

        // Only when there are reviews. Google rejects an AggregateRating with a zero count, and a
        // rejected block invalidates the whole graph rather than just itself.
        if (winner is { RatingCount: > 0, RatingAverage: not null })
        {
            node["aggregateRating"] = new Dictionary<string, object?>
            {
                ["@type"] = "AggregateRating",
                ["ratingValue"] = winner.RatingAverage.Value.ToString("0.0", CultureInfo.InvariantCulture),
                ["reviewCount"] = winner.RatingCount,
                ["bestRating"] = 5,
                ["worstRating"] = 1,
            };
        }

        var ancestry = await taxonomy.FindAncestryAsync(winner.CategoryId, cancellationToken).ConfigureAwait(false);

        var crumbs = new List<(string Name, string Path)> { ("Home", "/") };
        crumbs.AddRange(ancestry.Select(entry => (entry.Name, StorefrontRoutes.Category(entry.Slug))));
        crumbs.Add((winner.ProductName, path));

        return [node, BuildBreadcrumb(origin, url, crumbs)];
    }

    /// <summary>The trail from the home page to this one.</summary>
    private static Dictionary<string, object?> BuildBreadcrumb(
        string origin,
        string url,
        IReadOnlyList<(string Name, string Path)> crumbs)
        => new()
        {
            ["@type"] = "BreadcrumbList",
            ["@id"] = $"{url}#breadcrumb",
            ["itemListElement"] = crumbs
                .Select((crumb, index) => new Dictionary<string, object?>
                {
                    ["@type"] = "ListItem",
                    ["position"] = index + 1,
                    ["name"] = crumb.Name,
                    ["item"] = StorefrontRoutes.Absolute(origin, crumb.Path),
                })
                .ToList(),
        };

    private async Task<string?> ResolveImageUrlAsync(Guid? fileId, CancellationToken cancellationToken)
    {
        if (fileId is null)
        {
            return null;
        }

        var file = await media.GetAsync(fileId.Value, cancellationToken).ConfigureAwait(false);

        return file?.Url;
    }

    /// <summary>
    /// How the graph is written.
    /// </summary>
    /// <remarks>
    /// Property names in a JSON-LD document are schema.org's, not this codebase's: <c>@type</c>,
    /// <c>query-input</c> and <c>itemListElement</c> are spelled the way the vocabulary spells them,
    /// so nothing may re-case them.
    /// </remarks>
    private static readonly JsonSerializerOptions GraphJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    private static Dictionary<string, object?> Reference(string id) => new() { ["@id"] = id };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? Iso(DateTimeOffset? value)
        => value?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Serialises the graph, dropping the properties that came out null.
    /// </summary>
    /// <remarks>
    /// Nulls are stripped rather than emitted, because <c>schema.org</c> treats a present-but-null
    /// property as a claim that the value is empty. A <c>description</c> of null is worse than no
    /// description at all.
    /// </remarks>
    private static JsonElement Serialize(Dictionary<string, object?> graph)
    {
        var json = JsonSerializer.Serialize(graph, GraphJson);

        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }
}
