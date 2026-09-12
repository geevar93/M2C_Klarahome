using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.Modules.Content.Infrastructure.Rendering;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Content.Application.Storefront;

/// <summary>Reads one published CMS page by its address.</summary>
/// <param name="Slug">Its address.</param>
internal sealed record GetStorePageQuery(string? Slug) : IQuery<StorePageResponse>;

/// <summary>Reads the published home page.</summary>
internal sealed record GetHomePageQuery : IQuery<StorePageResponse>;

/// <summary>Reads one menu by its code, or failing that by the placement the code names.</summary>
/// <param name="Code">Its stable key, or a placement such as <c>header</c>.</param>
internal sealed record GetStoreMenuQuery(string? Code) : IQuery<StoreMenuResponse>;

/// <summary>Reads the banners that should render in one placement right now.</summary>
/// <param name="Placement">The slot, or null for every live banner.</param>
internal sealed record GetStoreBannersQuery(string? Placement) : IQuery<IReadOnlyList<StoreBannerResponse>>;

/// <summary>Reads a collection's landing page and a page of its products.</summary>
/// <param name="Slug">Its address.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many products to return.</param>
internal sealed record GetStoreCollectionQuery(string? Slug, string? Cursor, int? Size)
    : IQuery<StoreCollectionResponse>;

/// <summary>Lists the published blog posts, newest first.</summary>
/// <param name="Tag">Only the posts carrying one tag.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListBlogPostsQuery(string? Tag, string? Cursor, int? Size)
    : IQuery<PagedResult<BlogCardResponse>>;

/// <summary>Reads one published blog post.</summary>
/// <param name="Slug">Its address.</param>
internal sealed record GetBlogPostQuery(string? Slug) : IQuery<StorePageResponse>;

/// <summary>Reads one published CMS page.</summary>
/// <remarks>
/// Published, and nothing else. There is no <c>?preview=</c> here and deliberately so: an unpublished
/// page is reached through the admin preview, with the caller's own token and their own permission,
/// rather than through a query parameter that would be one guessed value away from serving a draft to
/// anybody.
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="composer">Resolves and windows the blocks.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class GetStorePageQueryHandler(ContentDbContext context, PageComposer composer, IClock clock)
    : IQueryHandler<GetStorePageQuery, StorePageResponse>
{
    public async Task<Result<StorePageResponse>> HandleAsync(
        GetStorePageQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await context.Pages
            .AsNoTracking()
            .Include(row => row.Blocks)
            .FirstOrDefaultAsync(
                row => row.Slug == query.Slug
                       && row.Status == PageStatus.Published
                       && row.Type != PageType.Blog,
                cancellationToken)
            .ConfigureAwait(false);

        if (page is null)
        {
            return Result.Failure<StorePageResponse>(ContentErrors.NotFound("page"));
        }

        var composed = await composer.ComposeAsync(page, clock.UtcNow, cancellationToken).ConfigureAwait(false);

        return Result.Success(composed);
    }
}

/// <summary>Reads the published home page.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="composer">Resolves and windows the blocks.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class GetHomePageQueryHandler(ContentDbContext context, PageComposer composer, IClock clock)
    : IQueryHandler<GetHomePageQuery, StorePageResponse>
{
    public async Task<Result<StorePageResponse>> HandleAsync(
        GetHomePageQuery query,
        CancellationToken cancellationToken)
    {
        var page = await context.Pages
            .AsNoTracking()
            .Include(row => row.Blocks)
            .FirstOrDefaultAsync(
                row => row.Type == PageType.Home && row.Status == PageStatus.Published,
                cancellationToken)
            .ConfigureAwait(false);

        if (page is null)
        {
            // A store with no published home page is a store that has not been set up yet, not an
            // error. The storefront renders its own default shell on a 404 here, which is what lets a
            // fresh deployment be browsable before anybody has opened the CMS.
            return Result.Failure<StorePageResponse>(ContentErrors.NotFound("home page"));
        }

        var composed = await composer.ComposeAsync(page, clock.UtcNow, cancellationToken).ConfigureAwait(false);

        return Result.Success(composed);
    }
}

