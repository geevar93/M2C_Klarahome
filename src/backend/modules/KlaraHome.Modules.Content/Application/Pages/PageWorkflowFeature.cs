using FluentValidation;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure;
using KlaraHome.Modules.Content.Infrastructure.Blocks;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.Modules.Content.Infrastructure.Rendering;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Content.Application.Pages;

/// <summary>Moves a page along the editorial workflow.</summary>
/// <param name="Id">The page.</param>
/// <param name="Status">Where it is being moved to.</param>
/// <param name="ScheduledAt">When it should go live, for a schedule.</param>
/// <param name="Note">Why, recorded on the version this publish snapshots.</param>
internal sealed record TransitionPageCommand(
    Guid Id,
    PageStatus Status,
    DateTimeOffset? ScheduledAt,
    string? Note)
    : ICommand<PageResponse>;

/// <summary>Lists a page's version history, newest first.</summary>
/// <param name="Id">The page.</param>
internal sealed record ListPageVersionsQuery(Guid Id) : IQuery<IReadOnlyList<PageVersionSummaryResponse>>;

/// <summary>Reads one version in full.</summary>
/// <param name="Id">The page.</param>
/// <param name="Version">Which version.</param>
internal sealed record GetPageVersionQuery(Guid Id, int Version) : IQuery<PageVersionResponse>;

/// <summary>Restores a version's content onto the page.</summary>
/// <param name="Id">The page.</param>
/// <param name="Version">Which version to restore.</param>
internal sealed record RollbackPageCommand(Guid Id, int Version) : ICommand<PageResponse>;

/// <summary>
/// Renders a page as the storefront would, whatever its status.
/// </summary>
/// <remarks>
/// The preview. It is a query on the admin surface rather than a token on the store surface, and that
/// is the safer of the two designs: a preview token is a URL that grants access to unpublished
/// content and gets pasted into chat threads, whereas this needs the caller's own bearer token and
/// their own permission on every request. The storefront's server-side renderer holds one when an
/// editor asks for a preview.
/// </remarks>
/// <param name="Id">The page.</param>
/// <param name="Version">A version to preview instead of the working copy, or null for the latter.</param>
internal sealed record PreviewPageQuery(Guid Id, int? Version) : IQuery<StorePageResponse>;

/// <summary>Rules a transition has to satisfy.</summary>
internal sealed class TransitionPageCommandValidator : AbstractValidator<TransitionPageCommand>
{
    public TransitionPageCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();

        // A real enum in the contract, so an unknown word never reaches a validator.
        RuleFor(command => command.Status).IsInEnum();

        RuleFor(command => command.Note).MaximumLength(500);

        RuleFor(command => command.ScheduledAt)
            .NotNull()
            .When(command => command.Status == PageStatus.Scheduled)
            .WithMessage("A scheduled page needs a time to go live.");
    }
}

