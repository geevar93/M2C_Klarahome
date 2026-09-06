namespace KlaraHome.Contracts.Catalog;

/// <summary>
/// A category, as a module that does not own the catalogue sees it.
/// </summary>
/// <remarks>
/// Deliberately the tree's <em>shape</em> and its URLs, and nothing about the products in it. A
/// caller that wants to know what is in a category is asking a question the search projection
/// answers; this contract exists for the two jobs that need the node itself — listing every URL a
/// crawler should know about, and naming the ancestors of one node so a breadcrumb can be drawn.
/// </remarks>
/// <param name="Id">The category.</param>
/// <param name="ParentId">The category above it, or null at the root.</param>
/// <param name="Name">Its name, as a shopper reads it.</param>
/// <param name="Slug">Its URL segment.</param>
/// <param name="Path">Its materialised path, <c>/id/id/</c>, ancestors first.</param>
/// <param name="Level">How deep it is. Zero at the root.</param>
/// <param name="IsActive">Whether the storefront serves it.</param>
/// <param name="ImageFileId">Its tile image, or null.</param>
/// <param name="UpdatedAt">When it last changed, for a sitemap's <c>lastmod</c>.</param>
public sealed record CategoryNode(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Slug,
    string Path,
    int Level,
    bool IsActive,
    Guid? ImageFileId,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Reads the catalogue's category tree from outside the Catalog module
/// (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// Added at Step 20 for the CMS, which has two jobs that need category nodes and no way to join to
/// <c>catalog.categories</c>. The sitemap has to list every browsable URL the store has, and a
/// category page's <c>BreadcrumbList</c> has to name the ancestors between the home page and the
/// node — neither is answerable from the search projection, which holds a leaf's name and a path of
/// ids but not the names along it.
/// </para>
/// <para>
/// It is the third seam onto the catalogue and the narrowest.
/// <see cref="IProductCatalog"/> prices a line, <see cref="IProductProjectionSource"/> fills a
/// read-model, and this one answers for the tree. Keeping them apart is what stops a sitemap paying
/// to read a product's tax facts.
/// </para>
/// <para>
/// Brands are deliberately absent. They have slugs and would be easy to add, and the storefront has
/// no route that serves one (docs/05-frontend-architecture.md §3.2) — so listing them would put URLs
/// in a sitemap that answer 404, which is worse for a crawler's opinion of the site than listing
/// nothing.
/// </para>
/// <para>
/// The listing returns the whole tree rather than a page. A category tree is tens of rows; paging it
/// would be a cursor nobody needed and a second round trip on the one call a sitemap makes.
/// </para>
/// </remarks>
public interface ICatalogTaxonomy
{
    /// <summary>
    /// Every category, ordered by path so a caller reading it in order sees a parent before its
    /// children.
    /// </summary>
    /// <param name="activeOnly">Whether to leave out categories the storefront does not serve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<CategoryNode>> ListCategoriesAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One category and every ancestor above it, root first, ending with the category itself.
    /// </summary>
    /// <remarks>
    /// The breadcrumb, in the order it is rendered. Empty when the category is unknown — a page whose
    /// category has since been deleted renders without a breadcrumb rather than failing.
    /// </remarks>
    /// <param name="categoryId">The category.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<CategoryNode>> FindAncestryAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default);
}
