using KlaraHome.Contracts.Reviews;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Reviews.Domain;
using KlaraHome.Modules.Reviews.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Reviews.Infrastructure.Rating;

/// <summary>
/// The single place a rating is recomputed and announced.
/// </summary>
/// <remarks>
/// <para>
/// Every path that can change a product's average goes through here — a review being approved, an
/// approved one being refused, a reviewer editing their score, a moderator reinstating something
/// after a complaint was dismissed. Five callers, one implementation, which is what stops the
/// storefront's average and the search index's from being computed two different ways.
/// </para>
/// <para>
/// It recounts rather than adjusts. Reading the approved reviews for one product and replacing all
/// seven numbers costs one grouped index read against a small set and removes an entire class of
/// bug: a redelivered message, a double-clicked approve button and a moderator undoing their own
/// decision all produce the same correct answer instead of three different drifts. An incrementing
/// counter would be cheaper and would be wrong within a week.
/// </para>
/// <para>
/// The two integration events are enqueued in the caller's own transaction, so a recompute that
/// then rolls back announces nothing. That is the whole point of the outbox (ADR-003) and it matters
/// more here than in most modules: a <c>ProductRatingChanged</c> that escaped a rolled-back approval
/// would leave the product page showing an average that no review in the database supports.
/// </para>
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="outbox">Where the recomputed aggregates are announced.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class RatingProjector(ReviewsDbContext context, IOutbox outbox, IClock clock)
{
    /// <summary>
    /// Recomputes and announces both aggregates a single review belongs to.
    /// </summary>
    /// <remarks>
    /// Both, always. A review contributes to its product's average and to its seller's, and a caller
    /// that had to remember to refresh the second would eventually forget — leaving a seller's rating
    /// frozen at whatever it was when somebody last thought about it.
    /// </remarks>
    /// <param name="review">The review that changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RefreshForAsync(Review review, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);

        await RefreshProductAsync(review.ProductId, cancellationToken).ConfigureAwait(false);
        await RefreshVendorAsync(review.VendorId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Recomputes one product's aggregate and announces it.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RefreshProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var histogram = await CountAsync(
                review => review.ProductId == productId,
                cancellationToken)
            .ConfigureAwait(false);

        var summary = await context.ProductRatings
            .FirstOrDefaultAsync(row => row.ProductId == productId, cancellationToken)
            .ConfigureAwait(false);

        if (summary is null)
        {
            // Nothing to store and nothing to announce. A product whose only review has just been
            // written and refused has never had a rating, and writing a row of zeroes for it would
            // fill the table with products nobody has an opinion about.
            if (histogram.Total == 0)
            {
                return;
            }

            summary = ProductRatingSummary.For(productId);
            context.ProductRatings.Add(summary);
        }

        summary.Recompute(histogram, clock.UtcNow);

        outbox.Enqueue(new ProductRatingChanged(productId, histogram.Average, histogram.Total));
    }

    /// <summary>Recomputes one seller's aggregate and announces it.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RefreshVendorAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        var histogram = await CountAsync(
                review => review.VendorId == vendorId,
                cancellationToken)
            .ConfigureAwait(false);

        var summary = await context.VendorRatings
            .FirstOrDefaultAsync(row => row.VendorId == vendorId, cancellationToken)
            .ConfigureAwait(false);

        if (summary is null)
        {
            if (histogram.Total == 0)
            {
                return;
            }

            summary = VendorRatingSummary.For(vendorId);
            context.VendorRatings.Add(summary);
        }

        summary.Recompute(histogram, clock.UtcNow);

        outbox.Enqueue(new VendorRatingChanged(vendorId, histogram.Average, histogram.Total));
    }

    /// <summary>
    /// Counts the approved reviews matching a predicate, grouped by score.
    /// </summary>
    /// <remarks>
    /// Grouped in the database rather than by reading the rows and counting them here. The set is
    /// small for almost every product and enormous for the handful that carry a store, and the
    /// handful are exactly the ones a product page is rendered for most often.
    /// </remarks>
    /// <param name="predicate">Which reviews to count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<RatingHistogram> CountAsync(
        System.Linq.Expressions.Expression<Func<Review, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var buckets = await context.Reviews
            .AsNoTracking()
            .Where(predicate)
            .Where(review => review.Status == ReviewStatus.Approved)
            .GroupBy(review => review.Rating)
            .Select(group => new { Rating = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var counts = new int[Review.MaxRating + 1];

        foreach (var bucket in buckets)
        {
            if (bucket.Rating is >= Review.MinRating and <= Review.MaxRating)
            {
                counts[bucket.Rating] = bucket.Count;
            }
        }

        return new RatingHistogram(counts[1], counts[2], counts[3], counts[4], counts[5]);
    }
}
