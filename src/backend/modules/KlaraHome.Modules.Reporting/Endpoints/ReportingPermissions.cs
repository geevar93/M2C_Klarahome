namespace KlaraHome.Modules.Reporting.Endpoints;

/// <summary>
/// The permissions this module's endpoints declare.
/// </summary>
/// <remarks>
/// <para>
/// Declared here and mirrored in the Identity module's permission catalogue, which is what the admin
/// UI lists and what a role grants. The duplication is deliberate and is what the module boundary
/// costs: a module may not reference another, so the two lists are kept in step by a test that
/// asserts every permission an endpoint asks for appears in the catalogue.
/// </para>
/// <para>
/// Two, and the split is between reading a number and putting it in somebody's inbox every week.
/// That is a wider gap than it looks: reading a report is a thing a manager does at a screen they are
/// already signed in to, and a schedule is a standing instruction to send the store's takings to a
/// list of addresses that nobody re-checks.
/// </para>
/// <para>
/// A seller holds the read permission and is confined to their own figures by the vendor query
/// filter and by the report declaration — the reports that are about the platform rather than about
/// a seller are marked not vendor-scoped and a seller is refused them outright, because a conversion
/// funnel is the store's business and not a supplier's.
/// </para>
/// </remarks>
internal static class ReportingPermissions
{
    /// <summary>
    /// Run a report and download an export.
    /// </summary>
    /// <remarks>
    /// Held by the platform's managers and by sellers. Which figures the holder actually sees is
    /// decided by whether their token carries a vendor id, not by a second permission — a permission
    /// per audience would be two grants to keep in step and one of them would eventually be wrong.
    /// </remarks>
    public const string ReportRead = "reporting.report.read";

    /// <summary>
    /// Create, edit and delete scheduled exports.
    /// </summary>
    /// <remarks>
    /// Deliberately not a seller's. A schedule sends commercial data to addresses on a timetable, and
    /// the list of who is on it is a decision for whoever runs the platform — a seller who wanted a
    /// weekly statement of their own has one through the settlements surface, which sends to the
    /// contact on their own account.
    /// </remarks>
    public const string ScheduleManage = "reporting.schedule.manage";
}
