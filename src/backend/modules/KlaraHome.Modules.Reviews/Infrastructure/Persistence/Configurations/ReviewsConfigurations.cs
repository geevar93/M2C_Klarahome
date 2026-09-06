using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Reviews.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Reviews.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>CHECK</c> lists, written once so a column and the constraint on it cannot drift apart.
/// </summary>
/// <remarks>
/// Every enum in this schema is stored as its word and constrained to the words the enum has, which
/// is what docs/03-database-design.md §1 asks for. The rating bound is the one that matters most: a
/// score outside one to five would corrupt every average on the platform, and it would do so
/// silently — the histogram would simply not add up to the count and nobody would notice for months.
/// </remarks>
internal static class ReviewCheckConstraints
{
    /// <summary>A score is one to five.</summary>
    public const string Rating = "rating BETWEEN 1 AND 5";

    /// <summary>The values <c>reviews.status</c> accepts.</summary>
    public const string ReviewStatuses = "status IN ('Pending', 'Approved', 'Rejected')";

    /// <summary>The values a question's or an answer's status accepts.</summary>
    public const string PostStatuses = "status IN ('Pending', 'Approved', 'Rejected')";

    /// <summary>The values <c>answers.author_type</c> accepts.</summary>
    public const string AnswerAuthors = "author_type IN ('Customer', 'Vendor', 'Store')";

    /// <summary>A seller's answer names the seller, and nobody else's does.</summary>
    /// <remarks>
    /// The invariant behind the label. "Answered by the seller" is the most trusted line on a product
    /// page, and an answer carrying that label with no seller attached would be one nobody could
    /// trace back to a person.
    /// </remarks>
    public const string AnswerVendor =
        "(author_type = 'Vendor' AND vendor_id IS NOT NULL) OR (author_type <> 'Vendor' AND vendor_id IS NULL)";

    /// <summary>The values <c>abuse_reports.target_type</c> accepts.</summary>
    public const string ReportTargets = "target_type IN ('Review', 'Question', 'Answer')";

    /// <summary>The values <c>abuse_reports.reason</c> accepts.</summary>
    public const string ReportReasons =
        "reason IN ('Spam', 'Offensive', 'Irrelevant', 'Misleading', 'PersonalData', 'Illegal', 'Other')";

    /// <summary>The values <c>abuse_reports.status</c> accepts.</summary>
    public const string ReportStatuses = "status IN ('Open', 'Upheld', 'Dismissed')";

    /// <summary>The values <c>stock_subscriptions.kind</c> accepts.</summary>
    public const string SubscriptionKinds = "kind IN ('BackInStock', 'PriceDrop')";

    /// <summary>The values <c>stock_subscriptions.status</c> accepts.</summary>
    public const string SubscriptionStatuses =
        "status IN ('Active', 'Notified', 'Cancelled', 'Expired')";

    /// <summary>
    /// An alert has somewhere to go.
    /// </summary>
    /// <remarks>
    /// A subscription with neither a customer nor an email address is a row that can never fire, and
    /// the worker sweeping them would carry it for ever. The handler refuses it too; this is the copy
    /// that holds for a row written any other way.
    /// </remarks>
    public const string SubscriptionContact = "customer_id IS NOT NULL OR email IS NOT NULL";

    /// <summary>Only a price-drop subscription names a price.</summary>
    public const string SubscriptionTarget =
        "kind = 'PriceDrop' OR (target_price IS NULL AND price_at_subscription IS NULL)";

    /// <summary>A named target price is a price.</summary>
    public const string TargetPricePositive = "target_price IS NULL OR target_price > 0";

    /// <summary>Counts are counts.</summary>
    public const string Counts =
        "helpful_count >= 0 AND not_helpful_count >= 0 AND report_count >= 0";

    /// <summary>An average is on the scale, when there is one at all.</summary>
    /// <remarks>
    /// Paired with the count, because the two are only ever meaningful together: an average with no
    /// reviews behind it and reviews with no average are both states this table must never reach.
    /// </remarks>
    public const string AverageScale =
        "(average IS NULL AND count = 0) OR (average BETWEEN 1 AND 5 AND count > 0)";

    /// <summary>A histogram is made of counts, and they add up to the count.</summary>
    public const string Histogram =
        "one_star >= 0 AND two_star >= 0 AND three_star >= 0 AND four_star >= 0 AND five_star >= 0 "
        + "AND one_star + two_star + three_star + four_star + five_star = count";

    /// <summary>Positions are ordinals.</summary>
    public const string Priority = "priority >= 0";

    /// <summary>Item counts are counts.</summary>
    public const string ItemCount = "item_count >= 0";
}

