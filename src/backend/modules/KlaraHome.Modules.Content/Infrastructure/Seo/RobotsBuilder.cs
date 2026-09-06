using System.Text;
using KlaraHome.Contracts.Platform;

namespace KlaraHome.Modules.Content.Infrastructure.Seo;

/// <summary>
/// Writes the store's <c>robots.txt</c>.
/// </summary>
/// <remarks>
/// <para>
/// Generated rather than a static file in the storefront's <c>assets</c> folder, and that is the
/// whole point of it being here. The single most consequential line in the document —
/// <c>Disallow: /</c> or not — is a decision an operator makes on the day the store goes live and
/// occasionally has to reverse in a hurry. A static file would make both of those a deployment.
/// </para>
/// <para>
/// When indexing is off the document is <b>only</b> the blanket disallow. Emitting the real
/// directives alongside it would publish the store's URL structure to anybody who fetched the file,
/// which is a small thing, and would leave a document whose meaning depended on a crawler reading two
/// rules in the right order, which is not.
/// </para>
/// </remarks>
internal static class RobotsBuilder
{
    /// <summary>Builds the document.</summary>
    /// <param name="seo">The store's SEO settings.</param>
    /// <returns>The body of <c>robots.txt</c>, ready to serve as <c>text/plain</c>.</returns>
    public static string Build(SeoSettings seo)
    {
        ArgumentNullException.ThrowIfNull(seo);

        var builder = new StringBuilder();

        builder.AppendLine("User-agent: *");

        if (!seo.AllowIndexing)
        {
            builder.AppendLine("Disallow: /");
            return builder.ToString();
        }

        foreach (var path in seo.DisallowedPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            builder.Append("Disallow: ").AppendLine(path.Trim());
        }

        if (!string.IsNullOrWhiteSpace(seo.RobotsExtra))
        {
            builder.AppendLine();
            builder.AppendLine(seo.RobotsExtra.Trim());
        }

        // The sitemap line is absolute by the protocol's own rule, which is why it is emitted only
        // when a canonical origin has been configured: a relative Sitemap directive is one every
        // crawler ignores.
        if (!string.IsNullOrWhiteSpace(seo.CanonicalBaseUrl))
        {
            builder.AppendLine();
            builder
                .Append("Sitemap: ")
                .AppendLine(StorefrontRoutes.Absolute(seo.CanonicalBaseUrl, StorefrontRoutes.SitemapPath));
        }

        return builder.ToString();
    }
}
