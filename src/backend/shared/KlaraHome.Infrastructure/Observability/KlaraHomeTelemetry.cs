using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace KlaraHome.Infrastructure.Observability;

/// <summary>
/// The single <see cref="ActivitySource"/> and <see cref="Meter"/> for first-party
/// instrumentation. Modules add spans and instruments here rather than creating their own, so
/// the collector configuration never has to change.
/// </summary>
public static class KlaraHomeTelemetry
{
    /// <summary>Name registered with OpenTelemetry for both traces and metrics.</summary>
    public const string Name = "KlaraHome";

    /// <summary>Informational version of the running build, stamped on telemetry and logs.</summary>
    public static readonly string Version =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    public static readonly ActivitySource ActivitySource = new(Name, Version);

    public static readonly Meter Meter = new(Name, Version);
}
