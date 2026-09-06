using KlaraHome.Contracts.Platform;

namespace KlaraHome.Modules.Content.Infrastructure.Features;

/// <summary>
/// The feature flags the Content module owns.
/// </summary>
/// <remarks>
/// Declared in code and seeded into <c>platform.feature_flags</c>, exactly as every module's are, so
/// the admin UI lists every switch that exists rather than only the ones somebody has already
/// touched. Three ship on because they gate features that work; two ship off, and they are the two
/// where "off" is the safe answer rather than the cautious one.
/// </remarks>
internal static class ContentFeatures
{
    /// <summary>
    /// Gates the storefront's CMS reads — pages, the home page, menus and collections.
    /// </summary>
    /// <remarks>
    /// The switch to reach for on the afternoon somebody publishes a page that breaks server-side
    /// rendering. With it off the storefront falls back to whatever it renders without a CMS, which
    /// is a plain shell rather than an error page, and an editor can fix the page without the store
    /// being down while they do.
    /// </remarks>
    public const string Storefront = "content.storefront";

    /// <summary>
    /// Gates banners and the announcement bar.
    /// </summary>
    /// <remarks>
    /// Separate from the pages flag because it fails separately and is withdrawn separately. A
    /// campaign that has to come down in the next ninety seconds — a price that was wrong, a partner
    /// who pulled out — comes down here, without touching anything else the storefront reads.
    /// </remarks>
    public const string Banners = "content.banners";

    /// <summary>
    /// Gates the redirect resolver.
    /// </summary>
    /// <remarks>
    /// On, and it should stay on: with it off, every renamed URL in the store's history becomes a 404
    /// again. It exists because a redirect table with a bad row in it can send visitors somewhere
    /// unintended, and turning the whole manager off is a faster remedy than finding the row.
    /// </remarks>
    public const string Redirects = "content.redirects";

    /// <summary>
    /// Gates the blog and lookbook.
    /// </summary>
    /// <remarks>
    /// Off. The step card calls it optional, and a store that has not written a post should not have
    /// an empty <c>/blog</c> in its sitemap — an indexed empty section is worse for a crawler's
    /// opinion of a site than no section at all.
    /// </remarks>
    public const string Blog = "content.blog";

    /// <summary>
    /// Allows custom-HTML blocks to be written and rendered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off, and it is the one flag in this module that is a security control rather than an
    /// operational one. A custom-HTML block is arbitrary markup on a page every shopper loads, which
    /// is the definition of a stored cross-site-scripting vector
    /// (docs/07-security-compliance.md §3).
    /// </para>
    /// <para>
    /// It is deliberately <em>both</em> a flag and a permission. The permission says who may write
    /// one; the flag says whether this deployment permits them at all — which is the control an
    /// operator needs when they cannot be sure which of forty staff accounts is still trustworthy.
    /// Turning it off also stops existing blocks rendering, so it is a remedy and not only a
    /// prohibition.
    /// </para>
    /// </remarks>
    public const string CustomHtml = "content.custom-html";

    /// <summary>Every flag this module declares, seeded on each deploy.</summary>
    public static IReadOnlyList<FeatureFlagDeclaration> All { get; } =
    [
        new(Storefront, true, "Serve CMS pages, menus and collections to the storefront."),
        new(Banners, true, "Serve banners and the announcement bar."),
        new(Redirects, true, "Answer a 404 with the redirect manager's rules."),
        new(Blog, false, "Publish the blog and lookbook."),
        new(CustomHtml, false, "Allow custom HTML blocks to be written and rendered."),
    ];
}

/// <summary>Publishes this module's flags to the seeder, like every other module.</summary>
internal sealed class ContentFeatureFlagSource : IFeatureFlagSource
{
    /// <inheritdoc />
    public string Module => "Content";

    /// <inheritdoc />
    public IReadOnlyList<FeatureFlagDeclaration> Flags => ContentFeatures.All;
}
