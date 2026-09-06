using FluentValidation;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Reviews.Domain;
using KlaraHome.Modules.Reviews.Infrastructure;
using KlaraHome.Modules.Reviews.Infrastructure.Persistence;
using KlaraHome.Modules.Reviews.Infrastructure.Projection;
using KlaraHome.Modules.Reviews.Infrastructure.Rating;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Reviews.Application.Reports;

/// <summary>Reports something somebody wrote.</summary>
/// <param name="Target">Review, Question or Answer.</param>
/// <param name="TargetId">Which one.</param>
/// <param name="Reason">Why.</param>
/// <param name="Note">What the reporter wants to say.</param>
/// <param name="ReporterId">Who is reporting, when they are signed in.</param>
internal sealed record ReportContentCommand(
    string? Target,
    Guid TargetId,
    string? Reason,
    string? Note,
    Guid? ReporterId) : ICommand<AbuseReportResponse>;

/// <summary>Lists the complaints queue.</summary>
/// <param name="Status">Open, Upheld or Dismissed. Null for all of them.</param>
/// <param name="Reason">Only complaints of this kind.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListAbuseReportsQuery(string? Status, string? Reason, string? Cursor, int? Size)
    : IQuery<PagedResult<AbuseReportResponse>>;

/// <summary>Closes a complaint, taking the content down if it is upheld.</summary>
/// <param name="Id">The report.</param>
/// <param name="Uphold">Whether the complaint is accepted.</param>
/// <param name="Resolution">What the moderator concluded.</param>
internal sealed record ResolveAbuseReportCommand(Guid Id, bool Uphold, string? Resolution)
    : ICommand<AbuseReportResponse>;

/// <summary>Rules a complaint has to satisfy.</summary>
internal sealed class ReportContentCommandValidator : AbstractValidator<ReportContentCommand>
{
    public ReportContentCommandValidator()
    {
        RuleFor(command => command.TargetId).NotEmpty();
        RuleFor(command => command.Note).MaximumLength(AbuseReport.MaxNoteLength);

        RuleFor(command => command.Target)
            .Must(target => Enum.TryParse<ReportTarget>(target, ignoreCase: true, out _))
            .WithMessage("A report is against a Review, a Question or an Answer.");

        RuleFor(command => command.Reason)
            .Must(reason => Enum.TryParse<ReportReason>(reason, ignoreCase: true, out _))
            .WithMessage("Choose a reason from the list.");
    }
}

