using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Reporting.Application;
using KlaraHome.Modules.Reporting.Infrastructure.Export;
using KlaraHome.Modules.Reporting.Infrastructure.Features;
using KlaraHome.Modules.Reporting.Infrastructure.Persistence;
using KlaraHome.Modules.Reporting.Infrastructure.Query;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Reporting.Infrastructure.Jobs;

/// <summary>
/// Produces the reports that are due, and sends them.
/// </summary>
/// <remarks>
/// <para>
/// The sweep reads one filtered index — active schedules whose next run has passed — which is almost
/// always empty, so the ordinary cost of running this every five minutes is an index seek. That is
/// the whole reason <c>next_run_at</c> is a stored column rather than a rule evaluated per row.
/// </para>
/// <para>
/// A schedule is moved on only when its run <em>completes</em>, and a run that fails still counts as
/// complete for that purpose. The alternative — leaving the next-run in the past on failure — turns a
/// report that fails for a structural reason into a job that retries every five minutes for ever and
/// fills the run log with the same error. The failure is on the run row where an operator can see
/// it, and the next scheduled occurrence tries again.
/// </para>
/// <para>
/// Bounded per pass, because a monthly cadence means every schedule in the store can come due within
/// the same minute on the first of the month. Whatever the batch does not take is taken five minutes
/// later.
/// </para>
/// </remarks>
/// <param name="services">The root provider; a scope is taken per pass.</param>
/// <param name="options">The interval and the batch size.</param>
/// <param name="logger">Reports what was produced.</param>
internal sealed partial class ReportScheduleWorker(
    IServiceProvider services,
    IOptionsMonitor<ReportingOptions> options,
    ILogger<ReportScheduleWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.SchedulerEnabled)
        {
            return;
        }

        WorkerStarted(logger, options.CurrentValue.SchedulerIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.CurrentValue;

            try
            {
                await RunDueAsync(settings, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                SweepFailed(logger, exception);
            }

            await Task.Delay(TimeSpan.FromMinutes(settings.SchedulerIntervalMinutes), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Produces the batch of reports whose time has come.</summary>
    private async Task RunDueAsync(ReportingOptions settings, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();

        var flags = scope.ServiceProvider.GetRequiredService<IFeatureFlags>();

        if (!await flags
                .IsEnabledAsync(ReportingFeatures.ScheduledExports, cancellationToken: cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var context = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var exporter = scope.ServiceProvider.GetRequiredService<ReportExporter>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var now = clock.UtcNow;

        var due = await context.Schedules
            .Where(schedule => schedule.IsActive && schedule.NextRunAt <= now)
            .OrderBy(schedule => schedule.NextRunAt)
            .Take(settings.SchedulerBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (due.Count == 0)
        {
            return;
        }

        foreach (var schedule in due)
        {
            var definition = ReportCatalog.Find(schedule.ReportKey);

            if (definition is null)
            {
                // A schedule naming a report that no longer exists. Moved on rather than left due, so
                // it does not occupy the batch on every pass; an operator sees it in the list with a
                // report key nothing matches.
                UnknownReport(logger, schedule.ReportKey, schedule.Id);
                schedule.RecordRun(now);
                continue;
            }

            var (start, end) = schedule.PeriodFor(now);

            await exporter
                .ProduceAsync(
                    definition,
                    new ReportRequest(
                        start,
                        end,
                        definition.GroupBy.Count > 0 ? definition.GroupBy[0] : null,
                        VendorId: null),
                    schedule,
                    requestedBy: null,
                    cancellationToken)
                .ConfigureAwait(false);

            // Moved on whether the run worked or not. A report that fails for a structural reason
            // must not become a job that retries every five minutes for ever.
            schedule.RecordRun(clock.UtcNow);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        SweepCompleted(logger, due.Count);
    }

    [LoggerMessage(
        EventId = 8240,
        Level = LogLevel.Information,
        Message = "Report scheduler started; running every {IntervalMinutes} minute(s)")]
    private static partial void WorkerStarted(ILogger logger, int intervalMinutes);

    [LoggerMessage(
        EventId = 8241,
        Level = LogLevel.Information,
        Message = "Report scheduler produced {Count} report(s)")]
    private static partial void SweepCompleted(ILogger logger, int count);

    [LoggerMessage(
        EventId = 8242,
        Level = LogLevel.Error,
        Message = "Report scheduler sweep failed")]
    private static partial void SweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 8243,
        Level = LogLevel.Warning,
        Message = "Schedule {ScheduleId} names report {ReportKey}, which this platform does not declare")]
    private static partial void UnknownReport(ILogger logger, string reportKey, Guid scheduleId);
}
