using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Reviews.Domain;

/// <summary>
/// Where a review stands (docs/03-database-design.md §4.15).
/// </summary>
/// <remarks>
/// Three states and no more. A review is waiting to be looked at, it is visible, or it has been
/// refused — and the fourth state somebody always proposes, "visible but flagged", is not a state:
/// it is an abuse report, which is its own row precisely so that a review's visibility and the
/// complaints about it are separate facts.
/// </remarks>
internal enum ReviewStatus
{
    /// <summary>Written and waiting for a moderator. Invisible to everybody but its author.</summary>
    Pending = 0,

    /// <summary>Visible on the storefront and counted in the product's rating.</summary>
    Approved = 1,

    /// <summary>Refused. Invisible, uncounted, and kept — a deleted review cannot be appealed.</summary>
    Rejected = 2,
}

/// <summary>
/// One image a reviewer attached.
/// </summary>
/// <remarks>
/// A soft reference to <c>media.files</c>, like every other file id in this system: no foreign key
/// crosses a schema (docs/01-architecture.md §2.1), and a file that has since been removed resolves
/// to nothing rather than breaking the review that named it (ADR-016).
/// </remarks>
/// <param name="FileId">The stored file.</param>
/// <param name="Caption">What the reviewer called it, or null.</param>
internal sealed record ReviewImage(Guid FileId, string? Caption);

