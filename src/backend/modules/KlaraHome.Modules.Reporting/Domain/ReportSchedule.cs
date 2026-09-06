using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Reporting.Domain;

/// <summary>How often a scheduled report runs.</summary>
/// <remarks>
/// Three cadences and no cron expression. A cron field on an admin screen is a support burden and a
/// way to schedule a report for 02:00 on the 31st of February; three named frequencies cover every
/// business rhythm a store actually has, and each one has an obvious next-run.
/// </remarks>
internal enum ReportFrequency
{
    /// <summary>Every day, covering the day before.</summary>
    Daily = 0,

    /// <summary>Once a week, covering the seven days before.</summary>
    Weekly = 1,

    /// <summary>Once a month, covering the calendar month before.</summary>
    Monthly = 2,
}

/// <summary>What a scheduled report is produced as.</summary>
/// <remarks>
/// CSV only, for now and deliberately. A finance team opens it in a spreadsheet, and every other
/// format on the shortlist is either a rendering problem (PDF) or a format nobody can open without
/// the right version of the right application (XLSX). The enum exists so adding one later is a case
/// rather than a schema change.
/// </remarks>
internal enum ExportFormat
{
    /// <summary>Comma-separated values, UTF-8 with a byte-order mark.</summary>
    Csv = 0,
}

/// <summary>Where a report run got to.</summary>
internal enum ReportRunStatus
{
    /// <summary>Claimed and running.</summary>
    Running = 0,

    /// <summary>Produced, stored and, where there were recipients, sent.</summary>
    Completed = 1,

    /// <summary>It did not work. The reason is on the row.</summary>
    Failed = 2,
}

