using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Payments.Application.Cod;
using KlaraHome.Modules.Payments.Application.Gateway;
using KlaraHome.Modules.Payments.Application.Payments;
using KlaraHome.Modules.Payments.Application.Refunds;
using KlaraHome.Modules.Payments.Application.Settlements;
using KlaraHome.Modules.Payments.Infrastructure.Processing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Payments.Endpoints;

/// <summary>The body of a manual capture.</summary>
/// <param name="Amount">How much to take, or null for everything outstanding.</param>
internal sealed record CaptureBody(decimal? Amount);

/// <summary>The body of a refund request.</summary>
/// <param name="Amount">What to send back.</param>
/// <param name="Reason">Why. Required, and it goes on the audit trail.</param>
/// <param name="SubOrderId">The seller's part it relates to, when it relates to one.</param>
/// <param name="Speed">How quickly: <c>normal</c> or <c>optimum</c>.</param>
internal sealed record RefundBody(decimal Amount, string Reason, Guid? SubOrderId, string? Speed);

/// <summary>The body of a refusal to approve.</summary>
/// <param name="Reason">Why the second signature was withheld.</param>
internal sealed record RejectRefundBody(string? Reason);

/// <summary>The body of a settlement import.</summary>
/// <param name="From">Start of the window, or null for the configured lookback.</param>
/// <param name="To">End of the window, or null for now.</param>
internal sealed record ImportSettlementsBody(DateTimeOffset? From, DateTimeOffset? To);

/// <summary>The body of a cash collection.</summary>
/// <param name="Amount">What was actually taken at the door.</param>
/// <param name="CollectedAt">When, or null for now.</param>
internal sealed record CollectCashBody(decimal Amount, DateTimeOffset? CollectedAt);

/// <summary>The body of a courier's remittance.</summary>
/// <param name="Ids">The cash records it covers.</param>
/// <param name="Reference">The courier's reference — a UTR or a batch number.</param>
/// <param name="Amount">What arrived in total, apportioned across the records.</param>
/// <param name="RemittedAt">When, or null for now.</param>
internal sealed record RemitCashBody(
    IReadOnlyList<Guid> Ids,
    string Reference,
    decimal? Amount,
    DateTimeOffset? RemittedAt);

/// <summary>
/// What platform staff can see and do about money (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// <para>
/// Four surfaces, four jobs. Support reads payments and their attempts. Finance raises refunds and
/// somebody else approves them. Whoever is diagnosing a disagreement between this platform and the
/// gateway works the webhook log and the settlement reports. Operations records cash from couriers.
/// Each is a separate permission, because in this module a single over-broad grant is the difference
/// between answering a support call and moving money out of the business.
/// </para>
/// <para>
/// There is no route that sets a payment's status, and that is the point of the design rather than an
/// omission. The only way a payment moves by hand is <c>sync</c>, which asks the gateway and applies
/// the answer through the same workflow a webhook uses.
/// </para>
/// <para>
/// No vendor surface. A payment has no seller — it is made against an order that may span two of
/// them — and what a seller may see about money is their settlement, which is Step 18's.
/// </para>
/// </remarks>
internal static class AdminPaymentEndpoints
{
    /// <summary>Maps the payment surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminPaymentEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        MapPayments(admin);
        MapRefunds(admin);
        MapGatewayEvents(admin);
        MapSettlements(admin);
        MapCash(admin);

