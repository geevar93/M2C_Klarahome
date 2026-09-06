using FluentValidation;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Identity;
using KlaraHome.Contracts.Media;
using KlaraHome.Contracts.Orders;
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

namespace KlaraHome.Modules.Reviews.Application.Reviews;

/// <summary>One image being attached to a review.</summary>
/// <param name="FileId">The already-uploaded file.</param>
/// <param name="Caption">What the reviewer calls it.</param>
internal sealed record ReviewImageInput(Guid FileId, string? Caption);

/// <summary>Writes a review against a delivered purchase.</summary>
/// <param name="OrderLineId">The line that proves the purchase. Everything else is derived from it.</param>
/// <param name="Rating">One to five.</param>
/// <param name="Title">The headline.</param>
/// <param name="Body">What they thought.</param>
/// <param name="Images">Pictures they took.</param>
/// <param name="CustomerId">The author, from the caller's token.</param>
internal sealed record WriteReviewCommand(
    Guid OrderLineId,
    int Rating,
    string? Title,
    string? Body,
    IReadOnlyList<ReviewImageInput>? Images,
    Guid CustomerId) : ICommand<ReviewResponse>;

/// <summary>Rewrites a review the caller wrote. It returns to moderation.</summary>
/// <param name="Id">The review.</param>
/// <param name="Rating">The new score.</param>
/// <param name="Title">The new headline.</param>
/// <param name="Body">The new body.</param>
/// <param name="Images">The pictures, replacing whatever was there.</param>
/// <param name="CustomerId">The caller, checked against the author.</param>
internal sealed record ReviseReviewCommand(
    Guid Id,
    int Rating,
    string? Title,
    string? Body,
    IReadOnlyList<ReviewImageInput>? Images,
    Guid CustomerId) : ICommand<ReviewResponse>;

/// <summary>Lists one product's published reviews.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Rating">Only reviews with this score, for the histogram's click-through.</param>
/// <param name="WithImagesOnly">Only reviews carrying a picture.</param>
/// <param name="Sort">
/// <c>recent</c>, <c>helpful</c>, <c>highest</c> or <c>lowest</c>. Anything else is <c>recent</c>.
/// </param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListProductReviewsQuery(
    Guid ProductId,
    int? Rating,
    bool? WithImagesOnly,
    string? Sort,
    string? Cursor,
    int? Size) : IQuery<PagedResult<ReviewResponse>>;

/// <summary>Reads one product's aggregate rating.</summary>
/// <param name="ProductId">The product.</param>
internal sealed record GetRatingSummaryQuery(Guid ProductId) : IQuery<RatingSummaryResponse>;

/// <summary>Asks whether the caller may review a product, and against which purchase.</summary>
/// <param name="ProductId">The product page they are on.</param>
/// <param name="CustomerId">The caller.</param>
internal sealed record GetReviewEligibilityQuery(Guid ProductId, Guid CustomerId)
    : IQuery<ReviewEligibilityResponse>;

/// <summary>Lists the reviews the caller has written.</summary>
/// <param name="CustomerId">The caller.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListMyReviewsQuery(Guid CustomerId, string? Cursor, int? Size)
    : IQuery<PagedResult<ModeratedReviewResponse>>;

/// <summary>Records whether the caller found a review useful.</summary>
/// <param name="ReviewId">The review.</param>
/// <param name="IsHelpful">Their opinion.</param>
/// <param name="CustomerId">The voter.</param>
internal sealed record VoteOnReviewCommand(Guid ReviewId, bool IsHelpful, Guid CustomerId) : ICommand;

/// <summary>Withdraws a vote the caller cast.</summary>
/// <param name="ReviewId">The review.</param>
/// <param name="CustomerId">The voter.</param>
internal sealed record WithdrawVoteCommand(Guid ReviewId, Guid CustomerId) : ICommand;

