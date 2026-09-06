namespace KlaraHome.Modules.Content.Endpoints;

/// <summary>
/// The permissions this module's endpoints declare.
/// </summary>
/// <remarks>
/// <para>
/// Declared here and mirrored in the Identity module's permission catalogue, which is what the admin
/// UI lists and what a role grants. The duplication is deliberate and is what the module boundary
/// costs: a module may not reference another, so the two lists are kept in step by a test that
/// asserts every permission an endpoint asks for appears in the catalogue — the arrangement every
/// module since Media has used.
/// </para>
/// <para>
/// Five, and the split is the point of the module. Writing a page and putting one live are separate
/// permissions because that separation is the entire value of an editorial workflow: an agency, an
/// intern or a seasonal hire can be given the first without the second, and a page then cannot reach
/// a shopper without somebody who holds the second looking at it. Collapsing them would leave the
/// <c>InReview</c> state as decoration.
/// </para>
/// <para>
/// There is no read permission for the storefront surface at all. Reading a published page is
/// anonymous, as reading a shop window is.
/// </para>
/// </remarks>
internal static class ContentPermissions
{
    /// <summary>
    /// Write pages, menus, banners, collections and redirects; read any of them, published or not.
    /// </summary>
    /// <remarks>
    /// The merchandiser's permission, and it covers everything except putting a page live. It
    /// includes reading drafts, which is why it is one permission rather than a read and a write: a
    /// draft that could be read but not edited is a preview, and the preview endpoint already exists
    /// for that.
    /// </remarks>
    public const string ContentManage = "content.content.manage";

    /// <summary>
    /// Publish, schedule, unpublish, archive and roll back a page.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="ContentManage"/>, and the only reason the editorial
    /// workflow means anything. Holding it is what makes somebody a publisher in the transition
    /// table; without it an editor can take a page as far as review and no further.
    /// </remarks>
    public const string ContentPublish = "content.page.publish";

    /// <summary>
    /// Write a custom-HTML block.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its own permission because it is its own risk. Every other block is a typed configuration
    /// document rendered by a component that was written for it; this one is arbitrary markup
    /// executed in every shopper's browser, which is a stored cross-site-scripting vector
    /// (docs/07-security-compliance.md §3).
    /// </para>
    /// <para>
    /// The person who writes the store's copy is not automatically the person who may embed a script,
    /// and on most teams they should not be. It is paired with the <c>content.custom-html</c> flag:
    /// the permission says who, the flag says whether at all.
    /// </para>
    /// </remarks>
    public const string CustomHtmlWrite = "content.custom-html.write";

    /// <summary>
    /// Edit the redirect manager.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ContentManage"/> because a redirect is not content — it is routing.
    /// A wrong row here sends every visitor arriving on a good URL somewhere else, including, if
    /// somebody is careless, off the site entirely; and unlike a bad page it does so without anything
    /// on screen looking wrong to the person who wrote it.
    /// </remarks>
    public const string RedirectManage = "content.redirect.manage";

    /// <summary>
    /// Read the SEO surfaces the admin app shows: the sitemap index, the robots document and a page's
    /// structured data.
    /// </summary>
    /// <remarks>
    /// A read permission rather than a write one, because there is nothing to write: the SEO
    /// <em>settings</em> are the Platform module's, guarded by its own permission, and everything
    /// here is computed from them. It exists so that the admin app's SEO screen can show an operator
    /// exactly what a crawler will be served without giving them the settings permission as well.
    /// </remarks>
    public const string SeoRead = "content.seo.read";
}