/// <summary>
/// One shopper's opinion of one thing they were actually sent
/// (docs/03-database-design.md §4.15).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OrderLineId"/> is the point of the whole aggregate. It is unique, it is not nullable,
/// and it is resolved through <c>IOrderPurchases</c> before a row is ever created — so a review
/// exists if and only if somebody received the thing, and the same line can produce exactly one.
/// The uniqueness is a database index rather than a check in the handler, because two submissions
/// racing each other is the ordinary case on a slow connection and a check-then-write loses it.
/// </para>
/// <para>
/// It records the <em>variant</em> bought and is displayed against the <see cref="ProductId"/>. That
/// asymmetry is deliberate: a shopper who bought the beige one is reviewing the product, and a page
/// that showed only reviews of the exact variant in front of them would show almost none. The
/// variant is kept because it is true and because a merchandiser deciding whether one colour is the
/// problem needs it.
/// </para>
/// <para>
/// <see cref="VendorId"/> is on the review because a marketplace review is of a sale, not only of a
/// thing. The same product from two sellers can arrive well packed or badly, on time or late, and
/// the seller's own rating is the average of the reviews of their sales. It is frozen from the order
/// line and never re-resolved: which seller currently wins the buy box has nothing to do with who
/// this shopper actually bought from.
/// </para>
/// <para>
/// <see cref="HelpfulCount"/> is a cache of the votes in <see cref="ReviewVote"/> rather than an
/// independent counter, which is what stops it from being stuffed: a vote is a row keyed on the
/// voter, so a second vote from the same person replaces their first rather than adding to a total.
/// </para>
/// </remarks>
internal sealed class Review : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    /// <summary>The lowest score a review may carry.</summary>
    public const int MinRating = 1;

    /// <summary>The highest score a review may carry.</summary>
    public const int MaxRating = 5;

    /// <summary>The longest headline.</summary>
    public const int MaxTitleLength = 150;

    /// <summary>The longest body.</summary>
    public const int MaxBodyLength = 4_000;

    /// <summary>The longest reply a seller may write.</summary>
    public const int MaxReplyLength = 2_000;

    /// <summary>The longest note a moderator may leave.</summary>
    public const int MaxNoteLength = 500;

    /// <summary>The most images one review may carry.</summary>
    /// <remarks>
    /// Six, which is what a product page renders in one row on a phone. It is not a storage limit —
    /// it is the number beyond which a review stops being read.
    /// </remarks>
    public const int MaxImages = 6;

    private readonly List<ReviewImage> _images = [];

    private Review(
        Guid id,
        Guid productId,
        Guid variantId,
        Guid vendorId,
        Guid customerId,
        Guid orderLineId,
        int rating)
        : base(id)
    {
        ProductId = productId;
        VariantId = variantId;
        VendorId = vendorId;
        CustomerId = customerId;
        OrderLineId = orderLineId;
        Rating = rating;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Review()
    {
    }

    /// <summary>The product the review is displayed against.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>The exact variant bought.</summary>
    public Guid VariantId { get; private set; }

    /// <summary>The seller who sold it, frozen from the order line.</summary>
    public Guid VendorId { get; private set; }

    /// <summary>Who wrote it.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The delivered line that proves the purchase. Unique per tenant.</summary>
    public Guid OrderLineId { get; private set; }

    /// <summary>The score, one to five.</summary>
    public int Rating { get; private set; }

    /// <summary>The headline, or null.</summary>
    public string? Title { get; private set; }

    /// <summary>The body, or null. A rating with no words is a legitimate review.</summary>
    public string? Body { get; private set; }

    /// <summary>The name to display, captured at submission.</summary>
    /// <remarks>
    /// Stored rather than resolved so a review keeps the name it was written under, and so rendering
    /// a page of reviews does not become a page of lookups into the identity schema. It is a display
    /// name and never an email address — docs/07-security-compliance.md §4 is explicit that a review
    /// is a public document.
    /// </remarks>
    public string? AuthorName { get; private set; }

    /// <summary>The images attached, in the order they were given.</summary>
    public IReadOnlyList<ReviewImage> Images => _images;

    /// <summary>Where it stands.</summary>
    public ReviewStatus Status { get; private set; } = ReviewStatus.Pending;

    /// <summary>Who decided, or null while it is pending.</summary>
    public Guid? ModeratedBy { get; private set; }

    /// <summary>When they decided, in UTC.</summary>
    public DateTimeOffset? ModeratedAt { get; private set; }

    /// <summary>Why they decided, for the author and for the next moderator.</summary>
    public string? ModerationNote { get; private set; }

    /// <summary>When it became visible, in UTC. Null unless it has been approved at least once.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>The seller's public reply, or null.</summary>
    public string? VendorReply { get; private set; }

    /// <summary>When the seller replied, in UTC.</summary>
    public DateTimeOffset? VendorRepliedAt { get; private set; }

    /// <summary>Which of the seller's staff replied.</summary>
    public Guid? VendorRepliedBy { get; private set; }

    /// <summary>How many people said it helped. A cache of the votes.</summary>
    public int HelpfulCount { get; private set; }

    /// <summary>How many said it did not. Kept apart so a ratio is available, not only a net.</summary>
    public int NotHelpfulCount { get; private set; }

    /// <summary>How many people have reported it. A cache of the abuse reports.</summary>
    public int ReportCount { get; private set; }

    /// <summary>Whether it is tied to a delivered purchase. Always true; carried so it is never assumed.</summary>
    public bool IsVerifiedPurchase { get; private set; } = true;

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

    /// <summary>Whether it counts towards the product's and the seller's rating.</summary>
    public bool IsCounted => Status == ReviewStatus.Approved;

    /// <summary>
    /// Writes a review against a purchase that has already been verified.
    /// </summary>
    /// <remarks>
    /// Every identifier here comes off the <c>PurchasedLine</c> the Orders module returned rather
    /// than off the request. A caller that could name its own product, variant or seller could write
    /// a five-star review of anything by quoting one line it really did buy.
    /// </remarks>
    /// <param name="productId">The product, from the purchased line.</param>
    /// <param name="variantId">The variant, from the purchased line.</param>
    /// <param name="vendorId">The seller, from the purchased line.</param>
    /// <param name="customerId">The author.</param>
    /// <param name="orderLineId">The delivered line that proves the purchase.</param>
    /// <param name="rating">One to five, already validated.</param>
    /// <param name="title">The headline.</param>
    /// <param name="body">The body.</param>
    /// <param name="authorName">The name to display.</param>
    /// <param name="images">The images attached.</param>
    public static Review Write(
        Guid productId,
        Guid variantId,
        Guid vendorId,
        Guid customerId,
        Guid orderLineId,
        int rating,
        string? title,
        string? body,
        string? authorName,
        IEnumerable<ReviewImage>? images = null)
    {
        var review = new Review(
            UuidV7.New(),
            Guard.NotEmpty(productId),
            Guard.NotEmpty(variantId),
            Guard.NotEmpty(vendorId),
            Guard.NotEmpty(customerId),
            Guard.NotEmpty(orderLineId),
            Math.Clamp(rating, MinRating, MaxRating))
        {
            Title = Trim(title, MaxTitleLength),
            Body = Trim(body, MaxBodyLength),
            AuthorName = Trim(authorName, 120),
        };

        if (images is not null)
        {
            review._images.AddRange(images.Take(MaxImages));
        }

        return review;
    }

    /// <summary>
    /// Rewrites the author's own words.
    /// </summary>
    /// <remarks>
    /// Editing returns an approved review to <see cref="ReviewStatus.Pending"/>, which is the only
    /// defensible behaviour: a review that could be approved with acceptable text and then rewritten
    /// would make moderation a formality anybody could walk around. The rating may change with it,
    /// and the caller republishes the aggregate afterwards.
    /// </remarks>
    /// <param name="rating">The new score.</param>
    /// <param name="title">The new headline.</param>
    /// <param name="body">The new body.</param>
    /// <param name="images">The images, replacing whatever was there.</param>
    public void Revise(int rating, string? title, string? body, IEnumerable<ReviewImage>? images)
    {
        Rating = Math.Clamp(rating, MinRating, MaxRating);
        Title = Trim(title, MaxTitleLength);
        Body = Trim(body, MaxBodyLength);

        _images.Clear();

        if (images is not null)
        {
            _images.AddRange(images.Take(MaxImages));
        }

        Status = ReviewStatus.Pending;
        ModeratedBy = null;
        ModeratedAt = null;
        ModerationNote = null;
    }

    /// <summary>
    /// Makes it visible.
    /// </summary>
    /// <remarks>
    /// <see cref="PublishedAt"/> is stamped once and never moved. A review that was approved, refused
    /// on a complaint and then reinstated keeps the date it was first shown, because that is when the
    /// shopper said it and it is the date the storefront displays.
    /// </remarks>
    /// <param name="moderatorId">Who approved it, or null when the store approves automatically.</param>
    /// <param name="at">When.</param>
    /// <param name="note">Why, if anything needs saying.</param>
    public void Approve(Guid? moderatorId, DateTimeOffset at, string? note = null)
    {
        Status = ReviewStatus.Approved;
        ModeratedBy = moderatorId;
        ModeratedAt = at;
        ModerationNote = Trim(note, MaxNoteLength);
        PublishedAt ??= at;
    }

    /// <summary>Refuses it. The row stays, so the decision can be explained and reversed.</summary>
    /// <param name="moderatorId">Who refused it.</param>
    /// <param name="at">When.</param>
    /// <param name="note">Why. Required by the handler, because a refusal with no reason cannot be appealed.</param>
    public void Reject(Guid? moderatorId, DateTimeOffset at, string? note)
    {
        Status = ReviewStatus.Rejected;
        ModeratedBy = moderatorId;
        ModeratedAt = at;
        ModerationNote = Trim(note, MaxNoteLength);
    }

    /// <summary>Records the seller's public reply.</summary>
    /// <param name="reply">What they said. Null clears it.</param>
    /// <param name="staffId">Which of their staff wrote it.</param>
    /// <param name="at">When.</param>
    public void Reply(string? reply, Guid? staffId, DateTimeOffset at)
    {
        VendorReply = Trim(reply, MaxReplyLength);
        VendorRepliedBy = VendorReply is null ? null : staffId;
        VendorRepliedAt = VendorReply is null ? null : at;
    }

    /// <summary>Refreshes the helpfulness caches from the votes actually recorded.</summary>
    /// <remarks>
    /// Set from a count rather than incremented, because a vote can be changed and withdrawn. A
    /// counter that only ever went up would drift the first time somebody clicked twice.
    /// </remarks>
    /// <param name="helpful">How many said it helped.</param>
    /// <param name="notHelpful">How many said it did not.</param>
    public void RecordVotes(int helpful, int notHelpful)
    {
        HelpfulCount = Math.Max(0, helpful);
        NotHelpfulCount = Math.Max(0, notHelpful);
    }

    /// <summary>Refreshes the abuse-report cache.</summary>
    /// <param name="reports">How many open reports there are against it.</param>
    public void RecordReports(int reports) => ReportCount = Math.Max(0, reports);

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

/// <summary>
/// One person's opinion of whether a review was any use.
/// </summary>
/// <remarks>
/// A row per voter rather than a counter, and that is the whole design. A counter can be pressed
/// repeatedly; a row keyed on <c>(review_id, customer_id)</c> can be changed and withdrawn but never
/// doubled, and the cached totals on the review are recomputed from these rows.
/// </remarks>
internal sealed class ReviewVote : Entity<Guid>, ITenantScoped, IAuditable
{
    private ReviewVote(Guid id, Guid reviewId, Guid customerId, bool isHelpful)
        : base(id)
    {
        ReviewId = reviewId;
        CustomerId = customerId;
        IsHelpful = isHelpful;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ReviewVote()
    {
    }

    /// <summary>The review voted on.</summary>
    public Guid ReviewId { get; private set; }

    /// <summary>Who voted.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Whether they found it helpful.</summary>
    public bool IsHelpful { get; private set; }

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

    /// <summary>Records a vote.</summary>
    /// <param name="reviewId">The review.</param>
    /// <param name="customerId">The voter.</param>
    /// <param name="isHelpful">Their opinion.</param>
    public static ReviewVote Cast(Guid reviewId, Guid customerId, bool isHelpful)
        => new(UuidV7.New(), Guard.NotEmpty(reviewId), Guard.NotEmpty(customerId), isHelpful);

    /// <summary>Changes a vote already cast.</summary>
    /// <param name="isHelpful">The new opinion.</param>
    public void Change(bool isHelpful) => IsHelpful = isHelpful;
}