/// <summary>
/// A standing instruction to produce one report on a timetable and send it to somebody
/// (docs/03-database-design.md §4.18).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NextRunAt"/> is a stored column rather than a computed one, and that is what makes the
/// worker's sweep an index seek on a filtered index that is almost always empty rather than a scan
/// evaluating a rule per row. It is advanced when a run completes, so a schedule that fails is
/// retried on the next pass rather than being skipped until tomorrow.
/// </para>
/// <para>
/// The recipients are addresses rather than user ids, and that is deliberate: a monthly sales
/// summary usually goes to a distribution list or to an accountant who has no account on the
/// platform. It is the one place in this module that holds a contact address, and the messages are
/// queued through Notifications like every other one so a recipient's suppression still applies.
/// </para>
/// <para>
/// A schedule with no recipients is legitimate and useful. The report is still produced and still
/// stored, and somebody downloads it from the admin console — which is how a store keeps a monthly
/// archive without emailing it to anybody.
/// </para>
/// </remarks>
internal sealed class ReportSchedule : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    /// <summary>The longest name a schedule may be given.</summary>
    public const int MaxNameLength = 120;

    /// <summary>The most addresses one schedule may send to.</summary>
    /// <remarks>
    /// Twenty. Beyond that it is a mailing list, and a mailing list belongs in the mail system rather
    /// than in a column that has to be edited by hand every time somebody leaves.
    /// </remarks>
    public const int MaxRecipients = 20;

    private ReportSchedule(Guid id, string reportKey, string name, ReportFrequency frequency)
        : base(id)
    {
        ReportKey = reportKey;
        Name = name;
        Frequency = frequency;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ReportSchedule()
    {
        ReportKey = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Which report, from the declared catalogue.</summary>
    public string ReportKey { get; private set; }

    /// <summary>What an operator calls this schedule.</summary>
    public string Name { get; private set; }

    /// <summary>How often it runs.</summary>
    public ReportFrequency Frequency { get; private set; }

    /// <summary>The hour of the day it runs at, in UTC.</summary>
    /// <remarks>
    /// UTC rather than the store's timezone, because that is what the worker compares against and a
    /// stored local hour would shift under a timezone change nobody made. The admin screen converts.
    /// </remarks>
    public int HourUtc { get; private set; }

    /// <summary>Which day of the week, for a weekly schedule. Monday is 1.</summary>
    public int? DayOfWeek { get; private set; }

    /// <summary>Which day of the month, for a monthly schedule.</summary>
    /// <remarks>
    /// Clamped to the length of the month when the next run is computed, so "the 31st" runs on the
    /// 28th in February rather than not running at all.
    /// </remarks>
    public int? DayOfMonth { get; private set; }

    /// <summary>Where to send it. Empty for a schedule that only files the report.</summary>
    public IReadOnlyList<string> Recipients { get; private set; } = [];

    /// <summary>What it is produced as.</summary>
    public ExportFormat Format { get; private set; } = ExportFormat.Csv;

    /// <summary>Whether it runs at all.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>When it last produced something, in UTC.</summary>
    public DateTimeOffset? LastRunAt { get; private set; }

    /// <summary>When it is next due, in UTC. The column the worker's sweep reads.</summary>
    public DateTimeOffset NextRunAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Opens a schedule.</summary>
    /// <param name="reportKey">Which report.</param>
    /// <param name="name">What to call it.</param>
    /// <param name="frequency">How often.</param>
    /// <param name="hourUtc">At what hour, in UTC.</param>
    /// <param name="dayOfWeek">Which weekday, for a weekly schedule.</param>
    /// <param name="dayOfMonth">Which day, for a monthly one.</param>
    /// <param name="recipients">Where to send it.</param>
    /// <param name="from">The instant the first run is computed from.</param>
    public static ReportSchedule Open(
        string reportKey,
        string name,
        ReportFrequency frequency,
        int hourUtc,
        int? dayOfWeek,
        int? dayOfMonth,
        IReadOnlyList<string>? recipients,
        DateTimeOffset from)
    {
        var schedule = new ReportSchedule(
            UuidV7.New(),
            Guard.NotNullOrWhiteSpace(reportKey).Trim(),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), MaxNameLength),
            frequency)
        {
            HourUtc = Math.Clamp(hourUtc, 0, 23),
            DayOfWeek = frequency == ReportFrequency.Weekly ? Math.Clamp(dayOfWeek ?? 1, 1, 7) : null,
            DayOfMonth = frequency == ReportFrequency.Monthly ? Math.Clamp(dayOfMonth ?? 1, 1, 31) : null,
            Recipients = Normalize(recipients),
        };

        schedule.NextRunAt = schedule.ComputeNextRun(from);

        return schedule;
    }

    /// <summary>Changes the timetable and who it goes to.</summary>
    /// <param name="name">What to call it.</param>
    /// <param name="frequency">How often.</param>
    /// <param name="hourUtc">At what hour, in UTC.</param>
    /// <param name="dayOfWeek">Which weekday.</param>
    /// <param name="dayOfMonth">Which day of the month.</param>
    /// <param name="recipients">Where to send it.</param>
    /// <param name="isActive">Whether it runs.</param>
    /// <param name="from">The instant the next run is recomputed from.</param>
    public void Update(
        string name,
        ReportFrequency frequency,
        int hourUtc,
        int? dayOfWeek,
        int? dayOfMonth,
        IReadOnlyList<string>? recipients,
        bool isActive,
        DateTimeOffset from)
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), MaxNameLength);
        Frequency = frequency;
        HourUtc = Math.Clamp(hourUtc, 0, 23);
        DayOfWeek = frequency == ReportFrequency.Weekly ? Math.Clamp(dayOfWeek ?? 1, 1, 7) : null;
        DayOfMonth = frequency == ReportFrequency.Monthly ? Math.Clamp(dayOfMonth ?? 1, 1, 31) : null;
        Recipients = Normalize(recipients);
        IsActive = isActive;

        // Recomputed on every edit. A schedule moved from 02:00 to 14:00 that kept yesterday's
        // next-run would fire at the old time once more, which is the kind of thing that makes an
        // operator stop trusting the feature.
        NextRunAt = ComputeNextRun(from);
    }

    /// <summary>Records that a run finished and moves the schedule on.</summary>
    /// <param name="at">When the run finished.</param>
    public void RecordRun(DateTimeOffset at)
    {
        LastRunAt = at;
        NextRunAt = ComputeNextRun(at);
    }

    /// <summary>
    /// The period a run starting now should cover.
    /// </summary>
    /// <remarks>
    /// Half-open, <c>[start, end)</c>, and always ending at the start of the current period rather
    /// than at "now". A daily report run at 02:00 covers the whole of yesterday, not the twenty-six
    /// hours since the last run — a period whose length depends on when the worker happened to wake
    /// is a period whose numbers cannot be compared with last week's.
    /// </remarks>
    /// <param name="at">When the run is happening.</param>
    public (DateTimeOffset Start, DateTimeOffset End) PeriodFor(DateTimeOffset at)
    {
        var midnight = new DateTimeOffset(at.UtcDateTime.Date, TimeSpan.Zero);

        return Frequency switch
        {
            ReportFrequency.Daily => (midnight.AddDays(-1), midnight),
            ReportFrequency.Weekly => (midnight.AddDays(-7), midnight),
            _ => MonthBefore(midnight),
        };
    }

    /// <summary>The calendar month before the one <paramref name="midnight"/> falls in.</summary>
    private static (DateTimeOffset Start, DateTimeOffset End) MonthBefore(DateTimeOffset midnight)
    {
        var firstOfThisMonth = new DateTimeOffset(midnight.Year, midnight.Month, 1, 0, 0, 0, TimeSpan.Zero);

        return (firstOfThisMonth.AddMonths(-1), firstOfThisMonth);
    }

    /// <summary>
    /// When this schedule is next due, strictly after <paramref name="from"/>.
    /// </summary>
    /// <remarks>
    /// Strictly after, so recording a run cannot produce a next-run equal to the run that just
    /// happened — which the worker would pick straight back up and loop on.
    /// </remarks>
    /// <param name="from">The instant to compute forward from.</param>
    private DateTimeOffset ComputeNextRun(DateTimeOffset from)
    {
        var day = from.UtcDateTime.Date;
        var candidate = new DateTimeOffset(day.AddHours(HourUtc), TimeSpan.Zero);

        return Frequency switch
        {
            ReportFrequency.Daily => candidate > from ? candidate : candidate.AddDays(1),
            ReportFrequency.Weekly => NextWeekly(candidate, from),
            _ => NextMonthly(candidate, from),
        };
    }

    /// <summary>The next occurrence of the configured weekday at the configured hour.</summary>
    private DateTimeOffset NextWeekly(DateTimeOffset candidate, DateTimeOffset from)
    {
        // ISO weekdays: Monday is 1 and Sunday is 7, which is what an operator picks from and what
        // .NET's Sunday-is-zero enum is not.
        var wanted = DayOfWeek ?? 1;
        var current = (int)candidate.UtcDateTime.DayOfWeek;
        var isoCurrent = current == 0 ? 7 : current;

        var offset = (wanted - isoCurrent + 7) % 7;
        var next = candidate.AddDays(offset);

        return next > from ? next : next.AddDays(7);
    }

    /// <summary>The next occurrence of the configured day of the month, clamped to its length.</summary>
    private DateTimeOffset NextMonthly(DateTimeOffset candidate, DateTimeOffset from)
    {
        var wanted = DayOfMonth ?? 1;

        for (var month = 0; month < 2; month++)
        {
            var reference = candidate.AddMonths(month);
            var length = DateTime.DaysInMonth(reference.Year, reference.Month);

            // Clamped, so "the 31st" runs on the 28th in February rather than not running at all.
            var next = new DateTimeOffset(
                new DateTime(reference.Year, reference.Month, Math.Min(wanted, length), 0, 0, 0, DateTimeKind.Utc)
                    .AddHours(HourUtc),
                TimeSpan.Zero);

            if (next > from)
            {
                return next;
            }
        }

        return candidate.AddMonths(1);
    }

    /// <summary>Trims, lower-cases and de-duplicates the addresses, and caps how many there may be.</summary>
    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? recipients)
        => recipients is null
            ? []
            :
            [
                .. recipients
                    .Where(address => !string.IsNullOrWhiteSpace(address))
                    .Select(address => address.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .Take(MaxRecipients),
            ];
}

