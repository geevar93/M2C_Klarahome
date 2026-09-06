using KlaraHome.Contracts.Catalog;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Infrastructure.Directory;

/// <summary>
/// The published read of the category tree (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The third and narrowest seam onto this schema, added at Step 20 for the CMS.
/// <see cref="ProductCatalogDirectory"/> answers for an offer's money and tax,
/// <see cref="ProductProjectionSource"/> fills a read-model, and this one answers for the tree — the
/// URLs a sitemap has to list, and the ancestors a breadcrumb has to name.
/// </para>
/// <para>
/// The ancestry walk is a single query and not a recursive one, because the tree is materialised: a
/// category's <c>path</c> is <c>/id/id/</c> from the root, so the ancestors are already named in the
/// row itself and the only work left is fetching them and putting them back in order.
/// </para>
/// </remarks>
/// <param name="context">The Catalog data context.</param>
internal sealed class CatalogTaxonomyDirectory(CatalogDbContext context) : ICatalogTaxonomy
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CategoryNode>> ListCategoriesAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var rows = await context.Categories
            .AsNoTracking()
            .Where(category => !activeOnly || category.IsActive)
            // Path order puts a parent before every one of its children, which is what a caller
            // building a tree in one pass needs and what a sitemap wants anyway.
            .OrderBy(category => category.Path)
            .Select(category => new CategoryNode(
                category.Id,
                category.ParentId,
                category.Name,
                category.Slug,
                category.Path,
                category.Level,
                category.IsActive,
                category.ImageFileId,
                category.UpdatedAt ?? category.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CategoryNode>> FindAncestryAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var category = await context.Categories
            .AsNoTracking()
            .Where(row => row.Id == categoryId)
            .Select(row => new { row.Path })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (category is null)
        {
            // An unknown category is a breadcrumb that is not drawn, never an error. The page whose
            // category was deleted still has to render.
            return [];
        }

        var ids = category.Path
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => Guid.TryParse(segment, out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        var nodes = await context.Categories
            .AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .Select(row => new CategoryNode(
                row.Id,
                row.ParentId,
                row.Name,
                row.Slug,
                row.Path,
                row.Level,
                row.IsActive,
                row.ImageFileId,
                row.UpdatedAt ?? row.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Back into path order. The `IN` came back in whatever order the index gave, and a breadcrumb
        // rendered in that order is a breadcrumb that occasionally reads backwards.
        var byId = nodes.ToDictionary(node => node.Id);

        return [.. ids.Where(byId.ContainsKey).Select(id => byId[id])];
    }
}
