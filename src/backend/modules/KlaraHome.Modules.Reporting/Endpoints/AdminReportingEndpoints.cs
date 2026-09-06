using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Features;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Reporting.Application;
using KlaraHome.Modules.Reporting.Application.Reports;
using KlaraHome.Modules.Reporting.Application.Schedules;
using KlaraHome.Modules.Reporting.Infrastructure.Export;
using KlaraHome.Modules.Reporting.Infrastructure.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Reporting.Endpoints;

/// <summary>The body of a new or edited schedule.</summary>
/// <param name="ReportKey">Which report. Ignored on an edit — a schedule's report is not editable.</param>
/// <param name="Name">What to call it.</param>
/// <param name="Frequency">Daily, Weekly or Monthly.</param>
/// <param name="HourUtc">The hour it runs at, in UTC.</param>
/// <param name="DayOfWeek">Which weekday, for a weekly schedule. Monday is 1.</param>
/// <param name="DayOfMonth">Which day, for a monthly one.</param>
/// <param name="Recipients">Where to send it. Empty to only file it.</param>
/// <param name="IsActive">Whether it runs. Ignored on a create; a new schedule is active.</param>
internal sealed record ScheduleBody(
    string? ReportKey,
    string? Name,
    string? Frequency,
    int HourUtc,
    int? DayOfWeek,
    int? DayOfMonth,
    IReadOnlyList<string>? Recipients,
    bool IsActive);

/// <summary>
/// The reporting surface (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// <para>
/// There is no storefront surface at all, and there never will be. Every number here is commercial —
/// what the store took, what a seller was paid, how many baskets were left — and none of it is
/// anybody's business but the people running the platform and the sellers reading their own figures.
/// </para>
/// <para>
/// Every route is vendor-scoped by the caller's token rather than by an id in the query string, which
/// is the arrangement the settlements surface uses at Step 18 and for the same reason: a seller
/// reading their own figures and a manager reading everybody's run the same query, and the only
/// difference is whether the caller carries a vendor id. The reports that are about the platform
/// rather than about a seller are declared as such and a seller is refused them outright.
/// </para>
/// <para>
/// <c>GET /admin/reports/{key}</c> serves the table, and it takes a <c>format</c> parameter as the
/// API specification says — <c>json</c> returns the table, and <c>csv</c> produces a run and answers
/// with a short-lived link rather than streaming the file. That indirection is deliberate: a report
/// somebody asked for and one that arrives by email every Monday are then the same artefact,
/// produced by the same code and recorded in the same log.
/// </para>
/// </remarks>
internal static class AdminReportingEndpoints
{
    /// <summary>Maps the reporting surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminReportingEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        MapReports(admin);
        MapRuns(admin);
        MapSchedules(admin);

        return admin;
    }

    /// <summary>The catalogue and the reports themselves.</summary>
    private static void MapReports(IEndpointRouteBuilder admin)
    {
        var group = admin
            .MapGroup("/reports")
            .WithTags("Reporting")
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);

        group.MapGet(string.Empty, async (ICallerContext caller, IDispatcher dispatcher, HttpContext context) =>
            {
                // A seller is offered only the reports they may actually run, so the picker cannot
                // show them something that then answers 403.
                var result = await dispatcher
                    .QueryAsync(new ListReportsQuery(caller.VendorId is not null), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListReports")
            .WithSummary("Every report this platform produces, with its columns and its groupings.")
            .RequirePermission(ReportingPermissions.ReportRead)
            .RequireFeature(ReportingFeatures.Reports)
            .Produces<IReadOnlyList<ReportDefinition>>();

        group.MapGet("/{reportKey}", async (
                string reportKey,
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? groupBy,
                Guid? vendorId,
                string? format,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
                {
                    var exported = await dispatcher
                        .SendAsync(
                            new ExportReportCommand(reportKey, from, to, groupBy, vendorId),
                            context.RequestAborted)
                        .ConfigureAwait(false);

                    return exported.ToOk(context);
                }

                var result = await dispatcher
                    .QueryAsync(
                        new RunReportQuery(reportKey, from, to, groupBy, vendorId),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminRunReport")
            .WithSummary("Runs a report. format=csv produces a file and answers with the run instead.")
            .RequirePermission(ReportingPermissions.ReportRead)
            .RequireFeature(ReportingFeatures.Reports)
            .Produces<ReportResult>()
            .Produces<ReportRunResponse>();
    }

    /// <summary>What has been produced, and how to fetch it.</summary>
    private static void MapRuns(IEndpointRouteBuilder admin)
    {
        var group = admin
            .MapGroup("/report-runs")
            .WithTags("Reporting")
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);

        group.MapGet(string.Empty, async (
                string? reportKey,
                string? status,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListReportRunsQuery(reportKey, status, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListReportRuns")
            .WithSummary("Every report produced, newest first, failures included.")
            .RequirePermission(ReportingPermissions.ReportRead)
            .RequireFeature(ReportingFeatures.Reports)
            .Produces<PagedResult<ReportRunResponse>>();

        group.MapGet("/{id:guid}/download", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetReportDownloadQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminDownloadReportRun")
            .WithSummary("A short-lived signed link to a produced report.")
            .RequirePermission(ReportingPermissions.ReportRead)
            .RequireFeature(ReportingFeatures.Reports)
            .Produces<ReportDownloadResponse>();
    }

    /// <summary>The standing instructions.</summary>
    private static void MapSchedules(IEndpointRouteBuilder admin)
    {
        var group = admin
            .MapGroup("/report-schedules")
            .WithTags("Reporting")
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);

        group.MapGet(string.Empty, async (bool? activeOnly, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListSchedulesQuery(activeOnly), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListReportSchedules")
            .WithSummary("The standing instructions, soonest due first.")
            .RequirePermission(ReportingPermissions.ScheduleManage)
            .Produces<IReadOnlyList<ReportScheduleResponse>>();

        group.MapPost(string.Empty, async (ScheduleBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new CreateScheduleCommand(
                            body.ReportKey,
                            body.Name,
                            body.Frequency,
                            body.HourUtc,
                            body.DayOfWeek,
                            body.DayOfMonth,
                            body.Recipients),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCreateReportSchedule")
            .WithSummary("Opens a standing instruction to produce a report on a timetable.")
            .RequirePermission(ReportingPermissions.ScheduleManage)
            .Produces<ReportScheduleResponse>();

        group.MapPut("/{id:guid}", async (
                Guid id,
                ScheduleBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(
                        new UpdateScheduleCommand(
                            id,
                            body.Name,
                            body.Frequency,
                            body.HourUtc,
                            body.DayOfWeek,
                            body.DayOfMonth,
                            body.Recipients,
                            body.IsActive),
                        context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminUpdateReportSchedule")
            .WithSummary("Changes the timetable and the recipients. The report it runs is not editable.")
            .RequirePermission(ReportingPermissions.ScheduleManage)
            .Produces<ReportScheduleResponse>();

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteScheduleCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminDeleteReportSchedule")
            .WithSummary("Removes a standing instruction. The reports it produced stay.")
            .RequirePermission(ReportingPermissions.ScheduleManage)
            .Produces(StatusCodes.Status204NoContent);
    }
}
