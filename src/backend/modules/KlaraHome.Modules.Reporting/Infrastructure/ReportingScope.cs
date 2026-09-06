using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Modules.Reporting.Endpoints;

namespace KlaraHome.Modules.Reporting.Infrastructure;

/// <summary>
/// Answers "who is asking, and whose figures are they entitled to".
/// </summary>
/// <remarks>
/// <para>
/// One question and one answer, which is all a module with no business rules needs. The whole of
/// authorisation here is whether the caller carries a vendor id: with one they see their own
/// figures and only the reports that are about a seller, and without one they see the platform's.
/// </para>
/// <para>
/// <see cref="VendorId"/> is read from the caller's token and never from a request. A vendor id in a
/// query string would be a seller reading a competitor's takings, which is the single worst thing
/// this module could get wrong.
/// </para>
/// </remarks>
/// <param name="caller">The signed-in caller.</param>
internal sealed class ReportingScope(ICallerContext caller)
{
    /// <summary>The user to attribute an export to.</summary>
    public Guid? ActorId => caller.UserId;

    /// <summary>The seller the caller is confined to, or null for platform staff.</summary>
    public Guid? VendorId => caller.VendorId;

    /// <summary>Whether the caller may keep a standing instruction to email a report.</summary>
    public bool CanManageSchedules => caller.HasPermission(ReportingPermissions.ScheduleManage);
}
