using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Caching;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Features;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Reviews.Application;
using KlaraHome.Modules.Reviews.Application.Questions;
using KlaraHome.Modules.Reviews.Application.Reports;
using KlaraHome.Modules.Reviews.Application.Reviews;
using KlaraHome.Modules.Reviews.Application.Subscriptions;
using KlaraHome.Modules.Reviews.Application.Wishlists;
using KlaraHome.Modules.Reviews.Infrastructure.Features;
using KlaraHome.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Reviews.Endpoints;

/// <summary>The body of a new or edited review.</summary>
/// <param name="OrderLineId">The delivered line being reviewed. Ignored on an edit.</param>
/// <param name="Rating">One to five.</param>
/// <param name="Title">The headline.</param>
/// <param name="Body">What they thought.</param>
/// <param name="Images">Already-uploaded pictures to attach.</param>
internal sealed record ReviewBody(
    Guid OrderLineId,
    int Rating,
    string? Title,
    string? Body,
    IReadOnlyList<ReviewImageInput>? Images);

/// <summary>The body of a helpfulness vote.</summary>
/// <param name="IsHelpful">Whether it helped.</param>
internal sealed record VoteBody(bool IsHelpful);

/// <summary>The body of a question.</summary>
/// <param name="Body">What they want to know.</param>
internal sealed record QuestionBody(string? Body);

/// <summary>The body of an answer.</summary>
/// <param name="Body">The answer.</param>
internal sealed record AnswerBody(string? Body);

/// <summary>The body of a save.</summary>
/// <param name="VariantId">The sellable thing being saved.</param>
/// <param name="WishlistId">Which list, or null for the default.</param>
/// <param name="Note">What the shopper wrote against it.</param>
/// <param name="Priority">Where it sits.</param>
internal sealed record SaveItemBody(Guid VariantId, Guid? WishlistId, string? Note, int Priority);

/// <summary>The body of a new or renamed list.</summary>
/// <param name="Name">What to call it.</param>
internal sealed record WishlistBody(string? Name);

/// <summary>The body of a sharing switch.</summary>
/// <param name="Share">Whether it should be shareable.</param>
internal sealed record ShareBody(bool Share);

/// <summary>The body of a stock alert.</summary>
/// <param name="VariantId">The sellable thing to watch.</param>
/// <param name="Kind">BackInStock or PriceDrop.</param>
/// <param name="TargetPrice">The price to wait for.</param>
/// <param name="Email">Where to write, for a shopper who is not signed in.</param>
internal sealed record SubscriptionBody(Guid VariantId, string? Kind, decimal? TargetPrice, string? Email);

/// <summary>The body of a complaint.</summary>
/// <param name="Target">Review, Question or Answer.</param>
/// <param name="TargetId">Which one.</param>
/// <param name="Reason">Why.</param>
/// <param name="Note">Anything the reporter wants to add.</param>
internal sealed record ReportBody(string? Target, Guid TargetId, string? Reason, string? Note);

/// <summary>
/// What a shopper's browser reads and writes (docs/04-api-specification.md §3.6).
/// </summary>
/// <remarks>
/// <para>
/// Split three ways by who may call it, and the split is worth reading. The reads — a product's
/// reviews, its rating, its questions — are anonymous and cacheable at the edge, because none of
/// them varies by caller. The writes all require an account, because every one of them attributes
/// text or an intention to a person. And two endpoints are anonymous <em>writes</em>: reporting
/// content, because a store that made itself hard to tell about unlawful content would be a worse
/// store, and subscribing to a stock alert, because somebody who has not signed up yet is exactly
/// the person a back-in-stock alert is for.
/// </para>
/// <para>
/// Both of those anonymous writes are rate limited harder than anything else here, and neither
/// publishes anything: a report goes into a queue a moderator works, and a subscription sends one
/// message to an address the caller supplied. Nothing an anonymous caller submits reaches another
/// shopper's screen.
/// </para>
/// <para>
/// There is no way to read an unmoderated review, question or answer from this surface, and no query
/// parameter that changes that. An author reading their own pending review does it through
/// <c>/store/me/reviews</c> with their own token.
/// </para>
/// </remarks>
internal static class StoreReviewEndpoints
{
    /// <summary>Maps the storefront engagement surface beneath <c>/store</c>.</summary>
    /// <param name="store">The <c>/store</c> group.</param>
    public static IEndpointRouteBuilder MapStoreReviewEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        MapReviews(store);
        MapQuestions(store);
        MapWishlist(store);
        MapSubscriptions(store);
        MapReports(store);

