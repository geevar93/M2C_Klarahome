using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Content.Application;
using KlaraHome.Modules.Content.Application.Banners;
using KlaraHome.Modules.Content.Application.Collections;
using KlaraHome.Modules.Content.Application.Menus;
using KlaraHome.Modules.Content.Application.Pages;
using KlaraHome.Modules.Content.Application.Redirects;
using KlaraHome.Modules.Content.Application.Seo;
using KlaraHome.Modules.Content.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Content.Endpoints;

/// <summary>The body of a new page.</summary>
/// <param name="Slug">Its address, or null to derive one from the title.</param>
/// <param name="Type">What it is for.</param>
/// <param name="Title">Its title.</param>
/// <param name="Summary">Its summary.</param>
internal sealed record CreatePageBody(string? Slug, PageType Type, string? Title, string? Summary);

/// <summary>The body of a page edit.</summary>
/// <param name="Slug">Its address.</param>
/// <param name="Title">Its title.</param>
/// <param name="Summary">Its summary.</param>
/// <param name="Seo">What a crawler is told.</param>
/// <param name="CoverImageFileId">Its cover image.</param>
/// <param name="Author">The byline, for a blog page.</param>
/// <param name="Tags">Its tags.</param>
/// <param name="Blocks">Its blocks, or null to leave them as they are.</param>
internal sealed record UpdatePageBody(
    string? Slug,
    string? Title,
    string? Summary,
    SeoBody? Seo,
    Guid? CoverImageFileId,
    string? Author,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<BlockBody>? Blocks);

/// <summary>The body of a workflow move.</summary>
/// <param name="Status">Where the page is being moved to.</param>
/// <param name="ScheduledAt">When it should go live, for a schedule.</param>
/// <param name="Note">Why, recorded on the version a publish snapshots.</param>
internal sealed record TransitionPageBody(PageStatus Status, DateTimeOffset? ScheduledAt, string? Note);

/// <summary>The body of a new menu.</summary>
/// <param name="Code">Its stable key.</param>
/// <param name="Name">What an editor calls it.</param>
/// <param name="Placement">Where the storefront renders it.</param>
internal sealed record CreateMenuBody(string? Code, string? Name, string? Placement);

/// <summary>The body of a menu edit.</summary>
/// <param name="Name">What an editor calls it.</param>
/// <param name="Placement">Where it renders.</param>
/// <param name="IsActive">Whether it is served.</param>
/// <param name="Items">The whole tree, parents before their children.</param>
internal sealed record UpdateMenuBody(
    string? Name,
    string? Placement,
    bool IsActive,
    IReadOnlyList<MenuItemBody>? Items);

/// <summary>The body of a banner, new or edited.</summary>
/// <param name="Name">What an editor calls it.</param>
/// <param name="Placement">Where it appears. Ignored on an edit — a placement is not editable.</param>
/// <param name="MediaFileId">The desktop image.</param>
/// <param name="MobileMediaFileId">The mobile image.</param>
/// <param name="Message">The words, for an announcement bar.</param>
/// <param name="AltText">Its alt text.</param>
/// <param name="Link">Where clicking it goes.</param>
/// <param name="CtaLabel">The button's wording.</param>
/// <param name="Priority">Which banner wins the placement.</param>
/// <param name="StartsAt">When it starts.</param>
/// <param name="EndsAt">When it stops.</param>
/// <param name="Audience">Who sees it.</param>
/// <param name="IsActive">Whether it is switched on.</param>
internal sealed record BannerBody(
    string? Name,
    BannerPlacement Placement,
    Guid? MediaFileId,
    Guid? MobileMediaFileId,
    string? Message,
    string? AltText,
    string? Link,
    string? CtaLabel,
    int Priority,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    BannerAudience? Audience,
    bool IsActive);

/// <summary>The body of a banner switch.</summary>
/// <param name="IsActive">Whether it is switched on.</param>
internal sealed record SetBannerActiveBody(bool IsActive);

