using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Returns.Application;
using KlaraHome.Modules.Returns.Application.CreditNotes;
using KlaraHome.Modules.Returns.Application.Reasons;
using KlaraHome.Modules.Returns.Application.Returns;
using KlaraHome.Modules.Returns.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Returns.Endpoints;

/// <summary>The body of an approval.</summary>
/// <param name="Amount">What to agree to, or null for everything the shopper was quoted.</param>
/// <param name="PickupRequired">Whether a courier collects, or null for what the reason says.</param>
/// <param name="Note">What the shopper should be told.</param>
internal sealed record ApproveReturnBody(decimal? Amount, bool? PickupRequired, string? Note);

/// <summary>The body of a refusal.</summary>
/// <param name="Reason">Why, in words the shopper is shown.</param>
internal sealed record RejectReturnBody(string? Reason);

/// <summary>The body of a collection booking.</summary>
/// <param name="PickupAt">When the courier should call, or null for the earliest they will.</param>
/// <param name="ManualAwb">A waybill an operator obtained from a courier themselves.</param>
/// <param name="ManualCourier">Who is carrying it, for a hand-booking.</param>
internal sealed record SchedulePickupBody(
    DateTimeOffset? PickupAt,
    string? ManualAwb,
    string? ManualCourier);

/// <summary>The body of a receipt.</summary>
/// <param name="Note">Anything the receiving bay wants recorded.</param>
internal sealed record ReceiveReturnBody(string? Note);

/// <summary>The body of an inspection.</summary>
/// <param name="Result">Whether the goods passed: <c>pass</c> or <c>fail</c>.</param>
/// <param name="Disposition">What becomes of them, or null for the store's default.</param>
/// <param name="Notes">What the inspector wrote.</param>
/// <param name="Lines">What they concluded per line, or empty to accept everything.</param>
internal sealed record QcBody(
    string Result,
    ReturnDisposition? Disposition,
    string? Notes,
    IReadOnlyList<QcLineRequest>? Lines);

/// <summary>The body of a payout.</summary>
/// <param name="Mode">Where the money goes: <c>original</c> or <c>wallet</c>.</param>
/// <param name="Amount">What to send back, or null for everything it is worth.</param>
internal sealed record RefundReturnBody(string? Mode, decimal? Amount);

/// <summary>The body of a replacement.</summary>
/// <param name="ReplacementOrderId">The order the replacement went out on.</param>
/// <param name="Note">What the shopper should be told.</param>
internal sealed record ReplaceReturnBody(Guid? ReplacementOrderId, string? Note);

/// <summary>The body of a close.</summary>
/// <param name="Note">Why it is being closed.</param>
internal sealed record CloseReturnBody(string? Note);

/// <summary>The body of a reason code.</summary>
/// <param name="Code">Its stable code. Ignored on an update — a code is never changed.</param>
/// <param name="Label">What the shopper reads.</param>
/// <param name="Description">The help beneath it.</param>
/// <param name="SortOrder">Where it sits on the dropdown.</param>
/// <param name="IsActive">Whether it is offered. Ignored on a create, which is always active.</param>
/// <param name="RequiresEvidence">Whether a photograph is needed.</param>
/// <param name="IsPickupRequired">Whether a courier collects.</param>
/// <param name="RequiresQc">Whether the goods are inspected.</param>
/// <param name="IsAutoApproved">Whether it is approved without a human.</param>
/// <param name="ShippingPayer">Who pays the reverse freight.</param>
/// <param name="IsVendorFault">Whether the seller bears the cost at settlement.</param>
/// <param name="AllowsReplacement">Whether a replacement may be asked for.</param>
internal sealed record ReturnReasonBody(
    string? Code,
    string Label,
    string? Description,
    int SortOrder,
    bool IsActive = true,
    bool RequiresEvidence = false,
    bool IsPickupRequired = true,
    bool RequiresQc = true,
    bool IsAutoApproved = false,
    string? ShippingPayer = null,
    bool IsVendorFault = false,
    bool AllowsReplacement = true);

/// <summary>
/// The returns back office (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// <para>
/// Four permissions across these routes, and the split is the split between four jobs. Reading is
/// support. The queue — approve, refuse, book a collection, close — is the returns desk, and a
/// seller holds it for their own goods. Grading is the receiving bay's alone, because a seller may
/// not decide the condition of goods they are about to be charged for. Paying is finance's, and it
/// sits on top of the Payments module's own maker–checker threshold rather than replacing it.
/// </para>
/// <para>
/// The reason codes are staff-only. What is auto-approved and who pays the freight are commercial
/// decisions, and a seller who could edit them would be deciding that their own category of fault is
/// free to return.
/// </para>
/// </remarks>
internal static class AdminReturnEndpoints
{
    /// <summary>Maps the returns surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminReturnEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        MapQueue(admin);
        MapWorkflow(admin);
        MapReasons(admin);
        MapCreditNotes(admin);

