using FluentValidation;
using KlaraHome.Contracts.Media;
using KlaraHome.Contracts.Platform;
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
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Reviews.Application.Moderation;

/// <summary>Lists reviews for the moderation queue.</summary>
/// <param name="Status">Pending, Approved or Rejected. Null for all of them.</param>
/// <param name="ProductId">Only this product's.</param>
/// <param name="VendorId">Only this seller's. Forced to the caller's own where they have one.</param>
/// <param name="Rating">Only reviews with this score.</param>
/// <param name="ReportedOnly">Only reviews somebody has complained about.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListReviewsForModerationQuery(
    string? Status,
    Guid? ProductId,
    Guid? VendorId,
    int? Rating,
    bool? ReportedOnly,
    string? Cursor,
    int? Size) : IQuery<PagedResult<ModeratedReviewResponse>>;

/// <summary>Reads one review in full.</summary>
/// <param name="Id">The review.</param>
internal sealed record GetReviewQuery(Guid Id) : IQuery<ModeratedReviewResponse>;

/// <summary>Decides a review.</summary>
/// <param name="Id">The review.</param>
/// <param name="Approve">Whether to publish it.</param>
/// <param name="Note">Why. Required for a refusal.</param>
internal sealed record ModerateReviewCommand(Guid Id, bool Approve, string? Note)
    : ICommand<ModeratedReviewResponse>;

/// <summary>Writes or clears a seller's public reply to a review of their own sale.</summary>
/// <param name="Id">The review.</param>
/// <param name="Reply">What to say, or null to withdraw the reply.</param>
internal sealed record ReplyToReviewCommand(Guid Id, string? Reply) : ICommand<ModeratedReviewResponse>;

/// <summary>Rules a moderation decision has to satisfy.</summary>
internal sealed class ModerateReviewCommandValidator : AbstractValidator<ModerateReviewCommand>
{
    public ModerateReviewCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Note).MaximumLength(Review.MaxNoteLength);
    }
}

/// <summary>Rules a reply has to satisfy.</summary>
internal sealed class ReplyToReviewCommandValidator : AbstractValidator<ReplyToReviewCommand>
{
    public ReplyToReviewCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Reply).MaximumLength(Review.MaxReplyLength);
    }
}

/// <summary>
/// Lists reviews for whoever is working the queue.
/// </summary>
/// <remarks>
/// <para>
/// A seller calling this sees only their own, and that confinement is applied here rather than by a
/// query filter on the context. The reason is that the same endpoint serves both audiences: a
/// moderator with no vendor id sees everything, a seller sees their own sales, and the difference is
/// one clause rather than two endpoints that would drift.
/// </para>
/// <para>
/// Ordered oldest first when filtered to pending, and newest first otherwise. That is not a
/// nicety — a queue worked newest-first grows an ageing tail nobody ever reaches, and the shopper
/// whose review has been waiting longest is the one most likely to write to support about it.
/// </para>
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="scope">Who is asking, and which seller they are confined to.</param>
/// <param name="media">Resolves the attached files.</param>
/// <param name="options">The page ceiling.</param>
internal sealed class ListReviewsForModerationQueryHandler(
    ReviewsDbContext context,
    ReviewScope scope,
    IMediaLibrary media,
    IOptionsMonitor<ReviewsOptions> options)
    : IQueryHandler<ListReviewsForModerationQuery, PagedResult<ModeratedReviewResponse>>
{
    public async Task<Result<PagedResult<ModeratedReviewResponse>>> HandleAsync(
        ListReviewsForModerationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.CurrentValue.MaxPageSize);
        var rows = context.Reviews.AsNoTracking().AsQueryable();

        // A seller's own vendor wins over anything the query string asked for. A vendor id in a
        // request is a suggestion; the one on the token is the fact.
        var vendorId = scope.VendorId ?? query.VendorId;

        if (vendorId is { } vendor)
        {
            rows = rows.Where(review => review.VendorId == vendor);
        }

        var pending = false;

        if (Enum.TryParse<ReviewStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(review => review.Status == status);
            pending = status == ReviewStatus.Pending;
        }

        if (query.ProductId is { } productId)
        {
            rows = rows.Where(review => review.ProductId == productId);
        }

        if (query.Rating is >= Review.MinRating and <= Review.MaxRating)
        {
            rows = rows.Where(review => review.Rating == query.Rating);
        }

        if (query.ReportedOnly == true)
        {
            rows = rows.Where(review => review.ReportCount > 0);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = pending
                ? rows.Where(review => review.Id.CompareTo(after) > 0)
                : rows.Where(review => review.Id.CompareTo(after) < 0);
        }

        rows = pending
            ? rows.OrderBy(review => review.Id)
            : rows.OrderByDescending(review => review.Id);

        var page = await rows.Take(size + 1).ToListAsync(cancellationToken).ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).ToList();

        var fileIds = items.SelectMany(review => review.Images).Select(image => image.FileId).Distinct().ToArray();

        var files = fileIds.Length == 0
            ? null
            : await media.GetManyAsync(fileIds, cancellationToken).ConfigureAwait(false);

        var responses = items.Select(review => ReviewProjection.ToModerated(review, files)).ToArray();
        var next = hasMore && items.Count > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<ModeratedReviewResponse>(responses, new PageInfo(size, next)));
    }
}

