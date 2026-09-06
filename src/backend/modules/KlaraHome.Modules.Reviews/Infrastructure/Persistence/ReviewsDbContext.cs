using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Reviews.Domain;
using KlaraHome.Modules.Reviews.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Reviews.Infrastructure.Persistence;

/// <summary>
/// The Reviews module's data access.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is vendor-scoped, and that is a decision rather than an omission. A review names the
/// seller who sold the thing, and a seller may reply to their own — but the review belongs to the
/// shopper who wrote it and to the store that publishes it, not to the seller it is about. A vendor
/// query filter would have made the moderation queue invisible to the people who moderate it, and it
/// would have given a seller a way to see which of their reviews were pending. Confinement to one
/// seller's reviews is therefore done in the handler that needs it, explicitly, and only for the two
/// operations that have it: listing them and replying.
/// </para>
/// <para>
/// Everything in the schema is owned outright and none of it is soft-deleted. A refused review is a
/// row in <see cref="ReviewStatus.Rejected"/>, not a deleted one, so a shopper can be told why and a
/// moderator can be overruled. A wishlist item a shopper removes really is gone, because nobody has
/// ever wanted to audit what somebody stopped wanting.
/// </para>
/// <para>
/// The one thing it does not own is products. A review, a question, a saved item and a stock alert
/// all name a variant or a product and hold no other fact about it: the name, the price, the picture
/// and the buy box are resolved through the catalogue's contracts at read time, so a wishlist shows
/// today's price and a review page links to the seller the product page would open on.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller, read by the vendor query filter.</param>
internal sealed class ReviewsDbContext(
    DbContextOptions<ReviewsDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => ReviewsModule.SchemaName;

    /// <summary>What shoppers thought of things they were sent.</summary>
    public DbSet<Review> Reviews => Set<Review>();

    /// <summary>Who found which review useful. One row per voter, never a counter.</summary>
    public DbSet<ReviewVote> ReviewVotes => Set<ReviewVote>();

    /// <summary>What a product's reviews add up to, kept rather than recomputed on read.</summary>
    public DbSet<ProductRatingSummary> ProductRatings => Set<ProductRatingSummary>();

    /// <summary>The same over one seller's sales.</summary>
    public DbSet<VendorRatingSummary> VendorRatings => Set<VendorRatingSummary>();

    /// <summary>What shoppers are still asking.</summary>
    public DbSet<Question> Questions => Set<Question>();

    /// <summary>What they were told. Reached through a question, never listed on its own.</summary>
    public DbSet<Answer> Answers => Set<Answer>();

    /// <summary>Complaints about any of the above.</summary>
    public DbSet<AbuseReport> AbuseReports => Set<AbuseReport>();

    /// <summary>Saved lists.</summary>
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();

    /// <summary>What is on them.</summary>
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();

    /// <summary>Standing requests to be told when something comes back or comes down.</summary>
    public DbSet<StockSubscription> StockSubscriptions => Set<StockSubscription>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new ReviewConfiguration());
        modelBuilder.ApplyConfiguration(new ReviewVoteConfiguration());
        modelBuilder.ApplyConfiguration(new ProductRatingSummaryConfiguration());
        modelBuilder.ApplyConfiguration(new VendorRatingSummaryConfiguration());
        modelBuilder.ApplyConfiguration(new QuestionConfiguration());
        modelBuilder.ApplyConfiguration(new AnswerConfiguration());
        modelBuilder.ApplyConfiguration(new AbuseReportConfiguration());
        modelBuilder.ApplyConfiguration(new WishlistConfiguration());
        modelBuilder.ApplyConfiguration(new WishlistItemConfiguration());
        modelBuilder.ApplyConfiguration(new StockSubscriptionConfiguration());
    }
}
