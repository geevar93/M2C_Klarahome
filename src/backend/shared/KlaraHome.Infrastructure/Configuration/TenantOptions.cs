using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Infrastructure.Configuration;

/// <summary>
/// The tenant identity of this deployment, used today only to stamp logs and telemetry
/// (docs/09-nfr-testing-observability.md §3.1). The Platform module replaces this with
/// database-backed, admin-editable settings at Step 6; the variable names do not change.
/// </summary>
public sealed class TenantOptions
{
    public const string SectionName = "Tenant";

    /// <summary>Short code identifying the white-label deployment (ADR-006).</summary>
    [Required]
    [RegularExpression("^[a-z0-9][a-z0-9-]{1,30}$", ErrorMessage = "Tenant code must be lowercase kebab-case.")]
    public string Code { get; set; } = "klarahome";

    /// <summary>
    /// The tenant's primary key, written into <c>tenant_id</c> on every row. Set this explicitly
    /// on any deployment that holds data. Left unset it is derived deterministically from
    /// <see cref="Code"/>, which survives a restart but not a change of code — see
    /// <c>ConfiguredTenantContext</c>.
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>BCP-47 locale used when the caller expresses no preference.</summary>
    [Required]
    public string DefaultLocale { get; set; } = "en-IN";

    /// <summary>IANA timezone used to render stored UTC timestamps.</summary>
    [Required]
    public string DefaultTimeZone { get; set; } = "Asia/Kolkata";
}
