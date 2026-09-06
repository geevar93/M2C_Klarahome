using KlaraHome.Modules.Content.Domain;

namespace KlaraHome.Modules.Content.Infrastructure.Seo;

/// <summary>
/// The storefront's URL shapes, as this module has to know them
/// (docs/05-frontend-architecture.md §3.2).
/// </summary>
/// <remarks>
/// <para>
/// A backend module knowing a frontend's routes is a coupling worth naming rather than hiding. It
/// exists because the two things this module produces — a sitemap and a structured-data graph — are
/// both lists of <em>URLs a crawler will fetch</em>, and there is no way to produce one without
/// knowing what those URLs are. The alternative is the storefront generating its own sitemap, which
/// would mean the Angular app enumerating the catalogue.
/// </para>
/// <para>
/// It is confined to this one file so the coupling is a single place to change, and the shapes come
/// straight from the route map in the frontend architecture document rather than from anybody's
/// memory.
/// </para>
/// </remarks>
internal static class StorefrontRoutes
{
    /// <summary>Where the storefront serves the sitemap index.</summary>
    public const string SitemapPath = "/sitemap.xml";

    /// <summary>Where it serves the robots document.</summary>
    public const string RobotsPath = "/robots.txt";

    /// <summary>The path a CMS page is served at.</summary>
    /// <remarks>
    /// The home page is the site root and not <c>/pages/home</c>, which is the one case worth
    /// spelling out: a home page listed in a sitemap under its slug would be a second URL serving the
    /// same content, and a crawler would treat one of them as a duplicate of the other.
    /// </remarks>
    /// <param name="type">What the page is for.</param>
    /// <param name="slug">Its address.</param>
    public static string Page(PageType type, string slug)
        => type switch
        {
            PageType.Home => "/",
            PageType.Blog => $"/blog/{slug}",
            _ => $"/pages/{slug}",
        };

    /// <summary>The path a curated collection is served at.</summary>
    /// <param name="slug">Its address.</param>
    public static string Collection(string slug) => $"/collections/{slug}";

    /// <summary>The path a category listing is served at.</summary>
    /// <param name="slug">Its address.</param>
    public static string Category(string slug) => $"/c/{slug}";

    /// <summary>The path a product page is served at.</summary>
    /// <param name="slug">Its address.</param>
    public static string Product(string slug) => $"/p/{slug}";

    /// <summary>
    /// Joins an origin and a path into an absolute URL.
    /// </summary>
    /// <remarks>
    /// Defensive about the slash on both sides, because the two halves come from different places —
    /// the origin from a setting an operator typed, the path from this class — and a doubled or
    /// missing slash produces a URL that is subtly wrong in every entry of a fifty-thousand-line file.
    /// </remarks>
    /// <param name="origin">The canonical origin, with scheme and no trailing slash.</param>
    /// <param name="path">The path, with a leading slash.</param>
    public static string Absolute(string origin, string path)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(path);

        var host = origin.TrimEnd('/');

        return path.Length == 0 || path == "/"
            ? host + "/"
            : host + (path.StartsWith('/') ? path : "/" + path);
    }
}
