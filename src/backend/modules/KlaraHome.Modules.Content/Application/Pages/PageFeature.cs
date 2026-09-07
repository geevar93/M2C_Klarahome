using FluentValidation;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Content.Application.Validation;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure;
using KlaraHome.Modules.Content.Infrastructure.Blocks;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.Modules.Content.Infrastructure.Rendering;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Content.Application.Pages;

/// <summary>Lists the pages an editor can work on.</summary>
/// <param name="Search">A fragment of the title or the address.</param>
/// <param name="Type">Only pages of one kind.</param>
/// <param name="Status">Only pages in one state.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListPagesQuery(
    string? Search,
    PageType? Type,
    PageStatus? Status,
    string? Cursor,
    int? Size)
    : IQuery<PagedResult<PageSummaryResponse>>;

/// <summary>Reads one page, blocks and all.</summary>
/// <param name="Id">The page.</param>
internal sealed record GetPageQuery(Guid Id) : IQuery<PageResponse>;

/// <summary>Lists the block types this platform renders, with their schemas.</summary>
internal sealed record GetBlockTypesQuery : IQuery<IReadOnlyList<BlockTypeResponse>>;

/// <summary>Opens a page.</summary>
/// <param name="Slug">Its address, or null to derive one from the title.</param>
/// <param name="Type">What it is for.</param>
/// <param name="Title">Its title.</param>
/// <param name="Summary">Its summary.</param>
internal sealed record CreatePageCommand(string? Slug, PageType Type, string? Title, string? Summary)
    : ICommand<PageResponse>;

/// <summary>Rewrites a page: its details, its SEO block and its blocks.</summary>
/// <param name="Id">The page.</param>
/// <param name="Slug">Its address.</param>
/// <param name="Title">Its title.</param>
/// <param name="Summary">Its summary.</param>
/// <param name="Seo">What a crawler is told.</param>
/// <param name="CoverImageFileId">Its cover image.</param>
/// <param name="Author">The byline, for a blog page.</param>
/// <param name="Tags">Its tags.</param>
/// <param name="Blocks">
/// Its blocks, in the order they should render, or null to leave them exactly as they are — which is
/// what the SEO screen sends when it saves nothing but the meta description.
/// </param>
internal sealed record UpdatePageCommand(
    Guid Id,
    string? Slug,
    string? Title,
    string? Summary,
    SeoBody? Seo,
    Guid? CoverImageFileId,
    string? Author,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<BlockBody>? Blocks) : ICommand<PageResponse>;

/// <summary>Removes a page that has never been published.</summary>
/// <param name="Id">The page.</param>
internal sealed record DeletePageCommand(Guid Id) : ICommand;

/// <summary>Rules a new page has to satisfy.</summary>
internal sealed class CreatePageCommandValidator : AbstractValidator<CreatePageCommand>
{
    public CreatePageCommandValidator()
    {
        RuleFor(command => command.Title).NotEmpty().MaximumLength(ContentPage.MaxTitleLength);
        RuleFor(command => command.Slug).MaximumLength(ContentPage.MaxSlugLength);
        RuleFor(command => command.Summary).MaximumLength(ContentPage.MaxSummaryLength);

        // A real enum in the contract, so an unknown word is refused by the model binder before a
        // validator sees it and the generated client cannot send one (Step 28B, deliverable 11).
        RuleFor(command => command.Type).IsInEnum();
    }
}

/// <summary>Rules an edit has to satisfy.</summary>
/// <remarks>
/// The page type is deliberately absent: it decides the URL space a page lives in, and changing it
/// would move a published page's address without anything writing the redirect that keeps its links
/// working. A page of the wrong type is archived and rewritten.
/// </remarks>
internal sealed class UpdatePageCommandValidator : AbstractValidator<UpdatePageCommand>
{
    public UpdatePageCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Title).NotEmpty().MaximumLength(ContentPage.MaxTitleLength);
        RuleFor(command => command.Slug).NotEmpty().MaximumLength(ContentPage.MaxSlugLength);
        RuleFor(command => command.Summary).MaximumLength(ContentPage.MaxSummaryLength);
        RuleFor(command => command.Author).MaximumLength(120);

        RuleFor(command => command.Tags)
            .Must(tags => tags is null || tags.Count <= 20)
            .WithMessage("A page may carry at most twenty tags.");

        RuleFor(command => command.Seo!.MetaTitle)
            .MaximumLength(SeoMetadata.MaxTitleLength)
            .When(command => command.Seo is not null);

        RuleFor(command => command.Seo!.MetaDescription)
            .MaximumLength(SeoMetadata.MaxDescriptionLength)
            .When(command => command.Seo is not null);

        RuleFor(command => command.Seo!.SitemapPriority)
            .InclusiveBetween(0m, 1m)
            .When(command => command.Seo?.SitemapPriority is not null);
    }
}

