namespace KlaraHome.Modules.Platform.Infrastructure;

/// <summary>
/// The handful of values the Platform module reads from configuration rather than from its own
/// tables.
/// </summary>
/// <remarks>
/// Almost nothing belongs here. Anything a business could reasonably want to change belongs in
/// <c>platform.store_settings</c>, where it is typed, audited and editable without a deploy. This
/// is only for what the module needs before there is a settings table to read from — and for
/// pointing at data that is mounted rather than shipped.
/// </remarks>
internal sealed class PlatformOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Platform";

    /// <summary>
    /// Path to the India Post PIN code CSV, or null when the deployment has not imported one. A
    /// header row, then <c>pincode,city,district,stateCode,zone</c>.
    /// </summary>
    public string? PincodeDataPath { get; set; }
}