/// <summary>Maps <see cref="Review"/> to <c>reviews.reviews</c>.</summary>
internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("reviews", table =>
        {
            table.HasCheckConstraint("ck_reviews_rating", ReviewCheckConstraints.Rating);
            table.HasCheckConstraint("ck_reviews_status", ReviewCheckConstraints.ReviewStatuses);
            table.HasCheckConstraint("ck_reviews_counts", ReviewCheckConstraints.Counts);
        });

        builder.HasKey(review => review.Id);
        builder.Property(review => review.Id).ValueGeneratedNever();

        builder.Property(review => review.Title).HasMaxLength(Review.MaxTitleLength);
        builder.Property(review => review.Body).HasMaxLength(Review.MaxBodyLength);
        builder.Property(review => review.AuthorName).HasMaxLength(120);
        builder.Property(review => review.VendorReply).HasMaxLength(Review.MaxReplyLength);
        builder.Property(review => review.ModerationNote).HasMaxLength(Review.MaxNoteLength);

        builder.Property(review => review.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // jsonb and read whole. Nothing joins to it and nothing filters on it: the images are
        // rendered with the review and never on their own, which is the narrow case §1 allows JSON
        // for.
        builder.OwnsMany(review => review.Images, images => images.ToJson());
        builder.Navigation(review => review.Images).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The acceptance criterion, enforced by the database rather than by the handler that checks
        // for it. Two submissions racing each other is the ordinary case on a slow connection, and a
        // check-then-write loses that race; this index cannot.
        builder.HasIndex(review => new { review.TenantId, review.OrderLineId })
            .IsUnique()
            .HasDatabaseName("ux_reviews_order_line");

        // The product page's read: this product's approved reviews, newest first.
        builder.HasIndex(review => new { review.TenantId, review.ProductId, review.Status, review.PublishedAt });

        // The moderation queue, oldest first — which is the order it has to be worked in.
        builder.HasIndex(review => new { review.TenantId, review.Status, review.CreatedAt });

        // A seller's own reviews, for their portal and for their reply.
        builder.HasIndex(review => new { review.TenantId, review.VendorId, review.Status });

        // "Have I reviewed this", asked by the product page for the signed-in shopper.
        builder.HasIndex(review => new { review.TenantId, review.CustomerId, review.ProductId });
    }
}

/// <summary>Maps <see cref="ReviewVote"/> to <c>reviews.review_votes</c>.</summary>
internal sealed class ReviewVoteConfiguration : IEntityTypeConfiguration<ReviewVote>
{
    public void Configure(EntityTypeBuilder<ReviewVote> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("review_votes");

        builder.HasKey(vote => vote.Id);
        builder.Property(vote => vote.Id).ValueGeneratedNever();

        // One vote per person per review, and this index is the whole anti-stuffing mechanism. A
        // second click changes the row it finds; it cannot add a second.
        builder.HasIndex(vote => new { vote.ReviewId, vote.CustomerId })
            .IsUnique()
            .HasDatabaseName("ux_review_votes_voter");
    }
}

/// <summary>Maps <see cref="ProductRatingSummary"/> to <c>reviews.product_ratings</c>.</summary>
internal sealed class ProductRatingSummaryConfiguration : IEntityTypeConfiguration<ProductRatingSummary>
{
    public void Configure(EntityTypeBuilder<ProductRatingSummary> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("product_ratings", table =>
        {
            table.HasCheckConstraint("ck_product_ratings_average", ReviewCheckConstraints.AverageScale);
            table.HasCheckConstraint("ck_product_ratings_histogram", ReviewCheckConstraints.Histogram);
        });

        builder.HasKey(summary => summary.Id);
        builder.Property(summary => summary.Id).ValueGeneratedNever();

        // One decimal place is what is stored as well as what is displayed. Keeping four would let a
        // page show 4.2 while a rating sort ordered on 4.1500001.
        builder.Property(summary => summary.Average).HasColumnType("numeric(2,1)");

        builder.HasIndex(summary => new { summary.TenantId, summary.ProductId })
            .IsUnique()
            .HasDatabaseName("ux_product_ratings_product");
    }
}

/// <summary>Maps <see cref="VendorRatingSummary"/> to <c>reviews.vendor_ratings</c>.</summary>
internal sealed class VendorRatingSummaryConfiguration : IEntityTypeConfiguration<VendorRatingSummary>
{
    public void Configure(EntityTypeBuilder<VendorRatingSummary> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("vendor_ratings", table =>
            table.HasCheckConstraint("ck_vendor_ratings_average", ReviewCheckConstraints.AverageScale));

        builder.HasKey(summary => summary.Id);
        builder.Property(summary => summary.Id).ValueGeneratedNever();

        builder.Property(summary => summary.Average).HasColumnType("numeric(2,1)");

        builder.HasIndex(summary => new { summary.TenantId, summary.VendorId })
            .IsUnique()
            .HasDatabaseName("ux_vendor_ratings_vendor");
    }
}