/// <summary>
/// Records a complaint, and updates the count the queue is triaged on.
/// </summary>
/// <remarks>
/// <para>
/// The report does not hide anything. Upholding it is what takes the content down, and keeping the
/// two apart is what lets a moderator dismiss a complaint without touching the review — and stops a
/// competitor from removing a five-star review by reporting it.
/// </para>
/// <para>
/// A signed-in reporter may have only one open complaint against any one thing, enforced by a
/// filtered unique index. An anonymous one has nobody to be unique against, so that case is held by
/// the rate limiter on the endpoint instead; the trade is deliberate, because a store that required
/// an account to report unlawful content would be a store that made itself hard to tell.
/// </para>
/// </remarks>
/// <param name="context">The Reviews data context.</param>
internal sealed class ReportContentCommandHandler(ReviewsDbContext context)
    : ICommandHandler<ReportContentCommand, AbuseReportResponse>
{
    public async Task<Result<AbuseReportResponse>> HandleAsync(
        ReportContentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var target = Enum.Parse<ReportTarget>(command.Target!, ignoreCase: true);
        var reason = Enum.Parse<ReportReason>(command.Reason!, ignoreCase: true);

        var exists = await TargetExistsAsync(target, command.TargetId, cancellationToken).ConfigureAwait(false);

        if (!exists)
        {
            return Result.Failure<AbuseReportResponse>(ReviewErrors.NotFound(target.ToString().ToLowerInvariant()));
        }

        if (command.ReporterId is { } reporter)
        {
            var already = await context.AbuseReports
                .AnyAsync(
                    row => row.Target == target
                           && row.TargetId == command.TargetId
                           && row.ReporterId == reporter
                           && row.Status == ReportStatus.Open,
                    cancellationToken)
                .ConfigureAwait(false);

            if (already)
            {
                return Result.Failure<AbuseReportResponse>(ReviewErrors.AlreadyReported);
            }
        }

        var report = AbuseReport.Raise(target, command.TargetId, reason, command.Note, command.ReporterId);

        context.AbuseReports.Add(report);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RefreshCountAsync(context, target, command.TargetId, cancellationToken).ConfigureAwait(false);

        return Result.Success(ReviewProjection.ToResponse(report));
    }

    /// <summary>Whether the thing being reported is something this module has.</summary>
    private Task<bool> TargetExistsAsync(ReportTarget target, Guid id, CancellationToken cancellationToken)
        => target switch
        {
            ReportTarget.Review => context.Reviews.AnyAsync(row => row.Id == id, cancellationToken),
            ReportTarget.Question => context.Questions.AnyAsync(row => row.Id == id, cancellationToken),
            _ => context.Answers.AnyAsync(row => row.Id == id, cancellationToken),
        };

    /// <summary>
    /// Replaces the cached count of open complaints on whatever was reported.
    /// </summary>
    /// <remarks>
    /// Recounted rather than incremented, like every other cache in this module, so that resolving a
    /// complaint puts the number back down without a second code path having to remember to.
    /// </remarks>
    /// <param name="context">The Reviews data context.</param>
    /// <param name="target">What kind of thing was reported.</param>
    /// <param name="targetId">Which one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task RefreshCountAsync(
        ReviewsDbContext context,
        ReportTarget target,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        var open = await context.AbuseReports
            .CountAsync(
                row => row.Target == target && row.TargetId == targetId && row.Status == ReportStatus.Open,
                cancellationToken)
            .ConfigureAwait(false);

        switch (target)
        {
            case ReportTarget.Review:
                var review = await context.Reviews
                    .FirstOrDefaultAsync(row => row.Id == targetId, cancellationToken)
                    .ConfigureAwait(false);

                review?.RecordReports(open);
                break;

            case ReportTarget.Question:
                var question = await context.Questions
                    .FirstOrDefaultAsync(row => row.Id == targetId, cancellationToken)
                    .ConfigureAwait(false);

                question?.RecordReports(open);
                break;

            default:
                var answer = await context.Answers
                    .FirstOrDefaultAsync(row => row.Id == targetId, cancellationToken)
                    .ConfigureAwait(false);

                answer?.RecordReports(open);
                break;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Lists the complaints queue.</summary>
/// <remarks>
/// Ordered by reason and then oldest first, with unlawful content sorting to the top. That ordering
/// is not cosmetic: in India an intermediary's obligation to act on unlawful content runs to a
/// statutory clock (docs/07-security-compliance.md §5), and a queue that buried those reports among
/// complaints about bad language could not be worked to it.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
internal sealed class ListAbuseReportsQueryHandler(ReviewsDbContext context)
    : IQueryHandler<ListAbuseReportsQuery, PagedResult<AbuseReportResponse>>
{
    public async Task<Result<PagedResult<AbuseReportResponse>>> HandleAsync(
        ListAbuseReportsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.AbuseReports.AsNoTracking().AsQueryable();

        if (Enum.TryParse<ReportStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(report => report.Status == status);
        }

        if (Enum.TryParse<ReportReason>(query.Reason, ignoreCase: true, out var reason))
        {
            rows = rows.Where(report => report.Reason == reason);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(report => report.Id.CompareTo(after) > 0);
        }

        var page = await rows
            .OrderByDescending(report => report.Reason == ReportReason.Illegal)
            .ThenBy(report => report.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).ToList();

        var responses = items.Select(ReviewProjection.ToResponse).ToArray();
        var next = hasMore && items.Count > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<AbuseReportResponse>(responses, new PageInfo(size, next)));
    }
}

/// <summary>
/// Closes a complaint, taking the content down if it is upheld.
/// </summary>
/// <remarks>
/// Upholding a complaint about a review refuses that review, which may change the product's rating —
/// so the aggregate is recomputed and republished on the way through. It is the one place outside
/// the moderation queue that can move a rating, and routing it through the same projector is what
/// stops the two from computing an average differently.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="ratings">The single place a rating is recomputed and announced.</param>
/// <param name="scope">Who is deciding.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ResolveAbuseReportCommandHandler(
    ReviewsDbContext context,
    RatingProjector ratings,
    ReviewScope scope,
    IClock clock) : ICommandHandler<ResolveAbuseReportCommand, AbuseReportResponse>
{
    public async Task<Result<AbuseReportResponse>> HandleAsync(
        ResolveAbuseReportCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var report = await context.AbuseReports
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (report is null)
        {
            return Result.Failure<AbuseReportResponse>(ReviewErrors.NotFound("report"));
        }

        var now = clock.UtcNow;

        report.Resolve(command.Uphold, scope.ActorId, now, command.Resolution);

        if (command.Uphold)
        {
            await TakeDownAsync(report, now, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await ReportContentCommandHandler
            .RefreshCountAsync(context, report.Target, report.TargetId, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(ReviewProjection.ToResponse(report));
    }

    /// <summary>Refuses whatever the upheld complaint was about.</summary>
    private async Task TakeDownAsync(AbuseReport report, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reason = report.Resolution ?? $"Upheld complaint: {report.Reason}.";

        switch (report.Target)
        {
            case ReportTarget.Review:
                var review = await context.Reviews
                    .FirstOrDefaultAsync(row => row.Id == report.TargetId, cancellationToken)
                    .ConfigureAwait(false);

                if (review is null)
                {
                    return;
                }

                var wasCounted = review.IsCounted;
                review.Reject(scope.ActorId, now, reason);

                if (wasCounted)
                {
                    await ratings.RefreshForAsync(review, cancellationToken).ConfigureAwait(false);
                }

                break;

            case ReportTarget.Question:
                var question = await context.Questions
                    .FirstOrDefaultAsync(row => row.Id == report.TargetId, cancellationToken)
                    .ConfigureAwait(false);

                question?.Reject(scope.ActorId, now, reason);
                break;

            default:
                var answer = await context.Answers
                    .FirstOrDefaultAsync(row => row.Id == report.TargetId, cancellationToken)
                    .ConfigureAwait(false);

                answer?.Reject(scope.ActorId, now);
                break;
        }
    }
}
