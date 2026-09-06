using KlaraHome.Contracts.Inventory;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Reviews.Application.Wishlists;
using KlaraHome.Modules.Reviews.Endpoints;
using KlaraHome.Modules.Reviews.Infrastructure;
using KlaraHome.Modules.Reviews.Infrastructure.Events;
using KlaraHome.Modules.Reviews.Infrastructure.Features;
using KlaraHome.Modules.Reviews.Infrastructure.Jobs;
using KlaraHome.Modules.Reviews.Infrastructure.Persistence;
using KlaraHome.Modules.Reviews.Infrastructure.Rating;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Reviews;

/// <summary>
/// What other people thought, what people are still asking, and what somebody wants but has not
/// bought yet.
/// </summary>
/// <remarks>
/// <para>
/// Four surfaces that look unrelated and are not. A rating, a question, a saved item and a price
/// somebody would buy at are all the same kind of fact: a shopper telling the store something it
/// could not have worked out on its own. All four are anchored to the catalogue and none of them may
/// read it, so every one goes through <c>IProductProjectionSource</c> at write time to confirm what
/// it names and at read time to price it.
/// </para>
/// <para>
/// The rule that shapes the module is that a review requires a <em>delivered</em> purchase. Not an
/// order and not a payment: delivery, resolved through <c>IOrderPurchases</c> — a seam added here and
/// implemented by Orders, because the module that owns the state machine deciding what delivered
/// means is the module that should answer for it. Every identifier on a review comes off what that
/// contract returned rather than off the request, and the one-review-per-line rule is a unique index
/// rather than a check, because two submissions racing each other is the ordinary case on a slow
/// connection.
/// </para>
/// <para>
/// It owns its reviews, questions, wishlists and alerts outright. What it does not own is the
/// aggregate that appears beside a product in search results and on the product page: that is
/// computed here and <em>published</em>, because Catalog holds the column a projection reads and
/// Vendors holds the seller's. Two events carry it, both idempotent to apply — a consumer stores two
/// numbers it was handed rather than adding a delta it might apply twice — and between them they
/// finally fill the <c>RatingAverage</c> that has been null since Step 19.
/// </para>
/// <para>
/// Moderation is the default and not an afterthought. Reviews, questions and answers all arrive
/// pending unless a deployment has decided otherwise, and whether they do is a configured decision
/// rather than a store setting: in India an intermediary that publishes user content unreviewed has a
/// different position under the IT Rules than one that moderates
/// (docs/07-security-compliance.md §5), and that is not a switch a merchandiser should be able to
/// flip from an admin screen.
/// </para>
/// </remarks>
public sealed class ReviewsModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "reviews";

    /// <inheritdoc />
    public string Name => "Reviews";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// Third of Phase E, after Search and Content. It consumes from Catalog, Inventory, Pricing and
    /// Orders and is consumed by Catalog, Vendors and Search — so it registers after everything it
    /// reads, and the order is a statement of that.
    /// </summary>
    public int Order => 160;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<ReviewsDbContext>(configuration, this);
        services.AddValidatedOptions<ReviewsOptions>(configuration, ReviewsOptions.SectionName);

        // Who is asking, and whether they may take somebody's words down.
        services.AddScoped<ReviewScope>();

        // The single place a rating is recomputed and announced. Five callers can move an average;
        // one implementation computes it.
        services.AddScoped<RatingProjector>();

        // Finding a customer's list and resolving its cards against today's catalogue, shared by
        // every wishlist handler so none of them can invent a second way to make a default list.
        services.AddScoped<WishlistReader>();

        AddEventHandlers(services);

        // Declared in code and seeded into platform.feature_flags, exactly as every module's are.
        services.AddSingleton<IFeatureFlagSource, ReviewFeatureFlagSource>();

        // Off in the API and on in the worker, exactly as every sweeper before it.
        services.AddHostedService<SubscriptionSweepWorker>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var store = endpoints.MapGroup("/store");
        store.MapStoreReviewEndpoints();

        var admin = endpoints.MapGroup("/admin");
        admin.MapAdminReviewEndpoints();
    }

    /// <summary>
    /// Subscribes to the two facts that can satisfy an alert somebody is waiting for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stock coming back and a price coming down, and there is deliberately nothing else. A shopper
    /// waiting for something is waiting for one of exactly those two events, and a module that
    /// subscribed to more would be doing work on behalf of a feature it does not have.
    /// </para>
    /// <para>
    /// <c>StockLevelChanged</c> is the highest-volume event on the platform and this handler runs for
    /// every one of them, which is why the first thing it does is the cheap test: the event carries
    /// both the old and the new availability, so a movement that did not cross the boundary is
    /// discarded before a query is issued at all.
    /// </para>
    /// </remarks>
    private static void AddEventHandlers(IServiceCollection services)
    {
        services.AddScoped<StockAlertHandlers>();

        services.AddScoped<IIntegrationEventHandler<StockLevelChanged>>(
            provider => provider.GetRequiredService<StockAlertHandlers>());

        services.AddScoped<IIntegrationEventHandler<PriceChanged>>(
            provider => provider.GetRequiredService<StockAlertHandlers>());
    }
}