/// <summary>Lists the pages an editor can work on, newest first.</summary>
/// <param name="context">The Content data context.</param>
internal sealed class ListPagesQueryHandler(ContentDbContext context)
    : IQueryHandler<ListPagesQuery, PagedResult<PageSummaryResponse>>
{
    public async Task<Result<PagedResult<PageSummaryResponse>>> HandleAsync(
        ListPagesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.Pages.AsNoTracking().AsQueryable();

        if (query.Search is { Length: > 0 } search)
        {
            var pattern = $"%{ContentQueries.EscapeLike(search)}%";

            rows = rows.Where(page =>
                EF.Functions.ILike(page.Title, pattern, "\\")
                || EF.Functions.ILike(page.Slug, pattern, "\\"));
        }

        if (query.Type is { } type)
        {
            rows = rows.Where(page => page.Type == type);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(page => page.Status == status);
        }

        // Newest first, keyed on the id. Ids are UUIDv7 and therefore time-ordered, so one column
        // both sorts and pages — which is why every list on this platform is shaped this way.
        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(page => page.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(row => row.Id)
            .Take(size + 1)
            .Select(row => new
            {
                Page = row,
                BlockCount = row.Blocks.Count,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        var items = page
            .Take(size)
            .Select(row => ContentProjection.ToPageSummary(row.Page, row.BlockCount))
            .ToArray();

        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<PageSummaryResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one page in full.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves the cover and Open Graph images.</param>
/// <param name="scope">Who is asking, so the transition list is theirs.</param>
internal sealed class GetPageQueryHandler(
    ContentDbContext context,
    ContentRenderer renderer,
    ContentScope scope)
    : IQueryHandler<GetPageQuery, PageResponse>
{
    public async Task<Result<PageResponse>> HandleAsync(GetPageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await context.Pages
            .AsNoTracking()
            .Include(row => row.Blocks)
            .FirstOrDefaultAsync(row => row.Id == query.Id, cancellationToken)
            .ConfigureAwait(false);

        if (page is null)
        {
            return Result.Failure<PageResponse>(ContentErrors.NotFound("page"));
        }

        var response = await PageReader
            .ToResponseAsync(page, renderer, scope.Actor, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>Lists the block types, with their schemas.</summary>
/// <remarks>
/// Served rather than compiled into the admin app, so the block editor's form and the validator that
/// judges it cannot drift: a field added in a release appears in the picker the moment the release is
/// deployed, with no second place to remember.
/// </remarks>
internal sealed class GetBlockTypesQueryHandler : IQueryHandler<GetBlockTypesQuery, IReadOnlyList<BlockTypeResponse>>
{
    public Task<Result<IReadOnlyList<BlockTypeResponse>>> HandleAsync(
        GetBlockTypesQuery query,
        CancellationToken cancellationToken)
        => Task.FromResult(Result.Success<IReadOnlyList<BlockTypeResponse>>(
            [.. BlockCatalog.All.Select(ContentProjection.ToBlockType)]));
}

/// <summary>Opens a page.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves images for the response.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class CreatePageCommandHandler(
    ContentDbContext context,
    ContentRenderer renderer,
    ContentScope scope,
    IClock clock)
    : ICommandHandler<CreatePageCommand, PageResponse>
{
    public async Task<Result<PageResponse>> HandleAsync(
        CreatePageCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? ContentFormats.ToSlug(command.Title!, ContentPage.MaxSlugLength)
            : ContentFormats.ToSlug(command.Slug, ContentPage.MaxSlugLength);

        if (!ContentFormats.Slug().IsMatch(slug))
        {
            return Result.Failure<PageResponse>(ContentErrors.InvalidSlug);
        }

        if (await context.Pages.AnyAsync(page => page.Slug == slug, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PageResponse>(ContentErrors.DuplicateSlug(slug));
        }

        var page = ContentPage.Create(slug, command.Type, command.Title!.Trim());

        page.Describe(
            slug,
            command.Title.Trim(),
            command.Summary?.Trim(),
            new SeoMetadata(),
            coverImageFileId: null,
            author: null,
            tags: [],
            clock.UtcNow);

        context.Pages.Add(page);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await PageReader
            .ToResponseAsync(page, renderer, scope.Actor, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>Rewrites a page.</summary>
/// <remarks>
/// <para>
/// A save is not a publish, and this handler is where that is true. Editing a live page changes what
/// is served — a typo fix must not need a republish — but it does not snapshot a version and does not
/// move the page's status. Going live is <see cref="Pages.TransitionPageCommand"/>'s job, and the
/// separation is what makes the version history a list of deliberate publishes rather than a
/// keystroke log.
/// </para>
/// <para>
/// A scheduled page is refused. It has been approved and frozen, and letting an edit through would
/// mean the content that goes live is not the content that was approved.
/// </para>
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="binder">Validates the blocks.</param>
/// <param name="renderer">Resolves images for the response.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class UpdatePageCommandHandler(
    ContentDbContext context,
    PageBlockBinder binder,
    ContentRenderer renderer,
    ContentScope scope,
    IClock clock)
    : ICommandHandler<UpdatePageCommand, PageResponse>
{
    public async Task<Result<PageResponse>> HandleAsync(
        UpdatePageCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var page = await context.Pages
            .Include(row => row.Blocks)
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (page is null)
        {
            return Result.Failure<PageResponse>(ContentErrors.NotFound("page"));
        }

        if (!PageLifecycle.IsEditable(page.Status))
        {
            return Result.Failure<PageResponse>(ContentErrors.PageNotEditable(page.Status.ToString()));
        }

        var slug = ContentFormats.ToSlug(command.Slug!, ContentPage.MaxSlugLength);

        if (!ContentFormats.Slug().IsMatch(slug))
        {
            return Result.Failure<PageResponse>(ContentErrors.InvalidSlug);
        }

        var taken = await context.Pages
            .AnyAsync(row => row.Slug == slug && row.Id != page.Id, cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            return Result.Failure<PageResponse>(ContentErrors.DuplicateSlug(slug));
        }

        var now = clock.UtcNow;

        if (command.Blocks is not null)
        {
            var bound = await binder
                .BindAsync(command.Blocks, scope.CanWriteCustomHtml, cancellationToken)
                .ConfigureAwait(false);

            if (bound.IsFailure)
            {
                return Result.Failure<PageResponse>(bound.Error);
            }

            page.SyncBlocks(bound.Value, now);
        }

        page.Describe(
            slug,
            command.Title!.Trim(),
            command.Summary?.Trim(),
            ContentProjection.FromSeo(command.Seo),
            command.CoverImageFileId,
            command.Author?.Trim(),
            Normalize(command.Tags),
            now);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await PageReader
            .ToResponseAsync(page, renderer, scope.Actor, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }

    /// <summary>Folds tags to lower case and drops the duplicates and the blanks.</summary>
    /// <remarks>
    /// A blog index groups by tag, and "Living Room", "living room" and "living-room" grouping
    /// separately is the single most common way a tag list becomes useless.
    /// </remarks>
    private static List<string> Normalize(IReadOnlyList<string>? tags)
    {
        var normalized = new List<string>();

        foreach (var tag in tags ?? [])
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var folded = tag.Trim().ToLowerInvariant();

            if (!normalized.Contains(folded, StringComparer.Ordinal))
            {
                normalized.Add(folded);
            }
        }

        return normalized;
    }
}

/// <summary>Removes a page that has never been published.</summary>
/// <remarks>
/// A soft delete, and refused for anything that has ever been live. The rule is not squeamishness: a
/// published page has links pointing at it from crawlers, bookmarks and other people's posts, and the
/// supported way to retire one is to archive it and add a redirect. Deletion is for the draft
/// somebody created by mistake ten minutes ago.
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class DeletePageCommandHandler(ContentDbContext context, ContentScope scope, IClock clock)
    : ICommandHandler<DeletePageCommand>
{
    public async Task<Result> HandleAsync(DeletePageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var page = await context.Pages
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (page is null)
        {
            return Result.Failure(ContentErrors.NotFound("page"));
        }

        if (page.HasBeenPublished)
        {
            return Result.Failure(ContentErrors.PublishedPageNotDeletable);
        }

        page.Delete(clock.UtcNow, scope.ActorId);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Assembles a page's response, resolving the two images it carries.</summary>
/// <remarks>
/// Shared by four handlers rather than repeated in each, so a page read one way and a page read
/// another cannot come back in two shapes.
/// </remarks>
internal static class PageReader
{
    /// <summary>Builds the document an editor opens.</summary>
    /// <param name="page">The page, with its blocks loaded.</param>
    /// <param name="renderer">Resolves the images.</param>
    /// <param name="actor">Who is looking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<PageResponse> ToResponseAsync(
        ContentPage page,
        ContentRenderer renderer,
        PageActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(renderer);

        var ogImage = await renderer
            .ResolveImageAsync(page.Seo.OgImageFileId, page.Title, cancellationToken)
            .ConfigureAwait(false);

        var cover = await renderer
            .ResolveImageAsync(page.CoverImageFileId, page.Title, cancellationToken)
            .ConfigureAwait(false);

        return ContentProjection.ToPage(page, ContentProjection.ToSeo(page.Seo, ogImage), cover, actor);
    }
}
