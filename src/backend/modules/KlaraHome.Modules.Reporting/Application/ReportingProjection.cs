using KlaraHome.Modules.Reporting.Domain;

namespace KlaraHome.Modules.Reporting.Application;

/// <summary>
/// Turns this module's entities into the shapes the API returns.
/// </summary>
/// <remarks>
/// One place, so two endpoints cannot answer with two different spellings of the same schedule. The
/// report's own name is looked up from the catalogue rather than stored on the schedule, which is
/// what keeps a renamed report from leaving stale names scattered through the schedule list.
/// </remarks>
internal static class ReportingProjection
{
    /// <summary>A scheduled report.</summary>
    /// <param name="schedule">The schedule.</param>
    public static ReportScheduleResponse ToResponse(ReportSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return new ReportScheduleResponse(
            schedule.Id,
            schedule.ReportKey,
            ReportCatalog.Find(schedule.ReportKey)?.Name,
            schedule.Name,
            schedule.Frequency.ToString(),
            schedule.HourUtc,
            schedule.DayOfWeek,
            schedule.DayOfMonth,
            schedule.Recipients,
            schedule.Format.ToString(),
            schedule.IsActive,
            schedule.LastRunAt,
            schedule.NextRunAt);
    }

    /// <summary>One production of a report.</summary>
    /// <param name="run">The run.</param>
    public static ReportRunResponse ToResponse(ReportRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new ReportRunResponse(
            run.Id,
            run.ScheduleId,
            run.ReportKey,
            run.PeriodStart,
            run.PeriodEnd,
            run.Status.ToString(),
            run.Format.ToString(),
            run.RowCount,
            run.ByteSize,
            run.Error,
            run.StartedAt,
            run.CompletedAt);
    }
}