        return admin;
    }

    private static void MapPayments(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/payments").WithTags("Payments");

        group.MapGet("/", async (
                string? status,
                string? method,
                string? provider,
                Guid? orderId,
                string? q,
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListPaymentsQuery(status, method, provider, orderId, q, from, to, cursor, size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListPayments")
            .WithSummary("Collections, newest first, filtered by status, method, provider or order.")
            .RequirePermission(PaymentsPermissions.PaymentRead)
            .Produces<PagedResult<PaymentSummaryResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetPaymentQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetPayment")
            .WithSummary("One collection in full, with every attempt and every refund.")
            .RequirePermission(PaymentsPermissions.PaymentRead)
            .Produces<PaymentResponse>();

        group.MapPost("/{id:guid}/sync", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SyncPaymentCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSyncPayment")
            .WithSummary("Re-reads the payment from the gateway and applies what it says.")
            .RequirePermission(PaymentsPermissions.PaymentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PaymentResponse>();

        group.MapPost("/{id:guid}/capture", async (
                Guid id,
                CaptureBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new CapturePaymentCommand(id, body?.Amount), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCapturePayment")
            .WithSummary("Takes money the gateway is holding on an authorised payment.")
            .RequirePermission(PaymentsPermissions.PaymentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PaymentResponse>();

        group.MapPost("/{id:guid}/refunds", async (
                Guid id,
                RefundBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                // Required, not optional. A refund is the one operation here where a duplicate is
                // money out of the door twice, so a request with no key is refused rather than
                // guessed at (docs/04-api-specification.md §1).
                var key = context.Request.Headers["Idempotency-Key"].ToString();

                var result = await dispatcher
                    .SendAsync(
                        new RaiseRefundCommand(id, body.Amount, body.Reason, body.SubOrderId, body.Speed, key),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRaiseRefund")
            .WithSummary("Raises a refund. Above the threshold it waits for a second signature.")
            .RequirePermission(PaymentsPermissions.RefundInitiate)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<RefundResponse>();

        group.MapPost("/reconcile", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RunReconciliationCommand(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRunReconciliation")
            .WithSummary("Runs the reconciliation sweep now instead of waiting for the timer.")
            .RequirePermission(PaymentsPermissions.GatewayManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ReconciliationSummary>();
    }

    private static void MapRefunds(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/refunds").WithTags("Payments");

        group.MapGet("/", async (
                string? status,
                Guid? orderId,
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListRefundsQuery(status, orderId, from, to, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListRefunds")
            .WithSummary("Refunds, newest first. Filter to Requested for the approvals queue.")
            .RequirePermission(PaymentsPermissions.PaymentRead)
            .Produces<PagedResult<RefundResponse>>();

        group.MapPost("/{id:guid}/approve", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ApproveRefundCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminApproveRefund")
            .WithSummary("The second signature. Refused if it is the same person who raised it.")
            .RequirePermission(PaymentsPermissions.RefundApprove)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<RefundResponse>();

        group.MapPost("/{id:guid}/reject", async (
                Guid id,
                RejectRefundBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RejectRefundCommand(id, body?.Reason), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRejectRefund")
            .WithSummary("Withholds the second signature. Nothing is sent to the gateway.")
            .RequirePermission(PaymentsPermissions.RefundApprove)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<RefundResponse>();

        group.MapPost("/{id:guid}/sync", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SyncRefundCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSyncRefund")
            .WithSummary("Re-reads the refund from the gateway and applies what it says.")
            .RequirePermission(PaymentsPermissions.PaymentManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<RefundResponse>();
    }

    private static void MapGatewayEvents(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/gateway-events").WithTags("Payments");

        group.MapGet("/", async (
                string? status,
                string? type,
                Guid? paymentId,
                DateTimeOffset? from,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListGatewayEventsQuery(status, type, paymentId, from, cursor, size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListGatewayEvents")
            .WithSummary("The webhook log and the dead-letter queue, one list. Filter status=DeadLettered.")
            .RequirePermission(PaymentsPermissions.GatewayManage)
            .Produces<PagedResult<GatewayEventSummaryResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetGatewayEventQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetGatewayEvent")
            .WithSummary("One stored webhook, with the body exactly as it arrived.")
            .RequirePermission(PaymentsPermissions.GatewayManage)
            .Produces<GatewayEventResponse>();

        group.MapPost("/{id:guid}/replay", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ReplayGatewayEventCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminReplayGatewayEvent")
            .WithSummary("Re-queues a failed or dead-lettered event. It is not re-verified.")
            .RequirePermission(PaymentsPermissions.GatewayManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<GatewayEventSummaryResponse>();
    }

    private static void MapSettlements(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/settlements").WithTags("Payments");

        group.MapGet("/", async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListSettlementsQuery(from, to, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListSettlements")
            .WithSummary("Imported settlement reports, newest first, with their reconciliation counts.")
            .RequirePermission(PaymentsPermissions.PaymentRead)
            .Produces<PagedResult<SettlementResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetSettlementQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetSettlement")
            .WithSummary("One settlement report.")
            .RequirePermission(PaymentsPermissions.PaymentRead)
            .Produces<SettlementResponse>();

        group.MapGet("/{id:guid}/entries", async (
                Guid id,
                string? matchStatus,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListSettlementEntriesQuery(id, matchStatus, cursor, size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListSettlementEntries")
            .WithSummary("A report's lines. Filter matchStatus=Mismatched for what needs acting on.")
            .RequirePermission(PaymentsPermissions.PaymentRead)
            .Produces<PagedResult<SettlementEntryResponse>>();

        group.MapPost("/import", async (
                ImportSettlementsBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ImportSettlementsCommand(body?.From, body?.To), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminImportSettlements")
            .WithSummary("Pulls settlement reports for a window and matches their lines.")
            .RequirePermission(PaymentsPermissions.GatewayManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<SettlementIngestionSummary>();
    }

    private static void MapCash(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/cod-collections").WithTags("Payments");

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
                        new ListCodCollectionsQuery(status, vendorId, orderId, from, to, cursor, size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListCodCollections")
            .WithSummary("Cash owed and collected at doors. Filter status=Collected for what is owed to us.")
            .RequirePermission(PaymentsPermissions.CodManage)
            .Produces<PagedResult<CodCollectionResponse>>();

        group.MapPost("/{id:guid}/collect", async (
                Guid id,
                CollectCashBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new RecordCodCollectionCommand(id, body.Amount, body.CollectedAt),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRecordCodCollection")
            .WithSummary("Records that the courier took the cash at the door.")
            .RequirePermission(PaymentsPermissions.CodManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CodCollectionResponse>();

        group.MapPost("/remit", async (
                RemitCashBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new RecordCodRemittanceCommand(body.Ids, body.Reference, body.Amount, body.RemittedAt),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRecordCodRemittance")
            .WithSummary("Records a courier's remittance against a batch of collections, in one go.")
            .RequirePermission(PaymentsPermissions.CodManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<IReadOnlyList<CodCollectionResponse>>();
    }
}