/// <summary>Reads one review in full, for the screen a decision is taken on.</summary>
/// <param name="context">The Reviews data context.</param>
/// <param name="scope">Who is asking, and which seller they are confined to.</param>
/// <param name="media">Resolves the attached files.</param>
internal sealed class GetReviewQueryHandler(ReviewsDbContext context, ReviewScope scope, IMediaLibrary media)
    : IQueryHandler<GetReviewQuery, ModeratedReviewResponse>
{
    public async Task<Result<ModeratedReviewResponse>> HandleAsync(
        GetReviewQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var review = await context.Reviews
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == query.Id, cancellationToken)
            .ConfigureAwait(false);

        // A seller asking for somebody else's review is told it does not exist, rather than that they
        // may not see it. The second answer confirms which review ids are real.
        if (review is null || !scope.OwnsVendor(review.VendorId))
        {
            return Result.Failure<ModeratedReviewResponse>(ReviewErrors.NotFound("review"));
        }

        var files = review.Images.Count == 0
            ? null
            : await media
                .GetManyAsync([.. review.Images.Select(image => image.FileId)], cancellationToken)
                .ConfigureAwait(false);

        return Result.Success(ReviewProjection.ToModerated(review, files));
    }
}

/// <summary>
/// Approves or refuses a review, and republishes the rating if the decision changed it.
/// </summary>
/// <remarks>
/// <para>
/// The recompute is conditional on the decision having actually moved the review in or out of the
/// counted set. Approving something already approved is a moderator double-clicking, and it must not
/// produce a second <c>ProductRatingChanged</c> — the event is idempotent for a consumer, but a
/// stream of them for no reason would have Search refreshing index rows all afternoon.
/// </para>
/// <para>
/// A refusal must carry a reason. That is enforced here rather than by the validator because it is a
/// conditional rule — an approval needs none — and because the reason is what the shopper is shown
/// and what the next moderator reads when the decision is questioned.
/// </para>
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="ratings">The single place a rating is recomputed and announced.</param>
/// <param name="scope">Who is deciding.</param>
/// <param name="audit">Records who took the decision, against the review.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ModerateReviewCommandHandler(
    ReviewsDbContext context,
    RatingProjector ratings,
    ReviewScope scope,
    IAuditLogger audit,
    IClock clock) : ICommandHandler<ModerateReviewCommand, ModeratedReviewResponse>
{
    /// <summary>What the audit trail files a moderation decision under.</summary>
    private const string AuditEntityType = "Review";

    public async Task<Result<ModeratedReviewResponse>> HandleAsync(
        ModerateReviewCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.Approve && string.IsNullOrWhiteSpace(command.Note))
        {
            return Result.Failure<ModeratedReviewResponse>(ReviewErrors.ReasonRequired);
        }

        var review = await context.Reviews
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (review is null)
        {
            return Result.Failure<ModeratedReviewResponse>(ReviewErrors.NotFound("review"));
        }

        var wasCounted = review.IsCounted;
        var now = clock.UtcNow;

        if (command.Approve)
        {
            review.Approve(scope.ActorId, now, command.Note);
        }
        else
        {
            review.Reject(scope.ActorId, now, command.Note);
        }

        if (wasCounted != review.IsCounted)
        {
            await ratings.RefreshForAsync(review, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Recorded after the save, because the audit trail writes in its own scope and must not be
        // able to commit a moderation decision that then failed.
        await audit
            .RecordAsync(
                new AuditEntry
                {
                    Action = command.Approve ? "review.approved" : "review.rejected",
                    EntityType = AuditEntityType,
                    EntityId = review.Id.ToString(),
                    After = new { review.Status, review.Rating, review.ProductId, command.Note },
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(ReviewProjection.ToModerated(review));
    }
}

/// <summary>
/// Writes a seller's public reply.
/// </summary>
/// <remarks>
/// Confined to the caller's own vendor, because a reply is the most visible piece of text a seller
/// can put under a bad review and one seller answering for another would be indistinguishable from
/// the real thing. Platform staff hold no vendor and may reply on the store's behalf, which is a
/// support action a marketplace genuinely needs.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="scope">Who is replying, and which seller they are.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ReplyToReviewCommandHandler(ReviewsDbContext context, ReviewScope scope, IClock clock)
    : ICommandHandler<ReplyToReviewCommand, ModeratedReviewResponse>
{
    public async Task<Result<ModeratedReviewResponse>> HandleAsync(
        ReplyToReviewCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var review = await context.Reviews
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (review is null)
        {
            return Result.Failure<ModeratedReviewResponse>(ReviewErrors.NotFound("review"));
        }

        if (!scope.OwnsVendor(review.VendorId))
        {
            return Result.Failure<ModeratedReviewResponse>(ReviewErrors.NotYourReview);
        }

        review.Reply(command.Reply, scope.ActorId, clock.UtcNow);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReviewProjection.ToModerated(review));
    }
}