/// <summary>The body of a new collection.</summary>
/// <param name="Slug">Its address, or null to derive one from the name.</param>
/// <param name="Name">What a shopper reads.</param>
/// <param name="Description">The copy beneath the heading.</param>
internal sealed record CreateCollectionBody(string? Slug, string? Name, string? Description);

/// <summary>The body of a collection edit.</summary>
/// <param name="Slug">Its address.</param>
/// <param name="Name">What a shopper reads.</param>
/// <param name="Description">The copy beneath the heading.</param>
/// <param name="Seo">What a crawler is told.</param>
/// <param name="HeroImageFileId">The masthead image.</param>
/// <param name="IsActive">Whether the storefront serves it.</param>
/// <param name="IsListed">Whether the sitemap lists it.</param>
internal sealed record UpdateCollectionBody(
    string? Slug,
    string? Name,
    string? Description,
    SeoBody? Seo,
    Guid? HeroImageFileId,
    bool IsActive,
    bool IsListed);

/// <summary>The body of a rule.</summary>
/// <param name="Rule">The rule, or null to make the collection hand-picked again.</param>
internal sealed record SetCollectionRuleBody(CollectionRuleBody? Rule);

/// <summary>The body of a membership edit.</summary>
/// <param name="Items">The products, in the order they should render.</param>
internal sealed record SetCollectionItemsBody(IReadOnlyList<CollectionItemBody>? Items);

/// <summary>The body of a single addition to a collection.</summary>
/// <param name="ProductId">The product to add.</param>
/// <param name="IsPinned">Whether it is fixed where it lands.</param>
internal sealed record AddCollectionItemBody(Guid ProductId, bool IsPinned);

/// <summary>The body of a new redirect.</summary>
/// <param name="FromPath">The path being asked for.</param>
/// <param name="ToPath">Where it goes.</param>
/// <param name="StatusCode">
/// 301, 302 or 410. A number rather than an enum, deliberately: it is the HTTP status the
/// storefront actually answers with, and spelling it <c>MovedPermanently</c> on the wire would put
/// a name in front of the one value every reader of a redirect table already knows by its number.
/// </param>
/// <param name="Note">Why.</param>
internal sealed record CreateRedirectBody(string? FromPath, string? ToPath, int StatusCode, string? Note);

/// <summary>The body of a redirect edit. The path it matches is not editable.</summary>
/// <param name="ToPath">Where it goes.</param>
/// <param name="StatusCode">301, 302 or 410. A number for the reason a create's is.</param>
/// <param name="IsActive">Whether it is applied.</param>
/// <param name="Note">Why.</param>
internal sealed record UpdateRedirectBody(string? ToPath, int StatusCode, bool IsActive, string? Note);

/// <summary>
/// The content back office (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// <para>
/// The whole surface a merchandiser works on, and the permission split across it is the one thing
/// worth reading twice. Writing anything needs <c>content.content.manage</c>; moving a page along the
/// workflow needs <c>content.page.publish</c>; the redirect manager has its own permission because a
/// redirect is routing rather than content. Nothing here is vendor-scoped, because a seller does not
/// merchandise the platform's storefront.
/// </para>
/// <para>
/// The preview is here rather than on the store surface, and that is a security decision. A preview
/// token is a URL that grants access to unpublished content and gets pasted into chat threads; this
/// needs the caller's own bearer token and their own permission on every request, and the storefront's
/// renderer holds one when an editor asks to see a draft.
/// </para>
/// </remarks>
internal static class AdminContentEndpoints
{
    /// <summary>Maps the content back office beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminContentEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        MapPages(admin);
        MapMenus(admin);
        MapBanners(admin);
        MapCollections(admin);
        MapRedirects(admin);
        MapSeo(admin);