        return admin;
    }

    /// <summary>The queue itself: what is outstanding, and one return in full.</summary>
    private static void MapQueue(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/returns").WithTags("Returns");

        group.MapGet("/", async (
                string? status,
                Guid? vendorId,
                Guid? orderId,
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListReturnsQuery(status, vendorId, orderId, from, to, cursor, size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListReturns")
            .WithSummary("The returns queue, newest first.")
            .RequirePermission(ReturnsPermissions.ReturnRead)
            .Produces<PagedResult<ReturnSummaryResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetReturnQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetReturn")
            .WithSummary("One return in full, with its evidence and what may be done to it next.")
            .RequirePermission(ReturnsPermissions.ReturnRead)
            .Produces<ReturnResponse>();

        group.MapGet("/{id:guid}/credit-note", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetReturnCreditNoteQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetReturnCreditNote")
            .WithSummary("The credit note raised against one return.")
            .RequirePermission(ReturnsPermissions.ReturnRead)
            .Produces<CreditNoteResponse>();
    }

    /// <summary>Everything that moves a return.</summary>
    private static void MapWorkflow(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/returns").WithTags("Returns");

        group.MapPost("/{id:guid}/approve", async (
                Guid id,
                ApproveReturnBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new ApproveReturnCommand(id, body?.Amount, body?.PickupRequired, body?.Note),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminApproveReturn")
            .WithSummary("Agrees to a return, and says whether the goods have to come back.")
            .RequirePermission(ReturnsPermissions.ReturnManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ReturnResponse>();

        group.MapPost("/{id:guid}/reject", async (
                Guid id,
                RejectReturnBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RejectReturnCommand(id, body?.Reason), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRejectReturn")
            .WithSummary("Refuses a return, with a reason the shopper is shown.")
            .RequirePermission(ReturnsPermissions.ReturnManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ReturnResponse>();

        group.MapPost("/{id:guid}/schedule-pickup", async (
                Guid id,
                SchedulePickupBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new SchedulePickupCommand(
                            id,
                            body?.PickupAt,
                            body?.ManualAwb,
                            body?.ManualCourier),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminScheduleReturnPickup")
            .WithSummary("Books a courier to collect an approved return.")
            .RequirePermission(ReturnsPermissions.ReturnManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ReturnResponse>();

        group.MapPost("/{id:guid}/receive", async (
                Guid id,
                ReceiveReturnBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ReceiveReturnCommand(id, body?.Note), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminReceiveReturn")
            .WithSummary("Books a returned parcel in at the warehouse.")
            .RequirePermission(ReturnsPermissions.ReturnQc)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ReturnResponse>();

        group.MapPost("/{id:guid}/qc", async (Guid id, QcBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var passed = string.Equals(body.Result, "pass", StringComparison.OrdinalIgnoreCase);

                var result = await dispatcher
                    .SendAsync(
                        new InspectReturnCommand(id, passed, body.Disposition, body.Notes, body.Lines),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminInspectReturn")
            .WithSummary("Records what quality control decided, and what becomes of the goods.")
            .RequirePermission(ReturnsPermissions.ReturnQc)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ReturnResponse>();

        group.MapPost("/{id:guid}/refund", async (
                Guid id,
                RefundReturnBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RefundReturnCommand(id, body?.Mode, body?.Amount), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRefundReturn")
            .WithSummary("Pays a return out and raises its credit note.")
            .RequirePermission(ReturnsPermissions.ReturnRefund)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ReturnResponse>();

        group.MapPost("/{id:guid}/replace", async (
                Guid id,
                ReplaceReturnBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new ReplaceReturnCommand(id, body?.ReplacementOrderId, body?.Note),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminReplaceReturn")
            .WithSummary("Records that a replacement was dispatched instead of a refund.")
            .RequirePermission(ReturnsPermissions.ReturnManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ReturnResponse>();

        group.MapPost("/{id:guid}/close", async (
                Guid id,
                CloseReturnBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new CloseReturnCommand(id, body?.Note), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCloseReturn")
            .WithSummary("Closes a return with nothing owed.")
            .RequirePermission(ReturnsPermissions.ReturnManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ReturnResponse>();
    }

    /// <summary>The reason codes and the policy attached to each.</summary>
    private static void MapReasons(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/return-reasons").WithTags("Returns");

        group.MapGet("/", async (bool? includeInactive, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListReturnReasonsQuery(includeInactive ?? false), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListReturnReasons")
            .WithSummary("The reason codes and the policy each carries.")
            .RequirePermission(ReturnsPermissions.ReasonManage)
            .Produces<IReadOnlyList<ReturnReasonResponse>>();

        group.MapPost("/", async (ReturnReasonBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new CreateReturnReasonCommand(
                            body.Code ?? string.Empty,
                            body.Label,
                            body.Description,
                            body.SortOrder,
                            body.RequiresEvidence,
                            body.IsPickupRequired,
                            body.RequiresQc,
                            body.IsAutoApproved,
                            body.ShippingPayer,
                            body.IsVendorFault,
                            body.AllowsReplacement),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCreateReturnReason")
            .WithSummary("Opens a reason a shopper may give.")
            .RequirePermission(ReturnsPermissions.ReasonManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ReturnReasonResponse>();

        group.MapPut("/{id:guid}", async (
                Guid id,
                ReturnReasonBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new UpdateReturnReasonCommand(
                            id,
                            body.Label,
                            body.Description,
                            body.SortOrder,
                            body.IsActive,
                            body.RequiresEvidence,
                            body.IsPickupRequired,
                            body.RequiresQc,
                            body.IsAutoApproved,
                            body.ShippingPayer,
                            body.IsVendorFault,
                            body.AllowsReplacement),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUpdateReturnReason")
            .WithSummary("Changes a reason's wording or the policy it carries. The code never changes.")
            .RequirePermission(ReturnsPermissions.ReasonManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ReturnReasonResponse>();
    }

    /// <summary>The credit notes, which is what a GST return is prepared from.</summary>
    private static void MapCreditNotes(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/credit-notes").WithTags("Returns");

        group.MapGet("/", async (
                Guid? vendorId,
                string? financialYear,
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListCreditNotesQuery(vendorId, financialYear, from, to, cursor, size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListCreditNotes")
            .WithSummary("The credit notes raised, newest first.")
            .RequirePermission(ReturnsPermissions.ReturnRead)
            .Produces<PagedResult<CreditNoteResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetCreditNoteQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetCreditNote")
            .WithSummary("One credit note, with its tax split.")
            .RequirePermission(ReturnsPermissions.ReturnRead)
            .Produces<CreditNoteResponse>();
    }
}
