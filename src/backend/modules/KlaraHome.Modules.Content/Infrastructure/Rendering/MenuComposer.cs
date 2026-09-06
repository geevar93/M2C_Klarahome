using KlaraHome.Contracts.Catalog;
using KlaraHome.Modules.Content.Application;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.Modules.Content.Infrastructure.Seo;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Content.Infrastructure.Rendering;

/// <summary>
/// Turns a stored menu into the nested, resolved tree a storefront renders.
/// </summary>
/// <remarks>
/// <para>
/// Two jobs, and both of them exist so the Angular shell does not have to do them. The flat list of
/// items is <b>nested</b> into a tree, because a header component renders a tree and reconstructing
/// one from parent ids in a template is the kind of code that ends up copied into three components.
/// And every item's target is <b>resolved into a path</b>, because that is the entire point of
/// storing a target rather than a URL: an item pointing at a category keeps working when that
/// category is renamed, and it only keeps working if somebody looks the slug up at render time.
/// </para>
/// <para>
/// An item whose target has since been deleted or unpublished is dropped, along with anything beneath
/// it. A menu entry that leads to a 404 is worse than a menu entry that is not there — the first is a
/// broken shop, the second is one an editor has not finished.
/// </para>
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="taxonomy">Resolves a category target into its slug.</param>
/// <param name="renderer">Resolves item icons.</param>
internal sealed class MenuComposer(
    ContentDbContext context,
    ICatalogTaxonomy taxonomy,
    ContentRenderer renderer)
{
    /// <summary>Composes a menu for the storefront.</summary>
    /// <param name="menu">The menu, with its items loaded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<StoreMenuResponse> ComposeAsync(Menu menu, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(menu);

        var visible = menu.Items.Where(item => item.IsVisible).ToList();

        var hrefs = await ResolveHrefsAsync(visible, cancellationToken).ConfigureAwait(false);

        var icons = await renderer
            .ResolveImagesAsync(
                [.. visible.Where(item => item.IconFileId is not null).Select(item => item.IconFileId!.Value)],
                cancellationToken)
            .ConfigureAwait(false);

        // An item whose target no longer resolves is dropped, and so is everything beneath it: a
        // submenu hanging off a deleted category is a column of links to a page that has gone.
        var dead = visible
            .Where(item => item.LinkType != MenuLinkType.None && !hrefs.ContainsKey(item.Id))
            .Select(item => item.Id)
            .ToHashSet();

        var live = visible.Where(item => !dead.Contains(item.Id) && !IsOrphaned(item, dead, visible)).ToList();

        return new StoreMenuResponse(
            menu.Code,
            menu.Name,
            menu.Placement,
            Nest(live, parentId: null, hrefs, icons));
    }

    /// <summary>Whether any ancestor of an item has been dropped.</summary>
    private static bool IsOrphaned(MenuItem item, HashSet<Guid> dead, IReadOnlyList<MenuItem> all)
    {
        var parentId = item.ParentId;

        while (parentId is { } id)
        {
            if (dead.Contains(id))
            {
                return true;
            }

            parentId = all.FirstOrDefault(candidate => candidate.Id == id)?.ParentId;
        }

        return false;
    }

    /// <summary>Builds one level of the tree, recursing into each item's children.</summary>
    private static IReadOnlyList<StoreMenuItemResponse> Nest(
        IReadOnlyList<MenuItem> items,
        Guid? parentId,
        IReadOnlyDictionary<Guid, string> hrefs,
        IReadOnlyDictionary<Guid, ContentImageResponse> icons)
        =>
        [
            .. items
                .Where(item => item.ParentId == parentId)
                .OrderBy(item => item.Position)
                .Select(item => new StoreMenuItemResponse(
                    item.Label,
                    hrefs.GetValueOrDefault(item.Id),
                    item.OpensInNewTab,
                    item.IconFileId is { } iconId ? icons.GetValueOrDefault(iconId) : null,
                    item.Badge,
                    Nest(items, item.Id, hrefs, icons))),
        ];

    /// <summary>
    /// Resolves every item's target into a path, in three queries rather than one per item.
    /// </summary>
    /// <remarks>
    /// A header is rendered on every page of the store, including during server-side rendering, so
    /// this is one of the hottest reads in the platform. Grouping the targets by kind and resolving
    /// each kind in a single query is the difference between three round trips and thirty.
    /// </remarks>
    private async Task<Dictionary<Guid, string>> ResolveHrefsAsync(
        IReadOnlyList<MenuItem> items,
        CancellationToken cancellationToken)
    {
        var hrefs = new Dictionary<Guid, string>();

        var pageIds = Targets(items, MenuLinkType.Page);
        var collectionIds = Targets(items, MenuLinkType.Collection);
        var categoryIds = Targets(items, MenuLinkType.Category);

        var pages = pageIds.Count == 0
            ? []
            : await context.Pages
                .AsNoTracking()
                .Where(page => pageIds.Contains(page.Id) && page.Status == PageStatus.Published)
                .Select(page => new { page.Id, page.Type, page.Slug })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var collections = collectionIds.Count == 0
            ? []
            : await context.Collections
                .AsNoTracking()
                .Where(collection => collectionIds.Contains(collection.Id) && collection.IsActive)
                .Select(collection => new { collection.Id, collection.Slug })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var categories = categoryIds.Count == 0
            ? []
            : (await taxonomy.ListCategoriesAsync(activeOnly: true, cancellationToken).ConfigureAwait(false))
                .Where(category => categoryIds.Contains(category.Id))
                .ToList();

        foreach (var item in items)
        {
            var href = item.LinkType switch
            {
                MenuLinkType.Url => item.Url,

                MenuLinkType.Page => pages
                    .Where(page => page.Id == item.TargetId)
                    .Select(page => StorefrontRoutes.Page(page.Type, page.Slug))
                    .FirstOrDefault(),

                MenuLinkType.Collection => collections
                    .Where(collection => collection.Id == item.TargetId)
                    .Select(collection => StorefrontRoutes.Collection(collection.Slug))
                    .FirstOrDefault(),

                MenuLinkType.Category => categories
                    .Where(category => category.Id == item.TargetId)
                    .Select(category => StorefrontRoutes.Category(category.Slug))
                    .FirstOrDefault(),

                _ => null,
            };

            if (href is { Length: > 0 })
            {
                hrefs[item.Id] = href;
            }
        }

        return hrefs;
    }

    private static List<Guid> Targets(IReadOnlyList<MenuItem> items, MenuLinkType linkType)
        =>
        [
            .. items
                .Where(item => item.LinkType == linkType && item.TargetId is not null)
                .Select(item => item.TargetId!.Value)
                .Distinct(),
        ];
}
