using System.Globalization;
using KlaraHome.Contracts.Notifications;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.Modules.Reporting.Application;
using KlaraHome.Modules.Reporting.Domain;
using KlaraHome.Modules.Reporting.Infrastructure.Persistence;
using KlaraHome.Modules.Reporting.Infrastructure.Query;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Reporting.Infrastructure.Export;

/// <summary>
/// Produces a report, stores it, and tells whoever asked for it.
/// </summary>
/// <remarks>
/// <para>
/// One implementation shared by the scheduler and by an operator asking for an export by hand, so a
/// scheduled monthly summary and one somebody clicked for cannot disagree. The run row is written
/// before the work starts and closed afterwards, which is what makes a report that died halfway
/// visible as a failure rather than as nothing having happened.
/// </para>
/// <para>
/// The file goes into object storage under this module's own prefix rather than into the media
/// library, and that is a deliberate departure from how every other generated document on this
/// platform is stored. The media library identifies what it accepts by sniffing the magic number of
/// the bytes, and CSV has none — teaching it to accept a format it cannot recognise would weaken the
/// control that stops the store serving executable content from its own domain (ADR-021). A report
/// is served instead by a short-lived signed URL minted per request.
/// </para>
/// <para>
/// Delivery is best-effort and never fails the run. A produced report that could not be emailed is
/// still a produced report sitting in the admin console; marking the run failed because a mail
/// server was busy would tell an operator that the numbers are missing when they are not.
/// </para>
/// </remarks>
/// <param name="context">The Reporting data context.</param>
/// <param name="engine">Runs the declared report.</param>
/// <param name="storage">Where the produced file goes.</param>
/// <param name="notifier">Tells the recipients.</param>
/// <param name="options">The link lifetime and the row ceiling.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was produced and what could not be.</param>
internal sealed partial class ReportExporter(
    ReportingDbContext context,
    ReportQueryEngine engine,
    IFileStorage storage,
    INotifier notifier,
    IOptionsMonitor<ReportingOptions> options,
    IClock clock,
    ILogger<ReportExporter> logger)
{
    /// <summary>The object-storage prefix every export is written under.</summary>
    /// <remarks>
    /// Its own prefix rather than sharing the documents one, so a retention sweep or a bucket policy
    /// can treat exports differently from invoices — which it should, because an invoice is a
    /// statutory record and an export is a convenience that can always be produced again.
    /// </remarks>
    private const string StoragePrefix = "reporting/exports";

    /// <summary>
    /// Produces one report and records the run.
    /// </summary>
    /// <param name="definition">The declared report.</param>
    /// <param name="request">The period, the grouping and the seller.</param>
    /// <param name="schedule">The schedule that asked, when one did.</param>
    /// <param name="requestedBy">The person who asked, when one did.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ReportRun> ProduceAsync(
        ReportDefinition definition,
        ReportRequest request,
        ReportSchedule? schedule,
        Guid? requestedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(request);

        var startedAt = clock.UtcNow;

        var run = ReportRun.Start(
            definition.Key,
            request.From,
            request.To,
            schedule?.Format ?? ExportFormat.Csv,
            schedule?.Id,
            requestedBy,
            startedAt);

        context.Runs.Add(run);

        // Saved before the work, so a run that dies halfway is visible as one that was started rather
        // than as one that never happened.
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await engine.RunAsync(definition, request, cancellationToken).ConfigureAwait(false);
            var bytes = CsvWriter.Write(result);
            var key = KeyFor(definition, request, run.Id);

            using var content = new MemoryStream(bytes, writable: false);

            var stored = await storage
                .PutAsync(key, content, "text/csv; charset=utf-8", StorageVisibility.Private, cancellationToken)
                .ConfigureAwait(false);

            run.Complete(result.Rows.Count, stored.Key, stored.ByteSize, clock.UtcNow);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            ExportProduced(logger, definition.Key, result.Rows.Count, stored.ByteSize);

            if (schedule is { Recipients.Count: > 0 })
            {
                await DeliverAsync(schedule, definition, run, result, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Recorded rather than thrown. A scheduled report that failed has to leave a row an
            // operator can see, and the scheduler has to be able to move on to the next one.
            run.Fail(exception.Message, clock.UtcNow);

            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            ExportFailed(logger, definition.Key, exception);
        }

        return run;
    }

    /// <summary>Mints a short-lived link to a produced report.</summary>
    /// <remarks>
    /// Per request rather than stored. A report is a private document — the store's takings, or one
    /// seller's — and a durable link is one somebody pastes into a chat thread that outlives their
    /// employment.
    /// </remarks>
    /// <param name="run">The completed run.</param>
    public ReportDownloadResponse? LinkTo(ReportRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.StorageKey is not { Length: > 0 } key || !storage.IsAvailable)
        {
            return null;
        }

        var lifetime = TimeSpan.FromMinutes(options.CurrentValue.DownloadLinkMinutes);
        var fileName = FileNameFor(run);

        return new ReportDownloadResponse(
            storage.GetSignedUrl(key, StorageVisibility.Private, lifetime, fileName),
            clock.UtcNow.Add(lifetime),
            fileName);
    }

    /// <summary>Tells the schedule's recipients that their report is ready.</summary>
    /// <remarks>
    /// The link in the message is short-lived and the message says how long it lasts, which is the
    /// honest way to email a private document: the alternative is attaching the store's takings to an
    /// email that then lives in somebody's inbox for ever.
    /// </remarks>
    private async Task DeliverAsync(
        ReportSchedule schedule,
        ReportDefinition definition,
        ReportRun run,
        ReportResult result,
        CancellationToken cancellationToken)
    {
        var link = LinkTo(run);

        if (link is null)
        {
            NoStorage(logger, definition.Key);
            return;
        }

        var period = $"{result.From:yyyy-MM-dd} to {result.To.AddDays(-1):yyyy-MM-dd}";

        foreach (var recipient in schedule.Recipients)
        {
            await notifier
                .EnqueueAsync(
                    new NotificationRequest(
                        NotificationEvents.ReportReady,
                        new NotificationRecipient(Email: recipient),
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["reportName"] = definition.Name,
                            ["periodLabel"] = period,
                            ["rowCount"] = result.Rows.Count.ToString(CultureInfo.InvariantCulture),
                            ["downloadUrl"] = link.Url,
                            ["expiryHours"] = Math.Max(
                                    1,
                                    options.CurrentValue.DownloadLinkMinutes / 60)
                                .ToString(CultureInfo.InvariantCulture),
                        }),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The object key a produced report is stored under.
    /// </summary>
    /// <remarks>
    /// The run's own id is the last segment, which is what makes the key unique without a collision
    /// check; everything before it is there so that an operator listing the bucket can tell what a
    /// file is without opening it.
    /// </remarks>
    private static string KeyFor(ReportDefinition definition, ReportRequest request, Guid runId)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{StoragePrefix}/{definition.Key}/{request.From:yyyy}/{request.From:MM}/{runId:N}.csv");

    /// <summary>What the browser should call the download.</summary>
    private static string FileNameFor(ReportRun run)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{run.ReportKey}-{run.PeriodStart:yyyyMMdd}-{run.PeriodEnd:yyyyMMdd}.csv");

    [LoggerMessage(
        EventId = 8220,
        Level = LogLevel.Information,
        Message = "Produced report {ReportKey}: {RowCount} row(s), {ByteSize} byte(s)")]
    private static partial void ExportProduced(ILogger logger, string reportKey, int rowCount, long byteSize);

    [LoggerMessage(
        EventId = 8221,
        Level = LogLevel.Error,
        Message = "Report {ReportKey} could not be produced")]
    private static partial void ExportFailed(ILogger logger, string reportKey, Exception exception);

    [LoggerMessage(
        EventId = 8222,
        Level = LogLevel.Warning,
        Message = "Report {ReportKey} was produced but object storage is unavailable, so it was not delivered")]
    private static partial void NoStorage(ILogger logger, string reportKey);
}