/// <summary>Reads one menu by its code.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="composer">Nests the items and resolves their targets.</param>
internal sealed class GetStoreMenuQueryHandler(ContentDbContext context, MenuComposer composer)
    : IQueryHandler<GetStoreMenuQuery, StoreMenuResponse>
{
    public async Task<Result<StoreMenuResponse>> HandleAsync(
        GetStoreMenuQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var code = query.Code?.Trim() ?? string.Empty;

        // The placement is matched as a literal, so a code arriving from the URL cannot widen it.
        var placement = code
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        // By code first, which is the contract; by placement second, because the storefront asks for
        // "header" and an editor who called their navigation "Cosmetics" and placed it in the header
        // meant it for the header. The alternative is a live shop with an empty navigation bar.
        var menu = await context.Menus
            .AsNoTracking()
            .Include(row => row.Items)
            .Where(row => row.IsActive
                && (row.Code == code
                    || (row.Placement != null && EF.Functions.ILike(row.Placement, placement, "\\"))))
            .OrderBy(row => row.Code == code ? 0 : 1)
            .ThenBy(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (menu is null)
        {
            return Result.Failure<StoreMenuResponse>(ContentErrors.NotFound("menu"));
        }

        var composed = await composer.ComposeAsync(menu, cancellationToken).ConfigureAwait(false);

        return Result.Success(composed);
    }
}

/// <summary>
/// Reads the banners that should render right now, for this caller.
/// </summary>
/// <remarks>
/// The window and the audience are applied here rather than by the storefront, and that is what makes
/// scheduling real: a banner that ends at midnight stops being served at midnight, whatever any
/// client happens to have cached. The audience is coarse — signed in or not — because that is what a
/// welcome offer needs and because a finer one would need the segment vocabulary the feature flags
/// already own.
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves the images.</param>
/// <param name="caller">Decides which audience this visitor belongs to.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class GetStoreBannersQueryHandler(
    ContentDbContext context,
    ContentRenderer renderer,
    ICallerContext caller,
    IClock clock)
    : IQueryHandler<GetStoreBannersQuery, IReadOnlyList<StoreBannerResponse>>
{
    public async Task<Result<IReadOnlyList<StoreBannerResponse>>> HandleAsync(
        GetStoreBannersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = context.Banners.AsNoTracking().Where(banner => banner.IsActive);

        if (Enum.TryParse<BannerPlacement>(query.Placement, ignoreCase: true, out var placement))
        {
            rows = rows.Where(banner => banner.Placement == placement);
        }

        var now = clock.UtcNow;

        // The window is applied in the database so a store with two years of finished campaigns does
        // not read them all into memory to throw them away.
        var candidates = await rows
            .Where(banner => banner.StartsAt == null || banner.StartsAt <= now)
            .Where(banner => banner.EndsAt == null || banner.EndsAt > now)
            .OrderByDescending(banner => banner.Priority)
            .ThenByDescending(banner => banner.StartsAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var audience = caller.IsAuthenticated ? BannerAudience.SignedIn : BannerAudience.Anonymous;
        var live = candidates.Where(banner => banner.IsLiveFor(now, audience)).ToList();

        var images = await renderer
            .ResolveImagesAsync(Banners.BannerImages.Of(live), cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<StoreBannerResponse>>(
        [
            .. live.Select(banner => ContentProjection.ToStoreBanner(
                banner,
                Banners.BannerImages.Find(images, banner.MediaFileId, banner.AltText),
                Banners.BannerImages.Find(images, banner.MobileMediaFileId, banner.AltText))),
        ]);
    }
}

/// <summary>Reads a collection's landing page and a page of its products.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves the masthead and the product cards.</param>
/// <param name="options">The page-size ceiling.</param>
internal sealed class GetStoreCollectionQueryHandler(
    ContentDbContext context,
    ContentRenderer renderer,
    IOptionsMonitor<ContentOptions> options)
    : IQueryHandler<GetStoreCollectionQuery, StoreCollectionResponse>
{
    public async Task<Result<StoreCollectionResponse>> HandleAsync(
        GetStoreCollectionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var collection = await context.Collections
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Slug == query.Slug && row.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return Result.Failure<StoreCollectionResponse>(ContentErrors.NotFound("collection"));
        }

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.CurrentValue.MaxPageSize);

        var rows = context.CollectionItems
            .AsNoTracking()
            .Where(item => item.CollectionId == collection.Id);

        if (Cursor.TryDecode(query.Cursor, out var key) && int.TryParse(key, out var after))
        {
            rows = rows.Where(item => item.Position > after);
        }

        var page = await rows
            .OrderBy(item => item.Position)
            .Take(size + 1)
            .Select(item => new { item.ProductId, item.Position })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var slice = page.Take(size).ToList();

        var cards = await renderer
            .ResolveProductsAsync([.. slice.Select(row => row.ProductId)], cancellationToken)
            .ConfigureAwait(false);

        var hero = await renderer
            .ResolveImageAsync(collection.HeroImageFileId, collection.Name, cancellationToken)
            .ConfigureAwait(false);

        var ogImage = await renderer
            .ResolveImageAsync(collection.Seo.OgImageFileId, collection.Name, cancellationToken)
            .ConfigureAwait(false);

        var next = hasMore && slice.Count > 0
            ? Cursor.Encode(slice[^1].Position.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : null;

        return Result.Success(new StoreCollectionResponse(
            collection.Slug,
            collection.Name,
            collection.Description,
            ContentProjection.ToSeo(collection.Seo, ogImage),
            hero,
            collection.ItemCount,
            cards,
            next));
    }
}

/// <summary>Lists the published blog posts, newest first.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves the cover images.</param>
internal sealed class ListBlogPostsQueryHandler(ContentDbContext context, ContentRenderer renderer)
    : IQueryHandler<ListBlogPostsQuery, PagedResult<BlogCardResponse>>
{
    public async Task<Result<PagedResult<BlogCardResponse>>> HandleAsync(
        ListBlogPostsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        var rows = context.Pages
            .AsNoTracking()
            .Where(page => page.Type == PageType.Blog && page.Status == PageStatus.Published);

        if (query.Tag is { Length: > 0 } tag)
        {
            var folded = tag.Trim().ToLowerInvariant();

            rows = rows.Where(page => page.Tags.Contains(folded));
        }

        // Paged on the publish date, which is what a blog index is ordered by; the id would order by
        // when the draft was created, which is a different and much less useful sequence.
        if (Cursor.TryDecode(query.Cursor, out var key)
            && DateTimeOffset.TryParse(
                key,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var after))
        {
            rows = rows.Where(page => page.PublishedAt < after);
        }

        var page = await rows
            .OrderByDescending(row => row.PublishedAt)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var posts = page.Take(size).ToList();

        var covers = await renderer
            .ResolveImagesAsync(
                [.. posts.Where(post => post.CoverImageFileId is not null)
                    .Select(post => post.CoverImageFileId!.Value)],
                cancellationToken)
            .ConfigureAwait(false);

        var items = posts
            .Select(post => new BlogCardResponse(
                post.Slug,
                post.Title,
                post.Summary,
                post.Author,
                post.Tags,
                post.CoverImageFileId is { } id && covers.TryGetValue(id, out var cover)
                    ? cover with { Alt = post.Title }
                    : null,
                post.PublishedAt))
            .ToArray();

        var next = hasMore && posts.Count > 0 && posts[^1].PublishedAt is { } last
            ? Cursor.Encode(last.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
            : null;

        return Result.Success(new PagedResult<BlogCardResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one published blog post.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="composer">Resolves and windows the blocks.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class GetBlogPostQueryHandler(ContentDbContext context, PageComposer composer, IClock clock)
    : IQueryHandler<GetBlogPostQuery, StorePageResponse>
{
    public async Task<Result<StorePageResponse>> HandleAsync(
        GetBlogPostQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var post = await context.Pages
            .AsNoTracking()
            .Include(row => row.Blocks)
            .FirstOrDefaultAsync(
                row => row.Slug == query.Slug
                       && row.Type == PageType.Blog
                       && row.Status == PageStatus.Published,
                cancellationToken)
            .ConfigureAwait(false);

        if (post is null)
        {
            return Result.Failure<StorePageResponse>(ContentErrors.NotFound("post"));
        }

        var composed = await composer.ComposeAsync(post, clock.UtcNow, cancellationToken).ConfigureAwait(false);

        return Result.Success(composed);
    }
}
