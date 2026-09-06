using FluentValidation;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.Modules.Reporting.Domain;
using KlaraHome.Modules.Reporting.Infrastructure;
using KlaraHome.Modules.Reporting.Infrastructure.Export;
using KlaraHome.Modules.Reporting.Infrastructure.Persistence;
using KlaraHome.Modules.Reporting.Infrastructure.Query;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Reporting.Application.Reports;

/// <summary>Lists the reports this platform declares.</summary>
/// <param name="VendorScopedOnly">Only the ones a seller may run.</param>
internal sealed record ListReportsQuery(bool VendorScopedOnly) : IQuery<IReadOnlyList<ReportDefinition>>;

/// <summary>Runs one report.</summary>
/// <param name="Key">Which report.</param>
/// <param name="From">The start of the period, inclusive. Null for thirty days ago.</param>
/// <param name="To">The end of the period, exclusive. Null for tomorrow.</param>
/// <param name="GroupBy">Which grouping, where the report offers a choice.</param>
/// <param name="VendorId">
/// The seller to confine the figures to. Forced to the caller's own where they have one.
/// </param>
internal sealed record RunReportQuery(
    string? Key,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? GroupBy,
    Guid? VendorId) : IQuery<ReportResult>;

/// <summary>Produces one report as a file and records the run.</summary>
/// <param name="Key">Which report.</param>
/// <param name="From">The start of the period, inclusive.</param>
/// <param name="To">The end, exclusive.</param>
/// <param name="GroupBy">Which grouping.</param>
/// <param name="VendorId">The seller to confine the figures to.</param>
internal sealed record ExportReportCommand(
    string? Key,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? GroupBy,
    Guid? VendorId) : ICommand<ReportRunResponse>;

/// <summary>Lists what has been produced.</summary>
/// <param name="ReportKey">Only runs of this report.</param>
/// <param name="Status">Running, Completed or Failed.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListReportRunsQuery(string? ReportKey, string? Status, string? Cursor, int? Size)
    : IQuery<PagedResult<ReportRunResponse>>;

/// <summary>Mints a short-lived link to a produced report.</summary>
/// <param name="Id">The run.</param>
internal sealed record GetReportDownloadQuery(Guid Id) : IQuery<ReportDownloadResponse>;

/// <summary>Rules an export has to satisfy.</summary>
internal sealed class ExportReportCommandValidator : AbstractValidator<ExportReportCommand>
{
    public ExportReportCommandValidator() => RuleFor(command => command.Key).NotEmpty();
}

/// <summary>Lists the declared reports.</summary>
/// <remarks>
/// Served over the API rather than hard-coded in the admin app, which is the arrangement the CMS
/// block schemas use at Step 20 and for the same reason: the report picker, the column headings and
/// the CSV all come off one declaration, so adding a report is a deploy of the backend rather than
/// of both.
/// </remarks>
internal sealed class ListReportsQueryHandler : IQueryHandler<ListReportsQuery, IReadOnlyList<ReportDefinition>>
{
    public Task<Result<IReadOnlyList<ReportDefinition>>> HandleAsync(
        ListReportsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<ReportDefinition> reports = query.VendorScopedOnly
            ? [.. ReportCatalog.All.Where(report => report.IsVendorScoped)]
            : ReportCatalog.All;

        return Task.FromResult(Result.Success(reports));
    }
}