/// <summary>Maps <see cref="Question"/> to <c>reviews.questions</c> and its answers.</summary>
internal sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("questions", table =>
            table.HasCheckConstraint("ck_questions_status", ReviewCheckConstraints.PostStatuses));

        builder.HasKey(question => question.Id);
        builder.Property(question => question.Id).ValueGeneratedNever();

        builder.Property(question => question.Body).HasMaxLength(Question.MaxBodyLength).IsRequired();
        builder.Property(question => question.AuthorName).HasMaxLength(120);
        builder.Property(question => question.ModerationNote).HasMaxLength(Question.MaxNoteLength);

        builder.Property(question => question.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasMany(question => question.Answers)
            .WithOne()
            .HasForeignKey(answer => answer.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(question => question.Answers).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The product page's read: this product's approved questions, best answered first.
        builder.HasIndex(question => new { question.TenantId, question.ProductId, question.Status });

        // The moderation queue, oldest first.
        builder.HasIndex(question => new { question.TenantId, question.Status, question.CreatedAt });
    }
}

/// <summary>Maps <see cref="Answer"/> to <c>reviews.answers</c>.</summary>
internal sealed class AnswerConfiguration : IEntityTypeConfiguration<Answer>
{
    public void Configure(EntityTypeBuilder<Answer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("answers", table =>
        {
            table.HasCheckConstraint("ck_answers_status", ReviewCheckConstraints.PostStatuses);
            table.HasCheckConstraint("ck_answers_author", ReviewCheckConstraints.AnswerAuthors);
            table.HasCheckConstraint("ck_answers_vendor", ReviewCheckConstraints.AnswerVendor);
        });

        builder.HasKey(answer => answer.Id);
        builder.Property(answer => answer.Id).ValueGeneratedNever();

        builder.Property(answer => answer.Body).HasMaxLength(Answer.MaxBodyLength).IsRequired();
        builder.Property(answer => answer.AuthorName).HasMaxLength(120);

        builder.Property(answer => answer.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(answer => answer.AuthorType).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Reading a question reads its answers in order; there is no other access path to this table
        // except the moderation queue, which is the second index.
        builder.HasIndex(answer => new { answer.QuestionId, answer.CreatedAt });
        builder.HasIndex(answer => new { answer.TenantId, answer.Status, answer.CreatedAt });
    }
}

/// <summary>Maps <see cref="AbuseReport"/> to <c>reviews.abuse_reports</c>.</summary>
internal sealed class AbuseReportConfiguration : IEntityTypeConfiguration<AbuseReport>
{
    public void Configure(EntityTypeBuilder<AbuseReport> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("abuse_reports", table =>
        {
            table.HasCheckConstraint("ck_abuse_reports_target", ReviewCheckConstraints.ReportTargets);
            table.HasCheckConstraint("ck_abuse_reports_reason", ReviewCheckConstraints.ReportReasons);
            table.HasCheckConstraint("ck_abuse_reports_status", ReviewCheckConstraints.ReportStatuses);
        });

        builder.HasKey(report => report.Id);
        builder.Property(report => report.Id).ValueGeneratedNever();

        builder.Property(report => report.Note).HasMaxLength(AbuseReport.MaxNoteLength);
        builder.Property(report => report.Resolution).HasMaxLength(AbuseReport.MaxNoteLength);

        builder.Property(report => report.Target)
            .HasColumnName("target_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(report => report.Reason).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(report => report.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // One open complaint per person per thing. Filtered on the status and on the reporter being
        // known, because an anonymous report has nobody to be unique against — that case is held by
        // the rate limiter on the endpoint instead.
        builder.HasIndex(report => new { report.TenantId, report.Target, report.TargetId, report.ReporterId })
            .IsUnique()
            .HasDatabaseName("ux_abuse_reports_open")
            .HasFilter("status = 'Open' AND reporter_id IS NOT NULL");

        // The queue: everything open, and the count against one thing.
        builder.HasIndex(report => new { report.TenantId, report.Status, report.Reason, report.CreatedAt });
        builder.HasIndex(report => new { report.Target, report.TargetId, report.Status });
    }
}

/// <summary>Maps <see cref="Wishlist"/> to <c>reviews.wishlists</c> and its items.</summary>
internal sealed class WishlistConfiguration : IEntityTypeConfiguration<Wishlist>
{
    public void Configure(EntityTypeBuilder<Wishlist> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("wishlists", table =>
            table.HasCheckConstraint("ck_wishlists_item_count", ReviewCheckConstraints.ItemCount));

        builder.HasKey(wishlist => wishlist.Id);
        builder.Property(wishlist => wishlist.Id).ValueGeneratedNever();

        builder.Property(wishlist => wishlist.Name).HasMaxLength(Wishlist.MaxNameLength).IsRequired();
        builder.Property(wishlist => wishlist.ShareToken).HasMaxLength(64);

        builder.HasMany(wishlist => wishlist.Items)
            .WithOne()
            .HasForeignKey(item => item.WishlistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(wishlist => wishlist.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Exactly one default list per customer, enforced by the database rather than by the handler
        // that creates one. Two of them is a heart icon writing to whichever row the planner returned
        // first, and a check-then-write loses under a simultaneous save from two tabs.
        builder.HasIndex(wishlist => new { wishlist.TenantId, wishlist.CustomerId })
            .IsUnique()
            .HasDatabaseName("ux_wishlists_default")
            .HasFilter("is_default");

        builder.HasIndex(wishlist => new { wishlist.TenantId, wishlist.CustomerId, wishlist.Name });

        // The share link's lookup. Unique because a token that identified two lists would show one
        // person's saved items to somebody who was sent the other's.
        builder.HasIndex(wishlist => wishlist.ShareToken)
            .IsUnique()
            .HasDatabaseName("ux_wishlists_share_token")
            .HasFilter("share_token IS NOT NULL");
    }
}

/// <summary>Maps <see cref="WishlistItem"/> to <c>reviews.wishlist_items</c>.</summary>
internal sealed class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("wishlist_items", table =>
            table.HasCheckConstraint("ck_wishlist_items_priority", ReviewCheckConstraints.Priority));

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.Property(item => item.Note).HasMaxLength(WishlistItem.MaxNoteLength);

        // One row per variant per list. Tapping a heart twice removes; a retried request must not
        // produce a second row.
        builder.HasIndex(item => new { item.WishlistId, item.VariantId })
            .IsUnique()
            .HasDatabaseName("ux_wishlist_items_variant");

        // "Is this on any of my lists", asked by the product page.
        builder.HasIndex(item => new { item.TenantId, item.ProductId });
    }
}

/// <summary>Maps <see cref="StockSubscription"/> to <c>reviews.stock_subscriptions</c>.</summary>
internal sealed class StockSubscriptionConfiguration : IEntityTypeConfiguration<StockSubscription>
{
    public void Configure(EntityTypeBuilder<StockSubscription> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_subscriptions", table =>
        {
            table.HasCheckConstraint("ck_stock_subscriptions_kind", ReviewCheckConstraints.SubscriptionKinds);
            table.HasCheckConstraint("ck_stock_subscriptions_status", ReviewCheckConstraints.SubscriptionStatuses);
            table.HasCheckConstraint("ck_stock_subscriptions_contact", ReviewCheckConstraints.SubscriptionContact);
            table.HasCheckConstraint("ck_stock_subscriptions_target", ReviewCheckConstraints.SubscriptionTarget);
            table.HasCheckConstraint("ck_stock_subscriptions_price", ReviewCheckConstraints.TargetPricePositive);
        });

