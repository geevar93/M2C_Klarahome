namespace KlaraHome.Modules.Reporting.Application;

/// <summary>
/// A produced report: what it is, what it covers, and its rows.
/// </summary>
/// <remarks>
/// The rows are dictionaries keyed on the column keys rather than a typed object per report,
/// deliberately. Thirteen report shapes would otherwise be thirteen response types the API client
/// generator has to emit and the admin app has to switch on; a declared column list plus untyped
/// rows lets one table component render all of them, and the CSV writer walk the columns in order
/// without knowing which report it is writing.
/// </remarks>
/// <param name="Key">Which report.</param>
/// <param name="Name">What a person calls it.</param>
/// <param name="Columns">Its columns, in the order they are rendered.</param>
/// <param name="From">The start of the period, inclusive.</param>
/// <param name="To">The end of the period, exclusive.</param>
/// <param name="GroupBy">Which grouping was applied, where the report offers a choice.</param>
/// <param name="CurrencyCode">What the money columns are in.</param>
/// <param name="Rows">The results, in the report's own order.</param>
/// <param name="Totals">
/// The column totals, where totalling is meaningful. Null for a report whose every measure is a
/// rate — a column of percentages does not have a sum, and printing one is worse than printing
/// nothing.
/// </param>
/// <param name="Truncated">
/// Whether the result hit the row ceiling. The admin app says so rather than letting somebody read
/// a partial table as a complete one.
/// </param>
internal sealed record ReportResult(
    string Key,
    string Name,
    IReadOnlyList<ReportColumn> Columns,
    DateTimeOffset From,
    DateTimeOffset To,
    string? GroupBy,
    string CurrencyCode,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyDictionary<string, object?>? Totals,
    bool Truncated);

/// <summary>A scheduled report, as the admin app lists it.</summary>
/// <param name="Id">The schedule.</param>
/// <param name="ReportKey">Which report.</param>
/// <param name="ReportName">Its name, so the list needs no second lookup.</param>
/// <param name="Name">What an operator calls this schedule.</param>
/// <param name="Frequency">Daily, Weekly or Monthly.</param>
/// <param name="HourUtc">The hour it runs at, in UTC.</param>
/// <param name="DayOfWeek">Which weekday, for a weekly schedule. Monday is 1.</param>
/// <param name="DayOfMonth">Which day, for a monthly one.</param>
/// <param name="Recipients">Where it is sent.</param>
/// <param name="Format">What it is produced as.</param>
/// <param name="IsActive">Whether it runs.</param>
/// <param name="LastRunAt">When it last produced something.</param>
/// <param name="NextRunAt">When it is next due.</param>
internal sealed record ReportScheduleResponse(
    Guid Id,
    string ReportKey,
    string? ReportName,
    string Name,
    string Frequency,
    int HourUtc,
    int? DayOfWeek,
    int? DayOfMonth,
    IReadOnlyList<string> Recipients,
    string Format,
    bool IsActive,
    DateTimeOffset? LastRunAt,
    DateTimeOffset NextRunAt);

/// <summary>One production of one report.</summary>
/// <param name="Id">The run.</param>
/// <param name="ScheduleId">The schedule that asked, or null when a person did.</param>
/// <param name="ReportKey">Which report.</param>
/// <param name="PeriodStart">The start of the period it covers, inclusive.</param>
/// <param name="PeriodEnd">The end, exclusive.</param>
/// <param name="Status">Running, Completed or Failed.</param>
/// <param name="Format">What it was produced as.</param>
/// <param name="RowCount">How many rows it holds.</param>
/// <param name="ByteSize">How large the file is.</param>
/// <param name="Error">Why it failed, when it did.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="CompletedAt">When it finished.</param>
internal sealed record ReportRunResponse(
    Guid Id,
    Guid? ScheduleId,
    string ReportKey,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string Status,
    string Format,
    int? RowCount,
    long? ByteSize,
    string? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Where to fetch a produced report from, and for how long.</summary>
/// <remarks>
/// A short-lived signed URL rather than a permanent one, and it is minted per request rather than
/// stored. A report is a private document — it is the store's takings, or one seller's — and a
/// durable link to it would be a durable link somebody could paste into a chat thread.
/// </remarks>
/// <param name="Url">The signed URL.</param>
/// <param name="ExpiresAt">When it stops working.</param>
/// <param name="FileName">What the browser should call the download.</param>
internal sealed record ReportDownloadResponse(string Url, DateTimeOffset ExpiresAt, string FileName);
