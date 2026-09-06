using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Returns.Application;
using KlaraHome.Modules.Returns.Application.CreditNotes;
using KlaraHome.Modules.Returns.Application.Returns;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Returns.Endpoints;

/// <summary>The body of a return request.</summary>
/// <param name="SubOrderId">The seller's part the goods came from.</param>
/// <param name="Type">Money back, or a replacement. Defaults to money back.</param>
/// <param name="ReasonCode">Why, from the list this store offers.</param>
/// <param name="ReasonNote">Why, in the shopper's own words.</param>
/// <param name="Lines">The units.</param>
/// <param name="EvidenceFileIds">The photographs, uploaded through the media library first.</param>
/// <param name="RefundMode">Where the money should go, when the shopper has a preference.</param>
internal sealed record RaiseReturnBody(
    Guid SubOrderId,
    string? Type,
    string ReasonCode,
    string? ReasonNote,
    IReadOnlyList<ReturnLineRequest>? Lines,
    IReadOnlyList<Guid>? EvidenceFileIds,
    string? RefundMode);

/// <summary>The body of a withdrawal.</summary>
/// <param name="Reason">Why, in the shopper's own words.</param>
internal sealed record CancelReturnBody(string? Reason);

/// <summary>
/// What a shopper can see and do about their own returns (docs/04-api-specification.md §3.4).
/// </summary>
/// <remarks>
/// <para>
/// Every route requires an account and every handler resolves a return by <em>(return, customer)</em>.
/// An id belonging to somebody else does not resolve, and the answer is the same 404 a made-up id
/// gets.
/// </para>
/// <para>
/// There is one write here and it is the request itself. A shopper may ask, look, and withdraw what
/// has not yet been collected; approving, grading and refunding are all somebody else's, and no
/// support question is worth an endpoint that lets a customer decide what they are owed.
/// </para>
/// <para>
/// The eligibility route hangs off the sub-order rather than off a return, because it is the
/// question asked <em>before</em> a return exists: "what can I send back, and until when".
/// </para>
/// </remarks>
internal static class StoreReturnEndpoints
{
    /// <summary>Maps the returns surface beneath <c>/store/returns</c>.</summary>
    /// <param name="store">The <c>/store</c> group.</param>
    public static IEndpointRouteBuilder MapStoreReturnEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var group = store
            .MapGroup("/returns")
            .WithTags("Returns")
            .RequireAuthorization();

        group.MapGet("/reasons", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListReturnReasonOptionsQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeListReturnReasons")
            .WithSummary("The reasons this store accepts for a return.")
            .Produces<IReadOnlyList<ReturnReasonOption>>();

        group.MapGet("/", async (
                string? status,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListMyReturnsQuery(status, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeListReturns")
            .WithSummary("The caller's own returns, newest first.")
            .Produces<PagedResult<ReturnSummaryResponse>>();

        group.MapPost("/", async (RaiseReturnBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new RaiseReturnCommand(
                            body.SubOrderId,
                            body.Type,
                            body.ReasonCode,
                            body.ReasonNote,
                            body.Lines ?? [],
                            body.EvidenceFileIds,
                            body.RefundMode),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeRaiseReturn")
            .WithSummary("Asks to send items back from one seller's part of an order.")
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<ReturnResponse>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetMyReturnQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetReturn")
            .WithSummary("One of the caller's own returns, in full.")
            .Produces<ReturnResponse>();

        group.MapPost("/{id:guid}/cancel", async (
                Guid id,
                CancelReturnBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new CancelMyReturnCommand(id, body?.Reason), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeCancelReturn")
            .WithSummary("Withdraws a return whose goods have not yet been collected.")
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<ReturnResponse>();

        group.MapGet("/{id:guid}/credit-note", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetReturnCreditNoteQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetReturnCreditNote")
            .WithSummary("The credit note raised against one of the caller's own returns.")
            .Produces<CreditNoteResponse>();

        MapEligibility(store);

        return store;
    }

    /// <summary>
    /// Maps the question a shopper asks before a return exists.
    /// </summary>
    /// <remarks>
    /// It hangs off the sub-order because that is what it is about. Putting it under
    /// <c>/store/returns</c> would make it look like a property of a return, and the shopper asking
    /// it does not have one yet.
    /// </remarks>
    private static void MapEligibility(IEndpointRouteBuilder store)
    {
        store.MapGet("/sub-orders/{id:guid}/returnable", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetReturnEligibilityQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithTags("Returns")
            .WithName("storeReturnEligibility")
            .WithSummary("What can still be sent back from one seller's part, and until when.")
            .RequireAuthorization()
            .Produces<ReturnEligibilityResponse>();
    }
}
