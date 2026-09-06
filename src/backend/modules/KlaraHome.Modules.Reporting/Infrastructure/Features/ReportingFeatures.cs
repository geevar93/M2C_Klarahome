using KlaraHome.Contracts.Platform;

namespace KlaraHome.Modules.Reporting.Infrastructure.Features;

/// <summary>
/// The feature flags the Reporting module owns.
/// </summary>
/// <remarks>
/// Three, and the shape of the set says what the module is. Reading a report is on, because a
/// business that cannot see its numbers is not running. Writing a scheduled export is off, because
/// it emails commercial data on a timetable and a deployment should have decided it wants that. And
/// the fact ingest has a flag of its own, which is the one worth reading twice: it is the switch that
/// stops this module consuming events at all, and turning it off leaves a gap in the facts that
/// nothing back-fills.
/// </remarks>
internal static class ReportingFeatures
{
    /// <summary>Gates every read of a report.</summary>
    /// <remarks>
    /// On. It exists for the afternoon a report is discovered to be wrong and somebody needs it off
    /// every screen while it is fixed, rather than for an ordinary deployment decision.
    /// </remarks>
    public const string Reports = "reporting.reports";

    /// <summary>
    /// Gates scheduled exports and their delivery.
    /// </summary>
    /// <remarks>
    /// Off. A scheduled export puts the store's takings into an email on a timetable, and the person
    /// who turns it on should be the person who has thought about who is on the list. Turning it off
    /// stops the worker without touching the schedules, so the arrangement survives being switched
    /// back on.
    /// </remarks>
    public const string ScheduledExports = "reporting.scheduled-exports";

    /// <summary>
    /// Gates writing the facts this module reports on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On, and it should stay on. Unlike almost every other flag in the platform this one is not
    /// safe to leave off: with it off the event handlers do nothing, no fact rows are written, and
    /// the period it was off is a hole in every report for ever — because the outbox will have moved
    /// on and there is no back-fill.
    /// </para>
    /// <para>
    /// It exists for exactly one situation, which is worth having a switch for: a deployment where
    /// the ingest is causing a problem under load and an operator needs the store to keep selling
    /// while somebody looks at it. Losing a day of reporting is recoverable; refusing orders is not.
    /// </para>
    /// </remarks>
    public const string FactIngest = "reporting.fact-ingest";

    /// <summary>Every flag this module declares, seeded on each deploy.</summary>
    public static IReadOnlyList<FeatureFlagDeclaration> All { get; } =
    [
        new(Reports, true, "Serve reports to the admin console."),
        new(ScheduledExports, false, "Produce scheduled report exports and email them."),
        new(FactIngest, true, "Record the facts reports are computed from. Off leaves a permanent gap."),
    ];
}

/// <summary>Publishes this module's flags to the seeder, like every other module.</summary>
internal sealed class ReportingFeatureFlagSource : IFeatureFlagSource
{
    /// <inheritdoc />
    public string Module => "Reporting";

    /// <inheritdoc />
    public IReadOnlyList<FeatureFlagDeclaration> Flags => ReportingFeatures.All;
}
