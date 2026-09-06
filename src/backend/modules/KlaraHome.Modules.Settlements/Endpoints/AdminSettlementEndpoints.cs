using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Settlements.Application;
using KlaraHome.Modules.Settlements.Application.Cycles;
using KlaraHome.Modules.Settlements.Application.Ledger;
using KlaraHome.Modules.Settlements.Application.Payouts;
using KlaraHome.Modules.Settlements.Application.Reports;
using KlaraHome.Modules.Settlements.Infrastructure.Reporting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Settlements.Endpoints;

/// <summary>The body of a manual period close.</summary>
/// <param name="VendorId">The seller, when closing a period no cycle has been opened for.</param>
/// <param name="Force">
/// Whether to close before the return hold has expired. Defaults to false, because the hold is what
/// stops a seller being paid for goods a shopper may still send back.
/// </param>
internal sealed record CloseCycleBody(Guid? VendorId, bool Force = false);

/// <summary>The body of a ledger adjustment.</summary>
/// <param name="VendorId">Whose account moves.</param>
/// <param name="Direction">Which way: <c>credit</c> or <c>debit</c>.</param>
/// <param name="Amount">How much, positive.</param>
/// <param name="Reason">Why. It appears on the seller's own statement.</param>
internal sealed record AdjustmentBody(Guid VendorId, string? Direction, decimal Amount, string? Reason);

/// <summary>The body of a payout batch.</summary>
/// <param name="CycleIds">Which periods to pay. Omit for every closed, unbatched, payable one.</param>
internal sealed record CreatePayoutBatchBody(IReadOnlyList<Guid>? CycleIds);

/// <summary>The body of a batch cancellation.</summary>
/// <param name="Reason">Why it is being abandoned.</param>
internal sealed record CancelPayoutBatchBody(string? Reason);

/// <summary>
/// The settlements back office (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// <para>
/// Four permissions across these routes, and the split is the split between four jobs. Reading is
/// what a seller does about their own account and what support does when a seller asks. Closing a
/// period and adjusting a ledger are the corrections finance makes when a statement is wrong.
/// Building a payout run and approving one are deliberately two permissions held by two people —
/// that separation is the maker–checker control, and no amount of care in a handler substitutes for
/// it.
/// </para>
/// <para>
/// There is no storefront surface. A shopper has no interest in what a seller was paid and no
/// business knowing it.
/// </para>
/// <para>
/// Vendor callers reach the read routes through the same paths as staff, scoped by their token and
/// never by an id in the query string — the arrangement docs/04-api-specification.md §2 requires. A
/// vendor id in a request from a seller is ignored rather than refused, because their own account is
/// what they meant.
/// </para>
/// </remarks>
internal static class AdminSettlementEndpoints
{
    /// <summary>Maps the settlements surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminSettlementEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        MapCycles(admin);
        MapLedger(admin);
        MapPayouts(admin);
        MapReports(admin);