/// <summary>Rules a new review has to satisfy.</summary>
internal sealed class WriteReviewCommandValidator : AbstractValidator<WriteReviewCommand>
{
    public WriteReviewCommandValidator()
    {
        RuleFor(command => command.OrderLineId).NotEmpty();
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.Rating).InclusiveBetween(Review.MinRating, Review.MaxRating);
        RuleFor(command => command.Title).MaximumLength(Review.MaxTitleLength);
        RuleFor(command => command.Body).MaximumLength(Review.MaxBodyLength);
        RuleFor(command => command.Images).Must(images => images is null || images.Count <= Review.MaxImages)
            .WithMessage($"A review carries at most {Review.MaxImages} images.");
    }
}

/// <summary>Rules an edit has to satisfy.</summary>
internal sealed class ReviseReviewCommandValidator : AbstractValidator<ReviseReviewCommand>
{
    public ReviseReviewCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.Rating).InclusiveBetween(Review.MinRating, Review.MaxRating);
        RuleFor(command => command.Title).MaximumLength(Review.MaxTitleLength);
        RuleFor(command => command.Body).MaximumLength(Review.MaxBodyLength);
    }
}

/// <summary>
/// Writes a review, having first established that the person really was sent the thing.
/// </summary>
/// <remarks>
/// <para>
/// The order of operations here is the acceptance criterion. The purchase is resolved through
/// <c>IOrderPurchases</c> <em>before</em> anything is written, and every identifier on the resulting
/// row — the product, the variant, the seller — comes off what that contract returned rather than off
/// the request. A caller who could name their own product would be able to write a five-star review
/// of anything by quoting one line they genuinely bought.
/// </para>
/// <para>
/// The duplicate check is here as well as in the unique index, and both are needed for different
/// reasons. This one gives a shopper a message they can act on; the index is what holds when two
/// submissions race, which on a slow connection is the ordinary case rather than the exotic one.
/// </para>
/// <para>
/// Whether the review is visible immediately is a configured decision rather than a merchandising
/// one, and it is deliberately not a store setting — see <c>ReviewsOptions.AutoApproveReviews</c>.
/// Where it is on, the rating is recomputed and announced in this same transaction; where it is off,
/// nothing is announced until a moderator releases it.
/// </para>
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="purchases">The proof of purchase, answered by the module that owns the order.</param>
/// <param name="catalogue">Translates the variant bought into the product it is reviewed against.</param>
/// <param name="customers">Resolves the name the review is signed with.</param>
/// <param name="ratings">The single place a rating is recomputed and announced.</param>
/// <param name="media">Resolves the attached files for the response.</param>
/// <param name="options">Whether a review is visible the moment it is written.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class WriteReviewCommandHandler(
    ReviewsDbContext context,
    IOrderPurchases purchases,
    IProductProjectionSource catalogue,
    ICustomerDirectory customers,
    RatingProjector ratings,
    IMediaLibrary media,
    IOptionsMonitor<ReviewsOptions> options,
    IClock clock) : ICommandHandler<WriteReviewCommand, ReviewResponse>
{
    public async Task<Result<ReviewResponse>> HandleAsync(
        WriteReviewCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var settings = options.CurrentValue;

        var purchase = await purchases
            .FindDeliveredLineAsync(command.OrderLineId, command.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (purchase is null)
        {
            return Result.Failure<ReviewResponse>(ReviewErrors.NotPurchased);
        }

        var now = clock.UtcNow;

        // The window is measured from delivery rather than from the order. A parcel that took three
        // weeks to arrive should give its recipient the same time to write about it as one that
        // arrived the next day.
        if (purchase.DeliveredAt.AddDays(settings.ReviewWindowDays) < now)
        {
            return Result.Failure<ReviewResponse>(ReviewErrors.NotPurchased);
        }

        var taken = await context.Reviews
            .AnyAsync(review => review.OrderLineId == command.OrderLineId, cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            return Result.Failure<ReviewResponse>(ReviewErrors.AlreadyReviewed);
        }

        // The product this review is displayed against comes from the catalogue's own answer about
        // the variant that was bought, not from the request.
        var productId = await ResolveProductAsync(purchase, cancellationToken).ConfigureAwait(false);

        if (productId is null)
        {
            return Result.Failure<ReviewResponse>(ReviewErrors.UnknownVariant);
        }

        // Shortened here rather than at read time. The full name has no use in this schema, and a
        // public document should not be storing more than the page shows.
        var customer = await customers.FindAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);

        var review = Review.Write(
            productId.Value,
            purchase.VariantId,
            purchase.VendorId,
            command.CustomerId,
            purchase.OrderLineId,
            command.Rating,
            command.Title,
            command.Body,
            DisplayNames.Shorten(customer?.DisplayName),
            ToImages(command.Images, settings.MaxReviewImages));

        if (settings.AutoApproveReviews)
        {
            review.Approve(moderatorId: null, now);
        }

        context.Reviews.Add(review);

        // Saved before the recompute, because the recompute counts approved reviews and this one has
        // to be among them. Both writes land in the same transaction; EF opens one for the whole
        // SaveChanges, so a failure in either rolls back the other and the outbox row with it.
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (review.IsCounted)
        {
            await ratings.RefreshForAsync(review, cancellationToken).ConfigureAwait(false);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var files = await ResolveMediaAsync(review, cancellationToken).ConfigureAwait(false);

        return Result.Success(ReviewProjection.ToPublic(review, files));
    }

    /// <summary>Which product the variant bought belongs to.</summary>
    /// <remarks>
    /// Asked of the catalogue rather than inferred, because this module holds no mapping from a
    /// variant to a product and must not invent one.
    /// </remarks>
    private async Task<Guid?> ResolveProductAsync(PurchasedLine purchase, CancellationToken cancellationToken)
    {
        var projections = await catalogue
            .FindByVariantsAsync([purchase.VariantId], cancellationToken)
            .ConfigureAwait(false);

        return projections.Count > 0 ? projections[0].ProductId : null;
    }

    private async Task<IReadOnlyDictionary<Guid, MediaFile>?> ResolveMediaAsync(
        Review review,
        CancellationToken cancellationToken)
    {
        if (review.Images.Count == 0)
        {
            return null;
        }

        return await media
            .GetManyAsync([.. review.Images.Select(image => image.FileId)], cancellationToken)
            .ConfigureAwait(false);
    }

    private static IEnumerable<ReviewImage>? ToImages(IReadOnlyList<ReviewImageInput>? images, int limit)
        => images?.Take(limit).Select(image => new ReviewImage(image.FileId, image.Caption?.Trim()));
}

/// <summary>
/// Rewrites a review the caller wrote, and sends it back to moderation.
/// </summary>
/// <remarks>
/// Returning it to <see cref="ReviewStatus.Pending"/> is not a convenience — it is what stops
/// moderation from being a formality anybody can walk around by getting acceptable text approved and
/// then editing it. Where the review was counted, the rating is recomputed on the way out, because
/// it no longer is.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="ratings">The single place a rating is recomputed and announced.</param>
/// <param name="media">Resolves the attached files for the response.</param>
/// <param name="options">The image ceiling, and whether an edit is visible immediately.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ReviseReviewCommandHandler(
    ReviewsDbContext context,
    RatingProjector ratings,
    IMediaLibrary media,
    IOptionsMonitor<ReviewsOptions> options,
    IClock clock) : ICommandHandler<ReviseReviewCommand, ReviewResponse>
{
    public async Task<Result<ReviewResponse>> HandleAsync(
        ReviseReviewCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var review = await context.Reviews
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (review is null)
        {
            return Result.Failure<ReviewResponse>(ReviewErrors.NotFound("review"));
        }

        if (review.CustomerId != command.CustomerId)
        {
            return Result.Failure<ReviewResponse>(ReviewErrors.NotAuthor);
        }

        var settings = options.CurrentValue;
        var wasCounted = review.IsCounted;

        review.Revise(
            command.Rating,
            command.Title,
            command.Body,
            command.Images?.Take(settings.MaxReviewImages)
                .Select(image => new ReviewImage(image.FileId, image.Caption?.Trim())));

        if (settings.AutoApproveReviews)
        {
            review.Approve(moderatorId: null, clock.UtcNow);
        }

        // Recomputed whenever it was counted before or is counted now. Skipping the first case would
        // leave an average that still includes a review nobody can see.
        if (wasCounted || review.IsCounted)
        {
            await ratings.RefreshForAsync(review, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var files = review.Images.Count == 0
            ? null
            : await media
                .GetManyAsync([.. review.Images.Select(image => image.FileId)], cancellationToken)
                .ConfigureAwait(false);

        return Result.Success(ReviewProjection.ToPublic(review, files));
    }
}

/// <summary>
/// Lists a product's published reviews.
/// </summary>
/// <remarks>
/// <para>
/// Published only, and there is no parameter that changes that. A caller who wants to see what is
/// pending is a moderator using the admin surface with their own token; a query-string flag one
/// guess away from serving unmoderated text on a product page is not a thing that should exist.
/// </para>
/// <para>
/// Every sort is paged on the review's own id as a tie-break, not on the sort column. Helpfulness
/// and rating both have enormous ties and neither is stable — a helpfulness count moves while a
/// shopper is reading page two — so a cursor on the sort column alone would repeat and skip rows.
/// </para>
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="media">Resolves the attached files.</param>
/// <param name="options">The page ceiling.</param>
internal sealed class ListProductReviewsQueryHandler(
    ReviewsDbContext context,
    IMediaLibrary media,
    IOptionsMonitor<ReviewsOptions> options)
    : IQueryHandler<ListProductReviewsQuery, PagedResult<ReviewResponse>>
{
    public async Task<Result<PagedResult<ReviewResponse>>> HandleAsync(
        ListProductReviewsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.CurrentValue.MaxPageSize);

        var rows = context.Reviews
            .AsNoTracking()
            .Where(review => review.ProductId == query.ProductId)
            .Where(review => review.Status == ReviewStatus.Approved);

        if (query.Rating is >= Review.MinRating and <= Review.MaxRating)
        {
            rows = rows.Where(review => review.Rating == query.Rating);
        }

        if (query.WithImagesOnly == true)
        {
            // Counted in the database against the JSON document rather than by loading every review
            // and filtering here. A product with four thousand reviews and forty photographs is the
            // case this filter exists for.
            rows = rows.Where(review => review.Images.Count > 0);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(review => review.Id.CompareTo(after) > 0);
        }

        rows = query.Sort switch
        {
            "helpful" => rows.OrderByDescending(review => review.HelpfulCount).ThenBy(review => review.Id),
            "highest" => rows.OrderByDescending(review => review.Rating).ThenBy(review => review.Id),
            "lowest" => rows.OrderBy(review => review.Rating).ThenBy(review => review.Id),

            // Newest first is the default, and the id carries the time: every key on this platform is
            // a UUIDv7, so ordering on it descending is ordering on when the review was written
            // without reading a second column.
            _ => rows.OrderByDescending(review => review.Id),
        };

        var page = await rows.Take(size + 1).ToListAsync(cancellationToken).ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).ToList();

        var fileIds = items.SelectMany(review => review.Images).Select(image => image.FileId).Distinct().ToArray();

        var files = fileIds.Length == 0
            ? null
            : await media.GetManyAsync(fileIds, cancellationToken).ConfigureAwait(false);

        var responses = items.Select(review => ReviewProjection.ToPublic(review, files)).ToArray();
        var next = hasMore && items.Count > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<ReviewResponse>(responses, new PageInfo(size, next)));
    }
}

