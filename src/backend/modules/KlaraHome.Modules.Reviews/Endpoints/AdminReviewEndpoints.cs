using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Reviews.Application;
using KlaraHome.Modules.Reviews.Application.Moderation;
using KlaraHome.Modules.Reviews.Application.Questions;
using KlaraHome.Modules.Reviews.Application.Reports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Reviews.Endpoints;

/// <summary>The body of a moderation decision.</summary>
/// <param name="Approve">Whether to publish it.</param>
/// <param name="Note">Why. Required for a refusal.</param>
internal sealed record ModerationBody(bool Approve, string? Note);

/// <summary>The body of a seller's reply.</summary>
/// <param name="Reply">What to say, or null to withdraw the reply.</param>
internal sealed record ReplyBody(string? Reply);

/// <summary>The body of a complaint's resolution.</summary>
/// <param name="Uphold">Whether the complaint is accepted, which takes the content down.</param>
/// <param name="Resolution">What the moderator concluded.</param>
internal sealed record ResolveReportBody(bool Uphold, string? Resolution);

/// <summary>
/// The moderation queue and the seller's reply surface (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// <para>
/// Three permissions across it, and the split is between three jobs. Reading is support's, and it
/// deliberately includes what is pending and what was refused — "where has my review gone" is not
/// answerable from the storefront's view of the world. Moderating is the queue and is the only
/// permission here that can take something down. Replying is a seller's, and it is separate from
/// moderating on purpose: a seller may answer a one-star review of their own sale, and must never be
/// able to make it disappear.
/// </para>
/// <para>
/// The listing endpoints serve a seller and a moderator through the same route. A caller whose token
/// carries a vendor id is confined to their own sales in the handler; a caller with none sees
/// everything. Two endpoints would have drifted, and the confinement being one clause in one place
/// is what makes it reviewable.
/// </para>
/// </remarks>
internal static class AdminReviewEndpoints
{
    /// <summary>Maps the moderation surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminReviewEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        MapReviews(admin);
        MapQuestions(admin);
        MapReports(admin);

        return admin;
    }

    /// <summary>The review queue.</summary>
    private static void MapReviews(IEndpointRouteBuilder admin)
    {
        var group = admin
            .MapGroup("/reviews")
            .WithTags("Reviews")
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);

        group.MapGet(string.Empty, async (
                string? status,
                Guid? productId,
                Guid? vendorId,
                int? rating,
                bool? reported,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListReviewsForModerationQuery(
                            status,
                            productId,
                            vendorId,
                            rating,
                            reported,
                            cursor,
                            size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListReviews")
            .WithSummary("The review queue. Pending first, oldest first; a seller sees only their own.")
            .RequirePermission(ReviewPermissions.ReviewRead)
            .Produces<PagedResult<ModeratedReviewResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetReviewQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetReview")
            .WithSummary("One review in full, with its images and its complaint count.")
            .RequirePermission(ReviewPermissions.ReviewRead)
            .Produces<ModeratedReviewResponse>();

        group.MapPost("/{id:guid}/moderate", async (
                Guid id,
                ModerationBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ModerateReviewCommand(id, body.Approve, body.Note), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminModerateReview")
            .WithSummary("Approves or refuses a review, republishing the rating if the decision moved it.")
            .RequirePermission(ReviewPermissions.ReviewModerate)
            .Produces<ModeratedReviewResponse>();

        group.MapPost("/{id:guid}/reply", async (
                Guid id,
                ReplyBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ReplyToReviewCommand(id, body.Reply), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminReplyToReview")
            .WithSummary("Writes a seller's public reply to a review of their own sale.")
            .RequirePermission(ReviewPermissions.ReviewReply)
            .Produces<ModeratedReviewResponse>();
    }

    /// <summary>The Q&amp;A queue.</summary>
    private static void MapQuestions(IEndpointRouteBuilder admin)
    {
        var group = admin
            .MapGroup("/questions")
            .WithTags("Reviews")
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);

        group.MapGet(string.Empty, async (
                string? status,
                Guid? productId,
                bool? unanswered,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListQuestionsForModerationQuery(status, productId, unanswered, cursor, size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListQuestions")
            .WithSummary("The question queue, every answer under each shown whatever its state.")
            .RequirePermission(ReviewPermissions.ReviewRead)
            .Produces<PagedResult<ModeratedQuestionResponse>>();

        group.MapPost("/{id:guid}/moderate", async (
                Guid id,
                ModerationBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ModerateQuestionCommand(id, body.Approve, body.Note), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminModerateQuestion")
            .WithSummary("Approves or refuses a question. The answers under it keep their own states.")
            .RequirePermission(ReviewPermissions.ReviewModerate)
            .Produces<ModeratedQuestionResponse>();

        group.MapPost("/{id:guid}/answers/{answerId:guid}/moderate", async (
                Guid id,
                Guid answerId,
                ModerationBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ModerateAnswerCommand(id, answerId, body.Approve), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminModerateAnswer")
            .WithSummary("Approves or refuses one answer.")
            .RequirePermission(ReviewPermissions.ReviewModerate)
            .Produces<ModeratedQuestionResponse>();

        // Answering from the admin surface goes through the same command the storefront uses, so the
        // label an answer carries is decided in exactly one place — from the caller's own claims.
        group.MapPost("/{id:guid}/answers", async (
                Guid id,
                AnswerBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new AnswerQuestionCommand(id, body.Body), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminAnswerQuestion")
            .WithSummary("Answers a question as the seller or as the store.")
            .RequirePermission(ReviewPermissions.ReviewReply)
            .Produces<QuestionResponse>();
    }

    /// <summary>The complaints queue.</summary>
    private static void MapReports(IEndpointRouteBuilder admin)
    {
        var group = admin
            .MapGroup("/content-reports")
            .WithTags("Reviews")
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);

        group.MapGet(string.Empty, async (
                string? status,
                string? reason,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListAbuseReportsQuery(status, reason, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListContentReports")
            .WithSummary("The complaints queue, unlawful content first and then oldest first.")
            .RequirePermission(ReviewPermissions.ReviewRead)
            .Produces<PagedResult<AbuseReportResponse>>();

        group.MapPost("/{id:guid}/resolve", async (
                Guid id,
                ResolveReportBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new ResolveAbuseReportCommand(id, body.Uphold, body.Resolution),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminResolveContentReport")
            .WithSummary("Closes a complaint, taking the content down if it is upheld.")
            .RequirePermission(ReviewPermissions.ReviewModerate)
            .Produces<AbuseReportResponse>();
    }
}