/// <summary>
/// One production of one report (docs/03-database-design.md §4.18).
/// </summary>
/// <remarks>
/// <para>
/// Written by the scheduled worker and by an operator asking for an export by hand, which is why
/// <see cref="ScheduleId"/> is nullable. Both produce the same artefact through the same code, so a
/// scheduled monthly summary and one somebody asked for cannot disagree.
/// </para>
/// <para>
/// <see cref="StorageKey"/> is an object-storage key in the private bucket, not a media-library file
/// id. That is a deliberate departure from how every other generated document on this platform is
/// stored: the media library identifies uploads by sniffing their magic number, and CSV has none —
/// teaching it to accept a format it cannot recognise would weaken the control that stops the store
/// serving malware from its own domain. A report is served instead by a short-lived signed URL this
/// module mints on request.
/// </para>
/// </remarks>
internal sealed class ReportRun : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    /// <summary>The longest error this row will keep.</summary>
    public const int MaxErrorLength = 1_000;

    private ReportRun(Guid id, string reportKey, DateTimeOffset periodStart, DateTimeOffset periodEnd)
        : base(id)
    {
        ReportKey = reportKey;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ReportRun() => ReportKey = string.Empty;

    /// <summary>The schedule that asked for it, or null when a person did.</summary>
    public Guid? ScheduleId { get; private set; }

    /// <summary>Which report.</summary>
    public string ReportKey { get; private set; }

    /// <summary>The start of the period it covers, inclusive.</summary>
    public DateTimeOffset PeriodStart { get; private set; }

    /// <summary>The end of the period, exclusive.</summary>
    public DateTimeOffset PeriodEnd { get; private set; }

    /// <summary>Where it got to.</summary>
    public ReportRunStatus Status { get; private set; } = ReportRunStatus.Running;

    /// <summary>What it was produced as.</summary>
    public ExportFormat Format { get; private set; } = ExportFormat.Csv;

    /// <summary>How many rows it holds. Null until it finishes.</summary>
    public int? RowCount { get; private set; }

    /// <summary>The object key in the private bucket. Null until it finishes.</summary>
    public string? StorageKey { get; private set; }

    /// <summary>How large the file is, in bytes.</summary>
    public long? ByteSize { get; private set; }

    /// <summary>Why it failed, when it did.</summary>
    public string? Error { get; private set; }

    /// <summary>Who asked for it, when a person did.</summary>
    public Guid? RequestedBy { get; private set; }

    /// <summary>When it started, in UTC.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>When it finished, in UTC.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Opens a run.</summary>
    /// <param name="reportKey">Which report.</param>
    /// <param name="periodStart">The start of the period, inclusive.</param>
    /// <param name="periodEnd">The end, exclusive.</param>
    /// <param name="format">What to produce.</param>
    /// <param name="scheduleId">The schedule, when one asked.</param>
    /// <param name="requestedBy">The person, when one did.</param>
    /// <param name="startedAt">When.</param>
    public static ReportRun Start(
        string reportKey,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        ExportFormat format,
        Guid? scheduleId,
        Guid? requestedBy,
        DateTimeOffset startedAt)
        => new(UuidV7.NewAt(startedAt), Guard.NotNullOrWhiteSpace(reportKey).Trim(), periodStart, periodEnd)
        {
            Format = format,
            ScheduleId = scheduleId,
            RequestedBy = requestedBy,
            StartedAt = startedAt,
        };

    /// <summary>Records that it worked.</summary>
    /// <param name="rowCount">How many rows.</param>
    /// <param name="storageKey">Where the file is.</param>
    /// <param name="byteSize">How large it is.</param>
    /// <param name="completedAt">When.</param>
    public void Complete(int rowCount, string storageKey, long byteSize, DateTimeOffset completedAt)
    {
        Status = ReportRunStatus.Completed;
        RowCount = Math.Max(0, rowCount);
        StorageKey = Guard.NotNullOrWhiteSpace(storageKey);
        ByteSize = byteSize;
        CompletedAt = completedAt;
        Error = null;
    }

    /// <summary>Records that it did not.</summary>
    /// <param name="error">Why.</param>
    /// <param name="completedAt">When.</param>
    public void Fail(string error, DateTimeOffset completedAt)
    {
        Status = ReportRunStatus.Failed;
        CompletedAt = completedAt;

        var message = error?.Trim() ?? "The report could not be produced.";
        Error = message.Length <= MaxErrorLength ? message : message[..MaxErrorLength];
    }
}