/// <summary>
/// Runs one report and returns its table.
/// </summary>
/// <remarks>
/// <para>
/// The period is resolved before anything else, and the defaults are chosen so that a caller who
/// asks for a report with no parameters gets something useful rather than an error: the last thirty
/// days, up to and including today. The upper bound is exclusive and defaults to tomorrow, which is
/// what makes "today" appear in a report run at lunchtime.
/// </para>
/// <para>
/// A seller's own vendor id wins over anything the query string asked for, and a report the
/// catalogue declares as not vendor-scoped is refused to them outright. Both are needed: the first
/// stops a seller reading another's figures through a parameter, and the second stops them reading
/// the platform's own — a conversion funnel confined to one seller would be a meaningless number
/// rather than a forbidden one, so refusing is the honest answer.
/// </para>
/// </remarks>
/// <param name="engine">Runs the declared report against the facts.</param>
/// <param name="scope">Who is asking, and which seller they are confined to.</param>
/// <param name="options">The period ceiling.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class RunReportQueryHandler(
    ReportQueryEngine engine,
    ReportingScope scope,
    IOptionsMonitor<ReportingOptions> options,
    IClock clock) : IQueryHandler<RunReportQuery, ReportResult>
{
    public async Task<Result<ReportResult>> HandleAsync(RunReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var resolved = ReportRequests.Resolve(
            query.Key,
            query.From,
            query.To,
            query.GroupBy,
            query.VendorId,
            scope,
            options.CurrentValue,
            clock.UtcNow);

        if (resolved.IsFailure)
        {
            return Result.Failure<ReportResult>(resolved.Error);
        }

        var (definition, request) = resolved.Value;

        return Result.Success(await engine.RunAsync(definition, request, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Produces one report as a file.</summary>
/// <remarks>
/// The same code the scheduler runs, so a report somebody clicked for and one that arrives by email
/// every Monday cannot differ. Storage being unavailable is refused up front rather than producing a
/// run row that is bound to fail — the caller is told to look at the configuration.
/// </remarks>
/// <param name="exporter">Produces, stores and records the run.</param>
/// <param name="storage">Checked for availability before a run is opened.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="options">The period ceiling.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ExportReportCommandHandler(
    ReportExporter exporter,
    IFileStorage storage,
    ReportingScope scope,
    IOptionsMonitor<ReportingOptions> options,
    IClock clock) : ICommandHandler<ExportReportCommand, ReportRunResponse>
{
    public async Task<Result<ReportRunResponse>> HandleAsync(
        ExportReportCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!storage.IsAvailable)
        {
            return Result.Failure<ReportRunResponse>(ReportErrors.StorageUnavailable);
        }

        var resolved = ReportRequests.Resolve(
            command.Key,
            command.From,
            command.To,
            command.GroupBy,
            command.VendorId,
            scope,
            options.CurrentValue,
            clock.UtcNow);

        if (resolved.IsFailure)
        {
            return Result.Failure<ReportRunResponse>(resolved.Error);
        }

        var (definition, request) = resolved.Value;

        var run = await exporter
            .ProduceAsync(definition, request, schedule: null, scope.ActorId, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(ReportingProjection.ToResponse(run));
    }
}

/// <summary>Lists what has been produced, newest first.</summary>
/// <param name="context">The Reporting data context.</param>
internal sealed class ListReportRunsQueryHandler(ReportingDbContext context)
    : IQueryHandler<ListReportRunsQuery, PagedResult<ReportRunResponse>>
{
    public async Task<Result<PagedResult<ReportRunResponse>>> HandleAsync(
        ListReportRunsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.Runs.AsNoTracking().AsQueryable();

        if (query.ReportKey is { Length: > 0 } key)
        {
            rows = rows.Where(run => run.ReportKey == key);
        }

        if (Enum.TryParse<ReportRunStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(run => run.Status == status);
        }

        if (Cursor.TryDecode(query.Cursor, out var cursor) && Guid.TryParse(cursor, out var after))
        {
            rows = rows.Where(run => run.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(run => run.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(ReportingProjection.ToResponse).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<ReportRunResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Mints a short-lived link to a produced report.</summary>
/// <param name="context">The Reporting data context.</param>
/// <param name="exporter">Mints the link.</param>
internal sealed class GetReportDownloadQueryHandler(ReportingDbContext context, ReportExporter exporter)
    : IQueryHandler<GetReportDownloadQuery, ReportDownloadResponse>
{
    public async Task<Result<ReportDownloadResponse>> HandleAsync(
        GetReportDownloadQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var run = await context.Runs
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == query.Id, cancellationToken)
            .ConfigureAwait(false);

        if (run is null)
        {
            return Result.Failure<ReportDownloadResponse>(ReportErrors.NotFound("report run"));
        }

        if (run.Status != ReportRunStatus.Completed || run.StorageKey is null)
        {
            return Result.Failure<ReportDownloadResponse>(ReportErrors.NothingToDownload);
        }

        var link = exporter.LinkTo(run);

        return link is null
            ? Result.Failure<ReportDownloadResponse>(ReportErrors.StorageUnavailable)
            : Result.Success(link);
    }
}

/// <summary>
/// Turns a caller's parameters into a report and a period, or into the reason it cannot.
/// </summary>
/// <remarks>
/// Shared by the read and the export so the two cannot disagree about what a period means, which
/// grouping is valid, or whether a seller may run something. Three callers would have been three
/// chances for a vendor-scoping check to be forgotten in one of them.
/// </remarks>
internal static class ReportRequests
{
    /// <summary>How far back a report goes when the caller does not say.</summary>
    private const int DefaultWindowDays = 30;

    /// <summary>Resolves the report, the period and the seller.</summary>
    /// <param name="key">Which report.</param>
    /// <param name="from">The start of the period, or null for the default window.</param>
    /// <param name="to">The end, or null for tomorrow.</param>
    /// <param name="groupBy">Which grouping, where the report offers a choice.</param>
    /// <param name="requestedVendorId">The seller the caller asked for.</param>
    /// <param name="scope">Who is asking.</param>
    /// <param name="options">The period ceiling.</param>
    /// <param name="now">The current instant.</param>
    public static Result<(ReportDefinition Definition, ReportRequest Request)> Resolve(
        string? key,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? groupBy,
        Guid? requestedVendorId,
        ReportingScope scope,
        ReportingOptions options,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(options);

        var definition = ReportCatalog.Find(key);

        if (definition is null)
        {
            return Result.Failure<(ReportDefinition, ReportRequest)>(ReportErrors.UnknownReport(key));
        }

        // A seller's own vendor wins over anything the query string asked for. A vendor id in a
        // request is a suggestion; the one on the token is the fact.
        var vendorId = scope.VendorId ?? requestedVendorId;

        if (scope.VendorId is not null && !definition.IsVendorScoped)
        {
            return Result.Failure<(ReportDefinition, ReportRequest)>(ReportErrors.NotAvailableToVendors);
        }

        // The upper bound is exclusive and defaults to tomorrow, which is what makes today's sales
        // appear in a report run at lunchtime.
        var end = to ?? new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);
        var start = from ?? end.AddDays(-DefaultWindowDays);

        if (end <= start)
        {
            return Result.Failure<(ReportDefinition, ReportRequest)>(ReportErrors.InvalidPeriod);
        }

        if ((end - start).TotalDays > options.MaxPeriodDays)
        {
            return Result.Failure<(ReportDefinition, ReportRequest)>(
                ReportErrors.PeriodTooLong(options.MaxPeriodDays));
        }

        // An unrecognised grouping falls back to the report's default rather than failing. It is a
        // presentation choice, and refusing a whole report because a stale bookmark carried an old
        // value would be the wrong trade.
        var grouping = definition.GroupBy.Count == 0
            ? null
            : definition.GroupBy.FirstOrDefault(option =>
                  string.Equals(option, groupBy, StringComparison.OrdinalIgnoreCase))
              ?? definition.GroupBy[0];

        return Result.Success((definition, new ReportRequest(start, end, grouping, vendorId)));
    }
}
