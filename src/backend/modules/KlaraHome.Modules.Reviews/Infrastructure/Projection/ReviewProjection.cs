using KlaraHome.Contracts.Media;
using KlaraHome.Modules.Reviews.Application;
using KlaraHome.Modules.Reviews.Domain;

namespace KlaraHome.Modules.Reviews.Infrastructure.Projection;

/// <summary>
/// Turns this module's entities into the shapes the API returns.
/// </summary>
/// <remarks>
/// One place, so two endpoints cannot answer with two different spellings of the same review. The
/// shopper's projection and the moderator's are deliberately separate methods rather than one with a
/// flag: the difference between them is which facts a caller is entitled to see, and a boolean
/// parameter is exactly the kind of thing that gets passed wrongly once and leaks a customer id onto
/// a public page.
/// </remarks>
internal static class ReviewProjection
{
    /// <summary>The shopper's view of a review.</summary>
    /// <param name="review">The review.</param>
    /// <param name="files">The resolved media, keyed on file id. Missing files render as no image.</param>
    public static ReviewResponse ToPublic(Review review, IReadOnlyDictionary<Guid, MediaFile>? files = null)
    {
        ArgumentNullException.ThrowIfNull(review);

        return new ReviewResponse(
            review.Id,
            review.ProductId,
            review.VariantId,
            review.Rating,
            review.Title,
            review.Body,
            review.AuthorName,
            review.IsVerifiedPurchase,
            ToImages(review, files),
            review.HelpfulCount,
            review.NotHelpfulCount,
            review.VendorReply,
            review.VendorRepliedAt,
            review.PublishedAt);
    }

    /// <summary>The moderator's view of a review.</summary>
    /// <param name="review">The review.</param>
    /// <param name="files">The resolved media, keyed on file id.</param>
    public static ModeratedReviewResponse ToModerated(
        Review review,
        IReadOnlyDictionary<Guid, MediaFile>? files = null)
    {
        ArgumentNullException.ThrowIfNull(review);

        return new ModeratedReviewResponse(
            review.Id,
            review.ProductId,
            review.VariantId,
            review.VendorId,
            review.CustomerId,
            review.OrderLineId,
            review.Rating,
            review.Title,
            review.Body,
            review.AuthorName,
            review.Status.ToString(),
            review.ModeratedBy,
            review.ModeratedAt,
            review.ModerationNote,
            ToImages(review, files),
            review.HelpfulCount,
            review.NotHelpfulCount,
            review.ReportCount,
            review.VendorReply,
            review.PublishedAt,
            review.CreatedAt);
    }

    /// <summary>A product's aggregate.</summary>
    /// <param name="productId">The product, so a product with no row still answers.</param>
    /// <param name="summary">The stored aggregate, or null when nothing has been reviewed.</param>
    public static RatingSummaryResponse ToSummary(Guid productId, ProductRatingSummary? summary)
        => summary is null
            ? new RatingSummaryResponse(productId, null, 0, 0, 0, 0, 0, 0)
            : new RatingSummaryResponse(
                summary.ProductId,
                summary.Average,
                summary.Count,
                summary.OneStar,
                summary.TwoStar,
                summary.ThreeStar,
                summary.FourStar,
                summary.FiveStar);

    /// <summary>The shopper's view of a question and its approved answers.</summary>
    /// <param name="question">The question, with its answers loaded.</param>
    public static QuestionResponse ToPublic(Question question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var answers = question.Answers
            .Where(answer => answer.Status == PostStatus.Approved)
            .OrderBy(answer => answer.CreatedAt)
            .Select(answer => new AnswerResponse(
                answer.Id,
                answer.Body,
                answer.AuthorType.ToString(),
                answer.AuthorName,
                answer.PublishedAt))
            .ToArray();

        return new QuestionResponse(
            question.Id,
            question.ProductId,
            question.Body,
            question.AuthorName,
            answers.Length,
            answers,
            question.PublishedAt);
    }

    /// <summary>The moderator's view of a question and every answer under it.</summary>
    /// <param name="question">The question, with its answers loaded.</param>
    public static ModeratedQuestionResponse ToModerated(Question question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var answers = question.Answers
            .OrderBy(answer => answer.CreatedAt)
            .Select(answer => new ModeratedAnswerResponse(
                answer.Id,
                answer.Body,
                answer.AuthorType.ToString(),
                answer.AuthorName,
                answer.VendorId,
                answer.Status.ToString(),
                answer.ReportCount,
                answer.CreatedAt))
            .ToArray();

        return new ModeratedQuestionResponse(
            question.Id,
            question.ProductId,
            question.CustomerId,
            question.Body,
            question.AuthorName,
            question.Status.ToString(),
            question.ReportCount,
            answers,
            question.CreatedAt);
    }

    /// <summary>A standing alert.</summary>
    /// <param name="subscription">The subscription.</param>
    public static StockSubscriptionResponse ToResponse(StockSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return new StockSubscriptionResponse(
            subscription.Id,
            subscription.Kind.ToString(),
            subscription.VariantId,
            subscription.ProductId,
            subscription.Status.ToString(),
            subscription.TargetPrice,
            subscription.PriceAtSubscription,
            subscription.NotifiedAt,
            subscription.ExpiresAt,
            subscription.CreatedAt);
    }

    /// <summary>A complaint.</summary>
    /// <param name="report">The report.</param>
    public static AbuseReportResponse ToResponse(AbuseReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new AbuseReportResponse(
            report.Id,
            report.Target.ToString(),
            report.TargetId,
            report.Reason.ToString(),
            report.Note,
            report.Status.ToString(),
            report.ResolvedAt,
            report.Resolution,
            report.CreatedAt);
    }

    /// <summary>
    /// Resolves a review's attached files into something a browser can load.
    /// </summary>
    /// <remarks>
    /// A file the media library no longer has resolves to a null URL rather than being dropped. The
    /// caption is still worth showing, and silently shrinking a gallery is how a bug of this kind
    /// goes unnoticed for a year (ADR-016).
    /// </remarks>
    private static ReviewImageResponse[] ToImages(Review review, IReadOnlyDictionary<Guid, MediaFile>? files)
        => [.. review.Images.Select(image => new ReviewImageResponse(
            image.FileId,
            files is not null && files.TryGetValue(image.FileId, out var file) ? file.Url : null,
            image.Caption))];
}