        return admin;
    }

    /// <summary>The pages, their blocks, their workflow and their history.</summary>
    private static void MapPages(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/pages").WithTags("Content");

        group.MapGet("/", async (
                string? search,
                PageType? type,
                PageStatus? status,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListPagesQuery(search, type, status, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListPages")
            .WithSummary("The pages an editor can work on, newest first.")
            .RequirePermission(ContentPermissions.ContentManage)
            .Produces<PagedResult<PageSummaryResponse>>();

        group.MapGet("/block-types", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetBlockTypesQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListBlockTypes")
            .WithSummary("The block types this storefront renders, with their schemas.")
            .RequirePermission(ContentPermissions.ContentManage)
            .Produces<IReadOnlyList<BlockTypeResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetPageQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetPage")
            .WithSummary("One page, blocks and all, with the moves this caller may make.")
            .RequirePermission(ContentPermissions.ContentManage)
            .Produces<PageResponse>();

        group.MapPost("/", async (CreatePageBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreatePageCommand(body.Slug, body.Type, body.Title, body.Summary);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCreatePage")
            .WithSummary("Opens a page, as a draft.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PageResponse>();

        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdatePageBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdatePageCommand(
                    id,
                    body.Slug,
                    body.Title,
                    body.Summary,
                    body.Seo,
                    body.CoverImageFileId,
                    body.Author,
                    body.Tags,
                    body.Blocks);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUpdatePage")
            .WithSummary("Rewrites a page. A save is never a publish.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PageResponse>();

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeletePageCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminDeletePage")
            .WithSummary("Removes a page that has never been published. Archive the rest.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);

        // The workflow. One endpoint rather than four verbs, because it is one transition table and
        // an admin screen draws its buttons from the list of moves the page already came back with.
        group.MapPost("/{id:guid}/transition", async (
                Guid id,
                TransitionPageBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new TransitionPageCommand(id, body.Status, body.ScheduledAt, body.Note);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminTransitionPage")
            .WithSummary("Submits, publishes, schedules, unpublishes or archives a page.")
            .RequirePermission(ContentPermissions.ContentPublish)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PageResponse>();

        group.MapGet("/{id:guid}/versions", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListPageVersionsQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListPageVersions")
            .WithSummary("A page's version history, newest first.")
            .RequirePermission(ContentPermissions.ContentManage)
            .Produces<IReadOnlyList<PageVersionSummaryResponse>>();

        group.MapGet("/{id:guid}/versions/{version:int}", async (
                Guid id,
                int version,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetPageVersionQuery(id, version), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetPageVersion")
            .WithSummary("One version of a page, in full.")
            .RequirePermission(ContentPermissions.ContentManage)
            .Produces<PageVersionResponse>();

        group.MapPost("/{id:guid}/versions/{version:int}/rollback", async (
                Guid id,
                int version,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RollbackPageCommand(id, version), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRollbackPage")
            .WithSummary("Restores a version's content, as a new version. Never changes the status.")
            .RequirePermission(ContentPermissions.ContentPublish)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PageResponse>();

        group.MapGet("/{id:guid}/preview", async (
                Guid id,
                int? version,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new PreviewPageQuery(id, version), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPreviewPage")
            .WithSummary("Renders a page exactly as the storefront would, whatever its status.")
            .RequirePermission(ContentPermissions.ContentManage)
            .Produces<StorePageResponse>();
    }

    /// <summary>The navigation menus and the footer.</summary>
    private static void MapMenus(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/menus").WithTags("Content");

        group.MapGet("/", async (string? placement, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListMenusQuery(placement), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListMenus")
            .WithSummary("The store's menus.")
            .RequirePermission(ContentPermissions.ContentManage)
            .Produces<IReadOnlyList<MenuSummaryResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetMenuQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetMenu")
            .WithSummary("One menu, items and all.")
            .RequirePermission(ContentPermissions.ContentManage)
            .Produces<MenuResponse>();

        group.MapPost("/", async (CreateMenuBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateMenuCommand(body.Code, body.Name, body.Placement);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCreateMenu")
            .WithSummary("Opens a menu.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<MenuResponse>();

        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateMenuBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateMenuCommand(id, body.Name, body.Placement, body.IsActive, body.Items);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUpdateMenu")
            .WithSummary("Rewrites a menu and its whole tree.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<MenuResponse>();

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteMenuCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminDeleteMenu")
            .WithSummary("Removes a menu.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);
    }

    /// <summary>The banners and the announcement bar.</summary>
    private static void MapBanners(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/banners").WithTags("Content");

        group.MapGet("/", async (
                BannerPlacement? placement,
                bool? activeOnly,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListBannersQuery(placement, activeOnly, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListBanners")
            .WithSummary("The store's banners, newest first.")
            .RequirePermission(ContentPermissions.ContentManage)
            .Produces<PagedResult<BannerResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetBannerQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetBanner")
            .WithSummary("One banner.")
            .RequirePermission(ContentPermissions.ContentManage)
            .Produces<BannerResponse>();

        group.MapPost("/", async (BannerBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateBannerCommand(
                    body.Name,
                    body.Placement,
                    body.MediaFileId,
                    body.MobileMediaFileId,
                    body.Message,
                    body.AltText,
                    body.Link,
                    body.CtaLabel,
                    body.Priority,
                    body.StartsAt,
                    body.EndsAt,
                    body.Audience,
                    body.IsActive);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCreateBanner")
            .WithSummary("Opens a banner.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<BannerResponse>();

        group.MapPut("/{id:guid}", async (
                Guid id,
                BannerBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateBannerCommand(
                    id,
                    body.Name,
                    body.MediaFileId,
                    body.MobileMediaFileId,
                    body.Message,
                    body.AltText,
                    body.Link,
                    body.CtaLabel,
                    body.Priority,
                    body.StartsAt,
                    body.EndsAt,
                    body.Audience,
                    body.IsActive);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUpdateBanner")
            .WithSummary("Rewrites a banner. Its placement is not editable.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<BannerResponse>();

        // Its own route so that taking a campaign down in the next ninety seconds does not mean
        // sending the whole banner back and passing its validation on the way.
        group.MapPost("/{id:guid}/active", async (
                Guid id,
                SetBannerActiveBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SetBannerActiveCommand(id, body.IsActive), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSetBannerActive")
            .WithSummary("Switches a banner on or off without touching its schedule.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<BannerResponse>();

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteBannerCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminDeleteBanner")
            .WithSummary("Removes a banner.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);
    }

    /// <summary>The curated collections, their rules and their membership.</summary>
    private static void MapCollections(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/collections").WithTags("Content");

        group.MapGet("/", async (
                string? search,
                string? kind,
                bool? activeOnly,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListCollectionsQuery(search, kind, activeOnly, cursor, size);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListCollections")
            .WithSummary("The store's collections, newest first.")
            .RequirePermission(ContentPermissions.ContentManage)
            .Produces<PagedResult<CollectionSummaryResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetCollectionQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetCollection")
            .WithSummary("One collection, with its rule.")
            .RequirePermission(ContentPermissions.ContentManage)
            .Produces<CollectionResponse>();

        group.MapGet("/{id:guid}/items", async (
                Guid id,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListCollectionItemsQuery(id, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListCollectionItems")
            .WithSummary("What is in a collection, in its own order.")
            .RequirePermission(ContentPermissions.ContentManage)
            .Produces<PagedResult<ProductCardResponse>>();

        group.MapPost("/", async (CreateCollectionBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateCollectionCommand(body.Slug, body.Name, body.Description);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCreateCollection")
            .WithSummary("Opens a collection, hand-picked to begin with.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CollectionResponse>();

        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateCollectionBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateCollectionCommand(
                    id,
                    body.Slug,
                    body.Name,
                    body.Description,
                    body.Seo,
                    body.HeroImageFileId,
                    body.IsActive,
                    body.IsListed);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUpdateCollection")
            .WithSummary("Rewrites a collection's details.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CollectionResponse>();

        group.MapPut("/{id:guid}/rule", async (
                Guid id,
                SetCollectionRuleBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SetCollectionRuleCommand(id, body.Rule), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSetCollectionRule")
            .WithSummary("Writes or clears a collection's rule, and evaluates it at once.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CollectionResponse>();

        group.MapPut("/{id:guid}/items", async (
                Guid id,
                SetCollectionItemsBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SetCollectionItemsCommand(id, body.Items), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSetCollectionItems")
            .WithSummary("Replaces the hand-picked membership in one ordered list. This is how a collection "
                         + "is reordered; adding or removing a single product has its own routes, because "
                         + "sending back only the rows a screen has loaded would delete the rest.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CollectionResponse>();

        group.MapPost("/{id:guid}/items", async (
                Guid id,
                AddCollectionItemBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new AddCollectionItemCommand(id, body.ProductId, body.IsPinned);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminAddCollectionItem")
            .WithSummary("Adds one product to the end of the hand-picked membership, leaving the rest alone. "
                         + "A product already there is re-pinned rather than refused.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CollectionResponse>();

        group.MapDelete("/{id:guid}/items/{productId:guid}", async (
                Guid id,
                Guid productId,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RemoveCollectionItemCommand(id, productId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRemoveCollectionItem")
            .WithSummary("Takes one product out of the hand-picked membership. A row the rule put there is "
                         + "refused: it would come back on the next refresh.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CollectionResponse>();

        group.MapPost("/{id:guid}/refresh", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RefreshCollectionCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRefreshCollection")
            .WithSummary("Evaluates a rule now, rather than waiting for the sweep.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CollectionResponse>();

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteCollectionCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminDeleteCollection")
            .WithSummary("Removes a collection.")
            .RequirePermission(ContentPermissions.ContentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);
    }

    /// <summary>The redirect manager.</summary>
    private static void MapRedirects(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/redirects").WithTags("Content");

        group.MapGet("/", async (
                string? search,
                bool? activeOnly,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListRedirectsQuery(search, activeOnly, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListRedirects")
            .WithSummary("The redirect rules, by path.")
            .RequirePermission(ContentPermissions.RedirectManage)
            .Produces<PagedResult<RedirectResponse>>();

        group.MapPost("/", async (CreateRedirectBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateRedirectCommand(body.FromPath, body.ToPath, body.StatusCode, body.Note);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCreateRedirect")
            .WithSummary("Declares a redirect.")
            .RequirePermission(ContentPermissions.RedirectManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<RedirectResponse>();

        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateRedirectBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateRedirectCommand(id, body.ToPath, body.StatusCode, body.IsActive, body.Note);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUpdateRedirect")
            .WithSummary("Rewrites a redirect's destination. The path it matches is not editable.")
            .RequirePermission(ContentPermissions.RedirectManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<RedirectResponse>();

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteRedirectCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminDeleteRedirect")
            .WithSummary("Removes a redirect.")
            .RequirePermission(ContentPermissions.RedirectManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);
    }

    /// <summary>What a crawler will be served, shown to an operator before it is.</summary>
    private static void MapSeo(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/seo").WithTags("Content");

        group.MapGet("/robots", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetRobotsQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetRobots")
            .WithSummary("The robots document this deployment currently publishes.")
            .RequirePermission(ContentPermissions.SeoRead)
            .Produces<string>();

        group.MapGet("/sitemap", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetSitemapIndexQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetSitemapIndex")
            .WithSummary("Which sitemaps exist, and how many URLs each carries.")
            .RequirePermission(ContentPermissions.SeoRead)
            .Produces<SitemapIndexResponse>();

        group.MapGet("/structured-data", async (string? path, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStructuredDataQuery(path), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetStructuredData")
            .WithSummary("The schema.org graph a crawler is served for one path.")
            .RequirePermission(ContentPermissions.SeoRead)
            .Produces<StructuredDataResponse>();
    }
}
