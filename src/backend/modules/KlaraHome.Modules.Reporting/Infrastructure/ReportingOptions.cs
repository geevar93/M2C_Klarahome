using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Reporting.Infrastructure;

/// <summary>
/// What the Reporting module reads from configuration.
/// </summary>
/// <remarks>
/// All of it is a deployment concern rather than a business one, which is how it should be: a report
/// is a question about what happened, and there is no policy to configure about that. What is here
/// is how hard the two background jobs may work, how much of a result one caller may ask for, and
/// how long a produced file is kept before it stops being a liability.
/// </remarks>
internal sealed class ReportingOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Reporting";

    /// <summary>The currency every money column is in.</summary>
    /// <remarks>
    /// One currency, because this platform is India-only in v1 (docs/01-architecture.md §1) and the
    /// fact tables store bare amounts rather than money values. It is a setting rather than a
    /// constant so a redeployment for another market changes a value instead of a schema.
    /// </remarks>
    [StringLength(3, MinimumLength = 3)]
    public string CurrencyCode { get; set; } = "INR";

    /// <summary>The most rows one report will return.</summary>
    /// <remarks>
    /// Five thousand: enough for a day-by-day report over three years or a SKU table nobody will read
    /// to the end of, and small enough that one request cannot hold a connection open building a
    /// table for a screen. A result that hits it says so, so a partial table is never read as a
    /// complete one.
    /// </remarks>
    [Range(100, 100_000)]
    public int MaxReportRows { get; set; } = 5_000;

    /// <summary>The longest period one report may cover, in days.</summary>
    /// <remarks>
    /// Three years and a bit. It is a ceiling on how much of the fact tables one query may scan, not
    /// a statement about retention — the rows stay, and a longer window is a question for the
    /// database rather than for the API.
    /// </remarks>
    [Range(1, 3_650)]
    public int MaxPeriodDays { get; set; } = 1_100;

    /// <summary>Whether this process takes the daily inventory snapshot. On in the worker.</summary>
    public bool SnapshotEnabled { get; set; }

    /// <summary>The hour of the day the snapshot is taken, in UTC.</summary>
    /// <remarks>
    /// Twenty past midnight UTC is a quarter to six in the morning in India, which is the quietest
    /// the store gets. The snapshot walks every stock line that holds anything, so it should not be
    /// competing with a sale.
    /// </remarks>
    [Range(0, 23)]
    public int SnapshotHourUtc { get; set; }

    /// <summary>How often the snapshot job checks whether it is due, in minutes.</summary>
    public int SnapshotCheckIntervalMinutes { get; set; } = 30;

    /// <summary>How many stock lines one page of the inventory walk reads.</summary>
    [Range(50, 500)]
    public int SnapshotPageSize { get; set; } = 250;

    /// <summary>The most stock lines one snapshot will record.</summary>
    /// <remarks>
    /// A ceiling rather than a promise, and the same shape as the collection sweep's at Step 20. It
    /// stops a catalogue that has grown past anybody's expectation from turning a nightly job into an
    /// all-night one.
    /// </remarks>
    [Range(1_000, 1_000_000)]
    public int MaxSnapshotLines { get; set; } = 100_000;

    /// <summary>Whether this process runs scheduled report exports. On in the worker.</summary>
    public bool SchedulerEnabled { get; set; }

    /// <summary>How often the scheduler looks for a report that is due, in minutes.</summary>
    /// <remarks>
    /// Five. Reports are scheduled to the hour, so a five-minute resolution is well inside what
    /// anybody notices, and the query behind it is an index seek on a filtered index that is almost
    /// always empty.
    /// </remarks>
    [Range(1, 120)]
    public int SchedulerIntervalMinutes { get; set; } = 5;

    /// <summary>The most reports one scheduler pass will produce.</summary>
    [Range(1, 50)]
    public int SchedulerBatchSize { get; set; } = 5;

    /// <summary>How long a signed download link lives, in minutes.</summary>
    /// <remarks>
    /// Fifteen. A report is a private document — the store's takings, or one seller's — and the link
    /// is minted per request rather than stored precisely so that it cannot outlive the conversation
    /// it was produced for.
    /// </remarks>
    [Range(1, 1_440)]
    public int DownloadLinkMinutes { get; set; } = 15;

    /// <summary>How long a produced file is kept, in days. Zero to keep them indefinitely.</summary>
    /// <remarks>
    /// Ninety by default. A stored export is a copy of commercial data sitting in object storage, and
    /// keeping every daily report for ever is a growing liability for no benefit — the report can
    /// always be produced again from the facts, which do not expire.
    /// </remarks>
    [Range(0, 3_650)]
    public int ExportRetentionDays { get; set; } = 90;
}