/// <summary>Reads one product's aggregate rating.</summary>
/// <remarks>
/// Answers for a product with no reviews rather than 404ing. "Nobody has reviewed this" is a real
/// and common answer, and a product page that had to treat it as an error would be a product page
/// with an error on it for every new product in the catalogue.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
internal sealed class GetRatingSummaryQueryHandler(ReviewsDbContext context)
    : IQueryHandler<GetRatingSummaryQuery, RatingSummaryResponse>
{
    public async Task<Result<RatingSummaryResponse>> HandleAsync(
        GetRatingSummaryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var summary = await context.ProductRatings
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.ProductId == query.ProductId, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(ReviewProjection.ToSummary(query.ProductId, summary));
    }
}

/// <summary>
/// Answers whether the caller may review this product, and against which purchase.
/// </summary>
/// <remarks>
/// The endpoint behind the "write a review" button, and it exists so the rule is enforced before a
/// shopper types rather than after. It resolves the product's variants through the catalogue, asks
/// Orders which of them this person received, and then removes the ones already reviewed — which is
/// the only order those three questions can be asked in without one of them reading another module's
/// tables.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="purchases">What this person actually received.</param>
/// <param name="catalogue">Which variants the product has.</param>
internal sealed class GetReviewEligibilityQueryHandler(
    ReviewsDbContext context,
    IOrderPurchases purchases,
    IProductProjectionSource catalogue)
    : IQueryHandler<GetReviewEligibilityQuery, ReviewEligibilityResponse>
{
    /// <summary>The most purchases one product page will offer to review.</summary>
    /// <remarks>
    /// Somebody who has bought the same consumable twenty times does not need twenty buttons. The
    /// most recent handful is what a page can render, and the rest are reachable from their orders.
    /// </remarks>
    private const int MaxEligible = 10;

    public async Task<Result<ReviewEligibilityResponse>> HandleAsync(
        GetReviewEligibilityQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var projections = await catalogue
            .FindByProductsAsync([query.ProductId], cancellationToken)
            .ConfigureAwait(false);

        var variantIds = projections.Select(projection => projection.VariantId).Distinct().ToHashSet();

        if (variantIds.Count == 0)
        {
            return Result.Success(new ReviewEligibilityResponse(false, []));
        }

        var delivered = new List<PurchasedLine>();

        foreach (var variantId in variantIds)
        {
            var lines = await purchases
                .ListDeliveredLinesAsync(query.CustomerId, variantId, MaxEligible, cancellationToken)
                .ConfigureAwait(false);

            delivered.AddRange(lines);
        }

        if (delivered.Count == 0)
        {
            return Result.Success(new ReviewEligibilityResponse(false, []));
        }

        var lineIds = delivered.Select(line => line.OrderLineId).ToArray();

        var reviewed = await context.Reviews
            .AsNoTracking()
            .Where(review => lineIds.Contains(review.OrderLineId))
            .Select(review => review.OrderLineId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var eligible = delivered
            .Where(line => !reviewed.Contains(line.OrderLineId))
            .OrderByDescending(line => line.DeliveredAt)
            .Take(MaxEligible)
            .Select(line => new EligiblePurchaseResponse(
                line.OrderLineId,
                line.OrderNumber,
                line.VariantId,
                line.Sku,
                line.Name,
                line.DeliveredAt))
            .ToArray();

        return Result.Success(new ReviewEligibilityResponse(eligible.Length > 0, eligible));
    }
}

/// <summary>Lists what the caller has written, in whatever state.</summary>
/// <remarks>
/// The moderated projection rather than the public one, and deliberately: the author is the one
/// person besides a moderator who is entitled to know that their review is pending and why it was
/// refused. Without this, "where has my review gone" is a support ticket.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="media">Resolves the attached files.</param>
/// <param name="options">The page ceiling.</param>
internal sealed class ListMyReviewsQueryHandler(
    ReviewsDbContext context,
    IMediaLibrary media,
    IOptionsMonitor<ReviewsOptions> options)
    : IQueryHandler<ListMyReviewsQuery, PagedResult<ModeratedReviewResponse>>
{
    public async Task<Result<PagedResult<ModeratedReviewResponse>>> HandleAsync(
        ListMyReviewsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.CurrentValue.MaxPageSize);

        var rows = context.Reviews
            .AsNoTracking()
            .Where(review => review.CustomerId == query.CustomerId);

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(review => review.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(review => review.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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

/// <summary>
/// Records whether the caller found a review useful.
/// </summary>
/// <remarks>
/// A vote is a row keyed on the voter, so pressing the button again changes their opinion rather
/// than adding to a total — which is what makes a helpfulness score mean anything. The cached counts
/// on the review are then recomputed from the rows, never incremented, so a vote changed from
/// helpful to unhelpful moves both numbers correctly.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
internal sealed class VoteOnReviewCommandHandler(ReviewsDbContext context) : ICommandHandler<VoteOnReviewCommand>
{
    public async Task<Result> HandleAsync(VoteOnReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var review = await context.Reviews
            .FirstOrDefaultAsync(row => row.Id == command.ReviewId, cancellationToken)
            .ConfigureAwait(false);

        if (review is null || review.Status != ReviewStatus.Approved)
        {
            return Result.Failure(ReviewErrors.NotFound("review"));
        }

        if (review.CustomerId == command.CustomerId)
        {
            return Result.Failure(ReviewErrors.CannotVoteOwn);
        }

        var vote = await context.ReviewVotes
            .FirstOrDefaultAsync(
                row => row.ReviewId == command.ReviewId && row.CustomerId == command.CustomerId,
                cancellationToken)
            .ConfigureAwait(false);

        if (vote is null)
        {
            context.ReviewVotes.Add(ReviewVote.Cast(command.ReviewId, command.CustomerId, command.IsHelpful));
        }
        else
        {
            vote.Change(command.IsHelpful);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RecountAsync(context, review, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>Replaces both cached counts from the votes actually recorded.</summary>
    /// <param name="context">The Reviews data context.</param>
    /// <param name="review">The review being recounted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task RecountAsync(
        ReviewsDbContext context,
        Review review,
        CancellationToken cancellationToken)
    {
        var tallies = await context.ReviewVotes
            .AsNoTracking()
            .Where(vote => vote.ReviewId == review.Id)
            .GroupBy(vote => vote.IsHelpful)
            .Select(group => new { IsHelpful = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        review.RecordVotes(
            tallies.FirstOrDefault(tally => tally.IsHelpful)?.Count ?? 0,
            tallies.FirstOrDefault(tally => !tally.IsHelpful)?.Count ?? 0);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Withdraws a vote. Succeeds whether or not one was there.</summary>
/// <remarks>
/// Idempotent on purpose. Un-voting something you have not voted on is not an error a shopper needs
/// to be told about, and answering 404 for it would make the button's state a thing the storefront
/// had to get exactly right.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
internal sealed class WithdrawVoteCommandHandler(ReviewsDbContext context) : ICommandHandler<WithdrawVoteCommand>
{
    public async Task<Result> HandleAsync(WithdrawVoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var vote = await context.ReviewVotes
            .FirstOrDefaultAsync(
                row => row.ReviewId == command.ReviewId && row.CustomerId == command.CustomerId,
                cancellationToken)
            .ConfigureAwait(false);

        if (vote is null)
        {
            return Result.Success();
        }

        context.ReviewVotes.Remove(vote);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var review = await context.Reviews
            .FirstOrDefaultAsync(row => row.Id == command.ReviewId, cancellationToken)
            .ConfigureAwait(false);

        if (review is not null)
        {
            await VoteOnReviewCommandHandler.RecountAsync(context, review, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }
}