        return store;
    }

    /// <summary>Reading and writing reviews.</summary>
    private static void MapReviews(IEndpointRouteBuilder store)
    {
        var group = store.MapGroup("/products/{productId:guid}").WithTags("Reviews");

        group.MapGet("/reviews", async (
                Guid productId,
                int? rating,
                bool? withImages,
                string? sort,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListProductReviewsQuery(productId, rating, withImages, sort, cursor, size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeListProductReviews")
            .WithSummary("A product's published reviews.")
            .AllowAnonymous()
            .RequireFeature(ReviewFeatures.Reviews)
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead)
            .CachePublicRead()
            .Produces<PagedResult<ReviewResponse>>();

        group.MapGet("/rating", async (Guid productId, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetRatingSummaryQuery(productId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetProductRating")
            .WithSummary("A product's average score and its histogram.")
            .AllowAnonymous()
            .RequireFeature(ReviewFeatures.Reviews)
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead)
            .CachePublicRead()
            .Produces<RatingSummaryResponse>();

        group.MapGet("/reviews/eligibility", async (
                Guid productId,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .QueryAsync(new GetReviewEligibilityQuery(productId, customerId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetReviewEligibility")
            .WithSummary("Whether the caller may review this product, and against which purchase.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Reviews)
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead)
            .Produces<ReviewEligibilityResponse>();

        group.MapPost("/reviews", async (
                Guid productId,
                ReviewBody body,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                // The product in the route is not passed on. It is the page the shopper is on, and
                // the product the review is filed against comes from the purchase they quote — which
                // is the only version of it a caller cannot choose.
                var result = await dispatcher
                    .SendAsync(
                        new WriteReviewCommand(
                            body.OrderLineId,
                            body.Rating,
                            body.Title,
                            body.Body,
                            body.Images,
                            customerId),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeWriteReview")
            .WithSummary("Writes a review against a delivered purchase.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Reviews)
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<ReviewResponse>();

        var reviews = store.MapGroup("/reviews").WithTags("Reviews");

        reviews.MapPut("/{id:guid}", async (
                Guid id,
                ReviewBody body,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .SendAsync(
                        new ReviseReviewCommand(id, body.Rating, body.Title, body.Body, body.Images, customerId),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeReviseReview")
            .WithSummary("Rewrites a review the caller wrote. It returns to moderation.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Reviews)
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<ReviewResponse>();

        reviews.MapPost("/{id:guid}/helpful", async (
                Guid id,
                VoteBody body,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .SendAsync(new VoteOnReviewCommand(id, body.IsHelpful, customerId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("storeVoteOnReview")
            .WithSummary("Records whether the caller found a review useful.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Reviews)
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces(StatusCodes.Status204NoContent);

        reviews.MapDelete("/{id:guid}/helpful", async (
                Guid id,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .SendAsync(new WithdrawVoteCommand(id, customerId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("storeWithdrawReviewVote")
            .WithSummary("Withdraws a helpfulness vote.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Reviews)
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces(StatusCodes.Status204NoContent);

        store.MapGet("/me/reviews", async (
                string? cursor,
                int? size,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .QueryAsync(new ListMyReviewsQuery(customerId, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeListMyReviews")
            .WithTags("Reviews")
            .WithSummary("What the caller has written, pending and refused included.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Reviews)
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead)
            .Produces<PagedResult<ModeratedReviewResponse>>();
    }

    /// <summary>Reading and writing product questions.</summary>
    private static void MapQuestions(IEndpointRouteBuilder store)
    {
        var group = store.MapGroup("/products/{productId:guid}").WithTags("Reviews");

        group.MapGet("/questions", async (
                Guid productId,
                bool? unanswered,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListProductQuestionsQuery(productId, unanswered, cursor, size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeListProductQuestions")
            .WithSummary("A product's published questions, best answered first.")
            .AllowAnonymous()
            .RequireFeature(ReviewFeatures.Questions)
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead)
            .CachePublicRead()
            .Produces<PagedResult<QuestionResponse>>();

        group.MapPost("/questions", async (
                Guid productId,
                QuestionBody body,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .SendAsync(
                        new AskQuestionCommand(productId, body.Body, customerId),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeAskQuestion")
            .WithSummary("Asks a question about a product.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Questions)
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<QuestionResponse>();

        store.MapPost("/questions/{id:guid}/answers", async (
                Guid id,
                AnswerBody body,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is null)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .SendAsync(new AnswerQuestionCommand(id, body.Body), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeAnswerQuestion")
            .WithTags("Reviews")
            .WithSummary("Answers a question. How the answer is labelled comes from the caller's token.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Questions)
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<QuestionResponse>();
    }

    /// <summary>The wishlist.</summary>
    private static void MapWishlist(IEndpointRouteBuilder store)
    {
        var group = store.MapGroup("/wishlist").WithTags("Reviews");

        group.MapGet(string.Empty, async (
                Guid? listId,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .QueryAsync(new GetWishlistQuery(customerId, listId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetWishlist")
            .WithSummary("The caller's list, every card priced against today's catalogue.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Wishlist)
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead)
            .Produces<WishlistResponse>();

        group.MapGet("/lists", async (ICallerContext caller, IDispatcher dispatcher, HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .QueryAsync(new ListWishlistsQuery(customerId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeListWishlists")
            .WithSummary("The caller's lists, without their contents.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Wishlist)
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead)
            .Produces<IReadOnlyList<WishlistResponse>>();

        group.MapPost("/items", async (
                SaveItemBody body,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .SendAsync(
                        new AddToWishlistCommand(
                            customerId,
                            body.VariantId,
                            body.WishlistId,
                            body.Note,
                            body.Priority),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeAddToWishlist")
            .WithSummary("Saves something for later.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Wishlist)
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<WishlistResponse>();

        group.MapDelete("/items/{variantId:guid}", async (
                Guid variantId,
                Guid? listId,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .SendAsync(new RemoveFromWishlistCommand(customerId, variantId, listId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("storeRemoveFromWishlist")
            .WithSummary("Takes something off a list.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Wishlist)
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/lists", async (
                WishlistBody body,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .SendAsync(new CreateWishlistCommand(customerId, body.Name), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeCreateWishlist")
            .WithSummary("Opens a new named list.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Wishlist)
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<WishlistResponse>();

        group.MapPut("/lists/{id:guid}", async (
                Guid id,
                WishlistBody body,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .SendAsync(new RenameWishlistCommand(customerId, id, body.Name), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeRenameWishlist")
            .WithSummary("Renames a list.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Wishlist)
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<WishlistResponse>();

        group.MapDelete("/lists/{id:guid}", async (
                Guid id,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .SendAsync(new DeleteWishlistCommand(customerId, id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("storeDeleteWishlist")
            .WithSummary("Deletes a list. The default one cannot be deleted.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.Wishlist)
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/lists/{id:guid}/share", async (
                Guid id,
                ShareBody body,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .SendAsync(new ShareWishlistCommand(customerId, id, body.Share), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeShareWishlist")
            .WithSummary("Turns sharing on or off. Turning it on mints a new link and revokes the old one.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.WishlistSharing)
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<WishlistResponse>();

        group.MapGet("/shared/{token}", async (string token, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetSharedWishlistQuery(token), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetSharedWishlist")
            .WithSummary("A shared list, by its link. No notes and no token in the response.")
            .AllowAnonymous()
            .RequireFeature(ReviewFeatures.WishlistSharing)
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead)
            .Produces<SharedWishlistResponse>();
    }

    /// <summary>Back-in-stock and price-drop alerts.</summary>
    private static void MapSubscriptions(IEndpointRouteBuilder store)
    {
        var group = store.MapGroup("/stock-subscriptions").WithTags("Reviews");

        group.MapPost(string.Empty, async (
                SubscriptionBody body,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                // Anonymous on purpose: somebody who has not signed up is exactly the person a
                // back-in-stock alert is for, and demanding an account first is how a store never
                // hears from them again. They give an address instead, and it is used for one message.
                var result = await dispatcher
                    .SendAsync(
                        new SubscribeToStockCommand(
                            body.VariantId,
                            body.Kind,
                            body.TargetPrice,
                            caller.UserId,
                            body.Email),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeSubscribeToStock")
            .WithSummary("Asks to be told when something comes back or comes down.")
            .AllowAnonymous()
            .RequireFeature(ReviewFeatures.StockAlerts)
            .RequireRateLimiting(RateLimitPolicies.Otp)
            .Produces<StockSubscriptionResponse>();

        group.MapGet(string.Empty, async (
                bool? activeOnly,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .QueryAsync(new ListMySubscriptionsQuery(customerId, activeOnly), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeListMySubscriptions")
            .WithSummary("What the caller is waiting to hear about.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.StockAlerts)
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead)
            .Produces<IReadOnlyList<StockSubscriptionResponse>>();

        group.MapDelete("/{id:guid}", async (
                Guid id,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (caller.UserId is not { } customerId)
                {
                    return ReviewErrors.SignInRequired.ToProblemResult(context);
                }

                var result = await dispatcher
                    .SendAsync(new CancelSubscriptionCommand(id, customerId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("storeCancelSubscription")
            .WithSummary("Withdraws a standing alert.")
            .RequireAuthorization()
            .RequireFeature(ReviewFeatures.StockAlerts)
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces(StatusCodes.Status204NoContent);
    }

    /// <summary>Reporting something somebody wrote.</summary>
    /// <remarks>
    /// Anonymous, and deliberately not gated by any feature flag. A store that had switched reviews
    /// off would still need to be told about something unlawful already published under them, and in
    /// India acting on such a report runs to a statutory timeline
    /// (docs/07-security-compliance.md §5) — a flag that could silence the reporting channel would be
    /// the wrong kind of switch to have.
    /// </remarks>
    private static void MapReports(IEndpointRouteBuilder store)
        => store.MapPost("/content-reports", async (
                ReportBody body,
                ICallerContext caller,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new ReportContentCommand(body.Target, body.TargetId, body.Reason, body.Note, caller.UserId),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeReportContent")
            .WithTags("Reviews")
            .WithSummary("Reports a review, question or answer to the moderation queue.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Otp)
            .Produces<AbuseReportResponse>();
}