/// <summary>
/// Moves a page along the editorial workflow, snapshotting it when it goes live.
/// </summary>
/// <remarks>
/// <para>
/// The single door into <see cref="PageStatus"/>, and it does three things the transition table
/// cannot. It refuses a second published home page, because that is a uniqueness rule rather than a
/// workflow one. It refuses a schedule in the past, because the scheduler would publish it on its
/// next pass and the editor would have meant "now". And it takes a version snapshot on the way to
/// <see cref="PageStatus.Published"/> — which is what makes the history a list of what was actually
/// served rather than a list of saves.
/// </para>
/// <para>
/// A snapshot is taken on the publish and not on the schedule, deliberately. What matters is what
/// went live, and a scheduled page can still be sent back to draft and rewritten before it does.
/// </para>
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves images for the response.</param>
/// <param name="scope">Who is asking, and what they may do.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class TransitionPageCommandHandler(
    ContentDbContext context,
    ContentRenderer renderer,
    ContentScope scope,
    IClock clock)
    : ICommandHandler<TransitionPageCommand, PageResponse>
{
    public async Task<Result<PageResponse>> HandleAsync(
        TransitionPageCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var next = command.Status;

        var page = await context.Pages
            .Include(row => row.Blocks)
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (page is null)
        {
            return Result.Failure<PageResponse>(ContentErrors.NotFound("page"));
        }

        if (!PageLifecycle.Exists(page.Status, next))
        {
            return Result.Failure<PageResponse>(
                ContentErrors.IllegalTransition(page.Status.ToString(), next.ToString()));
        }

        if (!PageLifecycle.Allows(page.Status, next, scope.Actor))
        {
            return Result.Failure<PageResponse>(ContentErrors.TransitionNotAllowed(next.ToString()));
        }

        var now = clock.UtcNow;

        if (next == PageStatus.Scheduled && command.ScheduledAt <= now)
        {
            return Result.Failure<PageResponse>(ContentErrors.ScheduleInPast);
        }

        if (next == PageStatus.Published && page.Type == PageType.Home)
        {
            var incumbent = await context.Pages
                .AnyAsync(
                    row => row.Type == PageType.Home
                           && row.Status == PageStatus.Published
                           && row.Id != page.Id,
                    cancellationToken)
                .ConfigureAwait(false);

            if (incumbent)
            {
                return Result.Failure<PageResponse>(ContentErrors.HomePageAlreadyPublished);
            }
        }

        if (!page.Transition(next, scope.Actor, now, command.ScheduledAt))
        {
            return Result.Failure<PageResponse>(ContentErrors.TransitionNotAllowed(next.ToString()));
        }

        if (next == PageStatus.Published)
        {
            Snapshot(page, command.Note, restoredFrom: null, now, scope.ActorId, context);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await PageReader
            .ToResponseAsync(page, renderer, scope.Actor, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }

    /// <summary>Writes the page's current state into the history.</summary>
    /// <param name="page">The page.</param>
    /// <param name="note">Why.</param>
    /// <param name="restoredFrom">The version restored, when this snapshot is a rollback.</param>
    /// <param name="takenAt">When.</param>
    /// <param name="actorId">Who.</param>
    /// <param name="context">The Content data context.</param>
    internal static void Snapshot(
        ContentPage page,
        string? note,
        int? restoredFrom,
        DateTimeOffset takenAt,
        Guid? actorId,
        ContentDbContext context)
    {
        var version = PageVersion.Capture(
            page.Id,
            page.NextVersion(),
            page.Title,
            page.Seo,
            ContentProjection.WriteSnapshot(page.Blocks),
            note,
            restoredFrom,
            takenAt);

        version.By(actorId);

        context.PageVersions.Add(version);
    }
}

/// <summary>Lists a page's version history.</summary>
/// <param name="context">The Content data context.</param>
internal sealed class ListPageVersionsQueryHandler(ContentDbContext context)
    : IQueryHandler<ListPageVersionsQuery, IReadOnlyList<PageVersionSummaryResponse>>
{
    public async Task<Result<IReadOnlyList<PageVersionSummaryResponse>>> HandleAsync(
        ListPageVersionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var versions = await context.PageVersions
            .AsNoTracking()
            .Where(version => version.PageId == query.Id)
            .OrderByDescending(version => version.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<PageVersionSummaryResponse>>(
            [.. versions.Select(ContentProjection.ToVersionSummary)]);
    }
}

/// <summary>Reads one version in full.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves the Open Graph image the version carried.</param>
internal sealed class GetPageVersionQueryHandler(ContentDbContext context, ContentRenderer renderer)
    : IQueryHandler<GetPageVersionQuery, PageVersionResponse>
{
    public async Task<Result<PageVersionResponse>> HandleAsync(
        GetPageVersionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var version = await context.PageVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.PageId == query.Id && row.Version == query.Version,
                cancellationToken)
            .ConfigureAwait(false);

        if (version is null)
        {
            return Result.Failure<PageVersionResponse>(ContentErrors.UnknownVersion(query.Version));
        }

        var ogImage = await renderer
            .ResolveImageAsync(version.Seo.OgImageFileId, version.Title, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(
            ContentProjection.ToVersion(version, ContentProjection.ToSeo(version.Seo, ogImage)));
    }
}

/// <summary>
/// Restores a version's content onto the page.
/// </summary>
/// <remarks>
/// <para>
/// A rollback writes a <em>new</em> version whose content is an old one's, rather than deleting
/// anything. The history is append-only, so the rollback is itself in it — which is the only way to
/// answer "why did this page change back on Tuesday" three months later.
/// </para>
/// <para>
/// It restores the content and not the status. Rolling back the wording of a live page must not take
/// the page down, and restoring a version captured while the page was still a draft must not silently
/// publish it.
/// </para>
/// <para>
/// The restored blocks are validated again on the way in. A version is content that was valid when it
/// was written, and a build in which a block type has gained a required field would otherwise restore
/// something the current storefront cannot render.
/// </para>
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="binder">Re-validates the snapshot's blocks.</param>
/// <param name="renderer">Resolves images for the response.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class RollbackPageCommandHandler(
    ContentDbContext context,
    PageBlockBinder binder,
    ContentRenderer renderer,
    ContentScope scope,
    IClock clock)
    : ICommandHandler<RollbackPageCommand, PageResponse>
{
    public async Task<Result<PageResponse>> HandleAsync(
        RollbackPageCommand command,
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

        if (PageLifecycle.IsTerminal(page.Status) || page.Status == PageStatus.Scheduled)
        {
            return Result.Failure<PageResponse>(ContentErrors.PageNotEditable(page.Status.ToString()));
        }

        var version = await context.PageVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.PageId == page.Id && row.Version == command.Version,
                cancellationToken)
            .ConfigureAwait(false);

        if (version is null)
        {
            return Result.Failure<PageResponse>(ContentErrors.UnknownVersion(command.Version));
        }

        var bound = await binder
            .BindSnapshotAsync(
                ContentProjection.ReadSnapshot(version.Blocks),
                scope.CanWriteCustomHtml,
                cancellationToken)
            .ConfigureAwait(false);

        if (bound.IsFailure)
        {
            return Result.Failure<PageResponse>(bound.Error);
        }

        var now = clock.UtcNow;

        page.Restore(version.Title, version.Seo.Clone(), bound.Value, now);

        TransitionPageCommandHandler.Snapshot(
            page,
            $"Rolled back to version {version.Version}.",
            version.Version,
            now,
            scope.ActorId,
            context);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await PageReader
            .ToResponseAsync(page, renderer, scope.Actor, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>Renders a page as the storefront would, whatever its status.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="composer">Resolves and windows the blocks, exactly as the storefront read does.</param>
/// <param name="binder">Rebuilds the blocks of a version being previewed.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class PreviewPageQueryHandler(
    ContentDbContext context,
    PageComposer composer,
    PageBlockBinder binder,
    ContentScope scope,
    IClock clock)
    : IQueryHandler<PreviewPageQuery, StorePageResponse>
{
    public async Task<Result<StorePageResponse>> HandleAsync(
        PreviewPageQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Untracked, and that is load-bearing rather than an optimisation. Previewing a version
        // applies that version's content to the entity in memory, and an entity the change tracker
        // was holding would be written by the next SaveChanges in the scope — a preview that silently
        // performed a rollback.
        var page = await context.Pages
            .AsNoTracking()
            .Include(row => row.Blocks)
            .FirstOrDefaultAsync(row => row.Id == query.Id, cancellationToken)
            .ConfigureAwait(false);

        if (page is null)
        {
            return Result.Failure<StorePageResponse>(ContentErrors.NotFound("page"));
        }

        var now = clock.UtcNow;

        if (query.Version is { } wanted)
        {
            var version = await context.PageVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(row => row.PageId == page.Id && row.Version == wanted, cancellationToken)
                .ConfigureAwait(false);

            if (version is null)
            {
                return Result.Failure<StorePageResponse>(ContentErrors.UnknownVersion(wanted));
            }

            var bound = await binder
                .BindSnapshotAsync(
                    ContentProjection.ReadSnapshot(version.Blocks),
                    scope.CanWriteCustomHtml,
                    cancellationToken)
                .ConfigureAwait(false);

            if (bound.IsFailure)
            {
                return Result.Failure<StorePageResponse>(bound.Error);
            }

            page.Restore(version.Title, version.Seo.Clone(), bound.Value, now);
        }

        var composed = await composer.ComposeAsync(page, now, cancellationToken).ConfigureAwait(false);

        return Result.Success(composed);
    }
}
