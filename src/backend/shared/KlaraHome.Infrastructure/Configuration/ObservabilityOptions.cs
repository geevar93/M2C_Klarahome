using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Infrastructure.Configuration;

/// <summary>Logging, tracing and metrics settings (docs/06-infrastructure-devops.md §4.1).</summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>Resource name reported to OpenTelemetry and stamped on every log line.</summary>
    [Required]
    [MinLength(3)]
    public string ServiceName { get; set; } = "klarahome-api";

    /// <summary>
    /// OTLP collector endpoint. Empty in local development, where traces and metrics are simply
    /// not exported; the collector arrives with the VPS stack at Step 31.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>Serilog minimum level for our own namespaces. Toggleable without a redeploy.</summary>
    [Required]
    public string MinimumLevel { get; set; } = "Information";

    /// <summary>Fraction of requests sampled for tracing. 1.0 until volume justifies less.</summary>
    [Range(0.0, 1.0)]
    public double TraceSampleRatio { get; set; } = 1.0;

    /// <summary>
    /// Console output format. Null means "JSON everywhere except Development", which is what
    /// production log shipping needs and what a developer reading a terminal does not.
    /// </summary>
    public bool? ConsoleJson { get; set; }

    /// <summary>True when an OTLP exporter should be wired up at all.</summary>
    public bool ExportsTelemetry => !string.IsNullOrWhiteSpace(OtlpEndpoint);
}