        builder.HasKey(subscription => subscription.Id);
        builder.Property(subscription => subscription.Id).ValueGeneratedNever();

        builder.Property(subscription => subscription.Email).HasMaxLength(320);

        builder.Property(subscription => subscription.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(subscription => subscription.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(subscription => subscription.TargetPrice).HasColumnType(ModelConventions.MoneyColumnType);
        builder.Property(subscription => subscription.PriceAtSubscription)
            .HasColumnType(ModelConventions.MoneyColumnType);

        // The event handlers' read, and the only one that runs on the hot path: a stock movement
        // arrives naming a listing, and this index answers "is anybody waiting for it" without
        // touching the rows of everybody who has already been told. Filtered on Active, which is
        // almost always a small fraction of the table.
        builder.HasIndex(subscription => new { subscription.TenantId, subscription.VariantId, subscription.Kind })
            .HasDatabaseName("ix_stock_subscriptions_waiting")
            .HasFilter("status = 'Active'");

        // A shopper's own list of what they are waiting for.
        builder.HasIndex(subscription => new { subscription.TenantId, subscription.CustomerId, subscription.Status });

        // The expiry sweep. Filtered, so it reads only rows that could possibly be due.
        builder.HasIndex(subscription => subscription.ExpiresAt)
            .HasDatabaseName("ix_stock_subscriptions_expiring")
            .HasFilter("status = 'Active'");
    }
}