        return admin;
    }

    /// <summary>The settlement periods, and closing one.</summary>
    private static void MapCycles(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/settlements/cycles").WithTags("Settlements");

        group.MapGet("/", async (
                Guid? vendorId,
                string? status,
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListSettlementCyclesQuery(vendorId, status, from, to, cursor, size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListSettlementCycles")
            .WithSummary("The settlement periods, newest first.")
            .RequirePermission(SettlementsPermissions.SettlementRead)
            .Produces<PagedResult<SettlementCycleResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetSettlementCycleQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetSettlementCycle")
            .WithSummary("One settlement period, with everything that was taken out of it.")
            .RequirePermission(SettlementsPermissions.SettlementRead)
            .Produces<SettlementCycleResponse>();

        group.MapPost("/{id:guid}/close", async (
                Guid id,
                CloseCycleBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new CloseSettlementCycleCommand(id, body?.VendorId, body?.Force ?? false),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCloseSettlementCycle")
            .WithSummary("Totals a settlement period and fixes it, applying TCS and TDS.")
            .RequirePermission(SettlementsPermissions.SettlementManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<SettlementCycleResponse>();

        group.MapPost("/close", async (
                CloseCycleBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new CloseSettlementCycleCommand(CycleId: null, body.VendorId, body.Force),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCloseVendorSettlementPeriod")
            .WithSummary("Closes the period that is currently due for one seller, opening it if need be.")
            .RequirePermission(SettlementsPermissions.SettlementManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<SettlementCycleResponse>();
    }

    /// <summary>The ledger itself: movements, statements, balances and corrections.</summary>
    private static void MapLedger(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/settlements").WithTags("Settlements");

        group.MapGet("/ledger", async (
                Guid? vendorId,
                string? entryType,
                Guid? cycleId,
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(
                        new ListLedgerEntriesQuery(vendorId, entryType, cycleId, from, to, cursor, size),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListLedgerEntries")
            .WithSummary("The movements on sellers' accounts, newest first.")
            .RequirePermission(SettlementsPermissions.SettlementRead)
            .Produces<PagedResult<LedgerEntryResponse>>();

        group.MapPost("/adjustments", async (
                AdjustmentBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new PostAdjustmentCommand(body.VendorId, body.Direction, body.Amount, body.Reason),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPostSettlementAdjustment")
            .WithSummary("Writes a correction into a seller's account. An append, never an edit.")
            .RequirePermission(SettlementsPermissions.SettlementManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<LedgerEntryResponse>();

        var vendorGroup = admin.MapGroup("/vendors/{vendorId:guid}").WithTags("Settlements");

        vendorGroup.MapGet("/ledger", async (
                Guid vendorId,
                DateTimeOffset? from,
                DateTimeOffset? to,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetVendorStatementQuery(vendorId, from, to), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetVendorStatement")
            .WithSummary("One seller's statement: opening balance, movements, closing balance.")
            .RequirePermission(SettlementsPermissions.SettlementRead)
            .Produces<LedgerStatementResponse>();

        vendorGroup.MapGet("/ledger/export", async (
                Guid vendorId,
                DateTimeOffset? from,
                DateTimeOffset? to,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ExportVendorStatementQuery(vendorId, from, to), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.Match(
                    file => Results.File(file, Csv.ContentType, $"statement-{vendorId:N}.csv"),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminExportVendorStatement")
            .WithSummary("The same statement as a spreadsheet.")
            .RequirePermission(SettlementsPermissions.SettlementRead);

        vendorGroup.MapGet("/balance", async (Guid vendorId, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetVendorBalanceQuery(vendorId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetVendorBalance")
            .WithSummary("What a seller is owed right now, and how much of it is waiting for a payout.")
            .RequirePermission(SettlementsPermissions.SettlementRead)
            .Produces<VendorBalanceResponse>();
    }

    /// <summary>The payout runs.</summary>
    private static void MapPayouts(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/payout-batches").WithTags("Settlements");

        group.MapGet("/", async (
                string? status,
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListPayoutBatchesQuery(status, from, to, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListPayoutBatches")
            .WithSummary("The payout runs, newest first.")
            .RequirePermission(SettlementsPermissions.SettlementRead)
            .Produces<PagedResult<PayoutBatchResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetPayoutBatchQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetPayoutBatch")
            .WithSummary("One payout run, its transfers, and what may be done to it next.")
            .RequirePermission(SettlementsPermissions.SettlementRead)
            .Produces<PayoutBatchResponse>();

        group.MapPost("/", async (
                CreatePayoutBatchBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new CreatePayoutBatchCommand(body?.CycleIds), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCreatePayoutBatch")
            .WithSummary("Builds a draft payout run from closed settlement periods. Sends nothing.")
            .RequirePermission(SettlementsPermissions.PayoutManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PayoutBatchResponse>();

        group.MapPost("/{id:guid}/approve", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ApprovePayoutBatchCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminApprovePayoutBatch")
            .WithSummary("Signs a payout run off. Never by the person who raised it.")
            .RequirePermission(SettlementsPermissions.PayoutApprove)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PayoutBatchResponse>();

        group.MapPost("/{id:guid}/process", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ProcessPayoutBatchCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminProcessPayoutBatch")
            .WithSummary("Hands an approved run to the gateway. Resumable: call it again for a large batch.")
            .RequirePermission(SettlementsPermissions.PayoutApprove)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PayoutBatchResponse>();

        group.MapPost("/{id:guid}/cancel", async (
                Guid id,
                CancelPayoutBatchBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new CancelPayoutBatchCommand(id, body?.Reason), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCancelPayoutBatch")
            .WithSummary("Abandons a run nothing has left. Its periods become payable again.")
            .RequirePermission(SettlementsPermissions.PayoutManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PayoutBatchResponse>();
    }

    /// <summary>The statutory extract and the platform's own revenue.</summary>
    private static void MapReports(IEndpointRouteBuilder admin)
    {
        var group = admin.MapGroup("/reports").WithTags("Settlements");

        group.MapGet("/tcs-tds", async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                Guid? vendorId,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStatutoryExtractQuery(from, to, vendorId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetStatutoryExtract")
            .WithSummary("Tax collected and deducted at source, one line per seller per period.")
            .RequirePermission(SettlementsPermissions.SettlementRead)
            .Produces<StatutoryExtractResponse>();

        group.MapGet("/tcs-tds/export", async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                Guid? vendorId,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ExportStatutoryExtractQuery(from, to, vendorId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.Match(
                    file => Results.File(file, Csv.ContentType, "tcs-tds.csv"),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminExportStatutoryExtract")
            .WithSummary("The same extract as a spreadsheet, for the GST and income-tax filings.")
            .RequirePermission(SettlementsPermissions.SettlementRead);

        group.MapGet("/platform-revenue", async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetPlatformRevenueQuery(from, to), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetPlatformRevenue")
            .WithSummary("What the platform earned, which is exactly what the sellers were charged.")
            .RequirePermission(SettlementsPermissions.SettlementManage)
            .Produces<PlatformRevenueResponse>();
    }
}
