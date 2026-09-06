using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Reviews.Domain;

/// <summary>
/// What everybody thinks of one product, kept as a row.
/// </summary>
/// <remarks>
/// <para>
/// A stored aggregate rather than an aggregate query, and the reason is the product page. That page
/// needs an average and a histogram on every render, the reviews behind it grow without bound, and
/// <c>avg(rating) group by product</c> over a table of that shape is a scan the storefront should
/// never pay for.
/// </para>
/// <para>
/// It is recomputed rather than adjusted. Every write path calls one recompute that reads the counts
/// straight from the reviews and replaces all seven numbers, which costs one grouped read of a small
/// indexed set and removes an entire class of bug: a moderator rejecting an approved review, a
/// reviewer editing their score, and a redelivered message all produce the same correct answer
/// instead of three different drifts.
/// </para>
/// <para>
/// The histogram is stored as five columns rather than a JSON document because every one of them is
/// rendered, and because "how many one-star reviews does this product have" is a question a
/// merchandising query should be able to ask with a <c>WHERE</c> clause.
/// </para>
/// </remarks>
internal sealed class ProductRatingSummary : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private ProductRatingSummary(Guid id, Guid productId)
        : base(id)
        => ProductId = productId;

    /// <summary>Required by EF Core's materialiser.</summary>
    private ProductRatingSummary()
    {
    }

    /// <summary>The product. Unique per tenant.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// The mean of every approved review, to one decimal place, or null when there are none.
    /// </summary>
    /// <remarks>
    /// Null and not zero. A product nobody has reviewed has no opinion; rendering that as nought out
    /// of five would put every new product below every bad one in a rating sort.
    /// </remarks>
    public decimal? Average { get; private set; }

    /// <summary>How many approved reviews there are.</summary>
    public int Count { get; private set; }

    /// <summary>How many gave one star.</summary>
    public int OneStar { get; private set; }

    /// <summary>How many gave two.</summary>
    public int TwoStar { get; private set; }

    /// <summary>How many gave three.</summary>
    public int ThreeStar { get; private set; }

    /// <summary>How many gave four.</summary>
    public int FourStar { get; private set; }

    /// <summary>How many gave five.</summary>
    public int FiveStar { get; private set; }

    /// <summary>When the numbers were last recomputed, in UTC.</summary>
    public DateTimeOffset? RecomputedAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Opens the row for a product that has just been reviewed for the first time.</summary>
    /// <param name="productId">The product.</param>
    public static ProductRatingSummary For(Guid productId)
        => new(UuidV7.New(), Guard.NotEmpty(productId));

    /// <summary>
    /// Replaces every number from a fresh count of the approved reviews.
    /// </summary>
    /// <param name="histogram">How many reviews gave each score, one to five.</param>
    /// <param name="at">When the recount was taken.</param>
    public void Recompute(RatingHistogram histogram, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(histogram);

        OneStar = histogram.One;
        TwoStar = histogram.Two;
        ThreeStar = histogram.Three;
        FourStar = histogram.Four;
        FiveStar = histogram.Five;
        Count = histogram.Total;
        Average = histogram.Average;
        RecomputedAt = at;
    }
}

/// <summary>
/// What everybody thinks of one seller, kept as a row.
/// </summary>
/// <remarks>
/// The same shape as <see cref="ProductRatingSummary"/> over a different grouping of the same
/// reviews, and it is a separate table rather than a second grouping key on one because they are
/// read by different people for different reasons: a shopper reads a product's stars, a category
/// manager reads a seller's, and the seller's number is what the Vendors module stores against the
/// seller and what the buy-box rule may rank on.
/// </remarks>
internal sealed class VendorRatingSummary : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private VendorRatingSummary(Guid id, Guid vendorId)
        : base(id)
        => VendorId = vendorId;

    /// <summary>Required by EF Core's materialiser.</summary>
    private VendorRatingSummary()
    {
    }

    /// <summary>The seller. Unique per tenant.</summary>
    public Guid VendorId { get; private set; }

    /// <summary>The mean of every approved review of their sales, or null when they have none.</summary>
    public decimal? Average { get; private set; }

    /// <summary>How many approved reviews that is over.</summary>
    public int Count { get; private set; }

    /// <summary>When the numbers were last recomputed, in UTC.</summary>
    public DateTimeOffset? RecomputedAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Opens the row for a seller who has just been reviewed for the first time.</summary>
    /// <param name="vendorId">The seller.</param>
    public static VendorRatingSummary For(Guid vendorId)
        => new(UuidV7.New(), Guard.NotEmpty(vendorId));

    /// <summary>Replaces both numbers from a fresh count.</summary>
    /// <param name="histogram">How many reviews gave each score.</param>
    /// <param name="at">When the recount was taken.</param>
    public void Recompute(RatingHistogram histogram, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(histogram);

        Count = histogram.Total;
        Average = histogram.Average;
        RecomputedAt = at;
    }
}

/// <summary>
/// How many approved reviews gave each score.
/// </summary>
/// <remarks>
/// The one place an average is worked out, so a product's and a seller's cannot round differently.
/// One decimal place, away from zero, because that is what a storefront renders and what a half-star
/// widget is drawn from; keeping four decimals would let a page show 4.2 while a sort ordered on
/// 4.1500001.
/// </remarks>
/// <param name="One">How many gave one star.</param>
/// <param name="Two">How many gave two.</param>
/// <param name="Three">How many gave three.</param>
/// <param name="Four">How many gave four.</param>
/// <param name="Five">How many gave five.</param>
internal sealed record RatingHistogram(int One, int Two, int Three, int Four, int Five)
{
    /// <summary>The empty histogram — a product or seller with no approved reviews at all.</summary>
    public static RatingHistogram Empty { get; } = new(0, 0, 0, 0, 0);

    /// <summary>How many approved reviews there are.</summary>
    public int Total => One + Two + Three + Four + Five;

    /// <summary>The mean, to one decimal place, or null when there is nothing to average.</summary>
    public decimal? Average
    {
        get
        {
            var total = Total;

            if (total == 0)
            {
                return null;
            }

            var sum = One + (2 * Two) + (3 * Three) + (4 * Four) + (5 * Five);

            return Math.Round((decimal)sum / total, 1, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>Builds a histogram from a sequence of scores.</summary>
    /// <param name="ratings">The scores of the approved reviews.</param>
    public static RatingHistogram From(IEnumerable<int> ratings)
    {
        ArgumentNullException.ThrowIfNull(ratings);

        var buckets = new int[Review.MaxRating + 1];

        foreach (var rating in ratings)
        {
            if (rating is >= Review.MinRating and <= Review.MaxRating)
            {
                buckets[rating]++;
            }
        }

        return new RatingHistogram(buckets[1], buckets[2], buckets[3], buckets[4], buckets[5]);
    }
}
