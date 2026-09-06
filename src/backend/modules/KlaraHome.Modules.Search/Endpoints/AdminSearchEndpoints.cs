using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Search.Application;
using KlaraHome.Modules.Search.Application.Indexing;
using KlaraHome.Modules.Search.Application.Insights;
using KlaraHome.Modules.Search.Application.Vocabulary;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Search.Endpoints;

/// <summary>The body of a new synonym rule.</summary>
/// <param name="Term">The word a shopper types.</param>
/// <param name="Expansions">What it is also taken to mean.</param>
/// <param name="IsBidirectional">Whether the expansions expand back to the term.</param>
/// <param name="Note">Why the rule exists.</param>
internal sealed record CreateSynonymBody(
    string? Term,
    IReadOnlyList<string>? Expansions,
    bool IsBidirectional,
    string? Note);

/// <summary>The body of a synonym edit. The term itself is not editable.</summary>
/// <param name="Expansions">The new expansions.</param>
/// <param name="IsBidirectional">Whether they expand back.</param>
/// <param name="IsActive">Whether the rule is applied.</param>
/// <param name="Note">Why the rule exists.</param>
internal sealed record UpdateSynonymBody(
    IReadOnlyList<string>? Expansions,
    bool IsBidirectional,
    bool IsActive,
    string? Note);

/// <summary>The body of a new stop word.</summary>
/// <param name="Word">The word this store ignores.</param>
internal sealed record CreateStopWordBody(string? Word);

/// <summary>The body of a stop-word switch.</summary>
/// <param name="IsActive">Whether it is applied.</param>
internal sealed record SetStopWordActiveBody(bool IsActive);

/// <summary>The body of a rebuild.</summary>
/// <param name="AfterVariantId">Resume after this variant, or null to start at the beginning.</param>
/// <param name="MaxVariants">The most variants this run walks.</param>
/// <param name="VariantIds">Rebuild only these, ignoring the walk entirely.</param>
internal sealed record RebuildIndexBody(
    Guid? AfterVariantId,
    int? MaxVariants,
    IReadOnlyList<Guid>? VariantIds);

/// <summary>
/// The search back office (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// <para>
/// Three permissions across these routes and they follow the three jobs: reading what shoppers
/// searched for is a buying decision, editing the vocabulary is a merchandising one, and rebuilding
/// the index is an operational one with a real cost.
/// </para>
/// <para>
/// There is no endpoint that edits an index row. Every column in the projection belongs to another
/// module, and an operator who could correct one by hand would be correcting a symptom while the
/// source stayed wrong — and their correction would be silently overwritten by the next event. The
/// rebuild is the supported repair, and it repairs by re-reading the truth.
/// </para>
/// </remarks>
internal static class AdminSearchEndpoints
{
    /// <summary>Maps the search back office beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminSearchEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        MapSynonyms(admin);
        MapStopWords(admin);
        MapInsights(admin);
        MapIndex(admin);

        return admin;
    }

    /// <summary>The store's synonym rules.</summary>
    private static void MapSynonyms(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/search/synonyms").WithTags("Search");

        group.MapGet("/", async (
                string? search,
                bool? activeOnly,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListSynonymsQuery(search, activeOnly, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListSynonyms")
            .WithSummary("The store's synonym rules, alphabetically.")
            .RequirePermission(SearchPermissions.VocabularyManage)
            .Produces<PagedResult<SynonymResponse>>();

        group.MapPost("/", async (CreateSynonymBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateSynonymCommand(
                    body.Term,
                    body.Expansions,
                    body.IsBidirectional,
                    body.Note);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCreateSynonym")
            .WithSummary("Adds a synonym rule. Single words only.")
            .RequirePermission(SearchPermissions.VocabularyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<SynonymResponse>();

        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateSynonymBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateSynonymCommand(
                    id,
                    body.Expansions,
                    body.IsBidirectional,
                    body.IsActive,
                    body.Note);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUpdateSynonym")
            .WithSummary("Changes what a word is taken to mean, or switches the rule off.")
            .RequirePermission(SearchPermissions.VocabularyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<SynonymResponse>();

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteSynonymCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminDeleteSynonym")
            .WithSummary("Removes a synonym rule.")
            .RequirePermission(SearchPermissions.VocabularyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);
    }

    /// <summary>The words this store ignores in a query.</summary>
    private static void MapStopWords(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/search/stop-words").WithTags("Search");

        group.MapGet("/", async (
                bool? activeOnly,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListStopWordsQuery(activeOnly, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListStopWords")
            .WithSummary("The words this store ignores in a query.")
            .RequirePermission(SearchPermissions.VocabularyManage)
            .Produces<PagedResult<StopWordResponse>>();

        group.MapPost("/", async (CreateStopWordBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new CreateStopWordCommand(body.Word), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCreateStopWord")
            .WithSummary("Adds a word to the stop list.")
            .RequirePermission(SearchPermissions.VocabularyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<StopWordResponse>();

        group.MapPut("/{id:guid}", async (
                Guid id,
                SetStopWordActiveBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SetStopWordActiveCommand(id, body.IsActive), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSetStopWordActive")
            .WithSummary("Switches a stop word on or off without losing it.")
            .RequirePermission(SearchPermissions.VocabularyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<StopWordResponse>();

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteStopWordCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminDeleteStopWord")
            .WithSummary("Removes a word from the stop list.")
            .RequirePermission(SearchPermissions.VocabularyManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);
    }

    /// <summary>What shoppers searched for, and what they did not find.</summary>
    private static void MapInsights(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/search/queries").WithTags("Search");

        group.MapGet("/", async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? source,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new SearchQueryReportQuery(from, to, ZeroResultsOnly: false, source, size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSearchQueryReport")
            .WithSummary("What shoppers searched for, most-asked first, with click-through.")
            .RequirePermission(SearchPermissions.QueryRead)
            .Produces<IReadOnlyList<SearchQueryReportRow>>();

        group.MapGet("/zero-results", async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? source,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new SearchQueryReportQuery(from, to, ZeroResultsOnly: true, source, size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSearchZeroResults")
            .WithSummary("What shoppers searched for and did not find. The buying team's list.")
            .RequirePermission(SearchPermissions.QueryRead)
            .Produces<IReadOnlyList<SearchQueryReportRow>>();
    }

    /// <summary>The index itself.</summary>
    private static void MapIndex(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/search/index").WithTags("Search");

        group.MapGet("/", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetIndexStatusQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSearchIndexStatus")
            .WithSummary("How many rows are indexed, how many have fallen behind, and which engine answers.")
            .RequirePermission(SearchPermissions.IndexManage)
            .Produces<SearchIndexStatus>();

        group.MapPost("/rebuild", async (
                RebuildIndexBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                // Named variants take precedence over a walk. An operator who supplies both means the
                // surgical one — nobody sends a list of five product ids and expects a full rebuild.
                if (body?.VariantIds is { Count: > 0 })
                {
                    var surgical = await dispatcher
                        .SendAsync(new ReindexVariantsCommand(body.VariantIds), context.RequestAborted)
                        .ConfigureAwait(false);

                    return surgical.ToOk(context);
                }

                var command = new RebuildIndexCommand(body?.AfterVariantId, body?.MaxVariants);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRebuildSearchIndex")
            .WithSummary("Rebuilds index rows from the catalogue. Bounded and resumable.")
            .RequirePermission(SearchPermissions.IndexManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ReindexResponse>();
    }
}
